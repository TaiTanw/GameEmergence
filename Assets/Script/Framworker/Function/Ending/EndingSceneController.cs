using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>每个结局场景只挂一个宿主或其子类。制作脚本只重写四个通知，不调用流转管理器。</summary>
[DefaultExecutionOrder(-100)]
public class EndingSceneController : MonoBehaviour
{
    [SerializeField] EndingActor initialActor;
    [SerializeField] Transform cameraTarget;
    [SerializeField, Min(.1f), Tooltip("黑幕内准备的真实时间上限，超时保留旧进度并返回菜单。")]
    float preparationTimeout = 30f;
    readonly Dictionary<Animator, float> pausedAnimators = new Dictionary<Animator, float>();
    readonly List<AudioSource> pausedAudio = new List<AudioSource>();
    readonly List<PlayableDirector> pausedDirectors = new List<PlayableDirector>();
    Coroutine preparing;
    readonly Stack<IEnumerator> preparationSteps = new Stack<IEnumerator>();
    internal bool Prepared { get; private set; }
    internal string PreparationError { get; private set; }
    internal float PreparationTimeout => preparationTimeout;
    public EndingSceneContext Context { get; private set; }

    internal void Initialize(EndingSceneContext context)
    {
        Context = context;
        Prepared = false; PreparationError = null;
        foreach (EndingActor actor in GetSceneComponents<EndingActor>()) context.RegisterActor(actor);
        if (cameraTarget != null) context.FollowCamera(cameraTarget);
        else context.SetCameraPosition(transform.position);
        if (initialActor != null && !context.SetControlledActor(initialActor))
            throw new InvalidOperationException("初始结局角色必须启用且属于当前结局场景。");
        preparing = StartCoroutine(PrepareSafely());
    }

    // 准备只安置和校验；异步准备可 yield，关键剧情只从 OnEndingBegin 开始。
    protected virtual IEnumerator OnPrepare(EndingSceneContext context) { yield break; }
    protected virtual void OnEndingBegin(EndingSceneContext context) { }
    protected virtual void OnEndingPauseChanged(EndingSceneContext context, bool paused) { }
    protected virtual void OnEndingExit(EndingSceneContext context) { }

    IEnumerator PrepareSafely()
    {
        try { preparationSteps.Push(OnPrepare(Context)); }
        catch (Exception error) { PreparationError = error.Message; yield break; }
        // 展开嵌套协程以捕获准备异常；退出时停止整条准备，不让旧回调提交结果。
        try
        {
            while (Context.IsActive && preparationSteps.Count > 0)
            {
                object next = null;
                bool moved = false;
                try { moved = preparationSteps.Peek() != null && preparationSteps.Peek().MoveNext(); if (moved) next = preparationSteps.Peek().Current; }
                catch (Exception error) { PreparationError = error.Message; yield break; }
                if (!moved) { DisposeStep(preparationSteps.Pop()); if (PreparationError != null) yield break; continue; }
                if (next is IEnumerator nested) { preparationSteps.Push(nested); continue; }
                yield return next;
            }
        }
        finally { DisposePreparation(); }
        if (!Context.IsActive || PreparationError != null) yield break;
        foreach (EndingActor actor in GetSceneComponents<EndingActor>())
        {
            Context.RegisterActor(actor);
            if (!actor.ValidateConfiguration(out string error))
            { PreparationError = error; yield break; }
        }
        Prepared = true;
        preparing = null;
    }

    void DisposePreparation()
    {
        while (preparationSteps.Count > 0) DisposeStep(preparationSteps.Pop());
    }
    void DisposeStep(IEnumerator step)
    { try { (step as IDisposable)?.Dispose(); } catch (Exception error) { PreparationError = error.Message; } }

    internal void Begin()
    {
        Context.Reveal();
        OnEndingBegin(Context);
    }

    internal void SetPaused(bool paused)
    {
        Context.SetPaused(paused);
        if (paused)
        {
            foreach (Animator animator in GetSceneComponents<Animator>())
            { if (!animator.isActiveAndEnabled) continue; pausedAnimators[animator] = animator.speed; animator.speed = 0f; }
            foreach (AudioSource audio in GetSceneComponents<AudioSource>())
            { if (!audio.isPlaying) continue; pausedAudio.Add(audio); audio.Pause(); }
            foreach (PlayableDirector director in GetSceneComponents<PlayableDirector>())
            { if (director.state != PlayState.Playing) continue; pausedDirectors.Add(director); director.Pause(); }
        }
        else
        {
            foreach (var pair in pausedAnimators) if (pair.Key != null) pair.Key.speed = pair.Value;
            foreach (AudioSource audio in pausedAudio) if (audio != null) audio.UnPause();
            foreach (PlayableDirector director in pausedDirectors) if (director != null) director.Resume();
            pausedAnimators.Clear(); pausedAudio.Clear(); pausedDirectors.Clear();
        }
        // 场景附加暂停通知出错也不能打断共用菜单的暂停／继续交接。
        try { OnEndingPauseChanged(Context, paused); }
        catch (Exception error) { Debug.LogException(error, this); }
    }

    internal void Release()
    {
        if (Context == null || !Context.IsActive) return;
        // 先作废通信，再停止协程；准备的 finally 也不能重新发起旧场景流转。
        Context.Release();
        if (preparing != null) StopCoroutine(preparing);
        preparing = null;
        DisposePreparation();
        try { OnEndingExit(Context); }
        catch (Exception error) { Debug.LogException(error, this); }
        StopAllCoroutines();
        pausedAnimators.Clear(); pausedAudio.Clear(); pausedDirectors.Clear();
    }
    void OnDestroy() => Release();
    T[] GetSceneComponents<T>() where T : Component
    {
        var found = new List<T>();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
        return found.ToArray();
    }
    internal static EndingSceneController FindFor(Scene scene, out string error)
    {
        EndingSceneController result = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (EndingSceneController host in root.GetComponentsInChildren<EndingSceneController>(true))
            {
                if (result != null) { error = "结局场景只能有一个结局宿主脚本。"; return null; }
                result = host;
            }
        error = result == null || !result.isActiveAndEnabled ? "结局场景需要一个启用的结局宿主脚本。" : null;
        return error == null ? result : null;
    }
}
