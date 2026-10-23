# Talking Avatar 3D — Grafică pe Calculator

**Avatar 3D procedural animat, ghidat de un LLM local (Ollama)**

> Scrie un mesaj → Ollama generează răspunsul în streaming → TTS îl rostește →
> Rhubarb analizează audio-ul → blendshapes pe avatar în timp real

---

## ▶ Pornire rapidă (un singur dublu-click)

**Dublu-click pe `START.bat`** din folderul proiectului.

Scriptul:

1. Verifică și pornește **Ollama** (descarcă modelul `qwen2.5:3b` la prima rulare)
2. Instalează dependențele Python și pornește **serverul TTS**
3. Deschide **Unity** cu proiectul

Când Unity e deschis: apasă **Play ▶**, alege limba, scrie un mesaj și apasă Enter.

---

## Arhitectura sistemului

```
[ Utilizator (text) ]
        │
        ▼
[ Ollama qwen2.5:3b ]  ── streaming token cu token ──►  [ UI Chat ]
        │ text complet (propoziție)
        ▼
[ TTS Server local Python :5000 ]
   ├── Engleză  → Windows SAPI  (pyttsx3)
   └── Română   → Piper TTS     (ro_RO-mihai-medium)
        │ WAV + header X-Viseme-Timeline (base64 JSON)
        ▼
[ Rhubarb Lip Sync ]  ──► cue-uri timed A-H per cadru audio
        │ (fallback: VisemeTextMapper G2P/silabic RO/EN)
        ▼
[ AvatarVisemeAnimator (Unity C#) ]
   ├── 7 blendshapes cu Lerp continuu (blendSpeed=12)
   │     jawopen · mouthfunnel · mouthsmile · mouthclose
   │     mouthshruglower · mouthpucker · mouthstretch
   └── Animații procedurale: mers pe loc + flotări
```

---

## Matricea de mapare viseme (implementată complet)

| Grup foneme (RO + EN)     | Visemă               | Blendshapes activate             |
| ------------------------- | -------------------- | -------------------------------- |
| A, O, Ă, Â, Î, UH, AH, AW | `OpenRound`          | jawopen 28% + mouthfunnel 18%    |
| E, I, AE, EE, AY          | `MidSpread`          | jawopen 16% + mouthsmile 20%     |
| B, M, P                   | `Closed`             | mouthclose 55%                   |
| F, V                      | `TeethOnLip`         | mouthshruglower 30% + jawopen 5% |
| W, OO, Y                  | `Pucker`             | mouthpucker 40% + jawopen 8%     |
| L, D, T, N, S, Ș, Z, ȚJ   | `Dd/Nn/Ss/Consonant` | jawopen 8% + mouthstretch 14%    |
| CH, SH                    | `RhubarbCh`          | jawopen 10% + mouthpucker 12%    |
| R, ER                     | `RhubarbRr`          | jawopen 12% + mouthstretch 8%    |
| K, G, H, NG               | `RhubarbKk`          | jawopen 10% + mouthstretch 10%   |
| Pauză / punctuație        | `Rest`               | toate → 0 (Lerp)                 |

Tranziții fluide via **interpolare Lerp continuă** pe fiecare frame.

---

## Cerințe software

| Software          | Versiune       | Descărcare                 |
| ----------------- | -------------- | -------------------------- |
| Unity Hub + Unity | **6000.2.3f1** | https://unity.com/download |
| Ollama            | oricare        | https://ollama.com         |
| Python            | 3.10–3.12      | https://python.org         |

### Opționale (îmbunătățesc calitatea)

| Component                  | Instalare                                                 |
| -------------------------- | --------------------------------------------------------- |
| Voce română Piper (~63 MB) | dublu-click `Tools\TtsService\install_romanian_voice.bat` |
| Rhubarb Lip Sync (~84 MB)  | dublu-click `Tools\TtsService\install_rhubarb.bat`        |

Fără Rhubarb, animația buzelor folosește G2P (engleză) și reguli silabice (română).  
Fără vocea Piper, română e citită de Windows SAPI (dacă e instalat).

---

## Instalare manuală (alternativă la START.bat)

```powershell
# 1. Pornește Ollama
ollama pull qwen2.5:3b
ollama serve

# 2. Pornește serverul TTS
cd Tools\TtsService
pip install -r requirements.txt
python tts_server.py

# 3. Deschide Unity și intră în Play Mode
```

Verificare servicii:

- Ollama: http://localhost:11434 → "Ollama is running"
- TTS: http://127.0.0.1:5000/health → `{"status":"ok",...}`

---

## Comenzi chat disponibile

| Română          | Engleză         |
| --------------- | --------------- |
| `Fă 5 pași`     | `Walk 5 steps`  |
| `Fă 3 flotări`  | `Do 3 push-ups` |
| Orice întrebare | Any question    |

Numărul de repetări: cifre (`5`) sau cuvinte (`cinci`, `five`). Default: 5.

---

## Structura proiectului

```
TalkingAvatar3D/
├── START.bat                          ← PORNIRE AUTOMATĂ (Ollama + TTS + Unity)
├── README.md
├── Assets/
│   ├── AvatarVisemeAnimator.cs        ← Blendshapes + Lerp + animații corp
│   ├── VisemeTextMapper.cs            ← G2P EN / silabificare RO + Rhubarb parser
│   ├── OllamaStreamTester.cs          ← Client Ollama streaming + queue TTS
│   ├── TalkingAvatarBootstrap.cs      ← Init scenă + UI OnGUI + health check
│   ├── model.glb                      ← Sursă model avatar
│   ├── StreamingAssets/model.glb      ← Model la runtime (încărcat de GLTFast)
│   ├── ollama-unity/                  ← Librărie Ollama C#
│   └── Scenes/SampleScene.unity
├── Tools/
│   └── TtsService/
│       ├── tts_server.py              ← Server TTS (SAPI + Piper)
│       ├── requirements.txt
│       ├── start_tts.bat
│       ├── install_romanian_voice.bat
│       └── install_rhubarb.bat
├── Packages/manifest.json             ← GLTFast, URP, Input System...
└── ProjectSettings/
```

---

## Funcționalități implementate

### Cerințe principale ✅

- Generare text **streaming** de la Ollama (token cu token, latență minimă)
- **Prompturi de sistem** per limbă (română cu diacritice / engleză naturală)
- **Lip-sync audio-based** via Rhubarb (cue-uri timed din WAV real)
- **Matricea viseme** completă (6 categorii + 9 Rhubarb A-H)
- **Interpolare Lerp** continuă pe 7 blendshapes simultane
- **Format GLB** cu blendshapes ARKit, încărcat async via GLTFast
- **G2P pentru engleză**: tabel 150+ cuvinte neregulate + reguli grafemice (ph→f, gh mut, kn→n, vocale finale mute etc.)
- **Silabificare pentru română**: segmentare V/CV/CCV, buffer onset/coda, digrame ch/gh

### Cerințe opționale ✅

- **Mers pe loc** procedural cu oase (LeftUpLeg/RightUpLeg/LeftArm/RightArm)
- **Flotări** procedurale cu tranziție și revenire la poza inițială
- **Detecție comenzi** bilingv cu număr de repetări (cifre + cuvinte)
- **Mișcare cap/corp** subtilă în timpul vorbirii (respirație)
- **UI OnGUI** cu istoric chat, selector limbă, status servicii în timp real
- **Health check** Ollama la pornire (afișează avertisment dacă nu rulează)

---

## Licențe componente terțe

| Componentă               | Licență    |
| ------------------------ | ---------- |
| ollama-unity             | MIT        |
| GLTFast (Unity)          | Apache 2.0 |
| Piper TTS engine         | MIT        |
| Vocea ro_RO-mihai-medium | CC0        |
| Rhubarb Lip Sync         | Freeware   |
| pyttsx3                  | MPL 2.0    |
