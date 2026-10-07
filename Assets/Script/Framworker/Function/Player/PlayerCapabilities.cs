using PhyData;

public enum PlayerIdentity { Body, Spirit }
public enum FourWayMode { None, Swimming, Flying, SurfaceSwimming }

/// <summary>
/// 职能：外部规则注入的有限角色许可，不表示当前输入、形态或环境。
/// 待机本体可以暂停运动及资源；改键只暂停输入，不改这一份许可。
/// </summary>
public readonly struct PlayerCapabilityPermissions
{
    public readonly bool movement;
    public readonly bool swimming;
    public readonly bool flight;
    public readonly bool motionUpdates;
    public readonly bool resourceUpdates;
    /// <summary>能否选中箱子并打开推拉能力；关闭时解除现有选中。漫游等阶段由规则层关闭。</summary>
    public readonly bool pushPull;

    public PlayerCapabilityPermissions(bool movement, bool swimming, bool flight,
        bool motionUpdates, bool resourceUpdates, bool pushPull = true)
    {
        this.movement = movement;
        this.swimming = swimming;
        this.flight = flight;
        this.motionUpdates = motionUpdates;
        this.resourceUpdates = resourceUpdates;
        this.pushPull = pushPull;
    }

    public static PlayerCapabilityPermissions Walking => new PlayerCapabilityPermissions(true, true, false, true, true);
    public static PlayerCapabilityPermissions Standby => default;
}

/// <summary>
/// 职能：角色实例的能力汇总；身份只在组装时确定。
/// 形态与水事实在同一方法里按优先级解析，水进出不能覆盖已启用的飞行。
/// </summary>
public sealed class PlayerCapabilityContext
{
    public PlayerIdentity Identity { get; }
    public PlayerCapabilityPermissions Permissions { get; private set; } = PlayerCapabilityPermissions.Walking;
    public bool FlightForm { get; private set; }

    public PlayerCapabilityContext(PlayerIdentity identity) => Identity = identity;
    public void ApplyPermissions(PlayerCapabilityPermissions permissions) => Permissions = permissions;

    internal bool SetFlightForm(bool flying)
    {
        // 玩法约束：实体不能开启飞行，许可与实际形态仍是两份事实。
        if (flying && (Identity != PlayerIdentity.Spirit || !Permissions.flight)) return false;
        FlightForm = flying;
        return true;
    }

    public FourWayMode ResolveFourWay(EntityWaterFrame water)
    {
        // 已确认优先级：飞行屏蔽外部行为注入，水开关不能把它改成游泳。
        if (Identity == PlayerIdentity.Spirit && Permissions.flight && FlightForm) return FourWayMode.Flying;
        if (Permissions.swimming && water.submerged && water.swimmingAvailable) return FourWayMode.Swimming;
        if (Permissions.swimming && !water.submerged && water.surfaceSwimmingAvailable) return FourWayMode.SurfaceSwimming;
        return FourWayMode.None;
    }
}
