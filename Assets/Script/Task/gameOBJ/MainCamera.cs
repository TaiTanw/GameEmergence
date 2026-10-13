using UnityEngine;

/// <summary>
/// 唯一游戏镜头。第一台跨场景保留并跟随当前控制目标；
/// 之后加载出来的关卡镜头只提供背景色和正交尺寸，随即关闭，避免抢 Camera.main 和音频监听器。
/// </summary>
public class MainCamera : MonoBehaviour
{
    static MainCamera resident;
    Camera portrait;
    bool showingPortrait;
    Transform portraitTarget;
    bool portraitCentered;
    bool endingView;
    Transform endingTarget;
    Vector3 toPoint;
    float followSpeed = 5f;
    /// <summary>实际镜头进入目标附近才允许交还操作；不以固定延时冒充交接完成。</summary>
    public bool HasReachedPoint => (transform.position - toPoint).sqrMagnitude <= .0004f;

    void Awake()
    {
        Camera incoming = GetComponent<Camera>();
        if (resident != null && resident != this)
        {
            AdoptLens(resident.GetComponent<Camera>(), incoming);
            DisableSceneCamera();
            resident.SetGameViewEnabled(true);
            return;
        }
        // 实际逻辑：从场景根节点摘出来，换关时不被一起卸掉。
        if (transform.parent != null) transform.SetParent(null, true);
        resident = this;
        DontDestroyOnLoad(gameObject);
        if (incoming != null) incoming.cullingMask |= 1 << Player.PlayerLayer;
        EnsurePortrait();
        InputControlMgr.Instance.SetMainCamera(this);
    }

    void OnDestroy()
    {
        if (resident != this) return;
        resident = null;
        if (!MonoPublicMgr.IsQuitting) InputControlMgr.Instance.ReleaseMainCamera(this);
    }

    static void AdoptLens(Camera kept, Camera scene)
    {
        if (kept == null || scene == null) return;
        kept.backgroundColor = scene.backgroundColor;
        int withoutPlayer = scene.cullingMask & ~(1 << Player.PlayerLayer);
        kept.cullingMask |= withoutPlayer;
        if (!resident.showingPortrait) kept.cullingMask |= 1 << Player.PlayerLayer;
        if (kept.orthographic && scene.orthographic) kept.orthographicSize = scene.orthographicSize;
        // 实际逻辑：变亮时仍使用实体镜头，新关镜头尺寸要同时交给它。
        if (resident.portrait != null) resident.portrait.orthographicSize = kept.orthographicSize;
    }

    void DisableSceneCamera()
    {
        AudioListener listener = GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.enabled = false;
    }

    /// <summary>菜单期间只保留游戏镜头实例，重新进关时恢复渲染与音频。</summary>
    public void SetGameViewEnabled(bool on)
    {
        Camera world = GetComponent<Camera>();
        AudioListener listener = GetComponent<AudioListener>();
        // 安全：镜头与监听器分别判断，缺少其中一个时仍处理另一个。
        if (world != null) world.enabled = on;
        if (listener != null) listener.enabled = on;
    }

    /// <summary>变暗时世界镜头不画玩家，由更靠前的镜头把实体画在遮罩之上。</summary>
    public void SetTransitionPortrait(bool on, Transform target = null)
    {
        Camera world = GetComponent<Camera>();
        if (world == null) return;
        EnsurePortrait();
        if (on)
        {
            // 第一次从旧镜头缓动到实体；同次换关的重新安置沿用已居中的状态。
            if (!showingPortrait) portraitCentered = false;
            portraitTarget = target;
            world.cullingMask &= ~(1 << Player.PlayerLayer);
            portrait.orthographicSize = world.orthographicSize;
            portrait.enabled = true;
            showingPortrait = true;
        }
        else if (!on && showingPortrait)
        {
            // 实际逻辑：只恢复玩家层，保留加载期间新关补入的背景层。
            world.cullingMask |= 1 << Player.PlayerLayer;
            portrait.enabled = false;
            showingPortrait = false;
            portraitTarget = null;
            portraitCentered = false;
        }
    }

    void EnsurePortrait()
    {
        if (portrait != null) return;
        Camera world = GetComponent<Camera>();
        var item = new GameObject("PlayerPortraitCamera");
        item.transform.SetParent(transform, false);
        portrait = item.AddComponent<Camera>();
        portrait.orthographic = true;
        portrait.orthographicSize = world != null ? world.orthographicSize : 5f;
        portrait.clearFlags = CameraClearFlags.Depth;
        portrait.cullingMask = 1 << Player.PlayerLayer;
        portrait.depth = 1f;
        portrait.nearClipPlane = world != null ? world.nearClipPlane : 0.3f;
        portrait.farClipPlane = world != null ? world.farClipPlane : 1000f;
        portrait.enabled = false;
    }

    public void SetPoint(Vector3 vector3)
    {
        toPoint = new Vector3(vector3.x, vector3.y, -10);
    }

    /// <summary>揭开加载页之前对准目标，避免从旧关位置缓动穿帮。</summary>
    public void SnapTo(Vector3 world)
    {
        SetPoint(world);
        transform.position = toPoint;
        if (showingPortrait) portraitCentered = true;
    }

    internal void SetEndingTarget(Transform target)
    {
        SetTransitionPortrait(false);
        SetGameViewEnabled(true);
        endingView = true; endingTarget = target;
        if (target != null) SnapTo(target.position);
    }
    internal void SetEndingPosition(Vector3 point)
    { SetEndingTarget(null); SnapTo(point); }
    internal void EndEndingView() { endingView = false; endingTarget = null; }

    void Update()
    {
        // 正常玩法保持原缓动；过场等本帧角色显示位置确定后再跟随。
        if (!showingPortrait && !endingView) MoveTowardPoint(Time.deltaTime);
    }

    void LateUpdate()
    {
        // 结局脚本只指定目标或固定点；镜头与普通输入跟随在这里明确交接。
        if (endingView)
        { if (endingTarget != null) SnapTo(endingTarget.position); return; }
        if (!showingPortrait || portraitTarget == null) return;
        SetPoint(portraitTarget.position);
        // 居中后直接跟随本帧位置，出生点迁移及物理下落不让前景角色偏离中心。
        if (portraitCentered) SnapTo(portraitTarget.position);
        else
        {
            MoveTowardPoint(Time.unscaledDeltaTime);
            if (HasReachedPoint) SnapTo(portraitTarget.position);
        }
    }

    void MoveTowardPoint(float delta)
    {
        Vector3 next = Vector3.Lerp(transform.position, toPoint, followSpeed * delta);
        // 大坐标／高帧率下缓动增量可能被舍入；有时间推进但位置不再变化时，收敛至既定目标。
        if (delta > 0f && next.Equals(transform.position)) next = toPoint;
        transform.position = next;
    }
}
