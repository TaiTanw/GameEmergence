using UnityEngine;

/// <summary>共用显示资源与 UI 操作手感配置，不持有玩法状态。</summary>
public sealed class GameUIAssets : ScriptableObject
{
    public Font font;
    public Sprite panel;
    public Sprite environmentPanel;
    public Sprite note;
    public Sprite bar;
    [Header("结束探索手感"), Min(.1f)] public float escapeHoldSeconds = 2f;
    [Min(.01f)] public float escapeRefillSeconds = .2f;
    static GameUIAssets loaded;
    public static GameUIAssets Shared => loaded != null ? loaded : loaded = Resources.Load<GameUIAssets>("GameUIAssets");
}
