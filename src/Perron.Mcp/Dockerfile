# Build context: repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src

# Restore in its own layer: only reruns when project files or central props change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Perron.Transport/Perron.Transport.csproj src/Perron.Transport/
COPY src/Perron.Mcp/Perron.Mcp.csproj src/Perron.Mcp/
RUN dotnet restore src/Perron.Mcp/Perron.Mcp.csproj

COPY src/Perron.Transport/ src/Perron.Transport/
COPY src/Perron.Mcp/ src/Perron.Mcp/
RUN dotnet publish src/Perron.Mcp/Perron.Mcp.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Non-root user provided by the .NET images (UID 1654); listens on ASPNETCORE_HTTP_PORTS=8080.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Perron.Mcp.dll"]
