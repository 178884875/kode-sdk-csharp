# KodaClaw Docker quick-install script (Windows PowerShell)
#
# One-liner install (run in PowerShell as Administrator if needed):
#   irm https://raw.githubusercontent.com/vanzheng/kode-sdk-csharp/main/products/KodaClaw/scripts/docker-install.ps1 | iex
#
# With custom directory:
#   & ([scriptblock]::Create((irm <url>))) -Dir "C:\kodaclaw"
#
# With Soul package:
#   & ([scriptblock]::Create((irm <url>))) -Soul "https://example.com/soul.zip"
#
# Prerequisites: Docker Desktop for Windows must be installed and running.
# Download: https://docs.docker.com/desktop/install/windows-install/

param(
    [string]$Port        = "5076",
    [string]$Dir         = "",
    [string]$ComposeUrl  = "https://raw.githubusercontent.com/vanzheng/kode-sdk-csharp/main/products/KodaClaw/docker-compose.prod.yml",
    [string]$Soul        = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ComposeFilename = "docker-compose.prod.yml"

Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║         KodaClaw — Docker Installer           ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── Helpers ───────────────────────────────────────────────────────────────────
function Invoke-ContainerExec {
    param([string[]]$Command)
    docker compose -f $ComposeFile exec -T kodaclaw @Command
}

function Invoke-ContainerCp {
    param([string]$Src, [string]$Dest)
    docker compose -f $ComposeFile cp $Src "kodaclaw:$Dest"
}

# ── Step 1: Check Docker is installed ────────────────────────────────────────
if (-not (Get-Command "docker" -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "Docker is not installed." -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Docker Desktop for Windows and retry:" -ForegroundColor Yellow
    Write-Host "  https://docs.docker.com/desktop/install/windows-install/" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "After installation, start Docker Desktop and re-run this script."
    exit 1
}

# ── Step 2: Check Docker daemon is running ───────────────────────────────────
try {
    docker info 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "not running" }
} catch {
    Write-Host ""
    Write-Host "Docker daemon is not running." -ForegroundColor Red
    Write-Host "Please start Docker Desktop and wait for it to finish loading, then retry." -ForegroundColor Yellow
    exit 1
}

# ── Step 3: Resolve install directory ─────────────────────────────────────────
$DefaultDir = Join-Path $env:USERPROFILE "kodaclaw"

if ([string]::IsNullOrWhiteSpace($Dir)) {
    Write-Host "Where should KodaClaw be installed?"
    Write-Host "  Press Enter to use the default: $DefaultDir"
    Write-Host ""
    $input = Read-Host "  Install directory"
    $Dir = if ([string]::IsNullOrWhiteSpace($input)) { $DefaultDir } else { $input.Trim() }
    Write-Host ""
}

# Expand environment variables (e.g. %USERPROFILE%)
$Dir = [System.Environment]::ExpandEnvironmentVariables($Dir)
$Dir = [System.IO.Path]::GetFullPath($Dir)
$ComposeFile = Join-Path $Dir $ComposeFilename

# Detect existing installation
$ExistingInstall = Test-Path $ComposeFile
$ContainerRunning = $false
if ($ExistingInstall) {
    $running = docker compose -f $ComposeFile ps --status running 2>&1
    $ContainerRunning = ($running -match "kodaclaw")
}

if ($ExistingInstall) {
    Write-Host "  ⚠  Existing installation detected at: $Dir" -ForegroundColor Yellow
    if ($ContainerRunning) {
        Write-Host "     Container is currently running." -ForegroundColor Gray
        Write-Host ""
        $confirm = Read-Host "  Upgrade and restart KodaClaw? [Y/n]"
    } else {
        Write-Host "     Container is not running." -ForegroundColor Gray
        Write-Host ""
        $confirm = Read-Host "  Re-install / restart KodaClaw here? [Y/n]"
    }
    if ($confirm -ne "" -and $confirm -notmatch "^[Yy]") {
        Write-Host "Aborted." -ForegroundColor Yellow
        exit 0
    }
} else {
    Write-Host "  Install directory: $Dir" -ForegroundColor Gray
}
Write-Host ""

# Validate parent directory exists
$ParentDir = Split-Path $Dir -Parent
if (-not (Test-Path $ParentDir)) {
    Write-Host "Error: Parent directory does not exist: $ParentDir" -ForegroundColor Red
    exit 1
}

# Create install directory if needed
if (-not (Test-Path $Dir)) {
    Write-Host "→ Creating directory: $Dir" -ForegroundColor Green
    New-Item -ItemType Directory -Path $Dir | Out-Null
    Write-Host "✓ Directory created." -ForegroundColor Green
}

# Test write access
try {
    $testFile = Join-Path $Dir ".write-test"
    [System.IO.File]::WriteAllText($testFile, "")
    Remove-Item $testFile -ErrorAction SilentlyContinue
} catch {
    Write-Host "Error: Directory is not writable: $Dir" -ForegroundColor Red
    exit 1
}

Set-Location $Dir

# Pre-create data directories so bind mounts work on first run
New-Item -ItemType Directory -Path (Join-Path $Dir "data\searxng") -Force | Out-Null

# ── Step 4: Ensure docker-compose.prod.yml ───────────────────────────────────
if (-not (Test-Path $ComposeFile)) {
    Write-Host "→ Downloading $ComposeFilename..." -ForegroundColor Green
    try {
        Invoke-WebRequest -Uri $ComposeUrl -OutFile $ComposeFile -UseBasicParsing
        Write-Host "✓ $ComposeFilename downloaded." -ForegroundColor Green
    } catch {
        Write-Error "Failed to download $ComposeFilename from:`n  $ComposeUrl`n$_"
        exit 1
    }
} elseif ($ExistingInstall) {
    Write-Host "→ Refreshing $ComposeFilename..." -ForegroundColor Green
    try {
        $tmpCompose = "$ComposeFile.tmp"
        Invoke-WebRequest -Uri $ComposeUrl -OutFile $tmpCompose -UseBasicParsing
        Move-Item -Path $tmpCompose -Destination $ComposeFile -Force
        Write-Host "✓ $ComposeFilename updated." -ForegroundColor Green
    } catch {
        Remove-Item "$ComposeFile.tmp" -ErrorAction SilentlyContinue
        Write-Host "  Warning: Could not refresh $ComposeFilename — using existing file." -ForegroundColor Yellow
    }
}

# ── Step 5: Pull and start ────────────────────────────────────────────────────
Write-Host "→ Starting KodaClaw..." -ForegroundColor Green
try { docker compose -f $ComposeFile pull 2>&1 | Out-Null } catch {}
docker compose -f $ComposeFile up -d
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "✓ KodaClaw is starting at http://localhost:$Port" -ForegroundColor Green

# ── Step 6: Wait for health ───────────────────────────────────────────────────
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
        Write-Error "Gateway did not become healthy within ${maxRetries}s. Run: docker compose -f $ComposeFile logs -f"
        exit 1
    }
    Start-Sleep -Seconds 1
}

# ── Step 7: Apply Soul package (optional) ─────────────────────────────────────
if ($Soul -ne "") {
    Write-Host ""
    Write-Host "→ Applying Soul package: $Soul" -ForegroundColor Green

    $tmpDir = $null
    $soulDir = $null

    try {
        if ($Soul -match "^https?://") {
            $tmpDir = Join-Path $env:TEMP ("kodaclaw-soul-" + [System.IO.Path]::GetRandomFileName())
            New-Item -ItemType Directory -Path $tmpDir | Out-Null
            $zipPath = Join-Path $tmpDir "soul.zip"
            Write-Host "  Downloading..." -ForegroundColor Gray
            try {
                Invoke-WebRequest -Uri $Soul -OutFile $zipPath -UseBasicParsing
            } catch {
                Write-Error "  Error: Failed to download from '$Soul': $_"; exit 1
            }
            $extractDir = Join-Path $tmpDir "extracted"
            try { Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force }
            catch { Write-Error "  Error: Not a valid zip archive."; exit 1 }
            $soulMd = Get-ChildItem -Path $extractDir -Recurse -Filter "SOUL.md" -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($soulMd) { $soulDir = $soulMd.DirectoryName }
        }
        elseif ($Soul -match "\.zip$") {
            $tmpDir = Join-Path $env:TEMP ("kodaclaw-soul-" + [System.IO.Path]::GetRandomFileName())
            New-Item -ItemType Directory -Path $tmpDir | Out-Null
            $extractDir = Join-Path $tmpDir "extracted"
            try { Expand-Archive -Path $Soul -DestinationPath $extractDir -Force }
            catch { Write-Error "  Error: '$Soul' is not a valid zip archive."; exit 1 }
            $soulMd = Get-ChildItem -Path $extractDir -Recurse -Filter "SOUL.md" -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($soulMd) { $soulDir = $soulMd.DirectoryName }
        }
        else {
            $soulDir = $Soul.TrimEnd('\', '/')
        }

        if (-not $soulDir -or -not (Test-Path $soulDir -PathType Container)) {
            Write-Error "  Error: Could not locate soul files in '$Soul'."; exit 1
        }

        $missing = @()
        foreach ($req in @("IDENTITY.md", "SOUL.md")) {
            $p = Join-Path $soulDir $req
            if (-not (Test-Path $p) -or (Get-Item $p).Length -eq 0) { $missing += $req }
        }
        if ($missing.Count -gt 0) {
            Write-Error "  Error: Soul package missing required files: $($missing -join ', ')"; exit 1
        }

        $illegal = Get-ChildItem -Path $soulDir -File | Where-Object { $_.Extension -ne ".md" }
        if ($illegal) {
            Write-Error "  Error: Soul package contains non-.md files:`n$($illegal.FullName -join "`n")"; exit 1
        }

        $doApply = $Force.IsPresent
        if (-not $doApply) {
            Invoke-ContainerExec @("test", "-f", "/data/workspace/workspace/IDENTITY.md") 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Warning: Soul files already exist. Use -Force to overwrite. Skipping." -ForegroundColor Yellow
            } else {
                $doApply = $true
            }
        }

        if ($doApply) {
            $containerTarget = "/data/workspace/workspace"
            $copied = 0
            foreach ($f in @("IDENTITY.md", "SOUL.md", "AGENTS.md", "ONTOLOGY.md")) {
                $src = Join-Path $soulDir $f
                if (Test-Path $src) {
                    Invoke-ContainerCp -Src $src -Dest "$containerTarget/$f"
                    if ($LASTEXITCODE -ne 0) { Write-Error "  Error: Failed to copy $f."; exit 1 }
                    Write-Host "  ✓ $f" -ForegroundColor Green
                    $copied++
                }
            }
            Write-Host "✓ Soul package applied ($copied files)." -ForegroundColor Green
        }
    }
    finally {
        if ($tmpDir -and (Test-Path $tmpDir)) {
            Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
        }
    }
}

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  KodaClaw is running!                                            ║" -ForegroundColor Cyan
Write-Host "║                                                                  ║" -ForegroundColor Cyan
Write-Host "║  Open:  http://localhost:$Port                                   ║" -ForegroundColor Cyan
Write-Host "║  Files: $Dir" -ForegroundColor Cyan
Write-Host "║                                                                  ║" -ForegroundColor Cyan
Write-Host "║  No API key? Complete setup at: http://localhost:$Port/setup     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
