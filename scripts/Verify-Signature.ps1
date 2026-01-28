<#
.SYNOPSIS
    验证 GestureSign 可执行文件的数字签名

.PARAMETER Path
    GestureSign 安装目录 (默认: C:\Program Files\GestureSign)
#>

param(
    [string]$Path = "C:\Program Files\GestureSign"
)

$signtool = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe"
) | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } | Select-Object -First 1 -ExpandProperty FullName

if (-not $signtool) {
    Write-Host "signtool.exe not found, using Get-AuthenticodeSignature" -ForegroundColor Yellow
}

$files = @("GestureSign.exe", "GestureSignControlPanel.exe")

foreach ($file in $files) {
    $filePath = Join-Path $Path $file
    if (-not (Test-Path $filePath)) {
        Write-Host "Not found: $filePath" -ForegroundColor Yellow
        continue
    }

    Write-Host "Verifying: $file" -ForegroundColor Cyan
    $sig = Get-AuthenticodeSignature -FilePath $filePath
    if ($sig.Status -eq "Valid") {
        Write-Host "  Valid signature ($($sig.SignerCertificate.Subject))" -ForegroundColor Green
    } else {
        Write-Host "  $($sig.Status)" -ForegroundColor Red
    }
}
