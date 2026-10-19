using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class NarrativeSliceAssets
{
    public static void Prepare()
    {
        const string path = "Assets/Resources/NarrativeAssets.asset";
        var assets = AssetDatabase.LoadAssetAtPath<NarrativeAssets>(path);
        if (assets == null)
        {
            assets = ScriptableObject.CreateInstance<NarrativeAssets>();
            AssetDatabase.CreateAsset(assets, path);
        }
        assets.room = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Backgrounds/NightFlat/unwalled-room-v1.png");
        assets.book = AssetDatabase.LoadAllAssetsAtPath("Assets/MyObjAssets/gameObject/Art/scobj/过关道具.png")
            .OfType<Sprite>().Single(s => s.name == "过关道具_9");
        if (assets.room == null || assets.book == null) throw new Exception("Narrative art is missing");
        EditorUtility.SetDirty(assets);
        AssetDatabase.SaveAssets();
        Debug.Log("NARRATIVE_ASSETS_READY");
    }

    public static void AuditNarrativeAnchors()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/Designed/L04_FoldedWells.unity");
            var cells = UnityEngine.Object.FindObjectsOfType<MapCell>().Where(c => c.WorldRect.height <= 3.3f && c.WorldRect.width >= 10f).ToArray();
            if(cells.Length != 10) throw new Exception("Expected all ten corridor observation cells, found " + cells.Length);
            Debug.Log("NARRATIVE_ANCHORS_PASS corridor=" + cells.Length);
            EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/Designed/L03_ElectricTraverse.unity");
            if(UnityEngine.Object.FindObjectsOfType<FixedContactHazard>().Length < 3 || UnityEngine.Object.FindObjectsOfType<PhysicalBox>().Length < 3)
                throw new Exception("Sliding-box observation sources missing");
            Debug.Log("NARRATIVE_ANCHORS_PASS sliding_boxes");
        }
        finally
        {
            if(previous.Any(s => s.isLoaded && s.isActive) && previous.All(s => !string.IsNullOrEmpty(s.path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null))
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            else EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity");
        }
    }
}
