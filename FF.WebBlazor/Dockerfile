# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY FantasyFootball.sln .
COPY FF.API/FF.API.csproj                       FF.API/
COPY FF.Application/FF.Application.csproj       FF.Application/
COPY FF.Domain/FF.Domain.csproj                 FF.Domain/
COPY FF.Infrastructure/FF.Infrastructure.csproj FF.Infrastructure/
COPY FF.SharedKernel/FF.SharedKernel.csproj     FF.SharedKernel/
COPY FF.WebBlazor/FF.WebBlazor.csproj           FF.WebBlazor/

RUN dotnet restore FF.WebBlazor/FF.WebBlazor.csproj

COPY . .
RUN dotnet publish FF.WebBlazor/FF.WebBlazor.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM nginx:alpine AS runtime

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY FF.WebBlazor/nginx.conf /etc/nginx/nginx.conf
COPY FF.WebBlazor/docker-entrypoint.sh /docker-entrypoint.d/40-swap-appsettings.sh
RUN chmod +x /docker-entrypoint.d/40-swap-appsettings.sh

EXPOSE 64236
ENTRYPOINT ["/docker-entrypoint.sh"]

# Copy published Blazor static files to nginx html directory
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

# nginx config for Blazor WASM — handles deep-link routing
COPY FF.WebBlazor/nginx.conf /etc/nginx/nginx.conf

EXPOSE 64236