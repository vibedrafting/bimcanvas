FROM node:20-bullseye-slim AS web-build
WORKDIR /src/BIMCanvas.Web

COPY BIMCanvas.Web/package.json BIMCanvas.Web/package-lock.json ./
RUN npm ci

COPY BIMCanvas.Web/ ./
RUN npm run build


FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS server-build
WORKDIR /src

COPY BIMCanvas.sln ./
COPY BIMCanvas.Core/BIMCanvas.Core.csproj BIMCanvas.Core/
COPY BIMCanvas.Server/BIMCanvas.Server.csproj BIMCanvas.Server/

RUN dotnet restore BIMCanvas.Server/BIMCanvas.Server.csproj \
    --disable-parallel \
    -p:RestoreTimeout=600

COPY BIMCanvas.Core/ BIMCanvas.Core/
COPY BIMCanvas.Server/ BIMCanvas.Server/
COPY demos/ demos/

RUN dotnet publish BIMCanvas.Server/BIMCanvas.Server.csproj \
    -c Release \
    -o /out/server \
    --no-restore


FROM node:20-bullseye-slim AS playwright-build
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
WORKDIR /out/server

COPY --from=server-build /out/server/ ./
RUN test -f .playwright/package/cli.js \
    && node .playwright/package/cli.js install chromium


FROM python:3.10-slim AS agent-build
ENV PIP_NO_CACHE_DIR=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /src/BIMCanvas.Agent
COPY BIMCanvas.Agent/pyproject.toml BIMCanvas.Agent/README.md ./
COPY BIMCanvas.Agent/src ./src

RUN python -m venv /opt/venv \
    && /opt/venv/bin/pip install --upgrade pip setuptools wheel \
    && /opt/venv/bin/pip install .


FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS server-runtime
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PLAYWRIGHT_BROWSERS_PATH=/root/.local/share/ms-playwright \
    BIMCANVAS_WEB_DIST=/app/BIMCanvas.Web/dist

RUN packages='\
    bash \
    ca-certificates \
    curl \
    git \
    lsof \
    python3 \
    fonts-liberation \
    fonts-noto-cjk \
    fonts-noto-color-emoji \
    libasound2 \
    libatk-bridge2.0-0 \
    libatk1.0-0 \
    libcairo2 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libgbm1 \
    libglib2.0-0 \
    libgtk-3-0 \
    libicu70 \
    libnspr4 \
    libnss3 \
    libpango-1.0-0 \
    libssl3 \
    libx11-6 \
    libx11-xcb1 \
    libxcb1 \
    libxcomposite1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxkbcommon0 \
    libxrandr2 \
    libxshmfence1 \
    tzdata \
    zlib1g'; \
    apt-get update -o Acquire::Retries=5 \
    && apt-get install -y --no-install-recommends $packages \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=server-build /out/server/ /app/server/
COPY --from=web-build /src/BIMCanvas.Web/dist/ /app/BIMCanvas.Web/dist/
COPY --from=playwright-build /ms-playwright/ /root/.local/share/ms-playwright/
COPY deploy/start.sh /app/deploy/start.sh

RUN chmod +x /app/deploy/start.sh \
    && mkdir -p /data "$PLAYWRIGHT_BROWSERS_PATH"

EXPOSE 5000 3456
VOLUME ["/data"]
ENTRYPOINT ["/app/deploy/start.sh"]


FROM python:3.10-slim AS agent-runtime
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    BIMCANVAS_HOME=/data \
    PATH=/opt/venv/bin:${PATH}

RUN apt-get update -o Acquire::Retries=5 \
    && apt-get install -y --no-install-recommends \
        bash \
        ca-certificates \
        curl \
        git \
        ripgrep \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=agent-build /opt/venv/ /opt/venv/
COPY deploy/start-agent.sh /app/deploy/start-agent.sh

RUN chmod +x /app/deploy/start-agent.sh \
    && mkdir -p /data

EXPOSE 8865
VOLUME ["/data"]
ENTRYPOINT ["/app/deploy/start-agent.sh"]
