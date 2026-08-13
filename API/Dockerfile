# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# CarPosAPI — REST API + MQTT ingest, in one container.
#
# Build context is API/, so both the app and its test project are visible and
# `dotnet restore` can resolve the solution the same way it does locally.
#
# Two things this image deliberately does NOT do:
#   * listen on HTTPS — TLS is terminated by the reverse proxy in front of it,
#     and a second certificate inside the container would only be one more
#     thing to rotate;
#   * carry appsettings.Local.json — the csproj already excludes it from
#     publish output, and .dockerignore keeps it out of the build context too.
#     Production secrets come from environment variables, nothing else.
# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files copied. This layer is cached until
# a dependency actually changes, so ordinary source edits skip the slow step.
COPY CarPos.sln ./
COPY CarPosAPI/CarPosAPI.csproj CarPosAPI/
COPY CarPosAPI.Tests/CarPosAPI.Tests.csproj CarPosAPI.Tests/
RUN dotnet restore CarPosAPI/CarPosAPI.csproj

COPY . .
RUN dotnet publish CarPosAPI/CarPosAPI.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# ---------------------------------------------------------------------------
# Runtime image: the ASP.NET runtime only — no SDK, no source, no build tools.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet runtime image is Debian *slim*: it ships neither wget nor curl, so
# the compose healthcheck (`wget -qO- .../health`) would exit 127 on every probe
# and the container could only ever go "unhealthy" — which in turn fails the
# frontend's `depends_on: service_healthy` and takes the whole stack down about
# two minutes after a perfectly healthy start-up. wget rather than curl because
# it exits non-zero on a 5xx by itself, so a failing /health really does mark the
# container unhealthy without extra flags.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

# The aspnet image ships a non-root "app" user (uid 1654). Running as it means a
# code-execution bug in the API is not automatically root inside the container.
USER app

# Plain HTTP on 8080 — see the header comment. Nothing publishes this port to
# the host; only the frontend's nginx can reach it, over the compose network.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .

ENTRYPOINT ["dotnet", "CarPosAPI.dll"]
