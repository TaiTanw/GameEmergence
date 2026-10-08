using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>把场景顺序拖好后，手动生成到 Resources/LevelOrder.json。不写场景里的教学和出生点。</summary>
public class LevelOrderWindow : EditorWindow
{
    readonly List<SceneAsset> scenes = new List<SceneAsset>();
    ReorderableList list;
    const string OutputPath = "Assets/Resources/LevelOrder.json";

    [MenuItem("自定义工具/关卡顺序")]
    static void Open() => GetWindow<LevelOrderWindow>("关卡顺序");

    [MenuItem("自定义工具/清空关卡进度")]
    static void ClearProgress()
    {
        DataAndInitMgr.Instance.ClearLevelProgress();
        Debug.Log("已清空关卡进度。下次进入从第一关开始。");
    }

    void OnEnable()
    {
        LoadExisting();
        list = new ReorderableList(scenes, typeof(SceneAsset), true, true, true, true);
        list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "从上到下就是通关后的下一关");
        // 实际逻辑：SceneAsset 不能被新出来，加号只补一个空槽。
        list.onAddCallback = _ => scenes.Add(null);
        list.drawElementCallback = (rect, index, active, focused) =>
        {
            // 安全：列表正在增删时跳过越界行。
            if (index < 0 || index >= scenes.Count) return;
            scenes[index] = (SceneAsset)EditorGUI.ObjectField(
                rect, scenes[index], typeof(SceneAsset), false);
        };
    }

    void OnGUI()
    {
        list.DoLayoutList();
        if (GUILayout.Button("生成到固定文件"))
            Write();
        if (GUILayout.Button("清空关卡进度"))
            ClearProgress();
    }

    void LoadExisting()
    {
        scenes.Clear();
        // 安全：还没有顺序文件时保持空列表。
        if (!File.Exists(OutputPath)) return;
        LevelOrderFile file = JsonUtility.FromJson<LevelOrderFile>(File.ReadAllText(OutputPath));
        if (file == null || file.scenes == null) return;
        foreach (string sceneName in file.scenes)
        {
            if (string.IsNullOrEmpty(sceneName)) continue;
            SceneAsset scene = FindScene(sceneName);
            // 安全：找不到的名字留空槽，避免生成时悄悄丢掉。
            if (scene == null) Debug.LogError("顺序文件里的场景不在工程中：" + sceneName);
            scenes.Add(scene);
        }
    }

    static SceneAsset FindScene(string sceneName)
    {
        // 运行时按构建场景名加载，优先找同一份已启用的场景。
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled || Path.GetFileNameWithoutExtension(entry.path) != sceneName) continue;
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:SceneAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (scene != null && scene.name == sceneName) return scene;
        }
        return null;
    }

    void Write()
    {
        var file = new LevelOrderFile();
        var names = new List<string>();
        var enabledPaths = new HashSet<string>();
        var enabledNames = new HashSet<string>();
        var ambiguousNames = new HashSet<string>();
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;
            enabledPaths.Add(entry.path);
            string name = Path.GetFileNameWithoutExtension(entry.path);
            if (!enabledNames.Add(name)) ambiguousNames.Add(name);
        }
        foreach (SceneAsset scene in scenes)
        {
            // 安全：有空槽时整次不写，避免空表覆盖已有顺序。
            if (scene == null)
            {
                Debug.LogError("列表里有空槽，没有写入顺序文件。");
                return;
            }
            // 运行时使用场景名，构建未启用或同名的场景不能安全写入。
            if (!enabledPaths.Contains(AssetDatabase.GetAssetPath(scene)))
            {
                Debug.LogError("场景未在 Build Settings 中启用，没有写入顺序文件：" + scene.name);
                return;
            }
            if (ambiguousNames.Contains(scene.name))
            {
                Debug.LogError("Build Settings 有同名场景，不能按名字加载，没有写入顺序文件：" + scene.name);
                return;
            }
            names.Add(scene.name);
        }
        if (names.Count == 0)
        {
            Debug.LogError("没有场景，不覆盖已有顺序。");
            return;
        }
        file.scenes = names.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, JsonUtility.ToJson(file, true));
        AssetDatabase.Refresh();
        Debug.Log("关卡顺序已写入 " + OutputPath);
    }
}
