// The ground.
//
// URP/Lit cannot do what this surface needs, which is why it is hand-written rather than
// a material tweak. Two things are missing from the stock shader:
//
// - **Vertex colour.** The field is six hundred metres across and every square metre of
//   it was the same green. Real ground varies at a scale of tens of metres — dry rises,
//   lush hollows, worn dirt — and that variation has to be smooth, so it belongs on the
//   mesh, interpolated across triangles. Quantising it into submeshes (the trick that
//   fixed the tree canopies) would put visible polygon edges through the lawn, because
//   the terrain grid is three and a half metres across near the camera.
//
// - **A second sampling rate.** One texture at one tiling rate always reads as a repeat
//   once the surface is big enough. Sampling the same map again four times larger and
//   modulating the first by the second breaks the repeat completely for one extra fetch:
//   the eye finds the pattern by spotting a feature twice, and no feature now recurs at
//   the same brightness.
//
// The alternative was URP/Lit's detail map, which does the second half of this. It was
// rejected because _DETAIL_MULX2 is a shader keyword and every material in this project
// is born at runtime — the same reason _EMISSION got stripped out of the first Android
// build and the brake lights did not light. A shader with no keywords cannot be stripped
// down to something that renders differently on the phone than it does here.
Shader "PN3D/Ground"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Grass", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _MacroScale ("Macro tiling (x base)", Range(0.05, 0.6)) = 0.21
        _MacroDepth ("Macro strength", Range(0, 1)) = 0.62
        // Dry and lush multiply the grass map rather than replacing it, so they are
        // written as gains about one, not as colours. Their midpoint has to land near
        // white or the whole field goes dark.
        _DryColor   ("Dry gain",  Color) = (1.22, 1.12, 0.80, 1)
        _LushColor  ("Lush gain", Color) = (0.78, 0.94, 0.66, 1)
        _WornColor  ("Worn earth", Color) = (0.44, 0.38, 0.28, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor, _DryColor, _LushColor, _WornColor;
            half   _MacroScale, _MacroDepth, _Smoothness;
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
            // Without this the cloud shadows land on every surface in the world except
            // the six hundred metres of field they are most visible on.
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 color      : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
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
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                o.color = IN.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half3 detail = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                // The same map, about five times larger and offset off the lattice so the
                // two samples never line up. Only its brightness is used: taken per
                // channel it would tint the whole field green a second time, and what is
                // wanted here is light and shade at a scale the tile cannot hold.
                half3 macro = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                                               IN.uv * _MacroScale + half2(0.37, 0.61)).rgb;
                half  mv = dot(macro, half3(0.33, 0.50, 0.17));
                half3 albedo = detail * lerp(1.0h, mv * 2.0h, _MacroDepth);

                // r: how lush this part of the field is. g: how worn it is.
                albedo *= lerp(_DryColor.rgb, _LushColor.rgb, IN.color.r);
                albedo = lerp(albedo, _WornColor.rgb * (0.6 + detail.g), IN.color.g);
                albedo *= _BaseColor.rgb;

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = albedo;
                sd.metallic = 0;
                sd.smoothness = _Smoothness;
                sd.occlusion = 1;
                sd.alpha = 1;
                sd.normalTS = half3(0, 0, 1);

                InputData id = (InputData)0;
                id.positionWS = IN.positionWS;
                id.normalWS = normalize(IN.normalWS);
                id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                id.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                id.fogCoord = IN.fogFactor;
                id.bakedGI = SampleSH(id.normalWS);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                id.shadowMask = half4(1, 1, 1, 1);

                half4 col = UniversalFragmentPBR(id, sd);
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }

        // The ground has to be in the depth prepass or screen-space ambient occlusion has
        // nothing to occlude against — the contact darkening under every kerb, wheel and
        // fence post is read out of this buffer.
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

            // URP stores _CameraNormalsTexture as a raw world-space normal, not remapped
            // into 0..1 — writing an encoded one here would give the ambient occlusion a
            // hemisphere of normals all pointing up and away.
            half4 frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
