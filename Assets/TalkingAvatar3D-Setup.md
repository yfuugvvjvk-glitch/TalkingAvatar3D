# Talking Avatar 3D setup

The Unity project streams text from the configured Ollama model, buffers completed phrases, and sends them to a local speech endpoint while the response is still being generated. A language-specific system prompt asks Ollama for clear, concise English or Romanian. English speech uses Windows SAPI and Romanian speech uses a local Piper neural voice. Rhubarb analyzes each generated WAV to provide timed mouth cues. Audio plays through an `AudioSource` while smoothed facial blendshapes and subtle head/upper-body movement follow the speech.

## Required services and assets

1. Install Ollama, pull the model named in `OllamaStreamTester.modelName` (default `qwen2.5:3b`) with `ollama pull qwen2.5:3b`, and leave its API available at `http://localhost:11434`.
2. Install the Romanian neural voice by double-clicking `Tools\TtsService\install_romanian_voice.bat`. This downloads the Piper `ro_RO-mihai-medium` voice (CC0 voice dataset; about 60 MB) into `Tools\TtsService\voices`. The TTS server uses Piper TTS, licensed GPL-3.0-or-later.
3. For audio-synchronized lip movement, install Rhubarb by double-clicking `Tools\TtsService\install_rhubarb.bat`. This downloads the official Windows release into `Tools\Rhubarb` (about 84 MB compressed; about 156 MB extracted). The local service detects it automatically. Rhubarb uses PocketSphinx recognition for English and its phonetic recognizer with the generated text as a dialog guide for Romanian.
4. Start the included local TTS service by double-clicking `Tools\TtsService\start_tts.bat`, or run:

   ```powershell
   cd Tools\TtsService
   py -m pip install -r requirements.txt
   py tts_server.py
   ```

   The service listens only on `127.0.0.1:5000` and serves `/v1/audio/speech`. On startup it warms the installed Romanian Piper voice to avoid first-sentence model-loading delay. Check `http://127.0.0.1:5000/health` to inspect installed voices and optional integrations. `speechLanguage` selects `English` or `Romanian`; `ttsVoice` can select an installed English SAPI voice.
5. `Assets/model.glb` is copied into `Assets/StreamingAssets/model.glb` so it is included in player builds. The project uses glTFast to load it automatically when a scene starts. The GLB includes ARKit blendshapes and Rhubarb viseme targets.
6. `TalkingAvatarBootstrap` creates the camera framing, avatar loader, audio source, Ollama client, and on-screen chat UI automatically. Enter Play mode and use the chat panel; no Inspector wiring is required.

Explicit requests for walking or push-ups in Romanian or English trigger procedural animations. For example, send `Fă 10 pași`, `Walk 10 steps`, `Fă 2 flotări`, or `Do 3 push-ups`; if no count is supplied, the avatar performs five. Walking is in place. The action count and progress appear in the UI. Other exercise/action requests are not animated.

## Blendshape support

When Rhubarb is installed, it analyzes each generated WAV and returns timed mouth cues. The cues drive only a low-weight jaw-open blendshape on the `AvatarHead` renderer to reduce visible mesh distortion; this is a conservative approximation rather than full phoneme articulation. They are not applied to teeth, eyelashes, or body meshes. Head and upper-body motion is subtle and returns to the rest pose after speech. If Rhubarb is missing or fails, the UI and Unity log report that the grapheme-based estimate is being used.

After changing scripts, stop and restart Play mode in Unity so the updated animator and Ollama system prompt are loaded.

The Romanian Piper voice and Rhubarb executable are separate downloads and are not bundled into the project. The TTS endpoint reports an explicit error when the requested voice is missing; if Rhubarb is absent or fails, it reports and uses the grapheme-based viseme fallback.
