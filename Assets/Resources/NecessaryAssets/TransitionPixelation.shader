Shader "GameEmergence/TransitionPixelation"
{
    Properties
    {
        _MainTex ("Camera Image", 2D) = "white" {}
        _BlockPixels ("Square Block Pixels", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlockPixels;
            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 size = _MainTex_TexelSize.zw;
                float block = max(1, _BlockPixels);
                float2 pixel = (floor(input.uv * size / block) + .5) * block;
                // 边缘不足一整块时仍采样画面内部，避免黑边或重复边缘颜色渗入。
                pixel = clamp(pixel, .5, size - .5);
                return tex2D(_MainTex, pixel / size);
            }
            ENDCG
        }
    }
}
