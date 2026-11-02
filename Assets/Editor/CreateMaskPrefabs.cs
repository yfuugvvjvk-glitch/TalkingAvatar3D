using UnityEngine;
using UnityEditor;

/// <summary>
/// Creează prefabs pentru head_top și head_jaw din OBJ-urile importate.
/// Menu: Tools → Create Mask Prefabs
/// </summary>
public class CreateMaskPrefabs : Editor
{
    [MenuItem("Tools/Create Mask Prefabs")]
    public static void Create()
    {
        // Caută mesh-urile importate
        string[] guids = AssetDatabase.FindAssets("head_top t:Mesh");
        Mesh topMesh = null;
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            topMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (topMesh != null) { Debug.Log("head_top mesh: " + path); break; }
        }

        guids = AssetDatabase.FindAssets("head_jaw t:Mesh");
        Mesh jawMesh = null;
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            jawMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (jawMesh != null) { Debug.Log("head_jaw mesh: " + path); break; }
        }

        // Material skin
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/UrpLitMaterial.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.87f, 0.75f, 0.65f);
            AssetDatabase.CreateAsset(mat, "Assets/Resources/MaskMaterial.mat");
        }

        // Creează prefab head_top
        if (topMesh != null)
            CreatePrefab("head_top", topMesh, mat);
        else
            Debug.LogError("head_top mesh nu a fost găsit! Verifică că head_top.obj e în Assets/Resources/");

        // Creează prefab head_jaw
        if (jawMesh != null)
            CreatePrefab("head_jaw", jawMesh, mat);
        else
            Debug.LogError("head_jaw mesh nu a fost găsit! Verifică că head_jaw.obj e în Assets/Resources/");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Prefabs create în Assets/Resources/");
    }

    private static void CreatePrefab(string name, Mesh mesh, Material mat)
    {
        GameObject go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;

        string prefabPath = "Assets/Resources/" + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        DestroyImmediate(go);
        Debug.Log("Creat: " + prefabPath);
    }
}
