using PhyData;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysicalBox : BasicEntity, IPlayerInteractable
{
    [SerializeField, Tooltip("带电方块：拒绝交互，玩家碰到任意表面即请求电击死亡；推动规则同普通箱")]
    private bool electrified;
    public bool IsElectrified => electrified;

    [SerializeField, Tooltip("收纳时显示色乘数")] private float storedTint = .45f;
    private const float RestoreClearance = .02f; // 拉出校验每边内缩，避免贴地或贴邻接触误判为阻挡
    private static readonly List<Collider2D> restoreHits = new List<Collider2D>();
    private bool stored;
    private Bounds storedBounds;
    private SpriteRenderer[] renderers;
    private Color[] baseColors;

    /// <summary>最近一次相位 1 的箱底几何支撑事实。关系规则须另查参与资格，并在双方本步采样后消费。</summary>
    public bool IsSupported => nowGemetry != null && nowGemetry.isGrounded;
    /// <summary>是否处于收纳状态：碰撞器关闭、位置固定，仍可被交互发现。</summary>
    public bool IsStored => stored;
    /// <summary>交互发现与重合判断用的世界矩形：收纳时为收纳瞬间缓存的碰撞矩形，否则为当前碰撞框。</summary>
    public Bounds InteractionBounds => stored ? storedBounds : BodyBounds;

    /// <summary>箱子对玩家交互的处理：点按选中外部箱或拉出内部箱；已选中者长按收纳。</summary>
    public PlayerInteractOutcome Interact(in PlayerInteractRequest request)
    {
        // 实际逻辑：带电方块拒绝一切交互。
        if (electrified) return PlayerInteractOutcome.Rejected;
        // 实际逻辑：已选中者长按请求收纳，校验通过才提交。
        if (request.kind == PlayerInteractKind.LongPress && request.requesterSelectsThis)
            return TryStore() ? PlayerInteractOutcome.Stored : PlayerInteractOutcome.Rejected;
        // 实际逻辑：未选中者的点按，内部箱请求拉出；外部箱须参与运动且有箱底支撑才可被选中。
        if (request.kind == PlayerInteractKind.Tap && !request.requesterSelectsThis)
        {
            if (stored) return TryRestore() ? PlayerInteractOutcome.Restored : PlayerInteractOutcome.Rejected;
            if (IsMotionParticipating && IsSupported) return PlayerInteractOutcome.Selected;
        }
        return PlayerInteractOutcome.Rejected;
    }

    private bool TryStore()
    {
        // 实际逻辑：只有参与运动的外部箱可收纳；与任一已收纳箱矩形重合则拒绝，状态不变。
        if (stored || !IsMotionParticipating || LevelBoxContext.OverlapsStored(BodyBounds, this)) return false;
        storedBounds = BodyBounds;
        stored = true;
        // 实际逻辑：关闭碰撞器即退出自研与原生运动；再清该箱实体接触，不留推力尾效。
        boxCollider.enabled = false;
        SynchronizeMotionParticipation();
        PhysicsSolverMgr.Instance.ForgetEntityContacts(this);
        SetStoredTint(true);
        return true;
    }

    private bool TryRestore()
    {
        // 实际逻辑：恢复矩形被玩家、其它实体或墙等实心碰撞体挡住时拒绝，状态不变。
        if (IsRestoreBlocked()) return false;
        ExitStored();
        // 实际逻辑：位置在收纳期间固定，原地显式刷新运动并重新采样，不回放收纳前的速度与环境缓存。
        RefreshMotionState();
        return true;
    }

    private bool IsRestoreBlocked()
    {
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(PhysicsWorld.GetLayerCollisionMask(gameObject.layer));
        Vector2 size = (Vector2)storedBounds.size - Vector2.one * (2f * RestoreClearance);
        restoreHits.Clear();
        PhysicsWorld.OverlapBox(storedBounds.center, size, 0f, filter, restoreHits);
        foreach (Collider2D hit in restoreHits)
            // 安全：自身碰撞器此时关闭，仍排除同一刚体上的其它碰撞体。
            if (hit != null && hit.attachedRigidbody != rb) return true;
        return false;
    }

    private void ExitStored()
    {
        stored = false;
        // 实际逻辑：先启用碰撞器，再同步参与，恢复双端运动。
        boxCollider.enabled = true;
        SynchronizeMotionParticipation();
        SetStoredTint(false);
    }

    private void SetStoredTint(bool dim)
    {
        if (renderers == null)
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;
        }
        for (int i = 0; i < renderers.Length; i++)
        {
            // 安全：运行中被移除的表现子节点。
            if (renderers[i] == null) continue;
            Color c = baseColors[i];
            renderers[i].color = dim ? new Color(c.r * storedTint, c.g * storedTint, c.b * storedTint, c.a) : c;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        // 当前推箱解算只处理平移。冻结物理根节点旋转；表现子节点仍可独立旋转。
        rb.freezeRotation = true;
        rb.angularVelocity = 0f;
        defaultPosition = rb.position;
    }

    private Vector2 defaultPosition;

    /// <summary>
    /// 规则侧显式恢复默认：退出收纳、回到首次 Awake 记录的位置，清运动与实体接触并重新采样。
    /// 不做阻挡校验；调用时机由关卡刷新规则决定。暂停、再次启用都不改默认记录。
    /// </summary>
    public void RestoreDefault()
    {
        if (stored) ExitStored();
        RefreshMotionState(defaultPosition);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        LevelBoxContext.Register(this);
    }

    protected override void OnDisable()
    {
        LevelBoxContext.Unregister(this);
        base.OnDisable();
    }

    protected override void GeometricQuery()
    {
        // 接触查询在命中选择前排除 Trigger；箱体沿自身朝向探测，不在这里解析墙滑能力。
        float a = Vector2.SignedAngle(Vector2.up, transform.up);
        ContactFilter2D contactFilter = new ContactFilter2D { useTriggers = false };
        contactFilter.SetLayerMask(cPhysics.groundLayer);
        RaycastHit2D hit = FindGroundHit(groundV.position, cPhysics.boxCastH, a,
            -transform.up, contactFilter);
        // 顶头与脚下同属接触查询，沿用同一层掩码和 Trigger 过滤。
        RaycastHit2D topHit = PhysicsWorld.BoxCast(upV.position, cPhysics.boxCastH, a,
            transform.up, 0f, contactFilter);
        nowGemetry.istop = topHit.collider != null;
        //缓存真实命中法线；斜坡位移与着地判定必须使用表面信息
        nowGemetry.groundNormal = hit.normal;
        bool onGroundNow = false;
        //默认的空中阻力系数
        self_resistanceCoefficient = 1;
        // 几何着地：法线足够朝上才算踩到支撑，与表面有没有环境能力无关。
        if (hit.collider != null && Vector2.Dot(hit.normal, Vector2.up) > 0.7f)
        {
            onGroundNow = true;
            //在地面，则自身阻力增大
            self_resistanceCoefficient = 20;
        }

        // 几何只保留 Collider；脚下能力由相位 2 经 groundCollider 解析。
        nowGemetry.isGrounded = onGroundNow;
        nowGemetry.groundCollider = onGroundNow ? hit.collider : null;
        // 左右墙只记几何接触。箱体没有贴墙下滑，不把侧面来源送进持续效果登记。
        contactFilter.SetLayerMask(cPhysics.wallLayer);
        RaycastHit2D hit1 = PhysicsWorld.BoxCast(leftV.position, cPhysics.boxCastV, a,
            -transform.right, 0f, contactFilter);
        RaycastHit2D hit2 = PhysicsWorld.BoxCast(rightV.position, cPhysics.boxCastV, a,
            transform.right, 0f, contactFilter);
        nowGemetry.onLeftWall = false;
        nowGemetry.leftWallCollider = null;
        //夹角需小于25度，内积近似0.9
        if (hit1.collider != null && Vector2.Dot(hit1.normal, Vector2.right) > 0.9f)
        {
            nowGemetry.onLeftWall = true;
            nowGemetry.leftWallCollider = hit1.collider;
        }
        nowGemetry.onRightWall = false;
        nowGemetry.rightWallCollider = null;
        if (hit2.collider != null && Vector2.Dot(hit2.normal, Vector2.left) > 0.9f)
        {
            nowGemetry.onRightWall = true;
            nowGemetry.rightWallCollider = hit2.collider;
        }

    }

}
