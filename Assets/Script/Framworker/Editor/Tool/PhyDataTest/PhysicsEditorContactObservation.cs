#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PhyData;

/// <summary>相位 4 原生模拟结束后查询真实接触；没有预测接触、传力批次或纠偏数据。</summary>
[InitializeOnLoad]
public static class PhysicsEditorContactObservation
{
    private static readonly List<ContactPairDebugSnapshot> contacts = new List<ContactPairDebugSnapshot>();
    private static readonly ContactPoint2D[] buffer = new ContactPoint2D[128];
    private static int observerCount;
    public static IReadOnlyList<ContactPairDebugSnapshot> Contacts => contacts;
    public static int PublishedFixedTick { get; private set; } = -1;

    static PhysicsEditorContactObservation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) Clear();
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
        Clear();
    }

    private static void StopAllObservers()
    {
        if (observerCount > 0)
        {
            PhysicsEditorObservation.PhysicsPhaseCompleted -= SamplePhase;
            PhysicsEditorObservationBridge.EntityDisabled -= Remove;
        }
        observerCount = 0;
        Clear();
    }

    private static void Remove(BasicEntity entity) => contacts.RemoveAll(pair => ReferenceEquals(pair.entityA, entity) || ReferenceEquals(pair.entityB, entity));

    private static void SamplePhase(int tick, int phase)
    {
        if (phase != 4) return;
        contacts.Clear();
        var pairs = new HashSet<ulong>();
        foreach (BasicEntity entity in Object.FindObjectsOfType<BasicEntity>())
        {
            if (!entity.isActiveAndEnabled) continue;
            Rigidbody2D body = entity.GetComponent<Rigidbody2D>();
            if (body == null || !body.simulated) continue;
            int count = body.GetContacts(buffer);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D point = buffer[i];
                if (point.collider == null || point.otherCollider == null) continue;
                uint a = unchecked((uint)point.collider.GetInstanceID()), b = unchecked((uint)point.otherCollider.GetInstanceID());
                ulong key = a < b ? ((ulong)a << 32) | b : ((ulong)b << 32) | a;
                if (!pairs.Add(key)) continue;
                contacts.Add(new ContactPairDebugSnapshot
                {
                    entityA = point.collider.GetComponentInParent<BasicEntity>(), entityB = point.otherCollider.GetComponentInParent<BasicEntity>(),
                    colliderA = point.collider, colliderB = point.otherCollider,
                    position = point.point, normal = point.normal, separation = point.separation,
                    normalImpulse = point.normalImpulse
                });
            }
        }
        PublishedFixedTick = tick;
    }

    private static void Clear() { contacts.Clear(); PublishedFixedTick = -1; }
}

namespace PhyData
{
    public struct ContactPairDebugSnapshot
    {
        public BasicEntity entityA, entityB;
        public Collider2D colliderA, colliderB;
        public Vector2 position, normal;
        public float separation, normalImpulse;
    }
}
#endif
