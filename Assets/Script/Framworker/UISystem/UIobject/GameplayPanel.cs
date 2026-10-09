using UnityEngine.UI;

/// <summary>游戏显示预设的只读绑定；资源和阶段仍归玩家及关卡规则，界面不另建计时或余额。</summary>
public class GameplayPanel : BasePanel
{
    Text cognitionText, staminaText, oxygenText, formCooldownText, stageText;

    protected override void OtherInit()
    {
        cognitionText = FindUIObj<Text>("CognitionText");
        staminaText = FindUIObj<Text>("StaminaText");
        oxygenText = FindUIObj<Text>("OxygenText");
        formCooldownText = FindUIObj<Text>("FormCooldownText");
        stageText = FindUIObj<Text>("StageText");
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
        // 安全：加载交接时没有控制对象，沿用占位显示，不用旧玩家继续显示资源。
        if (player == null)
        {
            cognitionText.text = staminaText.text = oxygenText.text = formCooldownText.text = "—";
            stageText.text = "";
            return;
        }
        LevelStageMgr stage = LevelStageMgr.Instance;
        PlayerResourceFacts resources = player.ResourceFacts;
        cognitionText.text = stage.HasExploration ? $"认知  {stage.CognitionRemaining:0.##}" : "认知  —";
        staminaText.text = $"体力  {resources.stamina:0.#} / {resources.staminaMax:0.#}";
        oxygenText.text = $"氧气  {resources.oxygen:0.#} / {resources.oxygenMax:0.#}";
        formCooldownText.text = $"转换 CD  {player.FormCooldownRemaining:0.0} 秒";
        stageText.text = stage.HasExploration ? (stage.IsSimulation ? "探索 · 模拟" : "探索 · 漫游")
            : stage.Stage == PlayerGameplayStage.Escape ? "逃生" : "教学";
    }
}
