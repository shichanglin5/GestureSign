# Open Admin Terminal Helper Script
# Opens an elevated PowerShell window with a specific command ready to run

param(
    [string]$Command = ""
)

$WorkspaceDir = Split-Path -Parent $PSScriptRoot

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "  UIAccess Release Build" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "This build requires Administrator privileges for:" -ForegroundColor White
Write-Host "  - Code signing with self-signed certificate" -ForegroundColor Gray
Write-Host "  - Copying files to C:\Program Files\GestureSign" -ForegroundColor Gray
Write-Host ""
Write-Host "Opening elevated PowerShell window..." -ForegroundColor Cyan

# Create the command to run in elevated window
# Automatically execute the command with a 3-second delay for user to see what's happening
$ElevatedCommand = "Set-Location '$WorkspaceDir'; " +
                   "Write-Host ''; " +
                   "Write-Host '=== GestureSign UIAccess Build ===' -ForegroundColor Cyan; " +
                   "Write-Host 'Workspace: $WorkspaceDir' -ForegroundColor Gray; " +
                   "Write-Host ''; " +
                   "Write-Host 'Executing command:' -ForegroundColor Green; " +
                   "Write-Host '  $Command' -ForegroundColor Cyan; " +
                   "Write-Host ''; " +
                   "Write-Host 'Starting in 2 seconds... (Press Ctrl+C to cancel)' -ForegroundColor Yellow; " +
                   "Start-Sleep -Seconds 2; " +
                   "Write-Host ''; " +
                   "try { Invoke-Expression '$Command'; Write-Host ''; Write-Host 'Command completed' -ForegroundColor Green } catch { Write-Host 'Error: ' -NoNewline -ForegroundColor Red; Write-Host `$_.Exception.Message -ForegroundColor Red }"

try {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $ElevatedCommand
    Write-Host ""
    Write-Host "Elevated PowerShell window opened successfully!" -ForegroundColor Green
    Write-Host "Please complete the build in that window." -ForegroundColor Yellow
    Write-Host ""
} catch {
    Write-Host ""
    Write-Host "Failed to open elevated window." -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please manually:" -ForegroundColor Yellow
    Write-Host "  1. Open PowerShell as Administrator" -ForegroundColor Gray
    Write-Host "  2. Navigate to: $WorkspaceDir" -ForegroundColor Gray
    Write-Host "  3. Run: $Command" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
