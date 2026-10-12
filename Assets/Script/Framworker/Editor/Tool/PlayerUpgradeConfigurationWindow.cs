#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>直接制作已有角色基础值与六项共享升级配置，不持有第二套运行数据。</summary>
public sealed class PlayerUpgradeConfigurationWindow : EditorWindow
{
    const string CatalogPath = "Assets/Resources/PlayerUpgradeCatalog.asset";
    const string BodyPath = "Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab";
    const string SpiritPath = "Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab";
    static readonly string[] Operations = { "乘法", "加减" };
    static readonly string[] KindLabels = { "移速", "起跳", "额外空中跳", "地图降费", "入包降费", "转换降费" };
    [SerializeField] Player selectedPlayer;
    [SerializeField] bool advanced;
    Vector2 scroll;
    PlayerUpgradeCatalog catalog;
    string savedMessage;
    static bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("自定义工具/开发工具/玩法/玩家基础属性与升级")]
    static void Open() => GetWindow<PlayerUpgradeConfigurationWindow>("玩家属性与升级");

    void OnEnable()
    {
        catalog = AssetDatabase.LoadAssetAtPath<PlayerUpgradeCatalog>(CatalogPath);
        if (selectedPlayer == null) SelectDefault(BodyPath);
        minSize = new Vector2(440f, 540f);
        Undo.undoRedoPerformed += Repaint;
    }

    void OnDisable() => Undo.undoRedoPerformed -= Repaint;
    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        if (!CanEdit) EditorGUILayout.HelpBox("请停止播放后制作；运行时仅查看原配置。", MessageType.Info);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSelection();
        CharacterPhysics physics = PhysicsOf(selectedPlayer);
        using (new EditorGUI.DisabledScope(!CanEdit))
        {
            DrawBasics(physics);
            DrawCatalog();
        }
        string error = ValidateConfiguration(physics);
        EditorGUILayout.HelpBox(error ?? "配置有效；下方预览使用所选角色的原基础值，所有等级使用同一步长。",
            error == null ? MessageType.Info : MessageType.Error);
        if (error == null && physics != null) DrawMaximumPreview(physics);
        using (new EditorGUI.DisabledScope(!CanEdit || error != null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(selectedPlayer == null))
                    if (GUILayout.Button("保存所选角色")) SavePlayer();
                using (new EditorGUI.DisabledScope(catalog == null))
                    if (GUILayout.Button("保存共享升级配置")) SaveCatalog();
            }
        }
        if (!string.IsNullOrEmpty(savedMessage)) EditorGUILayout.HelpBox(savedMessage, MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    void DrawSelection()
    {
        EditorGUILayout.LabelField("角色基础属性", EditorStyles.boldLabel);
        Player chosen = (Player)EditorGUILayout.ObjectField("角色组件／预设", selectedPlayer, typeof(Player), true);
        if (chosen != selectedPlayer) { selectedPlayer = chosen; savedMessage = null; }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("默认实体")) SelectDefault(BodyPath);
            if (GUILayout.Button("默认精神体")) SelectDefault(SpiritPath);
            if (GUILayout.Button("使用当前选中"))
            {
                GameObject selected = Selection.activeGameObject;
                selectedPlayer = selected != null ? selected.GetComponentInParent<Player>(true) : null;
                savedMessage = null;
            }
        }
        if (selectedPlayer != null)
            EditorGUILayout.LabelField("当前身份", selectedPlayer.Identity == PlayerIdentity.Body ? "实体" : "精神体");
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

    void DrawBasics(CharacterPhysics physics)
    {
        if (selectedPlayer == null) return;
        if (physics == null)
        { EditorGUILayout.HelpBox("所选角色须有且仅有一个 CharacterPhysics。", MessageType.Error); return; }
        var actorData = new SerializedObject(selectedPlayer);
        var physicsData = new SerializedObject(physics);
        actorData.Update(); physicsData.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(physicsData.FindProperty("speed"), new GUIContent("基础水平移速"));
        EditorGUILayout.PropertyField(physicsData.FindProperty("upSpeed"), new GUIContent("基础起跳初速度"));
        EditorGUILayout.PropertyField(actorData.FindProperty("baseAirJumps"), new GUIContent("基础额外空中跳次数"));
        EditorGUILayout.HelpBox("起跳高度通过初速度调整，仍受重力和松开短跳影响。空中次数不包含地面起跳；0 表示不能额外空中跳。实体和精神体分别制作。", MessageType.None);
        advanced = EditorGUILayout.Foldout(advanced, "高级：四向移动原基础值", true);
        if (advanced)
            EditorGUILayout.PropertyField(physicsData.FindProperty("fourWaySpeed"), new GUIContent("飞行／游泳基础速度"));
        if (EditorGUI.EndChangeCheck() && CanEdit)
        {
            Undo.SetCurrentGroupName("修改角色基础属性");
            ApplyData(actorData);
            ApplyData(physicsData);
            savedMessage = null;
        }
    }

    void DrawCatalog()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("六项共享升级", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("配置资源", catalog, typeof(PlayerUpgradeCatalog), false);
        if (catalog == null)
        { EditorGUILayout.HelpBox("缺少 " + CatalogPath + "；请恢复该配置资源，本面板不自动创建第二份。", MessageType.Error); return; }
        EditorGUILayout.HelpBox("每个能力固定一种运算与一个步长，所有等级共用每次价格。乘法：基础值 × 步长^等级；加减：基础值 + 步长 × 等级。降费加减填负数，费用最低为 0。", MessageType.None);
        var data = new SerializedObject(catalog);
        data.Update();
        SerializedProperty definitions = data.FindProperty("Definitions");
        EditorGUI.BeginChangeCheck();
        for (int i = 0; i < definitions.arraySize; i++)
        {
            SerializedProperty item = definitions.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                PlayerUpgradeKind kind = (PlayerUpgradeKind)item.FindPropertyRelative("Kind").intValue;
                string kindLabel = PlayerUpgradeCatalog.IsKnown(kind) ? KindLabels[(int)kind] : "无效能力标识";
                EditorGUILayout.LabelField(kindLabel + "（固定标识）", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Enabled"), new GUIContent("开放抽取"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Label"), new GUIContent("显示名称"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("MaxLevel"), new GUIContent("等级上限"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Cost"), new GUIContent("每次购买价格"));
                SerializedProperty operation = item.FindPropertyRelative("Operation");
                int selectedOperation = EditorGUILayout.Popup("固定运算", operation.enumValueIndex, Operations);
                if (selectedOperation >= 0 && selectedOperation != operation.enumValueIndex)
                    operation.enumValueIndex = selectedOperation;
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Step"), new GUIContent("每级固定步长"));
            }
        }
        if (EditorGUI.EndChangeCheck() && CanEdit)
        {
            Undo.SetCurrentGroupName("修改共享升级配置");
            ApplyData(data);
            savedMessage = null;
        }
    }

    static void ApplyData(SerializedObject data)
    {
        if (!CanEdit || !data.ApplyModifiedProperties()) return;
        Object target = data.targetObject;
        EditorUtility.SetDirty(target);
        Component component = target as Component;
        if (component == null || EditorUtility.IsPersistent(component)) return;
        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
    }

    string ValidateConfiguration(CharacterPhysics physics)
    {
        if (catalog == null) return "共享升级配置缺失。";
        if (!catalog.Validate(out string error)) return error;
        if (selectedPlayer == null) return null;
        if (physics == null) return "所选角色的物理组件缺失或不唯一。";
        if (PrefabUtility.GetPrefabAssetType(selectedPlayer) == PrefabAssetType.Model)
            return "模型导入预设不能直接修改，请选择可制作的角色预设或场景组件。";
        float[] bases = { physics.speed, physics.BaseFourWaySpeed, physics.upSpeed, selectedPlayer.BaseAirJumps };
        PlayerUpgradeKind[] kinds = { PlayerUpgradeKind.MovementSpeed, PlayerUpgradeKind.MovementSpeed,
            PlayerUpgradeKind.JumpStrength, PlayerUpgradeKind.AirJumps };
        string[] labels = { "基础水平移速", "四向移动基础速度", "起跳初速度", "额外空中跳次数" };
        for (int i = 0; i < bases.Length; i++)
        {
            if (!Finite(bases[i]) || bases[i] < 0f) return labels[i] + "必须是有限非负数。";
            PlayerUpgradeDefinition definition = catalog.Get(kinds[i]);
            if (!Finite(definition.Evaluate(bases[i], definition.MaxLevel)))
                return labels[i] + "在配置等级上限产生无效值或溢出，请降低上限／步长或调整基础值。";
        }
        return null;
    }

    void DrawMaximumPreview(CharacterPhysics physics)
    {
        EditorGUILayout.LabelField("所选角色：等级上限预览", EditorStyles.boldLabel);
        Preview("水平移速", PlayerUpgradeKind.MovementSpeed, physics.speed);
        Preview("四向速度", PlayerUpgradeKind.MovementSpeed, physics.BaseFourWaySpeed);
        Preview("起跳初速度", PlayerUpgradeKind.JumpStrength, physics.upSpeed);
        Preview("额外空中跳次数", PlayerUpgradeKind.AirJumps, selectedPlayer.BaseAirJumps);
    }

    void Preview(string label, PlayerUpgradeKind kind, float basis)
    {
        PlayerUpgradeDefinition definition = catalog.Get(kind);
        EditorGUILayout.LabelField(label, $"{basis:0.###} → {definition.Evaluate(basis, definition.MaxLevel):0.###}（{definition.MaxLevel} 级）");
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    void SavePlayer()
    {
        if (!CanEdit || selectedPlayer == null || ValidateConfiguration(PhysicsOf(selectedPlayer)) != null) return;
        PrefabStage stage = PrefabStageUtility.GetPrefabStage(selectedPlayer.gameObject);
        bool saved;
        if (stage != null)
        {
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out saved);
            savedMessage = saved ? "已保存当前预设编辑场景对应的角色预设。" : "角色预设保存失败，请检查控制台。";
        }
        else if (EditorUtility.IsPersistent(selectedPlayer))
        {
            PrefabUtility.SavePrefabAsset(selectedPlayer.transform.root.gameObject, out saved);
            savedMessage = saved ? "已保存所选角色预设。" : "角色预设保存失败，请检查控制台。";
        }
        else
        {
            saved = EditorSceneManager.SaveScene(selectedPlayer.gameObject.scene);
            savedMessage = saved ? "已保存所选角色所在场景；场景预设实例改动保留为该场景的覆盖值。" : "场景未保存。";
        }
    }

    void SaveCatalog()
    {
        if (!CanEdit || ValidateConfiguration(PhysicsOf(selectedPlayer)) != null) return;
        AssetDatabase.SaveAssetIfDirty(catalog);
        savedMessage = "已保存共享升级配置；所选角色由独立保存按钮处理。";
    }
}
#endif
