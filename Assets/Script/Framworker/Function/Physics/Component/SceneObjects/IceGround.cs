using PhyData;
using UnityEngine;

/// <summary>
/// 踩上冰面时，按受力者是否能主动移动给出初始参数；在冰面内根据输入施力或受控减速。
/// 职能：具体环境算法提供者。参数登记与清理由 Context 处理，实际惯性留在刚体。
/// 第一遍只读 CreateEnvironmentForce；理解实体 ENV-06 的消费循环后，再回来看 ForceCalculation。
/// </summary>
public class IceGround : BaseGround, IDynamicEnvironmentForce, IMovementSpeedModifier, IStateVelocitySource
{
    [SerializeField] protected float speedChangeNum; // 主动速度加性修饰，旧序列化名称保留。
    [SerializeField] protected Vector2 phySpeed; // 持续速度，非逐帧积分的力。
    public float MovementSpeedOffset => speedChangeNum;
    public Vector2 StateVelocity => phySpeed;

    /// <summary>加速影响因子（保留旧序列化字段）。</summary>
    public float Idex = 1;
    /// <summary>输入松开时的受控减速因子。</summary>
    public float Odex = 1;
    /// <summary>最大滑动速度系数，参与原有平衡速度公式。</summary>
    public float maxISpeed = 1;

    /// <summary>[ENV-05] 给 ENV-04 返回初始 ForceData；只产生参数，不在冰面上保存每个实体的累计速度。</summary>
    public ForceData CreateEnvironmentForce(IForceAction receiver)
    {
        ForceData force = new ForceData();
        // 保留旧公式：speedChangeNum 同时参与主动修饰和冰面平衡速度。
        // 自由箱以 Odex 与 slowingEffect 提供对称制动。
        // 算法分支：角色等有主动移动能力，使用其 Mobility；箱子走下方原无移动能力公式。
        if (receiver is ICanMove moving)
            force.Init(Idex, Odex, moving.Mobility * maxISpeed * speedChangeNum);
        else
        {
            // 自由箱双向滑动对称制动，保持原生接触带来的真实惯性。
            force.Init(0f, Mathf.Max(0f, Odex * slowingEffect), 0f);
            force.type = E_PhyForceType.controlRecovery;
        }
        return force;
    }

    /// <summary>相位 3 由实体 ENV-06 回调：修改该实体中的控制参数，随后实体从实际刚体速度计算本步加速度。</summary>
    public void ForceCalculation(IForceAction receiver)
    {
        // 算法分支：无主动移动能力者不做输入控制，保留初始化状态。
        if (receiver is ICanMove moving)
        {
            float direction = moving.MovingDirection;
            // 有方向输入则施力；松开输入则切受控减速。离开后撤销环境控制，真实惯性保留。
            if (direction != 0)
            {
                receiver.ChangeForce(this, direction * Idex);
                receiver.ChangeType(this, E_PhyForceType.apply);
            }
            else receiver.ChangeType(this, E_PhyForceType.controlRecovery);
        }
    }
}
