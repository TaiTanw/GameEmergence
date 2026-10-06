using UnityEditor;
using UnityEngine;

/// <summary>
/// 角色持续资源的只读调试窗口。
/// 运行时读取玩家事实，不改体力、开关或速率倍率。氧气以后加在这一块。
/// </summary>
public class PlayerResourceWindow : EditorWindow
{
    Vector2 scrollPosition;

    [MenuItem("自定义工具/角色资源")]
    public static void ShowWindow()
    {
        GetWindow<PlayerResourceWindow>("角色资源");
    }

    private void OnEnable()
    {
        EditorApplication.update += Repaint;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    private void OnGUI()
    {
        GUILayout.Label("角色资源", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("窗口只读取当前事实，不能改回角色。氧气以后加在这一块。道具效果未落地前，显示的是状态机上的原始数。", MessageType.Info);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("请先进入 Play 模式。", MessageType.Warning);
            return;
        }

        // 实际逻辑：只找场景里已有的管理器。不走 Instance，避免观测时创建持久对象。
        InputControlMgr input = Object.FindObjectOfType<InputControlMgr>();
        Player player = input != null ? input.Player : null;
        if (player == null)
        {
            EditorGUILayout.HelpBox("当前没有受控玩家。", MessageType.Warning);
            return;
        }

        PlayerResourceFacts resources = player.ResourceFacts;
        float speedScale = player._ActionData != null ? player._ActionData.speedScale : 1f;
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Space();
            GUILayout.Label("疾跑", EditorStyles.boldLabel);
            EditorGUILayout.Toggle("玩家按住", resources.sprintHeld);
            EditorGUILayout.Toggle("当前可疾跑", resources.canSprint);
            EditorGUILayout.FloatField("当前速率倍率", speedScale);
            EditorGUILayout.Toggle("因归零关闭", resources.closedBecauseEmpty);

            EditorGUILayout.Space();
            GUILayout.Label("体力", EditorStyles.boldLabel);
            EditorGUILayout.FloatField("当前", resources.stamina);
            EditorGUILayout.FloatField("上限", resources.staminaMax);
        }
        EditorGUILayout.EndScrollView();
    }
}
