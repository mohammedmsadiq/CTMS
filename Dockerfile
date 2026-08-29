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

COPY --from=build /app/publish ./

# The dotnet/aspnet image ships a non-root user `app` (uid 1654). Use it.
USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "CTMS.Api.dll"]
