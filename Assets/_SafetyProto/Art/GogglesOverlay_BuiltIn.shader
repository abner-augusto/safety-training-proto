Shader "SafetyProto/GogglesOverlay_BuiltIn"
{
    Properties
    {
        _SmudgeTex ("Smudge (A)", 2D) = "white" {}
        _SmudgeTiling ("Smudge Tiling", Float) = 1.0
        _SmudgeStrength ("Smudge Strength", Range(0,1)) = 0.08

        _VignetteStrength ("Vignette Strength", Range(0,1)) = 0.10
        _VignetteSoftness ("Vignette Softness", Range(0.01,4)) = 1.4

        _HighlightStrength ("Highlight Strength", Range(0,1)) = 0.04
        _HighlightWidth ("Highlight Width", Range(0.001,0.2)) = 0.03
        _HighlightY ("Highlight Y", Range(0,1)) = 0.88
        _HighlightSlope ("Highlight Slope", Range(-2,2)) = -0.4

        _GlobalAlpha ("Global Alpha", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SmudgeTex;
            float4 _SmudgeTex_ST;

            float _SmudgeTiling;
            float _SmudgeStrength;

            float _VignetteStrength;
            float _VignetteSoftness;

            float _HighlightStrength;
            float _HighlightWidth;
            float _HighlightY;
            float _HighlightSlope;

            float _GlobalAlpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Vignette(float2 uv)
            {
                float2 p = uv * 2.0 - 1.0;
                float r2 = dot(p, p);
                float v = saturate(1.0 - r2);
                v = pow(v, _VignetteSoftness);
                return 1.0 - v;
            }

            float Highlight(float2 uv)
            {
                float yLine = _HighlightY + (uv.x - 0.5) * _HighlightSlope;
                float d = abs(uv.y - yLine);
                float h = saturate(1.0 - d / max(1e-5, _HighlightWidth));
                return h;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                float2 suv = uv * _SmudgeTiling;
                float aSmudge = tex2D(_SmudgeTex, suv).a;

                float v = Vignette(uv);
                float h = Highlight(uv);

                float a = 0.0;
                a += v * _VignetteStrength;
                a += aSmudge * _SmudgeStrength;
                a += h * _HighlightStrength;

                a *= _GlobalAlpha;
                a = saturate(a);

                return fixed4(0,0,0,a);
            }
            ENDCG
        }
    }
}
