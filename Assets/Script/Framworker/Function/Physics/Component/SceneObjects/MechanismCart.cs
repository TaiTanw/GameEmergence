using System.Collections.Generic;
using UnityEngine;

/// <summary>固定方向的小车：只提供水平驱动，重力、惯性和推箱均由 BasicEntity 执行。</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
[DisallowMultipleComponent]
public sealed class MechanismCart : BasicEntity, ICanMove, IPlayerInteractable
{
    public enum Direction { Left = -1, Right = 1 }
    [Header("小车开关与水平驱动")]
    [SerializeField, Tooltip("固定向左／向右；玩家交互只开关，不改变方向。")]
    Direction direction = Direction.Right;
    [SerializeField, Tooltip("主动水平速度，必须大于零；环境修饰仍经现有实体执行。")]
    float speed = 3f;
    [SerializeField, Tooltip("本关默认开关；试验复位与失败恢复回到此值。")]
    bool initiallyOn;
    [SerializeField, Tooltip("玩家碰撞框距小车的最大交互间隙，必须非负。")]
    float interactionReach = .6f;
    static readonly List<MechanismCart> carts = new List<MechanismCart>();
    bool isOn, configurationValid, defaultsCaptured;
    Vector2 defaultPosition;
    public bool IsOn => isOn;
    public float MovingDirection => isOn ? (float)direction : 0f;
    public float Mobility => speed;
    protected override bool MotionExecutionReady => configurationValid;
    protected override bool HasHorizontalDrive => true;

    public bool ValidateConfiguration(out string error)
    {
        error = null;
        BoxCollider2D shape = GetComponent<BoxCollider2D>();
        if (direction != Direction.Left && direction != Direction.Right)
            error = $"{name}：小车方向只允许 Left／Right。";
        else if (!Finite(speed) || speed <= 0f || !Finite(interactionReach) || interactionReach < 0f)
            error = $"{name}：小车速度必须大于零，交互间隙必须非负，且均为有限数值。";
        else if (cPhysics == null || groundV == null || leftV == null || rightV == null || upV == null)
            error = $"{name}：小车物理配置及四个探针必须明确关联。";
        else if (shape == null || shape.isTrigger || gameObject.layer == Player.PlayerLayer
            || (shape.excludeLayers.value & (1 << Player.PlayerLayer)) == 0)
            error = $"{name}：小车须使用实心框、世界渲染层，并在碰撞框 Exclude Layers 排除 Player。";
        return error == null;
    }

    protected override void Awake()
    {
        configurationValid = ValidateConfiguration(out string error);
        // 安全：配置错误拒绝运动，不补探针或切层修正制作数据。
        if (!configurationValid) Debug.LogError(error, this);
        base.Awake();
        CaptureDefaults();
        isOn = initiallyOn;
    }

    void CaptureDefaults()
    {
        if (defaultsCaptured) return;
        defaultPosition = transform.position;
        defaultsCaptured = true;
    }

    public void RestoreDefault()
    {
        CaptureDefaults();
        isOn = initiallyOn;
        // 生命周期：初始禁用、尚未 Awake 的车只恢复制作位置，首次启用仍由 Awake 组装。
        if (rb == null) { transform.position = new Vector3(defaultPosition.x, defaultPosition.y, transform.position.z); return; }
        RefreshMotionState(defaultPosition);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!carts.Contains(this)) carts.Add(this);
    }

    protected override void OnDisable()
    {
        carts.Remove(this);
        base.OnDisable();
    }

    public PlayerInteractOutcome Interact(in PlayerInteractRequest request)
    {
        // 安全：只接运行中、可交互的行走角色的点按，长按不切开关。
        if (request.kind != PlayerInteractKind.Tap || !CanInteract(request.requester)) return PlayerInteractOutcome.Rejected;
        isOn = !isOn;
        if (!isOn) ReleaseHorizontalDrive();
        return PlayerInteractOutcome.Activated;
    }

    bool CanInteract(Player player)
    {
        if (!IsMotionParticipating || player == null || !player.isActiveAndEnabled
            || !player.IsInGameplayScene(gameObject.scene) || player.ResolveFourWayAbility() == FourWayMode.Flying
            || !player.Permissions.movement || !player.Permissions.motionUpdates) return false;
        Collider2D body = player.GetComponent<Collider2D>();
        if (body == null || !body.isActiveAndEnabled) return false;
        ColliderDistance2D distance = boxCollider.Distance(body);
        return distance.isValid && distance.distance <= interactionReach;
    }

    public static MechanismCart FindInteractTarget(Player player)
    {
        MechanismCart best = null;
        float nearest = float.PositiveInfinity;
        foreach (MechanismCart cart in carts)
        {
            if (cart == null || !cart.CanInteract(player)) continue;
            float distance = cart.boxCollider.Distance(player.GetComponent<Collider2D>()).distance;
            // 实际逻辑：范围内最近一辆独占本次点按，不再交给道具。
            if (distance >= nearest) continue;
            nearest = distance;
            best = cart;
        }
        return best;
    }

    protected override void HActiveSpeedOperation()
        => playerPhysicsData.horizontalSpeed = MovingDirection * speed * (1f + playerPhysicsData.nowPhyNum);

    protected override void GeometricQuery()
    {
        // 安全：无效配置不进入依赖探针的采样。
        if (!configurationValid) return;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(cPhysics.groundLayer.value & ~boxCollider.excludeLayers.value);
        RaycastHit2D hit = FindGroundHit(ProbePosition(groundV), cPhysics.boxCastH, 0f, Vector2.down, filter);
        bool supported = hit.collider != null && Vector2.Dot(hit.normal, Vector2.up) > .7f;
        nowGemetry.isGrounded = supported;
        nowGemetry.groundCollider = supported ? hit.collider : null;
        nowGemetry.groundNormal = hit.normal;
        self_resistanceCoefficient = supported ? 20f : 1f;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
