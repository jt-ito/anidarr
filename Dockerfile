FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install Node.js and Yarn
RUN apt-get update && apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    npm install -g yarn

# Copy source code
COPY . .

# Build frontend
RUN yarn install
RUN yarn build --env production

# Build backend
RUN dotnet publish src/NzbDrone.Console/Sonarr.Console.csproj -c Release -f net10.0 -o /app/publish -r linux-x64 --self-contained false -p:NuGetAudit=false
RUN dotnet publish src/NzbDrone.Mono/Sonarr.Mono.csproj -c Release -f net10.0 -o /app/publish -r linux-x64 --self-contained false -p:NuGetAudit=false

# Copy the built UI to the publish directory so the backend can serve it
RUN cp -r _output/UI /app/publish/UI

# Use the ASP.NET runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install runtime dependencies
RUN apt-get update && apt-get install -y \
    curl \
    mediainfo \
    sqlite3 \
    libcurl4 \
    tzdata \
    gosu \
    && rm -rf /var/lib/apt/lists/*

# Copy the published backend and frontend from the build stage
COPY --from=build /app/publish .

# Setup entrypoint script
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

# Data volume for config and database
VOLUME /config

# Default Sonarr port
EXPOSE 8989

ENTRYPOINT ["/app/docker-entrypoint.sh"]
CMD ["./Sonarr", "-nobrowser", "-data=/config"]
