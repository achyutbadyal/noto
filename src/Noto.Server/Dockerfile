# Build context is the repository root: docker build -f src/Noto.Server/Dockerfile -t noto/server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Noto.Core/Noto.Core.csproj src/Noto.Core/
COPY src/Noto.Sync/Noto.Sync.csproj src/Noto.Sync/
COPY src/Noto.Server/Noto.Server.csproj src/Noto.Server/
RUN dotnet restore src/Noto.Server/Noto.Server.csproj

COPY src/Noto.Core src/Noto.Core
COPY src/Noto.Sync src/Noto.Sync
COPY src/Noto.Server src/Noto.Server
RUN dotnet publish src/Noto.Server/Noto.Server.csproj -c Release -o /out --no-restore

# Use Chiseled Ubuntu for an ultra-small, highly secure distroless runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-jammy-chiseled AS runtime
WORKDIR /app
COPY --from=build /out .

# Secrets are required at runtime and deliberately have no defaults:
#   JWT_SIGNING_KEY (>= 32 chars), PUBLIC_URL, and DATABASE_URL (or DB=sqlite with a mounted DATA_DIR).
ENV PORT=8080 \
    DATA_DIR=/data \
    ASPNETCORE_ENVIRONMENT=Production

VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Noto.Server.dll"]