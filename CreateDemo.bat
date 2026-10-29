@echo off
chcp 65001 >nul
title Creare pachet demo

set "PROJECT_DIR=%~dp0"
set "BUILD_DIR=%~dp0Build"
set "TOOLS_DIR=%~dp0Tools"
set "OUTPUT=%~dp0TalkingAvatar3D-Demo.zip"

echo.
echo ============================================================
echo   Creare pachet demo TalkingAvatar3D
echo ============================================================
echo.

REM Verifica ca exista Build/
if not exist "%BUILD_DIR%\TalkingAvatar3D.exe" (
    echo   EROARE: Nu gasesc Build\TalkingAvatar3D.exe
    echo   Fa mai intai build din Unity: File - Build Profiles - Build
    pause & exit /b 1
)

REM Sterge zip-ul vechi daca exista
if exist "%OUTPUT%" del "%OUTPUT%"

echo   Creez arhiva...

REM Folosim PowerShell pentru zip
powershell -Command ^
    "$src = '%BUILD_DIR:\=\\%'; " ^
    "$tools = '%TOOLS_DIR:\=\\%'; " ^
    "$bat = '%PROJECT_DIR:\=\\%START_DEMO.bat'; " ^
    "$out = '%OUTPUT:\=\\%'; " ^
    "$tmp = Join-Path $env:TEMP 'TalkingAvatar3D-Demo'; " ^
    "if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }; " ^
    "New-Item -ItemType Directory $tmp | Out-Null; " ^
    "Copy-Item -Path $src\* -Destination $tmp -Recurse; " ^
    "Copy-Item -Path $tools -Destination $tmp -Recurse; " ^
    "Copy-Item -Path $bat -Destination $tmp; " ^
    "Compress-Archive -Path $tmp\* -DestinationPath $out -Force; " ^
    "Remove-Item $tmp -Recurse -Force; " ^
    "Write-Host 'Gata!'"

if exist "%OUTPUT%" (
    echo.
    echo   ============================================================
    echo   Pachetul a fost creat:
    echo   %OUTPUT%
    echo   ============================================================
    echo   Trimite acest fisier pe WeTransfer / Google Drive / USB.
    echo   Destinatarul dezarhiveaza si da dublu-click pe START_DEMO.bat
    echo.
) else (
    echo   EROARE: Arhiva nu a fost creata.
)

pause
