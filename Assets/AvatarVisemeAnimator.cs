using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animație avatar 3D fără blendshapes (modelul nu are morph targets ARKit).
/// Lip-sync prin rotirea AvatarTeethLower (maxilar inferior).
/// Animații procedurale: mers, flotări, wave, ridică brațe, înot, săritură,
/// rotație cap, dans. Control tastatură.
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
    private readonly Dictionary<string, List<(SkinnedMeshRenderer r, int i)>> shapeBindings
        = new Dictionary<string, List<(SkinnedMeshRenderer, int)>>();
    public int ShapeCount => 0;

    // ─── Timeline viseme ─────────────────────────────────────────────────────
    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private float speechDuration;
    private float speechStartedAt;

    // ─── Oase ────────────────────────────────────────────────────────────────
    private Transform headBone;          private Quaternion headRestRot;
    private Transform jawBone;           private Quaternion jawRestRot;
    private Transform neckBone;          private Quaternion neckRestRot;
    private Transform upperBodyBone;     private Quaternion upperBodyRestRot;
    private Transform avatarRoot;        private Vector3 rootRestPos; private Quaternion rootRestRot;
    private Transform leftLegBone;       private Quaternion leftLegRestRot;
    private Transform rightLegBone;      private Quaternion rightLegRestRot;
    private Transform leftArmBone;       private Quaternion leftArmRestRot;
    private Transform rightArmBone;      private Quaternion rightArmRestRot;
    private Transform leftForeArmBone;   private Quaternion leftForeArmRestRot;
    private Transform rightForeArmBone;  private Quaternion rightForeArmRestRot;
    private Transform hipsBone;          private Quaternion hipsRestRot;

    // ─── Stare animații ───────────────────────────────────────────────────────
    // Push-ups
    private int   pushUpReps;   private float pushUpStart;   private bool pushUpActive;
    // Walk
    private int   walkSteps;    private float walkStart;     private bool walkActive;
    // Wave
    private float waveStart   = -999f;                       private bool waveActive;
    // Raise arms
    private float raiseStart  = -999f;                       private bool raiseActive;
    // Swim
    private float swimDuration;  private float swimStart;    private bool swimActive;
    // Jump
    private int   jumpReps;    private float jumpStart;      private bool jumpActive;
    // Head rotation
    private float headRotAngle; private float headRotSpeed; private float headRotStart; private bool headRotActive;
    // Dance
    private float danceStart  = -999f;                       private bool danceActive;

    // Tastatură
    private float keyboardRotY;   // rotire avatar acumulată pe Y

    private string actionStatus = string.Empty;
    public  string ActionStatus => actionStatus;

    // ════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        CacheAllBones();
    }

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Apelat după încărcarea GLB — re-cache-uiește oasele.</summary>
    public void SetAvatarRoot(Transform root)
    {
        avatarRoot = root;
        if (avatarRoot == null) return;
        rootRestPos = avatarRoot.localPosition;
        rootRestRot = avatarRoot.localRotation;
        CacheAllBones();

#if UNITY_EDITOR
        if (logBones)
            Debug.Log($"[Avatar] head={headBone?.name ?? "null"} | jaw={jawBone?.name ?? "null"} | " +
                      $"spine={upperBodyBone?.name ?? "null"} | " +
                      $"leftLeg={leftLegBone?.name ?? "null"} | rightLeg={rightLegBone?.name ?? "null"} | " +
                      $"leftArm={leftArmBone?.name ?? "null"} | rightArm={rightArmBone?.name ?? "null"}");
#endif
    }

    /// <summary>Compatibilitate — modelul nu are blendshapes.</summary>
    public void RefreshBlendShapes()
    {
        shapeBindings.Clear();
        CacheAllBones();
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

    // ─── Animații publice ─────────────────────────────────────────────────────

    public bool StartWalking(int steps)
    {
        if (avatarRoot == null) { Debug.LogError("[Avatar] avatarRoot null."); return false; }
        StopAllAnimations();
        walkSteps    = Mathf.Clamp(steps, 1, 50);
        walkStart    = Time.time;
        walkActive   = true;
        actionStatus = $"Avatar: execută {walkSteps} pași...";
        return true;
    }

    public bool StartPushUps(int reps)
    {
        if (avatarRoot == null) { Debug.LogError("[Avatar] avatarRoot null."); return false; }
        StopAllAnimations();
        pushUpReps   = Mathf.Clamp(reps, 1, 20);
        pushUpStart  = Time.time;
        pushUpActive = true;
        actionStatus = $"Avatar: execută {pushUpReps} flotări...";
        return true;
    }

    public bool StartWaving()
    {
        if (rightArmBone == null) { Debug.LogWarning("[Avatar] RightArm nu a fost găsit."); return false; }
        StopAllAnimations();
        waveStart  = Time.time;
        waveActive = true;
        actionStatus = "Avatar: salută!";
        return true;
    }

    public bool StartRaisingArms()
    {
        if (leftArmBone == null || rightArmBone == null)
        { Debug.LogWarning("[Avatar] Brațele nu au fost găsite."); return false; }
        StopAllAnimations();
        raiseStart  = Time.time;
        raiseActive = true;
        actionStatus = "Avatar: ridică brațele!";
        return true;
    }

    public bool StartSwimming(int seconds)
    {
        if (leftArmBone == null || rightArmBone == null)
        { Debug.LogWarning("[Avatar] Brațele nu au fost găsite."); return false; }
        StopAllAnimations();
        swimDuration = Mathf.Clamp(seconds, 1, 30);
        swimStart    = Time.time;
        swimActive   = true;
        actionStatus = $"Avatar: înoată {seconds} secunde...";
        return true;
    }

    public bool StartJumping(int reps)
    {
        if (avatarRoot == null) { Debug.LogError("[Avatar] avatarRoot null."); return false; }
        StopAllAnimations();
        jumpReps   = Mathf.Clamp(reps, 1, 20);
        jumpStart  = Time.time;
        jumpActive = true;
        actionStatus = $"Avatar: execută {jumpReps} sărituri...";
        return true;
    }

    public bool StartHeadRotation(float angle, float speed)
    {
        if (headBone == null) { Debug.LogWarning("[Avatar] Head bone nu a fost găsit."); return false; }
        StopAllAnimations();
        headRotAngle  = angle;
        headRotSpeed  = speed;
        headRotStart  = Time.time;
        headRotActive = true;
        actionStatus  = "Avatar: rotește capul.";
        return true;
    }

    public bool StartDancing()
    {
        if (avatarRoot == null) { Debug.LogError("[Avatar] avatarRoot null."); return false; }
        StopAllAnimations();
        danceStart  = Time.time;
        danceActive = true;
        actionStatus = "Avatar: dansează!";
        return true;
    }

    // ─── Rotate avatar root (pentru butoane UI și taste A/D) ─────────────────

    public void RotateAvatar(float degrees)
    {
        if (avatarRoot == null) return;
        avatarRoot.Rotate(Vector3.up, degrees, Space.Self);
        rootRestRot = avatarRoot.localRotation;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Update
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        HandleKeyboard();

        float elapsed = Time.time - speechStartedAt;
        bool  speaking = elapsed < speechDuration && timeline.Count > 0;

        // ── Lip-sync: maxilar ─────────────────────────────────────────────
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
                Time.deltaTime * blendSpeed * 2f);
        }

        // ── Animații corp (exclusive) ─────────────────────────────────────
        if (pushUpActive) { UpdatePushUps(); return; }
        if (swimActive)   { UpdateSwim();   return; }
        if (walkActive)     UpdateWalk();
        if (waveActive)     UpdateWave();
        if (raiseActive)    UpdateRaiseArms();
        if (jumpActive)     UpdateJump();
        if (headRotActive)  UpdateHeadRotation();
        if (danceActive)    UpdateDance();

        // ── Idle: cap nodding când vorbește ───────────────────────────────
        if (headBone != null && !headRotActive)
        {
            float nod = speaking ? Mathf.Sin(elapsed * 2.5f) * 1.2f : 0f;
            headBone.localRotation = Quaternion.Slerp(
                headBone.localRotation,
                headRestRot * Quaternion.Euler(nod, 0f, 0f),
                Time.deltaTime * blendSpeed);
        }

        // ── Idle: respirație Spine2 ───────────────────────────────────────
        if (upperBodyBone != null)
        {
            float breath = Mathf.Sin(Time.time * 0.8f) * 0.4f;
            upperBodyBone.localRotation = Quaternion.Slerp(
                upperBodyBone.localRotation,
                upperBodyRestRot * Quaternion.Euler(breath, 0f, 0f),
                Time.deltaTime * 3f);
        }

        // ── Idle: brațe coborâte natural (postură) ────────────────────────
        if (!walkActive && !waveActive && !raiseActive && !swimActive && !danceActive && !pushUpActive)
        {
            float breathArm = Mathf.Sin(Time.time * 0.8f) * 1.5f;
            if (leftArmBone != null)
                leftArmBone.localRotation = Quaternion.Slerp(
                    leftArmBone.localRotation,
                    leftArmRestRot * Quaternion.Euler(breathArm, 0f, 8f),
                    Time.deltaTime * 3f);
            if (rightArmBone != null)
                rightArmBone.localRotation = Quaternion.Slerp(
                    rightArmBone.localRotation,
                    rightArmRestRot * Quaternion.Euler(breathArm, 0f, -8f),
                    Time.deltaTime * 3f);
        }

        // ── Bob vertical idle ──────────────────────────────────────────────
        if (avatarRoot != null && !walkActive && !pushUpActive && !jumpActive && !swimActive)
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
    // Control tastatură
    // ════════════════════════════════════════════════════════════════════════

    private void HandleKeyboard()
    {
        // W/S — bob vertical manual
        if (Input.GetKey(KeyCode.W) && avatarRoot != null)
            avatarRoot.localPosition += Vector3.up * (0.1f * Time.deltaTime * 5f);
        if (Input.GetKey(KeyCode.S) && avatarRoot != null)
            avatarRoot.localPosition -= Vector3.up * (0.1f * Time.deltaTime * 5f);

        // A/D — rotire stânga/dreapta
        if (Input.GetKey(KeyCode.A))  RotateAvatar(-45f * Time.deltaTime);
        if (Input.GetKey(KeyCode.D))  RotateAvatar( 45f * Time.deltaTime);

        // Q/E — rotire cap stânga/dreapta
        if (headBone != null)
        {
            if (Input.GetKey(KeyCode.Q))
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f, -40f, 0f), Time.deltaTime * blendSpeed);
            else if (Input.GetKey(KeyCode.E))
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f,  40f, 0f), Time.deltaTime * blendSpeed);
        }

        // Taste animații (one-shot pe KeyDown)
        if (Input.GetKeyDown(KeyCode.Alpha1)) StartWaving();
        if (Input.GetKeyDown(KeyCode.Alpha2)) StartWalking(5);
        if (Input.GetKeyDown(KeyCode.Alpha3)) StartPushUps(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) StartJumping(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) StartSwimming(3);
        if (Input.GetKeyDown(KeyCode.Alpha6)) StartDancing();
        if (Input.GetKeyDown(KeyCode.Alpha7)) StartRaisingArms();
        if (Input.GetKeyDown(KeyCode.Alpha8)) StartHeadRotation(45f, 60f);
        if (Input.GetKeyDown(KeyCode.Space))  StartJumping(1);
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
                return 12f;
            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
            case AvatarViseme.RhubarbE:
                return 7f;
            case AvatarViseme.Consonant:
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbTh:
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbRr:
            case AvatarViseme.RhubarbCh:
            case AvatarViseme.RhubarbG:
                return 4f;
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:
                return 0f;
            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
                return 2f;
            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:
                return 3f;
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
            RotateBoneZ(rightArmBone, rightArmRestRot, -(lift + wave));
            return;
        }
        float rec = Mathf.Clamp01((e - DUR) / RET);
        RotateBoneZ(rightArmBone, rightArmRestRot, 0f, rec);
        if (rec >= 1f) { waveActive = false; actionStatus = "Avatar: a salutat."; }
    }

    private void UpdateRaiseArms()
    {
        const float UP  = 0.5f;   // timp urcare
        const float HOLD = 1.0f;  // timp ținut sus
        const float DWN = 0.5f;   // timp coborâre
        float e = Time.time - raiseStart;
        float total = UP + HOLD + DWN;

        if (e < UP)
        {
            float t = Mathf.SmoothStep(0f, 1f, e / UP);
            RotateBoneZ(leftArmBone,  leftArmRestRot,   90f * t);
            RotateBoneZ(rightArmBone, rightArmRestRot, -90f * t);
            return;
        }
        if (e < UP + HOLD)
        {
            RotateBoneZ(leftArmBone,  leftArmRestRot,   90f);
            RotateBoneZ(rightArmBone, rightArmRestRot, -90f);
            return;
        }
        if (e < total)
        {
            float t = Mathf.SmoothStep(0f, 1f, (e - UP - HOLD) / DWN);
            RotateBoneZ(leftArmBone,  leftArmRestRot,   90f * (1f - t));
            RotateBoneZ(rightArmBone, rightArmRestRot, -90f * (1f - t));
            return;
        }
        // Terminat
        RotateBoneZ(leftArmBone,  leftArmRestRot, 0f);
        RotateBoneZ(rightArmBone, rightArmRestRot, 0f);
        raiseActive = false;
        actionStatus = "Avatar: a ridicat brațele.";
    }

    private void UpdateSwim()
    {
        const float RET = 0.5f;
        float e = Time.time - swimStart;

        if (e < swimDuration)
        {
            // Crawl: brațe se rotesc alternativ pe X (180° offset)
            float leftRot  = Mathf.Sin(e * 2f * Mathf.PI) * 90f;
            float rightRot = Mathf.Sin(e * 2f * Mathf.PI + Mathf.PI) * 90f;
            if (leftArmBone  != null) leftArmBone.localRotation  = Quaternion.Slerp(leftArmBone.localRotation,
                leftArmRestRot  * Quaternion.Euler(leftRot,  0f, 0f), Time.deltaTime * blendSpeed);
            if (rightArmBone != null) rightArmBone.localRotation = Quaternion.Slerp(rightArmBone.localRotation,
                rightArmRestRot * Quaternion.Euler(rightRot, 0f, 0f), Time.deltaTime * blendSpeed);

            // Corp: legănat lateral ușor
            if (upperBodyBone != null)
                upperBodyBone.localRotation = Quaternion.Slerp(upperBodyBone.localRotation,
                    upperBodyRestRot * Quaternion.Euler(0f, 0f, Mathf.Sin(e * 2f) * 8f),
                    Time.deltaTime * 5f);

            // Bob vertical corpul
            if (avatarRoot != null)
                avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                    rootRestPos + Vector3.up * (Mathf.Sin(e * 4f) * 0.03f),
                    Time.deltaTime * blendSpeed);

            actionStatus = $"Avatar: înoată... ({(int)(swimDuration - e)}s)";
            return;
        }

        // Recovery
        float rec = Mathf.Clamp01((e - swimDuration) / RET);
        if (leftArmBone  != null) leftArmBone.localRotation  = Quaternion.Slerp(leftArmBone.localRotation,
            leftArmRestRot,  rec);
        if (rightArmBone != null) rightArmBone.localRotation = Quaternion.Slerp(rightArmBone.localRotation,
            rightArmRestRot, rec);
        if (upperBodyBone != null) upperBodyBone.localRotation = Quaternion.Slerp(upperBodyBone.localRotation,
            upperBodyRestRot, rec);
        if (avatarRoot != null)   avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
            rootRestPos, Time.deltaTime * blendSpeed);
        if (rec >= 1f) { swimActive = false; actionStatus = "Avatar: a terminat înotul."; RestoreMovementPose(); }
    }

    private void UpdateJump()
    {
        const float SPJ = 0.5f;  // secunde per săritură
        const float RET = 0.3f;
        float e   = Time.time - jumpStart;
        float dur = jumpReps * SPJ;

        if (e < dur)
        {
            float cy  = (e % SPJ) / SPJ;            // 0..1 per ciclu
            float lft = Mathf.Sin(cy * Mathf.PI);   // bob sus-jos (sin 0..π)
            if (avatarRoot != null)
                avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                    rootRestPos + Vector3.up * (lft * 0.3f),
                    Time.deltaTime * blendSpeed * 3f);
            // Genunchi: cotire ușoară la aterizare
            if (leftLegBone  != null) leftLegBone.localRotation  = Quaternion.Slerp(leftLegBone.localRotation,
                leftLegRestRot  * Quaternion.Euler((1f - lft) * -20f, 0f, 0f), Time.deltaTime * blendSpeed);
            if (rightLegBone != null) rightLegBone.localRotation = Quaternion.Slerp(rightLegBone.localRotation,
                rightLegRestRot * Quaternion.Euler((1f - lft) * -20f, 0f, 0f), Time.deltaTime * blendSpeed);

            int done = Mathf.Min(jumpReps, Mathf.FloorToInt(e / SPJ) + 1);
            actionStatus = $"Avatar: săritura {done}/{jumpReps}";
            return;
        }

        float recovery = Mathf.Clamp01((e - dur) / RET);
        if (avatarRoot != null)
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition, rootRestPos, Time.deltaTime * blendSpeed);
        if (leftLegBone  != null) leftLegBone.localRotation  = Quaternion.Slerp(leftLegBone.localRotation,  leftLegRestRot,  recovery);
        if (rightLegBone != null) rightLegBone.localRotation = Quaternion.Slerp(rightLegBone.localRotation, rightLegRestRot, recovery);
        if (recovery >= 1f) { jumpActive = false; actionStatus = $"Avatar: a terminat {jumpReps} sărituri."; RestoreMovementPose(); }
    }

    private void UpdateHeadRotation()
    {
        const float CYCLE = 2f;  // o oscilație completă durată
        const float DUR   = 4f;  // durata totală
        const float RET   = 0.4f;
        float e = Time.time - headRotStart;

        if (e < DUR)
        {
            float ang = Mathf.Sin(e * Mathf.PI * 2f / CYCLE) * headRotAngle;
            if (headBone != null)
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f, ang, 0f),
                    Time.deltaTime * headRotSpeed * Time.deltaTime);
            return;
        }
        float rec = Mathf.Clamp01((e - DUR) / RET);
        if (headBone != null)
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation, headRestRot, rec);
        if (rec >= 1f) { headRotActive = false; actionStatus = "Avatar: a rotit capul."; }
    }

    private void UpdateDance()
    {
        const float DUR = 4f;
        const float RET = 0.5f;
        float e = Time.time - danceStart;

        if (e < DUR)
        {
            // Bob corp ritmic 1.2Hz
            float bob = Mathf.Sin(e * Mathf.PI * 2f * 1.2f) * 0.04f;
            if (avatarRoot != null)
                avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                    rootRestPos + Vector3.up * bob, Time.deltaTime * blendSpeed);

            // Swing brațe ritmic, în opoziție
            float swingL =  Mathf.Sin(e * Mathf.PI * 2f * 1.2f) * 35f;
            float swingR = -swingL;
            if (leftArmBone  != null) leftArmBone.localRotation  = Quaternion.Slerp(leftArmBone.localRotation,
                leftArmRestRot  * Quaternion.Euler(swingL, 0f, 0f), Time.deltaTime * blendSpeed);
            if (rightArmBone != null) rightArmBone.localRotation = Quaternion.Slerp(rightArmBone.localRotation,
                rightArmRestRot * Quaternion.Euler(swingR, 0f, 0f), Time.deltaTime * blendSpeed);

            // Balans corp
            if (upperBodyBone != null)
                upperBodyBone.localRotation = Quaternion.Slerp(upperBodyBone.localRotation,
                    upperBodyRestRot * Quaternion.Euler(0f, Mathf.Sin(e * 1.2f * Mathf.PI * 2f) * 6f, 0f),
                    Time.deltaTime * 5f);

            // Rotire ușoară șolduri
            if (hipsBone != null)
                hipsBone.localRotation = Quaternion.Slerp(hipsBone.localRotation,
                    hipsRestRot * Quaternion.Euler(0f, Mathf.Sin(e * 2.4f * Mathf.PI * 2f) * 10f, 0f),
                    Time.deltaTime * 6f);

            actionStatus = "Avatar: dansează!";
            return;
        }

        // Recovery
        float rec = Mathf.Clamp01((e - DUR) / RET);
        if (leftArmBone  != null) leftArmBone.localRotation  = Quaternion.Slerp(leftArmBone.localRotation,  leftArmRestRot,  rec);
        if (rightArmBone != null) rightArmBone.localRotation = Quaternion.Slerp(rightArmBone.localRotation, rightArmRestRot, rec);
        if (upperBodyBone != null) upperBodyBone.localRotation = Quaternion.Slerp(upperBodyBone.localRotation, upperBodyRestRot, rec);
        if (hipsBone      != null) hipsBone.localRotation      = Quaternion.Slerp(hipsBone.localRotation,      hipsRestRot,      rec);
        if (avatarRoot    != null) avatarRoot.localPosition    = Vector3.Lerp(avatarRoot.localPosition, rootRestPos, rec);
        if (rec >= 1f) { danceActive = false; actionStatus = "Avatar: a terminat dansul."; RestoreMovementPose(); }
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
            actionStatus = $"Avatar: pași {Mathf.Min(walkSteps, Mathf.FloorToInt(e / SPP))}/{walkSteps}";
            return;
        }

        float recovery = Mathf.Clamp01((e - dur) / RET);
        RotateBone(leftLegBone,  leftLegRestRot,  0f, recovery);
        RotateBone(rightLegBone, rightLegRestRot, 0f, recovery);
        RotateBone(leftArmBone,  leftArmRestRot,  0f, recovery);
        RotateBone(rightArmBone, rightArmRestRot, 0f, recovery);
        if (avatarRoot != null)
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition, rootRestPos, Time.deltaTime * blendSpeed);
        if (recovery >= 1f) { walkActive = false; actionStatus = $"Avatar: a terminat {walkSteps} pași."; RestoreMovementPose(); }
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
            actionStatus = $"Avatar: flotări {done}/{pushUpReps}";
            return;
        }
        float ret = Mathf.Clamp01((e - TRANS - dur) / TRANS);
        avatarRoot.localRotation = Quaternion.Slerp(floorRot, rootRestRot, Mathf.SmoothStep(0f, 1f, ret));
        avatarRoot.localPosition = Vector3.Lerp(floorPos, rootRestPos, ret);
        if (ret >= 1f) { pushUpActive = false; actionStatus = $"Avatar: a terminat {pushUpReps} flotări."; RestoreMovementPose(); }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Utilitare rotire oase
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Rotire pe axa X față de poza de rest.</summary>
    private void RotateBone(Transform bone, Quaternion rest, float angleX, float recovery = 0f)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.Slerp(bone.localRotation,
            rest * Quaternion.Euler(angleX, 0f, 0f),
            Mathf.Max(Time.deltaTime * blendSpeed, recovery));
    }

    /// <summary>Rotire pe axa Z față de poza de rest (ridicat brațe etc.).</summary>
    private void RotateBoneZ(Transform bone, Quaternion rest, float angleZ, float recovery = 0f)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.Slerp(bone.localRotation,
            rest * Quaternion.Euler(0f, 0f, angleZ),
            Mathf.Max(Time.deltaTime * blendSpeed, recovery));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Stare internă
    // ════════════════════════════════════════════════════════════════════════

    private void StopAllAnimations()
    {
        RestoreMovementPose();
        pushUpActive = false; walkActive   = false; waveActive    = false;
        raiseActive  = false; swimActive   = false; jumpActive    = false;
        headRotActive= false; danceActive  = false;
    }

    private void RestoreMovementPose()
    {
        if (avatarRoot    != null) { avatarRoot.localPosition = rootRestPos; avatarRoot.localRotation = rootRestRot; }
        if (leftLegBone   != null) leftLegBone.localRotation   = leftLegRestRot;
        if (rightLegBone  != null) rightLegBone.localRotation  = rightLegRestRot;
        if (leftArmBone   != null) leftArmBone.localRotation   = leftArmRestRot;
        if (rightArmBone  != null) rightArmBone.localRotation  = rightArmRestRot;
        if (leftForeArmBone  != null) leftForeArmBone.localRotation  = leftForeArmRestRot;
        if (rightForeArmBone != null) rightForeArmBone.localRotation = rightForeArmRestRot;
        if (headBone      != null) headBone.localRotation      = headRestRot;
        if (neckBone      != null) neckBone.localRotation      = neckRestRot;
        if (upperBodyBone != null) upperBodyBone.localRotation = upperBodyRestRot;
        if (jawBone       != null) jawBone.localRotation       = jawRestRot;
        if (hipsBone      != null) hipsBone.localRotation      = hipsRestRot;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cache oase
    // ════════════════════════════════════════════════════════════════════════

    private void CacheAllBones()
    {
        var root = avatarRoot != null ? avatarRoot : transform;

        headBone        = null; jawBone         = null; neckBone        = null;
        upperBodyBone   = null; hipsBone        = null;
        leftLegBone     = null; rightLegBone    = null;
        leftArmBone     = null; rightArmBone    = null;
        leftForeArmBone = null; rightForeArmBone= null;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string raw = t.name;
            int sep = raw.LastIndexOf(':');
            string n = sep >= 0 ? raw.Substring(sep + 1) : raw;

            // Head — fără SkinnedMeshRenderer (evităm mesh-ul AvatarHead)
            if (headBone == null &&
                n.Equals("Head", StringComparison.Ordinal) &&
                t.GetComponent<SkinnedMeshRenderer>() == null)
            { headBone = t; headRestRot = t.localRotation; continue; }

            // AvatarTeethLower = maxilar
            if (jawBone == null &&
                n.Equals("AvatarTeethLower", StringComparison.OrdinalIgnoreCase))
            { jawBone = t; jawRestRot = t.localRotation; continue; }

            switch (n)
            {
                case "Neck":
                case "Neck1":
                    if (neckBone == null) { neckBone = t; neckRestRot = t.localRotation; } break;

                case "Spine2":
                    upperBodyBone = t; upperBodyRestRot = t.localRotation; break;
                case "Spine1":
                    if (upperBodyBone == null) { upperBodyBone = t; upperBodyRestRot = t.localRotation; } break;
                case "Spine":
                    if (upperBodyBone == null) { upperBodyBone = t; upperBodyRestRot = t.localRotation; } break;

                case "Hips":
                    hipsBone = t; hipsRestRot = t.localRotation; break;

                case "LeftUpLeg":  leftLegBone  = t; leftLegRestRot  = t.localRotation; break;
                case "RightUpLeg": rightLegBone = t; rightLegRestRot = t.localRotation; break;

                case "LeftArm":    leftArmBone  = t; leftArmRestRot  = t.localRotation; break;
                case "RightArm":   rightArmBone = t; rightArmRestRot = t.localRotation; break;

                case "LeftForeArm":
                    leftForeArmBone  = t; leftForeArmRestRot  = t.localRotation; break;
                case "RightForeArm":
                    rightForeArmBone = t; rightForeArmRestRot = t.localRotation; break;
            }
        }

        // Salvează poza de rest a root-ului dacă există
        if (avatarRoot != null)
        {
            rootRestPos = avatarRoot.localPosition;
            rootRestRot = avatarRoot.localRotation;
        }
    }
}
