using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 摆在场景里的本关配置。登记教学、出生点、精神体预设和转换参数，不创建角色，不抢控制。
/// </summary>
public class SceneLevelSetup : MonoBehaviour
{
    [Header("本关性质")]
    [SerializeField, Tooltip("勾选后本关不创建精神体，实体自己受控")]
    bool teaching;
    [Header("出生点")]
    [SerializeField, Tooltip("实体进入本关后站立的位置")]
    Transform bodySpawn;
    [SerializeField, Tooltip("非教学关精神体的位置，不要和实体出生点重合")]
    Transform spiritSpawn;
    [SerializeField, Tooltip("地图接入前的固定试验重置起点；未设置时使用本配置物体的位置")]
    Transform trialStart;
    [Header("精神体")]
    [SerializeField, Tooltip("非教学关使用的精神体预设。教学关不会实例化它")]
    GameObject spiritPrefab;
    [SerializeField, Min(0f), Tooltip("成功转换后下一次手动切换的冷却秒数；死亡和区域强制不检查冷却")]
    float manualFormCooldown = 0.5f;
    [SerializeField, Min(0f), Tooltip("非教学关开局认知余额；初始化召唤不扣形态费用")]
    float initialCognition = 100f;
    [SerializeField, Min(0f), Tooltip("每次成功的手动／死亡／区域强制转换都扣一次；失败不扣")]
    float formChangeCost = 1f;
    [SerializeField, Min(0f), Tooltip("探索／模拟每游戏秒消耗的认知；漫游、菜单暂停与加载不计时")]
    float simulationCognitionPerSecond = 0.5f;
    [Header("认知地图")]
    [SerializeField, Tooltip("启用后试验重置使用玩家当前地图格中心；教学关不建立地图记录")]
    bool mapEnabled;
    [SerializeField, Tooltip("手工关联本关全部地图格子，允许边界相接，不允许面积重叠或漏引用")]
    MapCell[] mapCells = new MapCell[0];

    [Header("认知背包")]
    [SerializeField, Min(0), Tooltip("两种形态共用同一背包；初始装载与失败恢复不收取获取费用。")]
    int cognitionBackpackCapacity = 12;
    [SerializeField, Min(0f)] float cognitionPropertyCost = 1f;
    [SerializeField, Min(0f)] float cognitionCalculationCost = 1f;
    [SerializeReference, Tooltip("本关初始携带的有限性质，可留空；触发器性质为独立内容物。")]
    List<EnvironmentParameter> initialCognitionProperties = new List<EnvironmentParameter>();
    [SerializeField, Tooltip("本关初始计算点。每个数字产生一个独立点；必须为有限非负倍率。")]
    float[] initialCognitionCalculations = { 2f, .2f };

    public CognitionBackpack CreateCognitionBackpack() => new CognitionBackpack(cognitionBackpackCapacity,
        cognitionPropertyCost, cognitionCalculationCost, initialCognitionProperties, initialCognitionCalculations);

    public bool Teaching => teaching;
    public GameObject SpiritPrefab => spiritPrefab;
    public float ManualFormCooldown => Mathf.Max(0f, manualFormCooldown);
    public float InitialCognition => Mathf.Max(0f, initialCognition);
    public float FormChangeCost => Mathf.Max(0f, formChangeCost);
    public float SimulationCognitionPerSecond => Mathf.Max(0f, simulationCognitionPerSecond);
    public Vector2 BodyPoint => Pose(bodySpawn);
    public Vector2 SpiritPoint => Pose(spiritSpawn);
    public Vector2 TrialPoint => Pose(trialStart);
    public Vector3 BodyPose => FullPose(bodySpawn);
    public Vector3 SpiritPose => FullPose(spiritSpawn);
    public bool MapEnabled => mapEnabled;
    public IReadOnlyList<MapCell> MapCells => mapCells;

    [Header("逃生")]
    [SerializeField, Tooltip("拖入本场景的 WaterPool 或 WaterPoolWithEffects 实例；作为主水后保留环境效果，但禁止玩家编辑。")]
    WaterRegion risingWater;
    [SerializeField, Min(0f), Tooltip("水面每秒上涨的世界高度；只在正式逃生运行。")]
    float waterRiseSpeed = 0.5f;
    [SerializeField, Tooltip("关联主水后必填。拖入独立空物体，用其世界 Y 表示最高水面；不得挂在上涨水下。")]
    Transform risingWaterMaximum;
    [SerializeField, Range(0.01f, 1f)] float lowCognitionMovementMultiplier = 0.8f;
    [SerializeField] EscapeAbilityOffer[] escapeAbilities = {
        new EscapeAbilityOffer("步伐强化", PlayerEffectKind.MovementMultiplier, 20f, 1.2f),
        new EscapeAbilityOffer("起跳强化", PlayerEffectKind.JumpMultiplier, 20f, 1.2f)
    };
    public WaterRegion RisingWater => risingWater;
    public float WaterRiseSpeed => waterRiseSpeed;
    public Transform RisingWaterMaximum => risingWaterMaximum;
    public float LowCognitionMovementMultiplier => lowCognitionMovementMultiplier;
    public System.Collections.Generic.IReadOnlyList<EscapeAbilityOffer> EscapeAbilities => escapeAbilities;

    public bool ValidateRisingWaterConfiguration(out string error)
    {
        error = null;
        // 安全：无主水的关卡不要求最高点；孤立的最高点不是有效主水配置。
        if (risingWater == null)
        {
            if (risingWaterMaximum != null) error = $"{name}：设置了上涨水最高点，但没有关联上涨主水。";
            return error == null;
        }
        if (risingWater.gameObject.scene != gameObject.scene)
            error = $"{name}：上涨主水必须属于本场景。";
        else if (risingWaterMaximum == null)
            error = $"{name}：已关联上涨主水，必须指定上涨水最高点。";
        else if (risingWaterMaximum.gameObject.scene != gameObject.scene)
            error = $"{name}：上涨水最高点必须属于本场景。";
        else if (risingWaterMaximum.IsChildOf(risingWater.transform))
            error = $"{name}：上涨水最高点必须独立于水物体，不能随水一起移动。";
        else if (!risingWater.ValidateRunConfiguration(waterRiseSpeed, risingWaterMaximum.position.y, out error))
            return false;
        return error == null;
    }

    public bool PrepareRisingWater(out string error)
    {
        if (!ValidateRisingWaterConfiguration(out error)) return false;
        // 实际逻辑：关卡持有关联，水宿主只接收速度与水面上限，不自行搜索场景标记。
        return risingWater == null || risingWater.PrepareRun(waterRiseSpeed, risingWaterMaximum.position.y);
    }

    public static SceneLevelSetup Current { get; private set; }

    void Awake()
    {
        // 实际逻辑：后加载的场景配置覆盖上一关留下的登记。
        if (Current != null && Current != this)
            Debug.LogError("场景里有两份关卡配置，只保留后登记的一份。", this);
        Current = this;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    Vector2 Pose(Transform mark) => FullPose(mark);

    Vector3 FullPose(Transform mark)
    {
        // 安全：没摆出生点时用配置物体自己的位置，避免空引用。深度只留给角色根节点遮挡。
        return mark != null ? mark.position : transform.position;
    }
}
