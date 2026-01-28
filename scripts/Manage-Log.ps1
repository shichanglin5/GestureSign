<#
.SYNOPSIS
    查看 GestureSign 日志

.PARAMETER Action
    tail (实时跟踪) 或 delete (删除日志文件)
#>

param(
    [Parameter(Mandatory)]
    [ValidateSet("tail", "delete")]
    [string]$Action
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$logPath = Join-Path $env:LOCALAPPDATA "GestureSign\GestureSign.log"

switch ($Action) {
    "tail" {
        if (-not (Test-Path $logPath)) {
            Write-Host "Log file not found: $logPath" -ForegroundColor Yellow
            Write-Host "Waiting for log file..." -ForegroundColor Gray
            while (-not (Test-Path $logPath)) {
                Start-Sleep -Milliseconds 500
            }
        }
        Write-Host "Tailing: $logPath" -ForegroundColor Cyan
        Get-Content $logPath -Wait -Tail 50 -Encoding UTF8
    }
    "delete" {
        if (Test-Path $logPath) {
            Remove-Item $logPath -Force
            Write-Host "Deleted: $logPath" -ForegroundColor Green
        } else {
            Write-Host "Log file not found" -ForegroundColor Yellow
        }
    }
}
