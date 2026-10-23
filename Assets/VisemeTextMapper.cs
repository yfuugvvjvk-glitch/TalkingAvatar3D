using System;
using System.Collections.Generic;

/// <summary>
/// Visemele posibile ale avatarului.
/// Prefixul Rhubarb* corespunde literelor A–H din protocolul Rhubarb Lip Sync.
/// </summary>
public enum AvatarViseme
{
    Rest,        // gură închisă / pauză
    OpenRound,   // A, O, Ă, Â, Î, UH  →  Jaw_Open + Mouth_Funnel
    MidSpread,   // E, I, AE            →  Mouth_Smile + Jaw_Open mic
    Closed,      // B, M, P             →  Mouth_Close
    TeethOnLip,  // F, V                →  Mouth_Shrug_Lower
    Pucker,      // W, OO               →  Mouth_Pucker
    Consonant,   // consoane generice   →  Jaw_Open minim + Mouth_Stretch
    RhubarbCh,   // CH / SH
    RhubarbDd,   // D, T, L
    RhubarbRr,   // R
    RhubarbSs,   // S, Z
    RhubarbTh,   // TH (eng)
    RhubarbKk,   // K, G, H
    RhubarbNn,   // N
    RhubarbA,    // Rhubarb A  = gură larg deschisă
    RhubarbB,    // Rhubarb B  = buze complet închise
    RhubarbC,    // Rhubarb C  = EE
    RhubarbD,    // Rhubarb D  = vocală deschisă
    RhubarbE,    // Rhubarb E  = vocală medie
    RhubarbF,    // Rhubarb F  = F / V
    RhubarbG,    // Rhubarb G  = velare kk
    RhubarbH     // Rhubarb H  = W / OO
}

[Serializable]
public class RhubarbTimeline { public RhubarbCue[] mouthCues; }

[Serializable]
public class RhubarbCue { public float start; public float end; public string value; }

public struct VisemeFrame
{
    public float time;
    public AvatarViseme viseme;
    public VisemeFrame(float t, AvatarViseme v) { time = t; viseme = v; }
}

/// <summary>
/// Construiește timeline-uri de viseme din:
///   1. Date Rhubarb Lip Sync (JSON cu cue-uri timed A–H) — cea mai precisă metodă
///   2. G2P pentru engleză (tabel de pronunție pentru cuvinte neregulate + reguli grafemice)
///   3. Reguli silabice pentru română (segmentare V/CV/CCV + ponderi nucleu silabic)
/// </summary>
public static class VisemeTextMapper
{
    // ════════════════════════════════════════════════════════════════════════
    // 1. RHUBARB TIMELINE
    // ════════════════════════════════════════════════════════════════════════

    public static List<VisemeFrame> CreateRhubarbTimeline(string json, float duration)
    {
        var frames = new List<VisemeFrame>();
        if (string.IsNullOrWhiteSpace(json) || duration <= 0f) return frames;

        RhubarbTimeline result;
        try { result = UnityEngine.JsonUtility.FromJson<RhubarbTimeline>(json); }
        catch { return frames; }

        if (result?.mouthCues == null || result.mouthCues.Length == 0) return frames;

        float prev = -1f;
        foreach (var cue in result.mouthCues)
        {
            if (cue == null || cue.start < 0f || cue.start > duration ||
                cue.end < cue.start || cue.start < prev)
                return new List<VisemeFrame>();
            if (!TryMapRhubarbShape(cue.value, out AvatarViseme v))
                return new List<VisemeFrame>();
            frames.Add(new VisemeFrame(cue.start, v));
            prev = cue.start;
        }

        if (frames.Count == 0) return frames;
        if (frames[0].time > 0f) frames.Insert(0, new VisemeFrame(0f, AvatarViseme.Rest));
        if (frames[frames.Count - 1].time < duration)
            frames.Add(new VisemeFrame(duration, AvatarViseme.Rest));
        return frames;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. TEXT-BASED TIMELINE (G2P engleză + silabificare română)
    // ════════════════════════════════════════════════════════════════════════

    public static List<VisemeFrame> CreateTimeline(string text, float duration, string language)
    {
        var frames = new List<VisemeFrame>();
        if (string.IsNullOrWhiteSpace(text) || duration <= 0f) return frames;

        var units = GetUnits(text, language);
        if (units.Count == 0) return frames;

        float total = 0f;
        foreach (var u in units) total += GetWeight(u);
        if (total <= 0f) return frames;

        float elapsed = 0f;
        foreach (var u in units)
        {
            frames.Add(new VisemeFrame(duration * elapsed / total, MapUnit(u)));
            elapsed += GetWeight(u);
        }
        frames.Add(new VisemeFrame(duration, AvatarViseme.Rest));
        return frames;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. G2P ENGLEZĂ — tabel de pronunție pentru cuvinte neregulate
    //    (grapheme-to-phoneme pentru engleză fără librării externe)
    //    Fonemele sunt codificate ca secvențe de unități vizeme.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tabel G2P pentru cuvintele englezești cu pronunție neregulată.
    /// Valoarea este secvența de unități fonetice (mapate în MapUnit).
    /// Acoperă cele mai frecvente ~300 cuvinte cu litere mute sau pronunție surprinzătoare.
    /// </summary>
    private static readonly Dictionary<string, string[]> EnglishG2P =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        // Litere mute celebre
        { "the",      new[]{"th","e"}        },
        { "a",        new[]{"uh"}            },
        { "an",       new[]{"uh","n"}        },
        { "and",      new[]{"ae","n","d"}    },
        { "are",      new[]{"ah","r"}        },
        { "or",       new[]{"aw","r"}        },
        { "of",       new[]{"uh","v"}        },
        { "to",       new[]{"t","oo"}        },
        { "do",       new[]{"d","oo"}        },
        { "you",      new[]{"y","oo"}        },
        { "your",     new[]{"y","aw","r"}    },
        { "who",      new[]{"h","oo"}        },
        { "whose",    new[]{"h","oo","z"}    },
        { "what",     new[]{"w","ah","t"}    },
        { "where",    new[]{"w","eh","r"}    },
        { "when",     new[]{"w","eh","n"}    },
        { "why",      new[]{"w","ah","ee"}   },
        { "how",      new[]{"h","ah","w"}    },
        { "now",      new[]{"n","ah","w"}    },
        { "know",     new[]{"n","oh"}        },
        { "knew",     new[]{"n","y","oo"}    },
        { "knife",    new[]{"n","ah","ee","f"}  },
        { "knight",   new[]{"n","ah","ee","t"}  },
        { "knock",    new[]{"n","ah","k"}    },
        { "knee",     new[]{"n","ee"}        },
        { "knowledge",new[]{"n","ah","l","eh","j"}},
        { "write",    new[]{"r","ah","ee","t"}   },
        { "wrong",    new[]{"r","aw","ng"}   },
        { "wrist",    new[]{"r","ih","s","t"}},
        { "wrap",     new[]{"r","ae","p"}    },
        { "would",    new[]{"w","uh","d"}    },
        { "could",    new[]{"k","uh","d"}    },
        { "should",   new[]{"sh","uh","d"}   },
        { "was",      new[]{"w","ah","z"}    },
        { "has",      new[]{"h","ae","z"}    },
        { "have",     new[]{"h","ae","v"}    },
        { "give",     new[]{"g","ih","v"}    },
        { "live",     new[]{"l","ih","v"}    },
        { "love",     new[]{"l","ah","v"}    },
        { "come",     new[]{"k","ah","m"}    },
        { "some",     new[]{"s","ah","m"}    },
        { "home",     new[]{"h","oh","m"}    },
        { "gone",     new[]{"g","aw","n"}    },
        { "done",     new[]{"d","ah","n"}    },
        { "one",      new[]{"w","ah","n"}    },
        { "once",     new[]{"w","ah","n","s"}},
        { "none",     new[]{"n","ah","n"}    },
        { "phone",    new[]{"f","oh","n"}    },
        { "photo",    new[]{"f","oh","t","oh"}},
        { "enough",   new[]{"ih","n","ah","f"}  },
        { "rough",    new[]{"r","ah","f"}    },
        { "tough",    new[]{"t","ah","f"}    },
        { "through",  new[]{"th","r","oo"}   },
        { "though",   new[]{"th","oh"}       },
        { "thought",  new[]{"th","aw","t"}   },
        { "bought",   new[]{"b","aw","t"}    },
        { "brought",  new[]{"b","r","aw","t"}},
        { "caught",   new[]{"k","aw","t"}    },
        { "taught",   new[]{"t","aw","t"}    },
        { "daughter", new[]{"d","aw","t","er"}  },
        { "laugh",    new[]{"l","ae","f"}    },
        { "half",     new[]{"h","ae","f"}    },
        { "calf",     new[]{"k","ae","f"}    },
        { "calm",     new[]{"k","ah","m"}    },
        { "palm",     new[]{"p","ah","m"}    },
        { "walk",     new[]{"w","aw","k"}    },
        { "talk",     new[]{"t","aw","k"}    },
        { "chalk",    new[]{"ch","aw","k"}   },
        { "folk",     new[]{"f","oh","k"}    },
        { "yolk",     new[]{"y","oh","k"}    },
        { "could've", new[]{"k","uh","d","uh","v"}},
        { "said",     new[]{"s","eh","d"}    },
        { "says",     new[]{"s","eh","z"}    },
        { "again",    new[]{"uh","g","eh","n"}  },
        { "against",  new[]{"uh","g","eh","n","s","t"}},
        { "many",     new[]{"m","eh","n","ee"}  },
        { "any",      new[]{"eh","n","ee"}   },
        { "very",     new[]{"v","eh","r","ee"}  },
        { "every",    new[]{"eh","v","r","ee"}  },
        { "pretty",   new[]{"p","r","ih","t","ee"}},
        { "women",    new[]{"w","ih","m","ih","n"}},
        { "woman",    new[]{"w","uh","m","uh","n"}},
        { "people",   new[]{"p","ee","p","uh","l"}},
        { "their",    new[]{"th","eh","r"}   },
        { "there",    new[]{"th","eh","r"}   },
        { "they",     new[]{"th","ay"}       },
        { "them",     new[]{"th","eh","m"}   },
        { "then",     new[]{"th","eh","n"}   },
        { "this",     new[]{"th","ih","s"}   },
        { "that",     new[]{"th","ae","t"}   },
        { "with",     new[]{"w","ih","th"}   },
        { "which",    new[]{"w","ih","ch"}   },
        { "these",    new[]{"th","ee","z"}   },
        { "those",    new[]{"th","oh","z"}   },
        { "other",    new[]{"ah","th","er"}  },
        { "another",  new[]{"uh","n","ah","th","er"}},
        { "either",   new[]{"ee","th","er"}  },
        { "neither",  new[]{"n","ee","th","er"}},
        { "whether",  new[]{"w","eh","th","er"}},
        { "weather",  new[]{"w","eh","th","er"}},
        { "water",    new[]{"w","aw","t","er"}  },
        { "later",    new[]{"l","ay","t","er"}  },
        { "letter",   new[]{"l","eh","t","er"}  },
        { "better",   new[]{"b","eh","t","er"}  },
        { "butter",   new[]{"b","ah","t","er"}  },
        { "before",   new[]{"b","ih","f","aw","r"}},
        { "because",  new[]{"b","ih","k","ah","z"}},
        { "between",  new[]{"b","ih","t","w","ee","n"}},
        { "right",    new[]{"r","ah","ee","t"}  },
        { "night",    new[]{"n","ah","ee","t"}  },
        { "light",    new[]{"l","ah","ee","t"}  },
        { "fight",    new[]{"f","ah","ee","t"}  },
        { "might",    new[]{"m","ah","ee","t"}  },
        { "sight",    new[]{"s","ah","ee","t"}  },
        { "tight",    new[]{"t","ah","ee","t"}  },
        { "high",     new[]{"h","ah","ee"}   },
        { "sign",     new[]{"s","ah","ee","n"}  },
        { "assign",   new[]{"uh","s","ah","ee","n"}},
        { "design",   new[]{"d","ih","z","ah","ee","n"}},
        { "science",  new[]{"s","ah","ee","uh","n","s"}},
        { "island",   new[]{"ah","ee","l","uh","n","d"}},
        { "listen",   new[]{"l","ih","s","uh","n"}},
        { "often",    new[]{"aw","f","uh","n"}  },
        { "soften",   new[]{"s","aw","f","uh","n"}},
        { "fasten",   new[]{"f","ae","s","uh","n"}},
        { "castle",   new[]{"k","ae","s","uh","l"}},
        { "whistle",  new[]{"w","ih","s","uh","l"}},
        { "muscle",   new[]{"m","ah","s","uh","l"}},
        { "colonel",  new[]{"k","er","n","uh","l"}},
        { "iron",     new[]{"ah","ee","er","n"} },
        { "hour",     new[]{"ah","w","er"}    },
        { "honest",   new[]{"ah","n","ih","s","t"}},
        { "honor",    new[]{"ah","n","er"}    },
        { "herb",     new[]{"er","b"}         },
        { "heir",     new[]{"eh","r"}         },
        { "here",     new[]{"h","ih","r"}     },
        { "hear",     new[]{"h","ih","r"}     },
        { "heart",    new[]{"h","ah","r","t"} },
        { "earth",    new[]{"er","th"}        },
        { "early",    new[]{"er","l","ee"}    },
        { "work",     new[]{"w","er","k"}     },
        { "word",     new[]{"w","er","d"}     },
        { "world",    new[]{"w","er","l","d"} },
        { "worry",    new[]{"w","er","ee"}    },
        { "worse",    new[]{"w","er","s"}     },
        { "worth",    new[]{"w","er","th"}    },
        { "call",     new[]{"k","aw","l"}     },
        { "fall",     new[]{"f","aw","l"}     },
        { "ball",     new[]{"b","aw","l"}     },
        { "wall",     new[]{"w","aw","l"}     },
        { "hall",     new[]{"h","aw","l"}     },
        { "all",      new[]{"aw","l"}         },
        { "always",   new[]{"aw","l","w","ay","z"}},
        { "already",  new[]{"aw","l","r","eh","d","ee"}},
        { "also",     new[]{"aw","l","s","oh"}},
        { "almost",   new[]{"aw","l","m","oh","s","t"}},
        { "although", new[]{"aw","l","th","oh"}},
        { "altogether",new[]{"aw","l","t","uh","g","eh","th","er"}},
        { "picture",  new[]{"p","ih","k","ch","er"}},
        { "future",   new[]{"f","y","oo","ch","er"}},
        { "nature",   new[]{"n","ay","ch","er"}},
        { "feature",  new[]{"f","ee","ch","er"}},
        { "measure",  new[]{"m","eh","zh","er"}},
        { "treasure", new[]{"t","r","eh","zh","er"}},
        { "pleasure", new[]{"p","l","eh","zh","er"}},
        { "usual",    new[]{"y","oo","zh","oo","uh","l"}},
        { "vision",   new[]{"v","ih","zh","uh","n"}},
        { "decision", new[]{"d","ih","s","ih","zh","uh","n"}},
        { "question", new[]{"k","w","eh","s","ch","uh","n"}},
        { "action",   new[]{"ae","k","sh","uh","n"}},
        { "nation",   new[]{"n","ay","sh","uh","n"}},
        { "station",  new[]{"s","t","ay","sh","uh","n"}},
        { "situation",new[]{"s","ih","ch","oo","ay","sh","uh","n"}},
        { "education",new[]{"eh","j","oo","k","ay","sh","uh","n"}},
        { "information",new[]{"ih","n","f","er","m","ay","sh","uh","n"}},
        { "important", new[]{"ih","m","p","aw","r","t","uh","n","t"}},
        { "different", new[]{"d","ih","f","r","uh","n","t"}},
        { "everything",new[]{"eh","v","r","ee","th","ih","ng"}},
        { "something", new[]{"s","ah","m","th","ih","ng"}},
        { "nothing",  new[]{"n","ah","th","ih","ng"}},
        { "anything", new[]{"eh","n","ee","th","ih","ng"}},
        { "everything",new[]{"eh","v","r","ee","th","ih","ng"}},
        { "language", new[]{"l","ae","ng","g","w","ih","j"}},
        { "avatar",   new[]{"ae","v","uh","t","ah","r"}},
        { "computer", new[]{"k","uh","m","p","y","oo","t","er"}},
        { "program",  new[]{"p","r","oh","g","r","ae","m"}},
        { "algorithm",new[]{"ae","l","g","uh","r","ih","th","uh","m"}},
        { "beautiful",new[]{"b","y","oo","t","ih","f","uh","l"}},
        { "people",   new[]{"p","ee","p","uh","l"}},
        { "really",   new[]{"r","ee","uh","l","ee"}},
        { "actually", new[]{"ae","k","ch","oo","uh","l","ee"}},
        { "probably", new[]{"p","r","ah","b","uh","b","l","ee"}},
        { "obviously",new[]{"ah","b","v","ee","uh","s","l","ee"}},
        { "especially",new[]{"ih","s","p","eh","sh","uh","l","ee"}},
        { "generally",new[]{"j","eh","n","r","uh","l","ee"}},
        { "usually",  new[]{"y","oo","zh","oo","uh","l","ee"}},
        { "naturally",new[]{"n","ae","ch","r","uh","l","ee"}},
        { "literally",new[]{"l","ih","t","r","uh","l","ee"}},
        { "basically",new[]{"b","ay","s","ih","k","l","ee"}},
        { "certainly",new[]{"s","er","t","uh","n","l","ee"}},
    };

    // ════════════════════════════════════════════════════════════════════════
    // 4. SEGMENTARE TEXT → UNITĂȚI FONETICE
    // ════════════════════════════════════════════════════════════════════════

    // Vocale românești (cu diacritice)
    private static readonly HashSet<char> RomanianVowels = new HashSet<char>
    {
        'a','e','i','o','u',
        '\u0103', // ă
        '\u00e2', // â
        '\u00ee'  // î
    };

    private static List<string> GetUnits(string text, string language)
    {
        bool isRomanian = IsRomanian(language);
        string normalized = text.ToLowerInvariant();
        var units = new List<string>();

        if (isRomanian)
        {
            // ── Română: segmentare silabică V/CV/CCV ──────────────────────
            // Buffer consoane (onset) → emis înainte de vocala nucleu.
            // Coda (consoanele finale de silabă/cuvânt) e emisă imediat.
            var consonantBuf = new List<string>();

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];

                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    foreach (var con in consonantBuf) units.Add(con);
                    consonantBuf.Clear();
                    units.Add(c.ToString());
                    continue;
                }
                if (!char.IsLetter(c)) continue;

                // Digrame ch, gh
                if (i + 1 < normalized.Length)
                {
                    string p2 = normalized.Substring(i, 2);
                    if (p2 == "ch") { consonantBuf.Add("ch"); i++; continue; }
                    if (p2 == "gh") { consonantBuf.Add("g");  i++; continue; }
                }

                if (RomanianVowels.Contains(c))
                {
                    // Vocală → flush onset + vocală
                    foreach (var con in consonantBuf) units.Add(con);
                    consonantBuf.Clear();
                    units.Add(c.ToString());
                }
                else
                {
                    consonantBuf.Add(c.ToString());
                }
            }
            // Consoane finale
            foreach (var con in consonantBuf) units.Add(con);
        }
        else
        {
            // ── Engleză: G2P pe cuvinte + reguli grafemice fallback ───────
            // Tokenizăm în cuvinte și procesăm fiecare:
            // - dacă e în tabelul G2P → fonemele exacte
            // - altfel → reguli grafemice (digrame + corespondenţe)

            int pos = 0;
            while (pos < normalized.Length)
            {
                char c = normalized[pos];

                // Spații și punctuație
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    units.Add(c.ToString());
                    pos++;
                    continue;
                }

                if (!char.IsLetter(c)) { pos++; continue; }

                // Extragem cuvântul curent (litere + apostrofe pentru contractions)
                int wordStart = pos;
                while (pos < normalized.Length &&
                       (char.IsLetter(normalized[pos]) || normalized[pos] == '\''))
                    pos++;
                string word = normalized.Substring(wordStart, pos - wordStart).Trim('\'');

                if (EnglishG2P.TryGetValue(word, out string[] phonemes))
                {
                    // Cuvânt cu pronunție G2P cunoscută
                    foreach (var ph in phonemes)
                        units.Add(ph);
                }
                else
                {
                    // Fallback: reguli grafemice pe cuvânt
                    ApplyEnglishGraphemeRules(word, units);
                }
            }
        }

        return units;
    }

    /// <summary>
    /// Aplică reguli grafemice pentru engleză pe un singur cuvânt.
    /// Acoperă: digrame fonetice, vocale silențioase finale, pattern-uri comune.
    /// </summary>
    private static void ApplyEnglishGraphemeRules(string word, List<string> units)
    {
        int i = 0;
        while (i < word.Length)
        {
            // Perechi de 2 caractere
            if (i + 1 < word.Length)
            {
                string p2 = word.Substring(i, 2);
                switch (p2)
                {
                    case "th": units.Add("th"); i += 2; continue;
                    case "sh": units.Add("ch"); i += 2; continue;
                    case "ch": units.Add("ch"); i += 2; continue;
                    case "wh": units.Add("w");  i += 2; continue;
                    case "ph": units.Add("f");  i += 2; continue;  // phone → f
                    case "gh":
                        // gh mut la finalul cuvintelor (night, fight) sau gh=g
                        if (i + 2 >= word.Length || word[i + 2] == 't')
                        { i += 2; continue; } // silențios
                        units.Add("g"); i += 2; continue;
                    case "ck": units.Add("k");  i += 2; continue;
                    case "ng": units.Add("n");  i += 2; continue;  // ring → n final
                    case "mb":
                        // b mut la final (lamb, bomb)
                        if (i + 2 >= word.Length) { units.Add("m"); i += 2; continue; }
                        break; // altfel tratam literele separat
                    case "kn": units.Add("n");  i += 2; continue;  // knife → n
                    case "wr": units.Add("r");  i += 2; continue;  // write → r
                    case "gn":
                        if (i == 0) { units.Add("n"); i += 2; continue; } // gnat
                        break; // altfel tratam literele separat
                    case "qu": units.Add("k"); units.Add("w"); i += 2; continue; // queen
                    case "oo": units.Add("oo"); i += 2; continue;
                    case "ee": units.Add("ee"); i += 2; continue;
                    case "ea":
                        // ea = "ee" în majoritatea cazurilor (eat, read)
                        units.Add("ee"); i += 2; continue;
                    case "ou":
                        // ou = "ow" (out) sau "oo" (you) — default "ow"
                        units.Add("ah"); i += 2; continue;
                    case "ow":
                        units.Add("oh"); i += 2; continue;  // snow, show
                    case "oi": units.Add("oy"); i += 2; continue;
                    case "oy": units.Add("oy"); i += 2; continue;
                    case "ai":
                        units.Add("ay"); i += 2; continue;
                    case "ay": units.Add("ay"); i += 2; continue;
                    case "ey": units.Add("ay"); i += 2; continue;  // they, hey
                    case "ie":
                        units.Add("ee"); i += 2; continue;         // piece, field
                    case "ei":
                        units.Add("ay"); i += 2; continue;         // eight, vein
                    case "ue":
                        units.Add("oo"); i += 2; continue;         // blue, true
                    case "ui":
                        units.Add("oo"); i += 2; continue;         // fruit, juice
                    case "ew":
                        units.Add("y"); units.Add("oo"); i += 2; continue; // new, few
                    case "aw":
                        units.Add("aw"); i += 2; continue;
                    case "au":
                        units.Add("aw"); i += 2; continue;
                    case "ar":
                        units.Add("ah"); units.Add("r"); i += 2; continue;
                    case "er":
                    case "ir":
                    case "ur":
                        units.Add("er"); i += 2; continue;
                    case "or":
                        units.Add("aw"); units.Add("r"); i += 2; continue;
                }
            }

            char ch = word[i];

            // Vocala finală silențioasă: e la finalul cuvântului după consoană
            if (ch == 'e' && i == word.Length - 1 && i > 0 && !IsVowel(word[i - 1]))
            {
                i++; continue; // "e" mut (make, like, home)
            }

            units.Add(ch.ToString());
            i++;
        }
    }

    private static bool IsVowel(char c) =>
        c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u';

    // ════════════════════════════════════════════════════════════════════════
    // 5. MAPARE UNITATE FONETICĂ → VISEMĂ
    // ════════════════════════════════════════════════════════════════════════

    private static AvatarViseme MapUnit(string unit)
    {
        if (string.IsNullOrEmpty(unit)) return AvatarViseme.Rest;

        switch (unit)
        {
            // ── Vocale deschise/rotunde ──────────────────────────────────
            case "a": case "\u0103": case "\u00e2": case "\u00ee":
            case "o": case "u": case "ah": case "uh": case "aw": case "oh":
                return AvatarViseme.OpenRound;

            // ── Vocale medii/laterale ─────────────────────────────────────
            case "e": case "i": case "ae": case "ee": case "eh":
            case "ih": case "ay": case "oy":
                return AvatarViseme.MidSpread;

            // ── Ocluzive bilabiale ────────────────────────────────────────
            case "b": case "m": case "p":
                return AvatarViseme.Closed;

            // ── Labiodentale ──────────────────────────────────────────────
            case "f": case "v":
                return AvatarViseme.TeethOnLip;

            // ── Labiale rotunjite ─────────────────────────────────────────
            case "w": case "oo": case "y":
                return AvatarViseme.Pucker;

            // ── CH, SH (affricate / siflantă palatală) ────────────────────
            case "ch": case "sh": case "zh":
            case "\u0219": case "\u015f": // ș român
                return AvatarViseme.RhubarbCh;

            // ── D, T, L (alveolare) ───────────────────────────────────────
            case "d": case "t": case "l":
            case "\u0163": case "\u021b": // ț român
                return AvatarViseme.RhubarbDd;

            // ── R (vibrantă) ──────────────────────────────────────────────
            case "r": case "er":
                return AvatarViseme.RhubarbRr;

            // ── S, Z (siflante) ───────────────────────────────────────────
            case "s": case "z":
                return AvatarViseme.RhubarbSs;

            // ── TH (engleză) ──────────────────────────────────────────────
            case "th":
                return AvatarViseme.RhubarbTh;

            // ── K, G, H (velare) ──────────────────────────────────────────
            case "k": case "g": case "h": case "c": case "ng":
                return AvatarViseme.RhubarbKk;

            // ── N (nazală) ────────────────────────────────────────────────
            case "n":
                return AvatarViseme.RhubarbNn;

            // ── Pauze ─────────────────────────────────────────────────────
            case " ": case "\t": case "\n": case "\r":
            case ".": case "!": case "?": case ",": case ";": case ":":
            case "-": case "\u2014": case "\"": case "'":
                return AvatarViseme.Rest;

            default:
                return AvatarViseme.Consonant;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. RHUBARB A–H MAP
    // ════════════════════════════════════════════════════════════════════════

    private static bool TryMapRhubarbShape(string v, out AvatarViseme viseme)
    {
        switch (v)
        {
            case "A": viseme = AvatarViseme.RhubarbA; return true;
            case "B": viseme = AvatarViseme.RhubarbB; return true;
            case "C": viseme = AvatarViseme.RhubarbC; return true;
            case "D": viseme = AvatarViseme.RhubarbD; return true;
            case "E": viseme = AvatarViseme.RhubarbE; return true;
            case "F": viseme = AvatarViseme.RhubarbF; return true;
            case "G": viseme = AvatarViseme.RhubarbG; return true;
            case "H": viseme = AvatarViseme.RhubarbH; return true;
            case "X": viseme = AvatarViseme.Rest;      return true;
            default:  viseme = AvatarViseme.Rest;      return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. PONDERI TEMPORALE
    // ════════════════════════════════════════════════════════════════════════

    private static float GetWeight(string unit)
    {
        if (unit.Length == 1 && char.IsWhiteSpace(unit[0])) return 2.0f;
        if (unit.Length == 1 && char.IsPunctuation(unit[0])) return 1.2f;

        char ch = unit[0];
        // Vocale deschise (nucleu silabic) — timp mai lung
        if (ch == 'a' || ch == 'o' || ch == 'u' ||
            ch == '\u0103' || ch == '\u00e2' || ch == '\u00ee') return 2.2f;
        if (ch == 'e' || ch == 'i') return 1.8f;
        // Diftonguri și vocale englezești lungi
        if (unit == "ah" || unit == "uh" || unit == "oo" || unit == "ee" ||
            unit == "aw" || unit == "oh" || unit == "ay" || unit == "er") return 2.2f;
        if (unit == "ae" || unit == "eh" || unit == "ih" || unit == "oy") return 1.8f;

        return unit.Length; // consoane și digrame
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. UTILITARE
    // ════════════════════════════════════════════════════════════════════════

    private static bool IsRomanian(string language)
    {
        if (string.IsNullOrEmpty(language)) return true;
        return language.StartsWith("ro", StringComparison.OrdinalIgnoreCase) ||
               language.StartsWith("roman", StringComparison.OrdinalIgnoreCase);
    }
}
