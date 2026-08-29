# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# CTMS API image — multi-stage build.
# Build context MUST be the repository root:  docker build -t ctms-api .
# ---------------------------------------------------------------------------

# ----- build / publish -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1. Restore layer: copy only the files that affect `dotnet restore` so this
#    layer stays cached until a project's dependencies actually change. The image
#    only builds the API, so restore that project graph (Api -> Application ->
#    Domain, Infrastructure) rather than the whole solution — the AdminUI, client
#    SDK, samples and test projects are not part of the runtime image.
COPY global.json ./
COPY Directory.Build.props ./
COPY src/CTMS.Domain/CTMS.Domain.csproj            src/CTMS.Domain/
COPY src/CTMS.Application/CTMS.Application.csproj   src/CTMS.Application/
COPY src/CTMS.Infrastructure/CTMS.Infrastructure.csproj src/CTMS.Infrastructure/
COPY src/CTMS.Api/CTMS.Api.csproj                  src/CTMS.Api/
RUN dotnet restore src/CTMS.Api/CTMS.Api.csproj

# 2. Copy the rest of the source and publish the API.
COPY . .
RUN dotnet publish src/CTMS.Api/CTMS.Api.csproj \
        -c "$BUILD_CONFIGURATION" \
        --no-restore \
        -o /app/publish \
        /p:UseAppHost=false

# ----- runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Kestrel: HTTP only on 8080, no HTTPS inside the container (TLS is terminated
# by the ingress / container platform).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1
# Make sure no base-image default forces an HTTPS binding.
ENV ASPNETCORE_HTTPS_PORTS=

# Keep every path the runtime may write to under /tmp, so the container runs fine
# with a read-only root filesystem plus a writable `tmpfs: /tmp` (see
# docker-compose.prod.yml). TMPDIR covers Path.GetTempPath() (logging, TempData,
# form buffering); DOTNET_BUNDLE_EXTRACT_BASE_DIR covers any single-file host.
ENV TMPDIR=/tmp \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net

COPY --from=build /app/publish ./

# The dotnet/aspnet image ships a non-root user `app` (uid 1654). Use it.
USER app

EXPOSE 8080

# In-image liveness probe (useful for `docker run` / swarm / non-compose hosts;
# a compose healthcheck can still override it). The aspnet base image has no
# curl/wget, so hit /health over a raw TCP socket with bash and check for 200.
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/localhost/8080 || exit 1; printf "GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3; head -n 1 <&3 | grep -q "200" || exit 1'

ENTRYPOINT ["dotnet", "CTMS.Api.dll"]
