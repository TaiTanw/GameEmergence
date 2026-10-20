#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GasVolumeVisual)), CanEditMultipleObjects]
public sealed class GasVolumeVisualEditor : Editor
{
    bool references;
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("直接拖用 NewPf 下的气体团块。颜色与整体透明度调根 SpriteRenderer；缩放根物体可同时改变雾面和作用范围。外扩雾边包住原判定框，子节点自动跟随，无需手动修改。单独改尺寸时同步根 SpriteRenderer 的 Size 与 BoxCollider2D 的 Size。编辑器显示静态雾形，运行后自动流动。转实心沿原方块显示，有毒预设的固定危险仍然有效。", MessageType.Info);
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("density"), new GUIContent("雾面浓度"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("outerPadding"), new GUIContent("雾边外扩"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("edgeSoftness"), new GUIContent("边缘柔化"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("edgeRoughness"), new GUIContent("边缘起伏"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("noiseScale"), new GUIContent("噪点密度"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("flowSpeed"), new GUIContent("流动速度"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("phaseOffset"), new GUIContent("雾形错位"));
        references = EditorGUILayout.Foldout(references, "预设内部关联（通常不修改）", true);
        if (references)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bodySprite"), new GUIContent("主体精灵"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("range"), new GUIContent("作用范围"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fogSprite"), new GUIContent("独立雾面"));
        }
        serializedObject.ApplyModifiedProperties();
        foreach (Object item in targets)
            if (!((GasVolumeVisual)item).ValidateConfiguration(out string error))
                EditorGUILayout.HelpBox(item.name + "：" + error, MessageType.Error);
    }
}
#endif
