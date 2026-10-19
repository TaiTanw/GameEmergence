using UnityEngine;

/// <summary>共用显示资源与 UI 操作手感配置，不持有玩法状态。</summary>
public sealed class GameUIAssets : ScriptableObject
{
    public Font font;
    public Sprite panel;
    public Sprite environmentPanel;
    public Sprite note;
    public Sprite bar;
    [Header("界面音效")] public AudioClip buttonClickSound;
    [Header("结束探索手感"), Min(.1f), Tooltip("满认知时的确认秒数；按下时乘当前剩余认知比例。")]
    public float escapeHoldSeconds = 2f;
    [Min(.01f), Tooltip("低认知时的最短确认秒数，防止误触。")]
    public float escapeMinimumHoldSeconds = .2f;
    [Min(.01f)] public float escapeRefillSeconds = .2f;
    static GameUIAssets loaded;
    public static GameUIAssets Shared => loaded != null ? loaded : loaded = Resources.Load<GameUIAssets>("GameUIAssets");
}
