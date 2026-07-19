@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\sync-github.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
echo.
if "%EXITCODE%"=="0" (
    echo GitHub sync completed.
) else (
    echo GitHub sync failed with exit code %EXITCODE%.
)
exit /b %EXITCODE%
