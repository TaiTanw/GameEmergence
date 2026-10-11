#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MechanismCart))]
public sealed class MechanismCartEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            Draw("direction", "固定方向"); Draw("speed", "水平速度");
            Draw("initiallyOn", "默认开启"); Draw("interactionReach", "交互间隙");
            DrawPropertiesExcluding(serializedObject, "direction", "speed", "initiallyOn", "interactionReach");
        }
        serializedObject.ApplyModifiedProperties();
        if (!((MechanismCart)target).ValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        EditorGUILayout.HelpBox("玩家点按切换开关。小车保持世界渲染，碰撞框单独排除 Player；关闭驱动后由原实体物理处理。", MessageType.Info);
    }
    void Draw(string property, string label) => EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label));
}

[CustomEditor(typeof(MechanismPressurePlate))]
public sealed class MechanismPressurePlateEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("kind"), new GUIContent("压感类型"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("target"), new GUIContent("控制目标组件"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("topTolerance"), new GUIContent("脚底高度容差"));
        }
        serializedObject.ApplyModifiedProperties();
        MechanismPressurePlate plate = (MechanismPressurePlate)target;
        if (!plate.ValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        EditorGUILayout.LabelField("有效压住对象", plate.OccupantCount.ToString());
        EditorGUILayout.HelpBox("薄触发区须贴近实心地面，让脚底进入；压力板不提供承载或阻挡。普通压感认玩家、箱子、小车；带电压感只认带电箱，本身不是电击来源。每个目标只允许一个机关。", MessageType.Info);
    }
}

[CustomEditor(typeof(FixedElectricHazard))]
public sealed class FixedElectricHazardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(FixedElectricHazard.ReadOnlyDescription + "。实心与触发器均有效，普通箱不导电。", MessageType.Info);
        if (!((FixedElectricHazard)target).ValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
    }
}

public static class MechanismTargetGizmos
{
    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    static void Draw(Taijie target, GizmoType flags)
    {
        // 制作坐标来自当前场景默认位置；运行中的目标已移动，不重画一个漂移的终点。
        if (Application.isPlaying) return;
        var data = new SerializedObject(target);
        Vector3 end = target.transform.position + (Vector3)data.FindProperty("mechanismOffset").vector2Value;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(target.transform.position, end); Gizmos.DrawWireSphere(end, .15f);
        Handles.Label(end, "机关终点（默认位置＋世界偏移）");
    }
}
#endif
