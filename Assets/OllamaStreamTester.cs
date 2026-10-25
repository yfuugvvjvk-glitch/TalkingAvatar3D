using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public class OllamaStreamTester : MonoBehaviour
{
    private const string ProjectIntroduction =
        "Bună! Mă numesc Petrescu Cristian, sunt student în anul al treilea la Informatică, iar acesta este proiectul meu. " +
        "Avatarul poate să meargă, să facă flotări, să salute, să ridice brațele, să sară, să înoate, să danseze și să rotească capul. " +
        "Poți să-mi pui și întrebări, iar eu îți voi răspunde.";

    [Header("Setări Ollama")]
    public string modelName = "qwen2.5:3b";

    [Header("Setări TTS")]
    public bool speakResponses = true;
    public string ttsEndpoint = "http://127.0.0.1:5000/v1/audio/speech";
    public string ttsModel    = "tts-1";
    public string ttsVoice    = "alloy";
    public string speechLanguage = "English";

    [Header("UI References")]
    public TMP_InputField inputField;
    public TMP_Text       chatDisplay;
    public AudioSource    audioSource;
    public AvatarVisemeAnimator avatarAnimator;

    // ─── Stare internă ───────────────────────────────────────────────────────
    private readonly object        streamLock   = new object();
    private readonly Queue<string> speechQueue  = new Queue<string>();
    private readonly StringBuilder speechBuffer = new StringBuilder();
    private string currentStreamedText = "";
    private bool   isStreaming;
    private bool   chatInitialized;
    private bool   standaloneSpeechActive;
    private bool   commandInProgress;

    private Coroutine ttsCoroutine;

    public string SpeechStatus     { get; private set; } = "TTS: în așteptare";
    public string AvatarActionStatus => avatarAnimator != null ? avatarAnimator.ActionStatus : string.Empty;
    public bool   IsCommandInProgress => commandInProgress;
    public bool   IsStreaming        => isStreaming || commandInProgress || ttsCoroutine != null;
    public string CurrentChatText
    {
        get { lock (streamLock) return currentStreamedText; }
    }

    public void PresentProject()
    {
        lock (streamLock)
        {
            currentStreamedText += $"\nAI: {ProjectIntroduction}\n";
            if (chatDisplay != null) chatDisplay.text = currentStreamedText;
        }
        QueueStandaloneSpeech(ProjectIntroduction);
    }

    public void ReportAvatarCommand(string command, string actionDescription, bool executed)
    {
        if (IsStreaming) return;
        commandInProgress = true;
        AvatarVisemeAnimator.InputBlocked = true;
        string response = GetCommandResponse(executed, actionDescription);
        lock (streamLock)
        {
            currentStreamedText += $"\nTu: {command}\nAI: {response}\n";
            if (CanSpeak) speechQueue.Enqueue(response);
            if (chatDisplay != null) chatDisplay.text = currentStreamedText;
        }
        StartStandaloneTtsWorker();
        StartCoroutine(WaitForCommandCompletion());
    }

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    public void SendPrompt()
    {
        if (inputField == null) return;
        string p = inputField.text.Trim();
        inputField.text = "";
        if (!string.IsNullOrEmpty(p)) SendPrompt(p);
    }

    public async void SendPrompt(string prompt)
    {
        if (IsStreaming || string.IsNullOrWhiteSpace(prompt)) return;

        bool actionExecuted = TryStartAvatarAction(prompt, out bool commandRecognized, out string actionDescription);

        lock (streamLock)
        {
            string prev = chatDisplay != null ? chatDisplay.text : currentStreamedText;
            currentStreamedText = prev + $"\nTu: {prompt}\nAI: ";
        }
        if (chatDisplay != null) chatDisplay.text = currentStreamedText;

        lock (streamLock)
        {
            speechBuffer.Length = 0;
            speechQueue.Clear();
        }

        isStreaming = true;
        AvatarVisemeAnimator.InputBlocked = true;

        if (speakResponses && audioSource != null && !string.IsNullOrWhiteSpace(ttsEndpoint))
        {
            if (ttsCoroutine != null)
            {
                StopCoroutine(ttsCoroutine);
                ttsCoroutine = null;
            }
            ttsCoroutine = StartCoroutine(TtsWorker());
            Debug.Log("[TTS] Coroutine pornita pentru: " + prompt.Substring(0, Mathf.Min(30, prompt.Length)));
        }
        else if (speakResponses && audioSource == null)
        {
            Debug.LogWarning("[TTS] AudioSource lipsește — vocea nu va funcționa.");
        }
        else if (!speakResponses)
        {
            Debug.LogWarning("[TTS] speakResponses = false — vocea dezactivata.");
        }

        try
        {
            if (commandRecognized)
            {
                commandInProgress = true;
                AvatarVisemeAnimator.InputBlocked = true;
                string response = GetCommandResponse(actionExecuted, actionDescription);
                lock (streamLock)
                {
                    currentStreamedText += response;
                    if (CanSpeak) speechQueue.Enqueue(response);
                    if (chatDisplay != null) chatDisplay.text = currentStreamedText;
                }
                StartCoroutine(WaitForCommandCompletion());
                return;
            }

            if (!chatInitialized)
            {
                ollama.Ollama.InitChat(8, BuildSystemPrompt());
                chatInitialized = true;
            }
            else
            {
                ollama.Ollama.SetSystemPrompt(BuildSystemPrompt());
            }

            await ollama.Ollama.ChatStream(OnChunkReceived, modelName, prompt);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            lock (streamLock)
            {
                string tail = speechBuffer.ToString().Trim();
                speechBuffer.Length = 0;
                if (speakResponses && !string.IsNullOrWhiteSpace(tail))
                    speechQueue.Enqueue(tail);

                if (chatDisplay != null)
                    chatDisplay.text = currentStreamedText;
            }
            isStreaming = false;
        }
    }

    private static string GetCommandResponse(bool executed, string actionDescription)
    {
        if (!executed) return "Nu pot executa această acțiune acum.";
        return $"Acum {actionDescription}.";
    }

    private IEnumerator WaitForCommandCompletion()
    {
        while (avatarAnimator != null && avatarAnimator.IsPerformingAction)
            yield return null;

        while ((ttsCoroutine != null || audioSource != null && audioSource.isPlaying) &&
               (speakResponses && audioSource != null && !string.IsNullOrWhiteSpace(ttsEndpoint)))
            yield return null;

        commandInProgress = false;
    }

    private bool CanSpeak => speakResponses && audioSource != null &&
                             !string.IsNullOrWhiteSpace(ttsEndpoint);

    private void QueueStandaloneSpeech(string phrase)
    {
        if (!speakResponses || audioSource == null || string.IsNullOrWhiteSpace(ttsEndpoint))
            return;

        lock (streamLock) speechQueue.Enqueue(phrase);
        StartStandaloneTtsWorker();
    }

    private void StartStandaloneTtsWorker()
    {
        if (!speakResponses || audioSource == null || string.IsNullOrWhiteSpace(ttsEndpoint) ||
            ttsCoroutine != null)
            return;

        if (!isStreaming)
        {
            isStreaming = true;
            standaloneSpeechActive = true;
        }
        ttsCoroutine = StartCoroutine(TtsWorker());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Callbacks streaming Ollama
    // ════════════════════════════════════════════════════════════════════════

    private void OnChunkReceived(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        lock (streamLock)
        {
            currentStreamedText += chunk;
            if (speakResponses)
            {
                speechBuffer.Append(chunk);
                FlushCompletedSentences();
            }
        }
    }

    private void FlushCompletedSentences()
    {
        int sentenceEnd;
        while ((sentenceEnd = IndexOfSentenceEnd(speechBuffer)) >= 0)
        {
            string sentence = speechBuffer.ToString(0, sentenceEnd + 1).Trim();
            speechBuffer.Remove(0, sentenceEnd + 1);
            if (!string.IsNullOrWhiteSpace(sentence))
                speechQueue.Enqueue(sentence);
        }

        if (speechBuffer.Length >= 100)
        {
            int cut = speechBuffer.ToString().LastIndexOf(' ');
            if (cut > 0)
            {
                string chunk = speechBuffer.ToString(0, cut).Trim();
                speechBuffer.Remove(0, cut + 1);
                if (!string.IsNullOrWhiteSpace(chunk))
                    speechQueue.Enqueue(chunk);
            }
        }
    }

    private static int IndexOfSentenceEnd(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == '.' || sb[i] == '!' || sb[i] == '?' || sb[i] == '\n')
                return i;
        return -1;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Corutina TTS
    // ════════════════════════════════════════════════════════════════════════

    private IEnumerator TtsWorker()
    {
        while (true)
        {
            string phrase = null;
            lock (streamLock)
            {
                if (speechQueue.Count > 0)
                    phrase = speechQueue.Dequeue();
            }

            if (phrase == null)
            {
                if (standaloneSpeechActive)
                {
                    standaloneSpeechActive = false;
                    isStreaming = false;
                    ttsCoroutine = null;
                    yield break;
                }
                if (!isStreaming)
                {
                    ttsCoroutine = null;
                    yield break;
                }
                yield return null;
                continue;
            }

            yield return SendToTts(phrase);
        }
    }

    private IEnumerator SendToTts(string phrase)
    {
        var request = new SpeechRequest
        {
            model           = ttsModel,
            input           = phrase,
            voice           = ttsVoice,
            language        = speechLanguage,
            response_format = "wav"
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));

        using (var webReq = new UnityWebRequest(ttsEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            webReq.uploadHandler   = new UploadHandlerRaw(body);
            webReq.downloadHandler = new DownloadHandlerAudioClip(ttsEndpoint, AudioType.WAV);
            webReq.SetRequestHeader("Content-Type", "application/json");

            yield return webReq.SendWebRequest();

            if (webReq.result != UnityWebRequest.Result.Success)
            {
                string errBody = webReq.downloadHandler?.data != null
                    ? Encoding.UTF8.GetString(webReq.downloadHandler.data)
                    : "";
                var errObj = string.IsNullOrEmpty(errBody)
                    ? null : JsonUtility.FromJson<SpeechError>(errBody);
                string msg = errObj?.error ?? webReq.error;
                SpeechStatus = $"TTS eroare ({webReq.responseCode}): {msg}";
                Debug.LogError(SpeechStatus);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(webReq);
            if (clip == null)
            {
                SpeechStatus = "TTS: WAV invalid.";
                Debug.LogError(SpeechStatus);
                yield break;
            }

            string timelineJson  = null;
            string encodedHeader = webReq.GetResponseHeader("X-Viseme-Timeline");
            if (!string.IsNullOrEmpty(encodedHeader))
            {
                try { timelineJson = Encoding.UTF8.GetString(Convert.FromBase64String(encodedHeader)); }
                catch { /* header corupt — fallback text */ }
            }

            string visemeMode = webReq.GetResponseHeader("X-Viseme-Mode") ?? "";
            string ttsEngine  = webReq.GetResponseHeader("X-TTS-Engine")  ?? "audio";

            SpeechStatus = visemeMode.Equals("rhubarb", StringComparison.OrdinalIgnoreCase)
                ? $"TTS: {ttsEngine} + Rhubarb"
                : $"TTS: {ttsEngine} + estimare fonematică";

            if (avatarAnimator != null)
                avatarAnimator.PlaySpeech(phrase, clip.length, speechLanguage, timelineJson);

            audioSource.clip = clip;
            audioSource.Play();

            while (audioSource.isPlaying)
                yield return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Detecție comenzi de animație
    // ════════════════════════════════════════════════════════════════════════

    private bool TryStartAvatarAction(string prompt, out bool commandRecognized, out string actionDescription)
    {
        commandRecognized = false;
        actionDescription = string.Empty;
        string norm = prompt.ToLowerInvariant()
            .Replace("ă","a").Replace("â","a").Replace("î","i")
            .Replace("ș","s").Replace("ş","s").Replace("ț","t").Replace("ţ","t");

        // ── Ridică brațele / mâinile sus (prioritate față de wave simplu) ──
        if (Regex.IsMatch(norm, @"\b(ridica\s+mainile|ridica\s+mainile\s+sus|ridica\s+bratele|raise\s+(arms?|hands?))\b"))
        {
            commandRecognized = true;
            actionDescription = "ridic brațele";
            return avatarAnimator != null && avatarAnimator.StartRaisingArms();
        }

        // ── Wave / salut ───────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(salut[a]?|wave|misca\s+brat|ridica\s+brat|agit[a]?\s+brat)\b"))
        {
            commandRecognized = true;
            actionDescription = "salut";
            return avatarAnimator != null && avatarAnimator.StartWaving();
        }

        // ── Înot ───────────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(inoata|inot|swimming|swim)\b"))
        {
            int sec = ParseActionCount(norm, norm, 5);
            commandRecognized = true;
            actionDescription = "înot";
            return avatarAnimator != null && avatarAnimator.StartSwimming(sec);
        }

        // ── Săritură ───────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(sari|saritura|jump|sara)\b"))
        {
            if (Regex.IsMatch(norm, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$"))
                return false;
            int reps = ParseActionCount(norm, norm, 3);
            commandRecognized = true;
            actionDescription = "sar";
            return avatarAnimator != null && avatarAnimator.StartJumping(reps);
        }

        // ── Dans ───────────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(danseaza|dans|dance)\b"))
        {
            commandRecognized = true;
            actionDescription = "dansez";
            return avatarAnimator != null && avatarAnimator.StartDancing();
        }

        // ── Rotește capul ──────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(roteste\s+cap(ul)?|turn\s+head)\b"))
        {
            commandRecognized = true;
            actionDescription = "rotesc capul";
            return avatarAnimator != null && avatarAnimator.StartHeadRotation(45f, 60f);
        }

        // ── Flotări ────────────────────────────────────────────────────────
        Match mPush = Regex.Match(norm, @"\b(flotari|flotare|push[\s-]?ups?)\b");
        if (mPush.Success)
        {
            if (Regex.IsMatch(norm.Substring(0, mPush.Index), @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$"))
                return false;
            string before = norm.Substring(0, mPush.Index);
            string after  = norm.Substring(mPush.Index + mPush.Length);
            int reps = ParseActionCount(before, after, 5);
            commandRecognized = true;
            actionDescription = reps == 1 ? "fac o flotare" : $"fac {reps} flotări";
            return avatarAnimator != null && avatarAnimator.StartPushUps(reps);
        }

        // ── Mers ───────────────────────────────────────────────────────────
        Match mWalk = Regex.Match(norm, @"\b(pasi?|mers|mergi|mergeti|walk(?:ing)?|steps?)\b");
        if (!mWalk.Success) return false;

        string beforeWalk = norm.Substring(0, mWalk.Index);
        if (Regex.IsMatch(beforeWalk, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$")) return false;

        int steps = ParseActionCount(beforeWalk, norm.Substring(mWalk.Index + mWalk.Length), 5);
        commandRecognized = true;
        actionDescription = steps == 1 ? "fac un pas" : $"fac {steps} pași";
        return avatarAnimator != null && avatarAnimator.StartWalking(steps);
    }

    private static int ParseActionCount(string before, string after, int defaultVal)
    {
        Match n = Regex.Match(before, @"\b(\d{1,2})\b");
        if (!n.Success) n = Regex.Match(after, @"\b(\d{1,2})\b");
        if (n.Success && int.TryParse(n.Groups[1].Value, out int c))
            return Mathf.Clamp(c, 1, 50);

        string[] words = {
            "unu|una|one","doi|doua|two","trei|three","patru|four","cinci|five",
            "sase|six","sapte|seven","opt|eight","noua|nine","zece|ten"
        };
        for (int i = 0; i < words.Length; i++)
            if (Regex.IsMatch(before, $@"\b({words[i]})\b") ||
                Regex.IsMatch(after,  $@"\b({words[i]})\b"))
                return i + 1;
        return defaultVal;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Prompt sistem
    // ════════════════════════════════════════════════════════════════════════

    private string BuildSystemPrompt()
    {
        bool ro = speechLanguage.StartsWith("ro", StringComparison.OrdinalIgnoreCase) ||
                  speechLanguage.StartsWith("román", StringComparison.OrdinalIgnoreCase) ||
                  speechLanguage.StartsWith("romanian", StringComparison.OrdinalIgnoreCase);
        if (ro)
            return "Ești asistentul vocal al unui avatar 3D care îl ajută pe studentul Petrescu Cristian să-și prezinte proiectul. " +
                   "Răspunde în română, cu diacritice, în 1-3 propoziții scurte și simple. " +
                   "Poți răspunde la întrebări generale și de nivel introductiv despre proiect, informatică, " +
                   "geografie, știință și cultură generală. Nu pretinde că poți explica detalii avansate sau " +
                   "că știi informații care nu au fost furnizate. Dacă întrebarea este prea complicată, neclară " +
                   "sau nu cunoști răspunsul, spune sincer acest lucru și invită utilizatorul să întrebe ceva mai simplu. " +
                   "Nu inventa fapte. Comenzile avatarului sunt executate separat de aplicație.";
        return "You are the voice assistant for a 3D avatar helping student Petrescu Cristian present his project. " +
               "Reply in English using 1-3 short, simple sentences. Answer general and introductory questions about " +
               "the project, computer science, geography, science, and general knowledge. Do not claim advanced expertise " +
               "or knowledge of details that were not provided. If a question is too complex, unclear, or you do not know " +
               "the answer, say so honestly and invite the user to ask something simpler. Do not invent facts. " +
               "Avatar commands are executed separately by the application.";
    }

    // ════════════════════════════════════════════════════════════════════════
    // Update — sincronizare UI
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (chatDisplay == null) return;
        string text;
        lock (streamLock) text = currentStreamedText;
        if (chatDisplay.text != text) chatDisplay.text = text;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Clase serializabile pentru JSON
    // ════════════════════════════════════════════════════════════════════════

    [Serializable] private class SpeechRequest
    {
        public string model;
        public string input;
        public string voice;
        public string language;
        public string response_format;
    }

    [Serializable] private class SpeechError
    {
        public string error = string.Empty;
    }
}
