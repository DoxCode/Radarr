# ...existing code...
# 2) Runtime stage (use official ASP.NET runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# metadata
LABEL build_version="Linuxserver.io version: dox_radarr:1.0"
LABEL maintainer="Roxedus,thespad"

ENV XDG_CONFIG_HOME="/config/xdg" \
    COMPlus_EnableDiagnostics=0 \
    TMPDIR=/run/radarr-temp

# optional runtime tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    xmlstarlet \
    ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# copy published output from build stage
COPY --from=build /app/publish /app/radarr/bin

RUN printf "UpdateMethod=docker\nBranch=${RADARR_BRANCH}\nPackageAuthor=[local-build]\n" > /app/radarr/package_info

EXPOSE 7878
VOLUME /config
WORKDIR /app/radarr/bin

ENTRYPOINT ["dotnet", "Radarr.dll"]
# ...existing code...