#if UNITY_EDITOR
using System;
using System.Collections;

/// <summary>仅编辑器编译的批量验证夹具，不保存到制作场景、不编入游戏。</summary>
public sealed class EndingPreparationProbe : EndingSceneController
{
    public static bool Throw, Disposed;
    protected override IEnumerator OnPrepare(EndingSceneContext context)
    {
        if (Throw) throw new InvalidOperationException("P16 expected preparation failure");
        try { while (context.IsActive) yield return null; }
        finally { Disposed = true; context.ReturnToMenu(); }
    }
}
#endif
