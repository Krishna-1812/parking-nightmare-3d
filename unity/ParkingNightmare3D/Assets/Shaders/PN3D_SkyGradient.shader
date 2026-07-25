// Three-stop vertical sky gradient, matching the district `sky` palette.
//
// Written as a skybox rather than painted onto a dome mesh so it sits at infinity: a
// dome has to be scaled past the far plane and still parallaxes against distant scenery.
// Hand-authored ShaderLab rather than Shader Graph so it is reviewable in a diff and is
// guaranteed to be in the build without touching Always Included Shaders.
Shader "PN3D/SkyGradient"
{
    Properties
    {
        _Top       ("Zenith",  Color) = (0.49, 0.77, 0.94, 1)
        _Mid       ("Mid",     Color) = (0.72, 0.89, 0.98, 1)
        _Horizon   ("Horizon", Color) = (1.00, 0.93, 0.79, 1)
        _MidHeight ("Mid height", Range(0.02, 0.9)) = 0.22
        _Exponent  ("Falloff",     Range(0.2, 4.0)) = 1.1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _Top, _Mid, _Horizon;
            half  _MidHeight, _Exponent;

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.dirOS = IN.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float h = saturate(normalize(IN.dirOS).y);
                // below the mid height, blend horizon -> mid; above it, mid -> zenith
                float lower = saturate(h / _MidHeight);
                float upper = saturate((h - _MidHeight) / max(1e-4, 1.0 - _MidHeight));
                half3 low  = lerp(_Horizon.rgb, _Mid.rgb, pow(lower, _Exponent));
                half3 col  = lerp(low, _Top.rgb, pow(upper, _Exponent));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
