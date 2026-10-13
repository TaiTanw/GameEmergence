#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaterSurfaceVisual)), CanEditMultipleObjects]
public sealed class WaterSurfaceVisualEditor : Editor
{
    bool references;
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("调整根 BoxCollider2D 的尺寸／偏移或根 X/Y 正缩放，表现自动跟随。波形参数使用世界单位；主体颜色和排序读取根 SpriteRenderer。教学普通水池无需关联上涨主水。", MessageType.Info);
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "source", "range", "bodySprite", "surfaceMesh", "surfaceRenderer");
        references = EditorGUILayout.Foldout(references, "预设内部关联（通常不修改）", true);
        if (references)
            foreach (string name in new[] { "source", "range", "bodySprite", "surfaceMesh", "surfaceRenderer" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
        serializedObject.ApplyModifiedProperties();
        foreach (Object item in targets)
            if (!((WaterSurfaceVisual)item).ValidateConfiguration(out string error))
                EditorGUILayout.HelpBox(item.name + "：" + error, MessageType.Error);
    }
}
#endif
