# Build context is the repository root: docker build -f src/Noto.Server/Dockerfile -t noto/server .
# UID of the non-root user in the .NET chiseled images; declared here so it does not depend on the base image.
ARG APP_UID=1654

# The SDK stage always runs on the builder's own architecture and cross-compiles, so a multi-arch build does
# not run restore/publish under QEMU emulation (slow); only the small runtime stage is per-target.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG APP_UID
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Noto.Core/Noto.Core.csproj src/Noto.Core/
COPY src/Noto.Sync/Noto.Sync.csproj src/Noto.Sync/
COPY src/Noto.Server/Noto.Server.csproj src/Noto.Server/
RUN dotnet restore src/Noto.Server/Noto.Server.csproj -a $TARGETARCH

COPY src/Noto.Core src/Noto.Core
COPY src/Noto.Sync src/Noto.Sync
COPY src/Noto.Server src/Noto.Server
RUN dotnet publish src/Noto.Server/Noto.Server.csproj -c Release -a $TARGETARCH -o /out --no-restore -p:UseAppHost=false

# The chiseled runtime has no shell, so the data directory is prepared here and copied in with the app user's ownership.
RUN mkdir /data-seed && chown $APP_UID /data-seed

# Use Chiseled Ubuntu for an ultra-small, highly secure distroless runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
ARG APP_UID
WORKDIR /app
COPY --from=build /out .
COPY --from=build --chown=$APP_UID /data-seed /data

# Secrets are required at runtime and deliberately have no defaults:
#   JWT_SIGNING_KEY (>= 32 chars), PUBLIC_URL, and DATABASE_URL (or DB=sqlite with a mounted DATA_DIR).
ENV PORT=8080 \
    DATA_DIR=/data \
    ASPNETCORE_ENVIRONMENT=Production

VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Noto.Server.dll"]