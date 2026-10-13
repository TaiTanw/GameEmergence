using System.Collections.Generic;
using UnityEngine;

/// <summary>关卡里单独摆放的道具。不挂在角色或关卡配置上；场景里没有它也不影响加载。</summary>
public class SceneProp : MonoBehaviour
{
    enum AcquireMode
    {
        交互获取,
        触碰获取
    }

    [SerializeField, Tooltip("交互获取要靠近后点按；触碰获取进入范围即获取")]
    AcquireMode acquireMode = AcquireMode.交互获取;
    [SerializeField, Tooltip("勾选表示这件道具带有效果。效果记录尚未存在，勾了也不会附加能力")]
    bool hasEffect;
    [SerializeField, Tooltip("勾选后，教学或逃生阶段领取会进入下一关")]
    bool clearsLevel = true;
    [SerializeField, Tooltip("领取成功后累计的结局贡献 X、Y。失败重刷新丢弃本关贡献；Z 留待以后")]
    Vector2 endingContribution;
    public Vector2 EndingContribution => endingContribution;
    [SerializeField, Min(0), Tooltip("通关动作编号：对应实体控制器的 LevelClear_编号 与 LevelClearLoop_编号；精神体不能触发通关")]
    int levelClearAnimation;
    [SerializeField, Tooltip("可选的专用光效组件：探索阶段 enabled=true，其它阶段或已使用时为 false；组件自行在启停时播放／停止光效。未配置不影响通关")]
    Behaviour explorationLightEffect;

    static readonly List<SceneProp> interactProps = new List<SceneProp>();
    bool used;
    bool defaultActive;
    Vector3 defaultPosition;
    bool defaultsCaptured;

    void Awake() => CaptureDefaults();

    void CaptureDefaults()
    {
        if (defaultsCaptured) return;
        defaultActive = gameObject.activeSelf;
        defaultPosition = transform.position;
        defaultsCaptured = true;
    }

    /// <summary>失败重刷新由关卡编排调用，轻刷新保持领取记录。</summary>
    public void RestoreDefault()
    {
        // 初始禁用物尚未 Awake 时仍按制作现场记录，不能激活本应关闭的道具。
        CaptureDefaults();
        used = false;
        transform.position = defaultPosition;
        gameObject.SetActive(defaultActive);
        SyncExplorationLight();
    }

    void OnEnable()
    {
        // 实际逻辑：只有交互获取才接收点按。
        if (acquireMode == AcquireMode.交互获取) interactProps.Add(this);
        // 光效是可选表现；不能把本玩法组件配置为自己的启停目标。
        if (explorationLightEffect == this) Debug.LogError("通关道具的探索光效应关联专用光效组件，不能关联 SceneProp 自身。", this);
        SyncExplorationLight();
    }

    void OnDisable()
    {
        interactProps.Remove(this);
        if (explorationLightEffect != null && explorationLightEffect != this) explorationLightEffect.enabled = false;
    }

    void LateUpdate() => SyncExplorationLight();

    void SyncExplorationLight()
    {
        // 安全：接口未配置时不创建光效，也不影响道具流程。
        if (explorationLightEffect == null || explorationLightEffect == this) return;
        // 阶段事实归现有规则；组件只控制表现，漫游和模拟都属于探索。
        explorationLightEffect.enabled = isActiveAndEnabled && clearsLevel && !used && LevelStageMgr.Instance.HasExploration;
    }

    /// <summary>与提示共用同一近距目标；无效通关配置不把点按转给另一件道具。</summary>
    public static void OfferInteract(Player player)
    {
        FindInteractTarget(player)?.TryAcquire(player);
    }

    public static SceneProp FindInteractTarget(Player player)
    {
        // 安全：没有道具或当前控制角色时不查询范围，也不初始化输入管理器。
        if (interactProps.Count == 0 || !IsControlled(player)) return null;
        SceneProp nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < interactProps.Count; i++)
        {
            SceneProp prop = interactProps[i];
            if (prop == null || !prop.CanAcquire(player)) continue;
            float distance = ((Vector2)prop.transform.position - (Vector2)player.transform.position).sqrMagnitude;
            // 范围重叠时选最近一件；同距保持注册顺序，提示和点按不会各选一件。
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = prop;
        }
        return nearest;
    }

    /// <summary>提示优先包住道具精灵；无精灵的制作占位物使用自身触发范围。</summary>
    public Bounds DisplayBounds
    {
        get
        {
            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) return sprite.bounds;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool found = false;
            foreach (Collider2D range in GetComponentsInChildren<Collider2D>())
            {
                if (!range.isActiveAndEnabled || !range.isTrigger || range.GetComponentInParent<SceneProp>() != this) continue;
                if (found) bounds.Encapsulate(range.bounds);
                else { bounds = range.bounds; found = true; }
            }
            return bounds;
        }
    }

    void OnTriggerEnter2D(Collider2D other) => Notice(other);
    void OnTriggerStay2D(Collider2D other) => Notice(other);

    void Notice(Collider2D other)
    {
        // 实际逻辑：触碰获取当次核对控制与范围；交互获取只在点按时核对。
        if (acquireMode == AcquireMode.触碰获取) TryAcquire(PlayerOf(other));
    }

    bool CanAcquire(Player player)
    {
        // 安全：领取只接受当前活动的受控角色与尚未领取的道具。
        if (used || !isActiveAndEnabled || !IsControlled(player)) return false;
        // 终结动作不再接收其它道具；死亡和通关都保持各自本次结果。
        if (player.PendingDeath.HasValue || player.IsLevelClearing) return false;
        // 实际逻辑：控制交接或刷新搬移后，按当前几何核对，不能沿用旧角色的进入记录。
        if (!ContainsPlayer(player)) return false;
        // 实际逻辑：探索模拟统一禁止领取；回漫游后仍使用同一未领取记录。
        if (LevelStageMgr.Instance.HasExploration && LevelStageMgr.Instance.IsSimulation) return false;
        if (clearsLevel)
        {
            // 身份约束独立于阶段，精神体不能借教学或逃生配置触发通关。
            if (player.Identity != PlayerIdentity.Body) return false;
            SceneLevelSetup setup = SceneLevelSetup.Current;
            // 通关资格归阶段：探索不允许借通关物跳过逃生结算。
            if (setup == null || (!setup.Teaching && LevelStageMgr.Instance.Stage != PlayerGameplayStage.Escape)) return false;
        }
        return true;
    }

    bool TryAcquire(Player player)
    {
        if (!CanAcquire(player)) return false;
        // 制作配置与下一关都通过校验后才锁定，失败时道具仍可交互。
        if (clearsLevel && !SceneChangeMgr.Instance.TryClearLevel(player, levelClearAnimation, this)) return false;
        // 普通道具领取即登记；通关物由通关入口登记，不能重复添加。
        if (!clearsLevel && !DataAndInitMgr.Instance.TryAddEndingContribution(endingContribution, out string error))
        { Debug.LogError(error, this); return false; }
        used = true;
        // 实际逻辑：效果记录还没有。勾选有效果只表示这件道具带效果，不写能力、不改进度。
        if (hasEffect) { }
        // 通关物留在现场，领取锁与玩家通关锁共同阻止重复交互；普通道具仍沿原占位流程。
        if (!clearsLevel) gameObject.SetActive(false);
        SyncExplorationLight();
        return true;
    }

    /// <summary>前段或加载循环未能交接时，只撤销本次领取，保留道具当前的位置。</summary>
    internal void CancelLevelClearTake()
    {
        // 安全：只归还本次已领取的通关物，不重置普通道具或关卡默认。
        if (!clearsLevel || !used) return;
        used = false;
        gameObject.SetActive(true);
        SyncExplorationLight();
    }

    bool ContainsPlayer(Player player)
    {
        // 实际逻辑：刷新可能在同一帧搬移角色或道具，先同步后再读碰撞框距离。
        Physics2D.SyncTransforms();
        Collider2D[] ranges = GetComponentsInChildren<Collider2D>();
        Collider2D[] bodies = player.GetComponentsInChildren<Collider2D>();
        foreach (Collider2D range in ranges)
        {
            // 安全：只使用这件道具自己仍启用的领取触发范围。
            if (!range.isActiveAndEnabled || !range.isTrigger || range.GetComponentInParent<SceneProp>() != this) continue;
            foreach (Collider2D body in bodies)
            {
                // 安全：子碰撞框仍须归同一角色，停用的碰撞框不能代替实际范围。
                if (!body.isActiveAndEnabled || PlayerOf(body) != player) continue;
                ColliderDistance2D distance = range.Distance(body);
                // 实际逻辑：任一有效碰撞框真正相交即可领取，多框退出不会抹掉仍在范围内的另一框。
                if (distance.isValid && distance.isOverlapped) return true;
            }
        }
        return false;
    }

    static Player PlayerOf(Collider2D other)
    {
        // 安全：碰撞体在子物体上时仍找到角色。
        return other == null ? null : other.GetComponentInParent<Player>();
    }

    static bool IsControlled(Player player)
    {
        return player != null && player.isActiveAndEnabled && player == InputControlMgr.Instance.Player;
    }
}
