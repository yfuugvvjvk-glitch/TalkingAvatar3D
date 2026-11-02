@echo off
chcp 65001 >nul
title Talking Avatar 3D - Demo

echo.
echo ============================================================
echo   TALKING AVATAR 3D - Demo (fara Unity)
echo ============================================================
echo.

set "OLLAMA_EXE=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
set "PROJECT_DIR=%~dp0"
set "TTS_DIR=%~dp0Tools\TtsService"

REM Cauta exe-ul in Build\ sau direct langa bat
if exist "%~dp0Build\Latest\TalkingAvatar3D.exe" (
    set "BUILD_EXE=%~dp0Build\Latest\TalkingAvatar3D.exe"
    goto :build_selected
)

if exist "%~dp0Build\TalkingAvatar3D.exe" (
    set "BUILD_EXE=%~dp0Build\TalkingAvatar3D.exe"
) else (
    set "BUILD_EXE=%~dp0TalkingAvatar3D.exe"
)

:build_selected
REM ============================================================
REM 1. Python
REM ============================================================
echo [1/4] Verific Python...

py --version >nul 2>&1
if %errorlevel% equ 0 ( set "PY_CMD=py" & echo       Python OK & goto :step2 )

python --version >nul 2>&1
if %errorlevel% equ 0 ( set "PY_CMD=python" & echo       Python OK & goto :step2 )

echo.
echo   ATENTIE: Python nu a fost gasit. Aplicatia porneste fara voce.
echo.
set "PY_CMD="
goto :step2

:step2
REM ============================================================
REM 2. Ollama
REM ============================================================
echo.
echo [2/4] Pornire Ollama...

curl.exe -s --connect-timeout 1 --max-time 2 http://localhost:11434/api/tags >nul 2>&1
if %errorlevel% equ 0 (
    echo       Ollama raspunde
    goto :step3
)

if not exist "%OLLAMA_EXE%" (
    echo       ATENTIE: Ollama nu raspunde. Comenzile locale raman disponibile.
    goto :step3
)

echo       Pornesc Ollama...
start "Ollama" /min "%OLLAMA_EXE%" serve

echo       Astept Ollama sa fie gata...
set /a RETRY=0
:waitollama
timeout /t 1 /nobreak >nul
curl.exe -s --connect-timeout 1 --max-time 2 http://localhost:11434/api/tags >nul 2>&1
if %errorlevel% equ 0 goto :ollamaready
set /a RETRY+=1
if %RETRY% lss 6 goto :waitollama
echo       ATENTIE: Ollama nu raspunde. Aplicatia porneste fara chat AI.
goto :step3

:ollamaready
echo       Ollama OK

:step3
REM ============================================================
REM 3. Server TTS Python
REM ============================================================
echo.
echo [3/4] Pornire server TTS...

if not defined PY_CMD (
    echo       Fara Python, pornesc aplicatia fara voce.
    goto :step4
)

curl.exe -s --connect-timeout 1 --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% equ 0 (
    echo       Serverul TTS ruleaza deja
    goto :step4
)

netstat -ano -p tcp | findstr /R /C:":5000 .*LISTENING" >nul
if %errorlevel% equ 0 (
    echo       ATENTIE: Portul TTS 5000 nu raspunde. Pornesc aplicatia fara voce.
    goto :step4
)

if not exist "%TTS_DIR%\tts_server.py" (
    echo   ATENTIE: Nu gasesc serviciul TTS. Aplicatia porneste fara voce.
    goto :step4
)

echo       Instalez dependentele Python...
echo       (poate dura 1-2 minute la prima rulare)
%PY_CMD% -m pip install -r "%TTS_DIR%\requirements.txt" --quiet --no-warn-script-location 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   ATENTIE: Nu s-au instalat dependentele TTS. Pornesc aplicatia fara voce.
    goto :step4
)
echo       Dependente OK

echo       Pornesc serverul TTS...
start "TTS Server" /min cmd /k "%PY_CMD% "%TTS_DIR%\tts_server.py""

echo       Astept serverul TTS...
set /a RETRY=0
:waittts
timeout /t 2 /nobreak >nul
curl.exe -s --connect-timeout 1 --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% equ 0 goto :ttsready
set /a RETRY+=1
if %RETRY% lss 8 goto :waittts
echo.
echo   ATENTIE: TTS nu a raspuns. Aplicatia porneste fara voce;
echo   comenzile de miscare raman disponibile.
goto :step4

:ttsready
echo       Server TTS OK

:step4
REM ============================================================
REM 4. Lanseaza aplicatia
REM ============================================================
echo.
echo [4/4] Lansez aplicatia...

if not exist "%BUILD_EXE%" (
    echo   EROARE: Nu gasesc executabilul:
    echo   %BUILD_EXE%
    echo   Asigura-te ca ai dezarhivat corect pachetul demo.
    pause & exit /b 1
)

start "" "%BUILD_EXE%"
echo       Aplicatia pornita.

echo.
echo ============================================================
echo   Aplicatia a fost lansata.
echo.
echo   Ollama  : http://localhost:11434
echo   TTS     : http://127.0.0.1:5000/health
echo.
echo   Aceasta fereastra poate fi inchisa.
echo ============================================================
echo.
pause
