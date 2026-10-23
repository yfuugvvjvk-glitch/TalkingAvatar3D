# Talking Avatar 3D — Grafică pe Calculator

**Proiect:** Animație procedurală în timp real ghidată de LLM  
**Platformă:** Unity 6000.2.3f1 · Windows 10/11  
**Model 3D:** GLB cu ARKit blendshapes  
**LLM local:** Ollama (`qwen2.5:3b`)  
**Voce:** Windows SAPI (EN) + Piper TTS (RO)  
**Lip-sync:** Rhubarb Lip Sync + fallback fonematic

---

## Arhitectura sistemului

```
[ Utilizator (text) ]
        │
        ▼
[ Ollama qwen2.5:3b ]  ── streaming token cu token ──►  [ UI Chat (OnGUI) ]
        │
        │  text complet (propoziție)
        ▼
[ TTS Server local (Python 5000) ]
   ├── Engleză  → Windows SAPI  (pyttsx3)
   └── Română   → Piper TTS     (ro_RO-mihai-medium)
        │
        │  audio WAV + header X-Viseme-Timeline
        ▼
[ Modul Rhubarb Lip Sync ]  (opțional, ~84MB)
   └── JSON cu cue-uri timed (A-H) per cadru audio
        │
        ▼
[ AvatarVisemeAnimator (Unity C#) ]
   ├── ParseRhubarbTimeline → VisemeFrames
   ├── Fallback: VisemeTextMapper (fonematic RO/EN)
   ├── ApplyViseme() → 7 blendshapes cu Lerp
   │     jawopen · mouthfunnel · mouthsmile
   │     mouthclose · mouthshruglower
   │     mouthpucker · mouthstretch
   └── Animații procedurale: mers pe loc, flotări
```

---

## Matricea de mapare viseme (implementată în cod)

| Grup foneme         | Visemă               | Blendshapes activate                 |
| ------------------- | -------------------- | ------------------------------------ |
| A, O, Ă, Â, Î, UH   | `OpenRound`          | `jawopen` 28% + `mouthfunnel` 18%    |
| E, I, AE, EE        | `MidSpread`          | `jawopen` 16% + `mouthsmile` 20%     |
| B, M, P             | `Closed`             | `mouthclose` 55%                     |
| F, V                | `TeethOnLip`         | `mouthshruglower` 30% + `jawopen` 5% |
| W, OO               | `Pucker`             | `mouthpucker` 40% + `jawopen` 8%     |
| L, D, T, N, S, Ș, Z | `Consonant/Dd/Nn/Ss` | `jawopen` 8% + `mouthstretch` 14%    |
| CH, SH              | `RhubarbCh`          | `jawopen` 10% + `mouthpucker` 12%    |
| R                   | `RhubarbRr`          | `jawopen` 12% + `mouthstretch` 8%    |
| K, G, H             | `RhubarbKk`          | `jawopen` 10% + `mouthstretch` 10%   |
| Pauză               | `Rest`               | toate la 0 (Lerp)                    |

Toate tranzițiile folosesc **interpolare liniară continuă** (`Lerp` cu `blendSpeed = 12`) pentru a evita tranzițiile sacadate.

---

## Cerințe software (pentru evaluare)

### Obligatorii

| Software          | Versiune        | Link                       |
| ----------------- | --------------- | -------------------------- |
| Unity Hub + Unity | **6000.2.3f1**  | https://unity.com/download |
| Ollama            | oricare recentă | https://ollama.com         |
| Python            | 3.10 – 3.12     | https://python.org         |

### Opționale (lip-sync audio-based)

| Software          | Notă                              |
| ----------------- | --------------------------------- |
| Voce Piper română | instalată cu scriptul din proiect |
| Rhubarb Lip Sync  | instalat cu scriptul din proiect  |

---

## Ghid de instalare pas cu pas (pentru profesor)

### Pasul 1 — Descarcă și deschide proiectul Unity

```
1. Clonează sau descarcă ZIP-ul acestui repository
2. Deschide Unity Hub → Add → selectează folderul descărcat
3. Alege versiunea Unity 6000.2.3f1 (dacă nu o ai, Unity Hub te ghidează să o instalezi)
4. Apasă Open — Unity importă pachetele automat (durează 1-3 minute la prima deschidere)
5. Deschide scena: Assets → Scenes → SampleScene
```

### Pasul 2 — Instalează și pornește Ollama

```powershell
# Descarcă de pe https://ollama.com și instalează
# Apoi, în PowerShell sau CMD:
ollama pull qwen2.5:3b
ollama serve
```

Verifică că rulează: deschide http://localhost:11434 în browser — ar trebui să scrie `Ollama is running`.

> **Dacă Ollama nu e pornit**, în Unity apare eroarea `HTTP Error during 'api/chat' PostRequest` — exact ce se vede în screenshot.

### Pasul 3 — Instalează dependențele Python și pornește serverul TTS

```powershell
# Navighează în folderul proiectului:
cd "calea\spre\TalkingAvatar3D\Tools\TtsService"

# Instalează dependențele Python:
pip install -r requirements.txt

# Pornește serverul TTS (ține fereastra deschisă):
python tts_server.py
```

Serverul pornește pe `http://127.0.0.1:5000`. Verifică: http://127.0.0.1:5000/health

> **Alternativ**: dublu-click pe `Tools\TtsService\start_tts.bat`

### Pasul 4 — (Opțional) Instalează vocea românească Piper

Dublu-click pe `Tools\TtsService\install_romanian_voice.bat`  
Descarcă ~63 MB (vocea `ro_RO-mihai-medium`, licență CC0). Repornește serverul TTS după instalare.

### Pasul 5 — (Opțional) Instalează Rhubarb Lip Sync pentru lip-sync precis

Dublu-click pe `Tools\TtsService\install_rhubarb.bat`  
Descarcă ~84 MB. Repornește serverul TTS după instalare.

### Pasul 6 — Intră în Play Mode și testează

1. În Unity, apasă butonul **Play** ▶
2. În panoul din dreapta-sus al Game view, alege limba: **English** sau **Română**
3. Scrie un mesaj și apasă **Trimite** (sau Enter)
4. Pentru a testa animațiile procedurale, trimite:
   - `Fă 5 pași` / `Walk 5 steps`
   - `Fă 3 flotări` / `Do 3 push-ups`

---

## Verificare rapidă a serviciilor

| Test                  | Cum verifici                                            |
| --------------------- | ------------------------------------------------------- |
| Ollama rulează        | http://localhost:11434 → "Ollama is running"            |
| TTS server rulează    | http://127.0.0.1:5000/health → JSON cu `"status": "ok"` |
| Voce română instalată | `/health` → `"romanian_piper_voice": true`              |
| Rhubarb instalat      | `/health` → `"rhubarb": true`                           |

---

## Structura proiectului

```
TalkingAvatar3D/
├── Assets/
│   ├── AvatarVisemeAnimator.cs    ← Animație facială (blendshapes + Lerp) + corp
│   ├── VisemeTextMapper.cs        ← Mapare foneme RO/EN → viseme + parser Rhubarb
│   ├── OllamaStreamTester.cs      ← Client Ollama streaming + queue TTS
│   ├── TalkingAvatarBootstrap.cs  ← Inițializare scenă + UI OnGUI
│   ├── StreamingAssets/
│   │   └── model.glb              ← Avatar 3D cu blendshapes ARKit
│   ├── ollama-unity/              ← Librărie Ollama pentru Unity
│   └── Scenes/SampleScene.unity
├── Tools/
│   └── TtsService/
│       ├── tts_server.py          ← Server TTS (Windows SAPI + Piper)
│       ├── requirements.txt
│       ├── start_tts.bat
│       ├── install_romanian_voice.bat
│       └── install_rhubarb.bat
├── Packages/manifest.json         ← Dependențe Unity (glTFast, URP, etc.)
└── ProjectSettings/
```

---

## Funcționalități implementate

### Cerințe principale

- ✅ **Generare text streaming** de la Ollama (token cu token, fără latență de așteptare)
- ✅ **Prompturi de sistem** dedicate pentru română și engleză (răspunsuri corecte lingvistic)
- ✅ **Sincronizare buze (Lip-Sync)**: Rhubarb audio-based când e disponibil, fallback fonematic
- ✅ **Matrice viseme completă** conform specificației (OpenRound, MidSpread, Closed, TeethOnLip, Pucker, Consonant)
- ✅ **Interpolare Lerp continuă** între stări faciale (evită tranzițiile sacadate)
- ✅ **Control blendshapes ARKit** pe modelul GLB (7 shape-uri per visemă)
- ✅ **Detecție automată mesh față** cu 3 niveluri de fallback

### Cerințe opționale

- ✅ **Animație procedurală mers pe loc** cu oase (LeftUpLeg, RightUpLeg, LeftArm, RightArm)
- ✅ **Animație procedurală flotări** cu tranziție și revenire la poziția inițială
- ✅ **Detecție comenzi în română și engleză** cu număr de repetări (numerale + cuvinte)
- ✅ **Mișcare cap și corp** subtilă în timpul vorbirii (respirație + animație)
- ✅ **UI onscreen** cu istoric chat, selector limbă, status servicii

### Specificul limbii române

- ✅ Diacriticele (ă, â, î, ș, ț) mapate explicit pe visemele corecte
- ✅ Digrame specifice (ch, gh) tratate separat
- ✅ Promptul de sistem cere răspunsuri cu diacritice corecte
- ✅ Voce Piper neurală pentru română (când e instalată)

---

## Note tehnice

**De ce T-pose la pornire?** Avatarul stă în T-pose când nu vorbește — este poziția de repaus. Animația pornim numai la comenzi explicite (mers/flotări) sau în timpul vorbirii.

**Ollama HTTP Error?** Ollama trebuie pornit înainte de Unity Play Mode. Rulează `ollama serve` sau deschide aplicația Ollama.

**TTS în așteptare?** Serverul Python trebuie să ruleze. Verifică că `python tts_server.py` este activ.

**Blendshapes nu se văd?** Dacă modelul GLB are mesh-ul feței cu un alt nume, setează manual câmpul `Face Renderer` pe componenta `AvatarVisemeAnimator` din Inspector, sau activează `Log Blend Shape Names` pentru a vedea ce shape-uri a detectat.

---

## Licențe componente terțe

| Componentă               | Licență                          |
| ------------------------ | -------------------------------- |
| ollama-unity             | MIT                              |
| glTFast (Unity)          | Apache 2.0                       |
| Piper TTS engine         | MIT                              |
| Vocea ro_RO-mihai-medium | CC0 (date voce publice)          |
| Rhubarb Lip Sync         | Freeware (redistribuire permisă) |
| pyttsx3                  | MPL 2.0                          |
