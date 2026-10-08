#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>在原物理相位结束后读取实体和原生刚体，窗口不保存另一份运动请求或解算结果。</summary>
[InitializeOnLoad]
public static class PhysicsEditorEntityObservation
{
    private static readonly Dictionary<BasicEntity, EntityPhysicsDebugSnapshot> records = new Dictionary<BasicEntity, EntityPhysicsDebugSnapshot>();
    private static int observerCount;

    static PhysicsEditorEntityObservation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) records.Clear();
        };
        AssemblyReloadEvents.beforeAssemblyReload += StopAllObservers;
    }

    public static void StartObserving()
    {
        if (++observerCount != 1) return;
        PhysicsEditorObservation.PhysicsPhaseCompleted += SamplePhase;
        PhysicsEditorObservationBridge.EntityDisabled += Remove;
    }

    public static void StopObserving()
    {
        if (observerCount == 0 || --observerCount != 0) return;
        PhysicsEditorObservation.PhysicsPhaseCompleted -= SamplePhase;
        PhysicsEditorObservationBridge.EntityDisabled -= Remove;
        records.Clear();
    }

    private static void StopAllObservers()
    {
        if (observerCount > 0)
        {
            PhysicsEditorObservation.PhysicsPhaseCompleted -= SamplePhase;
            PhysicsEditorObservationBridge.EntityDisabled -= Remove;
        }
        observerCount = 0;
        records.Clear();
    }

    private static void Remove(BasicEntity entity) { if (!ReferenceEquals(entity, null)) records.Remove(entity); }

    private static void SamplePhase(int tick, int phase)
    {
        if (phase != 1 && phase != 2 && phase != 3 && phase != 5) return;
        foreach (BasicEntity entity in Object.FindObjectsOfType<BasicEntity>())
            if (entity.isActiveAndEnabled) records[entity] = Capture(entity, tick, phase);
    }

    public static EntityPhysicsDebugSnapshot GetSnapshot(BasicEntity entity)
    {
        if (entity == null) return default;
        if (!Application.isPlaying) return Capture(entity, PhysicsEditorObservationBridge.FixedTick, -1);
        return records.TryGetValue(entity, out EntityPhysicsDebugSnapshot snapshot)
            ? snapshot : new EntityPhysicsDebugSnapshot { entityName = entity.name, phase = -2 };
    }

    private static EntityPhysicsDebugSnapshot Capture(BasicEntity entity, int tick, int phase) => new EntityPhysicsDebugSnapshot
    {
        fixedTick = tick, phase = phase, entityName = entity.name,
        bodyType = entity.DebugBodyType, actualPosition = entity.DebugActualPosition,
        rotation = entity.DebugRotation, angularVelocity = entity.DebugAngularVelocity,
        planarVelocity = entity.DebugPlanarVelocity,
        isGrounded = entity.DebugIsGrounded, isTopBlocked = entity.DebugIsTopBlocked,
        isOnLeftWall = entity.DebugIsOnLeftWall, isOnRightWall = entity.DebugIsOnRightWall,
        groundNormal = entity.DebugGroundNormal, groundCollider = entity.DebugGroundCollider,
        leftWallCollider = entity.DebugLeftWallCollider, rightWallCollider = entity.DebugRightWallCollider,
        environmentSourceCount = entity.DebugEnvironmentSourceCount,
        movementModifierCount = entity.DebugMovementModifierCount, stateVelocityCount = entity.DebugStateVelocityCount,
        dynamicForceCount = entity.DebugDynamicForceCount,
        environmentSlowingMultiplier = entity.DebugEnvironmentSlowingMultiplier,
        environmentJumpHeightOffset = entity.DebugEnvironmentJumpHeightOffset
    };
}

/// <summary>只读的实际位置、速度、几何和环境事实。</summary>
public struct EntityPhysicsDebugSnapshot
{
    public int fixedTick, phase;
    public string entityName;
    public RigidbodyType2D bodyType;
    public Vector2 actualPosition, planarVelocity, groundNormal;
    public float rotation, angularVelocity;
    public bool isGrounded, isTopBlocked, isOnLeftWall, isOnRightWall;
    public Collider2D groundCollider, leftWallCollider, rightWallCollider;
    public int environmentSourceCount, movementModifierCount, stateVelocityCount, dynamicForceCount;
    public float environmentSlowingMultiplier, environmentJumpHeightOffset;
}
#endif
