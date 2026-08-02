# syntax=docker/dockerfile:1

# ------------------------------------------------------------------
# Build stage
# ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer caching: source changes do not re-download packages).
COPY MaybeComingBot.Console/MaybeComingBot.csproj MaybeComingBot.Console/
RUN dotnet restore MaybeComingBot.Console/MaybeComingBot.csproj

# Copy the rest of the source and publish.
COPY MaybeComingBot.Console/ MaybeComingBot.Console/
RUN dotnet publish MaybeComingBot.Console/MaybeComingBot.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# ------------------------------------------------------------------
# Runtime stage
# ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Install tzdata so .NET timezone APIs (used for status timestamps) work.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*

# Create a non-root user (UID 1654 is the conventional container user).
RUN useradd --create-home --uid 1654 appuser

WORKDIR /app

# Copy the published application.
COPY --from=build /app/publish ./

# Ship a base configuration file; real values come from environment variables.
COPY MaybeComingBot.Console/appsettings.Template.json ./appsettings.json

# Writable logs directory for the Serilog file sink.
RUN mkdir -p /app/logs \
    && chown -R appuser:appuser /app

USER appuser

# Health-server port. Railway overrides PORT automatically for web services.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MaybeComingBot.dll"]

