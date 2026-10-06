using UnityEngine;

/// <summary>
/// 职能：水域的唯一环境宿主，提供水事实并在原相位 0 提交已配置的矩形水位。
/// Trigger 表示水的范围；关掉游泳许可仍然是水，不取消淹没和以后的耗氧依据。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class WaterRegion : BasicPhysicalObject, IWaterRegion
{
    [SerializeField] private bool swimmingEnabled = true;
    public bool SwimmingEnabled => swimmingEnabled;
    private bool hasWaterLevel;
    private bool risingAllowed;
    private float bottomY;
    private float surfaceY;
    private float riseSpeed;
    public float SurfaceY => GetComponent<BoxCollider2D>().bounds.max.y;
    public bool RisingAllowed => hasWaterLevel && risingAllowed;

    protected override void OnSourceEnabled() => MonoPublicMgr.Instance.AddPhysicalTimingUpdate(AdvanceWaterLevel, 0);
    protected override void OnSourceDisabled()
    {
        if (!MonoPublicMgr.IsQuitting) MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(AdvanceWaterLevel, 0);
    }

    public bool ConfigureWaterLevel(float bottom, float surface, float speed, bool allowed)
    {
        // 配置边界：首版是水平矩形水域；非有限数、空范围、零缩放或旋转矩形拒绝提交。
        if (!IsFinite(bottom) || !IsFinite(surface) || !IsFinite(speed) || surface <= bottom || speed < 0f
            || Mathf.Abs(transform.lossyScale.y) < .0001f || Mathf.Abs(transform.up.y) < .9999f) return false;
        bottomY = bottom;
        surfaceY = surface;
        riseSpeed = speed;
        risingAllowed = allowed;
        hasWaterLevel = true;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void AdvanceWaterLevel()
    {
        // 普通静态池未配置水位时保持预设几何；上涨许可由外部规则给出。
        if (!hasWaterLevel) return;
        if (risingAllowed) surfaceY += riseSpeed * Time.fixedDeltaTime;
        CommitWaterGeometry();
    }

    private void CommitWaterGeometry()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        float localHeight = (surfaceY - bottomY) / Mathf.Abs(transform.lossyScale.y);
        Vector3 position = transform.position;
        position.y = (surfaceY + bottomY) * .5f;
        transform.position = position;
        box.offset = new Vector2(box.offset.x, 0f);
        box.size = new Vector2(box.size.x, localHeight);
        SpriteRenderer visual = GetComponent<SpriteRenderer>();
        // 表现只读同一次提交后的范围；不以精灵高度反推水判定。
        if (visual != null)
        {
            visual.drawMode = SpriteDrawMode.Sliced;
            visual.size = box.size;
        }
        // 项目关闭了自动同步；相位 0 必须提交几何，使本步相位 1 查询读取同一水位。
        Physics2D.SyncTransforms();
    }

    public void SetSwimmingEnabled(bool enabled) => swimmingEnabled = enabled;

    public bool TryGetSurfaceY(Collider2D region, float worldX, out float surface)
    {
        surface = 0f;
        // 安全与首版范围：只发布同宿主、有效 Trigger 的水平矩形水面。
        if (!IsEnvironmentActive || !(region is BoxCollider2D) || !region.enabled
            || region.gameObject != gameObject || !region.isTrigger
            || Mathf.Abs(region.transform.up.y) < .9999f) return false;
        Bounds bounds = region.bounds;
        // 几何事实：侧边外不能借包围框高度进入水面动作。
        if (worldX < bounds.min.x || worldX > bounds.max.x) return false;
        surface = bounds.max.y;
        return ContainsWaterPoint(region, new Vector2(worldX, surface - Mathf.Min(.001f, bounds.size.y * .5f)));
    }

    public bool ContainsWaterPoint(Collider2D region, Vector2 point)
    {
        // 安全：失效、不同宿主或实心几何不能作为本水源的区域。
        if (!IsEnvironmentActive || region == null || !region.enabled
            || region.gameObject != gameObject || !region.isTrigger) return false;
        // 实际逻辑：按实际 Collider 形状判断，不以名字或包围框交叠代替淹没。
        return region.OverlapPoint(point);
    }
}
