# Build from the repository root:
#   docker build -f src/Tools/NetBoxPlus.DemoSeeder/Dockerfile -t netboxplus/demo-seed .
# Locally it runs through ./deploy/dev.ps1 seed (compose profile "tools").

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Directory.Build.props src/
COPY src/Tools/NetBoxPlus.DemoSeeder/ src/Tools/NetBoxPlus.DemoSeeder/
RUN dotnet restore src/Tools/NetBoxPlus.DemoSeeder/NetBoxPlus.DemoSeeder.csproj
RUN dotnet publish src/Tools/NetBoxPlus.DemoSeeder/NetBoxPlus.DemoSeeder.csproj \
    --configuration Release --no-restore --output /app

# Not chiseled: the seeder draws its images and videos with ffmpeg, and the titles need a font.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ffmpeg fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "NetBoxPlus.DemoSeeder.dll"]
