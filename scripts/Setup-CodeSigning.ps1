<#
.SYNOPSIS
    创建自签名证书并为 GestureSign 签名(UIAccess 支持)

.DESCRIPTION
    此脚本执行以下操作:
    1. 检查是否已存在 GestureSign 代码签名证书
    2. 如果不存在,创建新的自签名证书
    3. 将证书安装到受信任的根证书存储区
    4. 对 GestureSign 可执行文件进行数字签名

.PARAMETER BuildConfiguration
    要签名的构建配置 (默认: uiAccessRelease)

.PARAMETER SkipSigning
    仅创建证书,不进行签名

.EXAMPLE
    .\Setup-CodeSigning.ps1
    创建证书并签名 uiAccessRelease 版本

.EXAMPLE
    .\Setup-CodeSigning.ps1 -BuildConfiguration Release
    创建证书并签名 Release 版本

.EXAMPLE
    .\Setup-CodeSigning.ps1 -SkipSigning
    仅创建证书
#>

param(
    [string]$BuildConfiguration = "uiAccessRelease",
    [switch]$SkipSigning
)

$ErrorActionPreference = "Stop"

# 配置参数
$CertSubject = "CN=GestureSign Development"
$CertFriendlyName = "GestureSign Code Signing Certificate"
$CertThumbprint = $null

Write-Host "=== GestureSign 代码签名设置 ===" -ForegroundColor Cyan
Write-Host ""

#region 步骤 1: 检查现有证书

Write-Host "[1/5] 检查现有证书..." -ForegroundColor Yellow

$ExistingCert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date)
}

if ($ExistingCert) {
    Write-Host "  ✓ 找到有效证书: $($ExistingCert.Thumbprint)" -ForegroundColor Green
    Write-Host "    主题: $($ExistingCert.Subject)" -ForegroundColor Gray
    Write-Host "    过期时间: $($ExistingCert.NotAfter)" -ForegroundColor Gray
    $CertThumbprint = $ExistingCert.Thumbprint
} else {
    Write-Host "  ⚠ 未找到有效证书,将创建新证书" -ForegroundColor Yellow
}

#endregion

#region 步骤 2: 创建新证书(如果需要)

if (-not $CertThumbprint) {
    Write-Host ""
    Write-Host "[2/5] 创建自签名证书..." -ForegroundColor Yellow

    # 创建证书(有效期 5 年)
    $Cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -FriendlyName $CertFriendlyName `
        -KeyExportPolicy Exportable `
        -KeySpec Signature `
        -KeyLength 2048 `
        -KeyAlgorithm RSA `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5) `
        -CertStoreLocation "Cert:\CurrentUser\My"

    $CertThumbprint = $Cert.Thumbprint

    Write-Host "  ✓ 证书创建成功!" -ForegroundColor Green
    Write-Host "    指纹: $CertThumbprint" -ForegroundColor Gray
    Write-Host "    过期时间: $($Cert.NotAfter)" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "[2/5] 跳过证书创建(已存在)" -ForegroundColor Green
}

#endregion

#region 步骤 3: 安装到受信任的根证书存储区

Write-Host ""
Write-Host "[3/5] 安装证书到受信任的根证书存储区..." -ForegroundColor Yellow

# 检查是否已在根存储区
$RootCert = Get-ChildItem -Path Cert:\CurrentUser\Root | Where-Object {
    $_.Thumbprint -eq $CertThumbprint
}

if ($RootCert) {
    Write-Host "  ✓ 证书已在受信任的根存储区" -ForegroundColor Green
} else {
    # 复制证书到根存储区
    $SourceCert = Get-Item -Path "Cert:\CurrentUser\My\$CertThumbprint"
    $DestStore = Get-Item -Path "Cert:\CurrentUser\Root"
    $DestStore.Open("ReadWrite")
    $DestStore.Add($SourceCert)
    $DestStore.Close()

    Write-Host "  ✓ 证书已安装到受信任的根存储区" -ForegroundColor Green
    Write-Host "    ⚠ 注意: 自签名证书仅在本机受信任" -ForegroundColor Yellow
}

#endregion

#region 步骤 4: 查找 signtool.exe

Write-Host ""
Write-Host "[4/5] 查找 signtool.exe..." -ForegroundColor Yellow

# 常见的 Windows SDK 路径
$PossiblePaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x86\signtool.exe",
    "C:\Program Files (x86)\Microsoft SDKs\Windows\*\bin\*\signtool.exe"
)

$SignTool = $null
foreach ($Pattern in $PossiblePaths) {
    $Found = Get-ChildItem -Path $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($Found) {
        $SignTool = $Found.FullName
        break
    }
}

if ($SignTool) {
    Write-Host "  ✓ 找到 signtool: $SignTool" -ForegroundColor Green
} else {
    Write-Host "  ✗ 未找到 signtool.exe" -ForegroundColor Red
    Write-Host "    请安装 Windows SDK: https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "证书已创建并安装,但需要 signtool.exe 才能进行签名。" -ForegroundColor Yellow
    exit 1
}

#endregion

#region 步骤 5: 对可执行文件签名

if ($SkipSigning) {
    Write-Host ""
    Write-Host "[5/5] 跳过签名(指定了 -SkipSigning)" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[5/5] 对 GestureSign 可执行文件签名..." -ForegroundColor Yellow

    # 查找要签名的文件
    $BinPath = Join-Path $PSScriptRoot "..\bin\$BuildConfiguration\net8.0-windows10.0.19041.0"
    $ExeFiles = @(
        "GestureSign.exe",
        "GestureSign.ControlPanel.exe"
    )

    if (-not (Test-Path $BinPath)) {
        Write-Host "  ✗ 未找到构建输出目录: $BinPath" -ForegroundColor Red
        Write-Host "    请先编译项目: dotnet build GestureSign.sln -c $BuildConfiguration" -ForegroundColor Yellow
        exit 1
    }

    $SignedCount = 0
    $SkippedCount = 0
    $FailedCount = 0

    foreach ($ExeFile in $ExeFiles) {
        $FilePath = Join-Path $BinPath $ExeFile

        if (-not (Test-Path $FilePath)) {
            Write-Host "  ⚠ 跳过: $ExeFile (文件不存在)" -ForegroundColor Yellow
            $SkippedCount++
            continue
        }

        # 检查是否已签名
        $Signature = Get-AuthenticodeSignature -FilePath $FilePath
        if ($Signature.Status -eq "Valid" -and $Signature.SignerCertificate.Thumbprint -eq $CertThumbprint) {
            Write-Host "  ✓ 跳过: $ExeFile (已使用当前证书签名)" -ForegroundColor Green
            $SkippedCount++
            continue
        }

        # 执行签名
        Write-Host "  → 签名: $ExeFile" -ForegroundColor Cyan

        $SignArgs = @(
            "sign",
            "/sha1", $CertThumbprint,
            "/fd", "SHA256",
            "/t", "http://timestamp.digicert.com",
            "/v",
            "`"$FilePath`""
        )

        $Process = Start-Process -FilePath $SignTool -ArgumentList $SignArgs -Wait -NoNewWindow -PassThru

        if ($Process.ExitCode -eq 0) {
            Write-Host "    ✓ 签名成功: $ExeFile" -ForegroundColor Green
            $SignedCount++
        } else {
            Write-Host "    ✗ 签名失败: $ExeFile (退出代码: $($Process.ExitCode))" -ForegroundColor Red
            $FailedCount++
        }
    }

    Write-Host ""
    $color = if ($FailedCount -eq 0) { "Green" } else { "Red" }
    Write-Host "签名结果: $SignedCount 个成功, $SkippedCount 个跳过, $FailedCount 个失败" -ForegroundColor $color
}

#endregion

#region 总结

Write-Host ""
Write-Host "=== 设置完成 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "证书信息:" -ForegroundColor White
Write-Host "  主题: $CertSubject" -ForegroundColor Gray
Write-Host "  指纹: $CertThumbprint" -ForegroundColor Gray
Write-Host ""

if (-not $SkipSigning) {
    Write-Host "下一步:" -ForegroundColor White
    Write-Host "  1. 将签名后的文件复制到受保护目录:" -ForegroundColor Gray
    Write-Host "     Copy-Item '$BinPath\*' 'C:\Program Files\GestureSign\' -Recurse -Force" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  2. 从受保护目录运行 UIAccess 版本:" -ForegroundColor Gray
    Write-Host "     & 'C:\Program Files\GestureSign\GestureSign.exe'" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  3. 或者启用 Windows 测试模式(允许从任何位置运行):" -ForegroundColor Gray
    Write-Host "     bcdedit /set testsigning on" -ForegroundColor Cyan
    Write-Host "     # 重启后生效,桌面会显示'测试模式'水印" -ForegroundColor Yellow
}

Write-Host ""

#endregion
