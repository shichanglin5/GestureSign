<#
.SYNOPSIS
    将构建输出复制到 Program Files 目录

.PARAMETER Configuration
    构建配置 (默认: uiAccessRelease)
#>

param(
    [string]$Configuration = "uiAccessRelease"
)

$ErrorActionPreference = "Stop"

$binPaths = @(
    (Join-Path $PSScriptRoot "..\bin\$Configuration"),
    (Join-Path $PSScriptRoot "..\bin\$Configuration\net8.0-windows10.0.19041.0"),
    (Join-Path $PSScriptRoot "..\bin\$Configuration\net8.0-windows")
)

$src = $null
foreach ($path in $binPaths) {
    if (Test-Path "$path\GestureSign.exe") {
        $src = $path
        break
    }
}

if (-not $src) {
    Write-Host "Build output not found. Build first." -ForegroundColor Red
    exit 1
}

$dest = "C:\Program Files\GestureSign"
if (-not (Test-Path $dest)) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Write-Host "Created: $dest" -ForegroundColor Green
}

Write-Host "Copying from: $src" -ForegroundColor Cyan
Copy-Item "$src\*" -Destination $dest -Recurse -Force
Write-Host "Files copied to $dest" -ForegroundColor Green
