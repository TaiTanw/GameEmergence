using System;
using System.Collections.Generic;

/// <summary>
/// 一次环境面板的工作副本。预览只改副本，确认才提交原来源和背包；同一个计算点只预览在一个位置。
/// 不保存逐次操作历史，不持有角色速度或物理接触状态。
/// </summary>
public sealed class EnvironmentEditDraft
{
    readonly CognitionBackpack backpack;
    readonly int backpackVersion, sourceVersion;
    readonly EnvironmentParameter[] colliderSlots, triggerSlots;
    readonly List<CognitionBackpackItem> items = new List<CognitionBackpackItem>();
    readonly System.Collections.ObjectModel.ReadOnlyCollection<CognitionBackpackItem> readOnlyItems;
    readonly HashSet<int> originalItemIds = new HashSet<int>();
    readonly Dictionary<EnvironmentParameter, int> propertyItemIds = new Dictionary<EnvironmentParameter, int>();
    int? geometryItemId;
    bool completed;
    public BasicPhysicalObject Source { get; }
    public bool IsTriggerEnvironment { get; private set; }
    public int SlotCapacity => colliderSlots.Length;
    public IReadOnlyList<CognitionBackpackItem> Items => readOnlyItems;
    public bool HasChanges { get; private set; }
    public bool IsCompleted => completed;
    public int BackpackCapacity => backpack.Capacity;
    public int PreviewBackpackCount => FinalItems().Count;
    public float Cost
    {
        get
        {
            float result = 0f;
            foreach (CognitionBackpackItem item in FinalItems())
                if (!originalItemIds.Contains(item.Id)) result += backpack.AcquisitionCost(item);
            return result;
        }
    }

    public EnvironmentEditDraft(BasicPhysicalObject source, CognitionBackpack backpack)
    {
        if (source == null || backpack == null) throw new ArgumentNullException("环境来源和背包必须就绪。");
        Source = source;
        this.backpack = backpack;
        colliderSlots = source.CopyEnvironmentSlots(false);
        triggerSlots = source.CopyEnvironmentSlots(true);
        sourceVersion = source.EnvironmentParameterVersion;
        backpackVersion = backpack.Version;
        IsTriggerEnvironment = source.IsTriggerEnvironment;
        foreach (CognitionBackpackItem item in backpack.Items)
        {
            items.Add(item.Copy());
            originalItemIds.Add(item.Id);
        }
        readOnlyItems = items.AsReadOnly();
    }

    public EnvironmentParameter GetSlot(bool trigger, int index)
    {
        EnvironmentParameter[] slots = trigger ? triggerSlots : colliderSlots;
        return index >= 0 && index < slots.Length ? slots[index] : null;
    }

    public string CalculationTarget(CognitionBackpackItem item)
    {
        if (item?.CalculationPoint == null) return "";
        EnvironmentCalculationPoint point = item.CalculationPoint;
        for (int i = 0; i < SlotCapacity; i++)
        {
            if (colliderSlots[i]?.CalculationPoint == point) return "碰撞器槽 " + (i + 1);
            if (triggerSlots[i]?.CalculationPoint == point) return "触发器槽 " + (i + 1);
        }
        foreach (CognitionBackpackItem nature in items)
            if (nature.Property?.CalculationPoint == point) return "背包 · " + nature.Property.DisplayName;
        return "";
    }

    public bool CanUseItem(int itemId, bool trigger, int slot, bool geometry = false)
    {
        if (completed) return false;
        CognitionBackpackItem item = FindItem(itemId);
        if (item == null) return false;
        // 独特性质只注入几何槽；浏览另一列表不会改变实际几何或赋予注入资格。
        if (geometry) return !IsTriggerEnvironment && item.Property?.Kind == EnvironmentParameterKind.TriggerEnvironment;
        if (trigger != IsTriggerEnvironment || slot < 0 || slot >= SlotCapacity) return false;
        EnvironmentParameter target = GetSlot(trigger, slot);
        if (target == null)
            return item.Property != null && item.Property.Kind != EnvironmentParameterKind.TriggerEnvironment
                && item.Property.IsTriggerProperty == trigger && !ContainsKind(trigger, item.Property.Kind);
        // 已确认的附加点随性质整体保留；只有尚留在背包里的预览点允许改选、替换预览。
        return item.IsCalculation && target.SupportsCalculation
            && (target.CalculationPoint == null || FindCalculationItem(target.CalculationPoint) != null);
    }

    public bool TryUseItem(int itemId, bool trigger, int slot, bool geometry, out string error)
    {
        error = null;
        if (!CanUseItem(itemId, trigger, slot, geometry))
        { error = "该内容物不能用于当前槽。"; return false; }
        CognitionBackpackItem item = FindItem(itemId);
        if (geometry)
        {
            IsTriggerEnvironment = true;
            geometryItemId = item.Id;
            items.Remove(item);
        }
        else if (!item.IsCalculation)
        {
            EnvironmentParameter injected = item.Property.CopyRuntime();
            (trigger ? triggerSlots : colliderSlots)[slot] = injected;
            propertyItemIds[injected] = item.Id;
            items.Remove(item);
        }
        else
        {
            EnvironmentParameter target = GetSlot(trigger, slot);
            // 先在副本验证结果；无效乘积不解除原目标、不改变任何已有预览。
            EnvironmentParameter trial = target.CopyRuntime();
            trial.RemoveCalculation();
            if (!trial.TryAttachCalculation(item.CalculationPoint))
            { error = "计算结果无效，不能应用此计算点。"; return false; }
            ReleasePointPreview(item.CalculationPoint);
            target.RemoveCalculation();
            target.TryAttachCalculation(item.CalculationPoint);
        }
        HasChanges = true;
        return true;
    }

    public bool TryExtract(bool trigger, int slot, out string error)
    {
        error = null;
        if (completed || slot < 0 || slot >= SlotCapacity || GetSlot(trigger, slot) == null)
        { error = "此槽没有可以抽取的性质。"; return false; }
        EnvironmentParameter property = GetSlot(trigger, slot);
        // 非生效列表仍可抽取；性质与附加点只创建一个完整内容物。
        CognitionBackpackItem item = propertyItemIds.TryGetValue(property, out int originalId)
            ? new CognitionBackpackItem(originalId, property.CopyRuntime(), null) : backpack.NewProperty(property);
        items.Add(item);
        if (FinalItems().Count > backpack.Capacity)
        { items.Remove(item); error = "背包空间不足。"; return false; }
        (trigger ? triggerSlots : colliderSlots)[slot] = null;
        HasChanges = true;
        return true;
    }

    public bool TryExtractTrigger(out string error)
    {
        error = null;
        if (completed || !IsTriggerEnvironment)
        { error = "当前没有触发器性质可抽取。"; return false; }
        CognitionBackpackItem item = geometryItemId.HasValue
            ? new CognitionBackpackItem(geometryItemId.Value, new TriggerEnvironmentParameter(), null)
            : backpack.NewProperty(new TriggerEnvironmentParameter());
        items.Add(item);
        if (FinalItems().Count > backpack.Capacity)
        { items.Remove(item); error = "背包空间不足。"; return false; }
        IsTriggerEnvironment = false;
        HasChanges = true;
        return true;
    }

    public bool TrySplitCalculation(int itemId, out string error)
    {
        error = null;
        CognitionBackpackItem item = FindItem(itemId);
        if (completed || item?.Property?.CalculationPoint == null)
        { error = "请选择背包中带计算点的性质。"; return false; }
        EnvironmentCalculationPoint point = item.Property.CalculationPoint;
        CognitionBackpackItem existing = FindCalculationItem(point);
        // 预览点尚未真正出包，拆回时恢复它的可用性；已消耗的点才作为新内容物入包。
        CognitionBackpackItem split = existing ?? backpack.NewCalculation(point);
        item.Property.RemoveCalculation();
        if (existing == null) items.Add(split);
        if (FinalItems().Count > backpack.Capacity)
        {
            if (existing == null) items.Remove(split);
            item.Property.TryAttachCalculation(point);
            error = "背包空间不足。";
            return false;
        }
        HasChanges = true;
        return true;
    }

    /// <summary>调用前由关卡规则复核漫游资格。全部检查完成后提交，收费不发出中途阶段切换。</summary>
    public bool TryCommit(LevelCognition cognition, out string error)
    {
        error = null;
        if (completed) { error = "本次修改已经确认。"; return false; }
        if (Source == null || !Source.AllowsEnvironmentEditing || Source.EnvironmentParameterVersion != sourceVersion
            || backpack.Version != backpackVersion)
        { error = "环境或背包已经改变，请重新打开面板。"; return false; }
        List<CognitionBackpackItem> final = FinalItems();
        if (final.Count > backpack.Capacity) { error = "背包空间不足。"; return false; }
        if (!ValidatePointOwnership(final, out error)) return false;
        if (!Source.ValidateEnvironmentEdit(colliderSlots, triggerSlots, IsTriggerEnvironment, out error)) return false;
        float cost = Cost;
        if (cognition == null || float.IsNaN(cost) || float.IsInfinity(cost) || cognition.Remaining < cost)
        { error = "认知不足，修改尚未确认。"; return false; }
        var committedItems = new List<CognitionBackpackItem>(final.Count);
        foreach (CognitionBackpackItem item in final) committedItems.Add(item.Copy());
        // 来源提交已预检且发生在同一次调用中；它不调用玩家或费用入口，不能中途重入。
        if (!Source.TryCommitEnvironmentEdit(colliderSlots, triggerSlots, IsTriggerEnvironment, out error)) return false;
        cognition.TryPayAbilityCost(cost);
        backpack.ReplaceValidated(committedItems);
        completed = true;
        return true;
    }

    List<CognitionBackpackItem> FinalItems()
    {
        var final = new List<CognitionBackpackItem>();
        foreach (CognitionBackpackItem item in items)
            if (!item.IsCalculation || CalculationTarget(item).Length == 0) final.Add(item);
        return final;
    }

    bool ValidatePointOwnership(List<CognitionBackpackItem> final, out string error)
    {
        var points = new HashSet<EnvironmentCalculationPoint>();
        foreach (EnvironmentParameter property in colliderSlots)
            if (property?.CalculationPoint != null && !points.Add(property.CalculationPoint))
            { error = "同一个计算点不能同时用于多个性质。"; return false; }
        foreach (EnvironmentParameter property in triggerSlots)
            if (property?.CalculationPoint != null && !points.Add(property.CalculationPoint))
            { error = "同一个计算点不能同时用于多个性质。"; return false; }
        foreach (CognitionBackpackItem item in final)
        {
            EnvironmentCalculationPoint point = item.IsCalculation ? item.CalculationPoint : item.Property?.CalculationPoint;
            if (point != null && !points.Add(point))
            { error = "计算点的背包与环境归属重复。"; return false; }
        }
        error = null;
        return true;
    }

    void ReleasePointPreview(EnvironmentCalculationPoint point)
    {
        foreach (EnvironmentParameter property in colliderSlots)
            if (property?.CalculationPoint == point) property.RemoveCalculation();
        foreach (EnvironmentParameter property in triggerSlots)
            if (property?.CalculationPoint == point) property.RemoveCalculation();
        foreach (CognitionBackpackItem item in items)
            if (item.Property?.CalculationPoint == point) item.Property.RemoveCalculation();
    }

    bool ContainsKind(bool trigger, EnvironmentParameterKind kind)
    {
        foreach (EnvironmentParameter property in trigger ? triggerSlots : colliderSlots)
            if (property != null && property.Kind == kind) return true;
        return false;
    }

    CognitionBackpackItem FindItem(int id)
    {
        foreach (CognitionBackpackItem item in items) if (item.Id == id) return item;
        return null;
    }

    CognitionBackpackItem FindCalculationItem(EnvironmentCalculationPoint point)
    {
        foreach (CognitionBackpackItem item in items)
            if (item.IsCalculation && item.CalculationPoint == point) return item;
        return null;
    }
}
