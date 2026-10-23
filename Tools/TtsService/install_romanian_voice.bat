@echo off
setlocal
set "VOICE_DIR=%~dp0voices"
set "MODEL_URL=https://huggingface.co/rhasspy/piper-voices/resolve/main/ro/ro_RO/mihai/medium/ro_RO-mihai-medium.onnx"
set "CONFIG_URL=https://huggingface.co/rhasspy/piper-voices/resolve/main/ro/ro_RO/mihai/medium/ro_RO-mihai-medium.onnx.json"

if not exist "%VOICE_DIR%" mkdir "%VOICE_DIR%"
curl.exe -L --fail --output "%VOICE_DIR%\ro_RO-mihai-medium.onnx" "%MODEL_URL%"
if errorlevel 1 (
    echo Could not download the Romanian Piper voice model.
    pause
    exit /b 1
)

curl.exe -L --fail --output "%VOICE_DIR%\ro_RO-mihai-medium.onnx.json" "%CONFIG_URL%"
if errorlevel 1 (
    echo Could not download the Romanian Piper voice configuration.
    pause
    exit /b 1
)

echo Romanian Piper voice installed. Restart the TTS service.
pause
