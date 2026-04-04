#!/bin/sh
if [ "$ASPNETCORE_ENVIRONMENT" = "Staging" ]; then
  cp /usr/share/nginx/html/appsettings.Staging.json /usr/share/nginx/html/appsettings.json
fi
