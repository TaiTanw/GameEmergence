using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
/// <summary>
/// 旧地面家族共用的消退阻力倍率和起跳加成；同时允许实体从脚下支撑登记持续效果。
/// 职能：旧场景参数兼容层，通过 IGroundResponse 给 ENV-S1 提供读值。
/// </summary>
public class BaseGround : BasicPhysicalObject, IGroundResponse
{

    /// <summary>
    /// 减速影响因子（处于此地面时，对消退类型力的衰减幅度）
    /// </summary>
    [SerializeField]
    protected float slowingEffect = 1;
    public float SlowingEffect=>slowingEffect;
    /// <summary>
    /// 跳跃高度速度影响百分比
    /// </summary>
    [SerializeField]
    protected float jumpHeightNum ;
    public float JumpHeightNum=>jumpHeightNum;

}
