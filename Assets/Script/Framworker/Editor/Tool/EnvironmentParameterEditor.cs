using UnityEditor;
using UnityEngine;

/// <summary>有限性质制作工具；运行中只读，正式修改交给整面板确认入口。</summary>
[CustomEditor(typeof(BasicPhysicalObject), true)]
[CanEditMultipleObjects]
public sealed class EnvironmentParameterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty parameters = serializedObject.FindProperty("environmentParameters");
        SerializedProperty legacySlowing = serializedObject.FindProperty("slowingEffect");
        SerializedProperty legacyJump = serializedObject.FindProperty("jumpHeightNum");
        SerializedProperty legacyWall = serializedObject.FindProperty("wallFriction");
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            DrawPropertiesExcluding(serializedObject, "environmentParameters", "slowingEffect", "jumpHeightNum", "wallFriction",
                "colliderOpacity", "triggerOpacity", "environmentCalculationPointLimit", "mechanismOffset", "mechanismTravelTime");
            if (target is Taijie platform)
            {
                EditorGUILayout.LabelField("机关目标位移", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mechanismOffset"), new GUIContent("目标位置偏移（世界 XY）"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mechanismTravelTime"), new GUIContent("往／返缓动时长（秒）"));
                EditorGUILayout.HelpBox(platform.GetComponent<PlatformEffector2D>() != null
                    ? "带平台效应器：可跳上的单向台阶。" : "无平台效应器：实心机关门／四面阻挡物。", MessageType.Info);
                if (!platform.ValidateMechanismConfiguration(out string motionError)) EditorGUILayout.HelpBox(motionError, MessageType.Error);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("environmentCalculationPointLimit"),
                new GUIContent("单槽计算点上限", "此环境两套列表的每个数值槽共用；默认 3，至少 1。"));
            DrawLegacy(parameters, legacySlowing, EnvironmentParameterKind.GroundSlowing, "旧地面消退倍率");
            DrawLegacy(parameters, legacyJump, EnvironmentParameterKind.GroundJumpMultiplier, "旧普通起跳加成");
            DrawLegacy(parameters, legacyWall, EnvironmentParameterKind.WallSlideMultiplier, "旧墙滑下落倍率");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("形态外观", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderOpacity"), new GUIContent("实心不透明度"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("triggerOpacity"), new GUIContent("触发器不透明度"));
            EditorGUILayout.HelpBox("0 为完全透明，1 为完全不透明。制作时用下方按钮同步初始颜色；运行时只在确认切换／恢复形态时使用对应值。", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("环境参数", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("实心／触发器列表各自占槽、共用上限。接触条件固定，每类最多一项；显式项优先，缺项沿旧字段。独特触发器性质由碰撞器默认几何确定。", MessageType.Info);
            for (int i = 0; i < parameters.arraySize; i++)
            {
                SerializedProperty element = parameters.GetArrayElementAtIndex(i);
                var configured = element.managedReferenceValue as EnvironmentParameter;
                EditorGUILayout.PropertyField(element, new GUIContent(configured?.DisplayName ?? "无效配置项"), true);
                if (GUILayout.Button("移除此配置项"))
                {
                    parameters.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            AddButton(parameters, EnvironmentParameterKind.GroundSlowing,
                new GroundSlowingParameter(legacySlowing != null ? legacySlowing.floatValue : 1f));
            AddButton(parameters, EnvironmentParameterKind.GroundJumpMultiplier,
                new GroundJumpMultiplierParameter(1f + (legacyJump != null ? legacyJump.floatValue : 0f)));
            AddButton(parameters, EnvironmentParameterKind.WallSlideMultiplier,
                new WallSlideMultiplierParameter(legacyWall != null ? legacyWall.floatValue : 1f));
            AddButton(parameters, EnvironmentParameterKind.WaterSwimming, new WaterSwimmingParameter());
            AddButton(parameters, EnvironmentParameterKind.MovementSpeedOffset, new MovementSpeedOffsetParameter());
            AddButton(parameters, EnvironmentParameterKind.PassiveVelocity, new PassiveVelocityParameter());
        }
        serializedObject.ApplyModifiedProperties();

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("按当前碰撞形态同步不透明度"))
            {
                var sources = new System.Collections.Generic.List<BasicPhysicalObject>();
                foreach (Object item in targets) if (item is BasicPhysicalObject source) sources.Add(source);
                EnvironmentOpacityTool.Synchronize(sources);
            }

        foreach (Object item in targets)
            if (item is BasicPhysicalObject host && !host.ValidateEnvironmentParameters(out string error))
                EditorGUILayout.HelpBox(error, MessageType.Error);

        if (!Application.isPlaying || targets.Length != 1) return;
        var runtimeHost = (BasicPhysicalObject)target;
        DrawRuntime(runtimeHost, false);
        DrawRuntime(runtimeHost, true);
    }

    private static void DrawRuntime(BasicPhysicalObject host, bool trigger)
    {
        EditorGUILayout.LabelField(trigger ? "触发器列表" : "实心列表", EditorStyles.boldLabel);
        EnvironmentParameter[] slots = host.CopyEnvironmentSlots(trigger);
        for (int i = 0; i < slots.Length; i++)
        {
            EnvironmentParameter parameter = slots[i];
            EditorGUILayout.LabelField($"槽 {i + 1}：{parameter?.DisplayName ?? "空"}", parameter == null ? ""
                : $"{parameter.ValueText}（{(parameter.CalculationCount > 0 ? parameter.CalculationText : "未附加计算点")}）");
        }
    }

    private void AddButton(SerializedProperty parameters, EnvironmentParameterKind kind, EnvironmentParameter value)
    {
        using (new EditorGUI.DisabledScope(HasConfigured(parameters, kind) || targets.Length != 1))
            if (GUILayout.Button("添加" + value.DisplayName))
            {
                // 显式添加才保存制作项，对应旧值作为初值；其它资产和参数保持。
                int index = parameters.arraySize++;
                parameters.GetArrayElementAtIndex(index).managedReferenceValue = value;
            }
    }

    private static void DrawLegacy(SerializedProperty parameters, SerializedProperty legacy,
        EnvironmentParameterKind kind, string label)
    {
        if (legacy != null && !HasConfigured(parameters, kind))
            EditorGUILayout.PropertyField(legacy, new GUIContent(label + "（未配置该项时使用）"));
    }

    private static bool HasConfigured(SerializedProperty parameters, EnvironmentParameterKind kind)
    {
        for (int i = 0; i < parameters.arraySize; i++)
            if (parameters.GetArrayElementAtIndex(i).managedReferenceValue is EnvironmentParameter value && value.Kind == kind)
                return true;
        return false;
    }
}
