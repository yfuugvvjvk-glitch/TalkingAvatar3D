@echo off
chcp 65001 >nul
title Talking Avatar 3D - Launcher

echo.
echo ============================================================
echo   TALKING AVATAR 3D - Pornire automata
echo ============================================================
echo.

REM Cai executabile
set "OLLAMA_EXE=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.2.3f1\Editor\Unity.exe"
set "PROJECT_DIR=%~dp0"
set "TTS_DIR=%~dp0Tools\TtsService"
set "OLLAMA_MODEL=qwen2.5:3b"

REM ==========================================================
REM 1. Python
REM ==========================================================
echo [1/4] Verific Python...

py --version >nul 2>&1
if %errorlevel% equ 0 (
    set "PY_CMD=py"
    echo       Python OK
    goto :step2
)
python --version >nul 2>&1
if %errorlevel% equ 0 (
    set "PY_CMD=python"
    echo       Python OK
    goto :step2
)

echo.
echo   EROARE: Python nu a fost gasit!
echo   Instaleaza Python 3.10+ de la https://python.org
echo   La instalare bifeaza "Add Python to PATH"
echo.
pause
exit /b 1

:step2
REM ==========================================================
REM 2. Ollama
REM ==========================================================
echo.
echo [2/4] Pornire Ollama...

curl.exe -s --max-time 2 http://localhost:11434 >nul 2>&1
if %errorlevel% equ 0 (
    echo       Ollama ruleaza deja
    goto :step3
)

if not exist "%OLLAMA_EXE%" (
    echo.
    echo   EROARE: Ollama nu a fost gasit la:
    echo   %OLLAMA_EXE%
    echo   Descarca de la https://ollama.com si instaleaza.
    echo.
    pause
    exit /b 1
)

echo       Pornesc Ollama serve...
start "Ollama" /min "%OLLAMA_EXE%" serve
echo       Astept 5 secunde...
timeout /t 5 /nobreak >nul

REM Verificam modelul
"%OLLAMA_EXE%" list 2>nul | findstr /i "%OLLAMA_MODEL%" >nul
if %errorlevel% neq 0 (
    echo.
    echo       Descarc modelul %OLLAMA_MODEL% (prima rulare, ~2 GB)...
    echo       Va dura cateva minute. Nu inchide aceasta fereastra!
    echo.
    "%OLLAMA_EXE%" pull %OLLAMA_MODEL%
    if %errorlevel% neq 0 (
        echo   EROARE la descarcarea modelului.
        pause
        exit /b 1
    )
)
echo       Ollama OK

:step3
REM ==========================================================
REM 3. Server TTS Python
REM ==========================================================
echo.
echo [3/4] Pornire server TTS...

curl.exe -s --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% equ 0 (
    echo       Serverul TTS ruleaza deja
    goto :step4
)

if not exist "%TTS_DIR%\tts_server.py" (
    echo   EROARE: Nu gasesc %TTS_DIR%\tts_server.py
    pause
    exit /b 1
)

echo       Instalez dependentele Python (o singura data)...
%PY_CMD% -m pip install -r "%TTS_DIR%\requirements.txt" -q
if %errorlevel% neq 0 (
    echo   EROARE la instalarea dependentelor Python.
    pause
    exit /b 1
)

echo       Pornesc serverul TTS...
start "TTS Server" /min cmd /k "%PY_CMD% \"%TTS_DIR%\tts_server.py\""

echo       Astept 5 secunde...
timeout /t 5 /nobreak >nul

curl.exe -s --max-time 3 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo   ATENTIE: TTS nu a raspuns inca. Verifica fereastra TTS Server.
    echo   (Animatia merge si fara TTS)
    echo.
) else (
    echo       Server TTS OK
)

:step4
REM ==========================================================
REM 4. Deschide Unity
REM ==========================================================
echo.
echo [4/4] Deschid Unity...

if exist "%UNITY_EXE%" (
    start "" "%UNITY_EXE%" -projectPath "%PROJECT_DIR%"
    echo       Unity pornit cu proiectul.
) else (
    echo       Unity Editor nu e la calea implicita, incerc Unity Hub...
    start "" "C:\Program Files\Unity Hub\Unity Hub.exe" -- --projectPath "%PROJECT_DIR%"
)

echo.
echo ============================================================
echo   Servicii active:
echo     Ollama  : http://localhost:11434
echo     TTS     : http://127.0.0.1:5000/health
echo     Unity   : se deschide...
echo.
echo   Apasa Play in Unity, scrie un mesaj si apasa Enter.
echo   Comenzi: "Fa 5 pasi" / "Walk 5 steps"
echo            "Fa 3 flotari" / "Do 3 push-ups"
echo ============================================================
echo.
pause
