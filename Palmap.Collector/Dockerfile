FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS base
USER $APP_UID
WORKDIR /app
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
  CMD dotnet Palmap.Collector.dll --health-check

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Palmap.Protocol/Palmap.Protocol.csproj", "Palmap.Protocol/"]
COPY ["Palmap.Collector/Palmap.Collector.csproj", "Palmap.Collector/"]
RUN dotnet restore "Palmap.Collector/Palmap.Collector.csproj"
COPY . .
RUN dotnet publish "Palmap.Collector/Palmap.Collector.csproj" \
  --configuration $BUILD_CONFIGURATION \
  --output /app/publish \
  /p:UseAppHost=false

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Palmap.Collector.dll"]
