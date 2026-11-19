# Build from the repository root:
#   docker build -f src/Services/Streaming/Streaming.Migrator/Dockerfile -t netboxplus/streaming-migrator .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Solution-wide settings first: they pin the SDK and every package version.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Streaming/ src/Services/Streaming/
# The only projects of other services it is built with: their published events.
COPY src/Services/Auth/Auth.Contracts/ src/Services/Auth/Auth.Contracts/
COPY src/Services/Billing/Billing.Contracts/ src/Services/Billing/Billing.Contracts/
COPY src/Services/Catalog/Catalog.Contracts/ src/Services/Catalog/Catalog.Contracts/

RUN dotnet restore src/Services/Streaming/Streaming.Migrator/Streaming.Migrator.csproj

RUN dotnet publish src/Services/Streaming/Streaming.Migrator/Streaming.Migrator.csproj \
    --configuration Release --no-restore --output /app

# Chiseled: no shell or package manager, runs as a non-root user by default.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
# Runs once and exits; arguments such as announce-profiles or reindex follow the image name.
ENTRYPOINT ["dotnet", "Streaming.Migrator.dll"]
