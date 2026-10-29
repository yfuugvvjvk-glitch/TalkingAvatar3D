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
set "OLLAMA_MODEL=qwen2.5:3b"

REM Cauta exe-ul in Build\ sau direct langa bat
if exist "%~dp0Build\TalkingAvatar3D.exe" (
    set "BUILD_EXE=%~dp0Build\TalkingAvatar3D.exe"
) else (
    set "BUILD_EXE=%~dp0TalkingAvatar3D.exe"
)

REM ============================================================
REM 1. Python
REM ============================================================
echo [1/3] Verific Python...

py --version >nul 2>&1
if %errorlevel% equ 0 ( set "PY_CMD=py" & echo       Python OK & goto :step2 )

python --version >nul 2>&1
if %errorlevel% equ 0 ( set "PY_CMD=python" & echo       Python OK & goto :step2 )

echo.
echo   EROARE: Python nu a fost gasit!
echo   Instaleaza Python 3.10+ de la https://python.org
echo   La instalare bifeaza "Add Python to PATH"
echo.
pause & exit /b 1

:step2
REM ============================================================
REM 2. Ollama
REM ============================================================
echo.
echo [2/3] Pornire Ollama...

curl.exe -s --max-time 3 http://localhost:11434 >nul 2>&1
if %errorlevel% equ 0 (
    echo       Ollama ruleaza deja
    goto :checkmodel
)

if not exist "%OLLAMA_EXE%" (
    echo.
    echo   EROARE: Ollama nu a fost gasit la:
    echo   %OLLAMA_EXE%
    echo   Descarca de la https://ollama.com
    echo.
    pause & exit /b 1
)

echo       Pornesc Ollama...
start "Ollama" /min "%OLLAMA_EXE%" serve

echo       Astept Ollama sa fie gata...
set /a RETRY=0
:waitollama
timeout /t 2 /nobreak >nul
curl.exe -s --max-time 2 http://localhost:11434 >nul 2>&1
if %errorlevel% equ 0 goto :ollamaready
set /a RETRY+=1
if %RETRY% lss 7 goto :waitollama
echo       ATENTIE: Ollama nu a raspuns in 14 secunde, continuam oricum...
goto :checkmodel

:ollamaready
echo       Ollama OK

:checkmodel
echo       Verific modelul %OLLAMA_MODEL%...
"%OLLAMA_EXE%" list 2>nul | findstr /i "%OLLAMA_MODEL%" >nul 2>&1
if %errorlevel% equ 0 (
    echo       Modelul %OLLAMA_MODEL% este prezent
    goto :step3
)
echo.
echo       Modelul %OLLAMA_MODEL% nu e descarcat.
echo       Descarc acum (~2 GB, poate dura cateva minute)...
echo       Nu inchide aceasta fereastra!
echo.
"%OLLAMA_EXE%" pull %OLLAMA_MODEL%
if %errorlevel% neq 0 (
    echo.
    echo   EROARE la descarcarea modelului. Verifica conexiunea la internet.
    pause & exit /b 1
)
echo       Model descarcat cu succes.

:step3
REM ============================================================
REM 3. Server TTS Python
REM ============================================================
echo.
echo [3/3] Pornire server TTS...

curl.exe -s --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% equ 0 (
    echo       Serverul TTS ruleaza deja
    goto :step4
)

if not exist "%TTS_DIR%\tts_server.py" (
    echo   EROARE: Nu gasesc %TTS_DIR%\tts_server.py
    pause & exit /b 1
)

echo       Instalez dependentele Python...
echo       (poate dura 1-2 minute la prima rulare)
%PY_CMD% -m pip install -r "%TTS_DIR%\requirements.txt" --quiet --no-warn-script-location 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   EROARE la instalarea dependentelor Python.
    echo   Incearca manual: py -m pip install -r Tools\TtsService\requirements.txt
    pause & exit /b 1
)
echo       Dependente OK

echo       Pornesc serverul TTS...
start "TTS Server" /min cmd /k "%PY_CMD% "%TTS_DIR%\tts_server.py""

echo       Astept serverul TTS...
set /a RETRY=0
:waittts
timeout /t 2 /nobreak >nul
curl.exe -s --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% equ 0 goto :ttsready
set /a RETRY+=1
if %RETRY% lss 8 goto :waittts
echo       ATENTIE: TTS nu a pornit in 16 secunde.
echo       Animatia merge si fara TTS (fara voce).
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
echo   Totul pornit cu succes!
echo.
echo   Ollama  : http://localhost:11434
echo   TTS     : http://127.0.0.1:5000/health
echo.
echo   Aceasta fereastra poate fi inchisa.
echo ============================================================
echo.
pause
