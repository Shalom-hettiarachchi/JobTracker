# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first so this layer is cached until the package list changes.
COPY src/JobTracker/JobTracker.csproj src/JobTracker/
RUN dotnet restore src/JobTracker/JobTracker.csproj

COPY src/JobTracker/ src/JobTracker/
RUN dotnet publish src/JobTracker/JobTracker.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# The container sits behind a TLS-terminating proxy (Azure Container Apps), so trust its
# X-Forwarded-* headers to know the original request was HTTPS.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "JobTracker.dll"]
