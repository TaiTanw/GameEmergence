using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>制作时按碰撞体初始形态同步主体精灵；不初始化运行时环境列表，不改材质或几何。</summary>
public static class EnvironmentOpacityTool
{
    const string Menu = "自定义工具/开发工具/环境/按形态同步选中预设不透明度";

    [MenuItem(Menu)]
    static void SynchronizeSelectedPrefabs()
    {
        var sources = new HashSet<BasicPhysicalObject>();
        foreach (GameObject root in Selection.gameObjects)
        {
            if (!EditorUtility.IsPersistent(root) || !AssetDatabase.GetAssetPath(root).EndsWith(".prefab")) continue;
            foreach (BasicPhysicalObject source in root.GetComponentsInChildren<BasicPhysicalObject>(true))
                sources.Add(source);
        }
        Synchronize(sources);
    }

    [MenuItem(Menu, true)]
    static bool CanSynchronizeSelectedPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        foreach (GameObject root in Selection.gameObjects)
            if (EditorUtility.IsPersistent(root) && AssetDatabase.GetAssetPath(root).EndsWith(".prefab")) return true;
        return false;
    }

    public static void Synchronize(IEnumerable<BasicPhysicalObject> sources)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Undo.SetCurrentGroupName("同步环境形态不透明度");
        int synchronized = 0, skipped = 0;
        foreach (BasicPhysicalObject source in sources)
        {
            if (!TrySynchronize(source, out string error))
            {
                skipped++;
                Debug.LogWarning($"跳过环境不透明度同步：{source?.name}，{error}", source);
                continue;
            }
            synchronized++;
            if (EditorUtility.IsPersistent(source)) AssetDatabase.SaveAssetIfDirty(source);
        }
        Debug.Log($"环境不透明度同步：已处理 {synchronized} 个，跳过 {skipped} 个。场景对象请手动保存；主水体共用预设时请核对实例颜色覆盖。");
    }

    internal static bool TrySynchronize(BasicPhysicalObject source, out string error)
    {
        error = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode || source == null)
        { error = "仅在编辑模式操作有效环境。"; return false; }
        // 以明确的主水引用排除，不依赖 Current 注册或对象是否位于活动场景。
        foreach (SceneLevelSetup setup in Resources.FindObjectsOfTypeAll<SceneLevelSetup>())
            if (setup.RisingWater == source)
            { error = "上涨主水体保留独立外观。"; return false; }

        Collider2D[] geometry = source.GetComponents<Collider2D>();
        SpriteRenderer visual = source.GetComponent<SpriteRenderer>();
        if (source.GetComponents<BasicPhysicalObject>().Length != 1 || geometry.Length == 0 || visual == null)
        { error = "需要唯一环境宿主、碰撞体及同物体主体精灵。"; return false; }
        bool trigger = geometry[0].isTrigger;
        foreach (Collider2D shape in geometry)
            if (shape.isTrigger != trigger)
            { error = "混合实心／触发器范围没有单一外观形态。"; return false; }

        Color color = visual.color;
        float opacity = source.GetEnvironmentOpacity(trigger);
        if (color.a == opacity) return true;
        Undo.RecordObject(visual, "同步环境形态不透明度");
        color.a = opacity;
        visual.color = color;
        EditorUtility.SetDirty(visual);
        if (PrefabUtility.IsPartOfPrefabInstance(visual))
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
        if (!EditorUtility.IsPersistent(visual) && visual.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(visual.gameObject.scene);
        return true;
    }
}
