@echo off
title Talking Avatar 3D — Launcher
color 0A
cls

echo ============================================================
echo   TALKING AVATAR 3D — Pornire automata completa
echo ============================================================
echo.

REM ── Caile catre executabile ─────────────────────────────────
set "OLLAMA_EXE=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.2.3f1\Editor\Unity.exe"
set "PROJECT_DIR=%~dp0"
set "TTS_DIR=%~dp0Tools\TtsService"
set "OLLAMA_MODEL=qwen2.5:3b"

REM ─────────────────────────────────────────────────────────────
REM PASUL 1: Verificam Python
REM ─────────────────────────────────────────────────────────────
echo [1/4] Se verifica Python...
py --version >nul 2>&1
if errorlevel 1 (
    python --version >nul 2>&1
    if errorlevel 1 (
        echo.
        echo  EROARE: Python nu a fost gasit!
        echo  Instaleaza Python 3.10+ de pe https://python.org
        echo  Asigura-te ca bifezi "Add Python to PATH" la instalare.
        echo.
        pause
        exit /b 1
    )
    set "PY_CMD=python"
) else (
    set "PY_CMD=py"
)
echo  Python OK (%PY_CMD%)

REM ─────────────────────────────────────────────────────────────
REM PASUL 2: Pornire Ollama
REM ─────────────────────────────────────────────────────────────
echo.
echo [2/4] Se porneste Ollama...

REM Verificam daca Ollama ruleaza deja
curl -s --max-time 2 http://localhost:11434 >nul 2>&1
if %errorlevel% == 0 (
    echo  Ollama ruleaza deja pe localhost:11434
) else (
    if not exist "%OLLAMA_EXE%" (
        echo.
        echo  EROARE: Ollama nu a fost gasit la:
        echo  %OLLAMA_EXE%
        echo  Descarca si instaleaza Ollama de pe https://ollama.com
        echo.
        pause
        exit /b 1
    )
    echo  Pornire Ollama serve...
    start "Ollama Server" /min "%OLLAMA_EXE%" serve
    echo  Se asteapta pornirea Ollama (5 secunde)...
    timeout /t 5 /nobreak >nul
    
    REM Verificam daca modelul e descarcat
    "%OLLAMA_EXE%" list 2>nul | findstr /i "%OLLAMA_MODEL%" >nul
    if errorlevel 1 (
        echo.
        echo  Modelul %OLLAMA_MODEL% nu este descarcat. Il descarc acum...
        echo  (Prima descarca poate dura cateva minute)
        echo.
        "%OLLAMA_EXE%" pull %OLLAMA_MODEL%
        if errorlevel 1 (
            echo  EROARE la descarcarea modelului %OLLAMA_MODEL%
            pause
            exit /b 1
        )
    )
    echo  Ollama OK
)

REM ─────────────────────────────────────────────────────────────
REM PASUL 3: Pornire server TTS Python
REM ─────────────────────────────────────────────────────────────
echo.
echo [3/4] Se porneste serverul TTS...

REM Verificam daca TTS ruleaza deja
curl -s --max-time 2 http://127.0.0.1:5000/health >nul 2>&1
if %errorlevel% == 0 (
    echo  Serverul TTS ruleaza deja pe port 5000
) else (
    if not exist "%TTS_DIR%\tts_server.py" (
        echo  EROARE: Nu gasesc Tools\TtsService\tts_server.py
        pause
        exit /b 1
    )
    
    REM Instalare dependente Python (silentios daca sunt deja instalate)
    echo  Instalare/verificare dependente Python...
    %PY_CMD% -m pip install -r "%TTS_DIR%\requirements.txt" -q --no-warn-script-location
    if errorlevel 1 (
        echo  EROARE la instalarea dependentelor Python.
        pause
        exit /b 1
    )
    
    echo  Pornire server TTS...
    start "TTS Server" /min cmd /c "%PY_CMD% "%TTS_DIR%\tts_server.py" & pause"
    echo  Se asteapta pornirea serverului TTS (4 secunde)...
    timeout /t 4 /nobreak >nul
    
    REM Verificam ca a pornit
    curl -s --max-time 3 http://127.0.0.1:5000/health >nul 2>&1
    if errorlevel 1 (
        echo.
        echo  ATENTIE: Serverul TTS nu a raspuns inca.
        echo  Verifica fereastra "TTS Server" pentru erori.
        echo  TTS-ul va functiona fara voce (animatia merge oricum).
        echo.
    ) else (
        echo  Server TTS OK
    )
)

REM ─────────────────────────────────────────────────────────────
REM PASUL 4: Deschidere Unity
REM ─────────────────────────────────────────────────────────────
echo.
echo [4/4] Se deschide Unity...

if not exist "%UNITY_EXE%" (
    echo  Unity Editor nu a fost gasit la:
    echo  %UNITY_EXE%
    echo.
    echo  Incerc prin Unity Hub...
    start "" "C:\Program Files\Unity Hub\Unity Hub.exe" -- --projectPath "%PROJECT_DIR%"
) else (
    start "" "%UNITY_EXE%" -projectPath "%PROJECT_DIR%"
)

echo.
echo ============================================================
echo   Toate serviciile au pornit!
echo.
echo   Ollama:     http://localhost:11434
echo   TTS Server: http://127.0.0.1:5000/health
echo   Unity:      se incarca proiectul...
echo.
echo   In Unity: apasa Play (▶), scrie un mesaj si apasa Enter.
echo.
echo   Comenzi utile in chat:
echo     "Fa 5 pasi"     / "Walk 5 steps"
echo     "Fa 3 flotari"  / "Do 3 push-ups"
echo.
echo   Inchide aceasta fereastra dupa ce Unity s-a deschis.
echo ============================================================
echo.
pause
