@echo off
cd /d "%~dp0"
py -m pip install -r requirements.txt
if errorlevel 1 (
    echo Could not install TTS requirements.
    pause
    exit /b 1
)
py tts_server.py
pause
