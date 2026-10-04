# Standard .NET two-stage build, per tech-stack.md: restore and publish in the
# SDK image, then run on the smaller aspnet runtime image.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Dependencies are restored first, so this layer is cached across source-only
# changes.
COPY Saakh.Api/Saakh.Api.csproj Saakh.Api/
RUN dotnet restore Saakh.Api/Saakh.Api.csproj

COPY . .
RUN dotnet publish Saakh.Api/Saakh.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# The runtime image has no SDK, so `dotnet ef` cannot run inside it. EF Core's
# migration bundle solves this: a self-contained executable that applies exactly
# the committed migrations. Migrations stay a one-off exec step against the
# running container rather than something that happens silently on boot.
RUN dotnet tool install --global dotnet-ef --version 9.* \
    && /root/.dotnet/tools/dotnet-ef migrations bundle \
        --project Saakh.Api/Saakh.Api.csproj \
        --configuration Release \
        --self-contained \
        --target-runtime linux-x64 \
        --output /app/publish/migrate \
        --force

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl is here only so the container can health-check its own /health endpoint,
# which is what the compose file's depends_on condition waits on.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The evidence-documents volume is mounted here by the compose file.
RUN mkdir -p /app/storage/evidence && chmod +x ./migrate

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://127.0.0.1:5000/health || exit 1

ENTRYPOINT ["dotnet", "Saakh.Api.dll"]
