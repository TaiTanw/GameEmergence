/// <summary>关序与场景性质记录；模式事实只代理场景宿主组装的规则，不另存一份阶段。</summary>
public class LevelStageMgr : BaseMgr<LevelStageMgr>
{
    public int LevelIndex { get; private set; }
    public bool Teaching { get; private set; }
    PlayerLifecycleRules rules;
    public PlayerGameplayStage Stage => rules?.Stage ?? PlayerGameplayStage.Tutorial;
    public bool IsSimulation => rules != null && rules.IsSimulation;
    public bool HasExploration => rules != null && rules.Stage == PlayerGameplayStage.Exploration;
    public float CognitionRemaining => rules?.Cognition.Remaining ?? 0f;
    public bool CognitionExhausted => rules != null && rules.Cognition.IsExhausted;

    internal void BindRules(PlayerLifecycleRules value) => rules = value;

    public void Note(int index, bool teaching)
    {
        LevelIndex = index;
        Teaching = teaching;
    }
}
