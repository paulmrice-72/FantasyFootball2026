#!/bin/sh
if [ "$ASPNETCORE_ENVIRONMENT" = "Staging" ]; then
  echo "Staging environment detected — swapping appsettings"
  cp /usr/share/nginx/html/appsettings.Staging.json \
     /usr/share/nginx/html/appsettings.json
fi
exec nginx -g "daemon off;"