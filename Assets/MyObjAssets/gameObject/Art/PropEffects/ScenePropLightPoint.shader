Shader "GameEmergence/ScenePropLightPoint"
{
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                // 光点直接用 UV 绘制柔边圆形，不需要额外图片或真实灯光。
                float2 offset = input.uv * 2 - 1;
                fixed4 color = input.color;
                color.a *= pow(saturate(1 - dot(offset, offset)), 2);
                return color;
            }
            ENDCG
        }
    }
}
