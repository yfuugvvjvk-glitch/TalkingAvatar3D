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

    [Header("Setări TTS (API compatibil OpenAI, audio WAV)")]
    public bool speakResponses = true;
    public string ttsEndpoint = "http://127.0.0.1:5000/v1/audio/speech";
    public string ttsModel = "tts-1";
    public string ttsVoice = "alloy";
    public string speechLanguage = "English";

    [Header("UI References")]
    public TMP_InputField inputField;
    public TMP_Text chatDisplay;
    public AudioSource audioSource;
    public AvatarVisemeAnimator avatarAnimator;

    private readonly object streamLock = new object();
    private readonly Queue<string> speechQueue = new Queue<string>();
    private readonly StringBuilder speechBuffer = new StringBuilder();
    private string currentStreamedText = "";
    private bool isStreaming;
    private bool chatInitialized;

    public string SpeechStatus { get; private set; } = "TTS: în așteptare";
    public string AvatarActionStatus => avatarAnimator != null ? avatarAnimator.ActionStatus : string.Empty;

    public string CurrentChatText
    {
        get
        {
            lock (streamLock)
                return currentStreamedText;
        }
    }

    public bool IsStreaming => isStreaming;

    public void SendPrompt()
    {
        if (inputField == null)
        {
            Debug.LogError("Asignează Input Field sau apelează SendPrompt(string).");
            return;
        }

        string prompt = inputField.text;
        inputField.text = "";
        SendPrompt(prompt);
    }

    public async void SendPrompt(string prompt)
    {
        if (isStreaming)
            return;

        if (string.IsNullOrWhiteSpace(prompt))
            return;

        TryStartAvatarAction(prompt);

        lock (streamLock)
        {
            string existingText = chatDisplay != null ? chatDisplay.text : currentStreamedText;
            currentStreamedText = existingText + $"\n<b>Tu:</b> {prompt}\n<b>AI:</b> ";
        }

        if (chatDisplay != null)
            chatDisplay.text = currentStreamedText;

        if (inputField != null)
            inputField.text = "";

        lock (streamLock)
        {
            speechBuffer.Length = 0;
            speechQueue.Clear();
        }
        isStreaming = true;

        if (speakResponses)
        {
            if (audioSource == null)
            {
                Debug.LogError("Asignează un AudioSource pentru redarea răspunsurilor TTS.");
                speakResponses = false;
            }
            else if (string.IsNullOrWhiteSpace(ttsEndpoint))
            {
                Debug.LogError("Configurează endpoint-ul serviciului TTS în Inspector.");
                speakResponses = false;
            }
            else
            {
                if (avatarAnimator == null)
                    Debug.LogError("Asignează AvatarVisemeAnimator pentru animația visemelor.");
                StartCoroutine(ProcessSpeechQueue());
            }
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
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            lock (streamLock)
            {
                QueueCompletedSpeech();
                if (chatDisplay != null)
                    chatDisplay.text = currentStreamedText;
            }
            isStreaming = false;
        }
    }

    private string BuildSystemPrompt()
    {
        bool romanian = string.Equals(speechLanguage, "Romanian", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(speechLanguage, "Română", StringComparison.OrdinalIgnoreCase);
        if (romanian)
        {
            return "Ești un asistent conversațional prietenos, care vorbește natural. Răspunde numai în limba română, cu diacritice corecte și propoziții clare. Răspunde direct la întrebarea utilizatorului, de regulă în 1-3 propoziții. Nu repeta întrebarea, nu inventa cuvinte, nu produce silabe fără sens și nu continua răspunsul cu text redundant. Dacă nu știi un răspuns, spune sincer acest lucru. Aplicația poate anima mersul pe loc și flotările numai când utilizatorul cere explicit aceste acțiuni; confirmă-le pe scurt și nu pretinde că avatarul execută alte mișcări sau se deplasează prin scenă.";
        }

        return "You are a friendly conversational assistant. Reply only in fluent, natural English using clear, grammatical sentences. Answer the user's question directly, usually in 1-3 sentences. Do not repeat the question, invent words, produce meaningless syllables, or continue with redundant text. If you do not know an answer, say so honestly. The application can animate walking in place and push-ups only when explicitly requested; briefly confirm these actions and do not claim the avatar performs other movements or travels through the scene.";
    }

    private void TryStartAvatarAction(string prompt)
    {
        string normalized = prompt.ToLowerInvariant()
            .Replace("ă", "a")
            .Replace("â", "a")
            .Replace("î", "i")
            .Replace("ș", "s")
            .Replace("ş", "s")
            .Replace("ț", "t")
            .Replace("ţ", "t");
        Match action = Regex.Match(normalized, @"\b(flotari|flotare|push[\s-]?ups?|pasi?|mers|mergi|mergeti|walk(?:ing)?|steps?)\b");
        if (!action.Success)
            return;

        string beforeAction = normalized.Substring(0, action.Index);
        if (Regex.IsMatch(beforeAction, @"\b(nu|don't|dont|stop|fara|without)\b[^.!?]{0,24}$"))
            return;

        int repetitions = ParseActionCount(beforeAction, normalized.Substring(action.Index + action.Length));
        bool isPushUp = action.Value.StartsWith("flotar", StringComparison.Ordinal) ||
                        action.Value.StartsWith("push", StringComparison.Ordinal);
        bool started = avatarAnimator != null &&
                       (isPushUp
                           ? avatarAnimator.StartPushUps(repetitions)
                           : avatarAnimator.StartWalking(repetitions));
        if (!started)
            Debug.LogError($"Comanda de {(isPushUp ? "flotări" : "mers")} a fost detectată, dar animația nu a putut porni.");
    }

    private static int ParseActionCount(string beforeAction, string afterAction)
    {
        Match numericCount = Regex.Match(beforeAction, @"\b(\d{1,2})\b");
        if (!numericCount.Success)
            numericCount = Regex.Match(afterAction, @"\b(\d{1,2})\b");
        if (numericCount.Success && int.TryParse(numericCount.Groups[1].Value, out int count))
            return Mathf.Clamp(count, 1, 50);

        string[] numberWords =
        {
            "unu|una|one", "doi|doua|two", "trei|three", "patru|four", "cinci|five",
            "sase|six", "sapte|seven", "opt|eight", "noua|nine", "zece|ten"
        };
        for (int i = 0; i < numberWords.Length; i++)
        {
            if (Regex.IsMatch(beforeAction, $@"\b({numberWords[i]})\b") ||
                Regex.IsMatch(afterAction, $@"\b({numberWords[i]})\b"))
                return i + 1;
        }

        return 5;
    }

    private void OnChunkReceived(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        lock (streamLock)
        {
            currentStreamedText += chunk;
            if (speakResponses)
            {
                speechBuffer.Append(chunk);
                QueueCompletedSentences();
            }
        }
    }

    private void QueueCompletedSentences()
    {
        int sentenceEnd;
        while ((sentenceEnd = FindSentenceEnd(speechBuffer)) >= 0)
        {
            string sentence = speechBuffer.ToString(0, sentenceEnd + 1).Trim();
            speechBuffer.Remove(0, sentenceEnd + 1);
            if (!string.IsNullOrWhiteSpace(sentence))
                speechQueue.Enqueue(sentence);
        }

        if (speechBuffer.Length >= 120)
        {
            int breakAt = speechBuffer.ToString().LastIndexOf(' ');
            if (breakAt > 0)
            {
                string phrase = speechBuffer.ToString(0, breakAt).Trim();
                speechBuffer.Remove(0, breakAt + 1);
                if (!string.IsNullOrWhiteSpace(phrase))
                    speechQueue.Enqueue(phrase);
            }
        }
    }

    private static int FindSentenceEnd(StringBuilder text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '.' || text[i] == '!' || text[i] == '?' || text[i] == '\n')
                return i;
        }

        return -1;
    }

    private void QueueCompletedSpeech()
    {
        string finalPhrase = speechBuffer.ToString().Trim();
        speechBuffer.Length = 0;
        if (speakResponses && !string.IsNullOrWhiteSpace(finalPhrase))
            speechQueue.Enqueue(finalPhrase);
    }

    private IEnumerator ProcessSpeechQueue()
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
                    yield break;

                yield return null;
                continue;
            }

            SpeechRequest request = new SpeechRequest
            {
                model = ttsModel,
                input = phrase,
                voice = ttsVoice,
                language = speechLanguage,
                response_format = "wav"
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            using (UnityWebRequest webRequest = new UnityWebRequest(ttsEndpoint, UnityWebRequest.kHttpVerbPOST))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(body);
                webRequest.downloadHandler = new DownloadHandlerAudioClip(ttsEndpoint, AudioType.WAV);
                webRequest.SetRequestHeader("Content-Type", "application/json");
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    string responseBody = webRequest.downloadHandler != null
                        ? Encoding.UTF8.GetString(webRequest.downloadHandler.data ?? Array.Empty<byte>())
                        : string.Empty;
                    SpeechError speechError = string.IsNullOrEmpty(responseBody)
                        ? null
                        : JsonUtility.FromJson<SpeechError>(responseBody);
                    string errorMessage = speechError != null && !string.IsNullOrEmpty(speechError.error)
                        ? speechError.error
                        : webRequest.error;
                    SpeechStatus = $"TTS indisponibil ({webRequest.responseCode}): {errorMessage}";
                    Debug.LogError(SpeechStatus);
                    continue;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(webRequest);
                if (clip == null)
                {
                    SpeechStatus = "TTS nu a returnat un WAV valid.";
                    Debug.LogError(SpeechStatus);
                    continue;
                }

                string timelineJson = null;
                string encodedTimeline = webRequest.GetResponseHeader("X-Viseme-Timeline");
                if (!string.IsNullOrEmpty(encodedTimeline))
                {
                    try
                    {
                        timelineJson = Encoding.UTF8.GetString(Convert.FromBase64String(encodedTimeline));
                    }
                    catch (FormatException exception)
                    {
                        Debug.LogWarning($"Cronologia visemelor Rhubarb nu este validă: {exception.Message}");
                    }
                }

                string visemeMode = webRequest.GetResponseHeader("X-Viseme-Mode");
                string ttsEngine = webRequest.GetResponseHeader("X-TTS-Engine");
                if (string.Equals(visemeMode, "rhubarb", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(timelineJson))
                {
                    SpeechStatus = $"TTS: {ttsEngine ?? "audio"} + sincronizare Rhubarb";
                }
                else
                {
                    SpeechStatus = $"TTS: {ttsEngine ?? "audio"} + estimare viseme din text (Rhubarb lipsește sau a eșuat)";
                    Debug.LogWarning(SpeechStatus);
                }

                audioSource.clip = clip;
                // PlaySpeech exact înainte de Play() pentru a alinia timeline-ul cu audio
                if (avatarAnimator != null)
                    avatarAnimator.PlaySpeech(phrase, clip.length, speechLanguage, timelineJson);
                audioSource.Play(); // speechStartedAt și Play() sunt acum consecutive pe același frame
                while (audioSource.isPlaying)
                    yield return null;
            }
        }
    }

    [Serializable]
    private class SpeechRequest
    {
        public string model;
        public string input;
        public string voice;
        public string language;
        public string response_format;
    }

    [Serializable]
    private class SpeechError
    {
        public string error = string.Empty;
    }

    private void Update()
    {
        if (!isStreaming || chatDisplay == null)
            return;

        string streamedText;
        lock (streamLock)
            streamedText = currentStreamedText;

        if (chatDisplay.text != streamedText)
            chatDisplay.text = streamedText;
    }
}