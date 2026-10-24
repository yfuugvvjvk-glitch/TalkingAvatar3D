using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Avatar 3D animatie procedurala completa:
/// - Mers cu deplasare reala + gravitatie simulata (WASD/sageti)
/// - Flotari corecte: brate se indoaie, corp coboara/urca
/// - Saritura: genunchi, brate ridicate, aterizare
/// - Ochi urmaresc o tinta random (idle)
/// - Degete: pozitii predefinite (relaxat, pointing, pumn)
/// - Brate la pozitie naturala permanent (nu T-pose)
/// - Lip-sync: rotire Head pe X sincronizat cu visema
/// </summary>
public class AvatarVisemeAnimator : MonoBehaviour
{
    [Header("Viteza animatii")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 10f;
    [SerializeField] private float moveSpeed = 1.8f;     // m/s mers
    [SerializeField] private float gravity   = -9.8f;    // gravitatie

    // ── Compatibilitate blendshapes ──────────────────────────────────────────
    public int ShapeCount => 0;

    // ── Timeline viseme ──────────────────────────────────────────────────────
    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private float speechDuration;
    private float speechStartedAt;

    // ── Oase principale ──────────────────────────────────────────────────────
    private Transform headBone;          private Quaternion headRestRot;
    private Transform neckBone;          private Quaternion neckRestRot;
    private Transform upperBodyBone;     private Quaternion upperBodyRestRot;
    private Transform hipsBone;          private Quaternion hipsRestRot;
    private Transform avatarRoot;        private Vector3    rootRestPos;
                                         private Quaternion rootRestRot;
    private Transform leftLegBone;       private Quaternion leftLegRestRot;
    private Transform rightLegBone;      private Quaternion rightLegRestRot;
    private Transform leftFootBone;      private Quaternion leftFootRestRot;
    private Transform rightFootBone;     private Quaternion rightFootRestRot;
    private Transform leftArmBone;       private Quaternion leftArmRestRot;
    private Transform rightArmBone;      private Quaternion rightArmRestRot;
    private Transform leftForeArmBone;   private Quaternion leftForeArmRestRot;
    private Transform rightForeArmBone;  private Quaternion rightForeArmRestRot;
    private Transform leftHandBone;      private Quaternion leftHandRestRot;
    private Transform rightHandBone;     private Quaternion rightHandRestRot;

    // ── Ochi ─────────────────────────────────────────────────────────────────
    private Transform leftEyeBone;   private Quaternion leftEyeRestRot;
    private Transform rightEyeBone;  private Quaternion rightEyeRestRot;
    private float eyeBlinkTimer;
    private float eyeLookTimer;
    private Vector2 eyeLookTarget; // unghi Y, X pentru privire

    // ── Degete (sampling: doar Index1 si Middle1 per mana) ───────────────────
    private Transform leftIndex1;  private Quaternion leftIndex1Rest;
    private Transform leftMiddle1; private Quaternion leftMiddle1Rest;
    private Transform leftRing1;   private Quaternion leftRing1Rest;
    private Transform leftPinky1;  private Quaternion leftPinky1Rest;
    private Transform rightIndex1; private Quaternion rightIndex1Rest;
    private Transform rightMiddle1;private Quaternion rightMiddle1Rest;
    private Transform rightRing1;  private Quaternion rightRing1Rest;
    private Transform rightPinky1; private Quaternion rightPinky1Rest;

    // ── Stare animatii ───────────────────────────────────────────────────────
    private bool  walkActive;    private int   walkSteps;   private float walkStart;
    private bool  moveActive;    // mers liber cu tastatura
    private bool  pushUpActive;  private int   pushUpReps;  private float pushUpStart;
    private bool  waveActive;                               private float waveStart;
    private bool  raiseActive;                              private float raiseStart;
    private bool  swimActive;    private float swimDur;     private float swimStart;
    private bool  jumpActive;    private int   jumpReps;    private float jumpStart;
    private bool  headRotActive; private float headRotAngle;private float headRotStart;
    private bool  danceActive;                              private float danceStart;

    // Fizica mers liber
    private float verticalVelocity = 0f;
    private float groundY;
    private bool  isGrounded = true;
    private float walkPhase  = 0f;

    // Lip-sync
    private float jawCurrent;

    private string actionStatus = string.Empty;
    public  string ActionStatus => actionStatus;

    // ════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake() => CacheAllBones(transform);

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    public void SetAvatarRoot(Transform root)
    {
        avatarRoot  = root;
        rootRestPos = root != null ? root.localPosition : Vector3.zero;
        rootRestRot = root != null ? root.localRotation : Quaternion.identity;
        groundY     = rootRestPos.y;
        if (root != null) CacheAllBones(root);
    }

    public void RefreshBlendShapes()
    {
        if (avatarRoot != null) CacheAllBones(avatarRoot);
    }

    public void PlaySpeech(string text, float duration, string language)
    {
        timeline.Clear();
        timeline.AddRange(VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration = duration; speechStartedAt = Time.time;
    }

    public void PlaySpeech(string text, float duration, string language, string rhubarbJson)
    {
        timeline.Clear();
        var rh = VisemeTextMapper.CreateRhubarbTimeline(rhubarbJson, duration);
        timeline.AddRange(rh.Count > 0 ? rh : VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration = duration; speechStartedAt = Time.time;
    }

    // ── Animatii ─────────────────────────────────────────────────────────────

    public bool StartWalking(int steps)
    {
        if (avatarRoot == null) return false;
        StopAll(); walkSteps = Mathf.Clamp(steps,1,50); walkStart = Time.time; walkActive = true;
        actionStatus = $"Mers: {walkSteps} pasi..."; return true;
    }

    public bool StartPushUps(int reps)
    {
        if (avatarRoot == null) return false;
        StopAll(); pushUpReps = Mathf.Clamp(reps,1,20); pushUpStart = Time.time; pushUpActive = true;
        actionStatus = $"Flotari: {pushUpReps}..."; return true;
    }

    public bool StartWaving()
    {
        if (rightArmBone == null) return false;
        StopAll(); waveStart = Time.time; waveActive = true;
        actionStatus = "Salut!"; return true;
    }

    public bool StartRaisingArms()
    {
        if (leftArmBone == null || rightArmBone == null) return false;
        StopAll(); raiseStart = Time.time; raiseActive = true;
        actionStatus = "Ridica brate!"; return true;
    }

    public bool StartSwimming(int seconds)
    {
        if (leftArmBone == null) return false;
        StopAll(); swimDur = Mathf.Clamp(seconds,1,30); swimStart = Time.time; swimActive = true;
        actionStatus = $"Inot {seconds}s..."; return true;
    }

    public bool StartJumping(int reps)
    {
        if (avatarRoot == null) return false;
        StopAll(); jumpReps = Mathf.Clamp(reps,1,20); jumpStart = Time.time; jumpActive = true;
        actionStatus = $"Saritura x{jumpReps}..."; return true;
    }

    public bool StartHeadRotation(float angle, float speed)
    {
        if (headBone == null) return false;
        StopAll(); headRotAngle = angle; headRotStart = Time.time; headRotActive = true;
        actionStatus = "Rotire cap..."; return true;
    }

    public bool StartDancing()
    {
        if (avatarRoot == null) return false;
        StopAll(); danceStart = Time.time; danceActive = true;
        actionStatus = "Dans!"; return true;
    }

    // ── Blocare input cand userul scrie in chat ──────────────────────────────
    // Setat din TalkingAvatarBootstrap cand inputField e focusat
    public static bool InputBlocked = false;

    public void RotateAvatar(float deg)
    {
        if (avatarRoot == null) return;
        avatarRoot.Rotate(Vector3.up, deg, Space.Self);
        rootRestRot = avatarRoot.localRotation;
    }

    // Reseteaza pozitia avatarului la poza initiala (daca a zburat)
    public void ResetPosition()
    {
        if (avatarRoot == null) return;
        // Resetam local position la pozitia initiala
        avatarRoot.localPosition = rootRestPos;
        avatarRoot.localRotation = rootRestRot;
        // Resetam si parent-ul la origine daca s-a deplasat
        if (avatarRoot.parent != null)
        {
            avatarRoot.parent.localPosition = Vector3.zero;
            avatarRoot.parent.localRotation = Quaternion.identity;
        }
        verticalVelocity = 0f;
        isGrounded = true;
        actionStatus = "Pozitie resetata.";
    }

    // ════════════════════════════════════════════════════════════════════════
    // Update principal
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        HandleInput();

        float elapsed = Time.time - speechStartedAt;
        bool  speaking = elapsed < speechDuration && timeline.Count > 0;

        // ── Lip-sync: rotire Head pe X ────────────────────────────────────
        if (headBone != null && !headRotActive)
        {
            float jaw = speaking ? VisemeToAngle(GetVisemeAt(elapsed)) : 0f;
            jawCurrent = Mathf.Lerp(jawCurrent, jaw, Time.deltaTime * blendSpeed * 2.5f);
            float nod  = speaking ? Mathf.Sin(elapsed * 2.5f) * 1.0f : 0f;
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                headRestRot * Quaternion.Euler(jawCurrent + nod, 0f, 0f),
                Time.deltaTime * blendSpeed * 2f);
        }

        // ── Ochi: urmarire tinta idle ─────────────────────────────────────
        UpdateEyes(elapsed, speaking);

        // ── Animatii exclusive ────────────────────────────────────────────
        if (pushUpActive) { UpdatePushUps(); return; }
        if (swimActive)   { UpdateSwim();    return; }

        // ── Mers liber cu tastatura ───────────────────────────────────────
        // (se poate combina cu animatii de brate)
        ApplyGravityAndGround();

        // ── Animatii procedurale ──────────────────────────────────────────
        if (walkActive)    UpdateWalk();
        if (waveActive)    UpdateWave();
        if (raiseActive)   UpdateRaiseArms();
        if (jumpActive)    UpdateJump();
        if (headRotActive) UpdateHeadRot();
        if (danceActive)   UpdateDance();

        // ── Idle: brate naturale (nu T-pose) ─────────────────────────────
        bool armsBusy = walkActive || waveActive || raiseActive || swimActive
                     || danceActive || moveActive || jumpActive || pushUpActive;
        if (!armsBusy)
        {
            float breath = Mathf.Sin(Time.time * 0.8f) * 1.2f;
            // Brate usor in jos si spre corp — postura naturala
            SmoothBone(leftArmBone,  leftArmRestRot  * Quaternion.Euler(breath, 0f,  8f), 3f);
            SmoothBone(rightArmBone, rightArmRestRot * Quaternion.Euler(breath, 0f, -8f), 3f);
            // Antebrate usor indoite
            SmoothBone(leftForeArmBone,  leftForeArmRestRot  * Quaternion.Euler(0f, -5f, 0f), 3f);
            SmoothBone(rightForeArmBone, rightForeArmRestRot * Quaternion.Euler(0f,  5f, 0f), 3f);
        }

        // ── Degete relaxate ───────────────────────────────────────────────
        ApplyFingerPose(FingerPose.Relaxed);

        // ── Respiratie corp ───────────────────────────────────────────────
        if (upperBodyBone != null && !danceActive)
        {
            float b = Mathf.Sin(Time.time * 0.8f) * 0.4f;
            SmoothBone(upperBodyBone, upperBodyRestRot * Quaternion.Euler(b, 0f, 0f), 3f);
        }

        // ── Bob vertical ─────────────────────────────────────────────────
        bool rootBusy = walkActive || pushUpActive || jumpActive || swimActive || moveActive;
        if (avatarRoot != null && !rootBusy)
        {
            float bob = speaking ? Mathf.Sin(elapsed * 3f) * 0.004f : 0f;
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                rootRestPos + Vector3.up * bob, Time.deltaTime * blendSpeed);
            avatarRoot.localRotation = Quaternion.Slerp(avatarRoot.localRotation,
                rootRestRot, Time.deltaTime * 2f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Ochi
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateEyes(float elapsed, bool speaking)
    {
        eyeLookTimer -= Time.deltaTime;
        if (eyeLookTimer <= 0f)
        {
            // Schimbam directia privirii la intervale random 1-4 secunde
            eyeLookTarget = new Vector2(
                Random.Range(-12f, 12f),   // Y = stanga/dreapta
                Random.Range(-6f,   6f));  // X = sus/jos
            eyeLookTimer = Random.Range(1f, 4f);
        }

        // Cand vorbeste, priveste mai mult inainte
        Vector2 target = speaking
            ? Vector2.Lerp(eyeLookTarget, Vector2.zero, 0.6f)
            : eyeLookTarget;

        if (leftEyeBone  != null)
            leftEyeBone.localRotation  = Quaternion.Slerp(leftEyeBone.localRotation,
                leftEyeRestRot  * Quaternion.Euler(target.y, target.x, 0f), Time.deltaTime * 5f);
        if (rightEyeBone != null)
            rightEyeBone.localRotation = Quaternion.Slerp(rightEyeBone.localRotation,
                rightEyeRestRot * Quaternion.Euler(target.y, target.x, 0f), Time.deltaTime * 5f);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Degete
    // ════════════════════════════════════════════════════════════════════════

    private enum FingerPose { Relaxed, Fist, Pointing, Wave }

    private void ApplyFingerPose(FingerPose pose)
    {
        float curl = 0f;
        switch (pose)
        {
            case FingerPose.Relaxed:  curl =  8f; break;   // usor indoite
            case FingerPose.Fist:     curl = 80f; break;
            case FingerPose.Pointing: curl = 60f; break;   // toate indoite, index drept
            case FingerPose.Wave:     curl =  5f; break;
        }

        // Stanga
        CurlFinger(leftIndex1,  leftIndex1Rest,  pose == FingerPose.Pointing ? 0f : curl);
        CurlFinger(leftMiddle1, leftMiddle1Rest, curl);
        CurlFinger(leftRing1,   leftRing1Rest,   curl);
        CurlFinger(leftPinky1,  leftPinky1Rest,  curl);
        // Dreapta
        CurlFinger(rightIndex1,  rightIndex1Rest,  pose == FingerPose.Pointing ? 0f : curl);
        CurlFinger(rightMiddle1, rightMiddle1Rest, curl);
        CurlFinger(rightRing1,   rightRing1Rest,   curl);
        CurlFinger(rightPinky1,  rightPinky1Rest,  curl);
    }

    private void CurlFinger(Transform bone, Quaternion rest, float angle)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.Slerp(bone.localRotation,
            rest * Quaternion.Euler(angle, 0f, 0f), Time.deltaTime * 4f);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Input tastatura — mers liber cu deplasare + gravitatie
    // ════════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        if (avatarRoot == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // Blocat cand userul scrie in chat
        if (InputBlocked) return;

        // ── Rotire cu sagetile stanga/dreapta ─────────────────────────────
        if (kb.leftArrowKey.isPressed)
            avatarRoot.parent.Rotate(Vector3.up, -120f * Time.deltaTime, Space.World);
        if (kb.rightArrowKey.isPressed)
            avatarRoot.parent.Rotate(Vector3.up,  120f * Time.deltaTime, Space.World);

        // ── Mers inainte/inapoi cu sagetile sus/jos ───────────────────────
        float fwd = 0f;
        if (kb.upArrowKey.isPressed)   fwd =  1f;
        if (kb.downArrowKey.isPressed) fwd = -1f;

        if (fwd != 0f && !pushUpActive && !swimActive)
        {
            moveActive = true;
            // Miscare pe axa Z a parent-ului (sol plat)
            Vector3 move = avatarRoot.parent.forward * (fwd * moveSpeed * Time.deltaTime);
            avatarRoot.parent.position += move;

            // Animatie picioare + brate
            walkPhase += Time.deltaTime * Mathf.PI * 2f / 0.76f;
            float sw = Mathf.Sin(walkPhase);
            BoneX(leftLegBone,   leftLegRestRot,   sw * 28f);
            BoneX(rightLegBone,  rightLegRestRot, -sw * 28f);
            BoneX(leftArmBone,   leftArmRestRot,  -sw * 20f);
            BoneX(rightArmBone,  rightArmRestRot,  sw * 20f);
            // Bob vertical mic sincronizat cu pasul
            float bobY = rootRestPos.y + Mathf.Abs(sw) * 0.014f;
            Vector3 p = avatarRoot.localPosition;
            p.y = bobY;
            avatarRoot.localPosition = p;

            actionStatus = fwd > 0f ? "Mers inainte" : "Mers inapoi";
        }
        else
        {
            if (moveActive)
            {
                // Revenire picioare/brate la poza normala
                BoneX(leftLegBone,  leftLegRestRot,  0f, 0.08f);
                BoneX(rightLegBone, rightLegRestRot, 0f, 0.08f);
                BoneX(leftArmBone,  leftArmRestRot,  0f, 0.08f);
                BoneX(rightArmBone, rightArmRestRot, 0f, 0.08f);
                // Revenim la Y-ul de rest
                Vector3 p = avatarRoot.localPosition;
                p.y = Mathf.Lerp(p.y, rootRestPos.y, Time.deltaTime * 8f);
                avatarRoot.localPosition = p;
                if (Mathf.Abs(p.y - rootRestPos.y) < 0.001f) moveActive = false;
            }
            else
            {
                moveActive = false;
            }
        }

        // ── Salt Space ────────────────────────────────────────────────────
        if (kb.spaceKey.wasPressedThisFrame && isGrounded && !pushUpActive && !swimActive)
            TriggerJump();

        // ── Q/E — rotire cap manual ───────────────────────────────────────
        if (headBone != null && !headRotActive)
        {
            if (kb.qKey.isPressed)
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f, -45f, 0f), Time.deltaTime * blendSpeed);
            else if (kb.eKey.isPressed)
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f,  45f, 0f), Time.deltaTime * blendSpeed);
        }

        // ── Taste animatii (1-8) ──────────────────────────────────────────
        if (kb.digit1Key.wasPressedThisFrame) StartWaving();
        if (kb.digit2Key.wasPressedThisFrame) StartWalking(5);
        if (kb.digit3Key.wasPressedThisFrame) StartPushUps(3);
        if (kb.digit4Key.wasPressedThisFrame) StartJumping(3);
        if (kb.digit5Key.wasPressedThisFrame) StartSwimming(4);
        if (kb.digit6Key.wasPressedThisFrame) StartDancing();
        if (kb.digit7Key.wasPressedThisFrame) StartRaisingArms();
        if (kb.digit8Key.wasPressedThisFrame) StartHeadRotation(45f, 60f);
    }

    private void TriggerJump()
    {
        verticalVelocity = 4.5f;
        isGrounded = false;
        if (leftArmBone  != null) leftArmBone.localRotation  = leftArmRestRot  * Quaternion.Euler(0f, 0f, 50f);
        if (rightArmBone != null) rightArmBone.localRotation = rightArmRestRot * Quaternion.Euler(0f, 0f, -50f);
        if (leftLegBone  != null) leftLegBone.localRotation  = leftLegRestRot  * Quaternion.Euler(-25f, 0f, 0f);
        if (rightLegBone != null) rightLegBone.localRotation = rightLegRestRot * Quaternion.Euler(-25f, 0f, 0f);
        actionStatus = "Salt!";
    }

    private void ApplyGravityAndGround()
    {
        if (avatarRoot == null || isGrounded) return;

        verticalVelocity += gravity * Time.deltaTime;

        // Modificam doar Y-ul local al avatarRoot
        Vector3 pos = avatarRoot.localPosition;
        pos.y += verticalVelocity * Time.deltaTime;

        // Aterizare — nu trecem sub Y-ul de rest
        if (pos.y <= rootRestPos.y)
        {
            pos.y = rootRestPos.y;
            avatarRoot.localPosition = pos;
            verticalVelocity = 0f;
            isGrounded = true;
            // Revenire brate/picioare
            SmoothBone(leftArmBone,  leftArmRestRot  * Quaternion.Euler(0f, 0f,  8f), 8f);
            SmoothBone(rightArmBone, rightArmRestRot * Quaternion.Euler(0f, 0f, -8f), 8f);
            SmoothBone(leftLegBone,  leftLegRestRot,  8f);
            SmoothBone(rightLegBone, rightLegRestRot, 8f);
            actionStatus = "";
        }
        else
        {
            avatarRoot.localPosition = pos;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Viseme → unghi cap
    // ════════════════════════════════════════════════════════════════════════

    private static float VisemeToAngle(AvatarViseme v)
    {
        switch (v)
        {
            case AvatarViseme.OpenRound:
            case AvatarViseme.RhubarbA:
            case AvatarViseme.RhubarbD:  return 6f;
            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
            case AvatarViseme.RhubarbE:  return 4f;
            case AvatarViseme.Consonant:
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbRr:
            case AvatarViseme.RhubarbCh:
            case AvatarViseme.RhubarbTh:
            case AvatarViseme.RhubarbG:  return 2f;
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:  return 0f;
            default:                     return 1f;
        }
    }

    private AvatarViseme GetVisemeAt(float t)
    {
        for (int i = 1; i < timeline.Count; i++)
            if (t < timeline[i].time) return timeline[i-1].viseme;
        return timeline.Count > 0 ? timeline[timeline.Count-1].viseme : AvatarViseme.Rest;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Animatii procedurale — CORECTE cu forearm/mana
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateWalk()
    {
        const float SPP = 0.38f, RET = 0.35f;
        float e = Time.time - walkStart, dur = walkSteps * SPP;
        if (e < dur)
        {
            float sw = Mathf.Sin(e * Mathf.PI / SPP);
            BoneX(leftLegBone,   leftLegRestRot,   sw * 28f);
            BoneX(rightLegBone,  rightLegRestRot, -sw * 28f);
            BoneX(leftArmBone,   leftArmRestRot,  -sw * 22f);
            BoneX(rightArmBone,  rightArmRestRot,  sw * 22f);
            // Antebrate usor indoite natural la mers
            BoneX(leftForeArmBone,  leftForeArmRestRot,  sw * 10f);
            BoneX(rightForeArmBone, rightForeArmRestRot, -sw * 10f);
            if (avatarRoot != null)
                avatarRoot.localPosition = rootRestPos + Vector3.up * Mathf.Abs(sw) * 0.016f;
            actionStatus = $"Pasi: {Mathf.Min(walkSteps, Mathf.FloorToInt(e/SPP))}/{walkSteps}";
            return;
        }
        float r = Mathf.Clamp01((e-dur)/RET);
        BoneX(leftLegBone,   leftLegRestRot,  0f, r);
        BoneX(rightLegBone,  rightLegRestRot, 0f, r);
        BoneX(leftArmBone,   leftArmRestRot,  0f, r);
        BoneX(rightArmBone,  rightArmRestRot, 0f, r);
        if (r >= 1f) { walkActive = false; actionStatus = $"Gata: {walkSteps} pasi."; Restore(); }
    }

    private void UpdatePushUps()
    {
        const float TR = 0.65f, REP = 1.4f;
        float e = Time.time - pushUpStart, dur = pushUpReps * REP;
        Quaternion floorRot = rootRestRot * Quaternion.Euler(90f, 90f, 0f);
        Vector3    floorPos = rootRestPos + Vector3.up * 0.28f;

        if (e < TR)
        {
            // Tranzitie la pozitia orizontala
            float t = Mathf.SmoothStep(0f, 1f, e / TR);
            avatarRoot.localRotation = Quaternion.Slerp(rootRestRot, floorRot, t);
            avatarRoot.localPosition = Vector3.Lerp(rootRestPos, floorPos, t);
            // Brate se duc lateral (Z) cu cotul usor indoit
            if (leftArmBone  != null) leftArmBone.localRotation  =
                Quaternion.Slerp(leftArmBone.localRotation,
                leftArmRestRot  * Quaternion.Euler(0f, 0f, -t * 35f), t);
            if (rightArmBone != null) rightArmBone.localRotation =
                Quaternion.Slerp(rightArmBone.localRotation,
                rightArmRestRot * Quaternion.Euler(0f, 0f,  t * 35f), t);
            return;
        }

        if (e < TR + dur)
        {
            float et    = e - TR;
            float cy    = (et % REP) / REP;
            // 0 = sus (brate intinse), 1 = jos (brate indoite)
            float bendT = (1f - Mathf.Cos(cy * Mathf.PI * 2f)) * 0.5f;

            // Corp coboara/urca
            float bodyY = Mathf.Lerp(0.28f, 0.04f, bendT);
            avatarRoot.localRotation = floorRot;
            avatarRoot.localPosition = floorPos + Vector3.up * (bodyY - 0.28f);

            // UpperArm: lateral, usor in fata — pozitia clasica de flotare
            // Z = deschidere laterala (35-40 grade)
            float armZ = 35f;  // lateral constant
            // ForeArm: cotul se indoaie spre sol
            // La flotare cotul merge in spate si in jos — rotire pe X local
            float elbowBend = bendT * 80f;  // 0=intins, 80=indoit complet

            if (leftArmBone  != null) leftArmBone.localRotation  =
                leftArmRestRot  * Quaternion.Euler(0f, 0f, -armZ);
            if (rightArmBone != null) rightArmBone.localRotation =
                rightArmRestRot * Quaternion.Euler(0f, 0f,  armZ);
            if (leftForeArmBone  != null) leftForeArmBone.localRotation  =
                leftForeArmRestRot  * Quaternion.Euler(elbowBend, 0f, 0f);
            if (rightForeArmBone != null) rightForeArmBone.localRotation =
                rightForeArmRestRot * Quaternion.Euler(elbowBend, 0f, 0f);

            int done = Mathf.Min(pushUpReps, (int)(et / REP) + (cy > 0.8f ? 1 : 0));
            actionStatus = $"Flotari: {done}/{pushUpReps}";
            return;
        }

        // Revenire la pozitia verticala
        float ret = Mathf.Clamp01((e - TR - dur) / TR);
        float sm  = Mathf.SmoothStep(0f, 1f, ret);
        avatarRoot.localRotation = Quaternion.Slerp(floorRot, rootRestRot, sm);
        avatarRoot.localPosition = Vector3.Lerp(floorPos, rootRestPos, sm);
        float sp = Time.deltaTime * blendSpeed * 2f;
        if (leftArmBone      != null) leftArmBone.localRotation      = Quaternion.Slerp(leftArmBone.localRotation,      leftArmRestRot,      sp);
        if (rightArmBone     != null) rightArmBone.localRotation     = Quaternion.Slerp(rightArmBone.localRotation,     rightArmRestRot,     sp);
        if (leftForeArmBone  != null) leftForeArmBone.localRotation  = Quaternion.Slerp(leftForeArmBone.localRotation,  leftForeArmRestRot,  sp);
        if (rightForeArmBone != null) rightForeArmBone.localRotation = Quaternion.Slerp(rightForeArmBone.localRotation, rightForeArmRestRot, sp);
        if (ret >= 1f) { pushUpActive = false; actionStatus = $"Gata: {pushUpReps} flotari."; Restore(); }
    }

    private void UpdateWave()
    {
        const float DUR = 2.5f, RET = 0.5f;
        float e = Time.time - waveStart;
        if (e < DUR)
        {
            float lift = Mathf.SmoothStep(0f,1f,Mathf.Clamp01(e/0.35f)) * 70f;
            float wag  = Mathf.Sin(e * 9f) * 18f;
            BoneZ(rightArmBone, rightArmRestRot, -(lift + wag));
            // Antebrat usor indoit
            BoneX(rightForeArmBone, rightForeArmRestRot, 15f);
            ApplyFingerPose(FingerPose.Wave);
            return;
        }
        float r = Mathf.Clamp01((e-DUR)/RET);
        BoneZ(rightArmBone, rightArmRestRot, 0f, r);
        SmoothBone(rightForeArmBone, rightForeArmRestRot, r * blendSpeed);
        if (r >= 1f) { waveActive = false; actionStatus = "Salut terminat."; Restore(); }
    }

    private void UpdateRaiseArms()
    {
        const float UP = 0.5f, HOLD = 1.0f, DN = 0.5f;
        float e = Time.time - raiseStart, total = UP+HOLD+DN;
        if (e < UP)
        {
            float t = Mathf.SmoothStep(0f,1f,e/UP);
            BoneZ(leftArmBone,   leftArmRestRot,    90f * t);
            BoneZ(rightArmBone,  rightArmRestRot,  -90f * t);
            BoneX(leftForeArmBone,  leftForeArmRestRot,  -t * 10f);
            BoneX(rightForeArmBone, rightForeArmRestRot, -t * 10f);
            return;
        }
        if (e < UP+HOLD)
        {
            BoneZ(leftArmBone,  leftArmRestRot,   90f);
            BoneZ(rightArmBone, rightArmRestRot, -90f);
            return;
        }
        if (e < total)
        {
            float t = Mathf.SmoothStep(0f,1f,(e-UP-HOLD)/DN);
            BoneZ(leftArmBone,  leftArmRestRot,   90f*(1f-t));
            BoneZ(rightArmBone, rightArmRestRot, -90f*(1f-t));
            return;
        }
        raiseActive = false; actionStatus = "Brate ridicate."; Restore();
    }

    private void UpdateSwim()
    {
        const float RET = 0.5f;
        float e = Time.time - swimStart;
        if (e < swimDur)
        {
            float lA = Mathf.Sin(e * Mathf.PI * 2f)       * 85f;
            float rA = Mathf.Sin(e * Mathf.PI * 2f + Mathf.PI) * 85f;
            float lF = Mathf.Sin(e * Mathf.PI * 2f + 1f)  * 40f; // forearm
            float rF = Mathf.Sin(e * Mathf.PI * 2f + Mathf.PI + 1f) * 40f;
            SmoothBone(leftArmBone,       leftArmRestRot       * Quaternion.Euler(lA, 0f, 0f), blendSpeed);
            SmoothBone(rightArmBone,      rightArmRestRot      * Quaternion.Euler(rA, 0f, 0f), blendSpeed);
            SmoothBone(leftForeArmBone,   leftForeArmRestRot   * Quaternion.Euler(lF, 0f, 0f), blendSpeed);
            SmoothBone(rightForeArmBone,  rightForeArmRestRot  * Quaternion.Euler(rF, 0f, 0f), blendSpeed);
            SmoothBone(upperBodyBone, upperBodyRestRot * Quaternion.Euler(0f, 0f, Mathf.Sin(e*2f)*7f), 5f);
            if (avatarRoot != null) avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                rootRestPos + Vector3.up * Mathf.Sin(e*4f)*0.025f, Time.deltaTime*blendSpeed);
            actionStatus = $"Inot... {Mathf.Max(0f,swimDur-e):F0}s"; return;
        }
        float rec = Mathf.Clamp01((e-swimDur)/RET);
        SmoothBone(leftArmBone,      leftArmRestRot,      rec);
        SmoothBone(rightArmBone,     rightArmRestRot,     rec);
        SmoothBone(leftForeArmBone,  leftForeArmRestRot,  rec);
        SmoothBone(rightForeArmBone, rightForeArmRestRot, rec);
        SmoothBone(upperBodyBone, upperBodyRestRot, rec);
        if (avatarRoot != null) avatarRoot.localPosition = Vector3.Lerp(
            avatarRoot.localPosition, rootRestPos, Time.deltaTime*blendSpeed);
        if (rec >= 1f) { swimActive = false; actionStatus = "Inot terminat."; Restore(); }
    }

    private void UpdateJump()
    {
        const float SPJ = 0.55f, RET = 0.3f;
        float e = Time.time - jumpStart, dur = jumpReps * SPJ;
        if (e < dur)
        {
            float cy  = (e % SPJ) / SPJ;
            float lft = Mathf.Sin(cy * Mathf.PI); // 0→1→0 per ciclu

            // Corp se ridica
            if (avatarRoot != null)
            {
                Vector3 p = avatarRoot.localPosition;
                p.y = Mathf.Lerp(rootRestPos.y, rootRestPos.y + 0.3f * lft, 0.3f);
                avatarRoot.localPosition = p;
            }

            // Genunchi indoiti la decolare si aterizare
            float kn = (1f - lft) * 22f;
            if (leftLegBone  != null) leftLegBone.localRotation  =
                leftLegRestRot  * Quaternion.Euler(-kn, 0f, 0f);
            if (rightLegBone != null) rightLegBone.localRotation =
                rightLegRestRot * Quaternion.Euler(-kn, 0f, 0f);

            // Brate ridicate in aer — direct, vizibil
            float armZ = lft * 50f;
            if (leftArmBone  != null) leftArmBone.localRotation  =
                leftArmRestRot  * Quaternion.Euler(0f, 0f,  armZ);
            if (rightArmBone != null) rightArmBone.localRotation =
                rightArmRestRot * Quaternion.Euler(0f, 0f, -armZ);

            actionStatus = $"Saritura {Mathf.Min(jumpReps, (int)(e/SPJ)+1)}/{jumpReps}";
            return;
        }

        float r = Mathf.Clamp01((e - dur) / RET);
        // Revenire smooth
        if (avatarRoot != null)
        {
            Vector3 p = avatarRoot.localPosition;
            p.y = Mathf.Lerp(p.y, rootRestPos.y, Time.deltaTime * blendSpeed * 2f);
            avatarRoot.localPosition = p;
        }
        if (leftLegBone  != null) leftLegBone.localRotation  = Quaternion.Slerp(
            leftLegBone.localRotation,  leftLegRestRot,  Time.deltaTime * blendSpeed);
        if (rightLegBone != null) rightLegBone.localRotation = Quaternion.Slerp(
            rightLegBone.localRotation, rightLegRestRot, Time.deltaTime * blendSpeed);
        if (leftArmBone  != null) leftArmBone.localRotation  = Quaternion.Slerp(
            leftArmBone.localRotation,  leftArmRestRot,  Time.deltaTime * blendSpeed);
        if (rightArmBone != null) rightArmBone.localRotation = Quaternion.Slerp(
            rightArmBone.localRotation, rightArmRestRot, Time.deltaTime * blendSpeed);
        if (r >= 1f) { jumpActive = false; actionStatus = $"Gata: {jumpReps} sarituri."; Restore(); }
    }

    private void UpdateHeadRot()
    {
        const float DUR = 4f, RET = 0.4f;
        float e = Time.time - headRotStart;
        if (e < DUR)
        {
            float ang = Mathf.Sin(e * Mathf.PI * 2f / 2f) * headRotAngle;
            SmoothBone(headBone, headRestRot * Quaternion.Euler(0f, ang, 0f), blendSpeed); return;
        }
        float r = Mathf.Clamp01((e-DUR)/RET);
        SmoothBone(headBone, headRestRot, r);
        if (r >= 1f) { headRotActive = false; actionStatus = "Rotire cap gata."; }
    }

    private void UpdateDance()
    {
        const float DUR = 5f, RET = 0.5f;
        float e = Time.time - danceStart;
        if (e < DUR)
        {
            float bob  = Mathf.Sin(e * Mathf.PI * 2.4f) * 0.04f;
            float swL  =  Mathf.Sin(e * Mathf.PI * 2.4f) * 38f;
            float swR  = -swL;
            float fAL  =  Mathf.Sin(e * Mathf.PI * 2.4f + 0.5f) * 20f;
            float fAR  = -fAL;
            if (avatarRoot != null) avatarRoot.localPosition = Vector3.Lerp(
                avatarRoot.localPosition, rootRestPos + Vector3.up * bob, Time.deltaTime*blendSpeed);
            SmoothBone(leftArmBone,      leftArmRestRot      * Quaternion.Euler(swL, 0f, 0f), blendSpeed);
            SmoothBone(rightArmBone,     rightArmRestRot     * Quaternion.Euler(swR, 0f, 0f), blendSpeed);
            SmoothBone(leftForeArmBone,  leftForeArmRestRot  * Quaternion.Euler(fAL, 0f, 0f), blendSpeed);
            SmoothBone(rightForeArmBone, rightForeArmRestRot * Quaternion.Euler(fAR, 0f, 0f), blendSpeed);
            SmoothBone(upperBodyBone, upperBodyRestRot * Quaternion.Euler(0f, Mathf.Sin(e*2.4f*Mathf.PI*2f)*5f, 0f), 5f);
            if (hipsBone != null) SmoothBone(hipsBone, hipsRestRot * Quaternion.Euler(0f, Mathf.Sin(e*4.8f*Mathf.PI*2f)*9f, 0f), 6f);
            actionStatus = "Dans!"; return;
        }
        float rec = Mathf.Clamp01((e-DUR)/RET);
        SmoothBone(leftArmBone,      leftArmRestRot,      rec);
        SmoothBone(rightArmBone,     rightArmRestRot,     rec);
        SmoothBone(leftForeArmBone,  leftForeArmRestRot,  rec);
        SmoothBone(rightForeArmBone, rightForeArmRestRot, rec);
        SmoothBone(upperBodyBone, upperBodyRestRot, rec);
        if (hipsBone != null) SmoothBone(hipsBone, hipsRestRot, rec);
        if (avatarRoot != null) avatarRoot.localPosition = Vector3.Lerp(
            avatarRoot.localPosition, rootRestPos, Time.deltaTime*blendSpeed);
        if (rec >= 1f) { danceActive = false; actionStatus = "Dans terminat."; Restore(); }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Utilitare rotire oase
    // ════════════════════════════════════════════════════════════════════════

    private void BoneX(Transform b, Quaternion rest, float ax, float r = 0f)
    {
        if (b == null) return;
        b.localRotation = Quaternion.Slerp(b.localRotation,
            rest * Quaternion.Euler(ax, 0f, 0f), Mathf.Max(Time.deltaTime * blendSpeed, r));
    }

    private void BoneZ(Transform b, Quaternion rest, float az, float r = 0f)
    {
        if (b == null) return;
        b.localRotation = Quaternion.Slerp(b.localRotation,
            rest * Quaternion.Euler(0f, 0f, az), Mathf.Max(Time.deltaTime * blendSpeed, r));
    }

    private void SmoothBone(Transform b, Quaternion target, float speed)
    {
        if (b == null) return;
        b.localRotation = Quaternion.Slerp(b.localRotation, target, Time.deltaTime * speed);
    }

    private void StopAll()
    {
        walkActive = pushUpActive = waveActive = raiseActive = false;
        swimActive = jumpActive   = headRotActive = danceActive = false;
        moveActive = false;
        Restore();
    }

    private void Restore()
    {
        if (avatarRoot != null) { avatarRoot.localPosition = rootRestPos; avatarRoot.localRotation = rootRestRot; }
        if (leftLegBone   != null) leftLegBone.localRotation   = leftLegRestRot;
        if (rightLegBone  != null) rightLegBone.localRotation  = rightLegRestRot;
        if (leftArmBone   != null) leftArmBone.localRotation   = leftArmRestRot;
        if (rightArmBone  != null) rightArmBone.localRotation  = rightArmRestRot;
        if (leftForeArmBone  != null) leftForeArmBone.localRotation  = leftForeArmRestRot;
        if (rightForeArmBone != null) rightForeArmBone.localRotation = rightForeArmRestRot;
        if (headBone      != null) headBone.localRotation      = headRestRot;
        if (neckBone      != null) neckBone.localRotation      = neckRestRot;
        if (upperBodyBone != null) upperBodyBone.localRotation = upperBodyRestRot;
        if (hipsBone      != null) hipsBone.localRotation      = hipsRestRot;
        isGrounded = true; verticalVelocity = 0f;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cache oase — include ochi si degete
    // ════════════════════════════════════════════════════════════════════════

    private void CacheAllBones(Transform searchRoot)
    {
        headBone = neckBone = upperBodyBone = hipsBone = null;
        leftLegBone = rightLegBone = leftArmBone = rightArmBone = null;
        leftForeArmBone = rightForeArmBone = leftHandBone = rightHandBone = null;
        leftEyeBone = rightEyeBone = null;
        leftFootBone = rightFootBone = null;
        leftIndex1 = leftMiddle1 = leftRing1 = leftPinky1 = null;
        rightIndex1 = rightMiddle1 = rightRing1 = rightPinky1 = null;

        foreach (var t in searchRoot.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int sep = n.LastIndexOf(':'); if (sep >= 0) n = n.Substring(sep + 1);

            // Head — fara SkinnedMeshRenderer
            if (headBone == null && n == "Head" && t.GetComponent<SkinnedMeshRenderer>() == null)
            { headBone = t; headRestRot = t.localRotation; continue; }

            switch (n)
            {
                case "Neck":  case "Neck1":
                    if (neckBone == null) { neckBone = t; neckRestRot = t.localRotation; } break;
                case "Spine2":
                    upperBodyBone = t; upperBodyRestRot = t.localRotation; break;
                case "Spine1":
                    if (upperBodyBone == null) { upperBodyBone = t; upperBodyRestRot = t.localRotation; } break;
                case "Spine":
                    if (upperBodyBone == null) { upperBodyBone = t; upperBodyRestRot = t.localRotation; } break;
                case "Hips":
                    hipsBone = t; hipsRestRot = t.localRotation; break;
                case "LeftUpLeg":
                    leftLegBone = t; leftLegRestRot = t.localRotation; break;
                case "RightUpLeg":
                    rightLegBone = t; rightLegRestRot = t.localRotation; break;
                case "LeftFoot":
                    leftFootBone = t; leftFootRestRot = t.localRotation; break;
                case "RightFoot":
                    rightFootBone = t; rightFootRestRot = t.localRotation; break;
                case "LeftArm":
                    leftArmBone = t; leftArmRestRot = t.localRotation; break;
                case "RightArm":
                    rightArmBone = t; rightArmRestRot = t.localRotation; break;
                case "LeftForeArm":
                    leftForeArmBone = t; leftForeArmRestRot = t.localRotation; break;
                case "RightForeArm":
                    rightForeArmBone = t; rightForeArmRestRot = t.localRotation; break;
                case "LeftHand":
                    leftHandBone = t; leftHandRestRot = t.localRotation; break;
                case "RightHand":
                    rightHandBone = t; rightHandRestRot = t.localRotation; break;
                // Ochi
                case "LeftEye":
                    leftEyeBone  = t; leftEyeRestRot  = t.localRotation; break;
                case "RightEye":
                    rightEyeBone = t; rightEyeRestRot = t.localRotation; break;
                // Degete stanga
                case "LeftHandIndex1":  leftIndex1  = t; leftIndex1Rest  = t.localRotation; break;
                case "LeftHandMiddle1": leftMiddle1 = t; leftMiddle1Rest = t.localRotation; break;
                case "LeftHandRing1":   leftRing1   = t; leftRing1Rest   = t.localRotation; break;
                case "LeftHandPinky1":  leftPinky1  = t; leftPinky1Rest  = t.localRotation; break;
                // Degete dreapta
                case "RightHandIndex1":  rightIndex1  = t; rightIndex1Rest  = t.localRotation; break;
                case "RightHandMiddle1": rightMiddle1 = t; rightMiddle1Rest = t.localRotation; break;
                case "RightHandRing1":   rightRing1   = t; rightRing1Rest   = t.localRotation; break;
                case "RightHandPinky1":  rightPinky1  = t; rightPinky1Rest  = t.localRotation; break;
            }
        }

        Debug.Log($"[Avatar] head={headBone?.name??"null"} eyes=L:{leftEyeBone?.name??"null"}/R:{rightEyeBone?.name??"null"} " +
                  $"fingers=L:{leftIndex1?.name??"null"}/R:{rightIndex1?.name??"null"} " +
                  $"forearm=L:{leftForeArmBone?.name??"null"}/R:{rightForeArmBone?.name??"null"}");
    }
}
