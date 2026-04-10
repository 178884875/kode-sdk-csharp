# ─────────────────────────────────────────────────────────────────────────────
# KodaClaw — multi-stage Docker build
# Build context: repo root (kode-sdk-csharp/)
#   docker build -f products/KodaClaw/Dockerfile -t kodaclaw:latest .
#
# Stage 1: build web UI (Node.js)
# Stage 2: build Gateway (.NET 10) — needs parent SDK projects in src/
# Stage 3: runtime image
# ─────────────────────────────────────────────────────────────────────────────

# ── Stage 1: Build web UI ─────────────────────────────────────────────────────
FROM node:20-alpine AS web-builder
WORKDIR /app/web
COPY products/KodaClaw/apps/kodaclaw-web/package*.json ./
RUN npm ci --prefer-offline
COPY products/KodaClaw/apps/kodaclaw-web/ ./
# Skip typecheck in Docker; build only
RUN npx vite build

# ── Stage 2: Build .NET Gateway ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS gateway-builder
WORKDIR /repo

# Root-level build props (Directory.Build.props imports chain, Packages.props for CPM)
COPY Directory.Build.props Directory.Packages.props ./

# Parent SDK source projects referenced by KodaClaw.Runtime
COPY src/ src/

# KodaClaw product (solution, build props, all source modules)
COPY products/KodaClaw/Directory.Build.props   products/KodaClaw/
COPY products/KodaClaw/KodaClaw.sln            products/KodaClaw/
COPY products/KodaClaw/src/                    products/KodaClaw/src/

# Restore (separate layer for caching — only re-runs when .csproj files change)
RUN dotnet restore products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj

# Build and publish
RUN dotnet publish products/KodaClaw/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj \
    --configuration Release \
    --no-restore \
    --output /app/gateway

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install common utilities needed by Agent tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    # network & download
    curl wget ca-certificates \
    # archive
    unzip zip tar \
    # text processing
    jq \
    # version control
    git \
    # python (for markitdown and other pip tools)
    python3 python3-pip python3-venv \
    && rm -rf /var/lib/apt/lists/*

# Install markitdown globally (web-tools web-reader PDF/Office conversion)
RUN pip3 install --no-cache-dir markitdown --break-system-packages

# Copy built Gateway
COPY --from=gateway-builder /app/gateway ./

# Copy built-in skills (static SKILL.md files referenced at runtime)
COPY --from=gateway-builder /repo/products/KodaClaw/src/KodaClaw.Gateway/skills/ ./skills/

# Copy built web UI into wwwroot/ so Gateway serves it as static files
COPY --from=web-builder /app/web/dist ./wwwroot/

# Workspace directory (mounted as a volume in production)
RUN mkdir -p /workspace

# Expose port
EXPOSE 5076

# Healthcheck
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -sf http://localhost:5076/healthz || exit 1

ENV ASPNETCORE_ENVIRONMENT=Production
ENV KODACLAW_GATEWAY_URL=http://0.0.0.0:5076
ENV KODACLAW_WORKSPACE_ROOT=/workspace

ENTRYPOINT ["./KodaClaw.Gateway"]
