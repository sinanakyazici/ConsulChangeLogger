FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ConsulChangeLogger.Core/ConsulChangeLogger.Core.csproj src/ConsulChangeLogger.Core/
COPY src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj src/ConsulChangeLogger.Proxy/
RUN dotnet restore src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj
COPY src/ src/
RUN dotnet publish src/ConsulChangeLogger.Proxy/ConsulChangeLogger.Proxy.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ConsulChangeLogger.Proxy.dll"]
