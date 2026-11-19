# Build from the repository root:
#   docker build -f src/Gateway/Gateway/Dockerfile -t netboxplus/gateway .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Solution-wide settings first: they pin the SDK and every package version.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Gateway/ src/Gateway/

RUN dotnet restore src/Gateway/Gateway/Gateway.csproj

RUN dotnet publish src/Gateway/Gateway/Gateway.csproj \
    --configuration Release --no-restore --output /app

# Chiseled: no shell or package manager, runs as a non-root user by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
