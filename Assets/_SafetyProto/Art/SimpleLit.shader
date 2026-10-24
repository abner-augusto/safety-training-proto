// Lambert stand-in for the Built-in Standard shader on handheld props and PPE. Standard samples
// up to five textures and evaluates a full PBR BRDF per fragment; on the Quest GPU the scene is
// per-pixel bound, and these objects only receive baked light through light probes anyway.
// Keeps Standard's property names (_Color, _MainTex, _BumpMap) so swapping the shader keeps the
// material's values.
Shader "SafetyProto/SimpleLit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150

        CGPROGRAM
        #pragma surface surf Lambert noforwardadd nolppv nodynlightmap nodirlightmap nometa
        #pragma shader_feature_local _NORMALMAP

        sampler2D _MainTex;
        fixed4 _Color;
#ifdef _NORMALMAP
        sampler2D _BumpMap;
#endif

        struct Input
        {
            float2 uv_MainTex;
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
        }
        ENDCG
    }

    Fallback "Mobile/VertexLit"
}
