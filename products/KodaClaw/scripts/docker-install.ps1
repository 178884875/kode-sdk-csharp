# KodaClaw Docker quick-install script (Windows PowerShell)
# Usage: .\scripts\docker-install.ps1
#
# Prerequisites: Docker Desktop for Windows must be installed and running.

param(
    [string]$Port = "5076",
    [string]$ImageName = "kodaclaw:latest",
    [string]$ComposeFile = "docker-compose.yml"
)

$ErrorActionPreference = "Stop"

Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║         KodaClaw — Docker Installer           ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── Prerequisites check ───────────────────────────────────────────────────────
if (-not (Get-Command "docker" -ErrorAction SilentlyContinue)) {
    Write-Error "Docker is not installed. Install Docker Desktop from https://docs.docker.com/get-docker/"
    exit 1
}

try {
    docker info 2>&1 | Out-Null
} catch {
    Write-Error "Docker daemon is not running. Start Docker Desktop and retry."
    exit 1
}

# ── Pull or build image ───────────────────────────────────────────────────────
if (Test-Path $ComposeFile) {
    Write-Host "→ Found $ComposeFile, using Docker Compose..." -ForegroundColor Green
    try {
        docker compose -f $ComposeFile pull 2>&1 | Out-Null
    } catch {
        # no registry image — will build locally
    }
    docker compose -f $ComposeFile up -d --build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} else {
    Write-Host "→ No docker-compose.yml found; pulling $ImageName..." -ForegroundColor Green
    try {
        docker pull $ImageName 2>&1 | Out-Null
    } catch {
        Write-Host "  (building locally...)" -ForegroundColor Gray
        docker build -t $ImageName .
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    docker run -d `
        --name kodaclaw `
        -p "${Port}:5076" `
        -v kodaclaw-workspace:/workspace `
        -e KODACLAW_GATEWAY_URL="http://0.0.0.0:5076" `
        -e KODACLAW_WORKSPACE_ROOT="/workspace" `
        --restart unless-stopped `
        $ImageName
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ""
Write-Host "✓ KodaClaw is starting at http://localhost:$Port" -ForegroundColor Green

# ── Wait for health ───────────────────────────────────────────────────────────
Write-Host "→ Waiting for gateway to become healthy..."
$maxRetries = 30
for ($i = 1; $i -le $maxRetries; $i++) {
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:$Port/healthz" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) {
            Write-Host "✓ Gateway is healthy." -ForegroundColor Green
            break
        }
    } catch {}
    if ($i -eq $maxRetries) {
        Write-Error "Gateway did not become healthy within ${maxRetries}s. Run: docker compose logs -f"
        exit 1
    }
    Start-Sleep -Seconds 1
}

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  KodaClaw is running!                                            ║" -ForegroundColor Cyan
Write-Host "║                                                                  ║" -ForegroundColor Cyan
Write-Host "║  Open: http://localhost:$Port                                    ║" -ForegroundColor Cyan
Write-Host "║                                                                  ║" -ForegroundColor Cyan
Write-Host "║  No API key? Go to: http://localhost:$Port/setup                 ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
