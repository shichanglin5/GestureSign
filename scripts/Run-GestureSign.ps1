param(
    [Parameter(Mandatory)]
    [ValidateSet("debug", "release", "uiaccess")]
    [string]$Config,

    [switch]$ControlPanel
)

$paths = @{
    "debug"   = "D:\wd\soft\GestureSign"
    "release" = "D:\wd\soft\GestureSign"
    "uiaccess" = "C:\Program Files\GestureSign"
}

$dir = $paths[$Config]
$exe = if ($ControlPanel) { "GestureSignControlPanel.exe" } else { "GestureSign.exe" }
$filePath = Join-Path $dir $exe

if (-not (Test-Path $filePath)) {
    Write-Host "File not found: $filePath" -ForegroundColor Red
    exit 1
}

$startArgs = @{ FilePath = $filePath; WorkingDirectory = $dir }
if ($Config -eq "debug" -and -not $ControlPanel) {
    $startArgs["ArgumentList"] = "--log.redirectToStd"
}

if ($Config -eq "uiaccess") {
    # UIAccess 程序需要通过 ShellExecute 启动（Invoke-Item 使用 ShellExecute API）
    Invoke-Item $filePath
} else {
    Start-Process @startArgs
}
Write-Host "Started: $filePath" -ForegroundColor Green
