@echo off
setlocal

cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-debug.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Debug build completed successfully.
) else (
    echo Debug build failed with exit code %EXITCODE%.
)

pause
exit /b %EXITCODE%
