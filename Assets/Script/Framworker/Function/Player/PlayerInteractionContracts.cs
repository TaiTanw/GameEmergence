/// <summary>交互键已判定的一次结果；点按与长按由 InputControlMgr 保证互斥，长按成功不补发点按。</summary>
public enum PlayerInteractKind { Tap, LongPress }

/// <summary>接收者对一次交互的处理结果。Rejected 表示接收者状态完全未变。</summary>
public enum PlayerInteractOutcome { Rejected, Selected, Stored, Restored, Activated }

/// <summary>
/// 本次按键将作用的目标及其预期结果，供表现层显示单目标高光与提示；与按键消费共用同一候选规则。
/// RestoreBlockedBySelf 表示玩家自己占着恢复位置，RestoreBlockedByOther 表示其它实体或地形占位。
/// </summary>
public enum PlayerInteractHint { None, Select, Selected, Restore, RestoreBlockedBySelf, RestoreBlockedByOther }

/// <summary>
/// 职能：有限交互契约的输入快照；不携带输入设备、阶段或格子数据。
/// 具体：谁按的、点按还是长按，以及请求者此刻是否已选中该对象。例如已选中箱子后的长按表示收纳；
/// 未选中时点按外部箱表示请求选中，点按内部箱表示拉出。
/// </summary>
public readonly struct PlayerInteractRequest
{
    public readonly Player requester;
    public readonly PlayerInteractKind kind;
    public readonly bool requesterSelectsThis;

    public PlayerInteractRequest(Player requester, PlayerInteractKind kind, bool requesterSelectsThis)
    { this.requester = requester; this.kind = kind; this.requesterSelectsThis = requesterSelectsThis; }
}

/// <summary>
/// 场景对象接收玩家交互的窄口；普通箱子为首个实现，其它对象以后接同一口，不另建注册框架。
/// 接收者只决定并提交自身状态（可否被选中、收纳、拉出），选中关系归玩家侧持有。
/// </summary>
public interface IPlayerInteractable
{
    PlayerInteractOutcome Interact(in PlayerInteractRequest request);
}
