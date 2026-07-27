// Skin.
//
// URP/Lit renders a person as painted plastic, and it is not a texture problem — it is
// the lighting model. Two things are wrong with a standard dielectric on skin, and both
// are about where the light goes after it hits:
//
// **The terminator.** A Lambert surface goes dark exactly where the surface turns away
// from the light. Skin does not: light enters, scatters a few millimetres through
// capillary-rich tissue, and comes back out further round the curve. So the shading rolls
// off softly and the roll-off is RED, because the shallow layers absorb blue and green
// long before red. Every "uncanny" CG face has a hard grey terminator on the cheek. The
// fix is a wrapped N.L plus a warm term concentrated where N.L crosses zero.
//
// **Thin parts glow.** An ear or a nostril against the sun is lit from behind — light
// passes right through. One transmission term does it, and it is worth having because the
// ears and the nose are exactly the silhouette the eye uses to read a head.
//
// The specular is broad and weak on purpose. Skin's sheen is an oil layer, very rough and
// very dim; a tight bright highlight is what makes CG faces look wet.
//
// This has to compute its own lighting rather than call UniversalFragmentPBR, because
// wrapped diffuse is not something the standard BRDF can be talked into from outside.
Shader "PN3D/Skin"
{
    Properties
    {
        [MainColor] _BaseColor ("Skin", Color) = (0.88, 0.70, 0.56, 1)

        _Wrap      ("Diffuse wrap", Range(0, 1)) = 0.42
        _SSSColor  ("Subsurface", Color) = (0.72, 0.22, 0.16, 1)
        _SSSScale  ("Subsurface strength", Range(0, 2)) = 0.55
        _TransScale("Transmission strength", Range(0, 2)) = 0.35
        _SpecPower ("Sheen tightness", Range(4, 128)) = 26
        _SpecScale ("Sheen strength", Range(0, 1)) = 0.10
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _SSSColor;
            half  _Wrap, _SSSScale, _TransScale, _SpecPower, _SpecScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 n = normalize(IN.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                half3 albedo = _BaseColor.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                // this overload is the one that applies the light cookie, so the cloud
                // shadows crossing the street also cross the people standing in it
                Light main = GetMainLight(shadowCoord, IN.positionWS, half4(1, 1, 1, 1));
                half atten = main.shadowAttenuation * main.distanceAttenuation;

                half ndl = dot(n, main.direction);

                // Wrapped diffuse: the terminator is pushed round the curve instead of
                // falling off a cliff at ninety degrees.
                half wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));

                // Blood. Concentrated in the band where the surface is turning away, which
                // is exactly where light that entered the lit side comes back out.
                half band = pow(saturate(1.0 - abs(ndl)), 2.0) * saturate(ndl + 0.72);

                // Light straight through the thin bits — ears, nostrils, fingers.
                half back = pow(saturate(dot(-main.direction, v)), 3.0)
                          * saturate(0.55 - ndl);

                half3 lit = albedo * wrapped
                          + albedo * _SSSColor.rgb * (band * _SSSScale)
                          + albedo * _SSSColor.rgb * (back * _TransScale);
                lit *= main.color * atten;

                // A rough oil sheen, gated on the surface actually facing the light so it
                // cannot appear on the shadow side.
                float3 h = normalize(main.direction + v);
                half spec = pow(saturate(dot(n, h)), _SpecPower) * _SpecScale
                          * saturate(ndl * 4.0);
                lit += main.color * atten * spec;

                half3 ambient = SampleSH(n) * albedo;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao =
                        GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionCS));
                    ambient *= ao.indirectAmbientOcclusion;
                #endif

                half3 col = lit + ambient;
                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 cs = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target { return half4(normalize(IN.normalWS), 0); }
            ENDHLSL
        }
    }

    Fallback Off
}
