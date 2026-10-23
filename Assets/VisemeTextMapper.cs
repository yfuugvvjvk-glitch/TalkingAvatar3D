using System;
using System.Collections.Generic;

/// <summary>
/// Enumerează visemele posibile ale avatarului.
/// Prefixul Rhubarb* corespunde literelor A-H din protocolul Rhubarb Lip Sync.
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
    RhubarbC,    // Rhubarb C  = EE (sunet „i" strâns)
    RhubarbD,    // Rhubarb D  = vocală deschisă
    RhubarbE,    // Rhubarb E  = vocală medie
    RhubarbF,    // Rhubarb F  = F / V
    RhubarbG,    // Rhubarb G  = kk (velare)
    RhubarbH     // Rhubarb H  = W / OO
}

[Serializable]
public class RhubarbTimeline
{
    public RhubarbCue[] mouthCues;
}

[Serializable]
public class RhubarbCue
{
    public float start;
    public float end;
    public string value;
}

/// <summary>O intrare în timeline-ul visemelor: moment de timp + visemă activă de la acel moment.</summary>
public struct VisemeFrame
{
    public float time;
    public AvatarViseme viseme;

    public VisemeFrame(float time, AvatarViseme viseme)
    {
        this.time = time;
        this.viseme = viseme;
    }
}

/// <summary>
/// Construiește timeline-uri de viseme fie din date Rhubarb (JSON),
/// fie direct din text prin analiză fonematică (română/engleză).
/// </summary>
public static class VisemeTextMapper
{
    // ────────────────────────────────────────────────────────────────────────
    // Timeline din date Rhubarb Lip Sync (JSON)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsează JSON-ul returnat de Rhubarb și construiește un timeline precis.
    /// Returnează o listă goală dacă datele sunt invalide (caz în care se va folosi fallback-ul text).
    /// </summary>
    public static List<VisemeFrame> CreateRhubarbTimeline(string json, float duration)
    {
        List<VisemeFrame> frames = new List<VisemeFrame>();
        if (string.IsNullOrWhiteSpace(json) || duration <= 0f)
            return frames;

        RhubarbTimeline result;
        try
        {
            result = UnityEngine.JsonUtility.FromJson<RhubarbTimeline>(json);
        }
        catch
        {
            return frames;
        }

        if (result == null || result.mouthCues == null || result.mouthCues.Length == 0)
            return frames;

        float previousStart = -1f;
        for (int i = 0; i < result.mouthCues.Length; i++)
        {
            RhubarbCue cue = result.mouthCues[i];
            if (cue == null || cue.start < 0f || cue.start > duration ||
                cue.end < cue.start || cue.start < previousStart)
                return new List<VisemeFrame>(); // date corupte → fallback

            if (!TryMapRhubarbShape(cue.value, out AvatarViseme viseme))
                return new List<VisemeFrame>();

            frames.Add(new VisemeFrame(cue.start, viseme));
            previousStart = cue.start;
        }

        if (frames.Count == 0)
            return frames;

        if (frames[0].time > 0f)
            frames.Insert(0, new VisemeFrame(0f, AvatarViseme.Rest));
        if (frames[frames.Count - 1].time < duration)
            frames.Add(new VisemeFrame(duration, AvatarViseme.Rest));

        return frames;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Timeline estimat din text (fallback când Rhubarb nu e disponibil)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Descompune textul în unități fonetice, le atribuie viseme și le distribuie
    /// proporțional în timp pe durata audio-ului.
    /// </summary>
    public static List<VisemeFrame> CreateTimeline(string text, float duration, string language)
    {
        List<VisemeFrame> frames = new List<VisemeFrame>();
        if (string.IsNullOrWhiteSpace(text) || duration <= 0f)
            return frames;

        List<string> units = GetUnits(text, language);
        if (units.Count == 0)
            return frames;

        float totalWeight = 0f;
        for (int i = 0; i < units.Count; i++)
            totalWeight += GetWeight(units[i]);

        if (totalWeight <= 0f)
            return frames;

        float elapsedWeight = 0f;
        for (int i = 0; i < units.Count; i++)
        {
            string unit = units[i];
            frames.Add(new VisemeFrame(duration * elapsedWeight / totalWeight, MapUnit(unit)));
            elapsedWeight += GetWeight(unit);
        }

        frames.Add(new VisemeFrame(duration, AvatarViseme.Rest));
        return frames;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Segmentare text în unități fonetice
    // ────────────────────────────────────────────────────────────────────────

    // Vocale românești (incluzând diacritice)
    private static readonly System.Collections.Generic.HashSet<char> RomanianVowels =
        new System.Collections.Generic.HashSet<char>
        {
            'a', 'e', 'i', 'o', 'u',
            '\u0103', // ă
            '\u00e2', // â
            '\u00ee'  // î
        };

    /// <summary>
    /// Parcurge textul și extrage unități fonetice.
    /// Română: aplică reguli silabice (V, CV, CCV) conform ortografiei românești —
    ///         consoanele sunt ataşate vocalei următoare, iar vocala primeşte pondere
    ///         mai mare pentru a reprezenta silaba.
    /// Engleză: grupează digrame fonetice (th, sh, wh, diftonguri).
    /// </summary>
    private static List<string> GetUnits(string text, string language)
    {
        List<string> units = new List<string>();
        string normalized = NormalizeText(text);
        bool isRomanian = IsRomanian(language);

        if (isRomanian)
        {
            // ── Română: reguli silabice V, CV, CCV ───────────────────────
            // Regulă: Parcurgem cuvintele; la fiecare vocală emitem mai întâi
            // consoanele buferate (ca unități individuale), apoi vocala.
            // Consoanele finale de cuvânt (coda) sunt emise după vocala precedentă.
            // Digrame: ch→"ch", gh→"g" (fonetică românească).
            List<string> consonantBuffer = new List<string>();

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];

                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    // Flushăm consoanele rămase (coda ultimei silabe) înaintea pauzei
                    foreach (string con in consonantBuffer)
                        units.Add(con);
                    consonantBuffer.Clear();
                    units.Add(c.ToString());
                    continue;
                }

                if (!char.IsLetter(c))
                    continue;

                // Digrame ch și gh
                if (i + 1 < normalized.Length)
                {
                    string pair = normalized.Substring(i, 2);
                    if (pair == "ch") { consonantBuffer.Add("ch"); i++; continue; }
                    if (pair == "gh") { consonantBuffer.Add("g");  i++; continue; }
                }

                if (RomanianVowels.Contains(c))
                {
                    // Vocală — emitem consoanele buferate (onset), apoi vocala
                    foreach (string con in consonantBuffer)
                        units.Add(con);
                    consonantBuffer.Clear();
                    units.Add(c.ToString());
                }
                else
                {
                    // Consoană — o bufferăm
                    consonantBuffer.Add(c.ToString());
                }
            }

            // Consoanele rămase la finalul textului
            foreach (string con in consonantBuffer)
                units.Add(con);
        }
        else
        {
            // ── Engleză: digrame fonetice și diftonguri ───────────────────
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];

                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    units.Add(c.ToString());
                    continue;
                }

                if (!char.IsLetter(c))
                    continue;

                if (i + 1 < normalized.Length)
                {
                    string pair = normalized.Substring(i, 2);
                    if (pair == "th") { units.Add("th"); i++; continue; }
                    if (pair == "sh") { units.Add("ch"); i++; continue; }
                    if (pair == "wh") { units.Add("w");  i++; continue; }
                    if (pair == "ah" || pair == "ae" || pair == "eh" ||
                        pair == "ee" || pair == "uh" || pair == "oo")
                    {
                        units.Add(pair);
                        i++;
                        continue;
                    }
                }

                units.Add(c.ToString());
            }
        }

        return units;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Mapare unitate fonetică → visemă
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Matrice de mapare completă pentru română și engleză.
    /// Diacriticele românești sunt tratate explicit.
    /// </summary>
    private static AvatarViseme MapUnit(string unit)
    {
        if (unit == null || unit.Length == 0)
            return AvatarViseme.Rest;

        switch (unit)
        {
            // ── Vocale deschise / rotunde ──────────────────────────────────
            // Română: A, Ă, Â, Î, O, U
            // Engleză: A, O, UH, AH
            case "a":
            case "\u0103": // ă
            case "\u00e2": // â
            case "\u00ee": // î (ca literă inițială sau corp de cuvânt)
            case "o":
            case "u":
            case "ah":
            case "uh":
                return AvatarViseme.OpenRound;

            // ── Vocale medii / laterale ────────────────────────────────────
            // Română: E, I   |   Engleză: E, I, AE, EE, EH
            case "e":
            case "i":
            case "ae":
            case "ee":
            case "eh":
                return AvatarViseme.MidSpread;

            // ── Ocluzive bilabiale → buze închise ─────────────────────────
            case "b":
            case "m":
            case "p":
                return AvatarViseme.Closed;

            // ── Labiodentale ───────────────────────────────────────────────
            case "f":
            case "v":
                return AvatarViseme.TeethOnLip;

            // ── Labiale rotunjite ──────────────────────────────────────────
            case "w":
            case "oo":
                return AvatarViseme.Pucker;

            // ── Africate / Siflante CH ─────────────────────────────────────
            case "ch":
            case "\u0219": // ș (s cu virgulă — literă română)
            case "\u015f": // ş (s cu sedilă — variantă)
                return AvatarViseme.RhubarbCh;

            // ── Alveolare D, T, L ──────────────────────────────────────────
            case "d":
            case "t":
            case "\u0163": // ț (t cu virgulă)
            case "\u021b": // ț (t cu virgulă — standard unicode)
            case "l":
                return AvatarViseme.RhubarbDd;

            // ── Vibrantă R ────────────────────────────────────────────────
            case "r":
                return AvatarViseme.RhubarbRr;

            // ── Siflante S, Z ──────────────────────────────────────────────
            case "s":
            case "z":
                return AvatarViseme.RhubarbSs;

            // ── TH englezesc ───────────────────────────────────────────────
            case "th":
                return AvatarViseme.RhubarbTh;

            // ── Velare K, G, H, C (uneori) ───────────────────────────────
            case "k":
            case "g":
            case "h":
            case "c":
                return AvatarViseme.RhubarbKk;

            // ── Nazală N ──────────────────────────────────────────────────
            case "n":
                return AvatarViseme.RhubarbNn;

            // ── Pauze / spații / punctuație ───────────────────────────────
            case " ":
            case "\t":
            case "\n":
            case "\r":
            case ".":
            case "!":
            case "?":
            case ",":
            case ";":
            case ":":
                return AvatarViseme.Rest;

            // ── Orice altă consoană (j, q, x, y etc.) ────────────────────
            default:
                return AvatarViseme.Consonant;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Mapare Rhubarb A-H, X
    // ────────────────────────────────────────────────────────────────────────

    private static bool TryMapRhubarbShape(string value, out AvatarViseme viseme)
    {
        switch (value)
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

    // ────────────────────────────────────────────────────────────────────────
    // Utilitare
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ponderea temporală a unei unități.
    /// Vocalele primesc mai mult timp (reprezintă nucleul silabei),
    /// consoanele mai puțin, spațiile/punctuația adaugă pauze naturale.
    /// </summary>
    private static float GetWeight(string unit)
    {
        if (unit.Length == 1 && char.IsWhiteSpace(unit[0]))
            return 2.0f;
        if (unit.Length == 1 && char.IsPunctuation(unit[0]))
            return 1.2f;
        // Vocale deschise românești (nucleul silabei) — durată mai mare
        char ch = unit[0];
        if (ch == 'a' || ch == 'o' || ch == 'u' ||
            ch == '\u0103' || ch == '\u00e2' || ch == '\u00ee')
            return 2.2f;
        // Vocale medii
        if (ch == 'e' || ch == 'i')
            return 1.8f;
        // Diftonguri englezești
        if (unit == "ah" || unit == "uh" || unit == "oo" || unit == "ee")
            return 2.2f;
        if (unit == "ae" || unit == "eh")
            return 1.8f;
        return unit.Length; // consoane și digrame
    }

    /// <summary>Converteste textul la lowercase păstrând diacriticele.</summary>
    private static string NormalizeText(string text)
    {
        return text.ToLowerInvariant();
    }

    private static bool IsRomanian(string language)
    {
        if (string.IsNullOrEmpty(language))
            return true; // default la română dacă nu e specificat

        return language.StartsWith("ro", StringComparison.OrdinalIgnoreCase) ||
               language.StartsWith("roman", StringComparison.OrdinalIgnoreCase);
    }
}
