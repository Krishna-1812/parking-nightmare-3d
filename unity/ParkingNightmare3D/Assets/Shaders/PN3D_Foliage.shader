// Leaves: they move, and light comes through them.
//
// Two things URP/Lit will not do, and between them they are most of the difference
// between foliage and painted geometry.
//
// **Wind.** Nothing in this world moved except the cars. A still tree is the strongest
// possible signal that a scene is a diorama, and the fix is four lines in the vertex
// stage — no bones, no simulation, no per-frame CPU work at all.
//
// The phase comes from the OBJECT's world position, not the vertex's. That is what makes
// a crown sway as one mass while the tree ten metres away is on a different beat; taking
// it per vertex would shear each crown apart from the inside. A small per-vertex term is
// added on top for flutter, and the whole thing is weighted by height above the tree's
// base, so the bottom of a shrub stays planted.
//
// **Translucency.** A leaf is thin. Stand so a tree is between you and the sun and the
// canopy lights up — that is light coming THROUGH the leaf, and a purely reflective
// shading model cannot produce it at any roughness. One back-scatter term buys it, and
// it is the single thing that stops a crown reading as a painted lump on a bright day.
//
// The ShadowCaster pass applies exactly the same displacement as the forward pass. It
// has to: a shadow computed from the undisplaced mesh detaches from a swaying tree and
// the whole effect reads as a bug rather than as wind.
Shader "PN3D/Foliage"
{
    Properties
    {
        [MainColor] _BaseColor ("Leaf", Color) = (0.24, 0.44, 0.20, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.06

        _TransColor ("Transmission", Color) = (0.52, 0.78, 0.26, 1)
        _TransPower ("Transmission falloff", Range(1, 12)) = 3.5
        _TransScale ("Transmission strength", Range(0, 3)) = 1.15

        _WindAmp   ("Sway amplitude (m)", Range(0, 1)) = 0.16
        _WindSpeed ("Sway speed", Range(0, 4)) = 0.85
        _WindDir   ("Wind direction (xz)", Vector) = (0.86, 0.51, 0, 0)
        _SwayFromY ("Sway onset, 1/metres", Range(0.05, 2)) = 0.34
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColor, _TransColor;
            half   _Smoothness, _TransPower, _TransScale;
            half   _WindAmp, _WindSpeed, _SwayFromY;
            float4 _WindDir;
        CBUFFER_END

        // Object-space displacement. Deterministic in time and position, so the forward
        // pass and the shadow pass agree to the last bit.
        float3 Sway(float3 posOS)
        {
            float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);

            // one beat per tree, from where the tree stands
            float t = _Time.y * _WindSpeed + origin.x * 0.13 + origin.z * 0.11;
            // ...and a little flutter that varies across the crown
            float f = t + posOS.x * 0.55 + posOS.z * 0.47;

            float s = sin(f) * 0.70 + sin(f * 2.31 + 1.7) * 0.30;
            // gusts: the wind is not a metronome, it comes and goes
            float gust = 0.45 + 0.55 * (sin(t * 0.37) * 0.5 + 0.5);

            float stiff = saturate(posOS.y * _SwayFromY);
            float amp = _WindAmp * stiff * stiff * gust;

            // A branch bending over traces an arc, so it drops as it swings out. Without
            // the dip the crown looks like it is sliding rather than bending.
            return posOS + float3(_WindDir.x, 0, _WindDir.y) * (s * amp)
                         + float3(0, -abs(s) * amp * 0.13, 0);
        }
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

                VertexPositionInputs p = GetVertexPositionInputs(Sway(IN.positionOS.xyz));
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

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = _BaseColor.rgb;
                sd.metallic = 0;
                sd.smoothness = _Smoothness;
                sd.occlusion = 1;
                sd.alpha = 1;
                sd.normalTS = half3(0, 0, 1);

                InputData id = (InputData)0;
                id.positionWS = IN.positionWS;
                id.normalWS = n;
                id.viewDirectionWS = v;
                id.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                id.fogCoord = IN.fogFactor;
                id.bakedGI = SampleSH(n);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                id.shadowMask = half4(1, 1, 1, 1);

                half4 col = UniversalFragmentPBR(id, sd);

                // Light through the leaf. Strongest when the sun is directly behind the
                // canopy, and gated on the light's own shadow so a crown in the shade of
                // the house next door does not glow.
                Light main = GetMainLight(id.shadowCoord);
                half back = saturate(dot(-main.direction, v));
                half wrap = saturate(dot(n, main.direction) * 0.5 + 0.5);
                col.rgb += _TransColor.rgb * main.color
                         * (pow(back, _TransPower) * _TransScale * wrap * main.shadowAttenuation);

                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
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

                float3 positionWS = TransformObjectToWorld(Sway(IN.positionOS.xyz));
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
                o.positionCS = TransformObjectToHClip(Sway(IN.positionOS.xyz));
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
                o.positionCS = TransformObjectToHClip(Sway(IN.positionOS.xyz));
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
