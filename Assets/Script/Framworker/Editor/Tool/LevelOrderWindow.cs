using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>把场景顺序拖好后，手动生成到 Resources/LevelOrder.json。不写场景里的教学和出生点。</summary>
public class LevelOrderWindow : EditorWindow
{
    readonly List<SceneAsset> scenes = new List<SceneAsset>();
    sealed class EndingRow
    {
        public SceneAsset scene;
        public EndingAxisRange x = new EndingAxisRange();
        public EndingAxisRange y = new EndingAxisRange();
    }
    readonly List<EndingRow> endings = new List<EndingRow>();
    ReorderableList list;
    ReorderableList endingList;
    Vector2 scroll;
    string intersectionReport;
    bool intersectionsValid;
    static readonly string[] BoundLabels = { "无界", "开", "闭" };
    const string OutputPath = "Assets/Resources/LevelOrder.json";

    [MenuItem("自定义工具/开发工具/关卡/关卡顺序")]
    static void Open() => GetWindow<LevelOrderWindow>("关卡顺序");

    [MenuItem("自定义工具/测试工具/进度/清空关卡进度")]
    static void ClearProgress()
    {
        DataAndInitMgr.Instance.ClearLevelProgress();
        Debug.Log("已清空关卡进度。下次进入从第一关开始。");
    }

    void OnEnable()
    {
        minSize = new Vector2(560, 380);
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
        endingList = new ReorderableList(endings, typeof(EndingRow), true, true, true, true);
        endingList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "结局场景与二维条件（X、Y 同时满足，必须唯一命中）");
        endingList.onAddCallback = _ => endings.Add(new EndingRow());
        endingList.elementHeight = (EditorGUIUtility.singleLineHeight + 4) * 4;
        endingList.drawElementCallback = (rect, index, active, focused) =>
        {
            if (index < 0 || index >= endings.Count) return;
            EndingRow row = endings[index];
            rect.height = EditorGUIUtility.singleLineHeight;
            row.scene = (SceneAsset)EditorGUI.ObjectField(rect, "结局 " + (index + 1), row.scene, typeof(SceneAsset), false);
            rect.y += rect.height + 4;
            DrawRange(rect, "X", row.x);
            rect.y += rect.height + 4;
            DrawRange(rect, "Y", row.y);
            rect.y += rect.height + 4;
            EditorGUI.LabelField(rect, $"X ∈ {row.x}    Y ∈ {row.y}");
        };
    }

    static void DrawRange(Rect rect, string axis, EndingAxisRange range)
    {
        if (range == null) { EditorGUI.LabelField(rect, axis + " 区间缺失，请删除后重新配置此行。"); return; }
        EditorGUI.LabelField(new Rect(rect.x, rect.y, 22, rect.height), axis);
        float width = (rect.width - 30) / 2;
        DrawBound(new Rect(rect.x + 26, rect.y, width - 4, rect.height), "下界", ref range.lowerKind, ref range.lower);
        DrawBound(new Rect(rect.x + 26 + width, rect.y, width - 4, rect.height), "上界", ref range.upperKind, ref range.upper);
    }

    static void DrawBound(Rect rect, string label, ref EndingBoundKind kind, ref float value)
    {
        EditorGUI.LabelField(new Rect(rect.x, rect.y, 32, rect.height), label);
        kind = (EndingBoundKind)EditorGUI.Popup(new Rect(rect.x + 34, rect.y, 54, rect.height), (int)kind, BoundLabels);
        using (new EditorGUI.DisabledScope(kind == EndingBoundKind.Unbounded))
            value = EditorGUI.FloatField(new Rect(rect.x + 92, rect.y, rect.width - 92, rect.height), value);
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUI.BeginChangeCheck();
        list.DoLayoutList();
        endingList.DoLayoutList();
        if (EditorGUI.EndChangeCheck()) intersectionReport = null;
        if (GUILayout.Button("检查结局区间交集"))
        {
            intersectionsValid = BuildEndingConditions().CheckEndingIntersections(out intersectionReport);
        }
        if (!string.IsNullOrEmpty(intersectionReport))
            EditorGUILayout.HelpBox(intersectionReport, intersectionsValid ? MessageType.Info : MessageType.Error);
        EditorGUILayout.HelpBox("下界无界表示负无穷，上界无界表示正无穷。交集检查只排查重叠，不保证所有向量都有结局。", MessageType.None);
        if (GUILayout.Button("生成到固定文件"))
            Write();
        if (GUILayout.Button("清空关卡进度"))
            ClearProgress();
        EditorGUILayout.EndScrollView();
    }

    void LoadExisting()
    {
        scenes.Clear();
        endings.Clear();
        // 安全：还没有顺序文件时保持空列表。
        if (!File.Exists(OutputPath)) return;
        LevelOrderFile file = JsonUtility.FromJson<LevelOrderFile>(File.ReadAllText(OutputPath));
        if (file == null) return;
        foreach (string sceneName in file.scenes ?? new string[0])
        {
            if (string.IsNullOrEmpty(sceneName)) continue;
            SceneAsset scene = FindScene(sceneName);
            // 安全：找不到的名字留空槽，避免生成时悄悄丢掉。
            if (scene == null) Debug.LogError("顺序文件里的场景不在工程中：" + sceneName);
            scenes.Add(scene);
        }
        foreach (EndingSceneEntry entry in file.endings ?? new EndingSceneEntry[0])
        {
            if (entry == null) { endings.Add(new EndingRow()); continue; }
            SceneAsset scene = FindScene(entry.scene);
            if (scene == null) Debug.LogError("顺序文件里的结局场景不在工程中：" + entry.scene);
            endings.Add(new EndingRow { scene = scene, x = entry.x, y = entry.y });
        }
    }

    static SceneAsset FindScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return null;
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
        if (!TryBuildOrder(out LevelOrderFile file, out string error)) { Debug.LogError(error); return; }
        if (!file.CheckEndingIntersections(out error)) { Debug.LogError(error); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, JsonUtility.ToJson(file, true));
        AssetDatabase.Refresh();
        Debug.Log("关卡顺序与结局条件已写入 " + OutputPath);
    }

    bool TryBuildOrder(out LevelOrderFile file, out string error)
    {
        file = new LevelOrderFile();
        error = null;
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
        var allScenes = new List<SceneAsset>(scenes);
        foreach (EndingRow row in endings) allScenes.Add(row.scene);
        foreach (SceneAsset scene in allScenes)
        {
            // 安全：有空槽时整次不写，避免空表覆盖已有顺序。
            if (scene == null)
            {
                error = "列表里有空槽，没有写入顺序文件。";
                return false;
            }
            // 运行时使用场景名，构建未启用或同名的场景不能安全写入。
            if (!enabledPaths.Contains(AssetDatabase.GetAssetPath(scene)))
            {
                error = "场景未在 Build Settings 中启用，没有写入顺序文件：" + scene.name;
                return false;
            }
            if (ambiguousNames.Contains(scene.name))
            {
                error = "Build Settings 有同名场景，不能按名字加载，没有写入顺序文件：" + scene.name;
                return false;
            }
        }
        if (scenes.Count == 0)
        {
            error = "没有线性场景，不覆盖已有顺序。";
            return false;
        }
        foreach (SceneAsset scene in scenes) names.Add(scene.name);
        file.scenes = names.ToArray();
        file.endings = BuildEndingConditions().endings;
        return file.ValidateEndings(out error);
    }

    LevelOrderFile BuildEndingConditions()
    {
        var entries = new List<EndingSceneEntry>();
        foreach (EndingRow row in endings)
            entries.Add(new EndingSceneEntry { scene = row.scene != null ? row.scene.name : null, x = row.x, y = row.y });
        return new LevelOrderFile { endings = entries.ToArray() };
    }
}
