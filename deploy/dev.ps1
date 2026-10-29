<#
.SYNOPSIS
    Starts, stops and inspects the local development dependencies.

.DESCRIPTION
    Wraps "docker compose" for deploy/compose.yaml. On the first "up" it creates deploy/.env from
    deploy/.env.example, replacing every "generate" value with a random password. "secrets" writes
    the services' connection settings from deploy/.env into their user secrets.

.EXAMPLE
    ./deploy/dev.ps1 up            # Postgres, RabbitMQ, Valkey, Aspire dashboard
    ./deploy/dev.ps1 up -Search    # ... plus Elasticsearch
    ./deploy/dev.ps1 secrets       # connection settings for running services from the IDE
    ./deploy/dev.ps1 status
    ./deploy/dev.ps1 down          # stop, keep data
    ./deploy/dev.ps1 reset -Force  # stop and DELETE all local data
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'status', 'logs', 'reset', 'secrets')]
    [string]$Command = 'status',

    # Also start Elasticsearch (compose profile "search").
    [switch]$Search,

    # Required by "reset", which deletes every volume.
    [switch]$Force,

    # For "logs": a single service, for example "postgres".
    [string]$Service
)

$ErrorActionPreference = 'Stop'

$deployDir = $PSScriptRoot
$composeFile = Join-Path $deployDir 'compose.yaml'
$envFile = Join-Path $deployDir '.env'
$envExample = Join-Path $deployDir '.env.example'

# Services whose projects use the user secrets id "netboxplus-<name>" and own a database "<name>".
$services = @('sample', 'feed')

function Get-DockerExecutable {
    $command = Get-Command docker -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    # A shell opened before Docker Desktop was installed does not have it on PATH yet.
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'),
        (Join-Path $env:ProgramFiles 'Docker\Docker\resources\bin\docker.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            # The CLI also looks up its credential helper (docker-credential-desktop) on PATH.
            $env:PATH = (Split-Path $candidate) + ';' + $env:PATH
            return $candidate
        }
    }

    throw 'Docker was not found. Install Docker Desktop and make sure it is running.'
}

function New-Secret {
    $bytes = New-Object byte[] 32
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }

    # Letters and digits only, so the value can go into URIs and connection strings unescaped.
    return ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '').Substring(0, 32)
}

function Initialize-EnvFile {
    if (Test-Path $envFile) { return }

    # -cmatch, not -match: case-insensitive matching follows the current culture, and under tr-TR
    # "I" does not match [A-Z] (its lower case is the dotless i, U+0131), so keys like RABBITMQ_* were skipped.
    $lines = Get-Content $envExample | ForEach-Object {
        if ($_ -cmatch '^\s*([A-Z0-9_]+)=generate\s*$') { "$($Matches[1])=$(New-Secret)" } else { $_ }
    }
    Set-Content -Path $envFile -Value $lines -Encoding Ascii
    Write-Host "Created $envFile with random passwords."
}

function Read-EnvFile {
    $values = @{}
    Get-Content $envFile | ForEach-Object {
        if ($_ -cmatch '^\s*([A-Z0-9_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
    }
    return $values
}

function Set-ServiceSecrets {
    $values = Read-EnvFile
    $amqp = "amqp://$($values['RABBITMQ_USER']):$($values['RABBITMQ_PASSWORD'])@localhost:5672/"

    foreach ($name in $services) {
        $id = "netboxplus-$name"
        $database = "Host=localhost;Port=5432;Database=$name;Username=$name;Password=$($values['POSTGRES_SERVICE_PASSWORD'])"

        foreach ($secret in @(@('ConnectionStrings:Database', $database), @('Messaging:ConnectionString', $amqp))) {
            & dotnet user-secrets set $secret[0] $secret[1] --id $id | Out-Null
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        Write-Host "User secrets set for $name (id $id)."
    }
}

function Invoke-Compose {
    param([string[]]$Arguments)

    $profileArguments = @()
    if ($Search) { $profileArguments = @('--profile', 'search') }

    & $script:docker compose --file $composeFile --env-file $envFile @profileArguments @Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$script:docker = Get-DockerExecutable

# Every command needs the file: compose resolves the required variables even for "down".
Initialize-EnvFile

switch ($Command) {
    'up' {
        Invoke-Compose @('up', '--detach', '--wait')
        Write-Host ''
        Write-Host 'Aspire dashboard:  http://localhost:18888'
        Write-Host 'RabbitMQ:          http://localhost:15672 (user and password in deploy/.env)'
        Write-Host 'Postgres:          localhost:5432, one database and role per service'
        Write-Host 'Valkey:            localhost:6379'
        if ($Search) { Write-Host 'Elasticsearch:     http://localhost:9200' }
    }
    'down' {
        Invoke-Compose @('--profile', 'search', 'down')
    }
    'status' {
        Invoke-Compose @('--profile', 'search', 'ps')
    }
    'logs' {
        $arguments = @('logs', '--follow', '--tail', '100')
        if ($Service) { $arguments += $Service }
        Invoke-Compose $arguments
    }
    'reset' {
        if (-not $Force) {
            throw 'reset deletes every local database, queue and cache. Run again with -Force to confirm.'
        }
        Invoke-Compose @('--profile', 'search', 'down', '--volumes')
        Write-Host 'All local data removed. The next "up" recreates the databases.'
    }
    'secrets' {
        Set-ServiceSecrets
    }
}
