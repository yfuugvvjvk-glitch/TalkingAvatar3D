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
    private AudioSource audioSource;
    private GltfImport avatarImport;
    private string prompt = string.Empty;
    private int speechLanguageIndex;
    private string loadStatus = "Se încarcă avatarul...";
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

        chat = gameObject.AddComponent<OllamaStreamTester>();
        chat.audioSource = audioSource;
        chat.avatarAnimator = avatarAnimator;

        CheckOllamaAsync();
        await LoadAvatar();
    }

    private async Task LoadAvatar()
    {
        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFileName);
        string modelUri = modelPath.Contains("://") ? modelPath : new Uri(modelPath).AbsoluteUri;
        GltfImport gltf = new GltfImport();
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

            avatarAnimator.RefreshBlendShapes();
            avatarImport = gltf;
            keepImportAlive = true;
            loadStatus = "Avatar încărcat. Scrie un mesaj pentru a începe.";
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
        if (avatarImport == null)
            return;

        avatarImport.Dispose();
        avatarImport = null;
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
            ollamaStatus = "⚠ Ollama nu rulează! Pornește: ollama serve";
        }
    }

    private void OnGUI()
    {
        float width = Mathf.Min(440f, Screen.width - 24f);
        float height = Mathf.Min(600f, Screen.height - 24f);
        Rect panel = new Rect(Screen.width - width - 12f, 12f, width, height);
        GUI.Box(panel, GUIContent.none);

        GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 12f, panel.width - 28f, panel.height - 24f));
        GUILayout.Label("TALKING AVATAR 3D", TitleStyle());
        GUILayout.Label(loadStatus);
        GUILayout.Label(chat != null && chat.IsStreaming ? "Ollama: generează răspunsul..." : ollamaStatus);
        if (chat != null)
        {
            GUILayout.Label(chat.SpeechStatus);
            if (!string.IsNullOrEmpty(chat.AvatarActionStatus))
                GUILayout.Label(chat.AvatarActionStatus);
        }
        GUILayout.Label("Limba vocii");
        speechLanguageIndex = GUILayout.Toolbar(speechLanguageIndex, new[] { "English", "Română" });
        if (chat != null)
            chat.speechLanguage = speechLanguageIndex == 0 ? "English" : "Romanian";

        float historyHeight = Mathf.Max(80f, panel.height - 330f);
        chatScroll = GUILayout.BeginScrollView(chatScroll, GUILayout.Height(historyHeight));
        string history = chat != null ? Regex.Replace(chat.CurrentChatText, "<.*?>", string.Empty) : string.Empty;
        GUILayout.Label(history, HistoryStyle());
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("TalkingAvatarPrompt");
        prompt = GUILayout.TextField(prompt, GUILayout.Height(34f), GUILayout.MinWidth(100f));
        GUI.enabled = chat != null && !chat.IsStreaming && !string.IsNullOrWhiteSpace(prompt);
        if (GUILayout.Button("Trimite", GUILayout.Width(90f), GUILayout.Height(34f)))
            SendPrompt();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

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
        if (chat == null || string.IsNullOrWhiteSpace(prompt))
            return;

        string submittedPrompt = prompt;
        prompt = string.Empty;
        chat.SendPrompt(submittedPrompt);
    }

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

    private static GUIStyle HistoryStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            richText = false,
            alignment = TextAnchor.UpperLeft
        };
    }
}
