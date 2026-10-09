using UnityEngine;

/// <summary>本关共享认知余额；实体和精神体消费同一记录。P8 的计时和格子继续接这里，不另建角色余额。</summary>
public sealed class LevelCognition
{
    public float Remaining { get; private set; }
    public bool IsExhausted => Remaining <= 0f;

    public LevelCognition(float initial) => Remaining = ValidAmount(initial);

    /// <summary>只由统一规则在成功转换后调用；强制与手动同价，余额最低为零。</summary>
    internal void PayFormCost(float cost) => Remaining = Mathf.Max(0f, Remaining - ValidAmount(cost));

    static float ValidAmount(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
}
