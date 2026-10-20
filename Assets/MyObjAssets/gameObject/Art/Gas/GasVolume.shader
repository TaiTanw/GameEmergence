Shader "GameEmergence/GasVolume"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
        [HideInInspector] _GasEnabled ("Gas Enabled", Float) = 0
        [HideInInspector] _GasRect ("Local Center / Half Size", Vector) = (0,0,1.5,1.5)
        [HideInInspector] _GasScale ("World Scale", Vector) = (1,1,0,0)
        [HideInInspector] _GasAppearance ("Density / Softness / Roughness / Noise", Vector) = (1,0.22,0.24,1.15)
        [HideInInspector] _GasTime ("Gas Time", Float) = 0
        [HideInInspector] _GasPhase ("Gas Phase", Float) = 0
        [HideInInspector] _GasPadding ("Outer Feather Margin", Float) = 0.6
    }
    SubShader
    {
        // 使用物体空间轮廓，禁止合批改写顶点空间；同一材质仍由所有气体共用。
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert nofog noambient nolightmap nodynlightmap keepalpha exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        #pragma multi_compile_instancing
        #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
        #include "UnitySprites.cginc"

        float _GasEnabled, _GasTime, _GasPhase, _GasPadding;
        float4 _GasRect, _GasScale, _GasAppearance;
        half _SceneLightingEnabled, _SceneLightingTransition, _SceneLightingLocal;
        half3 _SceneLightingAmbient;
        struct Input { float2 uv_MainTex; fixed4 color; float2 gasPosition; };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            vertex.vertex = UnityFlipSprite(vertex.vertex.xyz, _Flip);
            vertex.normal = float3(0,0,-1);
            output.color = vertex.color * _Color * _RendererColor;
            output.gasPosition = vertex.vertex.xy;
        }

        float hash(float2 p)
        {
            p = frac(p * float2(123.34,456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float noise(float2 p)
        {
            float2 cell = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash(cell), hash(cell + float2(1,0)), f.x),
                lerp(hash(cell + float2(0,1)), hash(cell + float2(1,1)), f.x), f.y);
        }

        float cloudNoise(float2 p)
        {
            return noise(p) * .57 + noise(p * 2.03 + 13.7) * .29 + noise(p * 4.11 + 29.3) * .14;
        }

        void surf(Input input, inout SurfaceOutput output)
        {
            fixed4 color = SampleSpriteTexture(input.uv_MainTex) * input.color;
            if (_GasEnabled > .5)
            {
                float2 p = (input.gasPosition - _GasRect.xy) * _GasScale.xy;
                float2 halfSize = max(_GasRect.zw * _GasScale.xy, .0001);
                float shortest = min(halfSize.x, halfSize.y);
                float2 n = p * _GasAppearance.w + _GasPhase * float2(.123,.347);
                float t = _GasTime;
                float broad = noise(n * .72 + float2(t * .16,-t * .11));
                float detail = noise(n * 2.15 + float2(-t * .23,t * .13));
                float2 warp = float2(broad, detail) - .5;
                float cloud = cloudNoise(n + warp * 1.35 + float2(t * .18,t * .09));
                float grain = noise(n * 17.0 + float2(-t * .14,t * .19));

                // 原判定矩形内始终完整覆盖，只有框外的雾边随噪声起伏并渐隐。
                float2 q = abs(p) - halfSize;
                float distance = length(max(q,0)) + min(max(q.x,q.y),0);
                float irregularity = (broad - .5) * _GasAppearance.z + (detail - .5) * _GasAppearance.z * .6;
                float edge = _GasPadding * (.65 + irregularity);
                float feather = _GasPadding * (.2 + _GasAppearance.y * 1.2);
                float coverage = 1.0 - smoothstep(max(0,edge - feather), edge, distance);

                // 大块云层改变浓淡，细噪点只轻微扰动透明度，保持持续翻涌而非闪烁。
                float opacity = lerp(.40,1.0,smoothstep(.20,.78,cloud));
                opacity *= (.88 + .12 * sin(t * .72 + _GasPhase)) * (.90 + .10 * grain);
                color.rgb = input.color.rgb * lerp(.64,1.17,cloud);
                color.a = saturate(input.color.a * _GasAppearance.x * coverage * opacity);
            }
            // 沿用现有场景受光与转场权重，实心形态仍显示原方块贴图。
            half3 rgb = color.rgb * color.a;
            half lighting = _SceneLightingEnabled * (1 - _SceneLightingTransition);
            // 附加灯光仅叠加颜色，不重复累计透明度，避免多灯下雾团变成不透明块。
            #ifdef UNITY_PASS_FORWARDADD
            output.Alpha = 0;
            #else
            output.Alpha = color.a;
            #endif
            output.Emission = rgb * lerp(half3(1,1,1), _SceneLightingAmbient, lighting);
            output.Albedo = rgb * lighting * _SceneLightingLocal;
        }
        ENDCG
    }
    Fallback "Sprites/Default"
}
