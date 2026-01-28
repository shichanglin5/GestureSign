<#
.SYNOPSIS
    停止所有 GestureSign 进程
#>

$ErrorActionPreference = "SilentlyContinue"

$processes = Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue
if ($processes) {
    $processes | Stop-Process -Force
    Write-Host "GestureSign processes stopped ($($processes.Count))" -ForegroundColor Green
} else {
    Write-Host "No GestureSign processes running" -ForegroundColor Gray
}
