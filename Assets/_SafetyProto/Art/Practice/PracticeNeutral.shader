// Flat neutral surface for the practice area. Every scene light is baked and the area sits outside
// the lightmapped space, so this shader shades from a fixed direction instead of reading lights:
// cubes still read as 3D at no lighting cost. A checker in world XZ marks the walkable floor.
Shader "SafetyProto/PracticeNeutral"
{
    Properties
    {
        _Color ("Color", Color) = (0.78, 0.78, 0.76, 1)
        _Color2 ("Checker Color", Color) = (0.62, 0.62, 0.6, 1)
        // 0 draws a solid colour.
        _CheckerSize ("Checker Size (m)", Float) = 0
        _LightDir ("Light Direction", Vector) = (0.35, 0.85, 0.4, 0)
        _Ambient ("Ambient", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _Color2;
            float _CheckerSize;
            float4 _LightDir;
            half _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = _Color;
                if (_CheckerSize > 0)
                {
                    float2 cell = floor(i.worldPos.xz / _CheckerSize);
                    col = lerp(_Color, _Color2, fmod(abs(cell.x + cell.y), 2.0));
                }
                half lambert = saturate(dot(normalize(i.worldNormal), normalize(_LightDir.xyz)));
                return fixed4(col.rgb * (_Ambient + (1.0 - _Ambient) * lambert), 1.0);
            }
            ENDCG
        }
    }
}
