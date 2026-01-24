# 重启 GestureSign Daemon 的 PowerShell 脚本

Write-Host "Stopping GestureSign.Daemon processes..." -ForegroundColor Yellow
Get-Process -Name "GestureSign.Daemon" -ErrorAction SilentlyContinue | Stop-Process -Force

Start-Sleep -Seconds 1

Write-Host "Starting GestureSign.Daemon..." -ForegroundColor Green
$daemonPath = Join-Path $PSScriptRoot "bin\Debug\net8.0-windows\GestureSign.Daemon.exe"

if (Test-Path $daemonPath) {
    Start-Process -FilePath $daemonPath
    Write-Host "Daemon started successfully!" -ForegroundColor Green
} else {
    Write-Host "Error: Daemon not found at $daemonPath" -ForegroundColor Red
    Write-Host "Please build the project first." -ForegroundColor Red
}
