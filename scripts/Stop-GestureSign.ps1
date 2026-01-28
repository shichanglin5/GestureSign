<#
.SYNOPSIS
    停止所有 GestureSign 进程（需要管理员权限才能停止 UIAccess 进程）
#>

$ErrorActionPreference = "SilentlyContinue"

# 检查是否有 GestureSign 进程
$processes = Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue
if (-not $processes) {
    Write-Host "No GestureSign processes running" -ForegroundColor Gray
    exit 0
}

# 尝试直接终止
foreach ($proc in $processes) {
    taskkill /F /PID $proc.Id 2>$null | Out-Null
}

Start-Sleep -Milliseconds 200
$remaining = Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue

if (-not $remaining) {
    Write-Host "GestureSign processes stopped ($($processes.Count))" -ForegroundColor Green
    exit 0
}

# 需要管理员权限
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Elevating to stop UIAccess processes..." -ForegroundColor Yellow
    $pids = ($remaining | ForEach-Object { $_.Id }) -join ","
    Start-Process -FilePath "powershell" -ArgumentList "-NoProfile", "-Command", "taskkill /F /PID $($remaining[0].Id); exit" -Verb RunAs -Wait -WindowStyle Hidden

    Start-Sleep -Milliseconds 200
    $stillRemaining = Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue
    if ($stillRemaining) {
        Write-Host "Warning: Some processes may still be running" -ForegroundColor Yellow
    } else {
        Write-Host "GestureSign processes stopped ($($processes.Count))" -ForegroundColor Green
    }
} else {
    Write-Host "Warning: Some processes may still be running" -ForegroundColor Yellow
}
