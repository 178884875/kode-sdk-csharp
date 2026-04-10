#!/usr/bin/env bash
# KodaClaw Docker quick-install script (Linux / macOS)
# Usage: curl -fsSL https://... | bash
#        or: ./scripts/docker-install.sh
set -euo pipefail

COMPOSE_FILE="${KODACLAW_COMPOSE_FILE:-docker-compose.yml}"
IMAGE_NAME="${KODACLAW_IMAGE:-kodaclaw:latest}"
PORT="${KODACLAW_PORT:-5076}"

echo "╔══════════════════════════════════════════════╗"
echo "║         KodaClaw — Docker Installer           ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

# ── Prerequisites check ───────────────────────────────────────────────────────
if ! command -v docker &>/dev/null; then
    echo "Error: Docker is not installed. Please install Docker first:"
    echo "  https://docs.docker.com/get-docker/"
    exit 1
fi

if ! docker info &>/dev/null; then
    echo "Error: Docker daemon is not running. Start Docker and retry."
    exit 1
fi

HAS_COMPOSE=false
if docker compose version &>/dev/null 2>&1; then
    HAS_COMPOSE=true
fi

# ── Pull or build image ───────────────────────────────────────────────────────
if [[ -f "$COMPOSE_FILE" ]]; then
    echo "→ Found $COMPOSE_FILE, using Docker Compose..."
    if $HAS_COMPOSE; then
        docker compose -f "$COMPOSE_FILE" pull 2>/dev/null || true
        docker compose -f "$COMPOSE_FILE" up -d --build
    else
        docker-compose -f "$COMPOSE_FILE" pull 2>/dev/null || true
        docker-compose -f "$COMPOSE_FILE" up -d --build
    fi
    echo ""
    echo "✓ KodaClaw is starting at http://localhost:${PORT}"
else
    echo "→ No docker-compose.yml found; pulling image $IMAGE_NAME..."
    docker pull "$IMAGE_NAME" 2>/dev/null || {
        echo "  (image not in registry — building locally...)"
        docker build -t "$IMAGE_NAME" .
    }
    docker run -d \
        --name kodaclaw \
        -p "${PORT}:5076" \
        -v kodaclaw-workspace:/workspace \
        -e KODACLAW_GATEWAY_URL="http://0.0.0.0:5076" \
        -e KODACLAW_WORKSPACE_ROOT="/workspace" \
        --restart unless-stopped \
        "$IMAGE_NAME"
    echo ""
    echo "✓ KodaClaw is starting at http://localhost:${PORT}"
fi

# ── Wait for health ───────────────────────────────────────────────────────────
echo "→ Waiting for gateway to become healthy..."
RETRIES=30
for i in $(seq 1 $RETRIES); do
    if curl -sf "http://localhost:${PORT}/healthz" &>/dev/null; then
        echo "✓ Gateway is healthy."
        break
    fi
    if [[ $i -eq $RETRIES ]]; then
        echo "  Gateway did not become healthy within ${RETRIES}s. Check logs:"
        echo "  docker compose logs -f  (or: docker logs kodaclaw)"
        exit 1
    fi
    sleep 1
done

# ── Done ──────────────────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════════════════╗"
echo "║  KodaClaw is running!                                            ║"
echo "║                                                                  ║"
echo "║  Open: http://localhost:${PORT}                                    ║"
echo "║                                                                  ║"
echo "║  No API key? Complete setup at: http://localhost:${PORT}/setup     ║"
echo "╚══════════════════════════════════════════════════════════════════╝"
