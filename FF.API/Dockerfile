# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY FantasyFootball.sln .
COPY FF.API/FF.API.csproj                       FF.API/
COPY FF.Application/FF.Application.csproj       FF.Application/
COPY FF.Domain/FF.Domain.csproj                 FF.Domain/
COPY FF.Infrastructure/FF.Infrastructure.csproj FF.Infrastructure/
COPY FF.SharedKernel/FF.SharedKernel.csproj     FF.SharedKernel/
COPY FF.WebBlazor/FF.WebBlazor.csproj           FF.WebBlazor/
COPY FF.Tests/FF.Tests.csproj                   FF.Tests/

# Restore — cached unless .csproj files change
RUN dotnet restore FF.API/FF.API.csproj

# Copy everything else and build
COPY . .
RUN dotnet publish FF.API/FF.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 64234

ENTRYPOINT ["dotnet", "FF.API.dll"]