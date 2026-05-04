@echo off
REM CC-DESC: Windows launcher for Context Control's ccReplace command from the contextcontrol\ tool folder.
setlocal
set "SCRIPT_DIR=%~dp0"
set "TARGET=%SCRIPT_DIR%ccReplace.ps1"
for %%I in ("%SCRIPT_DIR%..") do set "PROJECT_ROOT=%%~fI"
if not "%CC_PROJECT_ROOT%"=="" set "PROJECT_ROOT=%CC_PROJECT_ROOT%"

if not exist "%TARGET%" (
    echo Context Control launcher error: ccReplace.ps1 was not found next to %~nx0.
    exit /b 1
)

if not exist "%PROJECT_ROOT%" (
    echo Context Control launcher error: project root was not found. Expected parent of: %SCRIPT_DIR%
    echo Set CC_PROJECT_ROOT to override.
    exit /b 1
)

pushd "%PROJECT_ROOT%" || exit /b 1

where pwsh >nul 2>nul
if errorlevel 1 goto try_windows_powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TARGET%" %*
exit /b %ERRORLEVEL%

:try_windows_powershell
where powershell >nul 2>nul
if errorlevel 1 goto no_powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TARGET%" %*
exit /b %ERRORLEVEL%

:no_powershell
echo Context Control requires PowerShell.
echo Install PowerShell 7+ or run from a Windows PowerShell-enabled machine.
exit /b 127
