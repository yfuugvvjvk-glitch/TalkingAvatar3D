// Generator de materiale URP pentru GLTFast.
// Folosește direct Universal Render Pipeline/Lit, garantat în build fără shader graphs.

using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using UMat = UnityEngine.Material;

public class UrpMaterialGenerator : IMaterialGenerator
{
    static readonly int PropBaseColor     = Shader.PropertyToID("_BaseColor");
    static readonly int PropBaseMap       = Shader.PropertyToID("_BaseMap");
    static readonly int PropMetallic      = Shader.PropertyToID("_Metallic");
    static readonly int PropSmoothness    = Shader.PropertyToID("_Smoothness");
    static readonly int PropBumpMap       = Shader.PropertyToID("_BumpMap");
    static readonly int PropOcclusion     = Shader.PropertyToID("_OcclusionMap");
    static readonly int PropEmissionMap   = Shader.PropertyToID("_EmissionMap");
    static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");
    static readonly int PropCull          = Shader.PropertyToID("_Cull");
    static readonly int PropSurface       = Shader.PropertyToID("_Surface");
    static readonly int PropAlphaClip     = Shader.PropertyToID("_AlphaClip");
    static readonly int PropCutoff        = Shader.PropertyToID("_Cutoff");

    Shader _shader;
    Shader UrpLit
    {
        get
        {
            if (_shader != null) return _shader;

            // Prima opțiune: prin Shader.Find (funcționează în Editor și în build
            // dacă shader-ul e în AlwaysIncludedShaders sau referit de un material)
            _shader = Shader.Find("Universal Render Pipeline/Lit");
            if (_shader != null) return _shader;

            // A doua opțiune: extragem shader-ul din materialul de referință din Resources
            // Unity include garantat tot ce e în Resources/ în build
            var refMat = Resources.Load<UMat>("UrpLitMaterial");
            if (refMat != null && refMat.shader != null)
            {
                _shader = refMat.shader;
                return _shader;
            }

            // Fallback-uri
            _shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (_shader != null) return _shader;
            _shader = Shader.Find("Standard");
            if (_shader != null) return _shader;
            _shader = Shader.Find("Unlit/Color");
            return _shader;
        }
    }

    public UMat GetDefaultMaterial(bool pointsSupport = false)
    {
        var s = UrpLit;
        if (s == null)
        {
            Debug.LogError("[UrpMaterialGenerator] Niciun shader disponibil în build!");
            return new UMat(Shader.Find("Hidden/InternalErrorShader"));
        }
        return new UMat(s) { name = "glTF Default" };
    }

    public UMat GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf,
        bool pointsSupport = false)
    {
        var s = UrpLit;
        if (s == null)
        {
            Debug.LogError("[UrpMaterialGenerator] Shader null — returnez material de eroare.");
            return new UMat(Shader.Find("Hidden/InternalErrorShader"));
        }
        var mat = new UMat(s);
        if (gltfMaterial == null) return mat;

        mat.name = string.IsNullOrEmpty(gltfMaterial.name) ? "glTF Material" : gltfMaterial.name;

        // ── PBR Metallic Roughness ────────────────────────────────────────
        var pbr = gltfMaterial.PbrMetallicRoughness;
        if (pbr != null)
        {
            mat.SetColor(PropBaseColor, pbr.BaseColor);

            var bcTex = pbr.BaseColorTexture;
            if (bcTex != null)
            {
                var tex = gltf.GetTexture(bcTex.index);
                if (tex != null) mat.SetTexture(PropBaseMap, tex);
            }

            mat.SetFloat(PropMetallic,   Mathf.Clamp01(pbr.metallicFactor));
            mat.SetFloat(PropSmoothness, Mathf.Clamp01(1f - pbr.roughnessFactor));
        }
        else
        {
            mat.SetColor(PropBaseColor, Color.white);
            mat.SetFloat(PropMetallic,   0f);
            mat.SetFloat(PropSmoothness, 0.5f);
        }

        // ── Normal map ───────────────────────────────────────────────────
        var normTex = gltfMaterial.NormalTexture;
        if (normTex != null)
        {
            var tex = gltf.GetTexture(normTex.index);
            if (tex != null)
            {
                mat.SetTexture(PropBumpMap, tex);
                mat.EnableKeyword("_NORMALMAP");
            }
        }

        // ── Occlusion ────────────────────────────────────────────────────
        var occTex = gltfMaterial.OcclusionTexture;
        if (occTex != null)
        {
            var tex = gltf.GetTexture(occTex.index);
            if (tex != null) mat.SetTexture(PropOcclusion, tex);
        }

        // ── Emissive ─────────────────────────────────────────────────────
        Color emissive = gltfMaterial.Emissive;
        if (emissive != Color.black)
        {
            mat.SetColor(PropEmissionColor, emissive);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        var emissTex = gltfMaterial.EmissiveTexture;
        if (emissTex != null)
        {
            var tex = gltf.GetTexture(emissTex.index);
            if (tex != null)
            {
                mat.SetTexture(PropEmissionMap, tex);
                mat.EnableKeyword("_EMISSION");
            }
        }

        // ── Double sided ─────────────────────────────────────────────────
        if (gltfMaterial.doubleSided)
        {
            mat.SetFloat(PropCull, (float)UnityEngine.Rendering.CullMode.Off);
            mat.doubleSidedGI = true;
        }

        // ── Alpha mode ───────────────────────────────────────────────────
        switch (gltfMaterial.GetAlphaMode())
        {
            case MaterialBase.AlphaMode.Blend:
                mat.SetFloat(PropSurface, 1f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                break;
            case MaterialBase.AlphaMode.Mask:
                mat.SetFloat(PropAlphaClip, 1f);
                mat.SetFloat(PropCutoff, gltfMaterial.alphaCutoff);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                break;
            default:
                mat.SetFloat(PropSurface, 0f);
                break;
        }

        return mat;
    }

    public void SetLogger(ICodeLogger logger) { }
}
