#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>运行时只发布相位时点和实体生命周期；编辑器自行读取原生实际事实。</summary>
public static class PhysicsEditorObservationBridge
{
    public struct GizmoData
    {
        public bool hasGround, hasTop, hasLeft, hasRight;
        public Vector3 groundCenter, topCenter, leftCenter, rightCenter;
        public Vector2 horizontalSize, verticalSize;
    }
    public static event Action<int> PhysicsFrameStarted;
    public static event Action<int, int> PhysicsPhaseCompleted;
    public static event Action<BasicEntity> EntityEnabled;
    public static event Action<BasicEntity> EntityDisabled;
    public static int FixedTick { get; private set; }
    public static void ResetFixedTick() => FixedTick = 0;
    public static void BeginPhysicsFrame()
    {
        FixedTick++;
        Invoke(PhysicsFrameStarted, FixedTick);
    }
    public static void CompletePhysicsPhase(int phase)
    {
        if (PhysicsPhaseCompleted == null) return;
        foreach (Action<int, int> observer in PhysicsPhaseCompleted.GetInvocationList())
        {
            try { observer(FixedTick, phase); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
    public static void NotifyEntityEnabled(BasicEntity entity) => Invoke(EntityEnabled, entity);
    public static void NotifyEntityDisabled(BasicEntity entity) => Invoke(EntityDisabled, entity);
    private static void Invoke<T>(Action<T> observers, T value)
    {
        if (observers == null) return;
        foreach (Action<T> observer in observers.GetInvocationList())
        {
            try { observer(value); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
#endif
