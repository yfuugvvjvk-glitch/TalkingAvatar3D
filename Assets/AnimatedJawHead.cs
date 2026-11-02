using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Încarcă cele două piese ale măștii din StreamingAssets, le atașează pe osul Head
/// și animează mandibula sincronizat cu vorbirea.
/// </summary>
public class AnimatedJawHead : MonoBehaviour
{
    [HideInInspector] public Transform avatarHeadBone;
    [HideInInspector] public Transform avatarRoot;
    [HideInInspector] public AudioSource speechAudioSource;

    private Transform maskRoot;
    private Transform jawTransform;
    private Transform upperLip;
    private Transform lowerLip;
    private Transform mouthOpening;
    private Transform teeth;
    private Material maskMaterial;
    private Material upperMaskMaterial;
    private Material lipMaterial;
    private Material mouthMaterial;
    private Material teethMaterial;
    private Vector3 mouthCenterLocal;
    private Vector3 jawPivotLocal;
    private Quaternion jawRestRot;
    private Vector3 jawAxisLocal;
    private float jawAngle;
    private float mouthWidth = 0.064f;
    private float mouthOpen;
    private float mouthPout;
    private float audioEmphasis;
    private float audioPitchAccent;
    private float nextAudioSample;
    private readonly float[] audioSamples = new float[2048];
    private bool isReady;

    private readonly List<VisemeFrame> timeline = new List<VisemeFrame>();
    private float speechStart;
    private float speechDur;

    // ════════════════════════════════════════════════════════════════════════

    public bool AttachToHead()
    {
        if (avatarHeadBone == null)
        {
            Debug.LogError("[Mask] Osul Head al avatarului nu a fost găsit.");
            return false;
        }
        if (avatarRoot == null)
        {
            Debug.LogError("[Mask] Rădăcina avatarului nu a fost furnizată.");
            return false;
        }

        string topPath = System.IO.Path.Combine(Application.streamingAssetsPath, "head_top.obj");
        string jawPath = System.IO.Path.Combine(Application.streamingAssetsPath, "head_jaw.obj");

        if (!System.IO.File.Exists(topPath))
        {
            Debug.LogError("[Mask] Lipsește partea superioară a măștii: " + topPath);
            return false;
        }
        if (!System.IO.File.Exists(jawPath))
        {
            Debug.LogError("[Mask] Lipsește mandibula măștii: " + jawPath);
            return false;
        }

        Mesh topMesh = ParseOBJ(System.IO.File.ReadAllText(topPath));
        Mesh jawMesh = ParseOBJ(System.IO.File.ReadAllText(jawPath));

        if (topMesh == null)
        {
            Debug.LogError("[Mask] Mesh-ul head_top.obj nu conține vertecși și fețe valide.");
            return false;
        }
        if (jawMesh == null)
        {
            Destroy(topMesh);
            Debug.LogError("[Mask] Mesh-ul head_jaw.obj nu conține vertecși și fețe valide.");
            return false;
        }

        Vector3 jawPivot = new Vector3(
            (topMesh.bounds.min.x + topMesh.bounds.max.x +
             jawMesh.bounds.min.x + jawMesh.bounds.max.x) * 0.25f,
            Mathf.Max(topMesh.bounds.max.y, jawMesh.bounds.max.y),
            // Place the hinge behind the mouth split, toward the ear line.
            jawMesh.bounds.max.z + 0.02f);
        jawPivotLocal = ToHeadLocal(jawPivot);
        if (!ConformUpperMaskToAvatarFace(topMesh, jawMesh, out Material skinMaterial))
        {
            Destroy(topMesh);
            Destroy(jawMesh);
            return false;
        }

        maskMaterial = skinMaterial != null ? skinMaterial : MakeMat(new Color(0.76f, 0.59f, 0.51f));
        upperMaskMaterial = maskMaterial;
        lipMaterial = MakeMat(new Color(0.48f, 0.22f, 0.24f));
        mouthMaterial = MakeMat(new Color(0.045f, 0.012f, 0.014f));
        teethMaterial = MakeMat(new Color(0.96f, 0.89f, 0.77f));
        if (maskMaterial == null || upperMaskMaterial == null || lipMaterial == null ||
            mouthMaterial == null || teethMaterial == null)
        {
            Destroy(topMesh);
            Destroy(jawMesh);
            DestroyMaterials();
            Debug.LogError("[Mask] Nu s-a putut crea materialul măștii.");
            return false;
        }

        GameObject rootGO = new GameObject("Avatar Face Mask");
        maskRoot = rootGO.transform;
        maskRoot.SetParent(avatarHeadBone, false);
        maskRoot.localPosition = Vector3.zero;
        maskRoot.localRotation = Quaternion.identity;
        maskRoot.localScale = Vector3.one;

        GameObject topGO = new GameObject("HeadTop");
        topGO.transform.SetParent(maskRoot, false);
        topGO.AddComponent<MeshFilter>().sharedMesh = topMesh;
        topGO.AddComponent<MeshRenderer>().sharedMaterial = upperMaskMaterial;

        GameObject jawGO = new GameObject("HeadJaw");
        jawGO.transform.SetParent(maskRoot, false);
        jawGO.transform.localPosition = jawPivotLocal;
        jawGO.AddComponent<MeshFilter>().sharedMesh = jawMesh;
        jawGO.AddComponent<MeshRenderer>().sharedMaterial = maskMaterial;

        jawTransform = jawGO.transform;
        jawRestRot = jawGO.transform.localRotation;
        jawAxisLocal = avatarHeadBone.InverseTransformDirection(avatarRoot.right).normalized;
        mouthCenterLocal = ToHeadLocal(new Vector3(0f, -0.149f, 1.606f));
        CreateMouth();
        isReady = true;

        return true;
    }

    // ════════════════════════════════════════════════════════════════════════

    public void PlaySpeech(string text, float duration, string language, string rhubarbJson)
    {
        timeline.Clear();
        var rh = VisemeTextMapper.CreateRhubarbTimeline(rhubarbJson, duration);
        timeline.AddRange(rh.Count > 0
            ? rh : VisemeTextMapper.CreateTimeline(text, duration, language));
        speechDur = duration;
        speechStart = Time.time;
    }

    // ════════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!isReady || jawTransform == null) return;

        float elapsed  = Time.time - speechStart;
        bool  speaking = elapsed < speechDur && timeline.Count > 0;
        AvatarViseme viseme = speaking ? GetViseme(elapsed) : AvatarViseme.Rest;
        float target = speaking ? ToAngle(viseme) : 0f;

        jawAngle = Mathf.Lerp(jawAngle, target, Time.deltaTime * 14f);
        jawTransform.localRotation = Quaternion.Slerp(
            jawTransform.localRotation,
            jawRestRot * Quaternion.AngleAxis(jawAngle, jawAxisLocal),
            Time.deltaTime * 18f);

        UpdateLips(GetMouthPose(viseme), speaking);
    }

    private void CreateMouth()
    {
        upperLip = CreateSphere("Mask Upper Lip", maskRoot, lipMaterial);
        lowerLip = CreateSphere("Mask Lower Lip", jawTransform, lipMaterial);
        mouthOpening = CreateSphere("Mask Mouth Opening", maskRoot, mouthMaterial);
        teeth = CreateSphere("Mask Teeth", maskRoot, teethMaterial);
        teeth.gameObject.SetActive(false);
    }

    private static Transform CreateSphere(string objectName, Transform parent, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = objectName;
        sphere.transform.SetParent(parent, false);
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        sphere.GetComponent<MeshRenderer>().sharedMaterial = material;
        return sphere.transform;
    }

    private void UpdateLips(MouthPose pose, bool speaking)
    {
        float emphasis = speaking ? 0.88f + SampleAudioEmphasis() * 0.24f : 1f;
        float pitchShape = speaking ? 1f + audioPitchAccent * 0.04f : 1f;
        float targetOpen = pose.opening * emphasis *
            (speaking ? 1f + audioPitchAccent * 0.08f : 1f);
        mouthWidth = Mathf.Lerp(mouthWidth, pose.width * pitchShape, Time.deltaTime * 20f);
        mouthOpen = Mathf.Lerp(mouthOpen, targetOpen, Time.deltaTime * 24f);
        mouthPout = Mathf.Lerp(mouthPout, speaking ? pose.pout : 0f, Time.deltaTime * 20f);

        upperLip.localPosition = mouthCenterLocal +
            Vector3.up * 0.0025f + Vector3.forward * (0.003f + mouthPout);
        upperLip.localScale = new Vector3(mouthWidth, 0.005f, 0.0045f);

        lowerLip.localPosition = mouthCenterLocal - jawPivotLocal +
            Vector3.down * 0.0025f +
            Vector3.forward * (0.003f + mouthPout);
        lowerLip.localScale = new Vector3(mouthWidth, 0.005f, 0.0045f);

        mouthOpening.localPosition = mouthCenterLocal +
            Vector3.down * (mouthOpen * 0.35f) + Vector3.forward * 0.012f;
        mouthOpening.localScale = new Vector3(
            Mathf.Max(0.006f, mouthWidth * 1.12f),
            Mathf.Max(0.001f, mouthOpen * 1.15f),
            0.003f);

        bool showTeeth = speaking && pose.showTeeth;
        teeth.gameObject.SetActive(showTeeth);
        teeth.localPosition = mouthCenterLocal +
            Vector3.down * Mathf.Min(0.003f, mouthOpen * 0.15f) +
            Vector3.forward * 0.016f;
        teeth.localScale = new Vector3(mouthWidth * 0.58f, 0.007f, 0.003f);
    }

    private float SampleAudioEmphasis()
    {
        if (speechAudioSource == null || !speechAudioSource.isPlaying)
            return audioEmphasis;
        if (Time.time < nextAudioSample)
            return audioEmphasis;

        speechAudioSource.GetOutputData(audioSamples, 0);
        float sum = 0f;
        int sampleCount = audioSamples.Length / 2;
        for (int i = 0; i < sampleCount; i++)
        {
            float sample = audioSamples[i * 2];
            sum += sample * sample;
        }
        float rms = Mathf.Sqrt(sum / sampleCount);
        audioEmphasis = Mathf.Clamp01((rms - 0.008f) / 0.12f);
        audioPitchAccent = EstimatePitchAccent(sampleCount, rms);
        nextAudioSample = Time.time + 0.04f;
        return audioEmphasis;
    }

    private float EstimatePitchAccent(int sampleCount, float rms)
    {
        if (rms < 0.012f)
        {
            audioPitchAccent = Mathf.Lerp(audioPitchAccent, 0f, 0.3f);
            return audioPitchAccent;
        }

        float mean = 0f;
        for (int i = 0; i < sampleCount; i++)
            mean += audioSamples[i * 2];
        mean /= sampleCount;

        float effectiveSampleRate = AudioSettings.outputSampleRate * 0.5f;
        int minLag = Mathf.Max(1, Mathf.FloorToInt(effectiveSampleRate / 320f));
        int maxLag = Mathf.Min(sampleCount * 2 / 3,
            Mathf.CeilToInt(effectiveSampleRate / 70f));
        float bestCorrelation = 0f;
        int bestLag = 0;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float correlation = 0f;
            float energyA = 0f;
            float energyB = 0f;
            for (int i = 0; i < sampleCount - lag; i++)
            {
                float a = audioSamples[i * 2] - mean;
                float b = audioSamples[(i + lag) * 2] - mean;
                correlation += a * b;
                energyA += a * a;
                energyB += b * b;
            }

            float denominator = Mathf.Sqrt(energyA * energyB);
            float normalizedCorrelation = denominator > 0f
                ? correlation / denominator
                : 0f;
            if (normalizedCorrelation > bestCorrelation)
            {
                bestCorrelation = normalizedCorrelation;
                bestLag = lag;
            }
        }

        if (bestLag == 0 || bestCorrelation < 0.25f)
        {
            audioPitchAccent = Mathf.Lerp(audioPitchAccent, 0f, 0.3f);
            return audioPitchAccent;
        }

        float frequency = effectiveSampleRate / bestLag;
        float targetAccent = Mathf.Clamp((frequency - 175f) / 175f, -1f, 1f);
        audioPitchAccent = Mathf.Lerp(audioPitchAccent, targetAccent, 0.3f);
        return audioPitchAccent;
    }

    private struct MouthPose
    {
        public readonly float width;
        public readonly float opening;
        public readonly float pout;
        public readonly bool showTeeth;

        public MouthPose(float width, float opening, float pout, bool showTeeth = false)
        {
            this.width = width;
            this.opening = opening;
            this.pout = pout;
            this.showTeeth = showTeeth;
        }
    }

    private static MouthPose GetMouthPose(AvatarViseme viseme)
    {
        switch (viseme)
        {
            case AvatarViseme.OpenRound:
            case AvatarViseme.RhubarbA:
            case AvatarViseme.RhubarbD:
                return new MouthPose(0.048f, 0.018f, 0.002f);
            case AvatarViseme.MidSpread:
            case AvatarViseme.RhubarbC:
            case AvatarViseme.RhubarbE:
                return new MouthPose(0.068f, 0.009f, 0f, true);
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:
                return new MouthPose(0.060f, 0.001f, 0f);
            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
            case AvatarViseme.RhubarbTh:
                return new MouthPose(0.058f, 0.004f, 0f, true);
            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:
                return new MouthPose(0.036f, 0.007f, 0.009f);
            case AvatarViseme.RhubarbDd:
            case AvatarViseme.RhubarbNn:
            case AvatarViseme.RhubarbSs:
            case AvatarViseme.RhubarbCh:
                return new MouthPose(0.062f, 0.006f, 0f, true);
            case AvatarViseme.RhubarbRr:
            case AvatarViseme.RhubarbKk:
            case AvatarViseme.RhubarbG:
            case AvatarViseme.Consonant:
                return new MouthPose(0.058f, 0.008f, 0f);
            default:
                return new MouthPose(0.064f, 0.001f, 0f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════

    private static float ToAngle(AvatarViseme v)
    {
        switch (v)
        {
            case AvatarViseme.OpenRound:
            case AvatarViseme.RhubarbA:
            case AvatarViseme.RhubarbD:  return 8f;
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
            case AvatarViseme.RhubarbG:  return  2f;
            case AvatarViseme.TeethOnLip:
            case AvatarViseme.RhubarbF:
            case AvatarViseme.RhubarbTh: return  2f;
            case AvatarViseme.Pucker:
            case AvatarViseme.RhubarbH:  return  2f;
            case AvatarViseme.Closed:
            case AvatarViseme.RhubarbB:  return  0f;
            default:                     return  3f;
        }
    }

    private AvatarViseme GetViseme(float t)
    {
        for (int i = 1; i < timeline.Count; i++)
            if (t < timeline[i].time) return timeline[i - 1].viseme;
        return timeline.Count > 0 ? timeline[timeline.Count - 1].viseme : AvatarViseme.Rest;
    }

    private static Mesh ParseOBJ(string txt)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        foreach (string raw in txt.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("v "))
            {
                var p = line.Split(new[]{' '}, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4)
                    verts.Add(new Vector3(
                        float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (line.StartsWith("f "))
            {
                var p = line.Split(new[]{' '}, System.StringSplitOptions.RemoveEmptyEntries);
                int[] idx = new int[p.Length - 1];
                for (int i = 0; i < idx.Length; i++)
                    idx[i] = int.Parse(p[i + 1].Split('/')[0]) - 1;
                for (int i = 1; i < idx.Length - 1; i++)
                { tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]); }
            }
        }
        if (verts.Count == 0 || tris.Count == 0) return null;
        Mesh m = new Mesh { name = "MaskMesh" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private Vector3 ToHeadLocal(Vector3 blenderRootPoint)
    {
        // Blender Z-up to Unity Y-up, with Blender's front (-Y) facing Unity +Z.
        Vector3 unityRootPoint = new Vector3(
            blenderRootPoint.x,
            blenderRootPoint.z,
            -blenderRootPoint.y);
        return avatarHeadBone.InverseTransformPoint(avatarRoot.TransformPoint(unityRootPoint));
    }

    private bool ConformUpperMaskToAvatarFace(Mesh topMesh, Mesh jawMesh, out Material skinMaterial)
    {
        skinMaterial = null;
        SkinnedMeshRenderer faceRenderer = null;
        foreach (SkinnedMeshRenderer candidate in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (string.Equals(candidate.gameObject.name, "AvatarHead",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                faceRenderer = candidate;
                break;
            }
        }

        if (faceRenderer == null)
        {
            Debug.LogError("[Mask] Plasa AvatarHead nu a fost găsită pentru potrivirea părții superioare.");
            return false;
        }

        if (faceRenderer.sharedMaterial != null)
        {
            skinMaterial = new Material(faceRenderer.sharedMaterial)
            {
                name = "Avatar Mask Skin Texture"
            };
            if (skinMaterial.HasProperty("_Cull"))
                skinMaterial.SetFloat("_Cull", 0f);
        }
        else
        {
            skinMaterial = MakeMat(new Color(0.68f, 0.49f, 0.42f, 1f));
        }

        Mesh bakedFace = new Mesh { name = "Baked Avatar Face" };
        try
        {
            faceRenderer.BakeMesh(bakedFace);
            Vector3[] faceVertices = bakedFace.vertices;
            Vector2[] faceUvs = faceRenderer.sharedMesh.uv;
            for (int i = 0; i < faceVertices.Length; i++)
                faceVertices[i] = avatarRoot.InverseTransformPoint(
                    faceRenderer.transform.TransformPoint(faceVertices[i]));

            bool hasFaceUvs = faceUvs != null && faceUvs.Length == faceVertices.Length;
            List<Vector2> eyeCenters = GetEyeCentersInAvatarRoot();
            int[] faceTriangles = bakedFace.triangles;
            Vector3[] topVertices = topMesh.vertices;
            Vector3[] rootTopVertices = ConvertMaskVerticesToAvatarRoot(topVertices);

            int[] maskTriangles = topMesh.triangles;
            var visibleTriangles = new List<int>(maskTriangles.Length);
            int removedEyeTriangles = 0;
            for (int i = 0; i < maskTriangles.Length; i += 3)
            {
                int a = maskTriangles[i];
                int b = maskTriangles[i + 1];
                int c = maskTriangles[i + 2];
                Vector3 center = (rootTopVertices[a] + rootTopVertices[b] +
                                  rootTopVertices[c]) / 3f;
                if (IsEyeOpening(center, eyeCenters))
                {
                    removedEyeTriangles++;
                    continue;
                }

                visibleTriangles.Add(a);
                visibleTriangles.Add(b);
                visibleTriangles.Add(c);
            }

            Vector2[] topUvs = ConformMaskVertices(
                topVertices, rootTopVertices, Vector3.zero, 0.008f,
                faceVertices, faceTriangles, faceUvs, hasFaceUvs);
            topMesh.vertices = topVertices;
            topMesh.triangles = visibleTriangles.ToArray();
            if (hasFaceUvs)
                topMesh.uv = topUvs;
            topMesh.RecalculateNormals();
            topMesh.RecalculateBounds();

            Vector3[] jawVertices = jawMesh.vertices;
            Vector3[] rootJawVertices = ConvertMaskVerticesToAvatarRoot(jawVertices);
            Vector2[] jawUvs = ConformMaskVertices(
                jawVertices, rootJawVertices, jawPivotLocal, 0.006f,
                faceVertices, faceTriangles, faceUvs, hasFaceUvs);
            jawMesh.vertices = jawVertices;
            if (hasFaceUvs)
                jawMesh.uv = jawUvs;
            jawMesh.RecalculateNormals();
            jawMesh.RecalculateBounds();

            if (eyeCenters.Count > 0 && removedEyeTriangles == 0)
                Debug.LogWarning("[Mask] Nu s-au format deschideri pentru ochi; verifică alinierea măștii.");
            return true;
        }
        finally
        {
            Destroy(bakedFace);
        }
    }

    private Vector3[] ConvertMaskVerticesToAvatarRoot(Vector3[] vertices)
    {
        var rootVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            rootVertices[i] = new Vector3(vertices[i].x, vertices[i].z, -vertices[i].y);
        return rootVertices;
    }

    private Vector2[] ConformMaskVertices(
        Vector3[] localVertices,
        Vector3[] rootVertices,
        Vector3 localPivot,
        float surfaceOffset,
        Vector3[] faceVertices,
        int[] faceTriangles,
        Vector2[] faceUvs,
        bool projectUvs)
    {
        var maskUvs = projectUvs ? new Vector2[localVertices.Length] : null;
        for (int i = 0; i < localVertices.Length; i++)
        {
            Vector3 rootPoint = rootVertices[i];
            bool foundSurface = TryGetFrontSurface(
                rootPoint, faceVertices, faceTriangles, faceUvs,
                out float surfaceZ, out Vector2 surfaceUv);
            if (foundSurface)
                rootPoint.z = Mathf.Max(rootPoint.z, surfaceZ + surfaceOffset);
            else if (projectUvs)
                surfaceUv = GetNearestFaceUv(rootPoint, faceVertices, faceUvs);

            Vector3 headLocal = avatarHeadBone.InverseTransformPoint(
                avatarRoot.TransformPoint(rootPoint));
            localVertices[i] = headLocal - localPivot;
            if (projectUvs)
                maskUvs[i] = surfaceUv;
        }

        return maskUvs;
    }

    private static Vector2 GetNearestFaceUv(
        Vector3 point, Vector3[] faceVertices, Vector2[] faceUvs)
    {
        int nearest = 0;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < faceVertices.Length; i++)
        {
            float distance = (new Vector2(faceVertices[i].x, faceVertices[i].y) -
                              new Vector2(point.x, point.y)).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = i;
        }
        return faceUvs[nearest];
    }

    private List<Vector2> GetEyeCentersInAvatarRoot()
    {
        var centers = new List<Vector2>(2);
        foreach (Renderer candidate in avatarRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!candidate.name.Equals("AvatarLeftEyeball",
                    System.StringComparison.OrdinalIgnoreCase) &&
                !candidate.name.Equals("AvatarRightEyeball",
                    System.StringComparison.OrdinalIgnoreCase))
                continue;

            Vector3 center = avatarRoot.InverseTransformPoint(candidate.bounds.center);
            centers.Add(new Vector2(center.x, center.y));
        }

        if (centers.Count < 2)
            Debug.LogWarning("[Mask] Nu au fost găsite ambele globuri oculare pentru deschiderile măștii.");
        return centers;
    }

    private static bool IsEyeOpening(Vector3 triangleCenter, List<Vector2> eyeCenters)
    {
        const float eyeRadiusX = 0.014f;
        const float eyeRadiusY = 0.012f;
        foreach (Vector2 eyeCenter in eyeCenters)
        {
            float x = (triangleCenter.x - eyeCenter.x) / eyeRadiusX;
            float y = (triangleCenter.y - eyeCenter.y) / eyeRadiusY;
            if (x * x + y * y < 1f) return true;
        }
        return false;
    }

    private static bool TryGetFrontSurface(
        Vector3 point,
        Vector3[] faceVertices,
        int[] faceTriangles,
        Vector2[] faceUvs,
        out float surfaceZ,
        out Vector2 surfaceUv)
    {
        surfaceZ = float.NegativeInfinity;
        surfaceUv = Vector2.zero;
        bool foundSurface = false;

        for (int i = 0; i < faceTriangles.Length; i += 3)
        {
            int indexA = faceTriangles[i];
            int indexB = faceTriangles[i + 1];
            int indexC = faceTriangles[i + 2];
            Vector3 a = faceVertices[indexA];
            Vector3 b = faceVertices[indexB];
            Vector3 c = faceVertices[indexC];
            float denominator = (b.y - c.y) * (a.x - c.x) +
                                (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < 0.000001f)
                continue;

            float weightA = ((b.y - c.y) * (point.x - c.x) +
                             (c.x - b.x) * (point.y - c.y)) / denominator;
            float weightB = ((c.y - a.y) * (point.x - c.x) +
                             (a.x - c.x) * (point.y - c.y)) / denominator;
            float weightC = 1f - weightA - weightB;
            if (weightA < -0.0001f || weightB < -0.0001f || weightC < -0.0001f)
                continue;

            float candidateZ = weightA * a.z + weightB * b.z + weightC * c.z;
            if (foundSurface && candidateZ <= surfaceZ) continue;

            surfaceZ = candidateZ;
            if (faceUvs != null && faceUvs.Length == faceVertices.Length)
                surfaceUv = weightA * faceUvs[indexA] +
                            weightB * faceUvs[indexB] +
                            weightC * faceUvs[indexC];
            foundSurface = true;
        }

        return foundSurface;
    }

    private static Material MakeMat(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Unlit/Color");
        if (sh == null) return null;

        Material mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
        if (mat.HasProperty("_Cull"))      mat.SetFloat("_Cull", 0f);
        return mat;
    }

    private void OnDestroy()
    {
        if (maskRoot != null)
        {
            foreach (MeshFilter meshFilter in maskRoot.GetComponentsInChildren<MeshFilter>())
                if (meshFilter.sharedMesh != null &&
                    (meshFilter.gameObject.name == "HeadTop" ||
                     meshFilter.gameObject.name == "HeadJaw"))
                    Destroy(meshFilter.sharedMesh);

            Destroy(maskRoot.gameObject);
        }
        DestroyMaterials();
    }

    private void DestroyMaterials()
    {
        if (maskMaterial != null) Destroy(maskMaterial);
        if (upperMaskMaterial != null && upperMaskMaterial != maskMaterial)
            Destroy(upperMaskMaterial);
        if (lipMaterial != null) Destroy(lipMaterial);
        if (mouthMaterial != null) Destroy(mouthMaterial);
        if (teethMaterial != null) Destroy(teethMaterial);
    }
}
