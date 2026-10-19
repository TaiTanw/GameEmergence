Shader "GameEmergence/UI/EscapeFog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _DeepColor ("Deep Blue", Color) = (0.018,0.035,0.13,1)
        _MistColor ("Mist Blue", Color) = (0.10,0.22,0.43,1)
        _Opacity ("Body Opacity", Range(0,0.95)) = 0.86
        _EdgeRoughness ("Edge Roughness", Range(0,0.4)) = 0.26
        _EdgeOpacity ("Dense Edge Opacity", Range(0,1)) = 1
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
        Blend SrcAlpha OneMinusSrcAlpha
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
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float4 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float4 uv : TEXCOORD0; float4 position : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color, _DeepColor, _MistColor;
            float _Opacity, _EdgeRoughness, _EdgeOpacity;
            float4 _ClipRect;
            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(cell), hash(cell + float2(1,0)), f.x),
                    lerp(hash(cell + float2(0,1)), hash(cell + float2(1,1)), f.x), f.y);
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float2 p = input.uv.xy * 2.0 - 1.0;
                float t = input.uv.w * 0.055;
                float broad = noise(p * 2.4 + float2(t,-t * 0.6));
                float detail = noise(p * 6.0 + broad * 1.7 + float2(-t * 0.7,t));
                float cloud = broad * 0.68 + detail * 0.32;
                float grain = noise(p * 34.0 + float2(t * 0.4,-t * 0.3));
                // 大块起伏叠加细碎缺口，轮廓从中心展开，四边仍保留透明余量。
                float boundary = min(0.94, 0.82 + (broad - 0.5) * _EdgeRoughness
                    + (detail - 0.5) * _EdgeRoughness * 0.45 + (grain - 0.5) * 0.04);
                float distance = length(p) - boundary * saturate(input.uv.z * 1.28);
                float coverage = (1.0 - smoothstep(-0.012, 0.022, distance)) * saturate(input.uv.z * 5.0);
                // 仅外沿的部分厚雾团达到不透明，中央云层继续透出场景。
                float rim = smoothstep(-0.18, -0.10, distance) * (1.0 - smoothstep(-0.04, 0.0, distance));
                float densePatch = smoothstep(0.43, 0.60, detail);
                float opacity = lerp(_Opacity * (0.82 + cloud * 0.18), _EdgeOpacity, rim * densePatch);
                float3 tint = lerp(_DeepColor.rgb, _MistColor.rgb, cloud * 0.78);
                tint = lerp(tint, _DeepColor.rgb, rim * densePatch * 0.55);
                fixed4 result = fixed4(tint, coverage * opacity) * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.position.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
