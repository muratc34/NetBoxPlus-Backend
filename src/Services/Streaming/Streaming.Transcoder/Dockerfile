# Build from the repository root:
#   docker build -f src/Services/Streaming/Streaming.Transcoder/Dockerfile -t netboxplus/streaming-transcoder .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Streaming/ src/Services/Streaming/

RUN dotnet restore src/Services/Streaming/Streaming.Transcoder/Streaming.Transcoder.csproj
RUN dotnet publish src/Services/Streaming/Streaming.Transcoder/Streaming.Transcoder.csproj \
    --configuration Release --no-restore --output /app

# Not chiseled: ffmpeg comes from the distribution's (Ubuntu) packages, which need a package manager to install.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "Streaming.Transcoder.dll"]
