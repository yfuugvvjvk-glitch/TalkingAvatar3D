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

    // ─── Stare internă ───────────────────────────────────────────────────────
    private readonly object        streamLock   = new object();
    private readonly Queue<string> speechQueue  = new Queue<string>();
    private readonly StringBuilder speechBuffer = new StringBuilder();
    private string currentStreamedText = "";
    private bool   isStreaming;
    private bool   chatInitialized;

    private Coroutine ttsCoroutine;

    public string SpeechStatus     { get; private set; } = "TTS: în așteptare";
    public string AvatarActionStatus => avatarAnimator != null ? avatarAnimator.ActionStatus : string.Empty;
    public bool   IsStreaming        => isStreaming;
    public string CurrentChatText
    {
        get { lock (streamLock) return currentStreamedText; }
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
        if (isStreaming || string.IsNullOrWhiteSpace(prompt)) return;

        TryStartAvatarAction(prompt);

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

    private void TryStartAvatarAction(string prompt)
    {
        string norm = prompt.ToLowerInvariant()
            .Replace("ă","a").Replace("â","a").Replace("î","i")
            .Replace("ș","s").Replace("ş","s").Replace("ț","t").Replace("ţ","t");

        // ── Ridică brațele / mâinile sus (prioritate față de wave simplu) ──
        if (Regex.IsMatch(norm, @"\b(ridica\s+mainile|ridica\s+mainile\s+sus|ridica\s+bratele|raise\s+(arms?|hands?))\b"))
        {
            avatarAnimator?.StartRaisingArms();
            return;
        }

        // ── Wave / salut ───────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(salut[a]?|wave|misca\s+brat|ridica\s+brat|agit[a]?\s+brat)\b"))
        {
            avatarAnimator?.StartWaving();
            return;
        }

        // ── Înot ───────────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(inoata|inot|swimming|swim)\b"))
        {
            int sec = ParseActionCount(norm, norm, 5);
            avatarAnimator?.StartSwimming(sec);
            return;
        }

        // ── Săritură ───────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(sari|saritura|jump|sara)\b"))
        {
            if (Regex.IsMatch(norm, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$"))
                return;
            int reps = ParseActionCount(norm, norm, 3);
            avatarAnimator?.StartJumping(reps);
            return;
        }

        // ── Dans ───────────────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(danseaza|dans|dance)\b"))
        {
            avatarAnimator?.StartDancing();
            return;
        }

        // ── Rotește capul ──────────────────────────────────────────────────
        if (Regex.IsMatch(norm, @"\b(roteste\s+cap(ul)?|turn\s+head)\b"))
        {
            avatarAnimator?.StartHeadRotation(45f, 60f);
            return;
        }

        // ── Flotări ────────────────────────────────────────────────────────
        Match mPush = Regex.Match(norm, @"\b(flotari|flotare|push[\s-]?ups?)\b");
        if (mPush.Success)
        {
            if (Regex.IsMatch(norm.Substring(0, mPush.Index), @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$"))
                return;
            string before = norm.Substring(0, mPush.Index);
            string after  = norm.Substring(mPush.Index + mPush.Length);
            int reps = ParseActionCount(before, after, 5);
            avatarAnimator?.StartPushUps(reps);
            return;
        }

        // ── Mers ───────────────────────────────────────────────────────────
        Match mWalk = Regex.Match(norm, @"\b(pasi?|mers|mergi|mergeti|walk(?:ing)?|steps?)\b");
        if (!mWalk.Success) return;

        string beforeWalk = norm.Substring(0, mWalk.Index);
        if (Regex.IsMatch(beforeWalk, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$")) return;

        int steps = ParseActionCount(beforeWalk, norm.Substring(mWalk.Index + mWalk.Length), 5);
        avatarAnimator?.StartWalking(steps);
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
                  speechLanguage.StartsWith("román", StringComparison.OrdinalIgnoreCase);
        if (ro)
            return "Ești un asistent vocal pentru un avatar 3D. Răspunde scurt, 1-2 propoziții, " +
                   "în română cu diacritice. Nu refuza comenzi de animație — ele sunt gestionate de cod, nu de tine. " +
                   "Dacă utilizatorul cere o mișcare, confirmă scurt că avatarul o face.";
        return "You are a voice assistant for a 3D avatar. Reply briefly, 1-2 sentences. " +
               "Never refuse animation commands — they are handled by code, not you. " +
               "If the user requests a movement, briefly confirm the avatar is doing it.";
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
