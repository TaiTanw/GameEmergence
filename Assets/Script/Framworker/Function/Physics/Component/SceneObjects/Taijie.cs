using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// 移动平台在相位 0 更新位置并记录 delta；实体踩在上面时，经 ENV-S1 读到该帧位移。
/// 职能：平台自身运动与位移能力提供者；启停钩子只管理自身回调，环境解除由旧基类统一处理。
/// </summary>
public class Taijie : BaseGround, IPlatformMotion, IMovementSpeedModifier, IMechanismTarget
{
    [Header("机关目标（平台／实心门共用位移入口）")]
    [SerializeField, Tooltip("相对首次场景位置的世界 XY 偏移；门与平台均从实际位置往返。")]
    Vector2 mechanismOffset = new Vector2(0f, 3f);
    [SerializeField, Tooltip("每次压住或释放后的完整缓动时长，必须大于零。")]
    float mechanismTravelTime = 1.5f;
    Vector3 defaultPosition;
    bool defaultsCaptured, mechanismActive, easedMotion;
    static readonly List<Taijie> activePlatforms = new List<Taijie>();
    static bool worldPaused;
    bool paused;
    double pausedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetWorldPause()
    {
        activePlatforms.Clear();
        worldPaused = false;
    }

    /// <summary>规则暂停平台，保留当前位置和运行进度；恢复时不追赶暂停期间的时间。</summary>
    public static void SetWorldPaused(bool value)
    {
        worldPaused = value;
        foreach (Taijie platform in activePlatforms)
            if (platform != null) platform.SetPaused(value);
    }

    void SetPaused(bool value)
    {
        if (paused == value) return;
        double now = AudioSettings.dspTime;
        if (value) pausedAt = now;
        // 实际逻辑：从运行起点排除暂停时间，不移动平台来补进度。
        else if (start) nowtime += now - pausedAt;
        paused = value;
        delta = Vector2.zero;
    }
    /// <summary>平台在相位 0 记录的本帧位移；停止或禁用时清零，由平台自身持有。</summary>
    protected Vector2 delta;
    /// <summary>采样：通过 IPlatformMotion 供脚下实体读取，随后并入其运动快照。</summary>
    public Vector2 Delta => delta;
    /// <summary>曾接受机关往返命令的目标；夹击只判实心门，不把旧生成动画当机关门。</summary>
    internal bool HasMechanismMotion => easedMotion;

    [SerializeField] protected float speedChangeNum; // 主动速度加性修饰，旧序列化名称保留。
    public float MovementSpeedOffset => speedChangeNum;

    /// <summary>
    /// 平台效应器
    /// </summary>
    [SerializeField]
    private SpriteRenderer spriteRenderer;
    /// <summary>
    /// 目标点
    /// </summary>
    Vector3 toPoint;
    /// <summary>
    /// 生成时高度
    /// </summary>
    /// <summary>
    /// 到目标点时间
    /// </summary>
    double endTime=3;
    /// <summary>
    /// 当前时间
    /// </summary>
    double nowtime;
    /// <summary>
    /// 速度
    /// </summary>

    /// <summary>
    /// 开始运行
    /// </summary>
    bool start;
    /// <summary>
    /// 起始点缓存
    /// </summary>
    Vector3 startPos;

    protected override void OnSourceEnabled()
    {
        CaptureDefaults();
        if (!activePlatforms.Contains(this)) activePlatforms.Add(this);
        SetPaused(worldPaused);
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(FixFun, 0);
    }
    /// <summary>
    /// 生成时初始化
    /// </summary>
    /// <param name="v3"></param>
    public void ToMovePoint(Vector3 v3)
    {
        CaptureDefaults();
        // 安全：错误终点拒绝移动，不用原位置代替配置。
        if (!Finite(v3.x) || !Finite(v3.y) || !Finite(v3.z))
        { Debug.LogError($"{name}：平台目标位置必须是有限数值。", this); return; }
        toPoint = v3;
        // 实际逻辑：从当前位置移动，机关和旧调用都不隐式跳到高处。
        startPos = transform.position;
        endTime = 3d;
        easedMotion = false;
        nowtime= AudioSettings.dspTime;
        //lastT = 0;
        start = true;
        if (paused) pausedAt = nowtime;
    }

    void CaptureDefaults()
    {
        if (defaultsCaptured) return;
        defaultPosition = transform.position;
        defaultsCaptured = true;
    }

    public bool ValidateMechanismConfiguration(out string error)
    {
        error = null;
        if (!Finite(mechanismTravelTime) || mechanismTravelTime <= 0f)
            error = $"{name}：机关缓动时长必须是大于零的有限数值。";
        else if (!Finite(mechanismOffset.x) || !Finite(mechanismOffset.y))
            error = $"{name}：机关目标偏移必须是有限数值。";
        return error == null;
    }

    public bool TrySetMechanismActive(bool active)
    {
        // 安全：目标无效时不覆盖原运行进度。
        if (!ValidateMechanismConfiguration(out string error))
        { Debug.LogError(error, this); return false; }
        CaptureDefaults();
        if (mechanismActive == active) return true;
        mechanismActive = active;
        startPos = transform.position;
        toPoint = active ? defaultPosition + (Vector3)mechanismOffset : defaultPosition;
        endTime = mechanismTravelTime;
        nowtime = AudioSettings.dspTime;
        easedMotion = true;
        start = true;
        // 实际逻辑：暂停中的命令记录起点，恢复后才推进。
        if (paused) pausedAt = nowtime;
        return true;
    }

    public void RestoreMechanismDefaults()
    {
        CaptureDefaults();
        start = mechanismActive = easedMotion = false;
        delta = Vector2.zero;
        transform.position = defaultPosition;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    void FixFun()
    {
        if (!start || paused)
        {
            delta = Vector2.zero;
            return;
        }

        double rawT = (AudioSettings.dspTime - nowtime) / endTime;
        float t = Mathf.Clamp01((float)rawT);

        //t = Mathf.Max(t, lastT);

        Vector3 newPos;

        if (t >= 1.0f)
        {
            delta = (Vector2)(toPoint - transform.position);
            transform.position = toPoint;

            start = false;
            //lastT = 1f;
            return;
        }
        else
        {
            float progress = easedMotion ? t * t * (3f - 2f * t) : t;
            newPos = Vector3.Lerp(startPos, toPoint, progress);
        }

        delta = (Vector2)(newPos - transform.position);
        transform.position = newPos;

        //lastT = t;
    }

    protected override void OnSourceDisabled()
    {
        SetPaused(true);
        activePlatforms.Remove(this);
        delta = Vector2.zero;
        if (!MonoPublicMgr.IsQuitting)
        {
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(FixFun, 0);

        }
    }
}
