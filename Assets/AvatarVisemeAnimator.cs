using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animație avatar 3D fără blendshapes (modelul nu are morph targets ARKit).
/// Lip-sync prin rotirea AvatarTeethLower (maxilar inferior).
/// Animații procedurale: mers, flotări, wave.
/// </summary>
public class AvatarVisemeAnimator : MonoBehaviour
{
    [Header("Animație")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 10f;

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private bool logBones;
#endif

    // ─── Blendshapes (dezactivate — modelul nu are morph targets) ────────────
    // Păstrăm dicționarul gol; SetShape() nu face nimic dacă nu găsește chei
    private readonly Dictionary<string, List<(SkinnedMeshRenderer r, int i)>> shapeBindings
        = new Dictionary<string, List<(SkinnedMeshRenderer, int)>>();
    public int ShapeCount => shapeBindings.Count;

    // ─── Timeline viseme ─────────────────────────────────────────────────────
    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private float speechDuration;
    private float speechStartedAt;

    // ─── Oase ────────────────────────────────────────────────────────────────
    private Transform headBone;          private Quaternion headRestRot;
    private Transform jawBone;           private Quaternion jawRestRot;   // AvatarTeethLower
    private Transform upperBodyBone;     private Quaternion upperBodyRestRot;
    private Transform avatarRoot;        private Vector3 rootRestPos; private Quaternion rootRestRot;
    private Transform leftLegBone;       private Quaternion leftLegRestRot;
    private Transform rightLegBone;      private Quaternion rightLegRestRot;
    private Transform leftArmBone;       private Quaternion leftArmRestRot;
    private Transform rightArmBone;      private Quaternion rightArmRestRot;

    // ─── Animații corp ───────────────────────────────────────────────────────
    private int   pushUpReps;   private float pushUpStart;  private bool pushUpActive;
    private int   walkSteps;    private float walkStart;    private bool walkActive;
    private float waveStart = -999f;                        private bool waveActive;
    private string actionStatus = string.Empty;
    public  string ActionStatus => actionStatus;

    // ════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        CacheHeadBone();
        CacheUpperBodyBone();
    }

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Apelat după încărcarea GLB — re-cache-uiește oasele din sub-arbore.</summary>
    public void SetAvatarRoot(Transform root)
    {
        avatarRoot = root;
        if (avatarRoot == null) return;
        rootRestPos = avatarRoot.localPosition;
        rootRestRot = avatarRoot.localRotation;

        CacheHeadBone();
        CacheJawBone();
        CacheUpperBodyBone();
        CacheWalkBones();

#if UNITY_EDITOR
        if (logBones)
            Debug.Log($"[Avatar] head={headBone?.name ?? "null"} | jaw={jawBone?.name ?? "null"} | " +
                      $"spine={upperBodyBone?.name ?? "null"} | " +
                      $"leftLeg={leftLegBone?.name ?? "null"} | rightLeg={rightLegBone?.name ?? "null"} | " +
                      $"leftArm={leftArmBone?.name ?? "null"} | rightArm={rightArmBone?.name ?? "null"}");
#endif
    }

    /// <summary>Compatibilitate — modelul nu are blendshapes, nu face nimic.</summary>
    public void RefreshBlendShapes()
    {
        shapeBindings.Clear();
        if (headBone == null) CacheHeadBone();
        if (upperBodyBone == null) CacheUpperBodyBone();
        CacheWalkBones();
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

    public bool StartPushUps(int reps)
    {
        if (avatarRoot == null) { Debug.LogError("[Avatar] avatarRoot null."); return false; }
        RestoreMovementPose();
        pushUpReps   = Mathf.Clamp(reps, 1, 20);
        walkActive   = false; waveActive = false;
        pushUpStart  = Time.time; pushUpActive = true;
        actionStatus = $"Avatar: executa {pushUpReps} flotari...";
        return true;
    }

    public bool StartWalking(int steps)
    {
        if (avatarRoot == null) { Debug.LogError("[Avatar] avatarRoot null."); return false; }
        RestoreMovementPose();
        walkSteps    = Mathf.Clamp(steps, 1, 50);
        pushUpActive = false; waveActive = false;
        walkStart    = Time.time; walkActive = true;
        actionStatus = $"Avatar: incepe mersul ({walkSteps} pasi)...";
        return true;
    }

    public bool StartWaving()
    {
        if (rightArmBone == null) { Debug.LogWarning("[Avatar] RightArm nu a fost găsit."); return false; }
        RestoreMovementPose();
        pushUpActive = false; walkActive = false;
        waveStart    = Time.time; waveActive = true;
        actionStatus = "Avatar: saluta!";
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Update
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        float elapsed = Time.time - speechStartedAt;
        bool  speaking = elapsed < speechDuration && timeline.Count > 0;

        // ── Animație maxilar (lip-sync via AvatarTeethLower) ──────────────
        if (jawBone != null)
        {
            float jawAngle = 0f;
            if (speaking)
            {
                AvatarViseme v = GetVisemeAt(elapsed);
                jawAngle = VisemeToJawAngle(v);
            }
            jawBone.localRotation = Quaternion.Slerp(
                jawBone.localRotation,
                jawRestRot * Quaternion.Euler(jawAngle, 0f, 0f),
                Time.deltaTime * blendSpeed * 2f);  // maxilarul se mișcă rapid
        }

        // ── Animații corp ─────────────────────────────────────────────────
        if (pushUpActive) { UpdatePushUps(); return; }
        if (walkActive)     UpdateWalk();
        if (waveActive)     UpdateWave();

        // ── Cap — mișcare subtilă când vorbește ───────────────────────────
        if (headBone != null)
        {
            float nod = speaking ? Mathf.Sin(elapsed * 2.5f) * 1.2f : 0f;
            headBone.localRotation = Quaternion.Slerp(
                headBone.localRotation,
                headRestRot * Quaternion.Euler(nod, 0f, 0f),
                Time.deltaTime * blendSpeed);
        }

        // ── Corp superior — respirație ────────────────────────────────────
        if (upperBodyBone != null)
        {
            float breath = Mathf.Sin(elapsed * 0.8f) * 0.4f;
            upperBodyBone.localRotation = Quaternion.Slerp(
                upperBodyBone.localRotation,
                upperBodyRestRot * Quaternion.Euler(breath, 0f, 0f),
                Time.deltaTime * 3f);
        }

        // ── Bob vertical ușor ─────────────────────────────────────────────
        if (avatarRoot != null && !walkActive && !pushUpActive)
        {
            float bob = speaking ? Mathf.Sin(elapsed * 3f) * 0.005f : 0f;
            avatarRoot.localPosition = Vector3.Lerp(
                avatarRoot.localPosition,
                rootRestPos + Vector3.up * bob,
                Time.deltaTime * blendSpeed);
            avatarRoot.localRotation = Quaternion.Slerp(
                avatarRoot.localRotation, rootRestRot, Time.deltaTime * blendSpeed);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Viseme → unghi maxilar
    // ════════════════════════════════════════════════════════════════════════

    private static float VisemeToJawAngle(AvatarViseme v)
    {
        switch (v)
        {
            case AvatarViseme.OpenRound:
            case AvatarViseme.RhubarbA:
            case AvatarViseme.RhubarbD:
                return 12f;  // deschis larg
            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
            case AvatarViseme.RhubarbE:
                return 7f;   // deschis mediu
            case AvatarViseme.Consonant:
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbTh:
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbRr:
            case AvatarViseme.RhubarbCh:
            case AvatarViseme.RhubarbG:
                return 4f;   // ușor deschis
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:
                return 0f;   // închis
            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
                return 2f;   // aproape închis (F/V)
            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:
                return 3f;   // W/OO
            default:
                return 0f;
        }
    }

    private AvatarViseme GetVisemeAt(float elapsed)
    {
        for (int i = 1; i < timeline.Count; i++)
            if (elapsed < timeline[i].time) return timeline[i - 1].viseme;
        return timeline.Count > 0 ? timeline[timeline.Count - 1].viseme : AvatarViseme.Rest;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Animații procedurale
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateWave()
    {
        const float DUR = 2.5f;
        const float RET = 0.5f;
        float e = Time.time - waveStart;

        if (e < DUR)
        {
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.35f)) * 75f;
            float wave = Mathf.Sin(e * 9f) * 18f;
            RotateBone(rightArmBone, rightArmRestRot, -(lift + wave));
            return;
        }
        float rec = Mathf.Clamp01((e - DUR) / RET);
        RotateBone(rightArmBone, rightArmRestRot, 0f, rec);
        if (rec >= 1f) { waveActive = false; actionStatus = "Avatar: a salutat."; }
    }

    private void UpdateWalk()
    {
        const float SPP = 0.38f;
        const float RET = 0.35f;
        float e   = Time.time - walkStart;
        float dur = walkSteps * SPP;

        if (e < dur)
        {
            float phase = e * Mathf.PI * 2f / (SPP * 2f);
            float swing = Mathf.Sin(phase);
            RotateBone(leftLegBone,  leftLegRestRot,   swing * 28f);
            RotateBone(rightLegBone, rightLegRestRot, -swing * 28f);
            RotateBone(leftArmBone,  leftArmRestRot,  -swing * 20f);
            RotateBone(rightArmBone, rightArmRestRot,  swing * 20f);
            if (avatarRoot != null)
                avatarRoot.localPosition = rootRestPos + Vector3.up * (Mathf.Abs(swing) * 0.018f);
            actionStatus = $"Avatar: pasi {Mathf.Min(walkSteps, Mathf.FloorToInt(e / SPP))}/{walkSteps}";
            return;
        }

        float recovery = Mathf.Clamp01((e - dur) / RET);
        RotateBone(leftLegBone,  leftLegRestRot,  0f, recovery);
        RotateBone(rightLegBone, rightLegRestRot, 0f, recovery);
        RotateBone(leftArmBone,  leftArmRestRot,  0f, recovery);
        RotateBone(rightArmBone, rightArmRestRot, 0f, recovery);
        if (avatarRoot != null)
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition, rootRestPos, Time.deltaTime * blendSpeed);
        if (recovery >= 1f) { walkActive = false; actionStatus = $"Avatar: a terminat {walkSteps} pasi."; }
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
            float et = e - TRANS;
            float cy = (et % REP) / REP;
            float lf = (1f + Mathf.Cos(cy * Mathf.PI * 2f)) * 0.5f;
            avatarRoot.localRotation = floorRot;
            avatarRoot.localPosition = floorPos + Vector3.up * Mathf.Lerp(0.02f, 0.16f, lf);
            int done = Mathf.Min(pushUpReps, Mathf.FloorToInt(et / REP) + (cy > 0.8f ? 1 : 0));
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
    // Cache oase
    // ════════════════════════════════════════════════════════════════════════

    private void CacheHeadBone()
    {
        var root = avatarRoot != null ? avatarRoot : transform;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int sep = n.LastIndexOf(':'); if (sep >= 0) n = n.Substring(sep + 1);
            // Osul "Head" — fără SkinnedMeshRenderer (ăla e AvatarHead mesh-ul)
            if (n.Equals("Head", StringComparison.Ordinal) &&
                t.GetComponent<SkinnedMeshRenderer>() == null)
            { headBone = t; headRestRot = t.localRotation; return; }
        }
    }

    private void CacheJawBone()
    {
        var root = avatarRoot != null ? avatarRoot : transform;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            // Cauta AvatarTeethLower ca proxy pentru maxilar
            if (n.Equals("AvatarTeethLower", StringComparison.OrdinalIgnoreCase))
            { jawBone = t; jawRestRot = t.localRotation; return; }
        }
    }

    private void CacheUpperBodyBone()
    {
        var root = avatarRoot != null ? avatarRoot : transform;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.Equals("Spine2", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Spine1", StringComparison.OrdinalIgnoreCase))
            { upperBodyBone = t; upperBodyRestRot = t.localRotation; return; }
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Equals("Spine", StringComparison.OrdinalIgnoreCase))
            { upperBodyBone = t; upperBodyRestRot = t.localRotation; return; }
        }
    }

    private void CacheWalkBones()
    {
        var root = avatarRoot != null ? avatarRoot : transform;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int sep = n.LastIndexOf(':'); if (sep >= 0) n = n.Substring(sep + 1);
            switch (n)
            {
                case "LeftUpLeg":  leftLegBone  = t; leftLegRestRot  = t.localRotation; break;
                case "RightUpLeg": rightLegBone = t; rightLegRestRot = t.localRotation; break;
                case "LeftArm":    leftArmBone  = t; leftArmRestRot  = t.localRotation; break;
                case "RightArm":   rightArmBone = t; rightArmRestRot = t.localRotation; break;
            }
        }
    }

    private void RestoreMovementPose()
    {
        if (avatarRoot    != null) { avatarRoot.localPosition = rootRestPos; avatarRoot.localRotation = rootRestRot; }
        if (leftLegBone   != null) leftLegBone.localRotation   = leftLegRestRot;
        if (rightLegBone  != null) rightLegBone.localRotation  = rightLegRestRot;
        if (leftArmBone   != null) leftArmBone.localRotation   = leftArmRestRot;
        if (rightArmBone  != null) rightArmBone.localRotation  = rightArmRestRot;
        if (headBone      != null) headBone.localRotation      = headRestRot;
        if (upperBodyBone != null) upperBodyBone.localRotation = upperBodyRestRot;
        if (jawBone       != null) jawBone.localRotation       = jawRestRot;
    }
}
