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
        libldap2 \
        libsasl2-2 \
    && ln -sf /usr/lib/x86_64-linux-gnu/libldap.so.2 /usr/lib/x86_64-linux-gnu/libldap-2.5.so.0 \
    && ln -sf /lib/x86_64-linux-gnu/libldap.so.2 /lib/x86_64-linux-gnu/libldap-2.5.so.0 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /var/lib/consul-change-logger/outbox \
    && chown -R $APP_UID:0 /var/lib/consul-change-logger
USER $APP_UID
ENTRYPOINT ["dotnet", "ConsulChangeLogger.Proxy.dll"]
