#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["src/ApiGateways/OcelotAPI/OcelotAPI.csproj", "src/ApiGateways/OcelotAPI/"]
COPY ["src/BuildingBlocks/Shared/Shared.csproj", "src/BuildingBlocks/Shared/"]
RUN dotnet restore "src/ApiGateways/OcelotAPI/OcelotAPI.csproj"
COPY . .
WORKDIR "/src/src/ApiGateways/OcelotAPI"
RUN dotnet build "OcelotAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OcelotAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OcelotAPI.dll"]