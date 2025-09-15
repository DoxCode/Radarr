# 1) Build stage: compila y publica el proyecto
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

ARG TARGET_FRAMEWORK=net8.0

COPY . .

RUN dotnet restore src/Radarr.sln
RUN dotnet publish src/NzbDrone.Console/Radarr.Console.csproj -c Release -f ${TARGET_FRAMEWORK} -o /app/publish

# 2) Runtime stage (ASP.NET 8 runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

ARG VERSION=local
ARG BUILD_DATE=unknown
ARG RADARR_BRANCH=master

# metadata
LABEL org.opencontainers.image.version="${VERSION}"
LABEL org.opencontainers.image.created="${BUILD_DATE}"
LABEL maintainer="Roxedus,thespad"

ENV XDG_CONFIG_HOME="/config/xdg" \
    COMPlus_EnableDiagnostics=0 \
    TMPDIR=/run/radarr-temp

# herramientas opcionales
RUN apt-get update && apt-get install -y --no-install-recommends \
    xmlstarlet \
    ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# copiar el publish del stage build
COPY --from=build /app/publish /app/radarr/bin

# info de paquete
RUN printf "UpdateMethod=docker\nBranch=%s\nPackageVersion=%s\nPackageAuthor=[local-build]\n" "${RADARR_BRANCH}" "${VERSION}" > /app/radarr/package_info

EXPOSE 7878
VOLUME /config
WORKDIR /app/radarr/bin

ENTRYPOINT ["dotnet", "Radarr.dll"]