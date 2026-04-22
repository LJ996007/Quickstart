@echo off
setlocal

cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-release.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Release build completed successfully.
) else (
    echo Release build failed with exit code %EXITCODE%.
)

pause
exit /b %EXITCODE%
