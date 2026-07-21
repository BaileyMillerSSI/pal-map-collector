# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/health/ready || exit 1


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Palmap.Collector/Palmap.Collector.csproj", "Palmap.Collector/"]
COPY ["Palmap.CollectorApi/Palmap.CollectorApi.csproj", "Palmap.CollectorApi/"]
COPY ["Palmap.PalworldApi/Palmap.PalworldApi.csproj", "Palmap.PalworldApi/"]
RUN dotnet restore "./Palmap.Collector/Palmap.Collector.csproj"
COPY . .
WORKDIR "/src/Palmap.Collector"
RUN dotnet build "./Palmap.Collector.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Palmap.Collector.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Palmap.Collector.dll"]
