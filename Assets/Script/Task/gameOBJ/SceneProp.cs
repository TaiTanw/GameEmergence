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
    }

    void OnEnable()
    {
        // 实际逻辑：只有交互获取才接收点按。
        if (acquireMode == AcquireMode.交互获取) interactProps.Add(this);
    }

    void OnDisable()
    {
        interactProps.Remove(this);
    }

    /// <summary>箱子没有收下这次点按时，由范围内的交互道具接收。没有道具则不做任何事。</summary>
    public static void OfferInteract(Player player)
    {
        // 安全：没有控制角色时不获取。
        if (player == null) return;
        for (int i = 0; i < interactProps.Count; i++)
        {
            SceneProp prop = interactProps[i];
            if (prop.TryAcquire(player)) return;
        }
    }

    void OnTriggerEnter2D(Collider2D other) => Notice(other);
    void OnTriggerStay2D(Collider2D other) => Notice(other);

    void Notice(Collider2D other)
    {
        // 实际逻辑：触碰获取当次核对控制与范围；交互获取只在点按时核对。
        if (acquireMode == AcquireMode.触碰获取) TryAcquire(PlayerOf(other));
    }

    bool TryAcquire(Player player)
    {
        // 安全：领取只接受当前活动的受控角色与尚未领取的道具。
        if (used || !isActiveAndEnabled || !IsControlled(player)) return false;
        // 实际逻辑：控制交接或刷新搬移后，按当前几何核对，不能沿用旧角色的进入记录。
        if (!ContainsPlayer(player)) return false;
        // 实际逻辑：探索模拟统一禁止领取；回漫游后仍使用同一未领取记录。
        if (LevelStageMgr.Instance.HasExploration && LevelStageMgr.Instance.IsSimulation) return false;
        if (clearsLevel)
        {
            SceneLevelSetup setup = SceneLevelSetup.Current;
            // 通关资格归阶段：探索不允许借通关物跳过逃生结算。
            if (setup == null || (!setup.Teaching && LevelStageMgr.Instance.Stage != PlayerGameplayStage.Escape)) return false;
        }
        used = true;
        // 实际逻辑：效果记录还没有。勾选有效果只表示这件道具带效果，不写能力、不改进度。
        if (hasEffect) { }
        // 实际逻辑：允许通关时进入顺序中的下一关。同一实体留下，非教学关另建精神体。
        if (clearsLevel) SceneChangeMgr.Instance.EnterNextLevel();
        gameObject.SetActive(false);
        return true;
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
