@echo off
setlocal
set "INSTALL_DIR=%~dp0..\Rhubarb"
set "ARCHIVE=%TEMP%\Rhubarb-Lip-Sync-1.14.0-Windows.zip"
set "URL=https://github.com/DanielSWolf/rhubarb-lip-sync/releases/download/v1.14.0/Rhubarb-Lip-Sync-1.14.0-Windows.zip"

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
curl.exe -L --fail --output "%ARCHIVE%" "%URL%"
if errorlevel 1 (
    echo Rhubarb download failed.
    pause
    exit /b 1
)

powershell.exe -NoProfile -Command "Expand-Archive -LiteralPath '%ARCHIVE%' -DestinationPath '%INSTALL_DIR%' -Force"
if errorlevel 1 (
    echo Rhubarb extraction failed.
    del "%ARCHIVE%" 2>nul
    pause
    exit /b 1
)

del "%ARCHIVE%" 2>nul
echo Rhubarb installed under Tools\Rhubarb. Restart the TTS service.
pause
