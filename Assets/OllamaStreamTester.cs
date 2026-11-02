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
    public AnimatedJawHead jawHead;
    public CompanionVisemeFace companionFace;

    // ─── Stare internă ───────────────────────────────────────────────────────
    private readonly object        streamLock   = new object();
    private readonly Queue<SpeechItem> speechQueue = new Queue<SpeechItem>();
    private readonly StringBuilder speechBuffer = new StringBuilder();
    private string currentStreamedText = "";
    private bool   isStreaming;
    private bool   chatInitialized;
    private bool   standaloneSpeechActive;

    private Coroutine ttsCoroutine;

    public string SpeechStatus     { get; private set; } = "TTS: în așteptare";
    public string AvatarActionStatus => avatarAnimator != null ? avatarAnimator.ActionStatus : string.Empty;
    public bool   IsCommandInProgress => avatarAnimator != null && avatarAnimator.IsPerformingAction;
    public bool   IsStreaming        => isStreaming || ttsCoroutine != null;
    public string CurrentChatText
    {
        get { lock (streamLock) return currentStreamedText; }
    }

    public bool CanSubmitPrompt(string prompt)
    {
        return !IsStreaming && (avatarAnimator == null || !avatarAnimator.IsPerformingAction);
    }

    public void ReportAvatarCommand(string command, string actionDescription, Func<bool> startAction)
    {
        QueueAvatarCommand(command, actionDescription, startAction);
    }

    public void QueueAutomaticIntroduction(string introduction)
    {
        if (string.IsNullOrWhiteSpace(introduction)) return;

        lock (streamLock)
        {
            currentStreamedText += $"\nAI: {introduction}\n";
            if (CanSpeak) EnqueueSpeech(introduction);
            if (chatDisplay != null) chatDisplay.text = currentStreamedText;
        }

        if (CanSpeak) StartStandaloneTtsWorker();
    }

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    public void SendPrompt()
    {
        if (inputField == null) return;
        string p = inputField.text.Trim();
        if (string.IsNullOrEmpty(p) || !CanSubmitPrompt(p)) return;
        inputField.text = "";
        SendPrompt(p);
    }

    public async void SendPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || !CanSubmitPrompt(prompt)) return;

        bool commandRecognized = TryStartAvatarAction(prompt, out string actionDescription,
            out Func<bool> startAction);
        if (commandRecognized)
        {
            QueueAvatarCommand(prompt, actionDescription, startAction);
            return;
        }
        if (IsStreaming) return;

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
            lock (streamLock)
            {
                currentStreamedText += $"\n\nEroare la Ollama: {ex.Message}\n";
                if (chatDisplay != null)
                    chatDisplay.text = currentStreamedText;
            }
        }
        finally
        {
            lock (streamLock)
            {
                string tail = speechBuffer.ToString().Trim();
                speechBuffer.Length = 0;
                if (speakResponses && !string.IsNullOrWhiteSpace(tail))
                    EnqueueSpeech(tail);

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

    private void QueueAvatarCommand(string command, string actionDescription, Func<bool> startAction)
    {
        if (IsStreaming || (avatarAnimator != null && avatarAnimator.IsPerformingAction))
            return;

        if (CanSpeak)
        {
            string response = GetCommandResponse(true, actionDescription);
            lock (streamLock)
            {
                currentStreamedText += $"\nTu: {command}\nAI: {response}\n";
                EnqueueSpeech(response, () =>
                {
                    if (startAction == null || !startAction())
                        AppendCommandFailure();
                });
                if (chatDisplay != null) chatDisplay.text = currentStreamedText;
            }
            StartStandaloneTtsWorker();
            return;
        }

        bool executed = startAction != null && startAction();
        lock (streamLock)
        {
            currentStreamedText += $"\nTu: {command}\nAI: {GetCommandResponse(executed, actionDescription)}\n";
            if (chatDisplay != null) chatDisplay.text = currentStreamedText;
        }
    }

    private void AppendCommandFailure()
    {
        lock (streamLock)
        {
            currentStreamedText += "Comanda nu a putut fi executată.\n";
            if (chatDisplay != null) chatDisplay.text = currentStreamedText;
        }
        Debug.LogWarning("Comanda avatarului nu a putut porni după confirmarea vocală.");
    }

    private bool CanSpeak => speakResponses && audioSource != null &&
                             !string.IsNullOrWhiteSpace(ttsEndpoint);

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

    private void EnqueueSpeech(string phrase, Action afterPlayback = null)
    {
        if (!string.IsNullOrWhiteSpace(phrase))
            speechQueue.Enqueue(new SpeechItem(phrase, afterPlayback));
        else
            afterPlayback?.Invoke();
    }

    private sealed class SpeechItem
    {
        public readonly string Phrase;
        public readonly Action AfterPlayback;

        public SpeechItem(string phrase, Action afterPlayback)
        {
            Phrase = phrase;
            AfterPlayback = afterPlayback;
        }
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
                EnqueueSpeech(sentence);
        }

        if (speechBuffer.Length >= 100)
        {
            int cut = speechBuffer.ToString().LastIndexOf(' ');
            if (cut > 0)
            {
                string chunk = speechBuffer.ToString(0, cut).Trim();
                speechBuffer.Remove(0, cut + 1);
                if (!string.IsNullOrWhiteSpace(chunk))
                    EnqueueSpeech(chunk);
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
            SpeechItem item = null;
            lock (streamLock)
            {
                if (speechQueue.Count > 0)
                    item = speechQueue.Dequeue();
            }

            if (item == null)
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

            yield return SendToTts(item);
        }
    }

    private IEnumerator SendToTts(SpeechItem item)
    {
        SpeechStatus = "TTS: se generează vocea...";
        var request = new SpeechRequest
        {
            model           = ttsModel,
            input           = item.Phrase,
            voice           = ttsVoice,
            language        = speechLanguage,
            response_format = "wav"
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));

        using (var webReq = new UnityWebRequest(ttsEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            webReq.timeout = 60;
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
                item.AfterPlayback?.Invoke();
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(webReq);
            if (clip == null)
            {
                SpeechStatus = "TTS: WAV invalid.";
                Debug.LogError(SpeechStatus);
                item.AfterPlayback?.Invoke();
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
                avatarAnimator.PlaySpeech(item.Phrase, clip.length, speechLanguage, timelineJson);
            if (jawHead != null)
                jawHead.PlaySpeech(item.Phrase, clip.length, speechLanguage, timelineJson);
            if (companionFace != null)
                companionFace.PlaySpeech(item.Phrase, clip.length, speechLanguage, timelineJson);

            audioSource.clip = clip;
            audioSource.Play();

            while (audioSource.isPlaying)
                yield return null;

            item.AfterPlayback?.Invoke();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Detecție comenzi de animație
    // ════════════════════════════════════════════════════════════════════════

    private bool TryStartAvatarAction(string prompt, out string actionDescription,
        out Func<bool> startAction)
    {
        actionDescription = string.Empty;
        startAction = null;
        string norm = prompt.ToLowerInvariant()
            .Replace("ă","a").Replace("â","a").Replace("î","i")
            .Replace("ș","s").Replace("ş","s").Replace("ț","t").Replace("ţ","t");

        // ── Ridică brațele / mâinile sus (prioritate față de wave simplu) ──
        if (Regex.IsMatch(norm, @"\b(ridica\s+mainile|ridica\s+mainile\s+sus|ridica\s+bratele|raise\s+(arms?|hands?))\b"))
        {
            actionDescription = "ridic brațele";
            startAction = () => avatarAnimator != null && avatarAnimator.StartRaisingArms();
            return true;
        }

        // ── Wave / salut ───────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(salut[a]?|wave|misca\s+brat|ridica\s+brat|agit[a]?\s+brat)\b"))
        {
            actionDescription = "salut";
            startAction = () => avatarAnimator != null && avatarAnimator.StartWaving();
            return true;
        }

        // ── Înot ───────────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(inoata|inot|swimming|swim)\b"))
        {
            int sec = ParseActionCount(norm, norm, 5);
            actionDescription = "înot";
            startAction = () => avatarAnimator != null && avatarAnimator.StartSwimming(sec);
            return true;
        }

        // ── Săritură ───────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(sari|saritura|jump|sara)\b"))
        {
            if (Regex.IsMatch(norm, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$"))
                return false;
            int reps = ParseActionCount(norm, norm, 3);
            actionDescription = "sar";
            startAction = () => avatarAnimator != null && avatarAnimator.StartJumping(reps);
            return true;
        }

        // ── Dans ───────────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(danseaza|dans|dance)\b"))
        {
            actionDescription = "dansez";
            startAction = () => avatarAnimator != null && avatarAnimator.StartDancing();
            return true;
        }

        // ── Rotește capul ──────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(roteste\s+cap(ul)?|turn\s+head)\b"))
        {
            actionDescription = "rotesc capul";
            startAction = () => avatarAnimator != null && avatarAnimator.StartHeadRotation(45f, 60f);
            return true;
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
            actionDescription = reps == 1 ? "fac o flotare" : $"fac {reps} flotări";
            startAction = () => avatarAnimator != null && avatarAnimator.StartPushUps(reps);
            return true;
        }

        // ── Mers ───────────────────────────────────────────────────────────
        Match mWalk = Regex.Match(norm, @"\b(pasi?|mers|mergi|mergeti|walk(?:ing)?|steps?)\b");
        if (!mWalk.Success) return false;

        string beforeWalk = norm.Substring(0, mWalk.Index);
        if (Regex.IsMatch(beforeWalk, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$")) return false;

        int steps = ParseActionCount(beforeWalk, norm.Substring(mWalk.Index + mWalk.Length), 5);
        actionDescription = steps == 1 ? "fac un pas" : $"fac {steps} pași";
        startAction = () => avatarAnimator != null && avatarAnimator.StartWalking(steps);
        return true;
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
