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
    [SerializeField, Tooltip("勾选后，只有教学关会因此进入下一关")]
    bool clearsLevel = true;

    static readonly List<SceneProp> interactProps = new List<SceneProp>();
    bool used;
    bool playerInside;

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
            if (!prop.playerInside || !IsControlled(player)) continue;
            if (prop.TryAcquire()) return;
        }
    }

    void OnTriggerEnter2D(Collider2D other) => Notice(other);
    void OnTriggerStay2D(Collider2D other) => Notice(other);

    void OnTriggerExit2D(Collider2D other)
    {
        // 安全：离开的不是当前控制角色时，不取消仍在范围内的接触。
        if (!IsControlled(PlayerOf(other))) return;
        playerInside = false;
    }

    void Notice(Collider2D other)
    {
        Player player = PlayerOf(other);
        // 安全：范围里不是当前控制角色时忽略。
        if (!IsControlled(player)) return;
        playerInside = true;
        // 实际逻辑：触碰获取在范围内结算；交互获取只记下人在里面。
        if (acquireMode == AcquireMode.触碰获取) TryAcquire();
    }

    bool TryAcquire()
    {
        if (used) return false;
        // 实际逻辑：探索模拟统一禁止领取；回漫游后仍使用同一未领取记录。
        if (LevelStageMgr.Instance.HasExploration && LevelStageMgr.Instance.IsSimulation) return false;
        if (clearsLevel)
        {
            SceneLevelSetup setup = SceneLevelSetup.Current;
            // 安全：没摆关卡配置，或当前不是教学关，不通关，也不当成加载错误。
            if (setup == null || !setup.Teaching) return false;
        }
        used = true;
        // 实际逻辑：效果记录还没有。勾选有效果只表示这件道具带效果，不写能力、不改进度。
        if (hasEffect) { }
        // 实际逻辑：允许通关时进入顺序中的下一关。同一实体留下，非教学关另建精神体。
        if (clearsLevel) SceneChangeMgr.Instance.EnterNextLevel();
        gameObject.SetActive(false);
        return true;
    }

    static Player PlayerOf(Collider2D other)
    {
        // 安全：碰撞体在子物体上时仍找到角色。
        return other == null ? null : other.GetComponentInParent<Player>();
    }

    static bool IsControlled(Player player)
    {
        return player != null && player == InputControlMgr.Instance.Player;
    }
}
