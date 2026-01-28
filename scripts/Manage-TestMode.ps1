<#
.SYNOPSIS
    管理 Windows Test Signing 模式

.PARAMETER Action
    check, enable, 或 disable
#>

param(
    [Parameter(Mandatory)]
    [ValidateSet("check", "enable", "disable")]
    [string]$Action
)

switch ($Action) {
    "check" {
        $result = bcdedit /enum "{current}" 2>&1 | Select-String "testsigning"
        if ($result -match "Yes") {
            Write-Host "Test signing mode: ENABLED" -ForegroundColor Green
        } else {
            Write-Host "Test signing mode: DISABLED" -ForegroundColor Cyan
        }
    }
    "enable" {
        Write-Host "Enabling test signing mode..." -ForegroundColor Yellow
        bcdedit /set testsigning on
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Enabled. RESTART REQUIRED." -ForegroundColor Green
        } else {
            Write-Host "Failed - run as Administrator" -ForegroundColor Red
            exit 1
        }
    }
    "disable" {
        Write-Host "Disabling test signing mode..." -ForegroundColor Yellow
        bcdedit /set testsigning off
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Disabled. RESTART REQUIRED." -ForegroundColor Green
        } else {
            Write-Host "Failed - run as Administrator" -ForegroundColor Red
            exit 1
        }
    }
}
