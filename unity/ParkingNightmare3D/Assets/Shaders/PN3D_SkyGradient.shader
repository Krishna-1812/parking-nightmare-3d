// The sky: a three-stop vertical gradient, a cloud deck, a sun and the haze that ties
// them to the ground.
//
// Written as a skybox rather than painted onto a dome mesh so it sits at infinity: a
// dome has to be scaled past the far plane and still parallaxes against distant scenery.
// Hand-authored ShaderLab rather than Shader Graph so it is reviewable in a diff and is
// guaranteed to be in the build without touching Always Included Shaders.
//
// The clouds are projected onto a flat ceiling — uv = dir.xz / dir.y — rather than onto
// a sphere. That is not a shortcut, it is the correct projection for a cloud layer at a
// fixed altitude, and it comes with the thing a spherical mapping has to fake: the deck
// compresses and crowds toward the horizon on its own, because that is where a flat
// ceiling is seen edge-on. One texture fetch per layer buys all of it, which matters on
// a phone where the sky is a large part of every frame's fill.
Shader "PN3D/SkyGradient"
{
    Properties
    {
        _Top       ("Zenith",  Color) = (0.49, 0.77, 0.94, 1)
        _Mid       ("Mid",     Color) = (0.72, 0.89, 0.98, 1)
        _Horizon   ("Horizon", Color) = (1.00, 0.93, 0.79, 1)
        _MidHeight ("Mid height", Range(0.02, 0.9)) = 0.22
        _Exponent  ("Falloff",     Range(0.2, 4.0)) = 1.1

        _Clouds    ("Cloud deck", 2D) = "black" {}
        _CloudLit  ("Cloud lit",    Color) = (1.00, 0.99, 0.97, 1)
        _CloudDark ("Cloud shaded", Color) = (0.62, 0.66, 0.74, 1)
        _CloudAmount ("Coverage",   Range(0, 1))    = 1.0
        _CloudScale  ("Deck scale", Range(0.02, 1.0)) = 0.20
        _Drift       ("Drift (cumulus xy, cirrus zw)", Vector) = (0.0009, 0.0004, 0.0004, 0.0002)

        _SunDir  ("Sun direction", Vector) = (0.4, 0.7, 0.5, 0)
        _SunTint ("Sun tint",      Color)  = (1.0, 0.95, 0.85, 1)
        _SunSize ("Disc cos",      Range(0.9990, 0.99995)) = 0.99955
        _SunGlow ("Aureole",       Range(4, 600)) = 150
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

            TEXTURE2D(_Clouds);
            SAMPLER(sampler_Clouds);

            half4  _Top, _Mid, _Horizon;
            half   _MidHeight, _Exponent;
            half4  _CloudLit, _CloudDark, _SunTint;
            half   _CloudAmount, _CloudScale, _SunSize, _SunGlow;
            float4 _Drift, _SunDir;

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
                float3 d = normalize(IN.dirOS);
                float h = saturate(d.y);

                // below the mid height, blend horizon -> mid; above it, mid -> zenith
                float lower = saturate(h / _MidHeight);
                float upper = saturate((h - _MidHeight) / max(1e-4, 1.0 - _MidHeight));
                half3 low  = lerp(_Horizon.rgb, _Mid.rgb, pow(lower, _Exponent));
                half3 col  = lerp(low, _Top.rgb, pow(upper, _Exponent));

                float sd = saturate(dot(d, _SunDir.xyz));

                // The projection, and the one place this deliberately lies.
                //
                // A true flat ceiling is dir.xz / dir.y, which sends the scale to zero at
                // the horizon. That is correct and it is useless here: the chase camera
                // sits six metres up looking slightly down, so the top of the frame is
                // about nine degrees of elevation and the ENTIRE visible sky is inside the
                // band a true projection compresses to a grey smear. Softening the divisor
                // keeps the perspective — the deck still crowds and shrinks toward the
                // horizon, by about three and a half to one — while leaving the clouds
                // legible in the only part of the sky this game ever shows.
                float2 base = d.xz / (max(d.y, 0.0) + 0.28);

                // Below the horizon there is no deck at all, and everything approaching it
                // washes into the horizon colour, because a cloud on the skyline is a
                // hundred kilometres of atmosphere away.
                float deck = smoothstep(-0.005, 0.045, d.y);
                float aerial = smoothstep(0.0, 0.30, d.y);

                UNITY_BRANCH
                if (deck > 0.001)
                {
                    // cirrus first: it is the higher layer, so cumulus composites over it
                    float2 uvW = base * (_CloudScale * 0.34) + _Time.y * _Drift.zw + 17.3;
                    half4  w = SAMPLE_TEXTURE2D(_Clouds, sampler_Clouds, uvW);
                    half3  wc = lerp(_Horizon.rgb, lerp(_CloudLit.rgb, _CloudLit.rgb * 1.06, w.b), aerial);
                    col = lerp(col, wc, saturate(w.g * _CloudAmount * deck * 0.62));

                    float2 uvC = base * _CloudScale + _Time.y * _Drift.xy;
                    half4  c = SAMPLE_TEXTURE2D(_Clouds, sampler_Clouds, uvC);
                    half3  cc = lerp(_CloudDark.rgb, _CloudLit.rgb, c.r);
                    // the silver lining: thin cloud near the sun scatters light forward,
                    // which is most of what makes a cloud read as lit rather than painted
                    cc += _SunTint.rgb * (pow(sd, 5.0) * 0.55 * c.r);
                    cc = lerp(_Horizon.rgb, cc, aerial);
                    col = lerp(col, cc, saturate(c.a * _CloudAmount * deck));
                }

                // Sun last, over the cloud, so a cloud in front of it does not eat the
                // glow — a real one does not either; it lights up instead.
                float disc  = smoothstep(_SunSize, _SunSize + 0.00022, sd);
                float aureole = pow(sd, _SunGlow) * 0.45 + pow(sd, 6.0) * 0.12;
                col += _SunTint.rgb * (disc * 9.0 + aureole);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
