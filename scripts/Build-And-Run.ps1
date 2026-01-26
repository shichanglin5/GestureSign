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

# Check if running as administrator when building uiAccessRelease
if ($Configuration -eq "uiAccessRelease") {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdmin) {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Red
        Write-Host "  Administrator privileges required!" -ForegroundColor Red
        Write-Host "========================================" -ForegroundColor Red
        Write-Host ""
        Write-Host "The uiAccessRelease configuration requires administrator privileges for:" -ForegroundColor Yellow
        Write-Host "  - Code signing with self-signed certificate" -ForegroundColor Gray
        Write-Host "  - Copying files to C:\Program Files\GestureSign" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Options:" -ForegroundColor White
        Write-Host "  1. Re-run this command in an Administrator PowerShell/Terminal" -ForegroundColor Cyan
        Write-Host "  2. Use a different configuration (Debug, Release, Portable)" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "To launch as Administrator:" -ForegroundColor White
        Write-Host "  - Press Ctrl+Shift+P in VS Code" -ForegroundColor Gray
        Write-Host "  - Type 'Terminal: Create New Terminal (In Active Workspace)'" -ForegroundColor Gray
        Write-Host "  - Select 'Run as Administrator' from the dropdown" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Or run directly:" -ForegroundColor White
        Write-Host "  Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""$PSCommandPath"" -Configuration uiAccessRelease'" -ForegroundColor Cyan
        Write-Host ""

        # Try to open elevated terminal (may not work in VS Code integrated terminal)
        Write-Host "Attempting to open elevated PowerShell window..." -ForegroundColor Yellow
        $scriptPath = $MyInvocation.MyCommand.Path
        $arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -Command ""cd '$SolutionDir'; Write-Host 'Run: .\scripts\Build-And-Run.ps1 -Configuration uiAccessRelease' -ForegroundColor Green"""

        try {
            Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
            Write-Host "  Elevated PowerShell window opened - please run the command there." -ForegroundColor Green
        } catch {
            Write-Host "  Failed to open elevated window. Please run manually." -ForegroundColor Red
        }

        exit 1
    }
}

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
    } elseif ($Configuration -eq "uiAccessRelease") {
        # UIAccess release: start with console logging
        # The daemon will allocate its own console window when --log.redirectToStd is specified
        Write-Host "  Starting with console logging..." -ForegroundColor Yellow
        Write-Host ""

        try {
            # Start daemon with console logging - it will open its own console window
            Start-Process -FilePath $ExePath -ArgumentList "--log.redirectToStd"

            Start-Sleep -Milliseconds 1500

            # Check if daemon started
            $daemonProcess = Get-Process -Name "GestureSign" -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $ExePath }
            if ($daemonProcess) {
                Write-Host "  Daemon started with console window (PID: $($daemonProcess.Id))" -ForegroundColor Green
                Write-Host "  Check the daemon console window for logs" -ForegroundColor Cyan
            } else {
                Write-Host "  Warning: Could not verify daemon process" -ForegroundColor Yellow
            }
        } catch {
            Write-Host ""
            Write-Host "  Error starting daemon: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "  You may need to start it manually from: $ExePath" -ForegroundColor Yellow
        }
    } else {
        # Release mode: run in background
        Start-Process -FilePath $ExePath
    }

    if ($Configuration -ne "uiAccessRelease") {
        Write-Host "  Daemon started" -ForegroundColor Green
    }
} else {
    Write-Host "  Error: Daemon executable not found at $ExePath" -ForegroundColor Red
    exit 1
}

# Optionally start Control Panel
if ($RunControlPanel) {
    if (Test-Path $ControlPanelPath) {
        Write-Host "  Starting Control Panel: $ControlPanelPath" -ForegroundColor Cyan

        if ($Configuration -eq "uiAccessRelease") {
            # UIAccess release: use direct invocation or cmd fallback
            try {
                & $ControlPanelPath
            } catch {
                cmd /c start "" "$ControlPanelPath"
            }
        } else {
            Start-Process -FilePath $ControlPanelPath
        }

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
