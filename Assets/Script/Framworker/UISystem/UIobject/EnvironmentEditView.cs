using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>漫游环境编辑：只操作关卡规则给出的草稿，统一确认后才改变场景和认知背包。</summary>
public sealed class EnvironmentEditView : MonoBehaviour
{
    [SerializeField] Color availableFrameColor = new Color(.35f, .8f, .9f, .7f);
    [SerializeField] Color selectedFrameColor = new Color(1f, .8f, .2f, 1f);
    [SerializeField, Min(1f), Tooltip("屏幕像素线宽，不随镜头缩放改变。")]
    float availableFrameWidth = 2f, selectedFrameWidth = 4f;
    BasicPhysicalObject selected, pendingSelection;
    EnvironmentEditDraft draft;
    RectTransform editor, backpack, abilityContent, backpackContent;
    GameObject selectionHint;
    Text objectText, listStateText, hintText, occupancyText, costText, backpackModeText, backpackSelectionText, backpackCapacityText;
    Button confirmButton, cancelButton, colliderViewButton, triggerViewButton, geometryButton, extractTriggerButton;
    Button openBackpackButton, closeBackpackButton;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    readonly List<BasicPhysicalObject> frameSources = new List<BasicPhysicalObject>();
    readonly List<Button> slotButtons = new List<Button>(), extractButtons = new List<Button>();
    readonly List<Button> extractPointButtons = new List<Button>();
    readonly Dictionary<int, Button> bagButtons = new Dictionary<int, Button>();
    EnvironmentSelectionFrames selectionFrames;
    float nextFrameScan, nextOccupancyScan;
    int selectedSlot = -1, selectedBagItemId = -1, seenBackpackVersion = -1;
    bool viewTrigger, geometryTarget, backpackOpen, standaloneBackpack;
    string message = "点击蓝框环境查看性质。";

    public EnvironmentEditDraft DisplayedDraft => draft;
    public bool IsBackpackOpen => backpackOpen;
    public bool IsBackpackReadOnly => standaloneBackpack || draft == null;
    public bool IsViewingTriggerList => viewTrigger;
    public int SelectedAbilitySlot => selectedSlot;

    void Awake()
    {
        GameplayPanel panel = GetComponentInParent<GameplayPanel>();
        // 安全：预设必须挂在游戏面板下；缺少宿主时不创建游离背包。
        if (panel == null) { enabled = false; return; }
        EnvironmentEditLayout.Ensure(transform, panel.transform);
        editor = (RectTransform)transform.Find("EnvironmentEditorContent");
        selectionHint = transform.Find("EnvironmentSelectionHint").gameObject;
        backpack = (RectTransform)panel.transform.Find("CognitionBackpackArea");
        abilityContent = (RectTransform)editor.Find("EnvironmentAbilityScroll/Viewport/Content");
        backpackContent = (RectTransform)backpack.Find("CognitionBackpackScroll/Viewport/Content");
        objectText = editor.Find("EnvironmentSelectionTitleText").GetComponent<Text>();
        listStateText = editor.Find("EnvironmentListStateText").GetComponent<Text>();
        hintText = editor.Find("EnvironmentDraftHintText").GetComponent<Text>();
        occupancyText = editor.Find("EnvironmentOccupancyText").GetComponent<Text>();
        costText = editor.Find("EnvironmentPreviewCostText").GetComponent<Text>();
        backpackModeText = backpack.Find("BackpackModeText").GetComponent<Text>();
        backpackSelectionText = backpack.Find("BackpackSelectionText").GetComponent<Text>();
        backpackCapacityText = backpack.Find("BackpackCapacityText").GetComponent<Text>();
        confirmButton = Bind(editor, "ConfirmEnvironmentButton", () => ConfirmChanges());
        cancelButton = Bind(editor, "CancelEnvironmentButton", CancelChanges);
        Bind(editor, "CloseEnvironmentButton", () => ClearSelection("点击蓝框环境查看性质。"));
        colliderViewButton = Bind(editor, "ViewColliderListButton", () => SetViewedList(false));
        triggerViewButton = Bind(editor, "ViewTriggerListButton", () => SetViewedList(true));
        geometryButton = Bind(editor, "EnvironmentGeometrySlot", SelectGeometrySlot);
        extractTriggerButton = Bind(editor, "ExtractTriggerNatureButton", ExtractTrigger);
        openBackpackButton = Bind(panel.transform, "OpenCognitionBackpackButton", OpenReadOnlyBackpack);
        closeBackpackButton = Bind(backpack, "CloseCognitionBackpackButton", CloseBackpack);
        RefreshAll();
    }

    static Button Bind(Transform parent, string name, UnityEngine.Events.UnityAction action)
    {
        Button button = parent.Find(name).GetComponent<Button>();
        button.onClick.AddListener(action); return button;
    }

    void OnEnable() { nextFrameScan = nextOccupancyScan = 0f; }

    void OnDisable()
    {
        // 阶段关闭或菜单暂停时放弃未确认草稿，不把预览带入模拟或逃生。
        if (draft != null) SceneChangeMgr.Instance.CancelEnvironmentEdit();
        draft = null; selected = pendingSelection = null;
        selectedSlot = selectedBagItemId = -1; geometryTarget = false; backpackOpen = false;
        if (editor != null) editor.gameObject.SetActive(false);
        if (backpack != null) backpack.gameObject.SetActive(false);
        if (openBackpackButton != null) openBackpackButton.gameObject.SetActive(false);
        if (selectionFrames != null) selectionFrames.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (selectionFrames != null) Destroy(selectionFrames.gameObject);
        // 根面板通常一起销毁；单独移除编辑区时也清理它创建的侧栏。
        if (backpack != null) Destroy(backpack.gameObject);
        if (openBackpackButton != null) Destroy(openBackpackButton.gameObject);
    }

    void LateUpdate()
    {
        bool allowed = SceneChangeMgr.Instance.CanEditEnvironment;
        if (selectionFrames != null) selectionFrames.gameObject.SetActive(allowed);
        if (!allowed) return;
        if (selectionFrames == null)
        {
            GameplayPanel panel = GetComponentInParent<GameplayPanel>();
            if (panel == null) return;
            var item = new GameObject("EnvironmentSelectionFrames", typeof(RectTransform), typeof(CanvasRenderer), typeof(EnvironmentSelectionFrames));
            item.layer = panel.gameObject.layer; item.transform.SetParent(panel.transform, false); item.transform.SetAsFirstSibling();
            EnvironmentEditLayout.Stretch((RectTransform)item.transform);
            selectionFrames = item.GetComponent<EnvironmentSelectionFrames>(); selectionFrames.raycastTarget = false;
        }
        // 发现只遍历本关层级；绘制本帧几何，不建立第二份环境登记关系。
        if (Time.unscaledTime >= nextFrameScan) ScanFrameSources();
        selectionFrames.Display(frameSources, ValidSelection() ? selected : null, Camera.main,
            availableFrameColor, selectedFrameColor, availableFrameWidth, selectedFrameWidth);
    }

    void Update()
    {
        bool allowed = SceneChangeMgr.Instance.CanEditEnvironment;
        if (openBackpackButton != null) openBackpackButton.gameObject.SetActive(allowed && !backpackOpen);
        if (!allowed) return;
        // 来源销毁、禁用或退出本关立即撤销草稿，不能提交旧目标。
        if (!object.ReferenceEquals(selected, null) && !ValidSelection()) ClearSelection("目标已不可编辑，请重新选择。");
        CognitionBackpack bag = SceneChangeMgr.Instance.CurrentCognitionBackpack;
        if (draft == null && bag != null && seenBackpackVersion != bag.Version) RefreshBackpack();
        if (draft != null && Time.unscaledTime >= nextOccupancyScan) RefreshOccupancy();
        RefreshControls();
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        Vector2 pointer = mouse.position.ReadValue();
        if (HitsUI(pointer)) return;
        Camera camera = Camera.main;
        // 安全：没有场景镜头时不以屏幕坐标误选世界。
        if (camera == null) return;
        Ray ray = camera.ScreenPointToRay(pointer);
        if (new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance)) SelectAt(ray.GetPoint(distance));
    }

    bool HitsUI(Vector2 pointer)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        uiHits.Clear();
        // 即时检查本次 UI 命中，避免按钮点击穿透选中其下方的环境。
        events.RaycastAll(new PointerEventData(events) { position = pointer }, uiHits);
        foreach (RaycastResult hit in uiHits) if (hit.module is GraphicRaycaster) return true;
        return false;
    }

    void SelectAt(Vector2 point)
    {
        if (!SceneChangeMgr.Instance.CanEditEnvironment) return;
        BasicPhysicalObject candidate = null;
        foreach (Collider2D collider in Physics2D.OverlapPointAll(point))
        {
            BasicPhysicalObject[] sources = collider.GetComponents<BasicPhysicalObject>();
            if (sources.Length != 1 || !EditableSource(sources[0])) continue;
            // 不同来源重叠时不暗中排序；保留正在预览的编辑。
            if (candidate != null && candidate != sources[0]) { ShowMessage("这里有多个环境，请点击更清晰的位置。"); return; }
            candidate = sources[0];
        }
        // 暂停箱子模拟已关闭，点选读取其真实矩形，不临时打开刚体。
        ScanFrameSources();
        foreach (BasicPhysicalObject source in frameSources)
        {
            if (!EnvironmentSelectionFrames.ContainsPausedBox(source, point)) continue;
            if (candidate != null && candidate != source) { ShowMessage("这里有多个环境，请点击更清晰的位置。"); return; }
            candidate = source;
        }
        if (candidate == null) { ShowMessage("这里没有可编辑环境，请点击蓝框物体。"); return; }
        SelectEnvironment(candidate);
    }

    internal static bool EditableSource(BasicPhysicalObject source) => source != null && source.IsEnvironmentActive
        && source.AllowsEnvironmentEditing && SceneLevelSetup.Current != null
        && source.gameObject.scene == SceneLevelSetup.Current.gameObject.scene
        && source.GetComponents<BasicPhysicalObject>().Length == 1
        && EnvironmentSelectionFrames.TryGetBounds(source, out _);

    void ScanFrameSources()
    {
        nextFrameScan = Time.unscaledTime + .25f; frameSources.Clear();
        if (SceneLevelSetup.Current == null) return;
        foreach (GameObject root in SceneLevelSetup.Current.gameObject.scene.GetRootGameObjects())
            foreach (BasicPhysicalObject source in root.GetComponentsInChildren<BasicPhysicalObject>())
                if (EditableSource(source)) frameSources.Add(source);
    }

    bool ValidSelection() => EditableSource(selected);
    bool CanAct => SceneChangeMgr.Instance.CanEditEnvironment && draft != null && ValidSelection() && pendingSelection == null;
    bool LinkedBackpack => CanAct && backpackOpen && !standaloneBackpack;

    public void SelectEnvironment(BasicPhysicalObject source)
    {
        if (!SceneChangeMgr.Instance.CanEditEnvironment || !EditableSource(source)) return;
        if (source == selected) return;
        // 换对象须由当前面板的确认／取消结束草稿，不能自动丢弃未提交修改。
        if (draft != null && draft.HasChanges)
        {
            pendingSelection = source;
            ShowMessage($"先确认或取消当前修改，再查看 {source.name}。"); RefreshControls(); return;
        }
        BeginSelection(source);
    }

    void BeginSelection(BasicPhysicalObject source)
    {
        // 未改动草稿也归规则持有，换目标前明确结束旧编辑。
        if (draft != null) SceneChangeMgr.Instance.CancelEnvironmentEdit();
        draft = null; selected = null;
        EnvironmentEditDraft next = SceneChangeMgr.Instance.BeginEnvironmentEdit(source);
        if (next == null) { ClearSelection("当前无法编辑此环境。"); return; }
        selected = source; draft = next; pendingSelection = null;
        viewTrigger = draft.IsTriggerEnvironment; selectedSlot = selectedBagItemId = -1; geometryTarget = false;
        message = "选择空槽注入性质；选择已有性质使用计算点。";
        RefreshAll(); ResetAbilityScroll();
    }

    public void SetViewedList(bool trigger)
    {
        if (!CanAct || viewTrigger == trigger) return;
        // 浏览切换只改变显示，两个能力列表的草稿继续保留。
        viewTrigger = trigger; selectedSlot = selectedBagItemId = -1; geometryTarget = false;
        message = trigger == draft.IsTriggerEnvironment ? "当前列表生效，可注入、计算或抽取。" : "此列表停用，保留性质；只允许抽取。";
        RefreshAll(); ResetAbilityScroll();
    }

    public void SelectAbilitySlot(int slot)
    {
        if (!CanAct || slot < 0 || slot >= draft.SlotCapacity || viewTrigger != draft.IsTriggerEnvironment) return;
        EnvironmentParameter property = draft.GetSlot(viewTrigger, slot);
        if (property != null && !property.SupportsCalculation) return;
        selectedSlot = slot; geometryTarget = false; selectedBagItemId = -1;
        message = property == null ? "背包高光性质可注入此空槽。"
            : property.CalculationCount >= draft.CalculationPointLimit ? "此槽计算点已满，可从数值行右侧逐个抽出。"
            : "背包高光计算点可预览此性质的结果。";
        OpenLinkedBackpack(); RefreshAll();
    }

    public void SelectGeometrySlot()
    {
        if (!CanAct || draft.IsTriggerEnvironment) return;
        selectedSlot = -1; geometryTarget = true; selectedBagItemId = -1;
        bool available = false;
        foreach (CognitionBackpackItem item in draft.Items)
            if (draft.CanUseItem(item.Id, viewTrigger, selectedSlot, geometryTarget)) { available = true; break; }
        // 未取得独特性质时仍打开联动背包，明确说明取得路径，不误导玩家寻找不存在的高光项。
        message = available ? "请点击背包高光的触发器性质，完成注入预览后再确认。"
            : "背包暂无触发器性质。请先从普通触发器右侧抽取并确认，再回来注入。";
        OpenLinkedBackpack(); RefreshAll();
    }

    void OpenLinkedBackpack() { backpackOpen = true; standaloneBackpack = false; }

    public void OpenReadOnlyBackpack()
    {
        if (!SceneChangeMgr.Instance.CanEditEnvironment) return;
        backpackOpen = standaloneBackpack = true; selectedBagItemId = -1;
        RefreshBackpack(); RefreshControls();
    }

    public void CloseBackpack()
    {
        backpackOpen = false; selectedBagItemId = -1; RefreshControls();
    }

    public void SelectBackpackItem(int itemId)
    {
        if (!LinkedBackpack) return;
        CognitionBackpackItem item = FindItem(itemId);
        if (item == null) return;
        selectedBagItemId = itemId;
        bool targetSelected = geometryTarget || selectedSlot >= 0;
        // 背包只有兼容项可用于当前目标；拆点入口统一在环境数值行。
        if (!targetSelected || !draft.CanUseItem(itemId, viewTrigger, selectedSlot, geometryTarget))
        {
            message = "此内容物不适用于当前槽。";
            RefreshAll(); return;
        }
        if (!draft.TryUseItem(itemId, viewTrigger, selectedSlot, geometryTarget, out string error))
        { ShowMessage(error); RefreshAll(); return; }
        message = item.IsCalculation ? "计算结果已预览；背包标记使用位置，整面板确认后生效。" : "性质已预览注入；整面板确认后生效。";
        // 注入独特性质只切换生效列表；浏览仍可停留原列表，明确显示置灰。
        RefreshAll();
    }

    public void ExtractAbility(int slot)
    {
        if (!CanAct || slot < 0 || slot >= draft.SlotCapacity) return;
        if (!draft.TryExtract(viewTrigger, slot, out string error)) { ShowMessage(error); return; }
        selectedSlot = -1; geometryTarget = false; selectedBagItemId = -1;
        OpenLinkedBackpack(); message = "性质整体预览入包，附加点保留；确认时统一扣费。"; RefreshAll();
    }

    public void ExtractTrigger()
    {
        if (!CanAct || !draft.IsTriggerEnvironment) return;
        if (!draft.TryExtractTrigger(out string error)) { ShowMessage(error); return; }
        selectedSlot = selectedBagItemId = -1; geometryTarget = false;
        OpenLinkedBackpack(); message = "触发器性质预览入包，将恢复碰撞器；两套列表仍保留。"; RefreshAll();
    }

    public void ExtractSlotCalculation(int slot)
    {
        if (!CanAct || slot < 0 || slot >= draft.SlotCapacity) return;
        if (!draft.TryExtractCalculation(viewTrigger, slot, out string error)) { ShowMessage(error); return; }
        OpenLinkedBackpack(); selectedBagItemId = -1;
        message = "最后加入的计算点已预览抽出并放在背包末尾；确认后生效。";
        RefreshAll();
    }

    public bool ConfirmChanges()
    {
        if (!SceneChangeMgr.Instance.CanEditEnvironment || draft == null || !ValidSelection()) return false;
        if (!SceneChangeMgr.Instance.ConfirmEnvironmentEdit(draft, out string error))
        { ShowMessage(error); RefreshAll(); return false; }
        BasicPhysicalObject next = pendingSelection;
        ClearSelection("修改已确认。点击蓝框环境继续查看。");
        if (EditableSource(next) && SceneChangeMgr.Instance.CanEditEnvironment) BeginSelection(next);
        return true;
    }

    public void CancelChanges()
    {
        BasicPhysicalObject next = pendingSelection;
        ClearSelection("未确认修改已取消。");
        if (EditableSource(next) && SceneChangeMgr.Instance.CanEditEnvironment) BeginSelection(next);
    }

    void ClearSelection(string text)
    {
        if (draft != null) SceneChangeMgr.Instance.CancelEnvironmentEdit();
        draft = null; selected = pendingSelection = null;
        selectedSlot = selectedBagItemId = -1; geometryTarget = false; message = text;
        // 关闭编辑只关闭联动背包，独立只读背包保留查看状态。
        if (!standaloneBackpack) backpackOpen = false;
        RefreshAll();
    }

    void ShowMessage(string text)
    {
        message = string.IsNullOrEmpty(text) ? "操作未完成，请重新选择。" : text;
        if (hintText != null) hintText.text = message;
        if (selectionHint != null) selectionHint.GetComponentInChildren<Text>().text = message;
    }

    void RefreshAll()
    {
        if (editor == null) return;
        objectText.text = draft == null ? "环境性质" : selected.name;
        hintText.text = message;
        selectionHint.GetComponentInChildren<Text>().text = message;
        if (draft != null)
        {
            listStateText.text = viewTrigger == draft.IsTriggerEnvironment ? $"当前生效 · {draft.SlotCapacity} 槽" : $"已停用 · {draft.SlotCapacity} 槽";
            Label(geometryButton).text = draft.IsTriggerEnvironment
                ? geometryTarget ? "触发器性质 · 注入已预览，待确认" : "触发器性质 · 当前已具备（独立槽）"
                : geometryTarget ? "触发器性质 · 待注入，请点击背包高光项"
                : selected.IsTriggerEnvironment ? "实心碰撞器 · 抽取已预览，待确认" : "实心碰撞器 · 点击选择背包触发器性质";
            costText.text = $"预览费用 {draft.Cost:0.##}  ·  当前认知 {LevelStageMgr.Instance.CognitionRemaining:0.##}";
            RefreshOccupancy();
        }
        RebuildSlots();
        RefreshBackpack(); RefreshControls();
    }

    void RefreshOccupancy()
    {
        nextOccupancyScan = Time.unscaledTime + .25f;
        if (draft == null) return;
        int count = SceneChangeMgr.Instance.GetPushableEnvironmentOverlapCount(selected);
        // 只在当前或预览触发器中提示推得动的实体，玩家和其它环境不计入。
        occupancyText.text = (draft.IsTriggerEnvironment || selected.IsTriggerEnvironment) && count > 0
            ? $"范围内有 {count} 个可推动实体（仅提示）" : "";
    }

    void RefreshControls()
    {
        if (editor == null) return;
        bool allowed = SceneChangeMgr.Instance.CanEditEnvironment;
        editor.gameObject.SetActive(allowed && draft != null);
        selectionHint.SetActive(allowed && draft == null);
        backpack.gameObject.SetActive(allowed && backpackOpen);
        openBackpackButton.gameObject.SetActive(allowed && !backpackOpen);
        colliderViewButton.interactable = triggerViewButton.interactable = CanAct;
        confirmButton.interactable = allowed && draft != null && ValidSelection() && draft.HasChanges;
        // 只点目标槽还没有注入草稿；确认区直接显示当前待完成的动作。
        Label(confirmButton).text = draft != null && geometryTarget && !draft.IsTriggerEnvironment && !draft.HasChanges ? "待注入" : "确认";
        cancelButton.interactable = allowed && draft != null;
        if (draft != null) costText.text = $"预览费用 {draft.Cost:0.##}  ·  当前认知 {LevelStageMgr.Instance.CognitionRemaining:0.##}";
        geometryButton.interactable = CanAct && !draft.IsTriggerEnvironment;
        // 独特槽与普通槽一样持续标记选中目标，鼠标移开后仍能看出背包正在为哪个槽选择。
        geometryButton.GetComponent<Image>().color = draft != null && geometryTarget && !draft.IsTriggerEnvironment
            ? EnvironmentEditLayout.Highlight : EnvironmentEditLayout.Normal;
        extractTriggerButton.interactable = CanAct && draft.IsTriggerEnvironment;
        closeBackpackButton.interactable = allowed;
        for (int i = 0; i < slotButtons.Count; i++)
        {
            if (slotButtons[i] != null) slotButtons[i].interactable = CanAct && viewTrigger == draft.IsTriggerEnvironment;
            extractButtons[i].interactable = CanAct && draft.GetSlot(viewTrigger, i) != null;
            if (extractPointButtons[i] != null)
                extractPointButtons[i].interactable = CanAct && draft.GetSlot(viewTrigger, i).CalculationCount > 0;
        }
        foreach (KeyValuePair<int, Button> entry in bagButtons)
        {
            bool compatible = LinkedBackpack && (geometryTarget || selectedSlot >= 0)
                && draft.CanUseItem(entry.Key, viewTrigger, selectedSlot, geometryTarget);
            entry.Value.interactable = compatible;
        }
    }

    void RebuildSlots()
    {
        if (abilityContent == null) return;
        ScrollRect scroll = abilityContent.GetComponentInParent<ScrollRect>();
        float position = scroll != null ? scroll.verticalNormalizedPosition : 1f;
        ClearChildren(abilityContent); slotButtons.Clear(); extractButtons.Clear(); extractPointButtons.Clear();
        int count = draft?.SlotCapacity ?? 0;
        float y = 0f;
        for (int i = 0; i < count; i++)
        {
            int slot = i;
            EnvironmentParameter property = draft.GetSlot(viewTrigger, i);
            bool numeric = property != null && property.SupportsCalculation;
            float height = numeric ? 104f : 64f;
            Color color = viewTrigger != draft.IsTriggerEnvironment ? EnvironmentEditLayout.Inactive : EnvironmentEditLayout.Normal;
            Button body = null, pop = null;
            if (property == null)
            {
                body = EnvironmentEditLayout.Button(abilityContent, "EnvironmentAbilitySlot_" + i, $"{i + 1} · 空槽 · 点击注入性质",
                    new Vector2(0, 1), new Vector2(0, -y), new Vector2(576, height), 19);
            }
            else
            {
                RectTransform nameRow = EnvironmentEditLayout.Region(abilityContent, "EnvironmentAbilityName_" + i,
                    new Vector2(0, 1), new Vector2(0, -y), new Vector2(576, numeric ? 44 : height));
                EnvironmentEditLayout.Background(nameRow, color, false);
                EnvironmentEditLayout.Label(nameRow, "Label", $"{i + 1} · {property.DisplayName}"
                    + (numeric ? "" : " · " + property.ValueText), new Vector2(10, 0), new Vector2(556, numeric ? 44 : height), 19, EnvironmentEditLayout.Ink);
                if (numeric)
                {
                    body = EnvironmentEditLayout.Button(abilityContent, "EnvironmentAbilitySlot_" + i,
                        property.CalculationFormula + $"\n计算点 {property.CalculationCount}/{draft.CalculationPointLimit} · 点击选择计算点",
                        new Vector2(0, 1), new Vector2(0, -y - 48), new Vector2(464, 56), 17);
                    pop = EnvironmentEditLayout.Button(abilityContent, "ExtractEnvironmentCalculation_" + i, "抽出\n计算点",
                        new Vector2(0, 1), new Vector2(472, -y - 48), new Vector2(104, 56), 17);
                    pop.GetComponent<Image>().color = color;
                    pop.onClick.AddListener(() => ExtractSlotCalculation(slot));
                }
            }
            // 纯能力只有名称行；数值行和空槽才是背包选择目标，抽点按钮是独立点击区。
            if (body != null)
            {
                Label(body).alignment = TextAnchor.MiddleLeft;
                body.GetComponent<Image>().color = viewTrigger == draft.IsTriggerEnvironment && i == selectedSlot && !geometryTarget
                    ? EnvironmentEditLayout.Highlight : color;
                body.onClick.AddListener(() => SelectAbilitySlot(slot));
            }
            slotButtons.Add(body); extractPointButtons.Add(pop);
            Button extract = EnvironmentEditLayout.Button(abilityContent, "ExtractEnvironmentAbility_" + i, "抽取", new Vector2(0, 1),
                new Vector2(584, -y), new Vector2(78, height), 19);
            extract.GetComponent<Image>().color = color;
            extract.onClick.AddListener(() => ExtractAbility(slot)); extractButtons.Add(extract);
            y += height + 8f;
        }
        abilityContent.sizeDelta = new Vector2(0, Mathf.Max(0, y - 8f));
        if (scroll != null) scroll.verticalNormalizedPosition = position;
    }

    void ResetAbilityScroll()
    {
        ScrollRect scroll = abilityContent.GetComponentInParent<ScrollRect>();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    void RefreshBackpack()
    {
        if (backpack == null) return;
        CognitionBackpack bag = SceneChangeMgr.Instance.CurrentCognitionBackpack;
        seenBackpackVersion = bag?.Version ?? -1;
        IReadOnlyList<CognitionBackpackItem> items = draft != null ? draft.Items : bag?.Items;
        ClearChildren(backpackContent); bagButtons.Clear();
        int count = items?.Count ?? 0;
        backpackContent.sizeDelta = new Vector2(0, Mathf.Max(count * 64f, 48f));
        backpackModeText.text = IsBackpackReadOnly ? draft != null ? "只读 · 当前环境预览" : "独立打开：只读查看" : pendingSelection != null
            ? "请先确认或取消当前环境修改" : "修改预览 · 整个环境面板确认后生效";
        int occupancy = draft != null ? draft.PreviewBackpackCount : count;
        int capacity = draft != null ? draft.BackpackCapacity : bag?.Capacity ?? 0;
        backpackCapacityText.text = $"{(draft != null ? "确认后占用" : "当前占用")} {occupancy}/{capacity}"
            + (occupancy > capacity ? " · 超容，无法确认" : "")
            + (draft != null ? "\n已标记的计算点确认后移出" : "");
        backpackCapacityText.color = occupancy > capacity ? new Color(1f, .65f, .35f) : EnvironmentEditLayout.Muted;
        if (count == 0) EnvironmentEditLayout.Label(backpackContent, "Label", "背包暂无内容物", new Vector2(10, -8), new Vector2(300, 40), 19, EnvironmentEditLayout.Muted);
        bool hasCompatibleItem = false;
        for (int i = 0; i < count; i++)
        {
            CognitionBackpackItem item = items[i]; int id = item.Id;
            bool compatible = LinkedBackpack && (geometryTarget || selectedSlot >= 0) && draft.CanUseItem(id, viewTrigger, selectedSlot, geometryTarget);
            hasCompatibleItem |= compatible;
            string target = draft != null ? draft.CalculationTarget(item) : "";
            string caption = item.DisplayName + (string.IsNullOrEmpty(target) ? "" : $"\n预览用于 {target}");
            // 几何槽的高光只表示可选，背包正文明确标出还需点击才能注入。
            if (compatible && geometryTarget) caption = "触发器性质\n点击注入";
            Button button = EnvironmentEditLayout.Button(backpackContent, "CognitionBackpackItem_" + id, caption, new Vector2(0, 1),
                new Vector2(0, -i * 64f), new Vector2(324, 58), 18);
            Label(button).alignment = TextAnchor.MiddleLeft;
            button.GetComponent<Image>().color = compatible || id == selectedBagItemId ? EnvironmentEditLayout.Highlight : EnvironmentEditLayout.Normal;
            button.onClick.AddListener(() => SelectBackpackItem(id)); bagButtons.Add(id, button);
        }
        CognitionBackpackItem selectedItem = FindItem(selectedBagItemId);
        backpackSelectionText.text = selectedItem == null ? "" : selectedItem.DisplayName;
        if (LinkedBackpack && geometryTarget)
        {
            // 在两面板之间明确区分选择目标、注入预览与统一确认三个状态。
            backpackModeText.text = draft.IsTriggerEnvironment ? "注入已预览 · 请确认环境面板"
                : hasCompatibleItem ? "点击高光触发器性质，预览注入" : "暂无触发器性质 · 请先抽取并确认";
            backpackSelectionText.text = draft.IsTriggerEnvironment ? "触发器性质已预览注入，待面板确认"
                : hasCompatibleItem ? "待注入：请点击上方高光性质" : "";
        }
        else if (LinkedBackpack && selectedSlot >= 0)
        {
            EnvironmentParameter target = draft.GetSlot(viewTrigger, selectedSlot);
            backpackModeText.text = viewTrigger != draft.IsTriggerEnvironment ? "当前列表停用：只可从环境抽取"
                : target == null ? $"已选择空槽 {selectedSlot + 1}：点击高光性质注入"
                : target.CalculationCount >= draft.CalculationPointLimit ? $"槽 {selectedSlot + 1} 计算点已满：请从数值行抽出"
                : $"已选择数值行 {selectedSlot + 1}：点击高光计算点叠加";
            if (!hasCompatibleItem && target == null) backpackModeText.text += "\n暂无兼容性质";
        }
        else if (LinkedBackpack) backpackModeText.text = "请先选环境空槽、数值行或顶部形态槽\n所有修改由环境面板统一确认";
        // 内容变更可刷新行，浏览切换和计算重选仍保留背包滚动位置。
    }

    CognitionBackpackItem FindItem(int id)
    {
        if (id < 0) return null;
        IReadOnlyList<CognitionBackpackItem> items = draft != null
            ? draft.Items : SceneChangeMgr.Instance.CurrentCognitionBackpack?.Items;
        if (items == null) return null;
        foreach (CognitionBackpackItem item in items) if (item.Id == id) return item;
        return null;
    }

    static Text Label(Button button) => button.transform.Find("Label").GetComponent<Text>();

    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            // 本帧旧按钮先失活，Unity 延迟销毁不会让旧内容继续接收点击。
            child.gameObject.SetActive(false); child.name = "Retired_" + child.name;
            if (Application.isPlaying) Object.Destroy(child.gameObject);
            else Object.DestroyImmediate(child.gameObject);
        }
    }
}
