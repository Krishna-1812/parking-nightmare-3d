// Unlit alpha-blended silhouette for the horizon ring, drawn on the inside of a cylinder.
//
// A dedicated shader rather than URP/Unlit with a transparent surface type because this
// needs three things at once that the stock material would have to be poked into by code:
// front-face culling (we see the cylinder from inside), no depth write (it must never
// occlude scenery), and no fog (it IS the fog colour — fogging it again would grey it out
// twice and the ridge would vanish).
Shader "PN3D/Silhouette"
{
    Properties
    {
        _BaseMap   ("Silhouette", 2D)    = "white" {}
        _BaseColor ("Tint",       Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
