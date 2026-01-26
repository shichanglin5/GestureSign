# Quick Build UIAccess - Force rebuild and deploy
# This script forces MSBuild to rebuild and deploy, bypassing VS optimizations

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = Split-Path -Parent $ScriptDir

Write-Host "=== Quick UIAccess Build ===" -ForegroundColor Cyan
Write-Host ""

# Check admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script requires Administrator privileges" -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator" -ForegroundColor Yellow
    exit 1
}

# Step 1: Stop processes
Write-Host "[1/3] Stopping GestureSign processes..." -ForegroundColor Yellow
Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Write-Host "  Processes stopped" -ForegroundColor Green

# Step 2: Force rebuild main projects
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "[2/3] Force rebuilding main projects..." -ForegroundColor Yellow

    Push-Location $SolutionDir
    try {
        # Use dotnet build with --no-incremental to force rebuild
        dotnet build "GestureSign.Daemon\GestureSign.Daemon.csproj" -c uiAccessRelease --no-incremental -v minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Daemon build failed!" -ForegroundColor Red
            exit 1
        }

        dotnet build "GestureSign.ControlPanel\GestureSign.ControlPanel.csproj" -c uiAccessRelease --no-incremental -v minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ControlPanel build failed!" -ForegroundColor Red
            exit 1
        }

        Write-Host "  Build completed" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[2/3] Skipping build (-SkipBuild specified)" -ForegroundColor Yellow
}

# Step 3: Verify deployment
Write-Host ""
Write-Host "[3/3] Verifying deployment..." -ForegroundColor Yellow

$DeployPath = "C:\Program Files\GestureSign"
if (Test-Path "$DeployPath\GestureSign.exe") {
    Write-Host "  Deployed to: $DeployPath" -ForegroundColor Green

    # Check signature
    $sig = Get-AuthenticodeSignature "$DeployPath\GestureSign.exe"
    if ($sig.Status -eq "Valid") {
        Write-Host "  Signature: Valid" -ForegroundColor Green
    } else {
        Write-Host "  Warning: Signature invalid or missing" -ForegroundColor Yellow
    }

    # Show file info
    $file = Get-Item "$DeployPath\GestureSign.exe"
    Write-Host "  Last modified: $($file.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "  Warning: Files not found in $DeployPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "To start GestureSign:" -ForegroundColor White
Write-Host "  & '$DeployPath\GestureSign.exe'" -ForegroundColor Cyan
