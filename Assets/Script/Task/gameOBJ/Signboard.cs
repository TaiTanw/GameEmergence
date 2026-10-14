using UnityEngine;

/// <summary>告示内容和外观归子面板预设；这里只判断当前控制角色是否在阅读范围内。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D))]
public sealed class Signboard : MonoBehaviour
{
    [SerializeField] BoxCollider2D proximityRange;
    [SerializeField] GameObject noticePanel;

    void Awake()
    {
        // 安全：引用必须属于本实例，触发器在根节点，面板必须是子物体。
        if (proximityRange == null || proximityRange.gameObject != gameObject || !proximityRange.isTrigger
            || noticePanel == null || noticePanel == gameObject || !noticePanel.transform.IsChildOf(transform))
        {
            Debug.LogError(name + "：告示牌需要关联本体上的触发器和本预设的子面板。", this);
            enabled = false;
            return;
        }
        noticePanel.SetActive(false);
    }

    void LateUpdate()
    {
        if (noticePanel == null) return;
        InputControlMgr input = InputControlMgr.Instance;
        Player player = input.Player;
        SceneChangeMgr flow = SceneChangeMgr.Instance;
        // 不缓存进入记录：暂停探索、角色切换和传送后，按当前根节点位置直接核对。
        bool show = proximityRange != null && proximityRange.isActiveAndEnabled
            && player != null && player.isActiveAndEnabled && input.InputEnabled
            && !flow.IsLoading && !flow.GameMenuOpen
            && proximityRange.OverlapPoint(player.transform.position);
        if (noticePanel.activeSelf != show) noticePanel.SetActive(show);
    }

    void OnDisable()
    {
        // 停用告示牌时清掉面板；非法的外部引用不由本组件修改。
        if (noticePanel != null && noticePanel != gameObject && noticePanel.transform.IsChildOf(transform)) noticePanel.SetActive(false);
    }
}
