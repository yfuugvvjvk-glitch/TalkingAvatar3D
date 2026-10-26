using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CompanionVisemeFace : MonoBehaviour
{
    private const float FaceDistance = 2.8f;

    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private Camera targetCamera;
    private Transform faceRoot;
    private Transform mouth;
    private Transform upperLip;
    private Transform lowerLip;
    private Transform mouthOpening;
    private Transform teeth;
    private Material headMaterial;
    private Material eyeMaterial;
    private Material pupilMaterial;
    private Material lipMaterial;
    private Material mouthMaterial;
    private Material teethMaterial;
    private Vector3 baseLocalPosition;
    private float viewportX = 0.19f;
    private float viewportY = 0.54f;
    private float speechStartedAt;
    private float speechDuration;
    private Vector3 mouthScale = new Vector3(1f, 0.2f, 1f);
    private bool showTeeth;
    public bool InputBlocked { get; set; }

    public void Initialize(Camera cameraToUse)
    {
        if (cameraToUse == null)
        {
            Debug.LogError("Fața vorbitoare nu poate fi creată fără o cameră.");
            return;
        }

        targetCamera = cameraToUse;
        CreateMaterials();
        CreateFace();
        UpdatePosition();
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

    private void CreateMaterials()
    {
        Shader headShader = Shader.Find("Universal Render Pipeline/Lit");
        if (headShader == null) headShader = Shader.Find("Standard");
        Shader featureShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (featureShader == null) featureShader = Shader.Find("Unlit/Color");
        if (featureShader == null) featureShader = headShader;

        headMaterial = CreateMaterial(headShader, new Color(0.98f, 0.72f, 0.43f));
        eyeMaterial = CreateMaterial(featureShader, Color.white);
        pupilMaterial = CreateMaterial(featureShader, new Color(0.04f, 0.07f, 0.12f));
        lipMaterial = CreateMaterial(featureShader, new Color(0.78f, 0.08f, 0.18f));
        mouthMaterial = CreateMaterial(featureShader, new Color(0.08f, 0.01f, 0.025f));
        teethMaterial = CreateMaterial(featureShader, new Color(1f, 1f, 0.94f));
    }

    private static Material CreateMaterial(Shader shader, Color color)
    {
        if (shader == null)
        {
            Debug.LogError("Nu a fost găsit un shader compatibil pentru fața vorbitoare.");
            return null;
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private void CreateFace()
    {
        faceRoot = new GameObject("Companion Speech Face").transform;
        faceRoot.SetParent(targetCamera.transform, false);
        faceRoot.localScale = Vector3.one * 0.72f;

        CreateSphere("Head", faceRoot, Vector3.zero, new Vector3(1f, 1f, 1f), headMaterial);

        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.14f;
            CreateSphere("Eye", faceRoot, new Vector3(x, 0.09f, -0.62f),
                new Vector3(0.17f, 0.2f, 0.08f), eyeMaterial);
            CreateSphere("Pupil", faceRoot, new Vector3(x, 0.085f, -0.72f),
                new Vector3(0.075f, 0.1f, 0.04f), pupilMaterial);
            CreateSphere("Eye Highlight", faceRoot, new Vector3(x - 0.02f, 0.11f, -0.77f),
                new Vector3(0.03f, 0.035f, 0.018f), teethMaterial);
        }

        mouth = new GameObject("Articulating Mouth").transform;
        mouth.SetParent(faceRoot, false);
        mouth.localPosition = new Vector3(0f, -0.16f, -0.62f);

        upperLip = CreateSphere("Upper Lip", mouth, new Vector3(0f, 0.035f, -0.07f),
            new Vector3(0.48f, 0.09f, 0.07f), lipMaterial);
        lowerLip = CreateSphere("Lower Lip", mouth, new Vector3(0f, -0.035f, -0.07f),
            new Vector3(0.48f, 0.09f, 0.07f), lipMaterial);
        mouthOpening = CreateSphere("Mouth Opening", mouth, new Vector3(0f, 0f, -0.13f),
            new Vector3(0.36f, 0.03f, 0.04f), mouthMaterial);
        teeth = CreateSphere("Teeth", mouth, new Vector3(0f, 0.025f, -0.18f),
            new Vector3(0.22f, 0.055f, 0.025f), teethMaterial);
        teeth.gameObject.SetActive(false);
    }

    private static Transform CreateSphere(string objectName, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = objectName;
        sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = localPosition;
        sphere.transform.localScale = localScale;

        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return sphere.transform;
    }

    private void Update()
    {
        if (faceRoot == null) return;

        HandlePositionInput();

        float elapsed = Time.time - speechStartedAt;
        bool speaking = elapsed < speechDuration && timeline.Count > 0;
        Vector3 targetMouthScale = speaking
            ? GetMouthScale(GetVisemeAt(elapsed))
            : new Vector3(1f, 0.2f, 1f);

        mouthScale = Vector3.Lerp(mouthScale, targetMouthScale,
            Time.deltaTime * 16f);
        float lipSeparation = 0.06f * mouthScale.y;
        upperLip.localPosition = new Vector3(0f, lipSeparation, -0.07f);
        lowerLip.localPosition = new Vector3(0f, -lipSeparation, -0.07f);
        upperLip.localScale = new Vector3(0.48f * mouthScale.x, 0.09f, 0.07f);
        lowerLip.localScale = new Vector3(0.48f * mouthScale.x, 0.09f, 0.07f);
        mouthOpening.localScale = new Vector3(
            0.36f * mouthScale.x, 0.14f * mouthScale.y, 0.04f);
        teeth.localPosition = new Vector3(0f, 0.025f * mouthScale.y, -0.18f);
        bool targetTeeth = speaking && HasVisibleTeeth(GetVisemeAt(elapsed));
        if (targetTeeth != showTeeth)
        {
            showTeeth = targetTeeth;
            teeth.gameObject.SetActive(showTeeth);
        }

        float bob = speaking ? Mathf.Sin(elapsed * 4f) * 0.025f : 0f;
        faceRoot.localPosition = baseLocalPosition + Vector3.up * bob;
    }

    private void HandlePositionInput()
    {
        if (InputBlocked) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

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

    private static Vector3 GetMouthScale(AvatarViseme viseme)
    {
        switch (viseme)
        {
            case AvatarViseme.OpenRound:
            case AvatarViseme.RhubarbA:
            case AvatarViseme.RhubarbD:
                return new Vector3(0.88f, 1.4f, 1f);
            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
            case AvatarViseme.RhubarbE:
                return new Vector3(1.35f, 0.85f, 1f);
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:
                return new Vector3(1f, 0.08f, 1f);
            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
            case AvatarViseme.RhubarbTh:
                return new Vector3(0.92f, 0.48f, 1f);
            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:
                return new Vector3(0.58f, 0.85f, 1f);
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbCh:
                return new Vector3(1.25f, 0.42f, 1f);
            case AvatarViseme.RhubarbRr:
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbG:
            case AvatarViseme.Consonant:
                return new Vector3(0.9f, 0.55f, 1f);
            default:
                return new Vector3(1f, 0.2f, 1f);
        }
    }

    private static bool HasVisibleTeeth(AvatarViseme viseme)
    {
        return viseme == AvatarViseme.MidSpread ||
               viseme == AvatarViseme.RhubarbC ||
               viseme == AvatarViseme.RhubarbE ||
               viseme == AvatarViseme.TeethOnLip ||
               viseme == AvatarViseme.RhubarbF ||
               viseme == AvatarViseme.RhubarbTh ||
               viseme == AvatarViseme.RhubarbDd ||
               viseme == AvatarViseme.RhubarbNn ||
               viseme == AvatarViseme.RhubarbSs ||
               viseme == AvatarViseme.RhubarbCh;
    }

    private void UpdatePosition()
    {
        if (targetCamera == null || faceRoot == null) return;

        Vector3 worldPosition = targetCamera.ViewportToWorldPoint(
            new Vector3(viewportX, viewportY, FaceDistance));
        baseLocalPosition = targetCamera.transform.InverseTransformPoint(worldPosition);
        faceRoot.localPosition = baseLocalPosition;
    }

    private void OnDestroy()
    {
        DestroyMaterial(headMaterial);
        DestroyMaterial(eyeMaterial);
        DestroyMaterial(pupilMaterial);
        DestroyMaterial(lipMaterial);
        DestroyMaterial(mouthMaterial);
        DestroyMaterial(teethMaterial);
    }

    private static void DestroyMaterial(Material material)
    {
        if (material != null) Destroy(material);
    }
}
