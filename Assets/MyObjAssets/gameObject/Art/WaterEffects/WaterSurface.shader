Shader "GameEmergence/WaterSurface"
{
    Properties
    {
        _BodyColor ("Body Color", Color) = (0, .85, 1, .24)
        _SurfaceColor ("Surface Color", Color) = (.65, 1, 1, .75)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _BodyColor, _SurfaceColor, _Wave, _RippleShape, _WaterSize, _Appearance;
            float4 _Ripples[8];
            float _WaterTime;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 vertex : SV_POSITION; float depth : TEXCOORD0; };
            Output vert(Input v)
            {
                Output o;
                float x = mul(unity_ObjectToWorld, v.vertex).x;
                float k = 6.2831853 / _Wave.y;
                float offset = _Wave.x * (sin(k * (x - _WaterTime * _Wave.z))
                    + .35 * sin(k * 1.73 * (x + _WaterTime * _Wave.z * .6)));
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float age = _WaterTime - _Ripples[i].y;
                    float distance = abs(x - _Ripples[i].x) - _RippleShape.z * age;
                    float envelope = exp(-distance * distance / (_RippleShape.y * _RippleShape.y));
                    float alive = step(0, age) * (1 - step(_WaterSize.w, age));
                    offset -= _Ripples[i].z * exp(-max(0, age) * _RippleShape.w) * envelope
                        * cos(distance * 6.2831853 / _RippleShape.x) * alive;
                }
                // 仅上缘顶点变形，底边与水域物理范围保持原位置。
                v.vertex.y += clamp(offset, -_Wave.w, _Wave.w) * v.uv.x / _WaterSize.z;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.depth = (1 - v.uv.y) * _WaterSize.y;
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float deep = saturate(i.depth / max(_WaterSize.y, .001));
                float3 body = _BodyColor.rgb * (1 - deep * _Appearance.y);
                float edge = 1 - smoothstep(_Appearance.x * .5, _Appearance.x, i.depth);
                float3 color = lerp(body, _SurfaceColor.rgb, edge * _SurfaceColor.a);
                return fixed4(color, _BodyColor.a);
            }
            ENDCG
        }
    }
}
