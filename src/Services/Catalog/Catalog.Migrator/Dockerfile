# Build from the repository root:
#   docker build -f src/Services/Catalog/Catalog.Migrator/Dockerfile -t netboxplus/catalog-migrator .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Solution-wide settings first: they pin the SDK and every package version.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Catalog/ src/Services/Catalog/
# The one streaming project catalog may use: the events it consumes.
COPY src/Services/Streaming/Streaming.Contracts/ src/Services/Streaming/Streaming.Contracts/

RUN dotnet restore src/Services/Catalog/Catalog.Migrator/Catalog.Migrator.csproj

RUN dotnet publish src/Services/Catalog/Catalog.Migrator/Catalog.Migrator.csproj \
    --configuration Release --no-restore --output /app

# Chiseled: no shell or package manager, runs as a non-root user by default.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
# Runs once and exits; arguments such as announce-profiles or reindex follow the image name.
ENTRYPOINT ["dotnet", "Catalog.Migrator.dll"]
