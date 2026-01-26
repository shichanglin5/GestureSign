<#
.SYNOPSIS
    测试 GestureSign UIAccess 版本

.DESCRIPTION
    此脚本提供多种方式测试 UIAccess 版本:
    1. 启用 Windows 测试模式(推荐 - 可以从任何位置运行)
    2. 复制到受保护目录并运行
    3. 检查当前测试模式状态

.PARAMETER Mode
    测试模式

.EXAMPLE
    .\Test-UIAccess.ps1 -Mode CheckTestMode
    检查当前测试模式状态
#>

param(
    [ValidateSet("EnableTestMode", "DisableTestMode", "CheckTestMode", "CopyToProtectedDir", "RunFromProtectedDir")]
    [string]$Mode = "CheckTestMode"
)

$ErrorActionPreference = "Stop"

# 配置
$BuildConfig = "uiAccessRelease"
$BinPath = Join-Path $PSScriptRoot "..\bin\$BuildConfig\net8.0-windows10.0.19041.0"
$ProtectedDir = "C:\Program Files\GestureSign"

Write-Host "=== GestureSign UIAccess 测试工具 ===" -ForegroundColor Cyan
Write-Host ""

function Test-AdminPrivilege {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-TestSigningStatus {
    $bcdedit = bcdedit /enum "{current}" 2>&1 | Out-String
    if ($bcdedit -match "testsigning\s+Yes") {
        return $true
    }
    return $false
}

switch ($Mode) {
    "CheckTestMode" {
        Write-Host "[检查测试模式状态]" -ForegroundColor Yellow
        Write-Host ""

        $isTestMode = Get-TestSigningStatus

        if ($isTestMode) {
            Write-Host "✓ 测试模式: 已启用" -ForegroundColor Green
            Write-Host "  可以从任何位置运行 UIAccess 应用" -ForegroundColor Gray
        } else {
            Write-Host "✗ 测试模式: 未启用" -ForegroundColor Red
            Write-Host "  UIAccess 应用必须从受保护目录运行" -ForegroundColor Yellow
        }

        Write-Host ""
        Write-Host "当前构建路径: $BinPath" -ForegroundColor Gray
        Write-Host "受保护目录: $ProtectedDir" -ForegroundColor Gray
        Write-Host ""

        # 检查构建是否存在
        if (Test-Path "$BinPath\GestureSign.exe") {
            try {
                $signature = Get-AuthenticodeSignature "$BinPath\GestureSign.exe"
                Write-Host "GestureSign.exe 签名状态: $($signature.Status)" -ForegroundColor $(if ($signature.Status -eq "Valid") { "Green" } else { "Yellow" })

                if ($signature.Status -eq "Valid") {
                    Write-Host "  签名者: $($signature.SignerCertificate.Subject)" -ForegroundColor Gray
                }
            }
            catch {
                Write-Host "⚠ 无法检查签名状态" -ForegroundColor Yellow
            }
        } else {
            Write-Host "⚠ UIAccess 版本未编译" -ForegroundColor Yellow
            Write-Host "  运行: dotnet build GestureSign.sln -c $BuildConfig" -ForegroundColor Cyan
        }

        Write-Host ""
        Write-Host "下一步操作:" -ForegroundColor White
        Write-Host "  启用测试模式: .\Test-UIAccess.ps1 -Mode EnableTestMode" -ForegroundColor Cyan
        Write-Host "  复制到受保护目录: .\Test-UIAccess.ps1 -Mode CopyToProtectedDir" -ForegroundColor Cyan
    }

    "EnableTestMode" {
        Write-Host "[启用 Windows 测试模式]" -ForegroundColor Yellow
        Write-Host ""

        if (-not (Test-AdminPrivilege)) {
            Write-Host "✗ 此操作需要管理员权限" -ForegroundColor Red
            Write-Host "  请以管理员身份运行 PowerShell" -ForegroundColor Yellow
            exit 1
        }

        $isTestMode = Get-TestSigningStatus

        if ($isTestMode) {
            Write-Host "✓ 测试模式已启用,无需重复操作" -ForegroundColor Green
        } else {
            Write-Host "⚠ 警告: 启用测试模式后桌面会显示'测试模式'水印" -ForegroundColor Yellow
            Write-Host "  优点: 可以从任何位置运行 UIAccess 应用,方便开发调试" -ForegroundColor Gray
            Write-Host "  缺点: 桌面右下角会显示水印" -ForegroundColor Gray
            Write-Host ""

            $confirm = Read-Host "是否继续? (y/n)"
            if ($confirm -ne "y") {
                Write-Host "操作已取消" -ForegroundColor Yellow
                exit 0
            }

            Write-Host ""
            Write-Host "执行: bcdedit /set testsigning on" -ForegroundColor Cyan
            bcdedit /set testsigning on | Out-Null

            if ($LASTEXITCODE -eq 0) {
                Write-Host ""
                Write-Host "✓ 测试模式已启用" -ForegroundColor Green
                Write-Host "  请重启计算机使更改生效" -ForegroundColor Yellow
                Write-Host ""
                Write-Host "重启后可以直接运行:" -ForegroundColor White
                Write-Host "  $BinPath\GestureSign.exe" -ForegroundColor Cyan
            } else {
                Write-Host "✗ 启用失败" -ForegroundColor Red
            }
        }
    }

    "DisableTestMode" {
        Write-Host "[禁用 Windows 测试模式]" -ForegroundColor Yellow
        Write-Host ""

        if (-not (Test-AdminPrivilege)) {
            Write-Host "✗ 此操作需要管理员权限" -ForegroundColor Red
            Write-Host "  请以管理员身份运行 PowerShell" -ForegroundColor Yellow
            exit 1
        }

        Write-Host "执行: bcdedit /set testsigning off" -ForegroundColor Cyan
        bcdedit /set testsigning off | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "✓ 测试模式已禁用" -ForegroundColor Green
            Write-Host "  请重启计算机使更改生效" -ForegroundColor Yellow
        } else {
            Write-Host "✗ 禁用失败" -ForegroundColor Red
        }
    }

    "CopyToProtectedDir" {
        Write-Host "[复制到受保护目录]" -ForegroundColor Yellow
        Write-Host ""

        if (-not (Test-AdminPrivilege)) {
            Write-Host "✗ 此操作需要管理员权限" -ForegroundColor Red
            Write-Host "  请以管理员身份运行 PowerShell" -ForegroundColor Yellow
            exit 1
        }

        if (-not (Test-Path "$BinPath\GestureSign.exe")) {
            Write-Host "✗ UIAccess 版本未编译" -ForegroundColor Red
            Write-Host "  运行: dotnet build GestureSign.sln -c $BuildConfig" -ForegroundColor Yellow
            exit 1
        }

        # 创建目录
        if (-not (Test-Path $ProtectedDir)) {
            Write-Host "创建目录: $ProtectedDir" -ForegroundColor Cyan
            New-Item -Path $ProtectedDir -ItemType Directory -Force | Out-Null
        }

        # 停止运行中的进程
        Write-Host "停止运行中的 GestureSign 进程..." -ForegroundColor Cyan
        Get-Process -Name "GestureSign*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        # 复制文件
        Write-Host "复制文件到 $ProtectedDir..." -ForegroundColor Cyan
        Copy-Item -Path "$BinPath\*" -Destination $ProtectedDir -Recurse -Force

        Write-Host ""
        Write-Host "✓ 复制完成" -ForegroundColor Green
        Write-Host ""
        Write-Host "运行命令:" -ForegroundColor White
        Write-Host "  $ProtectedDir\GestureSign.exe" -ForegroundColor Cyan
    }

    "RunFromProtectedDir" {
        Write-Host "[从受保护目录运行]" -ForegroundColor Yellow
        Write-Host ""

        if (-not (Test-Path "$ProtectedDir\GestureSign.exe")) {
            Write-Host "✗ 受保护目录中未找到 GestureSign.exe" -ForegroundColor Red
            Write-Host "  请先运行: .\Test-UIAccess.ps1 -Mode CopyToProtectedDir" -ForegroundColor Yellow
            exit 1
        }

        Write-Host "启动: $ProtectedDir\GestureSign.exe" -ForegroundColor Cyan
        Start-Process -FilePath "$ProtectedDir\GestureSign.exe"
    }
}

Write-Host ""
