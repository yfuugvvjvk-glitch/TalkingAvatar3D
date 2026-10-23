@echo off
chcp 65001 >nul
title Talking Avatar 3D - Launcher

echo.
echo ============================================================
echo   TALKING AVATAR 3D - Pornire automata
echo ============================================================
echo.

set "OLLAMA_EXE=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.2.3f1\Editor\Unity.exe"
set "PROJECT_DIR=%~dp0"
set "TTS_DIR=%~dp0Tools\TtsService"
set "OLLAMA_MODEL=qwen2.5:3b"

REM ============================================================
REM 1. Python
REM ============================================================
echo [1/4] Verific Python...

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
echo [2/4] Pornire Ollama...

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

REM Asteptam ca Ollama sa fie gata - retry de pana la 15 secunde
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
REM Verificam si descarcam modelul daca lipseste
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
echo [3/4] Pornire server TTS...

curl.exe -s --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% equ 0 (
    echo       Serverul TTS ruleaza deja
    goto :step4
)

if not exist "%TTS_DIR%\tts_server.py" (
    echo   EROARE: Nu gasesc %TTS_DIR%\tts_server.py
    pause & exit /b 1
)

REM pip install - afisam progresul, nu silentios
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
start "TTS Server" /min cmd /k "%PY_CMD% \"%TTS_DIR%\tts_server.py\""

REM Asteptam TTS cu retry
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
REM 4. Deschide Unity
REM ============================================================
echo.
echo [4/4] Deschid Unity...

if exist "%UNITY_EXE%" (
    start "" "%UNITY_EXE%" -projectPath "%PROJECT_DIR%"
    echo       Unity pornit.
) else (
    echo       Unity Editor nu e la calea implicita, incerc Unity Hub...
    start "" "C:\Program Files\Unity Hub\Unity Hub.exe" -- --projectPath "%PROJECT_DIR%"
)

echo.
echo ============================================================
echo   Totul pornit cu succes!
echo.
echo   Ollama  : http://localhost:11434
echo   TTS     : http://127.0.0.1:5000/health
echo   Unity   : se deschide proiectul...
echo.
echo   In Unity: apasa Play, scrie un mesaj, apasa Enter.
echo   Fa 5 pasi / Walk 5 steps
echo   Fa 3 flotari / Do 3 push-ups
echo ============================================================
echo.
echo   Aceasta fereastra poate fi inchisa dupa ce Unity s-a deschis.
pause
