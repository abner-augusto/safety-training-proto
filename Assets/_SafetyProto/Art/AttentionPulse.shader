// Additive glow drawn over an interactive object (or a slot ghost) to call the participant's
// attention to it, on any base shader: it renders a copy of the mesh pushed out along its normals.
Shader "SafetyProto/AttentionPulse"
{
    Properties
    {
        _PulseColor ("Pulse Color", Color) = (1, 0.85, 0, 1)
        // Keep well under 3 Hz: faster flashing in a headset is a photosensitivity risk.
        _PulseSpeed ("Pulse Speed (Hz)", Range(0.1, 2)) = 1
        _Inflate ("Inflate (m)", Range(0, 0.02)) = 0.003
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
        // Fade written per renderer from C# through a MaterialPropertyBlock.
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _PulseColor;
            half _PulseSpeed;
            half _Inflate;
            half _RimPower;
            half _PulseAmount;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half rim : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz + worldNormal * _Inflate;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                o.rim = 1.0 - saturate(dot(viewDir, worldNormal));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half wave = 0.5 - 0.5 * cos(_Time.y * _PulseSpeed * 6.2831853);
                half glow = (0.3 + 0.7 * pow(i.rim, _RimPower)) * (0.25 + 0.75 * wave) * _PulseAmount;
                return fixed4(_PulseColor.rgb * glow, 1.0);
            }
            ENDCG
        }
    }
}
