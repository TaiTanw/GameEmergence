/// <summary>
/// 职能：角色侧持有的箱子选中关系；不读输入设备、不改箱子物理，只经 IPlayerInteractable 请求箱子提交自身状态。
/// 具体：点按选中、再次点按解除、选中后长按交给箱子收纳。跳跃、撤权等退出由 Player 调用 Release，
/// 关系立即消失；箱子本身不记录“被谁选中”。
/// </summary>
public sealed class PlayerBoxSelection
{
    private readonly Player owner;
    public PhysicalBox Selected { get; private set; }

    public PlayerBoxSelection(Player owner) => this.owner = owner;

    public void Consume(PlayerInteractKind kind)
    {
        if (Selected == null)
        {
            // 实际逻辑：新交互限定地面行走状态；空中、贴墙、游泳和飞行不发现候选。
            if (!owner.OnGroundForInteraction) return;
            PhysicalBox candidate = LevelBoxContext.FindSideContact(owner.BodyBounds, owner.InteractContactGap);
            // 实际逻辑：箱子同意被选中才建立关系；拒绝时双方状态不变。
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

    public void Release() => Selected = null;
}
