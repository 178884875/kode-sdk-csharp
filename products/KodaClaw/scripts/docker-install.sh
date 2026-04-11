#!/usr/bin/env bash
# KodaClaw Docker quick-install script (Linux / macOS)
#
# One-liner install:
#   curl -fsSL https://raw.githubusercontent.com/vanzheng/kode-sdk-csharp/main/products/KodaClaw/scripts/docker-install.sh | bash
#
# With custom install directory:
#   curl -fsSL <script-url> | bash -s -- --dir /opt/kodaclaw
#
# With Soul package:
#   curl -fsSL <script-url> | bash -s -- --soul https://example.com/soul.zip
#
# On a blank Linux server this script will auto-install missing dependencies
# (curl, unzip, Docker) via the system package manager (apt / dnf / yum / apk).
set -euo pipefail

# ── Configuration ──────────────────────────────────────────────────────────────
COMPOSE_URL="${KODACLAW_COMPOSE_URL:-https://raw.githubusercontent.com/vanzheng/kode-sdk-csharp/main/products/KodaClaw/docker-compose.prod.yml}"
COMPOSE_FILENAME="docker-compose.prod.yml"
PORT="${KODACLAW_PORT:-5076}"
INSTALL_DIR="${KODACLAW_DIR:-}"
SOUL_SOURCE=""
FORCE_SOUL=false

# ── Parse arguments ────────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --dir)   INSTALL_DIR="$2"; shift 2;;
        --soul)  SOUL_SOURCE="$2"; shift 2;;
        --force) FORCE_SOUL=true; shift;;
        *)       shift;;
    esac
done

# ── Helpers ───────────────────────────────────────────────────────────────────
GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; GRAY='\033[0;90m'; NC='\033[0m'
info()    { echo -e "${GREEN}✓${NC} $*"; }
step()    { echo -e "→ $*"; }
warn()    { echo -e "${YELLOW}⚠${NC}  $*"; }
error()   { echo -e "${RED}✗${NC}  $*" >&2; exit 1; }

maybe_sudo() {
    if [[ $EUID -eq 0 ]]; then "$@"; else sudo "$@"; fi
}

# Read user input from /dev/tty — works with curl | bash
tty_read() {
    local prompt="$1" var_name="$2" default="${3:-}"
    if [[ -t 0 ]]; then
        read -r -p "$prompt" "$var_name"
    elif [[ -e /dev/tty ]]; then
        read -r -p "$prompt" "$var_name" </dev/tty
    else
        printf -v "$var_name" '%s' "$default"
    fi
}

confirm() {
    local prompt="$1" default="${2:-y}"
    local reply
    tty_read "$prompt" reply "$default"
    reply="${reply:-$default}"
    [[ "$reply" =~ ^[Yy] ]]
}

PKG_MGR=""
detect_pkg_manager() {
    if [[ -n "$PKG_MGR" ]]; then return; fi
    if   command -v apt-get &>/dev/null; then PKG_MGR="apt"
    elif command -v dnf     &>/dev/null; then PKG_MGR="dnf"
    elif command -v yum     &>/dev/null; then PKG_MGR="yum"
    elif command -v apk     &>/dev/null; then PKG_MGR="apk"
    else error "No supported package manager found (apt / dnf / yum / apk). Please install missing tools manually."; fi
}

ensure_cmd() {
    local cmd="$1" pkg="${2:-$1}"
    command -v "$cmd" &>/dev/null && return
    step "'$cmd' not found — installing $pkg..."
    detect_pkg_manager
    case "$PKG_MGR" in
        apt) maybe_sudo apt-get update -qq && maybe_sudo apt-get install -y -qq "$pkg" ;;
        dnf) maybe_sudo dnf install -y -q "$pkg" ;;
        yum) maybe_sudo yum install -y -q "$pkg" ;;
        apk) maybe_sudo apk add --no-cache "$pkg" ;;
    esac
    command -v "$cmd" &>/dev/null || error "Failed to install $pkg. Please install it manually."
    info "$pkg installed."
}

container_exec() { docker compose -f "$COMPOSE_FILE" exec -T kodaclaw "$@"; }
container_cp()   { docker compose -f "$COMPOSE_FILE" cp "$1" "kodaclaw:$2"; }

# ── Banner ────────────────────────────────────────────────────────────────────
echo ""
echo "  ██╗  ██╗ ██████╗ ██████╗  █████╗  ██████╗██╗      █████╗ ██╗    ██╗"
echo "  ██║ ██╔╝██╔═══██╗██╔══██╗██╔══██╗██╔════╝██║     ██╔══██╗██║    ██║"
echo "  █████╔╝ ██║   ██║██║  ██║███████║██║     ██║     ███████║██║ █╗ ██║"
echo "  ██╔═██╗ ██║   ██║██║  ██║██╔══██║██║     ██║     ██╔══██║██║███╗██║"
echo "  ██║  ██╗╚██████╔╝██████╔╝██║  ██║╚██████╗███████╗██║  ██║╚███╔███╔╝"
echo "  ╚═╝  ╚═╝ ╚═════╝ ╚═════╝ ╚═╝  ╚═╝ ╚═════╝╚══════╝╚═╝  ╚═╝ ╚══╝╚══╝"
echo ""
echo "  Your personal AI — running locally, always under your control."
echo ""

# ── Step 1: Ensure curl ───────────────────────────────────────────────────────
ensure_cmd curl

# ── Step 2: Resolve install directory ────────────────────────────────────────
DEFAULT_DIR="$HOME/kodaclaw"

if [[ -z "$INSTALL_DIR" ]]; then
    echo "  Where should KodaClaw be installed?"
    echo -e "  ${GRAY}All data (workspace, history, settings) will be stored here.${NC}"
    echo -e "  ${GRAY}Press Enter to use the default: $DEFAULT_DIR${NC}"
    echo ""
    tty_read "  Directory: " INSTALL_DIR "$DEFAULT_DIR"
    INSTALL_DIR="${INSTALL_DIR:-$DEFAULT_DIR}"
    echo ""
fi

# Expand ~ and resolve path
INSTALL_DIR="${INSTALL_DIR/#\~/$HOME}"
INSTALL_DIR="$(realpath -m "$INSTALL_DIR" 2>/dev/null || echo "$INSTALL_DIR")"
COMPOSE_FILE="$INSTALL_DIR/$COMPOSE_FILENAME"

# Detect existing installation
EXISTING_INSTALL=false
CONTAINER_RUNNING=false
if [[ -f "$COMPOSE_FILE" ]]; then
    EXISTING_INSTALL=true
    if docker compose -f "$COMPOSE_FILE" ps --status running 2>/dev/null | grep -q "kodaclaw"; then
        CONTAINER_RUNNING=true
    fi
fi

if $EXISTING_INSTALL; then
    warn "Existing installation found at: $INSTALL_DIR"
    if $CONTAINER_RUNNING; then
        echo -e "     ${GRAY}KodaClaw is currently running.${NC}"
        echo ""
        confirm "  Pull latest image and restart? [Y/n] " "y" || { echo "Aborted."; exit 0; }
    else
        echo -e "     ${GRAY}KodaClaw is not running.${NC}"
        echo ""
        confirm "  Start KodaClaw here? [Y/n] " "y" || { echo "Aborted."; exit 0; }
    fi
else
    echo -e "  ${GRAY}Install directory: $INSTALL_DIR${NC}"
fi
echo ""

# Validate directory
PARENT_DIR="$(dirname "$INSTALL_DIR")"
[[ -d "$PARENT_DIR" ]] || error "Parent directory does not exist: $PARENT_DIR"
[[ -d "$INSTALL_DIR" && ! -w "$INSTALL_DIR" ]] && error "Directory not writable: $INSTALL_DIR\n  Try: sudo chown \$USER \"$INSTALL_DIR\""

if [[ ! -d "$INSTALL_DIR" ]]; then
    step "Creating $INSTALL_DIR..."
    mkdir -p "$INSTALL_DIR"
    info "Directory created."
fi

cd "$INSTALL_DIR"
mkdir -p data/searxng   # pre-create for bind mount

# ── Step 3: Ensure Docker ─────────────────────────────────────────────────────
if ! command -v docker &>/dev/null; then
    step "Docker not found — installing via get.docker.com..."
    curl -fsSL https://get.docker.com | maybe_sudo sh
    command -v docker &>/dev/null || error "Docker installation failed.\n  Install manually: https://docs.docker.com/get-docker/"
    info "Docker installed."
    # Add current user to docker group so they don't need sudo next time
    if [[ $EUID -ne 0 ]] && command -v usermod &>/dev/null; then
        sudo usermod -aG docker "$USER" 2>/dev/null || true
        echo ""
        warn "You've been added to the 'docker' group."
        echo -e "     ${GRAY}Log out and back in (or run: newgrp docker) to use Docker without sudo.${NC}"
        echo ""
    fi
fi

# ── Step 4: Ensure Docker daemon is running ───────────────────────────────────
if ! docker info &>/dev/null 2>&1; then
    step "Docker daemon not running — attempting to start..."
    if command -v systemctl &>/dev/null; then
        maybe_sudo systemctl enable docker --now 2>/dev/null || true
    elif command -v service &>/dev/null; then
        maybe_sudo service docker start 2>/dev/null || true
    fi
    sleep 3
    if ! docker info &>/dev/null 2>&1; then
        if sudo docker info &>/dev/null 2>&1; then
            error "Docker requires sudo on this system.\n  Fix: sudo usermod -aG docker \$USER && newgrp docker"
        else
            error "Docker daemon is not running. Start it and retry."
        fi
    fi
    info "Docker daemon started."
fi

# ── Step 5: Get docker-compose.prod.yml ──────────────────────────────────────
if [[ ! -f "$COMPOSE_FILE" ]]; then
    step "Downloading $COMPOSE_FILENAME..."
    curl -fsSL "$COMPOSE_URL" -o "$COMPOSE_FILE" || error "Failed to download $COMPOSE_FILENAME.\n  Check your internet connection and retry."
    info "$COMPOSE_FILENAME ready."
elif $EXISTING_INSTALL; then
    step "Refreshing $COMPOSE_FILENAME..."
    if curl -fsSL "$COMPOSE_URL" -o "$COMPOSE_FILE.tmp" 2>/dev/null; then
        mv "$COMPOSE_FILE.tmp" "$COMPOSE_FILE"
        info "$COMPOSE_FILENAME updated."
    else
        rm -f "$COMPOSE_FILE.tmp"
        warn "Could not refresh $COMPOSE_FILENAME — using existing file."
    fi
fi

# ── Step 6: Pull latest image and start ──────────────────────────────────────
step "Pulling latest KodaClaw image..."
docker compose -f "$COMPOSE_FILE" pull 2>/dev/null || true
step "Starting KodaClaw..."
docker compose -f "$COMPOSE_FILE" up -d
echo ""

# ── Step 7: Wait for gateway to be ready ─────────────────────────────────────
step "Waiting for KodaClaw to start"
RETRIES=40
i=0
while [[ $i -lt $RETRIES ]]; do
    if curl -sf "http://localhost:${PORT}/healthz" &>/dev/null; then
        echo ""
        info "KodaClaw is ready."
        break
    fi
    printf "."
    sleep 1
    i=$((i + 1))
    if [[ $i -eq $RETRIES ]]; then
        echo ""
        warn "KodaClaw did not start within ${RETRIES}s."
        echo ""
        echo "  Check logs with:"
        echo "    docker compose -f $COMPOSE_FILE logs -f"
        exit 1
    fi
done

# ── Step 8: Apply Soul package (optional) ─────────────────────────────────────
if [[ -n "$SOUL_SOURCE" ]]; then
    echo ""
    step "Applying Soul package: $SOUL_SOURCE"

    TMP_DIR=""
    SOUL_DIR=""

    if [[ "$SOUL_SOURCE" =~ ^https?:// ]]; then
        ensure_cmd unzip
        TMP_DIR=$(mktemp -d)
        step "Downloading Soul package..."
        curl -fsSL "$SOUL_SOURCE" -o "$TMP_DIR/soul.zip" || { rm -rf "$TMP_DIR"; error "Download failed."; }
        unzip -q "$TMP_DIR/soul.zip" -d "$TMP_DIR/extracted" 2>/dev/null || { rm -rf "$TMP_DIR"; error "Not a valid zip archive."; }
        SOUL_DIR=$(find "$TMP_DIR/extracted" -maxdepth 2 -name "SOUL.md" 2>/dev/null | head -1 | xargs -I{} dirname {} 2>/dev/null || true)
    elif [[ "$SOUL_SOURCE" == *.zip ]]; then
        ensure_cmd unzip
        TMP_DIR=$(mktemp -d)
        unzip -q "$SOUL_SOURCE" -d "$TMP_DIR/extracted" 2>/dev/null || { rm -rf "$TMP_DIR"; error "Not a valid zip archive."; }
        SOUL_DIR=$(find "$TMP_DIR/extracted" -maxdepth 2 -name "SOUL.md" 2>/dev/null | head -1 | xargs -I{} dirname {} 2>/dev/null || true)
    else
        SOUL_DIR="${SOUL_SOURCE%/}"
    fi

    [[ -z "$SOUL_DIR" || ! -d "$SOUL_DIR" ]] && { [[ -n "$TMP_DIR" ]] && rm -rf "$TMP_DIR"; error "Could not locate soul files in '$SOUL_SOURCE'."; }

    # Validate required files
    MISSING=""
    [[ ! -s "$SOUL_DIR/IDENTITY.md" ]] && MISSING="$MISSING IDENTITY.md"
    [[ ! -s "$SOUL_DIR/SOUL.md" ]]     && MISSING="$MISSING SOUL.md"
    [[ -n "$MISSING" ]] && { [[ -n "$TMP_DIR" ]] && rm -rf "$TMP_DIR"; error "Soul package missing required files:$MISSING"; }

    # Only .md files allowed
    ILLEGAL=$(find "$SOUL_DIR" -maxdepth 1 -type f ! -name "*.md" 2>/dev/null || true)
    [[ -n "$ILLEGAL" ]] && { [[ -n "$TMP_DIR" ]] && rm -rf "$TMP_DIR"; error "Soul package contains non-.md files:\n$ILLEGAL"; }

    # Check if soul already exists
    if ! $FORCE_SOUL; then
        if container_exec test -f /data/workspace/workspace/IDENTITY.md 2>/dev/null; then
            warn "Soul files already exist. Use --force to overwrite. Skipping."
            [[ -n "$TMP_DIR" ]] && rm -rf "$TMP_DIR"
            SOUL_DIR=""
        else
            FORCE_SOUL=true
        fi
    fi

    if $FORCE_SOUL && [[ -n "$SOUL_DIR" ]]; then
        COPIED=0
        for f in IDENTITY.md SOUL.md AGENTS.md ONTOLOGY.md; do
            if [[ -f "$SOUL_DIR/$f" ]]; then
                container_cp "$SOUL_DIR/$f" "/data/workspace/workspace/$f"
                info "$f"
                COPIED=$((COPIED + 1))
            fi
        done
        info "Soul package applied ($COPIED files)."
    fi

    [[ -n "$TMP_DIR" ]] && rm -rf "$TMP_DIR"
fi

# ── Done ──────────────────────────────────────────────────────────────────────
echo ""
echo "┌─────────────────────────────────────────────────┐"
echo "│                                                 │"
echo "│   KodaClaw is running!                          │"
echo "│                                                 │"
printf "│   Open:  \033[0;36mhttp://localhost:%-5s\033[0m                  │\n" "${PORT}"
echo "│                                                 │"
printf "│   Data:  %-39s│\n" "$INSTALL_DIR/data"
echo "│                                                 │"
echo "│   No API key yet?                               │"
printf "│   Setup: \033[0;36mhttp://localhost:%-5s/setup\033[0m             │\n" "${PORT}"
echo "│                                                 │"
echo "└─────────────────────────────────────────────────┘"
echo ""
echo "  Useful commands:"
echo -e "  ${GRAY}Stop:    docker compose -f $COMPOSE_FILE down${NC}"
echo -e "  ${GRAY}Logs:    docker compose -f $COMPOSE_FILE logs -f${NC}"
echo -e "  ${GRAY}Update:  curl -fsSL <script-url> | bash${NC}"
echo ""
