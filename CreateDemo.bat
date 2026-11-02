@echo off
chcp 65001 >nul
title Creare pachet demo

set "PROJECT_DIR=%~dp0"
set "TOOLS_DIR=%~dp0Tools"
set "DEMO_DIR=%~dp0TalkingAvatar3D-Demo"
set "OUTPUT=%~dp0TalkingAvatar3D-Demo.zip"

REM Alege il player al carui assembly de gameplay a fost compilat cel mai recent.
for /f "usebackq delims=" %%I in (`powershell.exe -NoProfile -Command "$candidates = @((Join-Path $env:PROJECT_DIR 'Build\TalkingAvatar3D.exe'), (Join-Path $env:PROJECT_DIR 'Build\Latest\TalkingAvatar3D.exe'), (Join-Path $env:PROJECT_DIR 'TalkingAvatar3D.exe')); $candidates | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Property { $assembly = Join-Path (Split-Path $_) 'TalkingAvatar3D_Data\Managed\Assembly-CSharp.dll'; if (Test-Path -LiteralPath $assembly) { (Get-Item -LiteralPath $assembly).LastWriteTimeUtc } else { (Get-Item -LiteralPath $_).LastWriteTimeUtc } } -Descending | Select-Object -First 1"`) do set "BUILD_EXE=%%I"

echo.
echo ============================================================
echo   Creare pachet demo TalkingAvatar3D
echo ============================================================
echo.

REM Verifica playerul selectat si datele buildului Unity.
if not defined BUILD_EXE (
    echo   EROARE: Nu gasesc niciun TalkingAvatar3D.exe.
    echo   Fa mai intai build din Unity: File - Build Profiles - Build
    pause & exit /b 1
)

if not exist "%BUILD_EXE%" (
    echo   EROARE: Playerul selectat nu exista:
    echo   %BUILD_EXE%
    pause & exit /b 1
)

for %%I in ("%BUILD_EXE%") do set "BUILD_DIR=%%~dpI"
if not exist "%BUILD_DIR%TalkingAvatar3D_Data\Managed\Assembly-CSharp.dll" (
    echo   EROARE: Buildul selectat nu contine Assembly-CSharp.dll:
    echo   %BUILD_DIR%TalkingAvatar3D_Data\Managed\Assembly-CSharp.dll
    pause & exit /b 1
)

echo   Build selectat:
echo   %BUILD_EXE%

REM Sterge folderul si zip-ul vechi
if exist "%DEMO_DIR%" rmdir /s /q "%DEMO_DIR%"
if exist "%OUTPUT%" del "%OUTPUT%"

echo   Copiez fisierele...

REM Creaza folderul demo
mkdir "%DEMO_DIR%"

REM Copiaza buildul selectat fara builduri vechi imbricate in Build\Latest.
robocopy "%BUILD_DIR%." "%DEMO_DIR%" /E /XD "%BUILD_DIR%Latest" /R:2 /W:1 >nul
if errorlevel 8 (
    echo   EROARE: Nu am putut copia buildul Unity.
    rmdir /s /q "%DEMO_DIR%"
    pause & exit /b 1
)

REM Copiaza Tools/
xcopy "%TOOLS_DIR%\*" "%DEMO_DIR%\Tools\" /E /I /Q

REM Copiaza START_DEMO.bat si CITESTE-MA.txt
copy "%PROJECT_DIR%START_DEMO.bat" "%DEMO_DIR%\START_DEMO.bat" >nul
copy "%PROJECT_DIR%CITESTE-MA.txt" "%DEMO_DIR%\CITESTE-MA.txt" >nul

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
