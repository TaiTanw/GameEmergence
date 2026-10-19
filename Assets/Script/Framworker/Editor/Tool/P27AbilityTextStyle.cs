#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>仅制作迷雾结果页的SDF字库、共享柔光材质与文字组件。</summary>
public static class P27AbilityTextStyle
{
    public const string FontPath = "Assets/MyObjAssets/PlayDataA/FZYTK Ability SDF.asset";
    public const string GlowPath = "Assets/MyObjAssets/gameObject/Art/UI/AbilityTextGlow.mat";
    public const string SeedText = "本场体能效果强化与削弱可能同时出现这些只在关逃生时效当前无任何正面负面移动速度游泳起跳初强度额外空中次开始剩余认知 ·×：。；，%+-/0123456789\n";
    public static readonly string[] ResultPaths = {
        "AbilityTitleText", "AbilityPage/AbilityHintText", "AbilityPage/EmptyResultText",
        "AbilityPage/PositiveResult/PositiveResultTitleText", "AbilityPage/PositiveResult/PositiveResultValueText",
        "AbilityPage/NegativeResult/NegativeResultTitleText", "AbilityPage/NegativeResult/NegativeResultValueText",
        "AbilityPage/FinalValuesText", "BeginEscapeButton/Label" };

    public static void EnsureAssets()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/MyObjAssets/PlayDataA/FZYTK.TTF");
            font = TMP_FontAsset.CreateFontAsset(source, 64, 14, GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, true);
            if (font == null) throw new InvalidOperationException("无法从现用字体建立能力菜单SDF字库。");
            font.name = "FZYTK Ability SDF";
            font.atlasTextures[0].name = "Ability SDF Atlas";
            font.material.name = "Ability SDF Plain";
            font.material.shader = Shader.Find("TextMeshPro/Distance Field");
            string ascii = ""; for (int c = 32; c <= 126; c++) ascii += (char)c;
            if (!font.TryAddCharacters(ascii + SeedText.Replace("\n", ""), out string missing))
                throw new InvalidOperationException("能力菜单字体缺字：" + missing);
            AssetDatabase.CreateAsset(font, FontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            // 预置字形随包保存，避免首次打开结果页才临时生成中文。
            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(font);
        }
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(GlowPath);
        if (glow == null)
        {
            glow = new Material(font.material) { name = "Ability Text Glow" };
            glow.EnableKeyword("GLOW_ON");
            glow.SetColor("_FaceColor", Color.white);
            glow.SetFloat("_FaceDilate", .025f);
            glow.SetColor("_GlowColor", new Color(.38f, .68f, 1f, .38f));
            glow.SetFloat("_GlowOffset", 0f);
            glow.SetFloat("_GlowInner", .02f);
            glow.SetFloat("_GlowOuter", .32f);
            glow.SetFloat("_GlowPower", .65f);
            AssetDatabase.CreateAsset(glow, GlowPath);
        }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("自定义工具/开发工具/UI/应用 P27.3 能力页文字柔光")]
    public static void ApplyToPrefab()
    {
        const string path = "Assets/MyObjAssets/UIPanel/GameplayPanel.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { Apply(root.transform.Find("EscapeChoiceArea")); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    public static void Apply(Transform overlay)
    {
        EnsureAssets();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(GlowPath);
        Transform card = overlay.Find("Choices");
        Transform title = card.Find("AbilityTitleText");
        if (title == null)
        {
            var obj = Object.Instantiate(card.Find("EscapeBudgetText").gameObject, card, false);
            obj.name = "AbilityTitleText"; title = obj.transform;
            title.GetComponent<Text>().text = "本场体能效果";
        }
        foreach (string path in ResultPaths)
        {
            GameObject target = card.Find(path).gameObject;
            Text old = target.GetComponent<Text>();
            TextMeshProUGUI label = target.GetComponent<TextMeshProUGUI>();
            if (old != null)
            {
                string text = old.text; float size = old.fontSize; TextAnchor alignment = old.alignment;
                Object.DestroyImmediate(old);
                label = target.AddComponent<TextMeshProUGUI>();
                label.text = text; label.fontSize = size;
                TextAlignmentOptions[] anchors = { TextAlignmentOptions.TopLeft, TextAlignmentOptions.Top, TextAlignmentOptions.TopRight,
                    TextAlignmentOptions.Left, TextAlignmentOptions.Center, TextAlignmentOptions.Right,
                    TextAlignmentOptions.BottomLeft, TextAlignmentOptions.Bottom, TextAlignmentOptions.BottomRight };
                label.alignment = anchors[(int)alignment];
            }
            label.font = font; label.fontSharedMaterial = glow; label.color = Color.white;
            label.raycastTarget = false; label.richText = false; label.enableAutoSizing = false;
            label.enableWordWrapping = true; label.overflowMode = TextOverflowModes.Truncate;
            label.extraPadding = true; label.UpdateMeshPadding();
        }
        title.gameObject.SetActive(false);
    }
}
#endif
