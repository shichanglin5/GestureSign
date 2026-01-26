# GestureSign Code Signing Script
# Creates self-signed certificate and signs UIAccess executables

param(
    [string]$BuildConfiguration = "uiAccessRelease",
    [switch]$SkipSigning
)

$ErrorActionPreference = "Stop"

# Configuration
$CertSubject = "CN=GestureSign Development"
$CertFriendlyName = "GestureSign Code Signing Certificate"
$CertThumbprint = $null

Write-Host "=== GestureSign Code Signing Setup ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check for existing certificate
Write-Host "[1/5] Checking for existing certificate..." -ForegroundColor Yellow

$ExistingCert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date)
}

if ($ExistingCert) {
    Write-Host "  Found valid certificate: $($ExistingCert.Thumbprint)" -ForegroundColor Green
    Write-Host "    Subject: $($ExistingCert.Subject)" -ForegroundColor Gray
    Write-Host "    Expires: $($ExistingCert.NotAfter)" -ForegroundColor Gray
    $CertThumbprint = $ExistingCert.Thumbprint
} else {
    Write-Host "  No valid certificate found, will create new one" -ForegroundColor Yellow
}

# Step 2: Create new certificate if needed
if (-not $CertThumbprint) {
    Write-Host ""
    Write-Host "[2/5] Creating self-signed certificate..." -ForegroundColor Yellow

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

    Write-Host "  Certificate created successfully!" -ForegroundColor Green
    Write-Host "    Thumbprint: $CertThumbprint" -ForegroundColor Gray
    Write-Host "    Expires: $($Cert.NotAfter)" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "[2/5] Skipping certificate creation (already exists)" -ForegroundColor Green
}

# Step 3: Install to Trusted Root
Write-Host ""
Write-Host "[3/5] Installing certificate to Trusted Root store..." -ForegroundColor Yellow

$RootCert = Get-ChildItem -Path Cert:\CurrentUser\Root | Where-Object {
    $_.Thumbprint -eq $CertThumbprint
}

if ($RootCert) {
    Write-Host "  Certificate already in Trusted Root store" -ForegroundColor Green
} else {
    $SourceCert = Get-Item -Path "Cert:\CurrentUser\My\$CertThumbprint"
    $DestStore = Get-Item -Path "Cert:\CurrentUser\Root"
    $DestStore.Open("ReadWrite")
    $DestStore.Add($SourceCert)
    $DestStore.Close()

    Write-Host "  Certificate installed to Trusted Root store" -ForegroundColor Green
    Write-Host "    Note: Self-signed certificate only trusted on this machine" -ForegroundColor Yellow
}

# Step 4: Find signtool.exe
Write-Host ""
Write-Host "[4/5] Finding signtool.exe..." -ForegroundColor Yellow

$PossiblePaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x86\signtool.exe",
    "C:\Program Files (x86)\Microsoft SDKs\Windows\*\bin\*\signtool.exe",
    "C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe",
    "C:\Program Files\Microsoft SDKs\ClickOnce\SignTool\signtool.exe"
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
    Write-Host "  Found signtool: $SignTool" -ForegroundColor Green
} else {
    Write-Host "  signtool.exe not found" -ForegroundColor Red
    Write-Host "    Please install Windows SDK: https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Certificate created and installed, but signtool.exe is required for signing." -ForegroundColor Yellow
    exit 1
}

# Step 5: Sign executables
if ($SkipSigning) {
    Write-Host ""
    Write-Host "[5/5] Skipping signing (-SkipSigning specified)" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[5/5] Signing GestureSign executables..." -ForegroundColor Yellow

    # Find files to sign - check both nested and non-nested paths
    $BinPaths = @(
        (Join-Path $PSScriptRoot "..\bin\$BuildConfiguration\net8.0-windows10.0.19041.0"),
        (Join-Path $PSScriptRoot "..\bin\$BuildConfiguration\net8.0-windows10.0.19041.0\net8.0-windows10.0.19041.0")
    )

    $BinPath = $null
    foreach ($Path in $BinPaths) {
        if (Test-Path "$Path\GestureSign.exe") {
            $BinPath = $Path
            break
        }
    }

    if (-not $BinPath) {
        Write-Host "  Build output directory not found" -ForegroundColor Red
        Write-Host "    Please build project first: dotnet build GestureSign.sln -c $BuildConfiguration" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "  Using build path: $BinPath" -ForegroundColor Gray

    $ExeFiles = @(
        "GestureSign.exe",
        "GestureSign.ControlPanel.exe"
    )

    $SignedCount = 0
    $SkippedCount = 0
    $FailedCount = 0

    foreach ($ExeFile in $ExeFiles) {
        $FilePath = Join-Path $BinPath $ExeFile

        if (-not (Test-Path $FilePath)) {
            Write-Host "  Skipped: $ExeFile (file not found)" -ForegroundColor Yellow
            $SkippedCount++
            continue
        }

        # Check if already signed
        $Signature = Get-AuthenticodeSignature -FilePath $FilePath
        if ($Signature.Status -eq "Valid" -and $Signature.SignerCertificate.Thumbprint -eq $CertThumbprint) {
            Write-Host "  Skipped: $ExeFile (already signed with current certificate)" -ForegroundColor Green
            $SkippedCount++
            continue
        }

        # Sign the file
        Write-Host "  Signing: $ExeFile" -ForegroundColor Cyan

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
            Write-Host "    Signed successfully: $ExeFile" -ForegroundColor Green
            $SignedCount++
        } else {
            Write-Host "    Signing failed: $ExeFile (exit code: $($Process.ExitCode))" -ForegroundColor Red
            $FailedCount++
        }
    }

    Write-Host ""
    $ResultColor = if ($FailedCount -eq 0) { "Green" } else { "Red" }
    Write-Host "Signing results: $SignedCount signed, $SkippedCount skipped, $FailedCount failed" -ForegroundColor $ResultColor
}

# Summary
Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Certificate Information:" -ForegroundColor White
Write-Host "  Subject: $CertSubject" -ForegroundColor Gray
Write-Host "  Thumbprint: $CertThumbprint" -ForegroundColor Gray
Write-Host ""

if (-not $SkipSigning -and $BinPath) {
    Write-Host "Next Steps:" -ForegroundColor White
    Write-Host "  1. Copy signed files to protected directory:" -ForegroundColor Gray
    Write-Host "     Copy-Item '$BinPath\*' 'C:\Program Files\GestureSign\' -Recurse -Force" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  2. Run UIAccess version from protected directory:" -ForegroundColor Gray
    Write-Host "     Start-Process 'C:\Program Files\GestureSign\GestureSign.exe'" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  3. OR enable Windows Test Mode (allows running from any location):" -ForegroundColor Gray
    Write-Host "     bcdedit /set testsigning on" -ForegroundColor Cyan
    Write-Host "     # Requires restart, shows 'Test Mode' watermark" -ForegroundColor Yellow
}

Write-Host ""
