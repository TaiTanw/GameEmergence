public enum PlayerDeathCause { OxygenDepleted, Abyss, Electrocuted, Crushed }
public enum PlayerGameplayStage { Tutorial, Exploration, Escape, Inactive }
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
public interface IPlayerTutorialRefreshRules { bool TryRestartTeaching(Player deadPlayer); }

/// <summary>
/// 精神体执行模块的接入口；开启屏蔽重力、外部效果和实心阻挡，仍走统一原生模拟，
/// 飞转行仅校验当前占用，通过后恢复碰撞并重采样环境。认知结算留在规则侧。
/// </summary>
public interface IPlayerFlightExecution { bool TrySetFlying(bool flying); }
