using PhyData;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
/// <summary>
/// 动态施力（施力物体可有
/// </summary>
public interface IDynamicAddForce 
{
    /// <summary>
    /// 动态施力规则
    /// </summary>
    public void ForceCalculation(IForceAction IF);

}

/// <summary>
/// 受力实体的效果写入口；来源关系由环境上下文管理，实际运动由刚体持有。
/// </summary>
public interface IForceAction
{
    /// <summary>
    /// 粘滞力注册
    /// </summary>
    /// <param name="id">环境来源身份</param>
    /// <param name="num">大小</param>
    public void StatePowerRegistration(EnvironmentRegistration id,float num);
    /// <summary>
    /// 粘滞力注销
    /// </summary>
    /// <param name="id">环境来源身份</param>
    public void StatePowerCancellation(EnvironmentRegistration id);
    /// <summary>
    /// 状态速度添加
    /// </summary>
    /// <param name="iD">环境来源身份</param>
    /// <param name="force">速度大小</param>
    public void AddSpeedStatus(EnvironmentRegistration iD, Vector2 force);
    /// <summary>
    /// 状态速度移除
    /// </summary>
    /// <param name="iD">环境来源身份</param>
    public void RemoveSpeedStatus(EnvironmentRegistration iD);
    /// <summary>
    /// 添加限时速度（水平
    /// </summary>
    /// <param name="time">持续时间</param>
    /// <param name="addSpeed">速度大小</param>
    public void AddTimeSpeed(float time, float addSpeed);

    /// <summary>
    /// 登记来源提供的初始动态力数据；本步从刚体实际速度计算环境加速度。
    /// </summary>
    /// <param name="iD">动态力来源</param>
    /// <param name="force">初始数据</param>
    public void AddForce(IDynamicAddForce iD, ForceData force);

    /// <summary>
    /// 只改变已登记来源的施力大小，保留其余参数和类型。
    /// </summary>
    /// <param name="iD">动态力来源</param>
    /// <param name="newForce">新的施力大小</param>
    public void ChangeForce(IDynamicAddForce iD, float newForce);

    /// <summary>
    /// 切换已登记来源的本步控制类型。
    /// </summary>
    /// <param name="iD">动态力来源</param>
    /// <param name="type">新的受力类型</param>
    public void ChangeType(IDynamicAddForce iD, E_PhyForceType type);
    /// <summary>
    /// 来源退出控制，获得的真实惯性仍由刚体保留。
    /// </summary>
    /// <param name="iD">动态力来源</param>
    public void RemoveForce(IDynamicAddForce iD);


}

/// <summary>
/// 可移动能力（受力物体可有
/// </summary>
public interface ICanMove
{
    /// <summary>
    /// 移动方向
    /// </summary>
    public float MovingDirection { get; }
    /// <summary>
    /// 移动能力
    /// </summary>
    public float Mobility { get; }
}
