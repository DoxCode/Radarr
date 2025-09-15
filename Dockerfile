# ...existing code...
# Multi-stage Dockerfile: build with dotnet SDK, run on same alpine base as original image

ARG BUILD_DATE
ARG VERSION
ARG RADARR_BRANCH="master"
ARG RADARR_RELEASE=""

# 1) Build stage (uses Microsoft SDK to compile the solution)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy repo and restore/build
COPY . .
RUN dotnet restore src/Radarr.sln

# Publish the Console project (ajusta la ruta si es necesario)
RUN dotnet publish src/NzbDrone.Console/Radarr.Console.csproj -c Release -o /app/publish

# 2) Runtime stage (keep original linuxserver base)
FROM ghcr.io/linuxserver/baseimage-alpine:3.22 AS runtime

# metadata
LABEL build_version="Linuxserver.io version:- ${VERSION} Build-date:- ${BUILD_DATE}"
LABEL maintainer="Roxedus,thespad"

ENV XDG_CONFIG_HOME="/config/xdg" \
    COMPlus_EnableDiagnostics=0 \
    TMPDIR=/run/radarr-temp

# runtime deps (mismatched con el SDK pero replicando tu base original)
RUN apk add -U --no-cache \
    icu-libs \
    sqlite-libs \
    xmlstarlet \
    ca-certificates

# copy published output from build stage
COPY --from=build /app/publish /app/radarr/bin

# package info (opcional)
RUN printf "UpdateMethod=docker\nBranch=${RADARR_BRANCH}\nPackageVersion=${VERSION}\nPackageAuthor=[local-build]\n" > /app/radarr/package_info

EXPOSE 7878
VOLUME /config
WORKDIR /app/radarr/bin

# Nota: si publicaste como framework-dependent usa "dotnet Radarr.dll"
# si publicaste como self-contained puedes ejecutar el binario nativo (./Radarr)
ENTRYPOINT ["dotnet", "Radarr.dll"]
# ...existing code...