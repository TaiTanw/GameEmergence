using UnityEngine;

/// <summary>
/// 唯一游戏镜头。第一台跨场景保留并跟随当前控制目标；
/// 之后加载出来的关卡镜头只提供背景色和正交尺寸，随即关闭，避免抢 Camera.main 和音频监听器。
/// </summary>
public class MainCamera : MonoBehaviour
{
    static MainCamera resident;
    Vector3 toPoint;
    float followSpeed = 5f;

    void Awake()
    {
        Camera incoming = GetComponent<Camera>();
        if (resident != null && resident != this)
        {
            AdoptLens(resident.GetComponent<Camera>(), incoming);
            DisableSceneCamera();
            return;
        }
        // 实际逻辑：从场景根节点摘出来，换关时不被一起卸掉。
        if (transform.parent != null) transform.SetParent(null, true);
        resident = this;
        DontDestroyOnLoad(gameObject);
        if (incoming != null) incoming.cullingMask |= 1 << Player.PlayerLayer;
        InputControlMgr.Instance.SetMainCamera(this);
    }

    void OnDestroy()
    {
        if (resident == this) resident = null;
    }

    static void AdoptLens(Camera kept, Camera scene)
    {
        if (kept == null || scene == null) return;
        kept.backgroundColor = scene.backgroundColor;
        kept.cullingMask |= scene.cullingMask;
        kept.cullingMask |= 1 << Player.PlayerLayer;
        if (kept.orthographic && scene.orthographic) kept.orthographicSize = scene.orthographicSize;
    }

    void DisableSceneCamera()
    {
        AudioListener listener = GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.enabled = false;
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
    }

    void Update()
    {
        transform.position = Vector3.Lerp(transform.position, toPoint, followSpeed * Time.deltaTime);
    }
}
