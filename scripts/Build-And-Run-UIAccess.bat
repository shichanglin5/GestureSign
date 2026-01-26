@echo off
REM GestureSign UIAccess Build and Run
REM Requires Administrator privileges

setlocal

echo ===============================================
echo GestureSign UIAccess Build and Run
echo ===============================================
echo.
echo WARNING: This script requires Administrator privileges
echo.

REM Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Error: This script must be run as Administrator
    echo Right-click and select "Run as administrator"
    pause
    exit /b 1
)

echo Running as Administrator - OK
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-And-Run.ps1" -Configuration uiAccessRelease

endlocal
