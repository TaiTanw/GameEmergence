using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 职能：水域的唯一环境宿主，提供水事实并在原相位 0 提交已配置的矩形水位。
/// Trigger 表示水的范围；关掉游泳许可仍然是水，不取消淹没和以后的耗氧依据。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class WaterRegion : BasicPhysicalObject, IWaterRegion
{
    [SerializeField] private bool swimmingEnabled = true;
    public bool SwimmingEnabled => TryGetWaterSwimming(out bool enabled) && enabled;
    protected override void AddLegacyEnvironmentParameters(List<EnvironmentParameter> parameters) =>
        AddLegacyEnvironmentParameter(parameters, new WaterSwimmingParameter(swimmingEnabled));
    private bool hasWaterLevel;
    private bool risingAllowed;
    private float bottomY;
    private float surfaceY;
    private float riseSpeed;
    private bool defaultsCaptured;
    private float defaultBottom, defaultSurface;
    private bool hasSurfaceLimit;
    private float maximumSurface;
    public float SurfaceY => GetComponent<BoxCollider2D>().bounds.max.y;
    public bool RisingAllowed => hasWaterLevel && risingAllowed;

    /// <summary>关卡就绪时从制作矩形保存上涨水的默认范围；普通池不主动调用此入口。</summary>
    public bool PrepareRun(float speed, float maximumSurfaceY)
    {
        if (!CanPrepareRun(speed, maximumSurfaceY)) return false;
        if (!defaultsCaptured)
        {
            Bounds bounds = GetComponent<BoxCollider2D>().bounds;
            if (bounds.size.y <= 0f) return false;
            defaultBottom = bounds.min.y; defaultSurface = bounds.max.y;
            defaultsCaptured = true;
        }
        // 实际逻辑：关卡准备一次提交固定上限，不随场景标记或上涨水的 Transform 移动。
        maximumSurface = maximumSurfaceY;
        hasSurfaceLimit = true;
        if (!ConfigureWaterLevel(defaultBottom, defaultSurface, speed, false)) return false;
        CommitWaterGeometry();
        return true;
    }

    public bool CanPrepareRun(float speed, float maximumSurfaceY) =>
        ValidateRunConfiguration(speed, maximumSurfaceY, out _);

    public bool ValidateRunConfiguration(float speed, float maximumSurfaceY, out string error)
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        error = null;
        // 安全：所有制作配置先读后验，拒绝时不捕获默认或改水位。
        if (!IsEnvironmentActive || box == null || !box.enabled || !box.isTrigger)
            error = $"{name}：上涨主水必须启用并使用 BoxCollider2D 触发器。";
        else if (!IsFinite(speed) || speed < 0f)
            error = $"{name}：上涨速度必须是有限非负数值。";
        else if (!IsFinite(maximumSurfaceY))
            error = $"{name}：上涨水最高点的世界 Y 必须是有限数值。";
        else if (!IsFinite(box.bounds.min.y) || !IsFinite(box.bounds.max.y) || box.bounds.size.y <= 0f
            || Mathf.Abs(transform.lossyScale.y) < .0001f || Mathf.Abs(transform.up.y) < .9999f)
            error = $"{name}：上涨主水必须是未旋转、非零高度与缩放的水平矩形。";
        else if (maximumSurfaceY < (defaultsCaptured ? defaultSurface : box.bounds.max.y))
            error = $"{name}：上涨水最高点不能低于初始水面。";
        return error == null;
    }

    public void SetRisingAllowed(bool allowed)
    {
        // 未配置的普通池不取得上涨许可。
        if (hasWaterLevel) risingAllowed = allowed;
    }

    public void RestoreRunDefaults()
    {
        if (!defaultsCaptured) return;
        bottomY = defaultBottom; surfaceY = defaultSurface; risingAllowed = false;
        CommitWaterGeometry();
    }

    protected override void OnSourceEnabled() => MonoPublicMgr.Instance.AddPhysicalTimingUpdate(AdvanceWaterLevel, 0);
    protected override void OnSourceDisabled()
    {
        if (!MonoPublicMgr.IsQuitting) MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(AdvanceWaterLevel, 0);
    }

    public bool ConfigureWaterLevel(float bottom, float surface, float speed, bool allowed)
    {
        // 配置边界：首版是水平矩形水域；非有限数、空范围、零缩放或旋转矩形拒绝提交。
        if (!IsFinite(bottom) || !IsFinite(surface) || !IsFinite(speed) || surface <= bottom || speed < 0f
            || Mathf.Abs(transform.lossyScale.y) < .0001f || Mathf.Abs(transform.up.y) < .9999f
            || (hasSurfaceLimit && surface > maximumSurface)) return false;
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
        if (risingAllowed)
        {
            float nextSurface = surfaceY + riseSpeed * Time.fixedDeltaTime;
            // 实际逻辑：一次物理步越过上限时只到最高水面，仍保留水域与游泳／环境效果。
            surfaceY = hasSurfaceLimit ? Mathf.Min(nextSurface, maximumSurface) : nextSurface;
        }
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

    public void SetSwimmingEnabled(bool enabled) => SetWaterSwimmingState(enabled);

    public bool TryGetSurfaceY(Collider2D region, float worldX, out float surface)
    {
        surface = 0f;
        // 安全与首版范围：只发布同宿主、有效 Trigger 的水平矩形水面。
        if (!IsEnvironmentActive || !TryGetWaterSwimming(out _) || !(region is BoxCollider2D) || !region.enabled
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
        if (!IsEnvironmentActive || !TryGetWaterSwimming(out _) || region == null || !region.enabled
            || region.gameObject != gameObject || !region.isTrigger) return false;
        // 实际逻辑：按实际 Collider 形状判断，不以名字或包围框交叠代替淹没。
        return region.OverlapPoint(point);
    }
}
