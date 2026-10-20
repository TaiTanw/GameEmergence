#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Keep physical fluid volumes in front of the body without moving the camera.</summary>
public static class P29FluidDepthTools
{
    const float FluidZ = -2f;
    const float BodyZ = -1f;
    const int FluidOrder = 50;
    static readonly string[] Scenes =
    {
        "L00_Orientation", "L01_PropertyLabyrinth", "L02_SequentialGates",
        "L03_ElectricTraverse", "L04_FoldedWells"
    };

    [MenuItem("Tools/P29/Set Fluid and Body Depth")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
        string previous = SceneManager.GetActiveScene().path;
        int prefabCount = 0, sceneCount = 0, volumeCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MyObjAssets/gameObject/Pf" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset || !HasFluid(asset)) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            int changed = SetFluid(contents);
            if (changed != 0)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                prefabCount++;
                volumeCount += changed;
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        try
        {
            foreach (string name in Scenes)
            {
                Scene scene = EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/Designed/" + name + ".unity");
                int changed = scene.GetRootGameObjects().Sum(SetFluid);
                SceneLevelSetup setup = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<SceneLevelSetup>(true)).Single();
                Transform body = setup.transform.Find("BodySpawn");
                if (!Mathf.Approximately(body.position.z, BodyZ))
                {
                    body.position = new Vector3(body.position.x, body.position.y, BodyZ);
                    changed++;
                }
                if (changed == 0) continue;
                volumeCount += changed;
                sceneCount++;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("P29_FLUID_DEPTH_SAVED: prefabs=" + prefabCount + " scenes=" + sceneCount + " changed=" + volumeCount);
    }

    static bool HasFluid(GameObject go) => go.GetComponentInChildren<WaterSurfaceVisual>(true)
        || go.GetComponentInChildren<GasVolumeVisual>(true);

    static int SetFluid(GameObject root)
    {
        int changed = 0;
        foreach (var water in root.GetComponentsInChildren<WaterSurfaceVisual>(true))
            changed += Place(water.transform, water.GetComponent<SpriteRenderer>());
        foreach (var gas in root.GetComponentsInChildren<GasVolumeVisual>(true))
            changed += Place(gas.transform, gas.GetComponent<SpriteRenderer>());
        return changed;
    }

    static int Place(Transform transform, SpriteRenderer renderer)
    {
        if (!renderer) throw new InvalidOperationException(transform.name + " has no body SpriteRenderer.");
        int changed = 0;
        Vector3 position = transform.position;
        if (!Mathf.Approximately(position.z, FluidZ))
        {
            transform.position = new Vector3(position.x, position.y, FluidZ);
            changed++;
        }
        if (renderer.sortingOrder != FluidOrder)
        {
            renderer.sortingOrder = FluidOrder;
            changed++;
        }
        return changed;
    }
}
#endif
