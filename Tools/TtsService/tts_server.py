import base64
import json
import os
import re
import shutil
import subprocess
import tempfile
import wave
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

import pyttsx3
from piper import PiperVoice


HOST = "127.0.0.1"
PORT = 5000
PROJECT_ROOT = Path(__file__).resolve().parents[2]
ROMANIAN_VOICE_MODEL = Path(__file__).resolve().parent / "voices" / "ro_RO-mihai-medium.onnx"


def voice_language(voice):
    values = [voice.name]
    for language in getattr(voice, "languages", []):
        if isinstance(language, bytes):
            values.append(language.decode("utf-8", errors="ignore"))
        else:
            values.append(str(language))
    return " ".join(values).lower()


def select_voice(engine, language, preferred_voice):
    voices = engine.getProperty("voices")
    target = "ro" if (language or "").lower().startswith(("ro", "roman")) else "en"

    def supports_language(voice):
        details = voice_language(voice)
        return bool(
            re.search(rf"(^|[^a-z]){target}([-_]|[^a-z]|$)", details)
            or (target == "en" and "english" in details)
            or (target == "ro" and "romanian" in details)
        )

    preferred = (preferred_voice or "").strip().lower()
    if preferred and preferred != "alloy":
        for voice in voices:
            if supports_language(voice) and (
                preferred in voice.name.lower() or preferred == voice.id.lower()
            ):
                engine.setProperty("voice", voice.id)
                return voice.name

    for voice in voices:
        if supports_language(voice):
            engine.setProperty("voice", voice.id)
            return voice.name

    raise RuntimeError(
        f"No installed Windows speech voice supports {language!r}. "
        "Install that language's speech voice in Windows Settings."
    )


def find_rhubarb():
    configured = os.environ.get("RHUBARB_EXECUTABLE")
    if configured:
        executable = Path(configured).expanduser()
        if executable.is_file():
            return str(executable)
        raise FileNotFoundError(f"RHUBARB_EXECUTABLE does not exist: {executable}")

    executable = shutil.which("rhubarb")
    if executable:
        return executable

    search_roots = [PROJECT_ROOT / "Tools" / "Rhubarb"]
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        search_roots.append(Path(local_app_data) / "Programs" / "Rhubarb-Lip-Sync")

    for search_root in search_roots:
        if search_root.is_dir():
            for executable in search_root.rglob("rhubarb.exe"):
                return str(executable)

    return None


def generate_visemes(executable, audio_path, text, language):
    with tempfile.TemporaryDirectory(prefix="talking-avatar-visemes-") as work_dir:
        dialog_path = Path(work_dir) / "dialog.txt"
        output_path = Path(work_dir) / "visemes.json"
        dialog_path.write_text(text, encoding="utf-8")

        recognition = "phonetic" if (language or "").lower().startswith(("ro", "roman")) else "pocketSphinx"
        command = [
            executable,
            "--exportFormat", "json",
            "--output", str(output_path),
            "--recognizer", recognition,
            "--dialogFile", str(dialog_path),
            audio_path,
        ]
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        if result.returncode != 0:
            raise RuntimeError(
                f"Rhubarb exited with code {result.returncode}: "
                f"{(result.stderr or result.stdout).strip()}"
            )

        timeline = json.loads(output_path.read_text(encoding="utf-8"))
        if not isinstance(timeline.get("mouthCues"), list) or not timeline["mouthCues"]:
            raise RuntimeError("Rhubarb returned no mouth cues")
        return json.dumps(timeline, separators=(",", ":"))


class TtsHandler(BaseHTTPRequestHandler):
    piper_voice = None

    def send_json_error(self, status, message):
        body = json.dumps({"error": message}).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path != "/health":
            self.send_error(404, "Use /health or POST /v1/audio/speech")
            return

        engine = pyttsx3.init("sapi5")
        voices = [voice.name for voice in engine.getProperty("voices")]
        engine.stop()
        body = json.dumps(
            {
                "status": "ok",
                "sapi_voices": voices,
                "romanian_piper_voice": ROMANIAN_VOICE_MODEL.is_file(),
                "rhubarb": find_rhubarb() is not None,
            }
        ).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        if self.path != "/v1/audio/speech":
            self.send_json_error(404, "Use POST /v1/audio/speech")
            return

        try:
            content_length = int(self.headers.get("Content-Length", "0"))
            if content_length <= 0 or content_length > 1_000_000:
                self.send_json_error(400, "Request body must be between 1 byte and 1 MB")
                return

            request = json.loads(self.rfile.read(content_length).decode("utf-8"))
            if not isinstance(request, dict):
                self.send_json_error(400, "Request body must be a JSON object")
                return

            language = request.get("language", "English")
            if not isinstance(language, str) or not language.lower().startswith(("en", "ro", "roman")):
                self.send_json_error(400, "The language field must be English or Romanian")
                return

            text = request.get("input", "")
            if not isinstance(text, str) or not text.strip():
                self.send_json_error(400, "The input field must not be empty")
                return
            text = text.strip()

            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as audio_file:
                audio_path = audio_file.name

            try:
                if language.lower().startswith(("ro", "roman")):
                    if not ROMANIAN_VOICE_MODEL.is_file():
                        raise FileNotFoundError(
                            "Romanian Piper voice is missing. Run "
                            "Tools\\TtsService\\install_romanian_voice.bat"
                        )

                    if self.piper_voice is None:
                        self.__class__.piper_voice = PiperVoice.load(str(ROMANIAN_VOICE_MODEL))
                    with wave.open(audio_path, "wb") as audio:
                        self.piper_voice.synthesize_wav(text, audio)
                    tts_engine = "Piper ro_RO-mihai-medium"
                else:
                    engine = pyttsx3.init("sapi5")
                    select_voice(engine, language, request.get("voice", ""))
                    engine.save_to_file(text, audio_path)
                    engine.runAndWait()
                    engine.stop()
                    tts_engine = "Windows SAPI"

                with wave.open(audio_path, "rb") as audio:
                    if audio.getnframes() == 0:
                        raise RuntimeError("The selected Windows voice produced an empty WAV file")
                with open(audio_path, "rb") as audio_file:
                    audio_data = audio_file.read()

                viseme_mode = "estimated"
                timeline = None
                try:
                    rhubarb = find_rhubarb()
                    if rhubarb is not None:
                        timeline = generate_visemes(
                            rhubarb,
                            audio_path,
                            text,
                            language,
                        )
                        viseme_mode = "rhubarb"
                except (OSError, RuntimeError, subprocess.TimeoutExpired, json.JSONDecodeError) as error:
                    self.log_error("Rhubarb failed; using text-estimated visemes: %s", error)
            finally:
                if os.path.exists(audio_path):
                    os.remove(audio_path)

            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(audio_data)))
            self.send_header("X-TTS-Engine", tts_engine)
            self.send_header("X-Viseme-Mode", viseme_mode)
            if timeline is not None:
                encoded_timeline = base64.b64encode(timeline.encode("utf-8")).decode("ascii")
                self.send_header("X-Viseme-Timeline", encoded_timeline)
            self.end_headers()
            self.wfile.write(audio_data)
        except (ValueError, UnicodeDecodeError, json.JSONDecodeError) as error:
            self.send_json_error(400, f"Invalid JSON request: {error}")
        except Exception as error:
            self.log_error("Speech synthesis failed: %s", error)
            self.send_json_error(503, f"Speech synthesis failed: {error}")

    def log_message(self, format_string, *args):
        print(f"[TTS] {self.address_string()} - {format_string % args}")


if __name__ == "__main__":
    print(f"Local Windows TTS listening on http://{HOST}:{PORT}")
    print(f"Health check: http://{HOST}:{PORT}/health")
    if ROMANIAN_VOICE_MODEL.is_file():
        print("Loading Romanian Piper voice...")
        TtsHandler.piper_voice = PiperVoice.load(str(ROMANIAN_VOICE_MODEL))
        print("Romanian Piper voice ready.")
    HTTPServer((HOST, PORT), TtsHandler).serve_forever()
