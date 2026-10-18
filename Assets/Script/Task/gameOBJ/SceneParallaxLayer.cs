using UnityEngine;

/// <summary>场景装饰层只读最终主镜头，按制作基准计算偏移，不参与关卡或物理。</summary>
[DefaultExecutionOrder(200), DisallowMultipleComponent]
public sealed class SceneParallaxLayer : MonoBehaviour
{
    [SerializeField, Tooltip("本层制作时对应的世界镜头位置；与其它层使用同一基准。")]
    Vector2 referenceCameraPosition;
    [SerializeField, Tooltip("跟随镜头的比例：0 为固定在世界，1 为固定在画面。远景通常大于中景。")]
    Vector2 followFactor = new Vector2(.65f, .5f);
    [SerializeField, Tooltip("远景可指定自身的完整底图；镜头飞出制作区域时，让底图边缘继续覆盖画面。透明中景留空。")]
    SpriteRenderer coveringSprite;

    Vector3 authoredPosition;
    bool captured;
    bool reportedError;

    public bool ValidateConfiguration(out string error)
    {
        // 安全：非法制作参数显式拒绝，不靠截断掩盖配置问题。
        if (!Finite(referenceCameraPosition.x) || !Finite(referenceCameraPosition.y)
            || !Finite(followFactor.x) || !Finite(followFactor.y)
            || followFactor.x < 0f || followFactor.x > 1f || followFactor.y < 0f || followFactor.y > 1f)
        { error = "基准镜头位置必须有限；视差跟随比例必须在 0–1 之间。"; return false; }
        if (coveringSprite != null && (coveringSprite.transform != transform || coveringSprite.sprite == null))
        { error = "边缘覆盖精灵必须是本物体上具有有效贴图的 SpriteRenderer。"; return false; }
        error = null;
        return true;
    }

    void OnEnable() => CaptureOrigin();

    void CaptureOrigin()
    {
        // 实际逻辑：只记制作位置，不把上一步已偏移的位置再次当起点。
        if (captured) return;
        authoredPosition = transform.position;
        captured = true;
    }

    void LateUpdate() => ApplyForCamera(Camera.main);

    /// <summary>正式帧和编辑器定向预览共用；只接受启用的正交镜头。</summary>
    public void ApplyForCamera(Camera view)
    {
        // 安全：菜单停画或镜头尚未就绪时保留现场，不搜索其它渲染镜头替代。
        if (!isActiveAndEnabled || view == null || !view.isActiveAndEnabled || !view.orthographic) return;
        if (!ValidateConfiguration(out string error))
        {
            RestoreAuthoredPosition();
            if (!reportedError) Debug.LogError(name + "：" + error, this);
            reportedError = true;
            return;
        }
        reportedError = false;
        CaptureOrigin();
        Vector2 delta = (Vector2)view.transform.position - referenceCameraPosition;
        // 实际逻辑：每帧从固定基准计算；往返、瞬移与暂停都不累计误差，Z 与镜头无关。
        Vector3 desired = authoredPosition + new Vector3(delta.x * followFactor.x, delta.y * followFactor.y, 0f);
        if (coveringSprite != null)
        {
            Vector3 half = coveringSprite.bounds.extents;
            float marginX = half.x - view.orthographicSize * view.aspect, marginY = half.y - view.orthographicSize;
            // 实际逻辑：只有到达底图边缘才收住偏移；正常取景保持原视差，远距离漫游不露底。
            if (marginX >= 0f) desired.x = Mathf.Clamp(desired.x, view.transform.position.x - marginX, view.transform.position.x + marginX);
            if (marginY >= 0f) desired.y = Mathf.Clamp(desired.y, view.transform.position.y - marginY, view.transform.position.y + marginY);
        }
        transform.position = desired;
    }

    public void RestoreAuthoredPosition()
    {
        if (captured) transform.position = authoredPosition;
    }

    void OnDisable()
    {
        RestoreAuthoredPosition();
        captured = false;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
