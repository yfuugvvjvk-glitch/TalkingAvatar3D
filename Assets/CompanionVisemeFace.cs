using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CompanionVisemeFace : MonoBehaviour
{
    private const float FaceDistance = 2.8f;
    private const float FaceViewportHeight = 0.48f;
    private const float VisemeWeightScale = 0.04f;

    private sealed class BlendShapeTarget
    {
        public SkinnedMeshRenderer renderer;
        public int index;
        public string name;
        public float currentWeight;
        public float targetWeight;
    }

    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private readonly List<BlendShapeTarget> headBlendShapes = new List<BlendShapeTarget>();
    private readonly List<BlendShapeTarget> teethBlendShapes = new List<BlendShapeTarget>();
    private Camera targetCamera;
    private Transform faceRoot;
    private Transform headBone;
    private Quaternion headRestRotation;
    private SkinnedMeshRenderer headRenderer;
    private Renderer[] visibleHeadRenderers;
    private float viewportX = 0.19f;
    private float viewportY = 0.54f;
    private float speechStartedAt;
    private float speechDuration;
    private bool headSized;
    public bool InputBlocked { get; set; }

    public void Initialize(Camera cameraToUse, Transform sourceAvatar)
    {
        if (cameraToUse == null)
        {
            Debug.LogError("[CompanionVisemeFace] Nu poate fi creat fără o cameră.");
            return;
        }
        if (sourceAvatar == null)
        {
            Debug.LogError("[CompanionVisemeFace] Avatarul sursă nu este disponibil.");
            return;
        }
        if (faceRoot != null)
            return;

        targetCamera = cameraToUse;
        CreateAvatarHead(sourceAvatar);
    }

    public void PlaySpeech(string text, float duration, string language, string rhubarbJson)
    {
        timeline.Clear();
        List<VisemeFrame> rhubarbFrames =
            VisemeTextMapper.CreateRhubarbTimeline(rhubarbJson, duration);
        timeline.AddRange(rhubarbFrames.Count > 0
            ? rhubarbFrames
            : VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDuration = duration;
        speechStartedAt = Time.time;
    }

    public void Move(float horizontal, float vertical)
    {
        viewportX = Mathf.Clamp(viewportX + horizontal, 0.06f, 0.44f);
        viewportY = Mathf.Clamp(viewportY + vertical, 0.16f, 0.88f);
        UpdatePosition();
    }

    private void CreateAvatarHead(Transform sourceAvatar)
    {
        GameObject clone = Instantiate(sourceAvatar.gameObject);
        clone.name = "Companion Avatar Head";
        faceRoot = clone.transform;
        faceRoot.SetParent(targetCamera.transform, false);
        faceRoot.localPosition = Vector3.zero;
        faceRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
        faceRoot.localScale = sourceAvatar.localScale;

        var visibleRenderers = new List<Renderer>();
        foreach (Renderer renderer in faceRoot.GetComponentsInChildren<Renderer>(true))
        {
            string rendererName = renderer.gameObject.name;
            bool isHeadPart =
                rendererName.Equals("AvatarHead", StringComparison.OrdinalIgnoreCase) ||
                rendererName.Equals("AvatarEyelashes", StringComparison.OrdinalIgnoreCase) ||
                rendererName.Equals("AvatarLeftEyeball", StringComparison.OrdinalIgnoreCase) ||
                rendererName.Equals("AvatarRightEyeball", StringComparison.OrdinalIgnoreCase) ||
                rendererName.Equals("AvatarTeethLower", StringComparison.OrdinalIgnoreCase) ||
                rendererName.Equals("AvatarTeethUpper", StringComparison.OrdinalIgnoreCase);
            renderer.enabled = isHeadPart;
            if (isHeadPart)
                visibleRenderers.Add(renderer);
            if (rendererName.Equals("AvatarHead", StringComparison.OrdinalIgnoreCase))
                headRenderer = renderer as SkinnedMeshRenderer;
        }

        if (headRenderer == null || visibleRenderers.Count == 0)
        {
            Destroy(faceRoot.gameObject);
            faceRoot = null;
            Debug.LogError("[CompanionVisemeFace] Mesh-ul capului avatarului nu a fost găsit.");
            return;
        }

        visibleHeadRenderers = visibleRenderers.ToArray();
        headBone = FindHeadBone(faceRoot);
        if (headBone != null)
            headRestRotation = headBone.localRotation;

        CacheBlendShapes(headRenderer, headBlendShapes);
        foreach (Renderer renderer in visibleHeadRenderers)
        {
            if (!renderer.gameObject.name.Equals("AvatarTeethLower",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
                CacheBlendShapes(skinnedRenderer, teethBlendShapes);
        }

        if (headBlendShapes.Count == 0)
            Debug.LogError("[CompanionVisemeFace] Mesh-ul capului nu conține blendshape-uri.");

        UpdatePosition();
    }

    private static Transform FindHeadBone(Transform root)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            if (candidate.name.Equals("Head", StringComparison.OrdinalIgnoreCase) &&
                candidate.GetComponent<SkinnedMeshRenderer>() == null)
                return candidate;
        return null;
    }

    private static void CacheBlendShapes(
        SkinnedMeshRenderer renderer,
        List<BlendShapeTarget> destination)
    {
        if (renderer == null || renderer.sharedMesh == null)
            return;

        for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
        {
            destination.Add(new BlendShapeTarget
            {
                renderer = renderer,
                index = i,
                name = renderer.sharedMesh.GetBlendShapeName(i),
                currentWeight = renderer.GetBlendShapeWeight(i),
                targetWeight = 0f
            });
        }
    }

    private void Update()
    {
        if (faceRoot == null)
            return;

        HandlePositionInput();
        float elapsed = Time.time - speechStartedAt;
        bool speaking = elapsed < speechDuration && timeline.Count > 0;
        AvatarViseme viseme = speaking ? GetVisemeAt(elapsed) : AvatarViseme.Rest;

        foreach (BlendShapeTarget shape in headBlendShapes)
            shape.targetWeight = 0f;
        foreach (BlendShapeTarget shape in teethBlendShapes)
            shape.targetWeight = 0f;

        ApplyHeadViseme(viseme);
        ApplyJawViseme(viseme);
        AnimateBlendShapes(headBlendShapes);
        AnimateBlendShapes(teethBlendShapes);

        if (headBone != null)
        {
            float nod = speaking ? Mathf.Sin(elapsed * 3f) * 0.6f : 0f;
            headBone.localRotation = Quaternion.Slerp(
                headBone.localRotation,
                headRestRotation * Quaternion.Euler(nod, 0f, 0f),
                Time.deltaTime * 10f);
        }

        float bob = speaking ? Mathf.Sin(elapsed * 4f) * 0.015f : 0f;
        Vector3 localPosition = faceRoot.localPosition;
        localPosition.y = basePositionY + bob;
        faceRoot.localPosition = localPosition;
    }

    private float basePositionY;

    private void ApplyHeadViseme(AvatarViseme viseme)
    {
        switch (viseme)
        {
            case AvatarViseme.OpenRound:
            case AvatarViseme.RhubarbA:
                SetShape(headBlendShapes, "jawOpen", 8f);
                SetShape(headBlendShapes, "mouthFunnel", 12f);
                break;
            case AvatarViseme.RhubarbD:
                SetShape(headBlendShapes, "jawOpen", 5f);
                SetShape(headBlendShapes, "mouthFunnel", 8f);
                break;
            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
                SetShape(headBlendShapes, "ih", 4.2f);
                SetShape(headBlendShapes, "mouthSmileLeft", 8f);
                SetShape(headBlendShapes, "mouthSmileRight", 8f);
                SetShape(headBlendShapes, "jawOpen", 4f);
                break;
            case AvatarViseme.RhubarbE:
                SetShape(headBlendShapes, "E", 4.2f);
                SetShape(headBlendShapes, "mouthSmileLeft", 7f);
                SetShape(headBlendShapes, "mouthSmileRight", 7f);
                SetShape(headBlendShapes, "jawOpen", 3f);
                break;
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:
                SetShape(headBlendShapes, "PP", 18f);
                SetShape(headBlendShapes, "mouthClose", 8f);
                break;
            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
                SetShape(headBlendShapes, "mouthShrugLower", 8f);
                break;
            case AvatarViseme.RhubarbTh:
                SetShape(headBlendShapes, "TH", 45f);
                break;
            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:
                SetShape(headBlendShapes, "ou", 48f);
                SetShape(headBlendShapes, "mouthPucker", 20f);
                break;
            case AvatarViseme.RhubarbDd:
                SetShape(headBlendShapes, "DD", 38f);
                ApplyAlveolarViseme();
                break;
            case AvatarViseme.RhubarbNn:
                SetShape(headBlendShapes, "nn", 38f);
                ApplyAlveolarViseme();
                break;
            case AvatarViseme.RhubarbSs:
                SetShape(headBlendShapes, "SS", 42f);
                ApplyAlveolarViseme();
                break;
            case AvatarViseme.RhubarbCh:
                SetShape(headBlendShapes, "CH", 42f);
                break;
            case AvatarViseme.RhubarbRr:
                SetShape(headBlendShapes, "RR", 38f);
                break;
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbG:
                SetShape(headBlendShapes, "kk", 38f);
                break;
            case AvatarViseme.Consonant:
                ApplyAlveolarViseme();
                break;
        }
    }

    private void ApplyAlveolarViseme()
    {
        SetShape(headBlendShapes, "mouthStretchLeft", 8f);
        SetShape(headBlendShapes, "mouthStretchRight", 8f);
        SetShape(headBlendShapes, "jawOpen", 2f);
    }

    private void ApplyJawViseme(AvatarViseme viseme)
    {
        float jawWeight = viseme == AvatarViseme.OpenRound ||
                          viseme == AvatarViseme.RhubarbA ? 8f :
                          viseme == AvatarViseme.RhubarbD ? 5f :
                          viseme == AvatarViseme.MidSpread ||
                          viseme == AvatarViseme.RhubarbC ? 4f :
                          viseme == AvatarViseme.RhubarbE ||
                          viseme == AvatarViseme.RhubarbNn ||
                          viseme == AvatarViseme.RhubarbSs ||
                          viseme == AvatarViseme.RhubarbDd ? 2f :
                          viseme == AvatarViseme.Consonant ||
                          viseme == AvatarViseme.RhubarbF ? 2f : 0f;
        SetShape(teethBlendShapes, "jawOpen", jawWeight);
    }

    private static void SetShape(
        List<BlendShapeTarget> shapes, string shapeName, float weight)
    {
        foreach (BlendShapeTarget shape in shapes)
            if (shape.name.Equals(shapeName, StringComparison.OrdinalIgnoreCase))
                shape.targetWeight = weight * VisemeWeightScale;
    }

    private static void AnimateBlendShapes(List<BlendShapeTarget> shapes)
    {
        float blend = Time.deltaTime * 14f;
        foreach (BlendShapeTarget shape in shapes)
        {
            if (shape.renderer == null)
                continue;
            shape.currentWeight = Mathf.Lerp(
                shape.currentWeight, shape.targetWeight, blend);
            shape.renderer.SetBlendShapeWeight(shape.index, shape.currentWeight);
        }
    }

    private void HandlePositionInput()
    {
        if (InputBlocked)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.jKey.wasPressedThisFrame) Move(-0.04f, 0f);
        if (keyboard.lKey.wasPressedThisFrame) Move(0.04f, 0f);
        if (keyboard.iKey.wasPressedThisFrame) Move(0f, 0.04f);
        if (keyboard.kKey.wasPressedThisFrame) Move(0f, -0.04f);
    }

    private AvatarViseme GetVisemeAt(float elapsed)
    {
        for (int index = 1; index < timeline.Count; index++)
            if (elapsed < timeline[index].time)
                return timeline[index - 1].viseme;

        return timeline.Count > 0
            ? timeline[timeline.Count - 1].viseme
            : AvatarViseme.Rest;
    }

    private void UpdatePosition()
    {
        if (targetCamera == null || faceRoot == null || visibleHeadRenderers == null)
            return;

        Bounds bounds = visibleHeadRenderers[0].bounds;
        for (int i = 1; i < visibleHeadRenderers.Length; i++)
            bounds.Encapsulate(visibleHeadRenderers[i].bounds);

        float viewHeight = 2f * FaceDistance *
            Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float desiredHeight = viewHeight * FaceViewportHeight;
        if (!headSized && bounds.size.y > 0.0001f)
        {
            faceRoot.localScale *= desiredHeight / bounds.size.y;
            headSized = true;
        }

        bounds = visibleHeadRenderers[0].bounds;
        for (int i = 1; i < visibleHeadRenderers.Length; i++)
            bounds.Encapsulate(visibleHeadRenderers[i].bounds);

        Vector3 targetCenter = targetCamera.ViewportToWorldPoint(
            new Vector3(viewportX, viewportY, FaceDistance));
        faceRoot.position += targetCenter - bounds.center;
        basePositionY = faceRoot.localPosition.y;
    }

    private void OnDestroy()
    {
        if (faceRoot != null)
            Destroy(faceRoot.gameObject);
    }
}
