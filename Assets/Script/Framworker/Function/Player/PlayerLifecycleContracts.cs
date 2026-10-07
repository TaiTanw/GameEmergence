public enum PlayerDeathCause { OxygenDepleted, Abyss, Electrocuted }
public enum PlayerGameplayStage { Tutorial, Exploration, Escape }
public enum PlayerFormChangeReason { Manual, Death, ForcedRegion }
public enum PlayerDeathStatus { Pending, Handled, Rejected }

/// <summary>
/// 职能：角色向规则层发出的单次死亡请求，保留死者和周期身份。
/// 氧气和深渊共用这个入口；控制切换不改变结算对象。
/// </summary>
public readonly struct PlayerDeathRequest
{
    public readonly Player subject;
    public readonly PlayerDeathCause cause;
    public readonly uint cycle;

    public PlayerDeathRequest(Player subject, PlayerDeathCause cause, uint cycle)
    { this.subject = subject; this.cause = cause; this.cycle = cycle; }
}

public interface IPlayerDeathReceiver { PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request); }

/// <summary>由精神体规则实现：统一形态入口同时负责安全执行和认知扣费，成功才确认完成。</summary>
public interface IPlayerFormRules
{
    bool TryChangeForm(Player subject, bool flying, PlayerFormChangeReason reason);
}

/// <summary>由关卡刷新规则实现：完整失败刷新、效果撤销及返回探索全部提交后才返回成功。</summary>
public interface IPlayerRunRefreshRules { bool TryRestartExploration(Player deadPlayer); }

/// <summary>
/// 精神体执行模块的接入口；成功开启须屏蔽重力、外部能力/效果、自定义接触和 Unity 原生阻挡，
/// 退出须恢复碰撞并重采样环境。此协议留给第 6 组，不把普通四向游泳冒充穿墙飞行。
/// </summary>
public interface IPlayerFlightExecution { bool TrySetFlying(bool flying); }
