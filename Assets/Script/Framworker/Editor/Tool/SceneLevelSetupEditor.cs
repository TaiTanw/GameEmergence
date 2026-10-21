using UnityEditor;
using UnityEngine;

/// <summary>关卡初始背包的有限性质制作入口；不创建运行时背包，不调用局内提交。</summary>
[CustomEditor(typeof(SceneLevelSetup))]
public sealed class SceneLevelSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "cognitionBackpackCapacity", "cognitionPropertyCost",
            "cognitionCalculationCost", "initialCognitionProperties", "initialCognitionCalculations",
            "risingWater", "waterRiseSpeed", "risingWaterMaximum", "escapeAbilities", "lowCognitionMovementMultiplier");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("上涨主水", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("可使用 WaterPoolWithEffects 保留减速／水流。指定主水后禁止玩家编辑。最高点用独立空物体的世界 Y，水面到此停止；普通水池不必关联。", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("risingWater"), new GUIContent("上涨主水"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("waterRiseSpeed"), new GUIContent("上涨速度（世界高度／秒）"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("risingWaterMaximum"), new GUIContent("上涨水最高点"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("认知背包", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("此处配置本关初始内容物，允许携带多个相同性质。初始装载不扣费；运行中只读，局内修改由环境面板统一确认。", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cognitionBackpackCapacity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cognitionPropertyCost"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cognitionCalculationCost"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("initialCognitionCalculations"), true);

            SerializedProperty properties = serializedObject.FindProperty("initialCognitionProperties");
            EditorGUILayout.LabelField("初始性质内容物", EditorStyles.boldLabel);
            for (int i = 0; i < properties.arraySize; i++)
            {
                SerializedProperty element = properties.GetArrayElementAtIndex(i);
                var value = element.managedReferenceValue as EnvironmentParameter;
                EditorGUILayout.PropertyField(element, new GUIContent($"{i + 1} · {value?.DisplayName ?? "空内容物"}"), true);
                if (GUILayout.Button("移除此初始内容物"))
                {
                    properties.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            AddButton(properties, new GroundSlowingParameter());
            AddButton(properties, new GroundJumpMultiplierParameter());
            AddButton(properties, new WallSlideMultiplierParameter());
            AddButton(properties, new WaterSwimmingParameter());
            AddButton(properties, new MovementSpeedOffsetParameter());
            AddButton(properties, new PassiveVelocityParameter());
            AddButton(properties, new GroundMovementMultiplierParameter());
            AddButton(properties, new TriggerEnvironmentParameter());
        }
        serializedObject.ApplyModifiedProperties();
        if (!((SceneLevelSetup)target).ValidateRisingWaterConfiguration(out string error))
            EditorGUILayout.HelpBox(error, MessageType.Error);
        if (!((SceneLevelSetup)target).ValidateEscapePreparation(out string preparationError))
            EditorGUILayout.HelpBox(preparationError, MessageType.Error);
    }

    void OnSceneGUI()
    {
        SceneLevelSetup setup = (SceneLevelSetup)target;
        if (setup.RisingWater == null || setup.RisingWaterMaximum == null) return;
        BoxCollider2D shape = setup.RisingWater.GetComponent<BoxCollider2D>();
        if (shape == null) return;
        Vector3 mark = setup.RisingWaterMaximum.position;
        if (float.IsNaN(mark.y) || float.IsInfinity(mark.y)) return;
        Bounds bounds = shape.bounds;
        Color previous = Handles.color;
        Handles.color = Color.cyan;
        Handles.DrawLine(new Vector3(bounds.min.x, mark.y, setup.RisingWater.transform.position.z),
            new Vector3(bounds.max.x, mark.y, setup.RisingWater.transform.position.z));
        Handles.Label(mark, "最高水面");
        Handles.color = previous;
    }

    static void AddButton(SerializedProperty properties, EnvironmentParameter value)
    {
        if (!GUILayout.Button("添加" + value.DisplayName)) return;
        int index = properties.arraySize++;
        properties.GetArrayElementAtIndex(index).managedReferenceValue = value;
    }
}
