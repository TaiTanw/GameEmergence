using System.Collections;
using UnityEngine;

/// <summary>三个短结局共用的场景编排；只使用结局通信，不接管普通关规则、存档或结局选择。</summary>
public sealed class OrimeEnding : EndingSceneController
{
    public enum Moment { StillRaining, OtherSide, Unfolded }
    [SerializeField] Moment moment;
    [SerializeField] EndingActor actor;
    [SerializeField] EndingActor companion;
    [SerializeField] TextMesh firstLine;
    [SerializeField] TextMesh secondLine;
    [SerializeField] Transform finishPoint;
    [SerializeField] Transform companionFinishPoint;
    [SerializeField] Transform cameraPoint;
    [SerializeField] SpriteRenderer closingVeil;
    [SerializeField] ParticleSystem weather;
    [SerializeField] SpriteRenderer candleHalo;
    [SerializeField] float firstLineX = -2.5f;
    [SerializeField] float secondLineX = 2f;
    [SerializeField] float finishX = 4.5f;
    [SerializeField, Min(.1f)] float textFadeSeconds = 1.4f;
    [SerializeField, Min(0f)] float lastHoldSeconds = 7f;
    float sceneTime;
    bool following;
    Vector3 companionRestPosition;

    protected override IEnumerator OnPrepare(EndingSceneContext context)
    {
        // 必填引用失败由原宿主报告，不能显示半个结局后才确认存档。
        if (actor == null || firstLine == null || secondLine == null || finishPoint == null
            || cameraPoint == null || closingVeil == null || (moment != Moment.StillRaining && companion == null)
            || (moment == Moment.Unfolded && companionFinishPoint == null))
            throw new System.InvalidOperationException("orime 结局缺少角色、句子、镜头或演出落点。");
        actor.SetControlEnabled(false);
        context.SetControlledActor(actor);
        context.SetCameraPosition(cameraPoint.position);
        SetAlpha(closingVeil, 0f);
        PrepareLine(firstLine); PrepareLine(secondLine);
        sceneTime = 0f;
        following = false;
        if (companion != null)
        {
            companion.SetControlEnabled(false);
            companion.SetAutomaticMotion(false);
            companion.SetAutomaticAnimation(false);
            companionRestPosition = companion.transform.position;
            Play(companion, "Idle");
        }
        if (weather != null) weather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        // 在全黑准备字体和精灵；主角沿原宿主揭幕许可参与基础物理。
        yield return null;
    }

    static void PrepareLine(TextMesh text)
    {
        if (text.font != null && text.font.dynamic)
            text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
        Color color = text.color; color.a = 0f; text.color = color;
    }

    protected override void OnEndingBegin(EndingSceneContext context)
    {
        if (weather != null) weather.Play(true);
        StartCoroutine(Perform(context));
    }

    IEnumerator Perform(EndingSceneContext context)
    {
        yield return context.Delay(1.5f);
        if (!context.IsActive) yield break;
        actor.SetControlEnabled(true);
        following = moment == Moment.Unfolded;
        yield return Reach(context, firstLineX);
        yield return RevealLine(context, firstLine);
        yield return Reach(context, secondLineX);
        yield return RevealLine(context, secondLine);
        yield return Reach(context, finishX);
        // 跳跃抵达时先让角色自然落地，再交出物理，避免空中播放行走。
        while (context.IsActive && !actor.IsGrounded) yield return null;
        // 玩家抵达最后一小段空间才接演出，没有失败计时或死亡入口。
        following = false;
        actor.SetControlEnabled(false);
        actor.SetAutomaticMotion(false);
        actor.SetAutomaticAnimation(false);
        if (moment == Moment.Unfolded && companionFinishPoint != null)
        {
            // 两个形象分别停在不同位置；不合体、不消失，也不给出解释。
            Vector3 start = companion.transform.position;
            Vector3 actorStart = actor.transform.position;
            float elapsed = 0f;
            Play(actor, "Walk"); Play(companion, "Walk");
            Face(actor, finishPoint.position.x - actorStart.x);
            Face(companion, companionFinishPoint.position.x - start.x);
            while (context.IsActive && elapsed < 3.5f)
            {
                elapsed += context.DeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 3.5f));
                actor.transform.position = Vector3.Lerp(actorStart, finishPoint.position, t);
                companion.transform.position = Vector3.Lerp(start, companionFinishPoint.position, t);
                yield return null;
            }
            Play(companion, "Idle");
        }
        else
        {
            Vector3 start = actor.transform.position;
            Play(actor, "Walk"); Face(actor, finishPoint.position.x - start.x);
            float elapsed = 0f;
            while (context.IsActive && elapsed < 2.5f)
            {
                elapsed += context.DeltaTime;
                actor.transform.position = Vector3.Lerp(start, finishPoint.position, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 2.5f)));
                yield return null;
            }
            if (moment == Moment.OtherSide) Face(companion, 1f);
        }
        Play(actor, "Idle");
        yield return context.Delay(lastHoldSeconds);
        float fade = 0f;
        while (context.IsActive && fade < 1f)
        {
            fade += context.DeltaTime / 2.5f;
            SetAlpha(closingVeil, Mathf.Clamp01(fade));
            yield return null;
        }
        if (context.IsActive) context.ReturnToMenu();
    }

    IEnumerator Reach(EndingSceneContext context, float x)
    {
        while (context.IsActive && actor.transform.position.x < x) yield return null;
    }

    IEnumerator RevealLine(EndingSceneContext context, TextMesh text)
    {
        float elapsed = 0f;
        while (context.IsActive && elapsed < textFadeSeconds)
        {
            elapsed += context.DeltaTime;
            Color color = text.color; color.a = Mathf.Clamp01(elapsed / textFadeSeconds); text.color = color;
            yield return null;
        }
        // 每句话完整保留片刻再淡去，快速跑过也不会直接跳过文字。
        yield return context.Delay(3f);
        elapsed = 0f;
        while (context.IsActive && elapsed < textFadeSeconds)
        {
            elapsed += context.DeltaTime;
            Color color = text.color; color.a = 1f - Mathf.Clamp01(elapsed / textFadeSeconds); text.color = color;
            yield return null;
        }
    }

    void Update()
    {
        // 场景自己的表现也消费演出时钟；暂停和揭幕前均不推进。
        if (Context == null || Context.DeltaTime <= 0f) return;
        sceneTime += Context.DeltaTime;
        // 文字淡去期间也守住收尾位置，持续向右输入不会先撞边界再倒退。
        if (actor.ControlEnabled && actor.transform.position.x >= finishX)
            actor.SetControlEnabled(false);
        if (candleHalo != null)
        {
            Color tint = candleHalo.color;
            tint.a = .10f + .025f * Mathf.Sin(sceneTime * 2.1f) + .015f * Mathf.Sin(sceneTime * 5.3f);
            candleHalo.color = tint;
        }
        if (!following || companion == null) return;
        Vector3 desired = new Vector3(actor.transform.position.x + .95f, companionRestPosition.y, companionRestPosition.z);
        float dx = desired.x - companion.transform.position.x;
        companion.transform.position = Vector3.MoveTowards(companion.transform.position, desired, Context.DeltaTime * 2.4f);
        Face(companion, dx);
        Play(companion, Mathf.Abs(dx) > .04f ? "Walk" : "Idle");
    }

    static void Play(EndingActor target, string state)
    {
        if (target == null || target.Animator == null) return;
        if (!target.Animator.GetCurrentAnimatorStateInfo(0).IsName(state)) target.Animator.Play(state, 0, 0f);
    }
    static void Face(EndingActor target, float direction)
    {
        if (target == null || Mathf.Abs(direction) < .01f) return;
        SpriteRenderer sprite = target.GetComponentInChildren<SpriteRenderer>();
        if (sprite != null) sprite.flipX = direction < 0f;
    }
    static void SetAlpha(SpriteRenderer sprite, float alpha)
    { Color color = sprite.color; color.a = alpha; sprite.color = color; }

    protected override void OnEndingPauseChanged(EndingSceneContext context, bool paused)
    {
        if (weather == null || !context.IsRevealed) return;
        if (paused) weather.Pause(true); else weather.Play(true);
    }
    protected override void OnEndingExit(EndingSceneContext context)
    {
        following = false;
        if (actor != null) actor.SetControlEnabled(false);
        if (weather != null) weather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
