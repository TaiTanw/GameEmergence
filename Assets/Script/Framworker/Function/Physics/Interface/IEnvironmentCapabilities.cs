using PhyData;
using UnityEngine;

/// <summary>
/// 让脚下/区域入口取得来源的 Registration；例如冰面经旧基类提供这一入口。
/// 职能：来源接入协议，不代表一定施力。消费位置见 Context.CollectSources（核对事实，ENV-02）。
/// </summary>
public interface IEnvironmentSource
{
    /// <summary>来源当前是否允许提供环境效果；实现方解释生命周期，不回读注册表 IsActive。</summary>
    bool IsEnvironmentActive { get; }
    EnvironmentRegistration EnvironmentRegistration { get; }
}

/// <summary>
/// 让区域或显式入口取得实体自己的 Context，而不是把登记单保存在场景来源上。
/// 职能：实体接收协议；BasicEntity 实现，Registration 的 ENV-R1 消费。
/// </summary>
public interface IEnvironmentReceiver
{
    EntityEnvironmentContext EnvironmentContext { get; }
}

/// <summary>提供主动移速加性修饰；ENV-04 登记，实体数值状态求和/限幅后按旧规则使用 1 + offset。职能：参数能力。</summary>
public interface IMovementSpeedModifier { float MovementSpeedOffset { get; } }

/// <summary>提供持续附加速度；ENV-04 登记到实体数值状态，ENV-06 按来源相加，不乘 EnvImpact。职能：参数能力。</summary>
public interface IStateVelocitySource { Vector2 StateVelocity { get; } }

/// <summary>
/// 让冰面/力场按受力者生成初始 ForceData；ENV-04 调用，具体例子见 IceGround 的 ENV-05。
/// 职能：动态算法初始化能力；后续 ForceCalculation 仍走继承的 IDynamicAddForce 协议。
/// </summary>
public interface IDynamicEnvironmentForce : IDynamicAddForce
{
    ForceData CreateEnvironmentForce(IForceAction receiver);
}

/// <summary>提供脚下的消退阻力倍率与起跳加成，Context.SampleGroundFrame 采到 ENV-S1。职能：表面读值能力，不决定是否着地。</summary>
public interface IGroundResponse
{
    float SlowingEffect { get; }
    float JumpHeightNum { get; }
}

/// <summary>提供平台本帧已走的世界位移；ENV-S1 采样后由 SpeedCalculation 一次并入运动快照。职能：平台位移读值能力，单位不是速度。</summary>
public interface IPlatformMotion { Vector2 Delta { get; } }

/// <summary>提供墙滑倍率，BasePhysicsEntity 解析、CharacterPhysics 消费。职能：墙滑能力；有效接口存在才可墙滑，普通墙仍可阻挡。</summary>
public interface IWallSlideSurface { float WallSlideMultiplier { get; } }

/// <summary>
/// 职能：区域水事实采样契约，不登记速度或资源。
/// 同一来源分别回答当前是否允许游泳，以及指定采样点是否进入它的有效水域。
/// </summary>
public interface IWaterRegion
{
    bool SwimmingEnabled { get; }
    bool ContainsWaterPoint(Collider2D region, Vector2 point);
    /// <summary>在指定世界 X 读取可用的水平水面；不支持的形状返回 false。</summary>
    bool TryGetSurfaceY(Collider2D region, float worldX, out float surfaceY);
}
