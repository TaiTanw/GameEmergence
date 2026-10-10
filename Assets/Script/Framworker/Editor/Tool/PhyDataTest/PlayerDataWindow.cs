// PlayerDataWindow.cs
using PhyData;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EPhy 只读调试窗口。
/// 本工具只读取运行时快照，不注册物理回调、不改写实体状态、不参与解算。
/// </summary>
public class PlayerDataWindow : EditorWindow
{
    BasicEntity targetEntity;
    Vector2 scrollPosition;

    [MenuItem("自定义工具/测试工具/运行观察/EPhy 实时数据显示")]
    public static void ShowWindow()
    {
        GetWindow<PlayerDataWindow>("EPhy Debug");
    }

    private void OnEnable()
    {
        EditorApplication.update += Repaint;
        PhysicsEditorEntityObservation.StartObserving();
        PhysicsEditorContactObservation.StartObserving();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
        PhysicsEditorEntityObservation.StopObserving();
        PhysicsEditorContactObservation.StopObserving();
    }

    private void OnGUI()
    {
        GUILayout.Label("EPhy 只读物理快照", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("窗口只读取调试快照；所有数值均不可回写到物理解算。", MessageType.Info);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("请先进入 Play 模式。", MessageType.Warning);
            return;
        }

        targetEntity = (BasicEntity)EditorGUILayout.ObjectField(
            "目标实体", targetEntity, typeof(BasicEntity), true);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("关联当前玩家"))
        {
            Player player = InputControlMgr.Instance.Player;
            targetEntity = player != null
                ? player.GetComponentInChildren<BasicEntity>()
                : null;
        }

        if (GUILayout.Button("关联当前选择"))
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                targetEntity = selected.GetComponentInParent<BasicEntity>();
                if (targetEntity == null)
                    targetEntity = selected.GetComponentInChildren<BasicEntity>();
            }
        }
        EditorGUILayout.EndHorizontal();

        if (targetEntity == null)
        {
            EditorGUILayout.HelpBox("请选择玩家或任意 PhysicalBox。", MessageType.Warning);
            return;
        }

        EntityPhysicsDebugSnapshot snapshot = PhysicsEditorEntityObservation.GetSnapshot(targetEntity);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        using (new EditorGUI.DisabledScope(true))
        {
            DrawIdentity(snapshot);
            DrawMotion(snapshot);
            DrawGeometry(snapshot);
            DrawEnvironment(snapshot);
        }
        DrawContactPairs(targetEntity);

        EditorGUILayout.EndScrollView();
    }

    static void DrawIdentity(EntityPhysicsDebugSnapshot snapshot)
    {
        EditorGUILayout.Space();
        GUILayout.Label("实体", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("名称", snapshot.entityName);
        EditorGUILayout.LabelField("Fixed Tick", snapshot.fixedTick.ToString());
        string sampledPhase = snapshot.phase == -2
            ? "无样本"
            : snapshot.phase == -1 ? "物理循环开始" : snapshot.phase.ToString();
        EditorGUILayout.LabelField("采样相位", sampledPhase);
        EditorGUILayout.LabelField("Body Type", snapshot.bodyType.ToString());
        DrawVector("实际刚体位置", snapshot.actualPosition);
        EditorGUILayout.LabelField("旋转", snapshot.rotation.ToString("F3"));
        EditorGUILayout.LabelField("角速度", snapshot.angularVelocity.ToString("F3"));
    }

    static void DrawMotion(EntityPhysicsDebugSnapshot snapshot)
    {
        EditorGUILayout.Space();
        GUILayout.Label("原生运动", EditorStyles.boldLabel);
        DrawVector("实际刚体速度", snapshot.planarVelocity);
    }

    static void DrawGeometry(EntityPhysicsDebugSnapshot snapshot)
    {
        EditorGUILayout.Space();
        GUILayout.Label("几何事实", EditorStyles.boldLabel);
        EditorGUILayout.Toggle("着地", snapshot.isGrounded);
        EditorGUILayout.Toggle("顶头", snapshot.isTopBlocked);
        EditorGUILayout.Toggle("左墙", snapshot.isOnLeftWall);
        EditorGUILayout.Toggle("右墙", snapshot.isOnRightWall);
        DrawVector("地面法线", snapshot.groundNormal);
        EditorGUILayout.ObjectField("脚下 Collider", snapshot.groundCollider, typeof(Collider2D), true);
        EditorGUILayout.ObjectField("左墙 Collider", snapshot.leftWallCollider, typeof(Collider2D), true);
        EditorGUILayout.ObjectField("右墙 Collider", snapshot.rightWallCollider, typeof(Collider2D), true);
    }

    static void DrawEnvironment(EntityPhysicsDebugSnapshot snapshot)
    {
        EditorGUILayout.Space();
        GUILayout.Label("环境接入（只读）", EditorStyles.boldLabel);
        EditorGUILayout.IntField("有效环境来源", snapshot.environmentSourceCount);
        EditorGUILayout.IntField("主动移速修饰项", snapshot.movementModifierCount);
        EditorGUILayout.IntField("持续速度项", snapshot.stateVelocityCount);
        EditorGUILayout.IntField("环境动态力项", snapshot.dynamicForceCount);
        EditorGUILayout.FloatField("支撑面消退阻力倍率", snapshot.environmentSlowingMultiplier);
        EditorGUILayout.FloatField("支撑面跳跃加成", snapshot.environmentJumpHeightOffset);
    }

    static void DrawContactPairs(BasicEntity target)
    {
        EditorGUILayout.Space();
        GUILayout.Label("相位 4 原生接触", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("采样帧", PhysicsEditorContactObservation.PublishedFixedTick.ToString());
        int count = 0;
        foreach (ContactPairDebugSnapshot pair in PhysicsEditorContactObservation.Contacts)
        {
            if (pair.entityA != target && pair.entityB != target) continue;
            count++;
            Collider2D other = pair.entityA == target ? pair.colliderB : pair.colliderA;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.ObjectField("对方碰撞体", other, typeof(Collider2D), true);
            DrawVector("接触点", pair.position);
            DrawVector("接触法线", pair.normal);
            EditorGUILayout.FloatField("表面间距", pair.separation);
            EditorGUILayout.FloatField("法向冲量", pair.normalImpulse);
            EditorGUILayout.EndVertical();
        }
        if (count == 0) EditorGUILayout.LabelField("无实际接触");
    }

    static void DrawVector(string label, Vector2 value)
    {
        EditorGUILayout.LabelField(label, $"({value.x:F5}, {value.y:F5})");
    }
}
