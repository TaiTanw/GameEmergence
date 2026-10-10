using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 为接触到本物体的角色提供墙滑倍率；无此组件的普通墙仍可裁剪位移。
/// 职能：具体表面能力提供者；墙滑参数由实体直接查询，不通过动态施力 Binding。
/// 墙顶支撑由几何查询判断；本组件不提供地面阻力倍率或起跳加成。
/// </summary>
public class Wall : BasicPhysicalObject, IWallSlideSurface
{
    /// <summary>
    /// 是否可以攀爬
    /// </summary>
    [SerializeField]
    bool canClimb;
    /// <summary>
    /// 墙跳距离加成（-1
    /// </summary>
    [SerializeField]
    float wallJumpDistance;
    /// <summary>
    /// 墙跳高度加持
    /// </summary>
    [SerializeField]
    float wallJumpHeight;
    /// <summary>
    /// 贴墙下滑影响程度（必须是正数，1表示无特殊影响
    /// </summary>
    [SerializeField]
    float wallFriction=1f;

    public float WallFRICTION => WallSlideMultiplier;
    public float WallSlideMultiplier => TryGetWallSlideMultiplier(out float value) ? value : 1f;
    protected override void AddLegacyEnvironmentParameters(List<EnvironmentParameter> parameters) =>
        AddLegacyEnvironmentParameter(parameters, new WallSlideMultiplierParameter(wallFriction));
}
