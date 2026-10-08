using UnityEngine;

/// <summary>
/// 职能：角色侧持有的箱子选中关系与本次交互目标；不读输入设备、不改箱子物理，只经 IPlayerInteractable 请求箱子提交自身状态。
/// 具体：点按选中、再次点按解除、选中后长按交给箱子收纳、点按收纳箱取出。跳跃、撤权等退出由 Player 调用 Release，
/// 关系立即消失；箱子本身不记录“被谁选中”。按键消费与提示预览共用 ResolveTarget，保证高光的就是按键作用的那一只。
/// </summary>
public sealed class PlayerBoxSelection
{
    private readonly Player owner;
    public PhysicalBox Selected { get; private set; }

    public PlayerBoxSelection(Player owner) => this.owner = owner;

    /// <summary>本次按键将作用的箱子：已有选中优先；否则限地面状态，按候选规则找外部箱或收纳箱。</summary>
    public PhysicalBox ResolveTarget()
    {
        if (Selected != null) return Selected;
        // 实际逻辑：新交互限定地面行走状态；空中、贴墙、游泳和飞行没有目标。
        if (!owner.OnGroundForInteraction) return null;
        return LevelBoxContext.FindInteractTarget(owner.BodyBounds, owner.InteractContactGap, owner.StoredReachGap);
    }

    public void Consume(PlayerInteractKind kind)
    {
        if (Selected == null)
        {
            PhysicalBox candidate = ResolveTarget();
            // 实际逻辑：箱子同意被选中才建立关系；拒绝或取出时不建立选中。
            if (candidate != null && candidate.Interact(new PlayerInteractRequest(owner, kind, false))
                == PlayerInteractOutcome.Selected)
                Selected = candidate;
            return;
        }
        // 实际逻辑：再次点按只解除关系，不通知箱子。
        if (kind == PlayerInteractKind.Tap) { Release(); return; }
        // 实际逻辑：长按交给选中箱收纳；成功后选中随之断开，拒绝时保持原关系。
        if (Selected.Interact(new PlayerInteractRequest(owner, kind, true)) == PlayerInteractOutcome.Stored)
            Release();
    }

    /// <summary>预览本次按键的目标与预期结果；只读判定，不改任何状态。</summary>
    public PlayerInteractHint Preview(out PhysicalBox target)
    {
        target = ResolveTarget();
        if (target == null) return PlayerInteractHint.None;
        if (target == Selected) return PlayerInteractHint.Selected;
        if (!target.IsStored) return PlayerInteractHint.Select;
        switch (target.CheckRestore(owner, out _))
        {
            case PhysicalBox.RestoreBlock.Requester: return PlayerInteractHint.RestoreBlockedBySelf;
            case PhysicalBox.RestoreBlock.Other: return PlayerInteractHint.RestoreBlockedByOther;
            default: return PlayerInteractHint.Restore;
        }
    }

    public void Release()
    {
        Selected = null;
        owner.ReleasePushPullConstraint();
    }
}
