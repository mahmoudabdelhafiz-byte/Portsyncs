# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PortSyncs.Api/PortSyncs.Api.csproj src/PortSyncs.Api/
RUN dotnet restore src/PortSyncs.Api/PortSyncs.Api.csproj
COPY src/ src/
RUN dotnet publish src/PortSyncs.Api/PortSyncs.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN mkdir -p /data/files && chown -R app:app /data
USER app
ENTRYPOINT ["dotnet", "PortSyncs.Api.dll"]
