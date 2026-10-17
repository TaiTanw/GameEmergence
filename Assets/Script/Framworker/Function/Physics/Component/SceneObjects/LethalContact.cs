using System.Collections.Generic;
using UnityEngine;

/// <summary>带电箱和固定危险共用真实几何查询；只返回死亡原因，不传播危险或结算死亡。</summary>
public static class LethalContact
{
    static readonly Dictionary<Component, Collider2D> sources = new Dictionary<Component, Collider2D>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => sources.Clear();
    public static void Register(Component owner, Collider2D geometry) => sources[owner] = geometry;
    public static void Unregister(Component owner) => sources.Remove(owner);

    public static bool Touches(Player player, float tolerance) => TryGetDeathCause(player, tolerance, out _);

    public static bool TryGetDeathCause(Player player, float tolerance, out PlayerDeathCause cause)
    {
        cause = default;
        // 安全：角色及实际碰撞框就绪后才查询。
        if (player == null) return false;
        Collider2D body = player.GetComponent<Collider2D>();
        if (body == null || !body.isActiveAndEnabled) return false;
        foreach (KeyValuePair<Component, Collider2D> source in sources)
        {
            Component owner = source.Key;
            Collider2D geometry = source.Value;
            // 安全：跨关、停用、收纳及无效固定配置不构成危险源。
            if (owner == null || geometry == null || !geometry.isActiveAndEnabled
                || !player.IsInGameplayScene(owner.gameObject.scene)) continue;
            PlayerDeathCause sourceCause;
            if (owner is PhysicalBox box)
            {
                if (!box.IsElectrified || !box.IsMotionParticipating) continue;
                sourceCause = PlayerDeathCause.Electrocuted;
            }
            else if (owner is FixedContactHazard hazard)
            {
                if (!hazard.IsHazardActive || !hazard.ValidateConfiguration(out _)) continue;
                sourceCause = PlayerDeathCause.ContactHazard;
            }
            else continue;
            ColliderDistance2D distance = geometry.Distance(body);
            // 触发器必须真正重叠；实心接触用小容差承接原生接触皮肤，不用包围盒代替形状。
            if (distance.isValid && (geometry.isTrigger ? distance.isOverlapped : distance.distance <= tolerance))
            { cause = sourceCause; return true; }
        }
        return false;
    }
}
