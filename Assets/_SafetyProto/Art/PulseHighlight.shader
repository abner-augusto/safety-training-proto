Shader "SafetyProto/PulseHighlight"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        [Header(Pulse)]
        _PulseColor ("Pulse Color", Color) = (1, 0.85, 0, 1)
        // Keep well under 3 Hz: faster flashing in a headset is a photosensitivity risk.
        _PulseSpeed ("Pulse Speed (Hz)", Range(0.1, 2)) = 1
        _PulseEmission ("Pulse Emission", Range(0, 2)) = 0.6
        // Gate written per renderer from C# through a MaterialPropertyBlock; 0 renders as plain Standard.
        _PulseAmount ("Pulse Amount", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        fixed4 _PulseColor;
        half _PulseSpeed;
        half _PulseEmission;
        half _PulseAmount;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            half wave = 0.5 - 0.5 * cos(_Time.y * _PulseSpeed * 6.2831853);
            half t = wave * _PulseAmount;

            o.Albedo = lerp(c.rgb, _PulseColor.rgb, t);
            // A metallic surface shows albedo only as a specular tint, so yellow would read as a
            // dim gold sheen. Dropping metallic and adding emission is what makes it read yellow.
            o.Metallic = lerp(_Metallic, 0, t);
            o.Smoothness = _Glossiness;
            o.Emission = _PulseColor.rgb * (t * _PulseEmission);
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
