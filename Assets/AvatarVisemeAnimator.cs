using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Animatii procedurale avatar 3D (fara blendshapes — modelul nu are morph targets).
/// Lip-sync: rotire subtila Head pe X. Animatii: mers, flotari, wave, brate sus,
/// inot, saritura, dans, rotatie cap. Control tastatura WASD + sageti + 1-8.
/// </summary>
public class AvatarVisemeAnimator : MonoBehaviour
{
    [Header("Viteza tranzitii")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 10f;

    // ── Compatibilitate (nu avem blendshapes) ────────────────────────────────
    public int ShapeCount => 0;

    // ── Timeline viseme ──────────────────────────────────────────────────────
    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private float speechDuration;
    private float speechStartedAt;

    // ── Oase ─────────────────────────────────────────────────────────────────
    private Transform headBone;        private Quaternion headRestRot;
    private Transform neckBone;        private Quaternion neckRestRot;
    private Transform upperBodyBone;   private Quaternion upperBodyRestRot;
    private Transform hipsBone;        private Quaternion hipsRestRot;
    private Transform avatarRoot;      private Vector3 rootRestPos;
                                       private Quaternion rootRestRot;
    private Transform leftLegBone;     private Quaternion leftLegRestRot;
    private Transform rightLegBone;    private Quaternion rightLegRestRot;
    private Transform leftArmBone;     private Quaternion leftArmRestRot;
    private Transform rightArmBone;    private Quaternion rightArmRestRot;
    private Transform leftForeArmBone; private Quaternion leftForeArmRestRot;
    private Transform rightForeArmBone;private Quaternion rightForeArmRestRot;

    // ── Stare animatii ───────────────────────────────────────────────────────
    private bool  walkActive;    private int   walkSteps;   private float walkStart;
    private bool  pushUpActive;  private int   pushUpReps;  private float pushUpStart;
    private bool  waveActive;                               private float waveStart;
    private bool  raiseActive;                              private float raiseStart;
    private bool  swimActive;    private float swimDur;     private float swimStart;
    private bool  jumpActive;    private int   jumpReps;    private float jumpStart;
    private bool  headRotActive; private float headRotAngle;private float headRotStart;
    private bool  danceActive;                              private float danceStart;

    // Lip-sync: unghi curent si tinta pentru Head
    private float jawCurrent;
    private float jawTarget;

    private string actionStatus = string.Empty;
    public  string ActionStatus => actionStatus;

    // ════════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        CacheAllBones(transform);
    }

    // ════════════════════════════════════════════════════════════════════════
    // API public
    // ════════════════════════════════════════════════════════════════════════

    public void SetAvatarRoot(Transform root)
    {
        avatarRoot   = root;
        rootRestPos  = root != null ? root.localPosition : Vector3.zero;
        rootRestRot  = root != null ? root.localRotation : Quaternion.identity;
        if (root != null) CacheAllBones(root);
    }

    public void RefreshBlendShapes()
    {
        // Modelul nu are blendshapes — doar re-cache-uim oasele
        if (avatarRoot != null) CacheAllBones(avatarRoot);
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
        var rh = VisemeTextMapper.CreateRhubarbTimeline(rhubarbJson, duration);
        timeline.AddRange(rh.Count > 0 ? rh : VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration  = duration;
        speechStartedAt = Time.time;
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

    public void RotateAvatar(float deg)
    {
        if (avatarRoot == null) return;
        avatarRoot.Rotate(Vector3.up, deg, Space.Self);
        rootRestRot = avatarRoot.localRotation;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Update
    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        HandleInput();

        float elapsed = Time.time - speechStartedAt;
        bool  speaking = elapsed < speechDuration && timeline.Count > 0;

        // ── Lip-sync: rotire Head pe X (simuleaza deschiderea gurii) ──────
        if (headBone != null && !headRotActive)
        {
            jawTarget = speaking ? VisemeToAngle(GetVisemeAt(elapsed)) : 0f;
            jawCurrent = Mathf.Lerp(jawCurrent, jawTarget, Time.deltaTime * blendSpeed * 2f);

            // Nod cap cand vorbeste
            float nod = speaking ? Mathf.Sin(elapsed * 2.5f) * 1.0f : 0f;
            headBone.localRotation = Quaternion.Slerp(
                headBone.localRotation,
                headRestRot * Quaternion.Euler(jawCurrent + nod, 0f, 0f),
                Time.deltaTime * blendSpeed * 2f);
        }

        // ── Animatii exclusive ────────────────────────────────────────────
        if (pushUpActive) { UpdatePushUps(); return; }
        if (swimActive)   { UpdateSwim();    return; }

        // Animatii cumulative (pot rula simultan)
        if (walkActive)    UpdateWalk();
        if (waveActive)    UpdateWave();
        if (raiseActive)   UpdateRaiseArms();
        if (jumpActive)    UpdateJump();
        if (headRotActive) UpdateHeadRot();
        if (danceActive)   UpdateDance();

        // ── Idle: brate coborite natural ──────────────────────────────────
        bool armsBusy = walkActive || waveActive || raiseActive || swimActive || danceActive;
        if (!armsBusy)
        {
            float breath = Mathf.Sin(Time.time * 0.8f) * 1.2f;
            SmoothBone(leftArmBone,  leftArmRestRot  * Quaternion.Euler(breath, 0f,  7f), 3f);
            SmoothBone(rightArmBone, rightArmRestRot * Quaternion.Euler(breath, 0f, -7f), 3f);
        }

        // ── Idle: respiratie corp ─────────────────────────────────────────
        if (upperBodyBone != null && !danceActive)
        {
            float b = Mathf.Sin(Time.time * 0.8f) * 0.4f;
            SmoothBone(upperBodyBone, upperBodyRestRot * Quaternion.Euler(b, 0f, 0f), 3f);
        }

        // ── Idle: bob vertical cand vorbeste ─────────────────────────────
        bool rootBusy = walkActive || pushUpActive || jumpActive || swimActive;
        if (avatarRoot != null && !rootBusy)
        {
            float bob = speaking ? Mathf.Sin(elapsed * 3f) * 0.004f : 0f;
            avatarRoot.localPosition = Vector3.Lerp(avatarRoot.localPosition,
                rootRestPos + Vector3.up * bob, Time.deltaTime * blendSpeed);
            // Revenim la rotatia de rest (evitam deriva)
            avatarRoot.localRotation = Quaternion.Slerp(avatarRoot.localRotation,
                rootRestRot, Time.deltaTime * 2f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Input tastatura
    // ════════════════════════════════════════════════════════════════════════

    private void HandleInput()
    {
        if (avatarRoot == null) return;

        // Sageti si WASD — deplasare/rotire
        // Folosim Keyboard din noul Input System
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        float h = 0f, v = 0f;
        if (kb.leftArrowKey.isPressed  || kb.aKey.isPressed) h = -1f;
        if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) h =  1f;
        if (kb.upArrowKey.isPressed    || kb.wKey.isPressed) v =  1f;
        if (kb.downArrowKey.isPressed  || kb.sKey.isPressed) v = -1f;

        if (h != 0f) RotateAvatar(h * 90f * Time.deltaTime);
        if (v != 0f)
        {
            avatarRoot.localPosition += Vector3.up * (v * 0.5f * Time.deltaTime);
            rootRestPos = avatarRoot.localPosition;
        }

        // Q/E — rotire cap manual
        if (headBone != null && !headRotActive)
        {
            if (kb.qKey.isPressed)
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f, -45f, 0f), Time.deltaTime * blendSpeed);
            else if (kb.eKey.isPressed)
                headBone.localRotation = Quaternion.Slerp(headBone.localRotation,
                    headRestRot * Quaternion.Euler(0f,  45f, 0f), Time.deltaTime * blendSpeed);
        }

        // Taste 1-8 — animatii (wasPressedThisFrame = KeyDown echivalent)
        if (kb.digit1Key.wasPressedThisFrame) StartWaving();
        if (kb.digit2Key.wasPressedThisFrame) StartWalking(5);
        if (kb.digit3Key.wasPressedThisFrame) StartPushUps(3);
        if (kb.digit4Key.wasPressedThisFrame) StartJumping(3);
        if (kb.digit5Key.wasPressedThisFrame) StartSwimming(4);
        if (kb.digit6Key.wasPressedThisFrame) StartDancing();
        if (kb.digit7Key.wasPressedThisFrame) StartRaisingArms();
        if (kb.digit8Key.wasPressedThisFrame) StartHeadRotation(45f, 60f);
        if (kb.spaceKey.wasPressedThisFrame)  StartJumping(1);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Viseme → unghi cap (lip-sync)
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
    // Animatii procedurale
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
            BoneX(leftArmBone,   leftArmRestRot,  -sw * 20f);
            BoneX(rightArmBone,  rightArmRestRot,  sw * 20f);
            if (avatarRoot != null)
                avatarRoot.localPosition = rootRestPos + Vector3.up * Mathf.Abs(sw) * 0.016f;
            actionStatus = $"Pasi: {Mathf.Min(walkSteps, Mathf.FloorToInt(e/SPP))}/{walkSteps}";
            return;
        }
        float r = Mathf.Clamp01((e-dur)/RET);
        BoneX(leftLegBone,  leftLegRestRot,  0f, r);
        BoneX(rightLegBone, rightLegRestRot, 0f, r);
        BoneX(leftArmBone,  leftArmRestRot,  0f, r);
        BoneX(rightArmBone, rightArmRestRot, 0f, r);
        if (r >= 1f) { walkActive = false; actionStatus = $"Gata: {walkSteps} pasi."; Restore(); }
    }

    private void UpdatePushUps()
    {
        const float TR = 0.65f, REP = 1.25f;
        float e = Time.time - pushUpStart, dur = pushUpReps * REP;
        Quaternion fr = rootRestRot * Quaternion.Euler(90f, 90f, 0f);
        Vector3    fp = rootRestPos + Vector3.up * 0.3f;
        if (e < TR)
        {
            float t = Mathf.SmoothStep(0f,1f,e/TR);
            avatarRoot.localRotation = Quaternion.Slerp(rootRestRot, fr, t);
            avatarRoot.localPosition = Vector3.Lerp(rootRestPos, fp, t); return;
        }
        if (e < TR+dur)
        {
            float et = e-TR, cy = (et%REP)/REP, lf = (1f+Mathf.Cos(cy*Mathf.PI*2f))*0.5f;
            avatarRoot.localRotation = fr;
            avatarRoot.localPosition = fp + Vector3.up * Mathf.Lerp(0.02f,0.16f,lf);
            actionStatus = $"Flotari: {Mathf.Min(pushUpReps,(int)(et/REP)+(cy>.8f?1:0))}/{pushUpReps}"; return;
        }
        float ret = Mathf.Clamp01((e-TR-dur)/TR);
        avatarRoot.localRotation = Quaternion.Slerp(fr, rootRestRot, Mathf.SmoothStep(0f,1f,ret));
        avatarRoot.localPosition = Vector3.Lerp(fp, rootRestPos, ret);
        if (ret >= 1f) { pushUpActive = false; actionStatus = $"Gata: {pushUpReps} flotari."; Restore(); }
    }

    private void UpdateWave()
    {
        const float DUR = 2.5f, RET = 0.5f;
        float e = Time.time - waveStart;
        if (e < DUR)
        {
            float lift = Mathf.SmoothStep(0f,1f,Mathf.Clamp01(e/0.35f))*75f;
            float wag  = Mathf.Sin(e*9f)*18f;
            BoneZ(rightArmBone, rightArmRestRot, -(lift+wag)); return;
        }
        float r = Mathf.Clamp01((e-DUR)/RET);
        BoneZ(rightArmBone, rightArmRestRot, 0f, r);
        if (r >= 1f) { waveActive = false; actionStatus = "Salut terminat."; Restore(); }
    }

    private void UpdateRaiseArms()
    {
        const float UP = 0.5f, HOLD = 1.0f, DN = 0.5f;
        float e = Time.time - raiseStart, total = UP+HOLD+DN;
        if (e < UP)
        {
            float t = Mathf.SmoothStep(0f,1f,e/UP);
            BoneZ(leftArmBone,  leftArmRestRot,   90f*t);
            BoneZ(rightArmBone, rightArmRestRot, -90f*t); return;
        }
        if (e < UP+HOLD)
        {
            BoneZ(leftArmBone,  leftArmRestRot,   90f);
            BoneZ(rightArmBone, rightArmRestRot, -90f); return;
        }
        if (e < total)
        {
            float t = Mathf.SmoothStep(0f,1f,(e-UP-HOLD)/DN);
            BoneZ(leftArmBone,  leftArmRestRot,   90f*(1f-t));
            BoneZ(rightArmBone, rightArmRestRot, -90f*(1f-t)); return;
        }
        BoneZ(leftArmBone,  leftArmRestRot, 0f);
        BoneZ(rightArmBone, rightArmRestRot, 0f);
        raiseActive = false; actionStatus = "Brate ridicate."; Restore();
    }

    private void UpdateSwim()
    {
        const float RET = 0.5f;
        float e = Time.time - swimStart;
        if (e < swimDur)
        {
            float lA = Mathf.Sin(e*Mathf.PI*2f)*85f;
            float rA = Mathf.Sin(e*Mathf.PI*2f+Mathf.PI)*85f;
            SmoothBone(leftArmBone,  leftArmRestRot  * Quaternion.Euler(lA,0f,0f), blendSpeed);
            SmoothBone(rightArmBone, rightArmRestRot * Quaternion.Euler(rA,0f,0f), blendSpeed);
            SmoothBone(upperBodyBone,upperBodyRestRot*Quaternion.Euler(0f,0f,Mathf.Sin(e*2f)*7f),5f);
            if (avatarRoot!=null) avatarRoot.localPosition=Vector3.Lerp(avatarRoot.localPosition,
                rootRestPos+Vector3.up*Mathf.Sin(e*4f)*0.025f, Time.deltaTime*blendSpeed);
            actionStatus=$"Inot... {Mathf.Max(0f,swimDur-e):F0}s"; return;
        }
        float r=Mathf.Clamp01((e-swimDur)/RET);
        SmoothBone(leftArmBone,  leftArmRestRot,  r);
        SmoothBone(rightArmBone, rightArmRestRot, r);
        SmoothBone(upperBodyBone,upperBodyRestRot,r);
        if (avatarRoot!=null) avatarRoot.localPosition=Vector3.Lerp(
            avatarRoot.localPosition, rootRestPos, Time.deltaTime*blendSpeed);
        if (r>=1f) { swimActive=false; actionStatus="Inot terminat."; Restore(); }
    }

    private void UpdateJump()
    {
        const float SPJ = 0.55f, RET = 0.3f;
        float e = Time.time - jumpStart, dur = jumpReps * SPJ;
        if (e < dur)
        {
            float cy = (e%SPJ)/SPJ;
            float h  = Mathf.Sin(cy*Mathf.PI)*0.28f;
            float kn = (1f-Mathf.Sin(cy*Mathf.PI))*12f;
            if (avatarRoot!=null) avatarRoot.localPosition=Vector3.Lerp(
                avatarRoot.localPosition, rootRestPos+Vector3.up*h, Time.deltaTime*blendSpeed*4f);
            BoneX(leftLegBone,  leftLegRestRot,  -kn);
            BoneX(rightLegBone, rightLegRestRot, -kn);
            actionStatus=$"Saritura {Mathf.Min(jumpReps,(int)(e/SPJ)+1)}/{jumpReps}"; return;
        }
        float r=Mathf.Clamp01((e-dur)/RET);
        if (avatarRoot!=null) avatarRoot.localPosition=Vector3.Lerp(
            avatarRoot.localPosition,rootRestPos,Time.deltaTime*blendSpeed);
        BoneX(leftLegBone,  leftLegRestRot,  0f, r);
        BoneX(rightLegBone, rightLegRestRot, 0f, r);
        if (r>=1f) { jumpActive=false; actionStatus=$"Gata: {jumpReps} sarituri."; Restore(); }
    }

    private void UpdateHeadRot()
    {
        const float DUR=4f, RET=0.4f;
        float e=Time.time-headRotStart;
        if (e<DUR)
        {
            float ang=Mathf.Sin(e*Mathf.PI*2f/2f)*headRotAngle;
            SmoothBone(headBone, headRestRot*Quaternion.Euler(0f,ang,0f), blendSpeed); return;
        }
        float r=Mathf.Clamp01((e-DUR)/RET);
        SmoothBone(headBone, headRestRot, r);
        if (r>=1f) { headRotActive=false; actionStatus="Rotire cap gata."; }
    }

    private void UpdateDance()
    {
        const float DUR=5f, RET=0.5f;
        float e=Time.time-danceStart;
        if (e<DUR)
        {
            float bob = Mathf.Sin(e*Mathf.PI*2f*1.2f)*0.04f;
            float swL =  Mathf.Sin(e*Mathf.PI*2f*1.2f)*38f;
            float swR = -swL;
            float bodyY= Mathf.Sin(e*1.2f*Mathf.PI*2f)*5f;
            float hipY = Mathf.Sin(e*2.4f*Mathf.PI*2f)*9f;
            if (avatarRoot!=null) avatarRoot.localPosition=Vector3.Lerp(
                avatarRoot.localPosition,rootRestPos+Vector3.up*bob,Time.deltaTime*blendSpeed);
            SmoothBone(leftArmBone,  leftArmRestRot *Quaternion.Euler(swL,0f,0f),blendSpeed);
            SmoothBone(rightArmBone, rightArmRestRot*Quaternion.Euler(swR,0f,0f),blendSpeed);
            SmoothBone(upperBodyBone,upperBodyRestRot*Quaternion.Euler(0f,bodyY,0f),5f);
            if (hipsBone!=null) SmoothBone(hipsBone,hipsRestRot*Quaternion.Euler(0f,hipY,0f),6f);
            actionStatus="Dans!"; return;
        }
        float r=Mathf.Clamp01((e-DUR)/RET);
        SmoothBone(leftArmBone, leftArmRestRot, r);
        SmoothBone(rightArmBone,rightArmRestRot,r);
        SmoothBone(upperBodyBone,upperBodyRestRot,r);
        if (hipsBone!=null) SmoothBone(hipsBone,hipsRestRot,r);
        if (avatarRoot!=null) avatarRoot.localPosition=Vector3.Lerp(
            avatarRoot.localPosition,rootRestPos,Time.deltaTime*blendSpeed);
        if (r>=1f) { danceActive=false; actionStatus="Dans terminat."; Restore(); }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Utilitare
    // ════════════════════════════════════════════════════════════════════════

    // Rotire pe axa X
    private void BoneX(Transform b, Quaternion rest, float ax, float r=0f)
    {
        if (b==null) return;
        b.localRotation=Quaternion.Slerp(b.localRotation,
            rest*Quaternion.Euler(ax,0f,0f), Mathf.Max(Time.deltaTime*blendSpeed,r));
    }

    // Rotire pe axa Z
    private void BoneZ(Transform b, Quaternion rest, float az, float r=0f)
    {
        if (b==null) return;
        b.localRotation=Quaternion.Slerp(b.localRotation,
            rest*Quaternion.Euler(0f,0f,az), Mathf.Max(Time.deltaTime*blendSpeed,r));
    }

    // Slerp spre rotatie target
    private void SmoothBone(Transform b, Quaternion target, float speed)
    {
        if (b==null) return;
        b.localRotation=Quaternion.Slerp(b.localRotation, target, Time.deltaTime*speed);
    }

    private void StopAll()
    {
        walkActive=pushUpActive=waveActive=raiseActive=false;
        swimActive=jumpActive=headRotActive=danceActive=false;
        Restore();
    }

    private void Restore()
    {
        if (avatarRoot!=null){ avatarRoot.localPosition=rootRestPos; avatarRoot.localRotation=rootRestRot; }
        if (leftLegBone!=null)   leftLegBone.localRotation=leftLegRestRot;
        if (rightLegBone!=null)  rightLegBone.localRotation=rightLegRestRot;
        if (leftArmBone!=null)   leftArmBone.localRotation=leftArmRestRot;
        if (rightArmBone!=null)  rightArmBone.localRotation=rightArmRestRot;
        if (leftForeArmBone!=null)  leftForeArmBone.localRotation=leftForeArmRestRot;
        if (rightForeArmBone!=null) rightForeArmBone.localRotation=rightForeArmRestRot;
        if (neckBone!=null)      neckBone.localRotation=neckRestRot;
        if (upperBodyBone!=null) upperBodyBone.localRotation=upperBodyRestRot;
        if (hipsBone!=null)      hipsBone.localRotation=hipsRestRot;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cache oase
    // ════════════════════════════════════════════════════════════════════════

    private void CacheAllBones(Transform searchRoot)
    {
        headBone=neckBone=upperBodyBone=hipsBone=null;
        leftLegBone=rightLegBone=leftArmBone=rightArmBone=null;
        leftForeArmBone=rightForeArmBone=null;

        foreach (var t in searchRoot.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int sep=n.LastIndexOf(':'); if(sep>=0) n=n.Substring(sep+1);

            // Head = osul fara SkinnedMeshRenderer (nu mesh-ul AvatarHead)
            if (headBone==null && n=="Head" && t.GetComponent<SkinnedMeshRenderer>()==null)
            { headBone=t; headRestRot=t.localRotation; continue; }

            switch (n)
            {
                case "Neck":  case "Neck1":
                    if (neckBone==null){neckBone=t; neckRestRot=t.localRotation;} break;
                case "Spine2":
                    upperBodyBone=t; upperBodyRestRot=t.localRotation; break;
                case "Spine1":
                    if(upperBodyBone==null){upperBodyBone=t;upperBodyRestRot=t.localRotation;} break;
                case "Spine":
                    if(upperBodyBone==null){upperBodyBone=t;upperBodyRestRot=t.localRotation;} break;
                case "Hips":
                    hipsBone=t; hipsRestRot=t.localRotation; break;
                case "LeftUpLeg":
                    leftLegBone=t; leftLegRestRot=t.localRotation; break;
                case "RightUpLeg":
                    rightLegBone=t; rightLegRestRot=t.localRotation; break;
                case "LeftArm":
                    leftArmBone=t; leftArmRestRot=t.localRotation; break;
                case "RightArm":
                    rightArmBone=t; rightArmRestRot=t.localRotation; break;
                case "LeftForeArm":
                    leftForeArmBone=t; leftForeArmRestRot=t.localRotation; break;
                case "RightForeArm":
                    rightForeArmBone=t; rightForeArmRestRot=t.localRotation; break;
            }
        }

        Debug.Log($"[Avatar] Oase: head={headBone?.name??"null"} neck={neckBone?.name??"null"} " +
                  $"spine={upperBodyBone?.name??"null"} hips={hipsBone?.name??"null"} " +
                  $"LA={leftArmBone?.name??"null"} RA={rightArmBone?.name??"null"} " +
                  $"LL={leftLegBone?.name??"null"} RL={rightLegBone?.name??"null"}");
    }
}
