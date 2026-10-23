using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlează animația facială (blendshapes) și mișcările corpului avatarului 3D.
/// Aplică viseme interpolate (Lerp) sincronizate cu vorbirea, plus animații procedurale
/// pentru mers pe loc și flotări.
/// </summary>
public class AvatarVisemeAnimator : MonoBehaviour
{
    [Header("Configurare mesh")]
    [Tooltip("Renderer-ul mesh-ului feței. Dacă e gol, scriptul îl detectează automat.")]
    [SerializeField] private SkinnedMeshRenderer faceRenderer;

    [Header("Parametri animație")]
    [Tooltip("Viteza de tranziție Lerp între stări faciale (mai mare = mai rapid).")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 12f;

    // ─── Date interne pentru blendshapes ────────────────────────────────────
    private struct ShapeBinding
    {
        public SkinnedMeshRenderer renderer;
        public int index;
    }

    // Mapare: nume normalizat -> lista de binding-uri (renderer + index blendshape)
    private readonly Dictionary<string, List<ShapeBinding>> shapeBindings =
        new Dictionary<string, List<ShapeBinding>>();

    // Valorile curente interpolate pentru fiecare shape activ
    private readonly Dictionary<string, float> currentShapeValues =
        new Dictionary<string, float>();

    // Timeline-ul visemelor pentru fraza curentă
    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();

    private bool warnedAboutInvalidRenderer;
    private float speechDuration;
    private float speechStartedAt;

    // ─── Oase cap și corp superior ──────────────────────────────────────────
    private Transform headBone;
    private Quaternion headRestRotation;
    private Transform upperBodyBone;
    private Quaternion upperBodyRestRotation;

    // ─── Rădăcina avatarului ─────────────────────────────────────────────────
    private Transform avatarRoot;
    private Vector3 avatarRootRestPosition;
    private Quaternion avatarRootRestRotation;

    // ─── Animație flotări ────────────────────────────────────────────────────
    private int pushUpRepetitions;
    private float pushUpStartedAt;
    private bool pushUpActive;

    // ─── Animație mers ──────────────────────────────────────────────────────
    private int walkSteps;
    private float walkStartedAt;
    private bool walkActive;
    private Transform leftLegBone;
    private Transform rightLegBone;
    private Transform leftArmBone;
    private Transform rightArmBone;
    private Quaternion leftLegRestRotation;
    private Quaternion rightLegRestRotation;
    private Quaternion leftArmRestRotation;
    private Quaternion rightArmRestRotation;

    private string actionStatus = string.Empty;
    public string ActionStatus => actionStatus;

    // ────────────────────────────────────────────────────────────────────────
    // Vizualizare shapes active (pentru debug în Editor)
    // ────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [Header("Debug (Editor only)")]
    [SerializeField] private bool logBlendShapeNames;
#endif

    // ────────────────────────────────────────────────────────────────────────
    // Lifecycle Unity
    // ────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        CacheHeadBone();
        CacheUpperBodyBone();
        CacheWalkBones();
        RefreshBlendShapes();
    }

    // ────────────────────────────────────────────────────────────────────────
    // API public
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Rescane toate blendshape-urile din ierarhie și le reindexează.</summary>
    public void RefreshBlendShapes()
    {
        shapeBindings.Clear();
        currentShapeValues.Clear();
        CacheBlendShapes();
        if (headBone == null)
            CacheHeadBone();
        if (upperBodyBone == null)
            CacheUpperBodyBone();
        CacheWalkBones();
        ResetFaceBlendShapes();
    }

    /// <summary>Pornește animația buzelor folosind estimarea fonematică din text.</summary>
    public void PlaySpeech(string text, float duration, string language)
    {
        timeline.Clear();
        timeline.AddRange(VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration = duration;
        speechStartedAt = Time.time;
    }

    /// <summary>Pornește animația buzelor cu date Rhubarb, sau fallback la text dacă datele lipsesc.</summary>
    public void PlaySpeech(string text, float duration, string language, string rhubarbJson)
    {
        timeline.Clear();
        List<VisemeFrame> rhubarb = VisemeTextMapper.CreateRhubarbTimeline(rhubarbJson, duration);
        if (rhubarb.Count > 0)
            timeline.AddRange(rhubarb);
        else
            timeline.AddRange(VisemeTextMapper.CreateTimeline(text, duration, language));

        speechDuration = duration;
        speechStartedAt = Time.time;
    }

    /// <summary>Setează rădăcina avatarului după ce modelul GLB a fost instanțiat.</summary>
    public void SetAvatarRoot(Transform root)
    {
        avatarRoot = root;
        if (avatarRoot == null)
            return;

        avatarRootRestPosition = avatarRoot.localPosition;
        avatarRootRestRotation = avatarRoot.localRotation;
    }

    /// <summary>Pornește animația procedurală de flotări.</summary>
    public bool StartPushUps(int repetitions)
    {
        if (avatarRoot == null)
        {
            Debug.LogError("Nu pot porni animația de flotări: avatarul nu este încărcat.", this);
            return false;
        }

        RestoreMovementPose();
        pushUpRepetitions = Mathf.Clamp(repetitions, 1, 20);
        walkActive = false;
        pushUpStartedAt = Time.time;
        pushUpActive = true;
        actionStatus = $"Avatar: execută {pushUpRepetitions} flotări...";
        return true;
    }

    /// <summary>Pornește animația procedurală de mers pe loc.</summary>
    public bool StartWalking(int steps)
    {
        if (avatarRoot == null || leftLegBone == null || rightLegBone == null)
        {
            Debug.LogError("Nu pot porni mersul: avatarul sau oasele picioarelor nu sunt disponibile.", this);
            return false;
        }

        RestoreMovementPose();
        walkSteps = Mathf.Clamp(steps, 1, 50);
        pushUpActive = false;
        walkStartedAt = Time.time;
        walkActive = true;
        actionStatus = $"Avatar: începe mersul ({walkSteps} pași)...";
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Update principal
    // ────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        float elapsed = Time.time - speechStartedAt;
        bool speaking = elapsed < speechDuration && timeline.Count > 0;

        // ── Animație facială ──
        if (shapeBindings.Count > 0)
        {
            AvatarViseme current = speaking ? GetVisemeAt(elapsed) : AvatarViseme.Rest;
            ApplyViseme(current);
        }

        // ── Animații corp ──
        if (pushUpActive)
        {
            UpdatePushUps();
            return; // nu mai aplica cap/corp în flotări
        }

        if (walkActive)
            UpdateWalk();

        // ── Mișcare cap subtilă (respirație + vorbire) ──
        if (headBone != null)
        {
            float motion = speaking ? Mathf.Sin(elapsed * 4.2f) : 0f;
            Quaternion targetRotation = headRestRotation *
                Quaternion.Euler(motion * 2.5f,
                                 Mathf.Sin(elapsed * 2.1f) * (speaking ? 1.5f : 0f),
                                 motion * 1.5f);
            headBone.localRotation = Quaternion.Slerp(
                headBone.localRotation, targetRotation, Time.deltaTime * blendSpeed);
        }

        // ── Mișcare corp superior subtilă (respirație) ──
        if (upperBodyBone != null)
        {
            float breath = speaking ? Mathf.Sin(elapsed * 2.4f) : 0f;
            Quaternion targetRotation = upperBodyRestRotation *
                Quaternion.Euler(breath * 1.5f, 0f, breath * 1.0f);
            upperBodyBone.localRotation = Quaternion.Slerp(
                upperBodyBone.localRotation, targetRotation, Time.deltaTime * blendSpeed);
        }

        // ── Bob vertical ușor al corpului ──
        if (avatarRoot != null && !walkActive)
        {
            float bob = speaking ? Mathf.Sin(elapsed * 3.2f) * 0.018f : 0f;
            Vector3 targetPosition = avatarRootRestPosition + Vector3.up * bob;
            avatarRoot.localPosition = Vector3.Lerp(
                avatarRoot.localPosition, targetPosition, Time.deltaTime * blendSpeed);
            avatarRoot.localRotation = Quaternion.Slerp(
                avatarRoot.localRotation, avatarRootRestRotation, Time.deltaTime * blendSpeed);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Animație mers pe loc
    // ────────────────────────────────────────────────────────────────────────

    private void UpdateWalk()
    {
        const float secondsPerStep = 0.36f;
        const float returnDuration = 0.30f;
        float elapsed = Time.time - walkStartedAt;
        float walkDuration = walkSteps * secondsPerStep;

        if (elapsed < walkDuration)
        {
            float phase = elapsed * Mathf.PI * 2f / (secondsPerStep * 2f);
            float legSwing = Mathf.Sin(phase);
            SetBoneRotation(leftLegBone, leftLegRestRotation, legSwing * 22f);
            SetBoneRotation(rightLegBone, rightLegRestRotation, -legSwing * 22f);
            SetBoneRotation(leftArmBone, leftArmRestRotation, -legSwing * 15f);
            SetBoneRotation(rightArmBone, rightArmRestRotation, legSwing * 15f);

            if (avatarRoot != null)
                avatarRoot.localPosition = avatarRootRestPosition + Vector3.up * (Mathf.Abs(legSwing) * 0.022f);

            int completed = Mathf.Min(walkSteps, Mathf.FloorToInt(elapsed / secondsPerStep));
            actionStatus = $"Avatar: pași {completed}/{walkSteps}";
            return;
        }

        float recovery = Mathf.Clamp01((elapsed - walkDuration) / returnDuration);
        SetBoneRotation(leftLegBone, leftLegRestRotation, 0f, recovery);
        SetBoneRotation(rightLegBone, rightLegRestRotation, 0f, recovery);
        SetBoneRotation(leftArmBone, leftArmRestRotation, 0f, recovery);
        SetBoneRotation(rightArmBone, rightArmRestRotation, 0f, recovery);

        if (avatarRoot != null)
            avatarRoot.localPosition = Vector3.Lerp(
                avatarRoot.localPosition, avatarRootRestPosition, Time.deltaTime * blendSpeed);

        if (recovery >= 1f)
        {
            walkActive = false;
            actionStatus = $"Avatar: a terminat {walkSteps} pași.";
        }
    }

    private void SetBoneRotation(Transform bone, Quaternion rest, float angle, float recovery = 0f)
    {
        if (bone == null)
            return;

        Quaternion target = rest * Quaternion.Euler(angle, 0f, 0f);
        float t = Mathf.Max(Time.deltaTime * blendSpeed, recovery);
        bone.localRotation = Quaternion.Slerp(bone.localRotation, target, t);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Animație flotări
    // ────────────────────────────────────────────────────────────────────────

    private void UpdatePushUps()
    {
        const float transitionDuration = 0.65f;
        const float repetitionDuration = 1.25f;
        float elapsed = Time.time - pushUpStartedAt;
        float exerciseDuration = pushUpRepetitions * repetitionDuration;
        Quaternion floorRotation = avatarRootRestRotation * Quaternion.Euler(90f, 90f, 0f);
        Vector3 floorPosition = avatarRootRestPosition + Vector3.up * 0.3f;

        if (elapsed < transitionDuration)
        {
            float blend = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
            avatarRoot.localRotation = Quaternion.Slerp(avatarRootRestRotation, floorRotation, blend);
            avatarRoot.localPosition = Vector3.Lerp(avatarRootRestPosition, floorPosition, blend);
            return;
        }

        if (elapsed < transitionDuration + exerciseDuration)
        {
            float exerciseTime = elapsed - transitionDuration;
            float cycle = (exerciseTime % repetitionDuration) / repetitionDuration;
            float lift = (1f + Mathf.Cos(cycle * Mathf.PI * 2f)) * 0.5f;
            avatarRoot.localRotation = floorRotation;
            avatarRoot.localPosition = floorPosition + Vector3.up * Mathf.Lerp(0.02f, 0.16f, lift);

            int completed = Mathf.Min(
                pushUpRepetitions,
                Mathf.FloorToInt(exerciseTime / repetitionDuration) + (cycle > 0.8f ? 1 : 0));
            actionStatus = $"Avatar: flotări {completed}/{pushUpRepetitions}";
            return;
        }

        float returnProgress = Mathf.Clamp01(
            (elapsed - transitionDuration - exerciseDuration) / transitionDuration);
        float returnBlend = Mathf.SmoothStep(0f, 1f, returnProgress);
        avatarRoot.localRotation = Quaternion.Slerp(floorRotation, avatarRootRestRotation, returnBlend);
        avatarRoot.localPosition = Vector3.Lerp(floorPosition, avatarRootRestPosition, returnBlend);

        if (returnProgress >= 1f)
        {
            pushUpActive = false;
            actionStatus = $"Avatar: a terminat {pushUpRepetitions} flotări.";
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Viseme lookup în timeline
    // ────────────────────────────────────────────────────────────────────────

    private AvatarViseme GetVisemeAt(float elapsed)
    {
        for (int i = 1; i < timeline.Count; i++)
        {
            if (elapsed < timeline[i].time)
                return timeline[i - 1].viseme;
        }

        return timeline.Count > 0 ? timeline[timeline.Count - 1].viseme : AvatarViseme.Rest;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Aplicare visemă — MATRICE COMPLETĂ cu toate shape-urile din specificație
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Traduce o visemă în ponderi de blendshape și le aplică via Lerp.
    /// Shape-urile inactive sunt aduse explicit la 0 pentru a evita deformările.
    /// </summary>
    private void ApplyViseme(AvatarViseme viseme)
    {
        // ── Definim valorile țintă pentru fiecare shape relevant ──────────
        // Blendshape-urile sunt normalizate la scala 0-100 (standard Unity).
        // Dacă avatarul tău folosește 0-1, înmulțeste valorile cu 0.01f.

        float jawOpen      = 0f;
        float mouthFunnel  = 0f;   // buze rotunjite (O, U)
        float mouthSmile   = 0f;   // colțuri ridicate (E, I, AE)
        float mouthClose   = 0f;   // buze complet închise (B, M, P)
        float mouthShrug   = 0f;   // buza inferioară coborâtă (F, V)
        float mouthPucker  = 0f;   // buze strânse în față (W, OO)
        float mouthStretch = 0f;   // dinți vizibili (L, D, T, N, S)

        switch (viseme)
        {
            // ── Vocale deschise rotunde: A, AH, O, UH, Ă, Â, Î ──────────
            case AvatarViseme.OpenRound:
                jawOpen     = 28f;
                mouthFunnel = 18f;
                break;

            // ── Vocale medii laterale: E, EH, AE, I ──────────────────────
            case AvatarViseme.MidSpread:
                jawOpen    = 16f;
                mouthSmile = 20f;
                break;

            // ── Ocluzive bilabiale: B, M, P ──────────────────────────────
            case AvatarViseme.Closed:
                mouthClose = 55f;
                jawOpen    =  0f;
                break;

            // ── Labiodentale: F, V ────────────────────────────────────────
            case AvatarViseme.TeethOnLip:
                mouthShrug = 30f;
                jawOpen    =  5f;
                break;

            // ── Labiale rotunjite: W, OO ──────────────────────────────────
            case AvatarViseme.Pucker:
                mouthPucker = 40f;
                jawOpen     =  8f;
                break;

            // ── Consoane alveolare/dentale: L, D, T, N, S, Ș, Z ─────────
            case AvatarViseme.Consonant:
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbTh:
                jawOpen      =  8f;
                mouthStretch = 14f;
                break;

            // ── CH, SH (affricate) ────────────────────────────────────────
            case AvatarViseme.RhubarbCh:
                jawOpen      = 10f;
                mouthPucker  = 12f;
                mouthStretch =  8f;
                break;

            // ── R (trilateral) ────────────────────────────────────────────
            case AvatarViseme.RhubarbRr:
                jawOpen      = 12f;
                mouthStretch =  8f;
                break;

            // ── K, G, H (velare) ──────────────────────────────────────────
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbC:  // Rhubarb 'C' = EE
            case AvatarViseme.RhubarbG:  // Rhubarb 'G' = KK
                jawOpen      = 10f;
                mouthStretch = 10f;
                break;

            // ── Rhubarb A = gură larg deschisă ────────────────────────────
            case AvatarViseme.RhubarbA:
                jawOpen     = 32f;
                mouthFunnel = 10f;
                break;

            // ── Rhubarb B = buze închise ──────────────────────────────────
            case AvatarViseme.RhubarbB:
                mouthClose = 50f;
                jawOpen    =  0f;
                break;

            // ── Rhubarb D = vocala deschisă ───────────────────────────────
            case AvatarViseme.RhubarbD:
                jawOpen    = 28f;
                mouthSmile = 10f;
                break;

            // ── Rhubarb E = vocala medie ──────────────────────────────────
            case AvatarViseme.RhubarbE:
                jawOpen    = 16f;
                mouthSmile = 22f;
                break;

            // ── Rhubarb F = F/V ───────────────────────────────────────────
            case AvatarViseme.RhubarbF:
                mouthShrug = 28f;
                jawOpen    =  5f;
                break;

            // ── Rhubarb H = W/OO ──────────────────────────────────────────
            case AvatarViseme.RhubarbH:
                mouthPucker = 38f;
                jawOpen     =  6f;
                break;

            // ── Rest și TeethOnLip cu jaw mic ─────────────────────────────
            case AvatarViseme.Rest:
            default:
                // Toate rămân la 0 → buza la pauză
                break;
        }

        // ── Trimitem toate valorile la SetShape — cele la 0 sunt resetate ──
        SetShape("jawopen",      jawOpen);
        SetShape("mouthfunnel",  mouthFunnel);
        SetShape("mouthsmile",   mouthSmile);
        SetShape("mouthsmilel",  mouthSmile);    // alias stânga
        SetShape("mouthsmiler",  mouthSmile);    // alias dreapta
        SetShape("mouthclose",   mouthClose);
        SetShape("mouthshruglower", mouthShrug);
        SetShape("mouthpucker",  mouthPucker);
        SetShape("mouthstretch", mouthStretch);
        SetShape("mouthstretchl", mouthStretch);
        SetShape("mouthstretchr", mouthStretch);
    }

    // ────────────────────────────────────────────────────────────────────────
    // SetShape cu Lerp și tracking al valorii curente
    // ────────────────────────────────────────────────────────────────────────

    private void SetShape(string shape, float target)
    {
        if (!shapeBindings.TryGetValue(shape, out List<ShapeBinding> bindings))
            return;

        currentShapeValues.TryGetValue(shape, out float current);
        float next = Mathf.Lerp(current, target, Time.deltaTime * blendSpeed);
        currentShapeValues[shape] = next;

        for (int i = 0; i < bindings.Count; i++)
        {
            ShapeBinding binding = bindings[i];
            if (binding.renderer == null || binding.renderer.sharedMesh == null ||
                binding.index >= binding.renderer.sharedMesh.blendShapeCount)
            {
                if (!warnedAboutInvalidRenderer)
                {
                    Debug.LogWarning(
                        "Un mesh al avatarului nu mai are blendshape-urile încărcate. " +
                        "Reîncarcă scena pentru a-l reimporta.", this);
                    warnedAboutInvalidRenderer = true;
                }

                continue;
            }

            binding.renderer.SetBlendShapeWeight(binding.index, next);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Cacheing blendshapes — detecție robustă a mesh-ului feței
    // ────────────────────────────────────────────────────────────────────────

    private void CacheBlendShapes()
    {
        SkinnedMeshRenderer[] allRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (allRenderers.Length == 0)
        {
            Debug.LogWarning("Nu am găsit niciun SkinnedMeshRenderer în avatar.", this);
            return;
        }

        // Dacă face renderer-ul a fost setat manual în Inspector, folosim doar ăla
        if (faceRenderer != null)
        {
            IndexRendererShapes(faceRenderer);

#if UNITY_EDITOR
            if (logBlendShapeNames)
                LogCachedShapes();
#endif
            return;
        }

        // ── Strategie de detecție automată ──────────────────────────────
        // Prioritate 1: mesh numit exact "AvatarHead"
        foreach (SkinnedMeshRenderer candidate in allRenderers)
        {
            if (string.Equals(candidate.gameObject.name, "AvatarHead", StringComparison.OrdinalIgnoreCase))
            {
                faceRenderer = candidate;
                break;
            }
        }

        // Prioritate 2: orice mesh care conține "head" sau "face" în nume
        if (faceRenderer == null)
        {
            foreach (SkinnedMeshRenderer candidate in allRenderers)
            {
                string n = candidate.gameObject.name;
                if (n.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    faceRenderer = candidate;
                    break;
                }
            }
        }

        // Prioritate 3: mesh-ul cu cei mai mulți blendshapes (cel mai probabil fața)
        if (faceRenderer == null)
        {
            int maxShapes = -1;
            foreach (SkinnedMeshRenderer candidate in allRenderers)
            {
                if (candidate.sharedMesh == null)
                    continue;

                int count = candidate.sharedMesh.blendShapeCount;
                if (count > maxShapes)
                {
                    maxShapes = count;
                    faceRenderer = candidate;
                }
            }
        }

        if (faceRenderer != null)
        {
            Debug.Log($"[AvatarVisemeAnimator] Mesh față detectat: '{faceRenderer.gameObject.name}' " +
                      $"({(faceRenderer.sharedMesh != null ? faceRenderer.sharedMesh.blendShapeCount : 0)} blendshapes).", this);
            IndexRendererShapes(faceRenderer);
        }
        else
        {
            Debug.LogWarning(
                "[AvatarVisemeAnimator] Nu am putut identifica mesh-ul feței. " +
                "Setează manual câmpul 'Face Renderer' în Inspector.", this);
        }

#if UNITY_EDITOR
        if (logBlendShapeNames)
            LogCachedShapes();
#endif
    }

    /// <summary>Indexează toate blendshape-urile dintr-un renderer și le adaugă în dicționar.</summary>
    private void IndexRendererShapes(SkinnedMeshRenderer renderer)
    {
        if (renderer == null || renderer.sharedMesh == null)
            return;

        int count = renderer.sharedMesh.blendShapeCount;
        for (int i = 0; i < count; i++)
        {
            string rawName = renderer.sharedMesh.GetBlendShapeName(i);
            string normalized = NormalizeName(rawName);
            AddShapeBinding(normalized, renderer, i);

            // Adăugăm și variante fără sufixul lateral (Left/Right) pentru a
            // simplifica adresarea uniformă a ambelor jumătăți ale feței
            if (normalized.EndsWith("left", StringComparison.Ordinal))
                AddShapeBinding(normalized.Substring(0, normalized.Length - 4), renderer, i);
            else if (normalized.EndsWith("right", StringComparison.Ordinal))
                AddShapeBinding(normalized.Substring(0, normalized.Length - 5), renderer, i);

            // Alias "l" / "r" suffix (ex: "mouthsmilel", "mouthsmiler")
            if (normalized.Length > 1 && normalized[normalized.Length - 1] == 'l')
                AddShapeBinding(normalized.Substring(0, normalized.Length - 1), renderer, i);
            else if (normalized.Length > 1 && normalized[normalized.Length - 1] == 'r')
                AddShapeBinding(normalized.Substring(0, normalized.Length - 1), renderer, i);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Cacheing oase
    // ────────────────────────────────────────────────────────────────────────

    private void CacheHeadBone()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in transforms)
        {
            string boneName = candidate.name;
            int separator = boneName.LastIndexOf(':');
            if (separator >= 0)
                boneName = boneName.Substring(separator + 1);

            if (string.Equals(boneName, "head", StringComparison.OrdinalIgnoreCase))
            {
                headBone = candidate;
                headRestRotation = candidate.localRotation;
                return;
            }
        }
    }

    private void CacheUpperBodyBone()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in transforms)
        {
            string n = candidate.name;
            if (string.Equals(n, "Spine2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "Chest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "Spine1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "UpperChest", StringComparison.OrdinalIgnoreCase))
            {
                upperBodyBone = candidate;
                upperBodyRestRotation = candidate.localRotation;
                return;
            }
        }
    }

    private void CacheWalkBones()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in transforms)
        {
            switch (candidate.name)
            {
                case "LeftUpLeg":
                    leftLegBone = candidate;
                    leftLegRestRotation = candidate.localRotation;
                    break;
                case "RightUpLeg":
                    rightLegBone = candidate;
                    rightLegRestRotation = candidate.localRotation;
                    break;
                case "LeftArm":
                    leftArmBone = candidate;
                    leftArmRestRotation = candidate.localRotation;
                    break;
                case "RightArm":
                    rightArmBone = candidate;
                    rightArmRestRotation = candidate.localRotation;
                    break;
            }
        }
    }

    private void RestoreMovementPose()
    {
        if (avatarRoot != null)
        {
            avatarRoot.localPosition = avatarRootRestPosition;
            avatarRoot.localRotation = avatarRootRestRotation;
        }

        if (leftLegBone != null)   leftLegBone.localRotation   = leftLegRestRotation;
        if (rightLegBone != null)  rightLegBone.localRotation  = rightLegRestRotation;
        if (leftArmBone != null)   leftArmBone.localRotation   = leftArmRestRotation;
        if (rightArmBone != null)  rightArmBone.localRotation  = rightArmRestRotation;
        if (headBone != null)      headBone.localRotation      = headRestRotation;
        if (upperBodyBone != null) upperBodyBone.localRotation = upperBodyRestRotation;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Reset și utilitare
    // ────────────────────────────────────────────────────────────────────────

    private void ResetFaceBlendShapes()
    {
        if (faceRenderer == null || faceRenderer.sharedMesh == null)
            return;

        for (int i = 0; i < faceRenderer.sharedMesh.blendShapeCount; i++)
            faceRenderer.SetBlendShapeWeight(i, 0f);

        currentShapeValues.Clear();
    }

    private void AddShapeBinding(string shape, SkinnedMeshRenderer renderer, int index)
    {
        if (!shapeBindings.TryGetValue(shape, out List<ShapeBinding> bindings))
        {
            bindings = new List<ShapeBinding>();
            shapeBindings.Add(shape, bindings);
        }

        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].renderer == renderer && bindings[i].index == index)
                return; // evitare duplicare
        }

        bindings.Add(new ShapeBinding { renderer = renderer, index = index });
    }

    /// <summary>Normalizează un nume de blendshape: elimină spații, '_', '-' și converteste la lowercase.</summary>
    private static string NormalizeName(string name)
    {
        return name.Replace("_", string.Empty)
                   .Replace("-", string.Empty)
                   .Replace(" ", string.Empty)
                   .Replace(".", string.Empty)
                   .ToLowerInvariant();
    }

#if UNITY_EDITOR
    private void LogCachedShapes()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[AvatarVisemeAnimator] {shapeBindings.Count} blendshape keys indexate:");
        foreach (var key in shapeBindings.Keys)
            sb.AppendLine($"  {key}");
        Debug.Log(sb.ToString(), this);
    }
#endif
}
