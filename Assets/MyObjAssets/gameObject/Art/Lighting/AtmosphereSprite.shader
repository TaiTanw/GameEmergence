Shader "GameEmergence/AtmosphereSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(1,3)) = 1
        [MaterialToggle] PixelSnap ("Pixel Snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert nofog noambient nolightmap nodynlightmap keepalpha exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        #pragma multi_compile_instancing
        #pragma multi_compile _ PIXELSNAP_ON
        #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
        #include "UnitySprites.cginc"

        half _Brightness;
        half _SceneLightingEnabled;
        half _SceneLightingTransition;
        half3 _SceneLightingAmbient;
        half _SceneLightingLocal;

        struct Input { float2 uv_MainTex; fixed4 color; };

        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            vertex.vertex = UnityFlipSprite(vertex.vertex.xyz, _Flip);
            // 精灵朝向镜头，使用原生点光／聚光灯的距离与角度衰减。
            vertex.normal = float3(0,0,-1);
            #ifdef PIXELSNAP_ON
            vertex.vertex = UnityPixelSnap(vertex.vertex);
            #endif
            output.color = vertex.color * _Color * _RendererColor;
        }

        void surf(Input input, inout SurfaceOutput output)
        {
            fixed4 color = SampleSpriteTexture(input.uv_MainTex) * input.color;
            half3 rgb = color.rgb * color.a * _Brightness;
            output.Alpha = color.a;
            half lighting = _SceneLightingEnabled * (1 - _SceneLightingTransition);
            // 无场景设置时保持原色。环境照度单独提供，不受旧场景天空盒影响。
            output.Emission = rgb * lerp(half3(1,1,1), _SceneLightingAmbient, lighting);
            output.Albedo = rgb * lighting * _SceneLightingLocal;
        }
        ENDCG
    }
    Fallback "Sprites/Default"
}
