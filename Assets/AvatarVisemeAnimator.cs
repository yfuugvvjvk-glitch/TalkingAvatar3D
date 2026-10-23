using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animație facială (blendshapes + Lerp) și animații procedurale (mers, flotări).
/// </summary>
public class AvatarVisemeAnimator : MonoBehaviour
{
    [Header("Mesh față")]
    [Tooltip("Setează manual dacă detecția automată nu găsește mesh-ul corect.")]
    [SerializeField] private SkinnedMeshRenderer faceRenderer;

    [Header("Animație")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 12f;

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private bool logBlendShapeNames;
#endif

    // ─── Blendshapes ────────────────────────────────────────────────────────
    private struct ShapeBinding { public SkinnedMeshRenderer renderer; public int index; }
    private readonly Dictionary<string, List<ShapeBinding>> shapeBindings = new Dictionary<string, List<ShapeBinding>>();
    private readonly Dictionary<string, float> currentShapeValues = new Dictionary<string, float>();
    // Set cu numele shape-urilor "reale" (nu alias) — pentru a evita aliasuri duplicate
    private readonly HashSet<string> realShapeNames = new HashSet<string>();
    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private bool warnedAboutInvalidRenderer;
    private float speechDuration;
    private float speechStartedAt;

    // ─── Oase ───────────────────────────────────────────────────────────────
    private Transform headBone;        private Quaternion headRestRot;
    private Transform upperBodyBone;   private Quaternion upperBodyRestRot;
    private Transform avatarRoot;      private Vector3 rootRestPos; private Quaternion rootRestRot;
    private Transform leftLegBone;     private Quaternion leftLegRestRot;
    private Transform rightLegBone;    private Quaternion rightLegRestRot;
    private Transform leftArmBone;     private Quaternion leftArmRestRot;
    private Transform rightArmBone;    private Quaternion rightArmRestRot;

    // ─── Animații corp ───────────────────────────────────────────────────────
    private int   pushUpReps;    private float pushUpStart;  private bool pushUpActive;
    private int   walkSteps;     private float walkStart;    private bool walkActive;
    private string actionStatus = string.Empty;
    public  string ActionStatus => actionStatus;

    // ════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        CacheHeadBone();
        CacheUpperBodyBone();
        // Oasele de mers sunt cache-uite din ierarhie; vor fi re-cache-uite și
        // din sub-arborele avatarului după SetAvatarRoot().
        RefreshBlendShapes();
    }

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    public void RefreshBlendShapes()
    {
        shapeBindings.Clear();
        currentShapeValues.Clear();
        realShapeNames.Clear();
        CacheBlendShapes();
        if (headBone == null)      CacheHeadBone();
        if (upperBodyBone == null) CacheUpperBodyBone();
        CacheWalkBones();
        ResetFaceBlendShapes();
    }

    /// <summary>
    /// Setează rădăcina avatarului GLB după instanțiere și
    /// re-cachez oasele din noul sub-arbore.
    /// </summary>
    public void SetAvatarRoot(Transform root)
    {
        avatarRoot = root;
        if (avatarRoot == null) return;

        rootRestPos = avatarRoot.localPosition;
        rootRestRot = avatarRoot.localRotation;

        // Re-cache oase din sub-arborele avatarului
        CacheHeadBone();
        CacheUpperBodyBone();
        CacheWalkBones();

        // Log ce oase s-au găsit
        Debug.Log($"[Avatar] SetAvatarRoot: {root.name} | " +
                  $"head={headBone?.name ?? "null"} | " +
                  $"spine={upperBodyBone?.name ?? "null"} | " +
                  $"leftLeg={leftLegBone?.name ?? "null"} | " +
                  $"rightLeg={rightLegBone?.name ?? "null"} | " +
                  $"leftArm={leftArmBone?.name ?? "null"} | " +
                  $"rightArm={rightArmBone?.name ?? "null"}");
    }

    public void PlaySpeech(string text, float duration, string language)
    {
        timeline.Clear();
        timeline.AddRange(VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration  = duration;
        speechStartedAt = Time.time;
    }

    public void PlaySpeech(string text, float duration, string language, string rhubarbJson)
    {
        timeline.Clear();
        var rhubarb = VisemeTextMapper.CreateRhubarbTimeline(rhubarbJson, duration);
        timeline.AddRange(rhubarb.Count > 0
            ? rhubarb
            : VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration  = duration;
        speechStartedAt = Time.time;
    }

    public bool StartPushUps(int repetitions)
    {
        if (avatarRoot == null)
        {
            Debug.LogError("[Avatar] avatarRoot este null — avatarul nu e încă încărcat.", this);
            return false;
        }
        RestoreMovementPose();
        pushUpReps    = Mathf.Clamp(repetitions, 1, 20);
        walkActive    = false;
        pushUpStart   = Time.time;
        pushUpActive  = true;
        actionStatus  = $"Avatar: executa {pushUpReps} flotari...";
        return true;
    }

    public bool StartWalking(int steps)
    {
        if (avatarRoot == null)
        {
            Debug.LogError("[Avatar] avatarRoot este null — avatarul nu e încă încărcat.", this);
            return false;
        }
        // Dacă oasele nu s-au găsit, log warning dar permite mersul (va fi
        // animat doar root-ul, fără swing de picioare)
        if (leftLegBone == null || rightLegBone == null)
            Debug.LogWarning("[Avatar] Oasele picioarelor nu au fost găsite. Mersu funcțional fără swing.", this);

        RestoreMovementPose();
        walkSteps   = Mathf.Clamp(steps, 1, 50);
        pushUpActive = false;
        walkStart   = Time.time;
        walkActive  = true;
        actionStatus = $"Avatar: incepe mersul ({walkSteps} pasi)...";
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Update
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        float elapsed = Time.time - speechStartedAt;
        bool  speaking = elapsed < speechDuration && timeline.Count > 0;

        // Animație facială
        if (shapeBindings.Count > 0)
            ApplyViseme(speaking ? GetVisemeAt(elapsed) : AvatarViseme.Rest);

        if (pushUpActive) { UpdatePushUps(); return; }
        if (walkActive)     UpdateWalk();

        // Cap
        if (headBone != null)
        {
            float m = speaking ? Mathf.Sin(elapsed * 4.2f) : 0f;
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                headRestRot * Quaternion.Euler(m * 2.5f, Mathf.Sin(elapsed * 2.1f) * (speaking ? 1.5f : 0f), m * 1.5f),
                Time.deltaTime * blendSpeed);
        }

        // Corp superior (respirație)
        if (upperBodyBone != null)
        {
            float b = speaking ? Mathf.Sin(elapsed * 2.4f) : 0f;
            upperBodyBone.localRotation = Quaternion.Slerp(upperBodyBone.localRotation,
                upperBodyRestRot * Quaternion.Euler(b * 1.5f, 0f, b * 1.0f),
                Time.deltaTime * blendSpeed);
        }

        // Bob vertical ușor
        if (avatarRoot != null && !walkActive)
        {
            float bob = speaking ? Mathf.Sin(elapsed * 3.2f) * 0.016f : 0f;
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                rootRestPos + Vector3.up * bob, Time.deltaTime * blendSpeed);
            avatarRoot.localRotation = Quaternion.Slerp(avatarRoot.localRotation,
                rootRestRot, Time.deltaTime * blendSpeed);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Animații procedurale
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateWalk()
    {
        const float SPP = 0.36f;   // secunde per pas
        const float RET = 0.30f;   // durata revenire
        float e   = Time.time - walkStart;
        float dur = walkSteps * SPP;

        if (e < dur)
        {
            float phase = e * Mathf.PI * 2f / (SPP * 2f);
            float swing = Mathf.Sin(phase);
            RotateBone(leftLegBone,  leftLegRestRot,   swing * 22f);
            RotateBone(rightLegBone, rightLegRestRot, -swing * 22f);
            RotateBone(leftArmBone,  leftArmRestRot,  -swing * 15f);
            RotateBone(rightArmBone, rightArmRestRot,  swing * 15f);
            if (avatarRoot != null)
                avatarRoot.localPosition = rootRestPos + Vector3.up * (Mathf.Abs(swing) * 0.022f);
            actionStatus = $"Avatar: pasi {Mathf.Min(walkSteps, Mathf.FloorToInt(e / SPP))}/{walkSteps}";
            return;
        }

        float rec = Mathf.Clamp01((e - dur) / RET);
        RotateBone(leftLegBone,  leftLegRestRot,  0f, rec);
        RotateBone(rightLegBone, rightLegRestRot, 0f, rec);
        RotateBone(leftArmBone,  leftArmRestRot,  0f, rec);
        RotateBone(rightArmBone, rightArmRestRot, 0f, rec);
        if (avatarRoot != null)
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition, rootRestPos, Time.deltaTime * blendSpeed);
        if (rec >= 1f) { walkActive = false; actionStatus = $"Avatar: a terminat {walkSteps} pasi."; }
    }

    private void UpdatePushUps()
    {
        const float TRANS = 0.65f;
        const float REP   = 1.25f;
        float e    = Time.time - pushUpStart;
        float dur  = pushUpReps * REP;
        Quaternion floorRot = rootRestRot * Quaternion.Euler(90f, 90f, 0f);
        Vector3    floorPos = rootRestPos + Vector3.up * 0.3f;

        if (e < TRANS)
        {
            float t = Mathf.SmoothStep(0f, 1f, e / TRANS);
            avatarRoot.localRotation = Quaternion.Slerp(rootRestRot, floorRot, t);
            avatarRoot.localPosition = Vector3.Lerp(rootRestPos, floorPos, t);
            return;
        }
        if (e < TRANS + dur)
        {
            float et    = e - TRANS;
            float cycle = (et % REP) / REP;
            float lift  = (1f + Mathf.Cos(cycle * Mathf.PI * 2f)) * 0.5f;
            avatarRoot.localRotation = floorRot;
            avatarRoot.localPosition = floorPos + Vector3.up * Mathf.Lerp(0.02f, 0.16f, lift);
            int done = Mathf.Min(pushUpReps, Mathf.FloorToInt(et / REP) + (cycle > 0.8f ? 1 : 0));
            actionStatus = $"Avatar: flotari {done}/{pushUpReps}";
            return;
        }
        float ret = Mathf.Clamp01((e - TRANS - dur) / TRANS);
        avatarRoot.localRotation = Quaternion.Slerp(floorRot, rootRestRot, Mathf.SmoothStep(0f, 1f, ret));
        avatarRoot.localPosition = Vector3.Lerp(floorPos, rootRestPos, ret);
        if (ret >= 1f) { pushUpActive = false; actionStatus = $"Avatar: a terminat {pushUpReps} flotari."; }
    }

    private void RotateBone(Transform bone, Quaternion rest, float angle, float recovery = 0f)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.Slerp(bone.localRotation,
            rest * Quaternion.Euler(angle, 0f, 0f),
            Mathf.Max(Time.deltaTime * blendSpeed, recovery));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Viseme — lookup și aplicare
    // ════════════════════════════════════════════════════════════════════════

    private AvatarViseme GetVisemeAt(float elapsed)
    {
        for (int i = 1; i < timeline.Count; i++)
            if (elapsed < timeline[i].time) return timeline[i - 1].viseme;
        return timeline.Count > 0 ? timeline[timeline.Count - 1].viseme : AvatarViseme.Rest;
    }

    private void ApplyViseme(AvatarViseme viseme)
    {
        float jawOpen = 0f, mouthFunnel = 0f, mouthSmile = 0f, mouthClose = 0f;
        float mouthShrug = 0f, mouthPucker = 0f, mouthStretch = 0f;

        switch (viseme)
        {
            case AvatarViseme.OpenRound:
                jawOpen = 28f; mouthFunnel = 18f; break;
            case AvatarViseme.RhubarbA:
                jawOpen = 32f; mouthFunnel = 10f; break;
            case AvatarViseme.RhubarbD:
                jawOpen = 28f; mouthSmile = 10f; break;

            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
                jawOpen = 16f; mouthSmile = 20f; break;
            case AvatarViseme.RhubarbE:
                jawOpen = 16f; mouthSmile = 22f; break;

            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:
                mouthClose = 55f; break;

            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
                mouthShrug = 30f; jawOpen = 5f; break;

            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:
                mouthPucker = 40f; jawOpen = 8f; break;

            case AvatarViseme.RhubarbCh:
                jawOpen = 10f; mouthPucker = 12f; mouthStretch = 8f; break;

            case AvatarViseme.RhubarbRr:
                jawOpen = 12f; mouthStretch = 8f; break;

            case AvatarViseme.Consonant:
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbTh:
                jawOpen = 8f; mouthStretch = 14f; break;

            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbG:
                jawOpen = 10f; mouthStretch = 10f; break;

            // Rest și default → tot la 0
        }

        SetShape("jawopen",         jawOpen);
        SetShape("mouthfunnel",     mouthFunnel);
        SetShape("mouthsmile",      mouthSmile);
        SetShape("mouthclose",      mouthClose);
        SetShape("mouthshruglower", mouthShrug);
        SetShape("mouthpucker",     mouthPucker);
        SetShape("mouthstretch",    mouthStretch);
    }

    private void SetShape(string shape, float target)
    {
        if (!shapeBindings.TryGetValue(shape, out var bindings)) return;

        currentShapeValues.TryGetValue(shape, out float current);
        float next = Mathf.Lerp(current, target, Time.deltaTime * blendSpeed);
        currentShapeValues[shape] = next;

        foreach (var b in bindings)
        {
            if (b.renderer == null || b.renderer.sharedMesh == null ||
                b.index >= b.renderer.sharedMesh.blendShapeCount)
            {
                if (!warnedAboutInvalidRenderer)
                {
                    Debug.LogWarning("[Avatar] Blendshape invalid — reîncarcă scena.", this);
                    warnedAboutInvalidRenderer = true;
                }
                continue;
            }
            b.renderer.SetBlendShapeWeight(b.index, next);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cache blendshapes — fără duplicate
    // ════════════════════════════════════════════════════════════════════════

    private void CacheBlendShapes()
    {
        var all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (all.Length == 0) { Debug.LogWarning("[Avatar] Niciun SkinnedMeshRenderer găsit.", this); return; }

        // Detecție mesh față
        if (faceRenderer == null)
        {
            // 1. Exact "AvatarHead"
            foreach (var r in all)
                if (r.gameObject.name.Equals("AvatarHead", StringComparison.OrdinalIgnoreCase))
                { faceRenderer = r; break; }

            // 2. Conține "head" sau "face"
            if (faceRenderer == null)
                foreach (var r in all)
                {
                    string n = r.gameObject.name;
                    if (n.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
                    { faceRenderer = r; break; }
                }

            // 3. Mesh cu cei mai mulți blendshapes
            if (faceRenderer == null)
            {
                int max = -1;
                foreach (var r in all)
                {
                    if (r.sharedMesh == null) continue;
                    int c = r.sharedMesh.blendShapeCount;
                    if (c > max) { max = c; faceRenderer = r; }
                }
            }
        }

        if (faceRenderer == null)
        {
            Debug.LogWarning("[Avatar] Nu am găsit mesh-ul feței. Setează Face Renderer manual în Inspector.", this);
            return;
        }

        Debug.Log($"[Avatar] Mesh față: '{faceRenderer.gameObject.name}' " +
                  $"({faceRenderer.sharedMesh?.blendShapeCount ?? 0} blendshapes).", this);

        IndexRenderer(faceRenderer);

#if UNITY_EDITOR
        if (logBlendShapeNames) LogShapes();
#endif
    }

    /// <summary>
    /// Indexează blendshape-urile unui renderer.
    /// Aliasuri Left/Right sunt adăugate NUMAI dacă shape-ul de bază (fără sufix)
    /// nu există deja ca shape independent — evitând aplicarea dublă.
    /// </summary>
    private void IndexRenderer(SkinnedMeshRenderer r)
    {
        if (r?.sharedMesh == null) return;
        var mesh = r.sharedMesh;

        // Pasul 1: înregistrează toate shape-urile reale
        var rawNames = new string[mesh.blendShapeCount];
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string norm = Norm(mesh.GetBlendShapeName(i));
            rawNames[i] = norm;
            realShapeNames.Add(norm);
        }

        // Pasul 2: adaugă binding-uri + aliasuri fără sufix lateral
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string norm = rawNames[i];
            Bind(norm, r, i);

            // Sufix "Left"/"Right" — 4/5 litere
            string baseL = null, baseR = null;
            if (norm.EndsWith("left",  StringComparison.Ordinal)) baseL = norm.Substring(0, norm.Length - 4);
            if (norm.EndsWith("right", StringComparison.Ordinal)) baseR = norm.Substring(0, norm.Length - 5);
            // Sufix "l"/"r" — 1 literă (mouthsmilel, mouthsmiler etc.)
            if (baseL == null && norm.Length > 2 && norm[norm.Length - 1] == 'l')
                baseL = norm.Substring(0, norm.Length - 1);
            if (baseR == null && norm.Length > 2 && norm[norm.Length - 1] == 'r')
                baseR = norm.Substring(0, norm.Length - 1);

            // Adaugă alias NUMAI dacă shape-ul de bază nu există ca shape real
            // (evitând duplicate când avatarul are ATÂT "MouthSmile" CÂT ȘI "MouthSmileLeft")
            if (baseL != null && !realShapeNames.Contains(baseL)) Bind(baseL, r, i);
            if (baseR != null && !realShapeNames.Contains(baseR)) Bind(baseR, r, i);
        }
    }

    private void Bind(string key, SkinnedMeshRenderer r, int idx)
    {
        if (!shapeBindings.TryGetValue(key, out var list))
        {
            list = new List<ShapeBinding>();
            shapeBindings[key] = list;
        }
        // Deduplicare (renderer + index)
        foreach (var b in list)
            if (b.renderer == r && b.index == idx) return;
        list.Add(new ShapeBinding { renderer = r, index = idx });
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cache oase
    // ════════════════════════════════════════════════════════════════════════

    private void CacheHeadBone()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int sep = n.LastIndexOf(':'); if (sep >= 0) n = n.Substring(sep + 1);
            if (n.Equals("head", StringComparison.OrdinalIgnoreCase))
            { headBone = t; headRestRot = t.localRotation; return; }
        }
    }

    private void CacheUpperBodyBone()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.Equals("Spine2",      StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Spine1",      StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Chest",       StringComparison.OrdinalIgnoreCase) ||
                n.Equals("UpperChest",  StringComparison.OrdinalIgnoreCase))
            { upperBodyBone = t; upperBodyRestRot = t.localRotation; return; }
        }
    }

    private void CacheWalkBones()
    {
        // Caută în tot sub-arborele (inclusiv avatarRoot dacă e setat)
        var root = avatarRoot != null ? avatarRoot : transform;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int sep = n.LastIndexOf(':'); if (sep >= 0) n = n.Substring(sep + 1);
            switch (n)
            {
                case "LeftUpLeg":  case "LeftThigh":  case "L_Thigh":
                case "LeftLeg":    case "Left_Thigh":
                    leftLegBone = t; leftLegRestRot = t.localRotation; break;
                case "RightUpLeg": case "RightThigh": case "R_Thigh":
                case "RightLeg":   case "Right_Thigh":
                    rightLegBone = t; rightLegRestRot = t.localRotation; break;
                case "LeftArm":    case "LeftShoulder": case "L_Arm":
                case "Left_Arm":   case "LeftForeArm":
                    if (leftArmBone == null) { leftArmBone = t; leftArmRestRot = t.localRotation; }
                    break;
                case "RightArm":   case "RightShoulder": case "R_Arm":
                case "Right_Arm":  case "RightForeArm":
                    if (rightArmBone == null) { rightArmBone = t; rightArmRestRot = t.localRotation; }
                    break;
            }
        }
    }

    private void RestoreMovementPose()
    {
        if (avatarRoot    != null) { avatarRoot.localPosition    = rootRestPos;       avatarRoot.localRotation    = rootRestRot; }
        if (leftLegBone   != null)   leftLegBone.localRotation   = leftLegRestRot;
        if (rightLegBone  != null)   rightLegBone.localRotation  = rightLegRestRot;
        if (leftArmBone   != null)   leftArmBone.localRotation   = leftArmRestRot;
        if (rightArmBone  != null)   rightArmBone.localRotation  = rightArmRestRot;
        if (headBone      != null)   headBone.localRotation      = headRestRot;
        if (upperBodyBone != null)   upperBodyBone.localRotation = upperBodyRestRot;
    }

    private void ResetFaceBlendShapes()
    {
        if (faceRenderer?.sharedMesh == null) return;
        for (int i = 0; i < faceRenderer.sharedMesh.blendShapeCount; i++)
            faceRenderer.SetBlendShapeWeight(i, 0f);
        currentShapeValues.Clear();
    }

    private static string Norm(string name) =>
        name.Replace("_", "").Replace("-", "").Replace(" ", "").Replace(".", "").ToLowerInvariant();

#if UNITY_EDITOR
    private void LogShapes()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Avatar] {shapeBindings.Count} blendshape keys:");
        foreach (var k in shapeBindings.Keys) sb.AppendLine($"  {k}");
        Debug.Log(sb.ToString(), this);
    }
#endif
}
