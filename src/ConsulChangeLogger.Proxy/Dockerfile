FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj src/ConsulChangeLogger.Proxy/
RUN dotnet restore src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj
COPY src/ src/
RUN dotnet publish src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        libldap-2.5-0 \
        libsasl2-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /var/lib/consul-change-logger/outbox \
    && chown -R $APP_UID:0 /var/lib/consul-change-logger
USER $APP_UID
ENTRYPOINT ["dotnet", "ConsulChangeLogger.Proxy.dll"]
