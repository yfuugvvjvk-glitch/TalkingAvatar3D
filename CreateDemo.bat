@echo off
chcp 65001 >nul
title Creare pachet demo

set "PROJECT_DIR=%~dp0"
set "BUILD_DIR=%~dp0Build"
set "TOOLS_DIR=%~dp0Tools"
set "DEMO_DIR=%~dp0TalkingAvatar3D-Demo"
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

REM Sterge folderul si zip-ul vechi
if exist "%DEMO_DIR%" rmdir /s /q "%DEMO_DIR%"
if exist "%OUTPUT%" del "%OUTPUT%"

echo   Copiez fisierele...

REM Creaza folderul demo
mkdir "%DEMO_DIR%"

REM Copiaza tot din Build/
xcopy "%BUILD_DIR%\*" "%DEMO_DIR%\" /E /I /Q

REM Copiaza Tools/
xcopy "%TOOLS_DIR%\*" "%DEMO_DIR%\Tools\" /E /I /Q

REM Copiaza START_DEMO.bat
copy "%PROJECT_DIR%START_DEMO.bat" "%DEMO_DIR%\START_DEMO.bat" >nul

echo   Creez arhiva zip...

powershell -Command "Compress-Archive -Path '%DEMO_DIR%\*' -DestinationPath '%OUTPUT%' -Force"

REM Sterge folderul temporar
rmdir /s /q "%DEMO_DIR%"

if exist "%OUTPUT%" (
    echo.
    echo   ============================================================
    echo   GATA! Arhiva creata:
    echo   %OUTPUT%
    echo   ============================================================
    echo   Trimite acest fisier pe WeTransfer / Google Drive / USB.
    echo   Destinatarul dezarhiveaza si da dublu-click pe START_DEMO.bat
    echo.
) else (
    echo   EROARE: Arhiva nu a fost creata!
)

pause
