using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

public class TalkingAvatarBootstrap : MonoBehaviour
{
    [SerializeField] private string modelFileName = "model.glb";
    [SerializeField] private float cameraDistance = 3.4f;

    private OllamaStreamTester chat;
    private AvatarVisemeAnimator avatarAnimator;
    private AnimatedJawHead jawHead;
    private CompanionVisemeFace companionFace;
    private AudioSource audioSource;
    private GltfImport avatarImport;
    private string prompt = string.Empty;
    private int speechLanguageIndex;
    private string loadStatus = string.Empty;
    private string ollamaStatus = "Ollama: se verifică...";
    private Vector2 chatScroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateApplication()
    {
        if (FindAnyObjectByType<TalkingAvatarBootstrap>() != null)
            return;

        GameObject application = new GameObject("Talking Avatar Application");
        application.AddComponent<TalkingAvatarBootstrap>();
    }

    private async void Start()
    {
        speechLanguageIndex = 1;
        Camera sceneCamera = Camera.main;
        if (sceneCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            sceneCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        sceneCamera.transform.position = new Vector3(0f, 1.05f, cameraDistance);
        sceneCamera.transform.LookAt(new Vector3(0f, 1.05f, 0f));
        sceneCamera.fieldOfView = 38f;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        avatarAnimator = gameObject.AddComponent<AvatarVisemeAnimator>();
        jawHead = gameObject.AddComponent<AnimatedJawHead>();
        companionFace = gameObject.AddComponent<CompanionVisemeFace>();

        chat = gameObject.AddComponent<OllamaStreamTester>();
        chat.audioSource = audioSource;
        chat.avatarAnimator = avatarAnimator;
        chat.jawHead = jawHead;
        chat.companionFace = companionFace;
        chat.speechLanguage = "Romanian";

        CheckOllamaAsync();
        await LoadAvatar();
        if (avatarAnimator.AvatarRoot != null)
            companionFace.Initialize(sceneCamera, avatarAnimator.AvatarRoot);

        if (avatarAnimator.HeadBone != null)
        {
            jawHead.avatarHeadBone = avatarAnimator.HeadBone;
            jawHead.avatarRoot = avatarAnimator.AvatarRoot;
            jawHead.speechAudioSource = audioSource;
            loadStatus = jawHead.AttachToHead()
                ? string.Empty
                : "Avatarul este încărcat, dar masca nu s-a putut atașa. Verifică mesajele din Console.";
        }
        else
        {
            loadStatus = "Avatarul nu are osul Head necesar pentru mască.";
            Debug.LogError("[Mask] Avatarul a fost încărcat fără osul Head; masca nu poate fi atașată.");
        }

        if (avatarAnimator.AvatarRoot != null)
            chat.QueueAutomaticIntroduction(
                "Bună! Sunt Pertescu Cristian, student în anul al III-lea la Informatică. " +
                "Acesta este avatarul meu: poate vorbi, se poate deplasa cu ajutorul tastaturii " +
                "și poate executa comenzi simple (din butoane ori din disutie). Pot conversa și în limba română, dar uneori am " +
                "nevoie de puțin mai mult timp ca să procesez ce îmi spui. Îmi poți da o comandă " +
                "sau îmi poți pune o întrebare.");
    }

    private async Task LoadAvatar()
    {
        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFileName);
        string modelUri = modelPath.Contains("://") ? modelPath : new Uri(modelPath).AbsoluteUri;
        GltfImport gltf = new GltfImport(materialGenerator: new UrpMaterialGenerator());
        bool keepImportAlive = false;

        try
        {
            bool loaded = await gltf.Load(modelUri);
            if (!loaded)
            {
                loadStatus = $"Nu s-a putut încărca avatarul: {modelPath}";
                Debug.LogError(loadStatus);
                return;
            }

            int childCountBeforeInstantiation = transform.childCount;
            bool instantiated = await gltf.InstantiateMainSceneAsync(transform);
            if (!instantiated)
            {
                loadStatus = "Avatarul a fost citit, dar nu a putut fi creat în scenă.";
                Debug.LogError(loadStatus);
                return;
            }

            if (transform.childCount > childCountBeforeInstantiation)
                avatarAnimator.SetAvatarRoot(transform.GetChild(childCountBeforeInstantiation));

            FixMagentaMaterials(transform);

            avatarAnimator.RefreshBlendShapes();
            avatarImport = gltf;
            keepImportAlive = true;
        }
        catch (Exception exception)
        {
            loadStatus = $"Eroare la încărcarea avatarului: {exception.Message}";
            Debug.LogException(exception);
        }
        finally
        {
            if (!keepImportAlive)
                gltf.Dispose();
        }
    }

    private void OnDestroy()
    {
        if (avatarImport == null) return;
        avatarImport.Dispose();
        avatarImport = null;
    }

    /// <summary>
    /// Înlocuiește materialele magenta (eroare shader) de pe avatarul GLB cu un material URP valid.
    /// GLTFast folosește shader graphs proprii care pot lipsi din build dacă nu sunt referite explicit.
    /// Această metodă garantează că avatarul are culori corecte indiferent de shaderele disponibile.
    /// </summary>
    private static void FixMagentaMaterials(Transform root)
    {
        // Găsim shader-ul URP Lit — garantat în build prin AlwaysIncludedShaders
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpLit == null)
        {
            Debug.LogWarning("[Bootstrap] URP Lit shader nu e disponibil în build.");
            return;
        }

        foreach (Renderer rend in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = rend.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material src = mats[i];
                if (src == null) { changed = true; mats[i] = new Material(urpLit); continue; }

                string sName = src.shader != null ? src.shader.name : "NULL";
                Debug.Log($"[FixMaterials] {rend.name}[{i}] shader={sName}");

                // Dacă shader-ul e URP nativ — nu atingem materialul
                if (sName.StartsWith("Universal Render Pipeline")) continue;

                // Orice alt shader (GLTFast ShaderGraph, Hidden, etc.) — înlocuim
                Material mat = new Material(urpLit);

                // Copiem culoarea de bază
                if (src.HasProperty("baseColorFactor"))
                    mat.SetColor("_BaseColor", src.GetColor("baseColorFactor"));
                else if (src.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                else if (src.HasProperty("_Color"))
                    mat.SetColor("_BaseColor", src.GetColor("_Color"));

                // Copiem textura dacă există
                if (src.HasProperty("baseColorTexture") && src.GetTexture("baseColorTexture") != null)
                    mat.SetTexture("_BaseMap", src.GetTexture("baseColorTexture"));
                else if (src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null)
                    mat.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                else if (src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null)
                    mat.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));

                mats[i] = mat;
                changed = true;
            }
            if (changed)
                rend.sharedMaterials = mats;
        }
    }


    /// <summary>Verifică dacă Ollama este pornit și actualizează statusul în UI.</summary>
    private async void CheckOllamaAsync()
    {
        ollamaStatus = "Ollama: se verifică...";
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://localhost:11434/");
            req.Timeout = 3000;
            req.Method = "GET";
            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            {
                ollamaStatus = resp.StatusCode == HttpStatusCode.OK
                    ? "Ollama: ✓ pornit (localhost:11434)"
                    : $"Ollama: răspuns {(int)resp.StatusCode}";
            }
        }
        catch
        {
            ollamaStatus = "⚠ Ollama nu răspunde pe localhost:11434.";
        }
    }

    private void OnGUI()
    {
        float width = Mathf.Min(460f, Screen.width - 24f);
        float height = Mathf.Min(680f, Screen.height - 24f);
        Rect panel = new Rect(Screen.width - width - 12f, 12f, width, height);
        GUI.Box(panel, GUIContent.none);

        GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 12f, panel.width - 28f, panel.height - 24f));

        // ── Titlu + status ─────────────────────────────────────────────────
        GUILayout.Label("TALKING AVATAR 3D", TitleStyle());
        if (!string.IsNullOrEmpty(loadStatus))
            GUILayout.Label(loadStatus);
        GUILayout.Label(ollamaStatus);
        if (chat != null)
        {
            GUILayout.Label(chat.SpeechStatus);
            if (!string.IsNullOrEmpty(chat.AvatarActionStatus))
                GUILayout.Label(chat.AvatarActionStatus);
        }

        // ── Limbă ──────────────────────────────────────────────────────────
        GUILayout.Label("Limba vocii");
        speechLanguageIndex = GUILayout.Toolbar(speechLanguageIndex, new[] { "English", "Română" });
        if (chat != null)
            chat.speechLanguage = speechLanguageIndex == 0 ? "English" : "Romanian";

        // ── Istoric chat ───────────────────────────────────────────────────
        float historyHeight = Mathf.Max(60f, panel.height - 420f);
        chatScroll = GUILayout.BeginScrollView(chatScroll, GUILayout.Height(historyHeight));
        string history = chat != null
            ? Regex.Replace(chat.CurrentChatText, "<.*?>", string.Empty)
            : string.Empty;
        GUILayout.Label(history, HistoryStyle());
        GUILayout.EndScrollView();

        // ── Input + Trimite ────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("TalkingAvatarPrompt");
        prompt = GUILayout.TextField(prompt, GUILayout.Height(34f), GUILayout.MinWidth(100f));
        GUI.enabled = chat != null && chat.CanSubmitPrompt(prompt) &&
                      !string.IsNullOrWhiteSpace(prompt);
        if (GUILayout.Button("Trimite", GUILayout.Width(90f), GUILayout.Height(34f)))
            SendPrompt();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // Blocam inputul tastaturii avatarului cand userul scrie in chat
        bool chatFocused = GUI.GetNameOfFocusedControl() == "TalkingAvatarPrompt";
        bool controlsBlocked = chatFocused && !string.IsNullOrWhiteSpace(prompt);
        AvatarVisemeAnimator.InputBlocked = controlsBlocked;
        if (companionFace != null)
            companionFace.InputBlocked = controlsBlocked;

        // Buton reset pozitie (daca avatarul a zburat)
        GUI.enabled = chat != null;
        if (avatarAnimator != null)
        {
            if (GUILayout.Button("↺ Reset pozitie", BtnStyle()))
                avatarAnimator.ResetPosition();
        }

        // ── Control direct avatar ──────────────────────────────────────────
        GUILayout.Space(6f);
        GUILayout.Label("Control direct avatar:", SubtitleStyle());

        // Rând 1: animații principale
        GUI.enabled = chat != null && chat.CanSubmitPrompt(string.Empty);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Wave", BtnStyle()))
            RunAvatarCommand("Salută", "salut", () => avatarAnimator != null && avatarAnimator.StartWaving());
        if (GUILayout.Button("Ridică brațe", BtnStyle()))
            RunAvatarCommand("Ridică brațele", "ridic brațele", () => avatarAnimator != null && avatarAnimator.StartRaisingArms());
        if (GUILayout.Button("Sari", BtnStyle()))
            RunAvatarCommand("Sari", "sar", () => avatarAnimator != null && avatarAnimator.StartJumping(3));
        if (GUILayout.Button("Înoată", BtnStyle()))
            RunAvatarCommand("Înoată", "înot", () => avatarAnimator != null && avatarAnimator.StartSwimming(3));
        GUILayout.EndHorizontal();

        // Rând 2: animații secundare
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Dans", BtnStyle()))
            RunAvatarCommand("Dansează", "dansez", () => avatarAnimator != null && avatarAnimator.StartDancing());
        if (GUILayout.Button("Rotește cap", BtnStyle()))
            RunAvatarCommand("Rotește capul", "rotesc capul", () => avatarAnimator != null && avatarAnimator.StartHeadRotation(45f, 60f));
        if (GUILayout.Button("Flotări 3", BtnStyle()))
            RunAvatarCommand("Fă 3 flotări", "fac 3 flotări", () => avatarAnimator != null && avatarAnimator.StartPushUps(3));
        if (GUILayout.Button("Mers 5", BtnStyle()))
            RunAvatarCommand("Fă 5 pași", "fac 5 pași", () => avatarAnimator != null && avatarAnimator.StartWalking(5));
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        // Rând 3: rotire avatar
        GUI.enabled = chat != null;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("← Rotire", BtnStyle(), GUILayout.ExpandWidth(true))) avatarAnimator?.RotateAvatar(-45f);
        if (GUILayout.Button("Rotire →", BtnStyle(), GUILayout.ExpandWidth(true))) avatarAnimator?.RotateAvatar(45f);
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        // Legendă taste
        GUILayout.Space(4f);
        GUILayout.Label("Taste: 1=Wave 2=Mers 3=Flotări 4=Sărit 5=Înot 6=Dans 7=Brațe 8=Cap\n" +
                        "WASD=avatar  QE=cap  Space=salt  IJKL=fața zburătoare",
            SmallStyle());

        GUILayout.EndArea();

        // ── Enter trimite prompt ───────────────────────────────────────────
        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Return &&
            GUI.GetNameOfFocusedControl() == "TalkingAvatarPrompt")
        {
            SendPrompt();
            current.Use();
        }

    }

    private void SendPrompt()
    {
        if (chat == null || string.IsNullOrWhiteSpace(prompt) || !chat.CanSubmitPrompt(prompt)) return;
        string submittedPrompt = prompt;
        prompt = string.Empty;
        GUI.FocusControl(null);
        AvatarVisemeAnimator.InputBlocked = false;
        chat.SendPrompt(submittedPrompt);
    }

    private void RunAvatarCommand(string command, string actionDescription, Func<bool> execute)
    {
        if (chat == null) return;
        chat.ReportAvatarCommand(command, actionDescription, execute);
    }

    // ── Stiluri GUI ─────────────────────────────────────────────────────────

    private static GUIStyle TitleStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            margin = new RectOffset(4, 4, 4, 8)
        };
    }

    private static GUIStyle SubtitleStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            margin = new RectOffset(0, 0, 6, 2)
        };
    }

    private static GUIStyle HistoryStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            richText = false,
            alignment = TextAnchor.UpperLeft
        };
    }

    private static GUIStyle BtnStyle()
    {
        return new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            margin = new RectOffset(2, 2, 2, 2)
        };
    }

    private static GUIStyle SmallStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
    }
}
