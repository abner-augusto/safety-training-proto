// Translucent "where it goes" silhouette for the practice slots and sockets. Unlit for the same
// reason as PracticeNeutral; the edges are brighter so the shape reads against the floor.
// _Color is what ScaffoldPieceInstaller and PracticeSnapSlot tint.
Shader "SafetyProto/PracticeGhost"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.25)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)
            half _RimPower;

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                o.rim = 1.0 - saturate(dot(normalize(_WorldSpaceCameraPos.xyz - worldPos), worldNormal));
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                half edge = 0.6 + 0.4 * pow(i.rim, _RimPower);
                return fixed4(c.rgb * (0.7 + 0.5 * edge), saturate(c.a * (0.6 + 0.9 * edge)));
            }
            ENDCG
        }
    }
}
