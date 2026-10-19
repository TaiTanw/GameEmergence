Shader "GameEmergence/UI/CognitionBarGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _GlowColor ("Glow Color", Color) = (1,0.76,0.3,1)
        _GlowIntensity ("Glow Intensity", Range(0,1)) = 0.75
        _GlowRadius ("Glow Radius (layout pixels)", Range(0.5,6)) = 6
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // 普通图形使用预乘透明；柔光输出零alpha，因此只有柔光做局部加色。
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 position : TEXCOORD1;
                float2 extent : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd, _GlowColor;
            float4 _ClipRect;
            float _GlowIntensity, _GlowRadius;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.uv = input.uv;
                // 矩形四角的绝对值相同，得到本条实际剩余半宽及固定星高；无需逐条材质实例。
                output.extent = abs(input.uv.xy) - 6.0;
                return output;
            }

            float profile(float x, float halfWidth, float starHeight)
            {
                float outward = min(starHeight * 2.5, halfWidth * 0.25);
                float starCenter = halfWidth - outward;
                float join = starCenter - outward * 0.45;
                float ribbon = starHeight * 0.34;
                if (x <= join) return ribbon;
                if (x <= starCenter)
                {
                    float t = (x - join) / max(starCenter - join, 0.0001);
                    return lerp(ribbon, starHeight, t * t * t);
                }
                float t = saturate(1.0 - (x - starCenter) / max(outward, 0.0001));
                return starHeight * t * t * t;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float clipFactor = 1.0;
                #ifdef UNITY_UI_CLIP_RECT
                clipFactor *= UnityGet2DClipping(input.position.xy, _ClipRect);
                #endif
                // 只有柔光四边形携带正的整条半宽；普通星框、白底和黑刻度沿原颜色绘制。
                if (input.uv.z > 0.0)
                {
                    float2 samplePosition = abs(input.uv.xy);
                    float height = profile(min(samplePosition.x, input.extent.x), input.uv.z, input.extent.y);
                    float2 outside = max(float2(samplePosition.x - input.extent.x, samplePosition.y - height), 0.0);
                    float falloff = saturate(1.0 - length(outside) / max(_GlowRadius, 0.0001));
                    falloff *= falloff;
                    float outward = min(input.extent.y * 2.5, input.uv.z * 0.25);
                    float join = input.uv.z - outward * 1.45;
                    float2 hole = max(float2(min(input.extent.y * 0.9, join * 0.5), input.extent.y * 0.5) - input.uv.w, 0.0001);
                    // 保留中央空心菱形的透明孔，不用光晕填实它。
                    falloff *= step(1.0, samplePosition.x / hole.x + samplePosition.y / hole.y);
                    float coverage = falloff * clipFactor * input.color.a * _GlowColor.a;
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(coverage - 0.001);
                    #endif
                    return fixed4(_GlowColor.rgb * input.color.rgb * _GlowIntensity * coverage, 0.0);
                }
                fixed4 color = (tex2D(_MainTex, input.uv.xy) + _TextureSampleAdd) * input.color;
                color.a *= clipFactor;
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
