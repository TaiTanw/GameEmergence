using UnityEditor;
using UnityEngine;

/// <summary>
/// 角色持续资源的只读调试窗口。
/// 运行时读取玩家事实，不改体力、氧气、开关或速率倍率。
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
        EditorGUILayout.HelpBox("窗口只读。道具效果未落地前，资源显示当前基础数；死亡显示待处理或处理完成，不能代替完整阶段验收。", MessageType.Info);

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
            GUILayout.Label("角色与水", EditorStyles.boldLabel);
            EditorGUILayout.TextField("身份", player.Identity == PlayerIdentity.Body ? "实体" : "精神体");
            EditorGUILayout.Toggle("四向能力可用", player.ResolveFourWayAbility() != FourWayMode.None);
            EditorGUILayout.Toggle("正在游泳", player._ActionData != null && player._ActionData.isSwimming);
            EditorGUILayout.Toggle("水面泳姿", player._ActionData != null && player._ActionData.isSurfaceSwimming);
            EditorGUILayout.Toggle("正在飞行", player._ActionData != null && player._ActionData.isFlying);
            EditorGUILayout.Toggle("几何淹没", player.WaterFacts.submerged);
            EditorGUILayout.Toggle("几何半露出水面", player.WaterFacts.partlyAboveSurface);
            EditorGUILayout.Toggle("水面可提供游泳", player.WaterFacts.surfaceSwimmingAvailable);
            EditorGUILayout.FloatField("水面高度", player.WaterFacts.surfaceY);
            EditorGUILayout.Toggle("水可提供游泳", player.WaterFacts.swimmingAvailable);
            EditorGUILayout.Toggle("运动更新许可", player.Permissions.motionUpdates);
            EditorGUILayout.Toggle("资源更新许可", player.Permissions.resourceUpdates);
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
            EditorGUILayout.Space();
            GUILayout.Label("氧气", EditorStyles.boldLabel);
            EditorGUILayout.FloatField("当前", resources.oxygen);
            EditorGUILayout.FloatField("上限", resources.oxygenMax);
            EditorGUILayout.Toggle("已经耗尽", resources.oxygenDepleted);
            EditorGUILayout.TextField("死亡请求", player.DeathStatus == PlayerDeathStatus.Pending ? "等待阶段系统"
                : player.DeathStatus == PlayerDeathStatus.Rejected ? "阶段不接受此角色" : "无待处理请求");
        }
        EditorGUILayout.EndScrollView();
    }
}
