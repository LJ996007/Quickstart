@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bump-version.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
echo.
if "%EXITCODE%"=="0" (
    echo Version bump completed.
) else (
    echo Version bump failed with exit code %EXITCODE%.
)
exit /b %EXITCODE%
