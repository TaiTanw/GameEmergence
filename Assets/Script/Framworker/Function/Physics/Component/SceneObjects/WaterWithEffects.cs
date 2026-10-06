using UnityEngine;

/// <summary>
/// 职能：带固定物理效果的水宿主，沿原环境 Binding 提供减速与二维持续流速。
/// 和纯水使用同一种淹没采样；不再叠第二个 ForceField 宿主。重叠内改效果参数仍归 K_7。
/// </summary>
public sealed class WaterWithEffects : WaterRegion, IMovementSpeedModifier, IStateVelocitySource
{
    [SerializeField, Range(-1f, 1f)] private float movementSpeedOffset = -0.25f;
    [SerializeField] private Vector2 flowVelocity;
    public float MovementSpeedOffset => movementSpeedOffset;
    public Vector2 StateVelocity => flowVelocity;
}
