// Glass-like column marking where to walk back to: bright rim, clear core, fading upward,
// pulsing slowly. Drawn after PlayspaceDim and without depth writes, so the dimming never hides it.
Shader "SafetyProto/PlayspaceBeacon"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.75, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.5
        _CoreAlpha ("Core Alpha", Range(0, 1)) = 0.08
        // Keep well under 3 Hz: faster flashing in a headset is a photosensitivity risk.
        _PulseSpeed ("Pulse Speed (Hz)", Range(0.1, 2)) = 0.6
        _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _RimPower;
            half _CoreAlpha;
            half _PulseSpeed;
            half _Alpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float height : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                // Unity's cylinder spans y = -1..1 in object space.
                o.height = saturate(v.vertex.y * 0.5 + 0.5);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half facing = abs(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                half rim = pow(1.0 - facing, _RimPower);
                half pulse = 0.75 + 0.25 * sin(_Time.y * _PulseSpeed * 6.2831853);
                half fade = 1.0 - i.height * i.height;
                half a = saturate(_CoreAlpha + rim) * fade * pulse * _Alpha;
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
