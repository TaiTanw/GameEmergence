using UnityEngine.UI;

/// <summary>游戏显示预设的只读绑定；资源和阶段仍归玩家及关卡规则，界面不另建计时或余额。</summary>
public class GameplayPanel : BasePanel
{
    Text cognitionText, staminaText, oxygenText, formCooldownText, stageText, refreshMessageText;
    UnityEngine.GameObject actionArea, environmentEditArea, escapeChoiceArea, mapArea;
    Button resetTrialButton, startEscapeButton;
    UnityEngine.RectTransform messageArea;
    UnityEngine.GameObject backpackArea, openBackpackButton;

    protected override void OtherInit()
    {
        cognitionText = FindUIObj<Text>("CognitionText");
        staminaText = FindUIObj<Text>("StaminaText");
        oxygenText = FindUIObj<Text>("OxygenText");
        formCooldownText = FindUIObj<Text>("FormCooldownText");
        stageText = FindUIObj<Text>("StageText");
        actionArea = transform.Find("ActionArea").gameObject;
        environmentEditArea = transform.Find("ActionArea/EnvironmentEditArea").gameObject;
        resetTrialButton = FindUIObj<Button>("ResetTrialButton");
        startEscapeButton = FindUIObj<Button>("StartEscapeButton");
        escapeChoiceArea = transform.Find("EscapeChoiceArea").gameObject;
        mapArea = transform.Find("MapArea").gameObject;
        refreshMessageText = FindUIObj<Text>("RefreshMessageText");
        messageArea = transform.Find("MessageArea").GetComponent<UnityEngine.RectTransform>();
        // 环境卡保持中下位置，底部单独留出重置按钮，不与右侧背包和地图重叠。
        actionArea.GetComponent<UnityEngine.RectTransform>().sizeDelta = new UnityEngine.Vector2(720f, 64f);
    }

    protected override void OnClickButton(string name)
    {
        if (name == "ResetTrialButton") SceneChangeMgr.Instance.ResetTrial();
        if (name == "StartEscapeButton") SceneChangeMgr.Instance.StartEscape();
    }

    public override void ShowMe()
    {
        base.ShowMe();
        RefreshDisplay();
    }

    void LateUpdate() => RefreshDisplay();

    void RefreshDisplay()
    {
        Player player = InputControlMgr.Instance.Player;
        string message = SceneChangeMgr.Instance.GameplayMessage;
        refreshMessageText.text = message;
        transform.Find("MessageArea").gameObject.SetActive(!string.IsNullOrEmpty(message));
        bool resetAllowed = SceneChangeMgr.Instance.CanResetTrial;
        bool editAllowed = SceneChangeMgr.Instance.CanEditEnvironment;
        bool startAllowed = SceneChangeMgr.Instance.CanStartEscape;
        bool choosing = SceneChangeMgr.Instance.EscapeSelectionPending;
        messageArea.anchoredPosition = new UnityEngine.Vector2(0f, editAllowed ? 560f : 140f);
        // 新预设侧栏已存在，旧预设稍后补齐；非漫游阶段始终不留下背包入口。
        if (backpackArea == null) backpackArea = transform.Find("CognitionBackpackArea")?.gameObject;
        if (openBackpackButton == null) openBackpackButton = transform.Find("OpenCognitionBackpackButton")?.gameObject;
        if (!editAllowed)
        {
            if (backpackArea != null) backpackArea.SetActive(false);
            if (openBackpackButton != null) openBackpackButton.SetActive(false);
        }
        resetTrialButton.gameObject.SetActive(resetAllowed);
        startEscapeButton.gameObject.SetActive(startAllowed);
        startEscapeButton.interactable = startAllowed;
        escapeChoiceArea.SetActive(choosing);
        mapArea.SetActive(SceneChangeMgr.Instance.CanViewMap);
        environmentEditArea.SetActive(editAllowed);
        actionArea.SetActive(resetAllowed || editAllowed);
        resetTrialButton.interactable = resetAllowed;
        // 安全：加载交接时没有控制对象，沿用占位显示，不用旧玩家继续显示资源。
        if (player == null)
        {
            cognitionText.text = choosing ? $"认知  {UnityEngine.Mathf.Floor(LevelStageMgr.Instance.CognitionRemaining):0}" : "—";
            staminaText.text = oxygenText.text = formCooldownText.text = "—";
            stageText.text = choosing ? "逃生准备" : "";
            return;
        }
        LevelStageMgr stage = LevelStageMgr.Instance;
        PlayerResourceFacts resources = player.ResourceFacts;
        cognitionText.text = stage.HasExploration ? $"认知  {UnityEngine.Mathf.Floor(stage.CognitionRemaining):0}" : "认知  —";
        staminaText.text = $"体力  {resources.stamina:0.#} / {resources.staminaMax:0.#}";
        oxygenText.text = $"氧气  {resources.oxygen:0.#} / {resources.oxygenMax:0.#}";
        formCooldownText.text = $"转换 CD  {player.FormCooldownRemaining:0.0} 秒";
        stageText.text = stage.HasExploration ? (stage.IsSimulation ? "探索 · 模拟" : "探索 · 漫游")
            : stage.Stage == PlayerGameplayStage.Escape ? "逃生" : "教学";
    }
}
