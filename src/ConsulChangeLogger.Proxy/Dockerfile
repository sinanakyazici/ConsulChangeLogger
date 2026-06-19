FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG APP_VERSION=0.0.0-local
COPY src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj src/ConsulChangeLogger.Proxy/
RUN dotnet restore src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj
COPY src/ src/
RUN dotnet publish src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj -c Release -o /app/publish --no-restore /p:InformationalVersion=$APP_VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0-distroless
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "ConsulChangeLogger.Proxy.dll"]
