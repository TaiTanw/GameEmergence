using System.Collections.Generic;
using UnityEngine;

/// <summary>相位 2 汇总薄触发区中的脚底重叠；不阻挡推箱，机关只发送状态，移动由目标完成。</summary>
[RequireComponent(typeof(BoxCollider2D))]
[DisallowMultipleComponent]
public sealed class MechanismPressurePlate : MonoBehaviour
{
    public enum PressureKind { Ordinary, ElectrifiedBoxOnly }
    [SerializeField, Tooltip("普通：玩家、箱子、小车；带电：只认 PhysicalBox 的带电配置。")]
    PressureKind kind;
    [SerializeField, Tooltip("明确拖入实现 IMechanismTarget 的组件；同关一个目标只允许一个压感控制。")]
    MonoBehaviour target;
    [SerializeField, Tooltip("脚底须落在薄触发区的上下边界内；此高度容差必须非负。身体侧擦／底部穿过不触发。")]
    float topTolerance = .04f;
    static bool worldPaused;
    bool configurationChecked, configurationValid;
    readonly List<Collider2D> overlaps = new List<Collider2D>();
    readonly HashSet<BasicEntity> occupants = new HashSet<BasicEntity>();
    BoxCollider2D shape;
    string lastError;
    public bool IsPressed { get; private set; }
    public int OccupantCount => occupants.Count;
    public MonoBehaviour TargetComponent => target;
    public PressureKind Kind => kind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => worldPaused = false;
    public static void SetWorldPaused(bool paused) => worldPaused = paused;

    public bool ValidateConfiguration(out string error)
    {
        error = null;
        BoxCollider2D geometry = GetComponent<BoxCollider2D>();
        if (geometry == null || !geometry.isTrigger || Mathf.Abs(Vector2.Dot(transform.up, Vector2.up) - 1f) > .001f)
            error = $"{name}：压感必须是水平、顶部朝上的触发器 BoxCollider2D。";
        else if (!Finite(topTolerance) || topTolerance < 0f)
            error = $"{name}：顶部容差必须是非负有限数值。";
        else if (kind != PressureKind.Ordinary && kind != PressureKind.ElectrifiedBoxOnly)
            error = $"{name}：压感类型配置无效。";
        else if (target == null || !(target is IMechanismTarget) || target.gameObject.scene != gameObject.scene)
            error = $"{name}：必须关联本关的 IMechanismTarget 组件。";
        else
        {
            // 配置归属：包括禁用的机关，不能按启用顺序默选一个控制者。
            if (gameObject.scene.IsValid()) foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (MechanismPressurePlate other in root.GetComponentsInChildren<MechanismPressurePlate>(true))
                    if (other != this && other.target == target)
                    { error = $"{name} 与 {other.name}：重复控制目标 {target.name}，一个目标只允许一个机关。"; return false; }
            if (target is Taijie platform && !platform.ValidateMechanismConfiguration(out error)) return false;
        }
        return error == null;
    }

    void OnEnable()
    {
        shape = GetComponent<BoxCollider2D>();
        configurationChecked = false;
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(SamplePressure, 2);
    }

    void OnDisable()
    {
        occupants.Clear();
        // 生命周期：停用压感释放已有命令，暂停本身不走 OnDisable。
        if (IsPressed && target != null && target is IMechanismTarget receiver) receiver.TrySetMechanismActive(false);
        IsPressed = false;
        if (!MonoPublicMgr.IsQuitting) MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(SamplePressure, 2);
    }

    void SamplePressure()
    {
        if (worldPaused) return;
        // 安全：配置错误显式报错并拒绝命令，同一错误不每步刷屏。
        if (!configurationChecked)
        {
            configurationValid = ValidateConfiguration(out string error);
            configurationChecked = true;
            if (!configurationValid && error != lastError) Debug.LogError(error, this);
            lastError = error;
        }
        if (!configurationValid || target == null || !target.isActiveAndEnabled) return;
        occupants.Clear(); overlaps.Clear();
        if (shape.isActiveAndEnabled)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer) & ~shape.excludeLayers.value);
            shape.OverlapCollider(filter, overlaps);
            Bounds zone = shape.bounds;
            foreach (Collider2D other in overlaps)
            {
                if (other == null || !other.isActiveAndEnabled || other.isTrigger) continue;
                if ((other.excludeLayers.value & (1 << gameObject.layer)) != 0 || Physics2D.GetIgnoreCollision(shape, other)) continue;
                ColliderDistance2D distance = shape.Distance(other);
                // 安全：每步查实际重叠，移走／收纳后立即释放，不依赖进入退出回调缓存。
                if (!distance.isValid || !distance.isOverlapped) continue;
                BasicEntity entity = other.GetComponentInParent<BasicEntity>();
                if (entity == null || !entity.IsMotionParticipating) continue;
                Bounds body = entity.BodyBounds;
                // 压住事实：物体在上方且脚底进入薄触发区；仅腰部擦过或从下方经过不触发。
                if (body.center.y <= zone.max.y || body.min.y < zone.min.y - topTolerance
                    || body.min.y > zone.max.y + topTolerance) continue;
                if (!Accepts(entity)) continue;
                occupants.Add(entity);
            }
        }
        bool pressed = occupants.Count > 0;
        // 聚合：只有第一个压上或最后一个离开时才改变目标，不重启每步缓动。
        if (pressed == IsPressed) return;
        if (((IMechanismTarget)target).TrySetMechanismActive(pressed)) IsPressed = pressed;
    }

    bool Accepts(BasicEntity entity)
    {
        if (kind == PressureKind.ElectrifiedBoxOnly) return entity is PhysicalBox box && box.IsElectrified;
        Player player = entity.GetComponent<Player>();
        if (player != null) return player.ResolveFourWayAbility() != FourWayMode.Flying && player.Permissions.motionUpdates;
        return entity is PhysicalBox || entity is MechanismCart;
    }

    public void RestoreDefault()
    {
        occupants.Clear(); overlaps.Clear();
        IsPressed = false;
        lastError = null;
        configurationChecked = false;
    }

#if UNITY_EDITOR
    void OnValidate() => configurationChecked = false;
#endif

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
