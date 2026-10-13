using System.Collections;
using UnityEngine;

/// <summary>制作示例：只使用场景引用及结局通信，不调用存档、关卡或输入管理器。</summary>
public sealed class EndingExample : EndingSceneController
{
    [SerializeField] EndingActor actor;
    [SerializeField] GameObject revealObject;
    protected override IEnumerator OnPrepare(EndingSceneContext context)
    {
        if (actor == null) throw new System.InvalidOperationException("结局示例需要关联场景演出角色。");
        actor.SetControlEnabled(false);
        context.SetControlledActor(actor);
        context.FollowCamera(actor.transform);
        if (revealObject != null)
        {
            // 全黑准备期间先生成文字网格并绘制一帧，避免第一次显示与开放操作挤在同一帧。
            revealObject.SetActive(true);
            foreach (TextMesh text in revealObject.GetComponentsInChildren<TextMesh>(true))
                if (text.font != null && text.font.dynamic)
                    text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
            yield return null;
            revealObject.SetActive(false);
        }
    }
    protected override void OnEndingBegin(EndingSceneContext context) => StartCoroutine(Perform(context));
    IEnumerator Perform(EndingSceneContext context)
    {
        // 两秒演出随暂停停止；脚本决定何时开放玩家操作。
        yield return context.Delay(2f);
        if (!context.IsActive) yield break;
        if (revealObject != null) revealObject.SetActive(true);
        actor.SetControlEnabled(true);
    }
    protected override void OnEndingExit(EndingSceneContext context)
    { if (actor != null) actor.SetControlEnabled(false); }
}
