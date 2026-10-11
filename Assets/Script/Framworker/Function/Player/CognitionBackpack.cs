using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>一个不可变的乘法点；对象身份用于识别预览中的同一个点，不保存接收者或计算历史。</summary>
public sealed class EnvironmentCalculationPoint
{
    public float Multiplier { get; }
    public string DisplayName => "×" + Multiplier.ToString("0.###", CultureInfo.InvariantCulture);

    public EnvironmentCalculationPoint(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "计算点必须是有限非负倍率。");
        Multiplier = multiplier;
    }
}

/// <summary>背包中的一个内容物：完整性质（可带计算点），或者独立计算点。</summary>
public sealed class CognitionBackpackItem
{
    public int Id { get; }
    public EnvironmentParameter Property { get; }
    public EnvironmentCalculationPoint CalculationPoint { get; }
    public bool IsCalculation => CalculationPoint != null;
    public string DisplayName => IsCalculation ? CalculationPoint.DisplayName
        : Property.DisplayName + "  " + Property.ValueText
            + (Property.CalculationCount > 0 ? "  [" + Property.CalculationText + "]" : "");

    internal CognitionBackpackItem(int id, EnvironmentParameter property, EnvironmentCalculationPoint point)
    {
        Id = id;
        Property = property;
        CalculationPoint = point;
    }

    internal CognitionBackpackItem Copy() => new CognitionBackpackItem(Id, Property?.CopyRuntime(), CalculationPoint);
}

/// <summary>本关规则持有的唯一背包。制作初始装载不算玩家获取；正式入包费用在整面板提交时结算。</summary>
public sealed class CognitionBackpack
{
    readonly List<CognitionBackpackItem> items = new List<CognitionBackpackItem>();
    readonly System.Collections.ObjectModel.ReadOnlyCollection<CognitionBackpackItem> readOnlyItems;
    readonly float propertyCost, calculationCost;
    int nextId;
    public int Capacity { get; }
    public int Version { get; private set; }
    public IReadOnlyList<CognitionBackpackItem> Items => readOnlyItems;

    public CognitionBackpack(int capacity, float propertyCost, float calculationCost,
        IEnumerable<EnvironmentParameter> initialProperties = null, IEnumerable<float> initialCalculations = null)
    {
        if (capacity < 0 || !ValidCost(propertyCost) || !ValidCost(calculationCost))
            throw new ArgumentException("认知背包容量及获取费用配置无效。");
        Capacity = capacity;
        this.propertyCost = propertyCost;
        this.calculationCost = calculationCost;
        readOnlyItems = items.AsReadOnly();
        if (initialProperties != null)
            foreach (EnvironmentParameter property in initialProperties)
            {
                // 制作配置里的空项不产生内容物；实际项独立复制，不回写关卡资产。
                if (property == null) continue;
                if (!property.IsConfigurationValid) throw new ArgumentException("初始背包性质配置无效。");
                items.Add(NewProperty(property));
            }
        if (initialCalculations != null)
            foreach (float multiplier in initialCalculations)
                items.Add(NewCalculation(new EnvironmentCalculationPoint(multiplier)));
        if (items.Count > Capacity) throw new ArgumentException("认知背包初始内容超过容量。");
    }

    internal CognitionBackpackItem NewProperty(EnvironmentParameter property) =>
        new CognitionBackpackItem(++nextId, property.CopyRuntime(), null);
    internal CognitionBackpackItem NewCalculation(EnvironmentCalculationPoint point) =>
        new CognitionBackpackItem(++nextId, null, point);
    internal float AcquisitionCost(CognitionBackpackItem item) => item.IsCalculation ? calculationCost : propertyCost;

    internal void ReplaceValidated(List<CognitionBackpackItem> replacement)
    {
        // 调用者已验证容量、身份和费用；一次替换后再增加版本，使旧草稿不能覆盖新背包。
        items.Clear();
        items.AddRange(replacement);
        Version++;
    }

    static bool ValidCost(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
}
