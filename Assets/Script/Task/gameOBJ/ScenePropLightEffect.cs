using UnityEngine;

/// <summary>通关物的提亮与悬浮光点。启停归 SceneProp 的阶段接口，本组件不持有玩法状态。</summary>
[DisallowMultipleComponent]
public sealed class ScenePropLightEffect : MonoBehaviour
{
    [SerializeField, Tooltip("道具本体精灵，材质需使用支持 _Brightness 的提亮着色器")]
    SpriteRenderer targetSprite;
    [SerializeField, Tooltip("本物体的悬浮光点子对象；颜色、数量、范围与漂浮速度在粒子系统中配置")]
    ParticleSystem lightPoints;
    [SerializeField, Min(1f), Tooltip("探索时的亮度倍率；1 为原亮度，较大值会使浅色区域接近白色")]
    float brightness = 1.35f;

    static readonly int Brightness = Shader.PropertyToID("_Brightness");
    MaterialPropertyBlock originalProperties;
    MaterialPropertyBlock litProperties;
    SpriteRenderer litSprite;

    public bool ValidateConfiguration(out string error)
    {
        // 配置关联必须留在本道具层级内，不能启停另一件道具的表现。
        if (targetSprite == null || lightPoints == null)
        { error = "缺少道具精灵或悬浮光点引用。"; return false; }
        if (!targetSprite.transform.IsChildOf(transform) || !lightPoints.transform.IsChildOf(transform))
        { error = "道具精灵和光点必须属于光效组件所在的道具层级。"; return false; }
        // 安全：非法数值或不支持提亮的材质明确拒绝，不替换制作配置。
        if (float.IsNaN(brightness) || float.IsInfinity(brightness) || brightness < 1f)
        { error = "亮度倍率必须为不小于 1 的有限数值。"; return false; }
        if (targetSprite.sharedMaterial == null || !targetSprite.sharedMaterial.HasProperty(Brightness))
        { error = "道具材质缺少 _Brightness 参数，请关联 ScenePropBrightness 材质。"; return false; }
        error = null;
        return true;
    }

    void OnEnable()
    {
        if (!ValidateConfiguration(out string error))
        { Debug.LogError($"{name} 的通关光效配置无效：{error}", this); return; }
        // 只修改本实例的渲染属性，关闭时恢复启用前的整份属性，不写共享材质或原图颜色。
        originalProperties ??= new MaterialPropertyBlock();
        litProperties ??= new MaterialPropertyBlock();
        litSprite = targetSprite;
        litSprite.GetPropertyBlock(originalProperties);
        litSprite.GetPropertyBlock(litProperties);
        litProperties.SetFloat(Brightness, brightness);
        litSprite.SetPropertyBlock(litProperties);
        lightPoints.Play(true);
    }

    void OnDisable()
    {
        // 阶段关闭或道具停用时清除残留，不能把探索光点带到逃生。
        if (lightPoints != null) lightPoints.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (litSprite != null) litSprite.SetPropertyBlock(originalProperties);
        litSprite = null;
    }
}
