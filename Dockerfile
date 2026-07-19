# Frontend build stage
FROM node:20-slim AS frontend-build
WORKDIR /src
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile
COPY . .
RUN yarn build --env production

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Solution and Project files for caching
COPY src/Sonarr.sln src/
COPY src/Directory.Build.props src/
COPY src/Directory.Build.targets src/
COPY src/NuGet.Config src/
COPY src/NzbDrone/Sonarr.csproj src/NzbDrone/
COPY src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj src/NzbDrone.Api.Test/
COPY src/NzbDrone.Automation.Test/Sonarr.Automation.Test.csproj src/NzbDrone.Automation.Test/
COPY src/NzbDrone.Common/Sonarr.Common.csproj src/NzbDrone.Common/
COPY src/NzbDrone.Common.Test/Sonarr.Common.Test.csproj src/NzbDrone.Common.Test/
COPY src/NzbDrone.Console/Sonarr.Console.csproj src/NzbDrone.Console/
COPY src/NzbDrone.Core/Sonarr.Core.csproj src/NzbDrone.Core/
COPY src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj src/NzbDrone.Core.Test/
COPY src/NzbDrone.Host/Sonarr.Host.csproj src/NzbDrone.Host/
COPY src/NzbDrone.Host.Test/Sonarr.Host.Test.csproj src/NzbDrone.Host.Test/
COPY src/NzbDrone.Integration.Test/Sonarr.Integration.Test.csproj src/NzbDrone.Integration.Test/
COPY src/NzbDrone.Libraries.Test/Sonarr.Libraries.Test.csproj src/NzbDrone.Libraries.Test/
COPY src/NzbDrone.Mono/Sonarr.Mono.csproj src/NzbDrone.Mono/
COPY src/NzbDrone.Mono.Test/Sonarr.Mono.Test.csproj src/NzbDrone.Mono.Test/
COPY src/NzbDrone.SignalR/Sonarr.SignalR.csproj src/NzbDrone.SignalR/
COPY src/NzbDrone.Test.Common/Sonarr.Test.Common.csproj src/NzbDrone.Test.Common/
COPY src/NzbDrone.Test.Dummy/Sonarr.Test.Dummy.csproj src/NzbDrone.Test.Dummy/
COPY src/NzbDrone.Update/Sonarr.Update.csproj src/NzbDrone.Update/
COPY src/NzbDrone.Update.Test/Sonarr.Update.Test.csproj src/NzbDrone.Update.Test/
COPY src/NzbDrone.Windows/Sonarr.Windows.csproj src/NzbDrone.Windows/
COPY src/NzbDrone.Windows.Test/Sonarr.Windows.Test.csproj src/NzbDrone.Windows.Test/
COPY src/ServiceHelpers/ServiceInstall/ServiceInstall.csproj src/ServiceHelpers/ServiceInstall/
COPY src/ServiceHelpers/ServiceUninstall/ServiceUninstall.csproj src/ServiceHelpers/ServiceUninstall/
COPY src/Sonarr.Api.V3/Sonarr.Api.V3.csproj src/Sonarr.Api.V3/
COPY src/Sonarr.Api.V5/Sonarr.Api.V5.csproj src/Sonarr.Api.V5/
COPY src/Sonarr.Http/Sonarr.Http.csproj src/Sonarr.Http/
COPY src/Sonarr.Http.Test/Sonarr.Http.Test.csproj src/Sonarr.Http.Test/
COPY src/Sonarr.RuntimePatches/Sonarr.RuntimePatches.csproj src/Sonarr.RuntimePatches/

# Restore dependencies
RUN dotnet restore src/Sonarr.sln

# Copy the rest of the source code
COPY . .

# Build backend
RUN dotnet publish src/NzbDrone.Console/Sonarr.Console.csproj -c Release -f net10.0 -o /app/publish -r linux-x64 --self-contained false --no-restore -p:NuGetAudit=false
RUN dotnet publish src/NzbDrone.Mono/Sonarr.Mono.csproj -c Release -f net10.0 -o /app/publish -r linux-x64 --self-contained false --no-restore -p:NuGetAudit=false

# Copy frontend UI output into the published directory
COPY --from=frontend-build /src/_output/UI /app/publish/UI

# Use the ASP.NET runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    mediainfo \
    sqlite3 \
    libcurl4 \
    tzdata \
    gosu \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Copy the published backend and frontend from the build stage
COPY --from=build /app/publish .

# Setup entrypoint script
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh
RUN if [ -f /app/ffprobe ]; then chmod +x /app/ffprobe; fi

# Data volume for config and database
VOLUME /config

# Default Sonarr port
EXPOSE 8989

# Healthcheck
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8989/ping || exit 1

ENTRYPOINT ["/app/docker-entrypoint.sh"]
CMD ["./Anidarr", "-nobrowser", "-data=/config"]
