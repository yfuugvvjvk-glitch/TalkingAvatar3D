# Talking Avatar 3D — Grafică pe Calculator

**Aplicație cu avatar 3D animat procedural, răspunsuri generate local cu Ollama și sinteză vocală.**

> Scrie un mesaj → Ollama generează răspunsul în streaming → serviciul TTS redă vocea
> → fața sferică separată articulează cuvintele, iar personajul poate executa comenzi.

---

## ▶ Pornire rapidă — fără Unity instalat

**Dublu-click pe `START_DEMO.bat`** din folderul proiectului.

Scriptul pornește automat:

1. **Ollama** (descarcă modelul `qwen2.5:3b` la prima rulare, ~2 GB)
2. **Serverul TTS** Python (instalează dependențele la prima rulare)
3. **Aplicația** `Build\TalkingAvatar3D.exe` direct — fără Unity Editor

Când aplicația s-a deschis: avatarul se prezintă automat în română,
apoi poți alege limba, scrie un mesaj și apăsa Enter.

> **Nu este nevoie de Unity instalat pentru a rula demo-ul.**

---

## ▶ Pornire pentru dezvoltatori (cu Unity)

**Dublu-click pe `START.bat`** — pornește Ollama + TTS + Unity Editor.

Când Unity e deschis: apasă **Play ▶**.

---

## Cerințe software

### Pentru demo (`START_DEMO.bat`)

| Software | Versiune         | Descărcare         |
| -------- | ---------------- | ------------------ |
| Ollama   | versiune curentă | https://ollama.com |
| Python   | 3.10–3.12        | https://python.org |

> La instalarea Python bifează **"Add Python to PATH"**.

### Pentru dezvoltare (`START.bat`)

| Software          | Versiune         | Descărcare                 |
| ----------------- | ---------------- | -------------------------- |
| Unity Hub + Unity | **6000.2.3f1**   | https://unity.com/download |
| Ollama            | versiune curentă | https://ollama.com         |
| Python            | 3.10–3.12        | https://python.org         |

---

## Opționale (îmbunătățesc calitatea vocii)

| C![alt text](image.png)omponent | Instalare                                                 |
| ------------------------------- | --------------------------------------------------------- |
| Voce română Piper (~63 MB)      | dublu-click `Tools\TtsService\install_romanian_voice.bat` |
| Rhubarb Lip Sync (~84 MB)       | dublu-click `Tools\TtsService\install_rhubarb.bat`        |

Fără Rhubarb, aplicația estimează visemele din text. Fără Piper, româna folosește o voce Windows SAPI.

---

## Instalare manuală (alternativă la scripturile .bat)

```powershell
# Terminal 1 — pornește Ollama
ollama pull qwen2.5:3b
ollama serve
```

```powershell
# Terminal 2 — pornește serverul TTS
cd Tools\TtsService
py -m pip install -r requirements.txt
py tts_server.py
```

Apoi dublu-click pe `Build\TalkingAvatar3D.exe`.

Verifică serviciile înainte de demonstrație:

- Ollama: http://localhost:11434 — trebuie să răspundă „Ollama is running"
- TTS: http://127.0.0.1:5000/health — trebuie să răspundă cu status `ok`

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
[ CompanionVisemeFace (Unity C#) ]
   ├── Față sferică independentă, cu ochi și gură animată
   ├── Estimează visemele din text sau folosește cue-uri Rhubarb
   └── Săgețile repoziționează fața pe ecran
        │
[ AvatarVisemeAnimator (Unity C#) ]
   └── Animații procedurale: mers, flotări, salut, sărituri, înot și dans
```

---

## Ghid de demonstrație

1. Dublu-click pe `START_DEMO.bat` și așteaptă să pornească aplicația.
2. Avatarul se prezintă automat. Verifică statusul Ollama și TTS din panoul aplicației.
3. Alege **Română** sau **English**, introdu o întrebare și apasă **Trimite**.
4. Demonstrează comenzile de animație:

| Română             | Engleză          |
| ------------------ | ---------------- |
| `Fă 5 pași`        | `Walk 5 steps`   |
| `Fă 3 flotări`     | `Do 3 push-ups`  |
| `Salută`           | `Wave`           |
| `Sari`             | `Jump`           |
| `Dansează`         | `Dance`          |
| `Înoată 5 secunde` | `Swim 5 seconds` |

Numărul de repetări: cifre (`5`) sau cuvinte (`cinci`, `five`). Default: 5.

---

## Structura proiectului

```
TalkingAvatar3D/
├── START_DEMO.bat                     ← DEMO (fara Unity — doar dublu-click)
├── START.bat                          ← DEZVOLTARE (porneste si Unity Editor)
├── README.md
├── Build/                             ← generat de Unity (nu e in repo)
│   ├── TalkingAvatar3D.exe
│   └── TalkingAvatar3D_Data/
├── Assets/
│   ├── AvatarVisemeAnimator.cs        ← Lip-sync + animații corp procedural
│   ├── CompanionVisemeFace.cs         ← Față sferică cu ochi și gură animată
│   ├── OllamaStreamTester.cs          ← Client Ollama streaming + queue TTS
│   ├── TalkingAvatarBootstrap.cs      ← Init scenă + UI OnGUI + health check
│   ├── UrpMaterialGenerator.cs        ← Generator materiale URP pentru GLTFast
│   ├── VisemeTextMapper.cs            ← G2P EN / silabificare RO + Rhubarb parser
│   ├── Resources/
│   │   └── UrpLitMaterial.mat         ← Material de referință — forțează URP/Lit în build
│   ├── model.glb                      ← Model avatar
│   ├── ollama-unity/                  ← Librărie Ollama C#
│   └── Scenes/SampleScene.unity
└── Tools/
    └── TtsService/
        ├── tts_server.py              ← Server TTS (SAPI + Piper)
        ├── requirements.txt
        ├── install_romanian_voice.bat
        └── install_rhubarb.bat
```

---

## Compilare pe GitHub Actions

Workflow-ul **Build Windows Player** creează executabilul Windows la fiecare push
pe `main` sau la pornire manuală. Adaugă licența Unity ca repository secret cu
numele `UNITY_LICENSE`. Build-ul reușit apare ca artifact `TalkingAvatar3D-Windows`.

---

## Ce demonstrează proiectul

- Încărcarea unui model GLB într-o scenă Unity și animații procedurale
- Integrarea cu Ollama local, generarea răspunsurilor în streaming și interfața chat
- Serviciu TTS local pentru engleză (SAPI) și română (Piper)
- Detectarea comenzilor de animație bilingv (română + engleză)
- Estimarea visemelor din text și suport opțional Rhubarb pentru cue-uri din audio
- G2P pentru engleză: tabel 150+ cuvinte neregulate + reguli grafemice
- Silabificare pentru română: segmentare V/CV/CCV, buffer onset/coda, digrame ch/gh

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
