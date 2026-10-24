# Talking Avatar 3D — Grafică pe Calculator

**Aplicație Unity cu avatar 3D animat procedural, răspunsuri generate local cu Ollama și sinteză vocală.**

> Scrie un mesaj → Ollama generează răspunsul în streaming → serviciul TTS redă vocea
> → animația procedurală sincronizează mișcarea capului cu vorbirea.

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
   ├── Estimează visemele din text sau folosește cue-uri Rhubarb, dacă sunt disponibile
   ├── Animează subtil capul; modelul curent nu are blendshapes pentru gură
   └── Animații procedurale: mers, flotări, salut, sărituri, înot și dans
```

---

Visemele sunt estimate din text (reguli pentru română și engleză) sau, dacă Rhubarb
Lip Sync este instalat, extrase din audio. Animatorul folosește aceste indicii pentru
o mișcare subtilă a capului. Modelul GLB inclus nu conține blendshapes/morph targets,
deci aplicația **nu** animă forma gurii.

---

## Cerințe software

| Software          | Versiune       | Descărcare                 |
| ----------------- | -------------- | -------------------------- |
| Unity Hub + Unity | **6000.2.3f1** | https://unity.com/download |
| Ollama            | versiune curentă | https://ollama.com       |
| Python            | 3.10–3.12      | https://python.org         |

Proiectul și scripturile de lansare sunt pregătite pentru Windows. Modelul Ollama
`qwen2.5:3b` se descarcă separat (aproximativ 2 GB) la prima configurare.

### Opționale (îmbunătățesc calitatea)

| Component                  | Instalare                                                 |
| -------------------------- | --------------------------------------------------------- |
| Voce română Piper (~63 MB) | dublu-click `Tools\TtsService\install_romanian_voice.bat` |
| Rhubarb Lip Sync (~84 MB)  | dublu-click `Tools\TtsService\install_rhubarb.bat`        |

Fără Rhubarb, aplicația estimează visemele din text. Fără modelul vocal Piper,
româna folosește o voce Windows SAPI compatibilă, dacă este instalată. Niciunul
dintre aceste componente opționale nu este necesar pentru a deschide proiectul Unity.

---

## Instalare manuală (alternativă la START.bat)

```powershell
# Descarcă modelul o singură dată, apoi pornește Ollama într-un terminal
ollama pull qwen2.5:3b
ollama serve
```

Într-o a doua fereastră PowerShell, instalează și pornește serverul TTS:

```powershell
cd Tools\TtsService
py -m pip install -r requirements.txt
py tts_server.py
```

În Unity Hub, adaugă folderul repository-ului ca proiect existent, deschide-l cu
Unity **6000.2.3f1**, deschide `Assets/Scenes/SampleScene.unity` și apasă **Play**.
`START.bat` automatizează pornirea serviciilor și Unity pe Windows, dar necesită ca
Ollama, Python și Unity să fie deja instalate. Scriptul nu instalează Ollama sau Unity.

Verifică serviciile înainte de demonstrație:

- Ollama: http://localhost:11434 — trebuie să răspundă „Ollama is running”.
- TTS: http://127.0.0.1:5000/health — trebuie să răspundă cu status `ok`.

---

## Ghid de rulare și demonstrație

1. Pornește Ollama și serverul TTS, apoi deschide proiectul în Unity **6000.2.3f1**.
2. Apasă **Play**. Așteaptă confirmarea încărcării avatarului și verifică statusul
   Ollama din panoul aplicației.
3. Alege **Română** sau **English**, introdu o întrebare și apasă **Trimite**.
   Răspunsul generat local apare în istoricul chatului și este rostit de TTS.
4. Demonstrează comenzile de animație: `Fă 5 pași`, `Fă 3 flotări`, `Salut`,
   `Sari` sau `Dansează`. Comenzile sunt disponibile și prin butoanele de control.
5. Pentru cue-uri Rhubarb extrase din audio, instalează componenta opțională și
   repornește serverul TTS. Fără Rhubarb, aplicația estimează visemele din text.

### Ce demonstrează proiectul

- Încărcarea unui model GLB într-o scenă Unity și animații procedurale.
- Integrarea cu Ollama local, generarea răspunsurilor în streaming și interfața chat.
- Integrarea unui serviciu TTS local pentru engleză și română.
- Detectarea comenzilor de animație în română și engleză.
- Estimarea visemelor din text și suport opțional Rhubarb pentru cue-uri din audio.

**Limitare cunoscută:** modelul inclus nu are morph targets/blendshapes pentru gură.
Lip-sync-ul este reprezentat printr-o mișcare subtilă a capului, nu prin deformarea
buzelor.

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
│   ├── AvatarVisemeAnimator.cs        ← Lip-sync prin mișcarea capului + animații corp
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
- **TTS local** și suport opțional pentru cue-uri Rhubarb extrase din audio
- **Estimare de viseme** din text pentru română și engleză
- **Animație procedurală** a avatarului și mișcare subtilă de cap sincronizată cu vorbirea
- **Format GLB** încărcat asincron via GLTFast (modelul inclus nu are blendshapes)
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
