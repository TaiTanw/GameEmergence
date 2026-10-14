#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>复用原制作窗口，基础属性、三项降费与全局自动池分别制作，不持运行状态。</summary>
public sealed class PlayerUpgradeConfigurationWindow : EditorWindow
{
    const string CatalogPath = "Assets/Resources/PlayerUpgradeCatalog.asset";
    const string PoolPath = "Assets/Resources/PlayerAbilityPool.asset";
    const string BodyPath = "Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab";
    const string SpiritPath = "Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab";
    static readonly string[] Operations = { "乘法", "加减" };
    [SerializeField] Player selectedPlayer;
    [SerializeField] int tab;
    Vector2 scroll;
    PlayerUpgradeCatalog catalog;
    PlayerAbilityPool pool;
    string savedMessage;
    static bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("自定义工具/开发工具/玩法/玩家属性与能力池")]
    static void Open() => GetWindow<PlayerUpgradeConfigurationWindow>("玩家属性与能力池");

    void OnEnable()
    {
        catalog = AssetDatabase.LoadAssetAtPath<PlayerUpgradeCatalog>(CatalogPath);
        pool = AssetDatabase.LoadAssetAtPath<PlayerAbilityPool>(PoolPath);
        if (selectedPlayer == null) SelectDefault(BodyPath);
        minSize = new Vector2(480f, 560f);
        Undo.undoRedoPerformed += Repaint;
    }
    void OnDisable() => Undo.undoRedoPerformed -= Repaint;
    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        if (!CanEdit) EditorGUILayout.HelpBox("请停止播放后制作。", MessageType.Info);
        tab = GUILayout.Toolbar(tab, new[] { "基础属性与上下限", "认知降费", "自动能力池" });
        scroll = EditorGUILayout.BeginScrollView(scroll);
        using (new EditorGUI.DisabledScope(!CanEdit))
        {
            if (tab == 0) DrawPlayer();
            else if (tab == 1) DrawCosts();
            else DrawPool();
        }
        if (!string.IsNullOrEmpty(savedMessage)) EditorGUILayout.HelpBox(savedMessage, MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    void SelectDefault(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        selectedPlayer = prefab != null ? prefab.GetComponent<Player>() : null;
        savedMessage = null;
    }

    static CharacterPhysics PhysicsOf(Player actor)
    {
        if (actor == null) return null;
        CharacterPhysics[] components = actor.GetComponentsInChildren<CharacterPhysics>(true);
        return components.Length == 1 ? components[0] : null;
    }

    void DrawPlayer()
    {
        selectedPlayer = (Player)EditorGUILayout.ObjectField("角色组件／预设", selectedPlayer, typeof(Player), true);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("默认实体")) SelectDefault(BodyPath);
            if (GUILayout.Button("默认精神体")) SelectDefault(SpiritPath);
            if (GUILayout.Button("使用当前选中")) selectedPlayer = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<Player>(true) : null;
        }
        CharacterPhysics physics = PhysicsOf(selectedPlayer);
        if (selectedPlayer == null || physics == null)
        { EditorGUILayout.HelpBox("选择有唯一 CharacterPhysics 的角色。", MessageType.Error); return; }
        var actor = new SerializedObject(selectedPlayer);
        var data = new SerializedObject(physics);
        actor.Update(); data.Update();
        EditorGUI.BeginChangeCheck();
        DrawAttribute(data, "speed", "movementSpeedLimits", "基础水平移速");
        DrawAttribute(data, "upSpeed", "jumpSpeedLimits", "基础起跳初速度");
        DrawAttribute(actor, "baseAirJumps", "airJumpLimits", "基础额外空中跳次数");
        DrawAttribute(data, "fourWaySpeed", "fourWaySpeedLimits", "飞行／游泳基础速度");
        if (EditorGUI.EndChangeCheck()) { ApplyData(actor); ApplyData(data); savedMessage = null; }
        EditorGUILayout.HelpBox("正面后负面，最终值只限界一次。范围限制能力算出的主动参数，不截断重力或环境被动速度。", MessageType.None);
        string error = null;
        if (!physics.ValidateMotionConfiguration(out error) || !selectedPlayer.AirJumpLimits.Validate(selectedPlayer.BaseAirJumps, true, out error))
            EditorGUILayout.HelpBox(error, MessageType.Error);
        using (new EditorGUI.DisabledScope(error != null)) if (GUILayout.Button("保存所选角色与范围")) SavePlayer();
    }

    static void DrawAttribute(SerializedObject data, string valueName, string rangeName, string label)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(data.FindProperty(valueName), new GUIContent(label));
            SerializedProperty range = data.FindProperty(rangeName);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(range.FindPropertyRelative("Min"), new GUIContent("最小"));
                EditorGUILayout.PropertyField(range.FindPropertyRelative("Max"), new GUIContent("最大"));
            }
        }
    }

    void DrawCosts()
    {
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("全局降费配置", catalog, typeof(PlayerUpgradeCatalog), false);
        if (catalog == null) { EditorGUILayout.HelpBox("缺少 " + CatalogPath, MessageType.Error); return; }
        EditorGUILayout.HelpBox("只开放三项永久降费；菜单 1 列全三项，可多次购买。等级在通关后确认，本关新购在失败或退出时撤销。旧运动编号保留兼容，不参与升级。", MessageType.None);
        var data = new SerializedObject(catalog); data.Update();
        SerializedProperty definitions = data.FindProperty("Definitions");
        EditorGUI.BeginChangeCheck();
        for (int i = 0; i < definitions.arraySize; i++)
        {
            SerializedProperty item = definitions.GetArrayElementAtIndex(i);
            PlayerUpgradeKind kind = (PlayerUpgradeKind)item.FindPropertyRelative("Kind").intValue;
            if (!PlayerUpgradeCatalog.IsCost(kind)) continue;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(kind == PlayerUpgradeKind.MapCost ? "地图认知降费" : kind == PlayerUpgradeKind.BackpackCost ? "入包认知降费" : "转换认知降费", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Enabled"), new GUIContent("开放购买"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Label"), new GUIContent("配置名称"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("MaxLevel"), new GUIContent("等级上限"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Cost"), new GUIContent("每次购买认知"));
                SerializedProperty op = item.FindPropertyRelative("Operation");
                op.enumValueIndex = EditorGUILayout.Popup("固定运算", op.enumValueIndex, Operations);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Step"), new GUIContent("每级固定步长"));
            }
        }
        if (EditorGUI.EndChangeCheck()) { ApplyData(data); savedMessage = null; }
        bool valid = catalog.Validate(out string error);
        if (!valid) EditorGUILayout.HelpBox(error, MessageType.Error);
        using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button("保存三项降费配置")) { AssetDatabase.SaveAssetIfDirty(catalog); savedMessage = "已保存降费配置。"; }
    }

    void DrawPool()
    {
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("全局自动池", pool, typeof(PlayerAbilityPool), false);
        if (pool == null) { EditorGUILayout.HelpBox("缺少 " + PoolPath, MessageType.Error); return; }
        EditorGUILayout.HelpBox("正负各按概率触发，然后在对应分组按权重抽一项；全池只作用本场。移速包含游泳速度，跳数使用整数增减。", MessageType.None);
        var data = new SerializedObject(pool); data.Update();
        SerializedProperty entries = data.FindProperty("Entries");
        EditorGUI.BeginChangeCheck();
        for (int group = 0; group < 2; group++)
        {
            bool positive = group == 0;
            EditorGUILayout.LabelField(positive ? "正面分组" : "负面分组", EditorStyles.boldLabel);
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty item = entries.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("Positive").boolValue != positive) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    SerializedProperty kind = item.FindPropertyRelative("Kind");
                    kind.enumValueIndex = EditorGUILayout.Popup("能力", kind.enumValueIndex, new[] { "移动速度", "起跳强度", "额外空中跳" });
                    SerializedProperty sign = item.FindPropertyRelative("Positive");
                    sign.boolValue = EditorGUILayout.Popup("正负", sign.boolValue ? 0 : 1, new[] { "正面", "负面" }) == 0;
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("Weight"), new GUIContent("权重"));
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("Value"), new GUIContent(kind.enumValueIndex == 2 ? "次数增减（整数）" : "倍率"));
                    if (GUILayout.Button("删除此条")) { entries.DeleteArrayElementAtIndex(i); GUI.changed = true; break; }
                }
            }
            if (GUILayout.Button(positive ? "＋ 正面能力" : "＋ 负面能力")) ShowAddMenu(positive);
        }
        if (EditorGUI.EndChangeCheck()) { ApplyData(data); savedMessage = null; }
        bool valid = pool.Validate(out string error);
        if (!valid) EditorGUILayout.HelpBox(error, MessageType.Error);
        using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button("保存全局自动池")) { AssetDatabase.SaveAssetIfDirty(pool); savedMessage = "已保存全局自动池。"; }
    }

    void ShowAddMenu(bool positive)
    {
        var menu = new GenericMenu();
        foreach (PlayerAbilityKind kind in System.Enum.GetValues(typeof(PlayerAbilityKind)))
        {
            PlayerAbilityKind selected = kind;
            menu.AddItem(new GUIContent(PlayerAbilityEffect.Name(kind)), false, () => {
                if (!CanEdit || pool == null) return;
                Undo.RecordObject(pool, "添加自动能力");
                float value = selected == PlayerAbilityKind.AirJumps ? (positive ? 1f : -1f) : (positive ? 1.2f : .8f);
                if (pool.Entries == null) pool.Entries = new System.Collections.Generic.List<PlayerAbilityOffer>();
                pool.Entries.Add(new PlayerAbilityOffer(selected, positive, value));
                EditorUtility.SetDirty(pool); savedMessage = null; Repaint();
            });
        }
        menu.ShowAsContext();
    }

    static void ApplyData(SerializedObject data)
    {
        if (!CanEdit || !data.ApplyModifiedProperties()) return;
        Object target = data.targetObject; EditorUtility.SetDirty(target);
        Component component = target as Component;
        if (component == null || EditorUtility.IsPersistent(component)) return;
        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
    }

    void SavePlayer()
    {
        if (!CanEdit || selectedPlayer == null) return;
        PrefabStage stage = PrefabStageUtility.GetPrefabStage(selectedPlayer.gameObject);
        bool saved;
        if (stage != null) PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out saved);
        else if (EditorUtility.IsPersistent(selectedPlayer)) PrefabUtility.SavePrefabAsset(selectedPlayer.transform.root.gameObject, out saved);
        else saved = EditorSceneManager.SaveScene(selectedPlayer.gameObject.scene);
        savedMessage = saved ? "已保存所选角色与上下限。" : "保存失败，请检查控制台。";
    }
}

// 直接从项目窗口打开资产时也能看到制作错误，避免必须进专用窗口才发现空池。
[CustomEditor(typeof(PlayerAbilityPool))]
sealed class PlayerAbilityPoolInspector : Editor
{
    public override void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawDefaultInspector();
        if (!((PlayerAbilityPool)target).Validate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
    }
}
#endif
