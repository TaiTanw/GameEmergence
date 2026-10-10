#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>只生成可手工摆放的空格子预设，不生成规则网格或修改关卡场景。</summary>
public static class MapCellPrefabBuilder
{
    [MenuItem("自定义工具/开发工具/地图/生成空格子预设")]
    public static void Build()
    {
        const string folder = "Assets/MyObjAssets/gameObject/Pf/Map";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/MyObjAssets/gameObject/Pf", "Map");
        var item = new GameObject("MapCell");
        try
        {
            item.AddComponent<MapCell>();
            PrefabUtility.SaveAsPrefabAsset(item, folder + "/MapCell.prefab");
            AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(item); }
    }

    public static void RunForBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
