using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// 移动平台在相位 0 更新位置并记录 delta；实体踩在上面时，经 ENV-S1 读到该帧位移。
/// 职能：平台自身运动与位移能力提供者；启停钩子只管理自身回调，环境解除由旧基类统一处理。
/// </summary>
public class Taijie : BaseGround, IPlatformMotion, IMovementSpeedModifier
{
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
    float startPoint=5;
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
        toPoint = v3;
        this.transform.position = new Vector3(v3.x, v3.y+startPoint,v3.z);

        startPos = transform.position;

        nowtime= AudioSettings.dspTime;
        //lastT = 0;
        start = true;
        if (paused) pausedAt = nowtime;
    }
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
            newPos = Vector3.Lerp(startPos, toPoint, t);
        }

        delta = (Vector2)(newPos - transform.position);
        transform.position = newPos;

        //lastT = t;
    }

    protected override void OnSourceDisabled()
    {
        activePlatforms.Remove(this);
        delta = Vector2.zero;
        if (!MonoPublicMgr.IsQuitting)
        {
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(FixFun, 0);

        }
    }
}
