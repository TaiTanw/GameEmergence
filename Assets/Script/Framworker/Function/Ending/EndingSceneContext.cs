using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>结局脚本只读的入场数据；重播沿保存目标，不再次计算贡献。</summary>
public readonly struct EndingSceneData
{
    public readonly string SceneName;
    public readonly Vector3 Vector;
    public readonly bool IsReplay;
    public EndingSceneData(string sceneName, Vector3 vector, bool isReplay)
    { SceneName = sceneName; Vector = vector; IsReplay = isReplay; }
}

/// <summary>专属角色只消费这两个字段，不接普通玩家的动作数据。</summary>
public readonly struct EndingInputFrame
{
    public readonly float Horizontal;
    public readonly bool JumpPressed;
    public EndingInputFrame(float horizontal, bool jumpPressed)
    { Horizontal = horizontal; JumpPressed = jumpPressed; }
}

/// <summary>演出通信入口。外部流转由绑定的服务处理，失效的旧场景不能操作新场景。</summary>
public sealed class EndingSceneContext
{
    readonly Action<EndingActor> bindInput;
    readonly Action<Transform> followCamera;
    readonly Action<Vector3> positionCamera;
    readonly Action returnToMenu;
    readonly List<EndingActor> actors = new List<EndingActor>();
    public EndingSceneData Data { get; }
    public bool IsActive { get; private set; } = true;
    public bool IsRevealed { get; private set; }
    public bool IsPaused { get; private set; }
    public float DeltaTime => IsActive && IsRevealed && !IsPaused ? Time.deltaTime : 0f;
    public EndingActor ControlledActor { get; private set; }

    internal EndingSceneContext(EndingSceneData data, Action<EndingActor> bindInput,
        Action<Transform> followCamera, Action<Vector3> positionCamera, Action returnToMenu)
    { Data = data; this.bindInput = bindInput; this.followCamera = followCamera;
        this.positionCamera = positionCamera; this.returnToMenu = returnToMenu; }

    public bool RegisterActor(EndingActor actor)
    {
        if (!IsActive || actor == null || actor.gameObject.scene.name != Data.SceneName) return false;
        if (!actors.Contains(actor)) actors.Add(actor);
        actor.Attach(this);
        return true;
    }

    public bool SetControlledActor(EndingActor actor)
    {
        if (!IsActive || (actor != null && (!actor.isActiveAndEnabled || !RegisterActor(actor)))) return false;
        if (ControlledActor != null) ControlledActor.ClearInput();
        ControlledActor = actor;
        bindInput(actor);
        return true;
    }

    public void FollowCamera(Transform target)
    { if (IsActive && target != null && target.gameObject.scene.name == Data.SceneName) followCamera(target); }
    public void SetCameraPosition(Vector3 position)
    { if (IsActive) positionCamera(position); }
    public void ReturnToMenu() { if (IsActive) returnToMenu(); }
    internal void ResetActorInput(EndingActor actor)
    {
        if (!IsActive || ControlledActor != actor) return;
        bindInput(null); bindInput(actor);
    }

    /// <summary>演出延时随暂停停止；退出立即结束，不需要读取全局阶段或时间开关。</summary>
    public IEnumerator Delay(float seconds)
    {
        while (IsActive && seconds > 0f)
        { seconds -= DeltaTime; yield return null; }
    }

    internal void Reveal() { IsRevealed = true; RefreshActors(); }
    internal void SetPaused(bool paused) { IsPaused = paused; RefreshActors(); }
    internal void Release()
    {
        if (!IsActive) return;
        IsActive = false;
        RefreshActors();
        ControlledActor = null;
        actors.Clear();
    }
    void RefreshActors()
    {
        foreach (EndingActor actor in actors)
            if (actor != null) actor.RefreshActivity();
    }
}
