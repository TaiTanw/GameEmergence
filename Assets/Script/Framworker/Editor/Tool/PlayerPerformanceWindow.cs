using UnityEditor;
using UnityEngine;

/// <summary>制作时手动统一两份角色预设的基础运动参数；运行时能力仍读共用效果记录。</summary>
public sealed class PlayerPerformanceWindow : EditorWindow
{
    [SerializeField] GameObject sourcePrefab;
    [SerializeField] GameObject targetPrefab;
    static readonly string[,] Fields = {
        { "speed", "水平移速" }, { "upSpeed", "起跳速度" },
        { "fourWaySpeed", "四向主动速度" }, { "gravity", "重力" },
        { "airResistance", "空气阻力" }, { "envImpact", "环境影响系数" },
        { "horizontalAcceleration", "水平加速度" }
    };

    [MenuItem("自定义工具/开发工具/角色/基础性能统一")]
    static void Open() => GetWindow<PlayerPerformanceWindow>("基础性能统一");

    void OnEnable()
    {
        if (sourcePrefab == null)
            sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab");
        if (targetPrefab == null)
            targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab");
        minSize = new Vector2(390f, 330f);
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("先调整来源预设，再手动同步。默认由本体同步到精神体；身份、碰撞框、探针、动画和阶段权限各自保留。", MessageType.Info);
        sourcePrefab = (GameObject)EditorGUILayout.ObjectField("来源预设", sourcePrefab, typeof(GameObject), false);
        targetPrefab = (GameObject)EditorGUILayout.ObjectField("目标预设", targetPrefab, typeof(GameObject), false);
        CharacterPhysics source = PhysicsOf(sourcePrefab);
        CharacterPhysics target = PhysicsOf(targetPrefab);
        if (source == null || target == null || sourcePrefab == targetPrefab)
        {
            EditorGUILayout.HelpBox("请选择两份不同的预设资源，每份须有且仅有一个 CharacterPhysics。", MessageType.Warning);
            return;
        }

        var sourceData = new SerializedObject(source);
        var targetData = new SerializedObject(target);
        int differences = 0;
        for (int i = 0; i < Fields.GetLength(0); i++)
        {
            SerializedProperty from = sourceData.FindProperty(Fields[i, 0]);
            SerializedProperty to = targetData.FindProperty(Fields[i, 0]);
            if (from == null || to == null || from.propertyType != SerializedPropertyType.Float
                || to.propertyType != SerializedPropertyType.Float)
            {
                EditorGUILayout.HelpBox("参数结构已改变，请先更新工具：" + Fields[i, 0], MessageType.Error);
                return;
            }
            bool different = from.floatValue != to.floatValue;
            if (different) differences++;
            EditorGUILayout.LabelField(Fields[i, 1], from.floatValue.ToString("G5") + " → "
                + to.floatValue.ToString("G5") + (different ? "  （不同）" : ""));
        }
        EditorGUILayout.HelpBox("体力、氧气和跳跃次数目前使用同一状态机的代码默认值；能力倍率运行时共用同一份记录。基础性能统一由人工制作保证。", MessageType.None);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || differences == 0))
            if (GUILayout.Button("将来源基础性能同步到目标（" + differences + " 项差异）"))
                Synchronize(sourceData, targetData);
    }

    static CharacterPhysics PhysicsOf(GameObject prefab)
    {
        // 只操作预设资源，不向场景或播放中的角色写回。
        if (prefab == null || !EditorUtility.IsPersistent(prefab)
            || PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab) return null;
        CharacterPhysics[] components = prefab.GetComponentsInChildren<CharacterPhysics>(true);
        return components.Length == 1 ? components[0] : null;
    }

    void Synchronize(SerializedObject sourceData, SerializedObject targetData)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // 仅复制表中的性能值；ApplyModifiedProperties 提供撤销，不复制整份组件或几何引用。
        Undo.SetCurrentGroupName("统一角色基础性能");
        for (int i = 0; i < Fields.GetLength(0); i++)
            targetData.FindProperty(Fields[i, 0]).floatValue = sourceData.FindProperty(Fields[i, 0]).floatValue;
        if (!targetData.ApplyModifiedProperties()) return;
        AssetDatabase.SaveAssetIfDirty(targetPrefab);
        Debug.Log("已统一角色基础性能：" + sourcePrefab.name + " → " + targetPrefab.name + "。请从开始场景核对模拟与逃生手感。", targetPrefab);
    }
}
