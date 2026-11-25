# Build context: repository root (see docker-compose.yml).
# uv and the pinned third-party Python MCP servers are added in task 3.1.

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Perron.Transport/Perron.Transport.csproj src/Perron.Transport/
COPY src/Perron.Bench/Perron.Bench.csproj src/Perron.Bench/
RUN dotnet restore src/Perron.Bench/Perron.Bench.csproj

COPY src/Perron.Transport/ src/Perron.Transport/
COPY src/Perron.Bench/ src/Perron.Bench/
RUN dotnet publish src/Perron.Bench/Perron.Bench.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
ENTRYPOINT ["dotnet", "Perron.Bench.dll"]
