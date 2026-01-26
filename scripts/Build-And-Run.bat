@echo off
REM GestureSign Build and Run - Batch Wrapper
REM This allows easy execution from Visual Studio or command prompt

setlocal

REM Default configuration
set CONFIG=Debug

REM Parse command line arguments
if "%1"=="" goto :run
set CONFIG=%1

:run
echo Running Build-And-Run.ps1 with configuration: %CONFIG%
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-And-Run.ps1" -Configuration %CONFIG%

endlocal
