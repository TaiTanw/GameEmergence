using System.Collections.Generic;
using UnityEngine;

/// <summary>带电箱和固定环境共用的真实几何电击查询；不传播电，也不负责死亡结算。</summary>
public static class ElectricContact
{
    static readonly Dictionary<Component, Collider2D> sources = new Dictionary<Component, Collider2D>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => sources.Clear();
    public static void Register(Component owner, Collider2D geometry) => sources[owner] = geometry;
    public static void Unregister(Component owner) => sources.Remove(owner);

    public static bool Touches(Player player, float tolerance)
    {
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
            if (owner is PhysicalBox box)
            { if (!box.IsElectrified || !box.IsMotionParticipating) continue; }
            else if (owner is FixedElectricHazard hazard)
            { if (!hazard.IsHazardActive || !hazard.ValidateConfiguration(out _)) continue; }
            else continue;
            ColliderDistance2D distance = geometry.Distance(body);
            // 触发器必须真正重叠；实心接触用小容差承接原生接触皮肤，不用包围盒代替形状。
            if (distance.isValid && (geometry.isTrigger ? distance.isOverlapped : distance.distance <= tolerance)) return true;
        }
        return false;
    }
}
