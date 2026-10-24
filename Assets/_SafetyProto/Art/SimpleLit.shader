// Lambert stand-in for the Built-in Standard shader on handheld props and PPE. Standard samples
// up to five textures and evaluates a full PBR BRDF per fragment; on the Quest GPU the scene is
// per-pixel bound, and these objects only receive baked light through light probes anyway.
// Keeps Standard's property names (_Color, _MainTex, _BumpMap, _Metallic, _Glossiness) so swapping
// the shader keeps the material's values.
//
// Lambert alone renders a metal as matte paint in its albedo colour. Metals opt into Reflections:
// one reflection-probe sample at Standard's roughness mip, tinted by albedo, with the diffuse
// scaled down by metallic the way Standard does.
Shader "SafetyProto/SimpleLit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        [Toggle(_SIMPLELIT_REFLECTIONS)] _Reflections ("Reflections (metals)", Float) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150

        CGPROGRAM
        #pragma surface surf Lambert noforwardadd nolppv nodynlightmap nodirlightmap nometa
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _SIMPLELIT_REFLECTIONS
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex;
        fixed4 _Color;
#ifdef _NORMALMAP
        sampler2D _BumpMap;
#endif
#ifdef _SIMPLELIT_REFLECTIONS
        half _Metallic;
        half _Glossiness;
#endif

        struct Input
        {
            float2 uv_MainTex;
#ifdef _SIMPLELIT_REFLECTIONS
            float3 worldRefl;
            INTERNAL_DATA
#endif
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
#ifdef _NORMALMAP
            // Albedo UVs, as Standard samples its normal map.
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
#endif
#ifdef _SIMPLELIT_REFLECTIONS
            half roughness = 1 - _Glossiness;
            half mip = roughness * (1.7 - 0.7 * roughness) * UNITY_SPECCUBE_LOD_STEPS;
            half4 probe = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, WorldReflectionVector(IN, o.Normal), mip);
            half3 specColor = lerp(unity_ColorSpaceDielectricSpec.rgb, c.rgb, _Metallic);
            o.Albedo = c.rgb * OneMinusReflectivityFromMetallic(_Metallic);
            o.Emission = DecodeHDR(probe, unity_SpecCube0_HDR) * specColor;
#endif
        }
        ENDCG
    }

    Fallback "Mobile/VertexLit"
}
