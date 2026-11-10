# Build from the repository root:
#   docker build -f src/Services/Billing/Billing.API/Dockerfile -t netboxplus/billing-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Solution-wide settings first: they pin the SDK and every package version.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Billing/ src/Services/Billing/
# The one auth project billing may use: the events it consumes.
COPY src/Services/Auth/Auth.Contracts/ src/Services/Auth/Auth.Contracts/

RUN dotnet restore src/Services/Billing/Billing.API/Billing.API.csproj

# The OpenAPI document is generated (and committed) by local builds, not inside the image.
RUN dotnet publish src/Services/Billing/Billing.API/Billing.API.csproj \
    --configuration Release --no-restore --output /app \
    -p:OpenApiGenerateDocuments=false

# Chiseled: no shell or package manager, runs as a non-root user by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Billing.API.dll"]
