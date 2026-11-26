# Build context: repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Perron.Transport/Perron.Transport.csproj src/Perron.Transport/
COPY src/Perron.Bench/Perron.Bench.csproj src/Perron.Bench/
RUN dotnet restore src/Perron.Bench/Perron.Bench.csproj

COPY src/Perron.Transport/ src/Perron.Transport/
COPY src/Perron.Bench/ src/Perron.Bench/
RUN dotnet publish src/Perron.Bench/Perron.Bench.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# Pinned uv (plan.md, decision log: Python servers in the bench image).
FROM ghcr.io/astral-sh/uv:0.13.7 AS uv

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# Third-party Python MCP servers (stdio), installed at build time with every dependency pinned by version and hash.
# Nothing is downloaded when a server starts. One virtual environment per server under /opt/mcp/<id>.
COPY --from=uv /uv /usr/local/bin/uv
ENV UV_PYTHON_INSTALL_DIR=/opt/python \
    UV_PYTHON_PREFERENCE=only-managed \
    UV_NO_CACHE=1
RUN uv python install 3.12.16
COPY bench/python/ /opt/mcp/requirements/
RUN for server in spt stm; do \
        uv venv --python 3.12.16 "/opt/mcp/$server" \
        && uv pip install --python "/opt/mcp/$server" --require-hashes --no-deps -r "/opt/mcp/requirements/$server.txt" \
        || exit 1; \
    done

# Commit of this build for the run manifest (AK-8.2); docker-compose.yml passes GIT_SHA.
ARG GIT_SHA=unknown
ENV GIT_SHA=$GIT_SHA

WORKDIR /app
COPY --from=build /app .
# Server registry and methodology (BENCH_DIR defaults to ./bench). Task sets and results are not baked in:
# docker-compose.yml mounts ./bench over /app/bench for bench run.
COPY bench/servers.json bench/bench.json bench/

USER $APP_UID
ENTRYPOINT ["dotnet", "Perron.Bench.dll"]
