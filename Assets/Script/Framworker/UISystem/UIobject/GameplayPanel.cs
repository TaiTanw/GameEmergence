using UnityEngine.UI;

/// <summary>游戏显示预设的只读绑定；资源和阶段仍归玩家及关卡规则，界面不另建计时或余额。</summary>
public class GameplayPanel : BasePanel
{
    [UnityEngine.SerializeField, UnityEngine.Min(0f)] float fullResourceHideDelay = 1f;
    Text cognitionText, refreshMessageText, endExplorationText;
    UnityEngine.GameObject actionArea, environmentEditArea, escapeChoiceArea, mapArea, cognitionArea, endExplorationNote;
    Button resetTrialButton;
    UnityEngine.RectTransform messageArea, staminaBar, oxygenBar, panelRect;
    Image cognitionFill, endExplorationFill, staminaFill, oxygenFill;
    UnityEngine.GameObject backpackArea, openBackpackButton;
    UnityEngine.Canvas canvas;
    Player displayedPlayer;
    UnityEngine.SpriteRenderer displayedSprite;
    float staminaFullSince = -1f, oxygenFullSince = -1f;

    protected override void OtherInit()
    {
        cognitionText = FindUIObj<Text>("CognitionText");
        cognitionFill = FindUIObj<Image>("CognitionFill");
        endExplorationFill = FindUIObj<Image>("EndExplorationFill");
        endExplorationText = FindUIObj<Text>("EndExplorationText");
        staminaFill = FindUIObj<Image>("StaminaFill");
        oxygenFill = FindUIObj<Image>("OxygenFill");
        cognitionArea = transform.Find("CognitionArea").gameObject;
        endExplorationNote = transform.Find("EndExplorationNote").gameObject;
        staminaBar = transform.Find("StaminaBar") as UnityEngine.RectTransform;
        oxygenBar = transform.Find("OxygenBar") as UnityEngine.RectTransform;
        panelRect = (UnityEngine.RectTransform)transform;
        actionArea = transform.Find("ActionArea").gameObject;
        environmentEditArea = transform.Find("ActionArea/EnvironmentEditArea").gameObject;
        resetTrialButton = FindUIObj<Button>("ResetTrialButton");
        escapeChoiceArea = transform.Find("EscapeChoiceArea").gameObject;
        mapArea = transform.Find("MapArea").gameObject;
        refreshMessageText = FindUIObj<Text>("RefreshMessageText");
        messageArea = transform.Find("MessageArea") as UnityEngine.RectTransform;
        actionArea.GetComponent<UnityEngine.RectTransform>().sizeDelta = new UnityEngine.Vector2(720f, 64f);
    }

    protected override void OnClickButton(string name)
    { if (name == "ResetTrialButton") SceneChangeMgr.Instance.ResetTrial(); }

    public override void ShowMe()
    {
        base.ShowMe();
        displayedPlayer = null;
        staminaBar.gameObject.SetActive(false); oxygenBar.gameObject.SetActive(false);
        staminaFullSince = oxygenFullSince = -1f;
        RefreshDisplay();
    }

    void LateUpdate() => RefreshDisplay();

    void RefreshDisplay()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        InputControlMgr input = InputControlMgr.Instance;
        Player player = input.Player;
        string message = host.GameplayMessage;
        refreshMessageText.text = message;
        messageArea.gameObject.SetActive(!string.IsNullOrEmpty(message));
        bool resetAllowed = host.CanResetTrial, editAllowed = host.CanEditEnvironment;
        messageArea.anchoredPosition = new UnityEngine.Vector2(0f, editAllowed ? 560f : 140f);
        if (backpackArea == null) backpackArea = transform.Find("CognitionBackpackArea")?.gameObject;
        if (openBackpackButton == null) openBackpackButton = transform.Find("OpenCognitionBackpackButton")?.gameObject;
        // 模拟与逃生不留下漫游背包入口。
        if (!editAllowed)
        {
            if (backpackArea != null) backpackArea.SetActive(false);
            if (openBackpackButton != null) openBackpackButton.SetActive(false);
        }
        resetTrialButton.gameObject.SetActive(resetAllowed);
        resetTrialButton.interactable = resetAllowed;
        escapeChoiceArea.SetActive(host.EscapeSelectionPending);
        mapArea.SetActive(host.CanViewMap);
        environmentEditArea.SetActive(editAllowed);
        actionArea.SetActive(resetAllowed || editAllowed);
        SceneLevelSetup setup = SceneLevelSetup.Current;
        bool hasCognition = setup != null && !setup.Teaching && setup.InitialCognition > 0f;
        // 准备购买仍显示余额；确认交接及正式逃生隐藏整组顶部条，失败回探索再恢复。
        cognitionArea.SetActive(hasCognition && LevelStageMgr.Instance.Stage == PlayerGameplayStage.Exploration
            && !host.IsEscapeHandoff);
        // 安全：没有本关认知时不画占位条，也不除以零。
        if (hasCognition)
        {
            float remaining = LevelStageMgr.Instance.CognitionRemaining;
            SetCenteredFill(cognitionFill, remaining / setup.InitialCognition);
            cognitionText.text = $"{UnityEngine.Mathf.Floor(remaining):0} / {setup.InitialCognition:0.##}";
        }
        bool canEnd = host.CanStartEscape;
        endExplorationNote.SetActive(canEnd);
        endExplorationFill.gameObject.SetActive(hasCognition && canEnd);
        SetCenteredFill(endExplorationFill, 1f - input.EscapeHoldProgress);
        endExplorationText.text = input.EscapeKeyConflict ? "回车与玩家按键冲突\n请在设置中改开" : "长按回车\n结束探索";
        // 换人时清显示计时，不把精神体的旧条留给实体。
        if (displayedPlayer != player)
        {
            displayedPlayer = player;
            displayedSprite = player != null ? player.GetComponentInChildren<UnityEngine.SpriteRenderer>() : null;
            staminaBar.gameObject.SetActive(false); oxygenBar.gameObject.SetActive(false);
            staminaFullSince = oxygenFullSince = -1f;
        }
        // 安全：交接中没有控制角色，不沿用旧位置与资源。
        if (player == null)
        { staminaBar.gameObject.SetActive(false); oxygenBar.gameObject.SetActive(false); return; }
        PlayerResourceFacts resources = player.ResourceFacts;
        UpdateResource(staminaBar, staminaFill, resources.stamina, resources.staminaMax, ref staminaFullSince);
        UpdateResource(oxygenBar, oxygenFill, resources.oxygen, resources.oxygenMax, ref oxygenFullSince);
        PlaceResourceBars(player, input.GameCamera);
    }

    // 两条横条保持中心不动，两端按剩余比例同时向中间收缩。
    static void SetCenteredFill(Image fill, float fraction)
    {
        float inset = (1f - UnityEngine.Mathf.Clamp01(fraction)) * .5f;
        UnityEngine.RectTransform rect = fill.rectTransform;
        rect.anchorMin = new UnityEngine.Vector2(inset, rect.anchorMin.y);
        rect.anchorMax = new UnityEngine.Vector2(1f - inset, rect.anchorMax.y);
    }

    void UpdateResource(UnityEngine.RectTransform bar, Image fill, float current, float maximum, ref float fullSince)
    {
        // 安全：角色未配置有效容量时不绘制资源。
        if (maximum <= 0f) { bar.gameObject.SetActive(false); return; }
        fill.fillAmount = UnityEngine.Mathf.Clamp01(current / maximum);
        if (current < maximum - .001f)
        { fullSince = -1f; bar.gameObject.SetActive(true); return; }
        if (!bar.gameObject.activeSelf) return;
        if (fullSince < 0f) fullSince = UnityEngine.Time.unscaledTime;
        if (UnityEngine.Time.unscaledTime - fullSince >= fullResourceHideDelay) bar.gameObject.SetActive(false);
    }

    void PlaceResourceBars(Player player, UnityEngine.Camera gameCamera)
    {
        if (canvas == null) canvas = GetComponentInParent<UnityEngine.Canvas>();
        // 安全：镜头或画布尚未就绪时不使用错误坐标显示条。
        if (gameCamera == null || canvas == null) return;
        UnityEngine.Bounds bounds = player.BodyBounds;
        UnityEngine.Vector3 center = gameCamera.WorldToScreenPoint(bounds.center);
        // 动画显示可比碰撞框更宽；按显示边缘摆条，避开头发和尾巴。
        UnityEngine.Bounds visible = displayedSprite != null ? displayedSprite.bounds : bounds;
        UnityEngine.Vector3 left = gameCamera.WorldToScreenPoint(new UnityEngine.Vector3(visible.min.x, bounds.center.y, bounds.center.z));
        UnityEngine.Vector3 right = gameCamera.WorldToScreenPoint(new UnityEngine.Vector3(visible.max.x, bounds.center.y, bounds.center.z));
        if (center.z <= 0f) { staminaBar.gameObject.SetActive(false); oxygenBar.gameObject.SetActive(false); return; }
        UnityEngine.Camera uiCamera = canvas.renderMode == UnityEngine.RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        float gap = 18f * canvas.scaleFactor;
        PlaceBar(staminaBar, new UnityEngine.Vector2(left.x - gap, center.y), uiCamera);
        PlaceBar(oxygenBar, new UnityEngine.Vector2(right.x + gap, center.y), uiCamera);
    }

    void PlaceBar(UnityEngine.RectTransform bar, UnityEngine.Vector2 screen, UnityEngine.Camera uiCamera)
    {
        if (!UnityEngine.RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, screen, uiCamera, out UnityEngine.Vector2 point)) return;
        UnityEngine.Rect rect = panelRect.rect;
        point.x = UnityEngine.Mathf.Clamp(point.x, rect.xMin + 16f, rect.xMax - 16f);
        point.y = UnityEngine.Mathf.Clamp(point.y, rect.yMin + 54f, rect.yMax - 100f);
        bar.anchoredPosition = point;
    }
}
