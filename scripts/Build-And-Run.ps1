# GestureSign Build and Run Script
# Builds the specified configuration and runs the application

param(
    [ValidateSet("Debug", "Release", "uiAccessRelease", "Portable", "Centennial")]
    [string]$Configuration = "Debug",

    [switch]$SkipBuild,

    [switch]$RunControlPanel
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = Split-Path -Parent $ScriptDir

Write-Host "=== GestureSign Build and Run ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host ""

# Step 1: Stop running processes
Write-Host "[1/3] Stopping running GestureSign processes..." -ForegroundColor Yellow
Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Write-Host "  Processes stopped" -ForegroundColor Green

# Step 2: Build
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "[2/3] Building solution ($Configuration)..." -ForegroundColor Yellow

    Push-Location $SolutionDir
    try {
        dotnet build GestureSign.sln -c $Configuration

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Build failed!" -ForegroundColor Red
            exit 1
        }

        Write-Host "  Build completed successfully" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host ""
    Write-Host "[2/3] Skipping build (-SkipBuild specified)" -ForegroundColor Yellow
}

# Step 3: Run application
Write-Host ""
Write-Host "[3/3] Starting GestureSign..." -ForegroundColor Yellow

if ($Configuration -eq "uiAccessRelease") {
    # UIAccess version runs from C:\Program Files\
    $ExePath = "C:\Program Files\GestureSign\GestureSign.exe"
    $ControlPanelPath = "C:\Program Files\GestureSign\GestureSignControlPanel.exe"
} else {
    # Other versions run from development directory
    $ExePath = "D:\wd\soft\GestureSign\GestureSign.exe"
    $ControlPanelPath = "D:\wd\soft\GestureSign\GestureSignControlPanel.exe"
}

# Start Daemon
if (Test-Path $ExePath) {
    Write-Host "  Starting Daemon: $ExePath" -ForegroundColor Cyan

    if ($Configuration -eq "Debug") {
        # Debug mode: run with console output
        Start-Process -FilePath $ExePath -ArgumentList "--log.redirectToStd"
    } else {
        # Release mode: run in background
        Start-Process -FilePath $ExePath
    }

    Start-Sleep -Milliseconds 500
    Write-Host "  Daemon started" -ForegroundColor Green
} else {
    Write-Host "  Error: Daemon executable not found at $ExePath" -ForegroundColor Red
    exit 1
}

# Optionally start Control Panel
if ($RunControlPanel) {
    if (Test-Path $ControlPanelPath) {
        Write-Host "  Starting Control Panel: $ControlPanelPath" -ForegroundColor Cyan
        Start-Process -FilePath $ControlPanelPath
        Write-Host "  Control Panel started" -ForegroundColor Green
    } else {
        Write-Host "  Warning: Control Panel not found at $ControlPanelPath" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=== GestureSign Started ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Running processes:" -ForegroundColor White
Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, MainWindowTitle -AutoSize

Write-Host ""
Write-Host "Tip: Use './Build-And-Run.ps1 -RunControlPanel' to also start Control Panel" -ForegroundColor Gray
