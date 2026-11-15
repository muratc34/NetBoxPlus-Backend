<#
.SYNOPSIS
    Starts, stops and inspects the local development dependencies.

.DESCRIPTION
    Wraps "docker compose" for deploy/compose.yaml. On the first "up" it creates deploy/.env from
    deploy/.env.example, replacing every "generate" value with a random password, and adds the
    token and playback signing keys. "secrets" writes the services' connection settings and those
    keys from deploy/.env into their user secrets.

.EXAMPLE
    ./deploy/dev.ps1 up            # Postgres, RabbitMQ, Valkey, Aspire dashboard
    ./deploy/dev.ps1 up -Search    # ... plus Elasticsearch
    ./deploy/dev.ps1 up -App       # ... plus every service and the gateway in containers (http://localhost:5100)
    ./deploy/dev.ps1 up -App -Observability  # ... and telemetry to Grafana (http://localhost:3000) instead of Aspire
    ./deploy/dev.ps1 seed          # demo movies, series and plans through the gateway (after up -App)
    ./deploy/dev.ps1 secrets       # connection settings and signing keys for running services and the gateway from the IDE
    ./deploy/dev.ps1 status
    ./deploy/dev.ps1 down          # stop, keep data
    ./deploy/dev.ps1 reset -Force  # stop and DELETE all local data
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'status', 'logs', 'reset', 'secrets', 'seed')]
    [string]$Command = 'status',

    # Also start Elasticsearch (compose profile "search").
    [switch]$Search,

    # For "up": also build and start every service, the gateway and the transcoder in containers
    # (compose profile "app", which includes Elasticsearch).
    [switch]$App,

    # With -App: also start Grafana with Prometheus, Loki and Tempo (compose profile "observability")
    # and send the containers' telemetry there instead of to the Aspire dashboard.
    [switch]$Observability,

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
$services = @('auth', 'catalog', 'billing', 'streaming', 'notification')

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
    # -cmatch, not -match: case-insensitive matching follows the current culture, and under tr-TR
    # "I" does not match [A-Z] (its lower case is the dotless i, U+0131), so keys like RABBITMQ_* were skipped.
    if (-not (Test-Path $envFile)) {
        $lines = Get-Content $envExample | ForEach-Object {
            if ($_ -cmatch '^\s*([A-Z0-9_]+)=generate\s*$') { "$($Matches[1])=$(New-Secret)" } else { $_ }
        }
        Set-Content -Path $envFile -Value $lines -Encoding Ascii
        Write-Host "Created $envFile with random passwords."
        return
    }

    # Settings added to the example after deploy/.env was created are appended; existing values stay.
    $existing = Read-EnvFile
    $added = @()
    foreach ($line in Get-Content $envExample) {
        if ($line -cmatch '^\s*([A-Z0-9_]+)=(.*)$' -and -not $existing.ContainsKey($Matches[1])) {
            $value = $Matches[2].Trim()
            if ($value -ceq 'generate') { $value = New-Secret }
            $added += "$($Matches[1])=$value"
        }
    }
    if ($added.Count -gt 0) {
        Add-Content -Path $envFile -Value $added -Encoding Ascii
        Write-Host "Added $($added.Count) new setting(s) to $envFile."
    }
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

    # Read by Auth.Migrator, which creates the account and gives it the administrator role.
    foreach ($secret in @(@('Auth:Seed:AdminEmail', $values['AUTH_ADMIN_EMAIL']), @('Auth:Seed:AdminPassword', $values['AUTH_ADMIN_PASSWORD']))) {
        & dotnet user-secrets set $secret[0] $secret[1] --id 'netboxplus-auth' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Write-Host "Administrator $($values['AUTH_ADMIN_EMAIL']) set for auth; the password is AUTH_ADMIN_PASSWORD in $envFile."

    # Catalog images: the well-known settings of the local Azure Blob Storage emulator.
    foreach ($secret in @(@('ConnectionStrings:Images', 'UseDevelopmentStorage=true'), @('ConnectionStrings:Search', 'http://localhost:9200'), @('Images:UploadOrigins:0', 'http://localhost:5174'))) {
        & dotnet user-secrets set $secret[0] $secret[1] --id 'netboxplus-catalog' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # Streaming videos: the local Azure Blob Storage emulator; the admin panel uploads, the app plays.
    foreach ($secret in @(@('ConnectionStrings:Videos', 'UseDevelopmentStorage=true'), @('Videos:UploadOrigins:0', 'http://localhost:5174'), @('Videos:PlaybackOrigins:0', 'http://localhost:5173'))) {
        & dotnet user-secrets set $secret[0] $secret[1] --id 'netboxplus-streaming' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # Notification sends e-mail to Mailpit unless deploy/.env says EMAIL_PROVIDER=Brevo.
    if ((Get-EmailProvider $values) -eq 'Brevo') {
        $email = @(@('Email:Provider', 'Brevo'), @('Email:Brevo:ApiKey', $values['BREVO_API_KEY']), @('Email:From', $values['EMAIL_FROM']))
        Write-Host 'EMAIL_PROVIDER=Brevo: notification sends real e-mail through Brevo.'
    }
    else {
        $email = @(@('Email:Provider', 'Smtp'), @('Email:Smtp:Host', 'localhost'), @('Email:Smtp:Port', '1025'), @('Email:From', 'no-reply@netboxplus.local'))
        & dotnet user-secrets remove 'Email:Brevo:ApiKey' --id 'netboxplus-notification' | Out-Null
        Write-Host 'Notification sends to Mailpit (http://localhost:8025).'
    }
    foreach ($secret in $email) {
        & dotnet user-secrets set $secret[0] $secret[1] --id 'netboxplus-notification' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # Billing talks to Stripe only with a key; without one its payment endpoints answer 503.
    $stripeKey = $values['STRIPE_SECRET_KEY']
    if ($stripeKey) {
        & dotnet user-secrets set 'Billing:Stripe:SecretKey' $stripeKey --id 'netboxplus-billing' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host 'Stripe key set for billing.'
    }
    else {
        & dotnet user-secrets remove 'Billing:Stripe:SecretKey' --id 'netboxplus-billing' | Out-Null
        Write-Host 'No STRIPE_SECRET_KEY in deploy/.env; billing payments answer 503.'
    }

    $webhookSecret = $values['STRIPE_WEBHOOK_SECRET']
    if ($webhookSecret) {
        & dotnet user-secrets set 'Billing:Stripe:WebhookSecret' $webhookSecret --id 'netboxplus-billing' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host 'Stripe webhook secret set for billing.'
    }
    else {
        & dotnet user-secrets remove 'Billing:Stripe:WebhookSecret' --id 'netboxplus-billing' | Out-Null
        Write-Host 'No STRIPE_WEBHOOK_SECRET in deploy/.env; the billing webhook answers 503.'
    }

    # The gateway keeps browser sessions in Valkey.
    $valkey = "localhost:6379,password=$($values['VALKEY_PASSWORD'])"
    & dotnet user-secrets set 'ConnectionStrings:Valkey' $valkey --id 'netboxplus-gateway' | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host 'User secrets set for gateway (id netboxplus-gateway).'

    # The signing keys live in deploy/.env (see Initialize-SigningKeys), so the containers use the same ones.
    $keyId = $values['AUTH_TOKEN_KEY_ID']
    foreach ($secret in @(@("Auth:Tokens:SigningKeys:$keyId", $values['AUTH_TOKEN_SIGNING_KEY']), @('Auth:Tokens:ActiveKeyId', $keyId))) {
        & dotnet user-secrets set $secret[0] $secret[1] --id 'netboxplus-auth' | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Write-Host "Token signing key $keyId set for auth."
    & dotnet user-secrets set 'Playback:SigningKey' $values['PLAYBACK_SIGNING_KEY'] --id 'netboxplus-streaming' | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host 'Playback signing key set for streaming.'
}

function Read-UserSecrets([string]$id) {
    # Read straight from the file, so no .NET SDK is needed; values are flattened ("A:B:C").
    $path = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
    if (-not (Test-Path $path)) { return $null }
    return Get-Content $path -Raw | ConvertFrom-Json
}

function New-TokenSigningKey {
    # A P-256 private key as PKCS#8 PEM on one line (the key importer accepts spaces for line breaks),
    # so it fits in deploy/.env. CNG works in Windows PowerShell 5.1, which cannot export PEM itself.
    $parameters = New-Object System.Security.Cryptography.CngKeyCreationParameters
    $parameters.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
    $key = [System.Security.Cryptography.CngKey]::Create([System.Security.Cryptography.CngAlgorithm]::ECDsaP256, $null, $parameters)
    try { $der = $key.Export([System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob) } finally { $key.Dispose() }
    return '-----BEGIN PRIVATE KEY----- ' + [Convert]::ToBase64String($der) + ' -----END PRIVATE KEY-----'
}

function Initialize-SigningKeys {
    # Created once and kept: a new token key would end every session, a new playback key every playlist
    # address handed out. Keys made earlier by "secrets" (in user secrets only) are moved here as they are.
    $values = Read-EnvFile
    $added = @()

    if (-not $values['AUTH_TOKEN_SIGNING_KEY']) {
        $auth = Read-UserSecrets 'netboxplus-auth'
        $keyId = if ($auth) { $auth.'Auth:Tokens:ActiveKeyId' } else { $null }
        $pem = if ($keyId) { $auth."Auth:Tokens:SigningKeys:$keyId" } else { $null }
        if ($pem) {
            $pem = ($pem -split "\s*[\r\n]+\s*" | Where-Object { $_ }) -join ' '
            Write-Host "Token signing key $keyId moved from the auth user secrets to $envFile."
        }
        else {
            $keyId = 'local-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
            $pem = New-TokenSigningKey
            Write-Host "Token signing key $keyId created in $envFile."
        }
        $added += "AUTH_TOKEN_KEY_ID=$keyId", "AUTH_TOKEN_SIGNING_KEY=$pem"
    }

    if (-not $values['PLAYBACK_SIGNING_KEY']) {
        $streaming = Read-UserSecrets 'netboxplus-streaming'
        $playback = if ($streaming) { $streaming.'Playback:SigningKey' } else { $null }
        if ($playback) {
            Write-Host "Playback signing key moved from the streaming user secrets to $envFile."
        }
        else {
            $bytes = New-Object byte[] 32
            $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
            try { $random.GetBytes($bytes) } finally { $random.Dispose() }
            $playback = [Convert]::ToBase64String($bytes)
            Write-Host "Playback signing key created in $envFile."
        }
        $added += "PLAYBACK_SIGNING_KEY=$playback"
    }

    if ($added.Count -gt 0) {
        # A key line from an older file may exist but be empty; the last value wins when read.
        Add-Content -Path $envFile -Value $added -Encoding Ascii
    }
}

function Invoke-Compose {
    param([string[]]$Arguments, [string[]]$Profiles = @())

    $profileArguments = @()
    foreach ($name in $Profiles) { $profileArguments += @('--profile', $name) }

    & $script:docker compose --file $composeFile --env-file $envFile @profileArguments @Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Build-AppImages {
    # One image at a time: building all of them at once can stall Docker Desktop on a laptop.
    # The layers they share (restore, SDK) are cached after the first, so this costs little.
    param([string[]]$Profiles)

    $profileArguments = @()
    foreach ($name in $Profiles) { $profileArguments += @('--profile', $name) }
    $built = & $script:docker compose --file $composeFile --env-file $envFile @profileArguments config --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    foreach ($service in $built.services.PSObject.Properties) {
        if ($service.Value.build) {
            Write-Host "Building $($service.Name)..."
            Invoke-Compose @('build', $service.Name) $Profiles
        }
    }
}

function Get-EmailProvider {
    # Mailpit unless deploy/.env explicitly says EMAIL_PROVIDER=Brevo: a Brevo key on its own never
    # sends real e-mail, so test sign-ups with made-up addresses stay local.
    param([hashtable]$Values)
    $provider = $Values['EMAIL_PROVIDER']
    if (-not $provider -or $provider -ceq 'Mailpit') { return 'Mailpit' }
    if ($provider -cne 'Brevo') { throw "EMAIL_PROVIDER in deploy/.env must be Mailpit or Brevo, not '$provider'." }
    if (-not ($Values['BREVO_API_KEY'] -and $Values['EMAIL_FROM'])) {
        throw 'EMAIL_PROVIDER=Brevo needs BREVO_API_KEY and EMAIL_FROM in deploy/.env.'
    }
    return 'Brevo'
}

function Set-ContainerEmailSettings {
    # The same rule as "secrets" (Get-EmailProvider). Passed to compose through the environment;
    # compose reads them in the notification-api entry.
    $values = Read-EnvFile
    if ((Get-EmailProvider $values) -eq 'Brevo') {
        $env:NETBOXPLUS_EMAIL_PROVIDER = 'Brevo'
        $env:NETBOXPLUS_EMAIL_FROM = $values['EMAIL_FROM']
        Write-Host 'EMAIL_PROVIDER=Brevo: notification sends real e-mail through Brevo.'
    }
    else {
        $env:NETBOXPLUS_EMAIL_PROVIDER = 'Smtp'
        $env:NETBOXPLUS_EMAIL_FROM = 'no-reply@netboxplus.local'
    }
}

function Wait-AppReady {
    # The images have no shell for a compose health check, so readiness is polled from outside.
    $endpoints = [ordered]@{
        'auth-api' = 'http://localhost:5103/health/ready'
        'catalog-api' = 'http://localhost:5104/health/ready'
        'billing-api' = 'http://localhost:5105/health/ready'
        'streaming-api' = 'http://localhost:5106/health/ready'
        'notification-api' = 'http://localhost:5107/health/ready'
        'gateway' = 'http://localhost:5100/health/ready'
    }
    $deadline = (Get-Date).AddMinutes(3)
    foreach ($name in $endpoints.Keys) {
        while ($true) {
            try {
                Invoke-WebRequest -Uri $endpoints[$name] -UseBasicParsing -TimeoutSec 5 | Out-Null
                Write-Host "  $name ready"
                break
            }
            catch {
                if ((Get-Date) -gt $deadline) {
                    throw "$name is not ready after 3 minutes. See: ./deploy/dev.ps1 logs -Service $name"
                }
                Start-Sleep -Seconds 2
            }
        }
    }
}

$script:docker = Get-DockerExecutable

# Every command needs the file: compose resolves the required variables even for "down".
Initialize-EnvFile
Initialize-SigningKeys

switch ($Command) {
    'up' {
        if ($Observability -and -not $App) {
            throw '-Observability goes with -App: only the app containers send their telemetry to Grafana.'
        }
        $dependencies = @()
        if ($Search -or $App) { $dependencies = @('search') }
        Invoke-Compose @('up', '--detach', '--wait') $dependencies
        # A database added to NETBOXPLUS_DATABASES after the volume was created: made now, data kept.
        Invoke-Compose @('exec', '-T', 'postgres', 'sh', '/docker-entrypoint-initdb.d/01-service-databases.sh')
        if ($App) {
            # The databases exist now; each Migrator runs and exits, then the services start.
            Set-ContainerEmailSettings
            $appProfiles = @('search', 'app')
            if ($Observability) {
                $env:NETBOXPLUS_OTLP_ENDPOINT = 'http://lgtm:4317'
                $appProfiles += 'observability'
            }
            else {
                # Back to the Aspire dashboard, also for containers that ran with Grafana before.
                Remove-Item Env:NETBOXPLUS_OTLP_ENDPOINT -ErrorAction SilentlyContinue
            }
            Build-AppImages $appProfiles
            Invoke-Compose @('up', '--detach') $appProfiles
            Wait-AppReady
        }
        Write-Host ''
        if ($App) {
            Write-Host 'Gateway:           http://localhost:5100 (the only address a browser needs)'
            Write-Host 'Services:          http://localhost:5103-5107 (auth, catalog, billing, streaming, notification)'
        }
        if ($Observability) {
            Write-Host 'Grafana:           http://localhost:3000 (dashboard "NetBoxPlus"; admin/admin to edit)'
        }
        Write-Host 'Aspire dashboard:  http://localhost:18888'
        Write-Host 'RabbitMQ:          http://localhost:15672 (user and password in deploy/.env)'
        Write-Host 'Postgres:          localhost:5432, one database and role per service'
        Write-Host 'Valkey:            localhost:6379'
        Write-Host 'Mailpit:           http://localhost:8025 (e-mails sent locally)'
        if ($Search) { Write-Host 'Elasticsearch:     http://localhost:9200' }
    }
    'down' {
        Invoke-Compose @('down') @('search', 'app', 'observability')
    }
    'status' {
        Invoke-Compose @('ps', '--all') @('search', 'app', 'observability')
    }
    'logs' {
        $arguments = @('logs', '--follow', '--tail', '100')
        if ($Service) { $arguments += $Service }
        Invoke-Compose $arguments @('search', 'app', 'observability')
    }
    'reset' {
        if (-not $Force) {
            throw 'reset deletes every local database, queue and cache. Run again with -Force to confirm.'
        }
        Invoke-Compose @('down', '--volumes') @('search', 'app', 'observability')
        Write-Host 'All local data removed. The next "up" recreates the databases.'
    }
    'secrets' {
        Set-ServiceSecrets
    }
    'seed' {
        # The seeder uses the gateway's admin API like the admin panel, so the system must be up.
        try {
            Invoke-WebRequest -Uri 'http://localhost:5100/health/ready' -UseBasicParsing -TimeoutSec 5 | Out-Null
        }
        catch {
            throw 'The gateway is not running. Start the system first: ./deploy/dev.ps1 up -App'
        }
        # Safe to run again: it finishes what is missing and leaves the rest as it is.
        Invoke-Compose @('run', '--rm', '--build', 'demo-seed') @('tools')
        Write-Host ''
        Write-Host 'Demo content is in place: http://localhost:5100/catalog/titles'
    }
}
