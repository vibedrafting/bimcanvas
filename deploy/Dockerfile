FROM ubuntu:22.04

SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    PYTHONUNBUFFERED=1 \
    PLAYWRIGHT_BROWSERS_PATH=/root/.local/share/ms-playwright \
    AGENT_VENV_BIN=/app/BIMCanvas.Agent/venv/bin \
    PATH=/app/BIMCanvas.Agent/venv/bin:/root/.dotnet/tools:/usr/share/dotnet:${PATH}

RUN packages='\
    ca-certificates \
    curl \
    git \
    gnupg \
    lsof \
    python3 \
    python3-pip \
    python3-venv \
    python-is-python3 \
    wget \
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
    for attempt in 1 2 3; do \
        apt-get update -o Acquire::Retries=5 \
        && apt-get install -y --fix-missing --no-install-recommends $packages \
        && break; \
        if [ "$attempt" -eq 3 ]; then exit 1; fi; \
        echo "Retrying base apt install ($attempt/3)"; \
        apt-get clean; \
        rm -rf /var/lib/apt/lists/*; \
        sleep 5; \
    done; \
    apt-get clean; \
    rm -rf /var/lib/apt/lists/*

RUN wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /tmp/dotnet-install.sh

RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && for attempt in 1 2 3; do \
        apt-get update -o Acquire::Retries=5 \
        && apt-get install -y --fix-missing --no-install-recommends nodejs \
        && break; \
        if [ "$attempt" -eq 3 ]; then exit 1; fi; \
        echo "Retrying nodejs apt install ($attempt/3)"; \
        apt-get clean; \
        rm -rf /var/lib/apt/lists/*; \
        sleep 5; \
    done \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

RUN npm install -g @anthropic-ai/claude-code claude-code-router \
    && claude --version \
    && ccr --version

WORKDIR /app
COPY . /app

RUN dotnet restore BIMCanvas.Server \
    && dotnet build BIMCanvas.Server -c Release --no-restore

RUN cd BIMCanvas.Web && npm ci && npm run build

RUN cd BIMCanvas.Agent \
    && python -m venv venv \
    && ./venv/bin/pip install --no-cache-dir --upgrade pip \
    && ./venv/bin/pip install --no-cache-dir -e . \
    && python -c "import claude_agent_sdk"

RUN PLAYWRIGHT_CLI=/app/BIMCanvas.Server/bin/Release/net8.0/.playwright/package/cli.js \
    && test -f "$PLAYWRIGHT_CLI" \
    && node "$PLAYWRIGHT_CLI" install chromium

RUN chmod +x /app/deploy/start.sh \
    && mkdir -p /data "$PLAYWRIGHT_BROWSERS_PATH"

EXPOSE 5000 8865
VOLUME ["/data"]

ENTRYPOINT ["/app/deploy/start.sh"]
