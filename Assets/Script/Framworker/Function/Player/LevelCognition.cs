using UnityEngine;

/// <summary>本关共享认知余额；转换与模拟计时消费同一记录，后续格子继续接这里，不另建角色余额。</summary>
public sealed class LevelCognition
{
    readonly float capacity;
    float spent;
    public float Remaining { get; private set; }
    public bool IsExhausted => Remaining <= 0f;

    public LevelCognition(float initial) => capacity = Remaining = ValidAmount(initial);

    /// <summary>只由统一规则在成功转换后调用；强制与手动同价，余额最低为零。</summary>
    internal void PayFormCost(float cost) => Spend(ValidAmount(cost));

    /// <summary>模拟的游戏时间与转换费用消费同一余额；暂停时间由场景宿主挡住。</summary>
    internal void PaySimulationTime(float seconds, float amountPerSecond) =>
        Spend(ValidAmount(seconds) * ValidAmount(amountPerSecond));

    /// <summary>能力购买只在费用合法且足够时成功；失败保持余额，不以钳到零冒充已支付。</summary>
    public bool TryPayAbilityCost(float cost)
    {
        // 安全：无效或余额不足的费用不产生部分付款。
        if (float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0f || Remaining < cost) return false;
        Spend(cost);
        return true;
    }

    /// <summary>格子只记录实际预留额，余额不足时不得按配置全额登记退款。</summary>
    internal float OccupyMapCost(float cost)
    {
        float previous = Remaining;
        Remaining -= Mathf.Min(Remaining, ValidAmount(cost));
        return previous - Remaining;
    }

    /// <summary>取消只释放原预留，并以初始容量减已消耗为退款上界。</summary>
    internal void ReleaseMapCost(float reserved)
    {
        float refundable = Mathf.Max(0f, capacity - spent - Remaining);
        Remaining += Mathf.Min(ValidAmount(reserved), refundable);
    }

    void Spend(float cost)
    {
        // 费用乘积溢出仍耗尽现有余额，累计的只有实际付出，余额始终不为负。
        float previous = Remaining;
        Remaining -= Mathf.Min(Remaining, cost);
        float paid = previous - Remaining;
        spent = Mathf.Min(capacity, spent + paid);
    }

    static float ValidAmount(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
}
