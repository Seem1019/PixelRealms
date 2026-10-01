# syntax=docker/dockerfile:1
# HU-073 CA1: imagen multi-stage (sdk:10.0 → aspnet:10.0-alpine, usuario no root). Contexto de build = raíz del repo:
#   docker build -f server/Dockerfile -t ghcr.io/<usuario>/pixelrealms-server:latest .
# El servidor lee content/ y maps/ de /app (Content:Dir); .env NO se copia: las variables llegan por entorno (compose).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/PixelRealms.sln server/Directory.Build.props* server/Directory.Packages.props* ./server/
COPY server/src ./server/src
COPY server/tools ./server/tools
RUN dotnet restore server/src/PixelRealms.Server/PixelRealms.Server.csproj
RUN dotnet publish server/src/PixelRealms.Server/PixelRealms.Server.csproj -c Release -o /out --no-restore \
    -p:UseAppHost=false -p:DebugType=none -p:InvariantGlobalization=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
RUN addgroup -S pixelrealms && adduser -S -G pixelrealms pixelrealms
COPY --from=build /out ./
COPY content ./content
COPY maps ./maps
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:5080 \
    Content__Dir=/app/content \
    Maps__Dir=/app/maps \
    DOTNET_gcServer=0
USER pixelrealms
EXPOSE 5080
HEALTHCHECK --interval=30s --timeout=3s --retries=3 CMD wget -qO- http://127.0.0.1:5080/health >/dev/null || exit 1
ENTRYPOINT ["dotnet", "PixelRealms.Server.dll"]
