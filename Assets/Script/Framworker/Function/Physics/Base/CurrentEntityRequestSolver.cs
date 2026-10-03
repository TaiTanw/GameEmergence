using PhyData;
using System.Collections.Generic;
using UnityEngine;

/// <summary>现行坡面、墙面、顶面请求策略；保持原有阈值与计算顺序。</summary>
internal sealed class CurrentEntityRequestSolver : IEntityRequestSolveStrategy
{
    internal static readonly CurrentEntityRequestSolver Instance = new CurrentEntityRequestSolver();

    private CurrentEntityRequestSolver() { }

    public void ResolveCandidate(in EntityEnvironmentSolveInput input, EntityCandidateGeometry geometry,
        IPhysicsWorldAccess2D world, ref EntitySolutionResult result)
    {
        float dx = geometry.candidateDelta.x;
        if (Mathf.Abs(dx) <= 0.0001f) return;
        float minUp = Mathf.Cos(Mathf.Clamp(input.maxClimbAngle, 0f, 89f) * Mathf.Deg2Rad);
        float currentBottom = input.bounds.min.y;
        result.environmentOffset.y = FindSlopeRise(input, geometry, world,
            geometry.candidateCenter, dx, geometry.downwardHits, minUp, out Collider2D climbSurface);

        float pathLength = geometry.candidateDelta.magnitude;
        foreach (RaycastHit2D hit in geometry.pathHits)
        {
            if (hit.collider == null || hit.collider == climbSurface ||
                IsOneWayPassThrough(input, geometry, hit, currentBottom)) continue;
            // 起点已接触且终点仍重叠时，只在法线明确反对本帧 X 时阻止进入。
            if (hit.distance <= 0f && geometry.endpointOverlaps.Contains(hit.collider) &&
                hit.normal.y < minUp)
            {
                if (hit.normal.x * dx < -0.0001f)
                    result.allowedHorizontalDelta = 0f;
                continue;
            }
            if (hit.normal.x * dx >= -0.0001f) continue;
            float allowed = Mathf.Max(0f, hit.distance - 0.001f) * Mathf.Abs(dx) / pathLength;
            result.allowedHorizontalDelta = Mathf.Sign(dx) *
                Mathf.Min(Mathf.Abs(result.allowedHorizontalDelta), allowed);
        }

        // X 缩短后在允许终点重查坡面 Y。
        float allowedX = result.allowedHorizontalDelta;
        if (Mathf.Abs(allowedX - dx) <= 0.0001f) return;
        result.environmentOffset.y = 0f;
        if (Mathf.Abs(allowedX) <= 0.0001f) return;
        Vector2 allowedCenter = geometry.candidateCenter + Vector2.right * (allowedX - dx);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(input.groundLayer);
        world.BoxCast(allowedCenter + Vector2.up * geometry.downwardCastLift,
            input.bounds.size, 0f, Vector2.down, geometry.downwardCastLift + 0.05f,
            filter, geometry.allowedDownwardHits);
        result.environmentOffset.y = FindSlopeRise(input, geometry, world,
            allowedCenter, allowedX, geometry.allowedDownwardHits, minUp, out _);
    }

    private static float FindSlopeRise(in EntityEnvironmentSolveInput input,
        EntityCandidateGeometry geometry, IPhysicsWorldAccess2D world, Vector2 center, float dx,
        List<RaycastHit2D> hits, float minUp, out Collider2D support)
    {
        support = null;
        float rise = 0f;
        foreach (RaycastHit2D hit in hits)
        {
            Collider2D surface = hit.collider;
            if (surface == null || surface.attachedRigidbody == input.body ||
                EnvironmentCapabilities.FindEntity(surface) != null) continue;
            geometry.RecordOneWayEffector(surface);
            if (hit.normal.y < minUp || hit.normal.x * dx >= 0f ||
                IsOneWayPassThrough(input, geometry, hit, input.bounds.min.y)) continue;
            float needed = geometry.downwardCastLift - hit.distance;
            float slopeRise = Mathf.Abs(dx * hit.normal.x / hit.normal.y);
            if (needed <= 0f || needed > slopeRise + 0.01f || needed <= rise) continue;
            support = surface;
            rise = needed;
        }
        if (support == null || HasLiftObstacle(input, geometry, world, center, support, rise))
        {
            support = null;
            return 0f;
        }
        return rise;
    }

    private static bool HasLiftObstacle(in EntityEnvironmentSolveInput input,
        EntityCandidateGeometry geometry, IPhysicsWorldAccess2D world, Vector2 center,
        Collider2D support, float rise)
    {
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask((int)input.groundLayer | (int)input.wallLayer);
        geometry.liftHits.Clear();
        world.BoxCast(center, input.bounds.size, 0f, Vector2.up, rise, filter, geometry.liftHits);
        foreach (RaycastHit2D hit in geometry.liftHits)
        {
            if (hit.collider == null || hit.collider == support ||
                hit.collider.attachedRigidbody == input.body ||
                EnvironmentCapabilities.FindEntity(hit.collider) != null) continue;
            geometry.RecordOneWayEffector(hit.collider);
            if (!IsOneWayPassThrough(input, geometry, hit, input.bounds.min.y)) return true;
        }
        return false;
    }

    private static bool IsOneWayPassThrough(in EntityEnvironmentSolveInput input,
        EntityCandidateGeometry geometry, RaycastHit2D hit, float currentBottom)
    {
        if (!geometry.oneWayEffectors.TryGetValue(hit.collider, out PlatformEffector2D effector))
            return false;
        if (Vector2.Angle(effector.transform.up, hit.normal) > effector.surfaceArc * 0.5f)
            return true;
        return input.groundCollider != hit.collider &&
            (geometry.candidateDelta.y > 0f || currentBottom < hit.point.y - 0.01f);
    }

    public void FinalizeRequest(in EntityEnvironmentSolveInput input,
        EntityCandidateGeometry geometry, ref EntitySolutionResult result)
    {
        Vector2 candidateDelta = input.motionFrame.plannedWorldDelta + result.displacementOffset;
        float allowedX = result.allowedHorizontalDelta;
        float motionX = input.motionFrame.motionDelta.x;
        float retained = Mathf.Abs(motionX) > 0.0001f
            ? Mathf.Clamp01((allowedX - input.motionFrame.platformDelta.x) / motionX) : 1f;
        float groundSlopeY = input.isGrounded
            ? input.motionFrame.motionDelta.y - input.verticalSpeed * input.fixedDeltaTime : 0f;
        result.requestedWorldDelta = new Vector2(allowedX,
            candidateDelta.y - groundSlopeY * (1f - retained) + result.environmentOffset.y);

        float pathLength = geometry.candidateDelta.magnitude;
        if (geometry.candidateDelta.y > 0.0001f &&
            result.requestedWorldDelta.y > 0f && pathLength > 0f)
        {
            float reachable = 1f;
            foreach (RaycastHit2D hit in geometry.pathHits)
            {
                if (hit.collider == null || hit.collider == input.groundCollider ||
                    hit.normal.y >= -0.7f ||
                    IsOneWayPassThrough(input, geometry, hit, input.bounds.min.y)) continue;
                float fraction = Mathf.Clamp01(Mathf.Max(0f, hit.distance - 0.001f) / pathLength);
                reachable = Mathf.Min(reachable, fraction);
            }
            result.requestedWorldDelta *= reachable;
        }

        bool finalBlocked = Mathf.Abs(result.requestedWorldDelta.x) + 0.0001f <
            Mathf.Abs(candidateDelta.x);
        result.blockedHorizontalDirection = finalBlocked ? Mathf.Sign(candidateDelta.x) : 0f;
    }
}
