using UnityEngine;

public partial class PresentationLayer
{
    CharacterPhysics waterMotionSource;
    Bounds previousWaterBody;
    uint waterMotionVersion;
    bool hasWaterMotionSample;
    float lastWaterStrokeTime;

    void UpdateWaterRipples()
    {
        // 安全：未组装的动画夹具／专属演出没有普通角色物理，不建立水接触历史。
        if (!isInitialized || !Application.isPlaying) return;
        if (waterMotionSource == null)
        {
            Player owner = GetComponentInParent<Player>();
            if (owner != null) waterMotionSource = owner.GetComponentInChildren<CharacterPhysics>();
        }
        if (waterMotionSource == null) return;
        // 死亡待机仍有被动下落，保留入水接触；普通待命实体继续忽略。
        bool permitted = !transitionPlayback && !transitionHidden && !levelClearPlaybackActive
            && !actionData.isFlying && (!actionData.HoldIdle || waterMotionSource.IsPassiveMotion)
            && waterMotionSource.IsMotionParticipating;
        // 区域命中与位置必须来自同一次采样，不能把相位 1 的列表与相位 5 的新位置拼接。
        SampleWaterRipples(geometryPhysicsData.RegionBodyBounds, waterMotionSource.MotionRefreshVersion,
            geometryPhysicsData.RegionBodyVelocity, permitted, Time.time);
    }

    void SampleWaterRipples(Bounds body, uint version, Vector2 velocity, bool permitted, float now)
    {
        // 暂停、飞行与显式传送只建立新位置基线，不能变成一次猛烈入水。
        bool fresh = !hasWaterMotionSample || version != waterMotionVersion || !permitted || BasicEntity.IsWorldPaused;
        if (!fresh)
        {
            var regions = geometryPhysicsData.RegionOverlaps;
            bool stroke = !deathPlaybackActive && actionData.isSwimming && (Mathf.Abs(actionData.onMove) > MoveInputDeadZone
                || Mathf.Abs(actionData.verticalInput) > MoveInputDeadZone);
            bool emittedStroke = false;
            for (int i = 0; i < regions.Count; i++)
            {
                Collider2D region = regions[i];
                // 原查询保证同物体环境宿主；没有水表现的区域直接略过。
                if (region == null || !region.TryGetComponent(out WaterSurfaceVisual water)) continue;
                if (water.SampleContact(previousWaterBody, body, velocity, stroke, now - lastWaterStrokeTime)) emittedStroke |= stroke;
            }
            if (emittedStroke) lastWaterStrokeTime = now;
        }
        previousWaterBody = body;
        waterMotionVersion = version;
        hasWaterMotionSample = permitted && !BasicEntity.IsWorldPaused;
        if (fresh) lastWaterStrokeTime = now;
    }
}
