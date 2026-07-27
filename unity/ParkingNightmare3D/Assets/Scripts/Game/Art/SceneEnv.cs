using UnityEngine;
using UnityEngine.Rendering;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Sky, sun, ambient, fog and the horizon ring — everything that makes the world feel
    /// like it is somewhere rather than floating in a clear colour.
    ///
    /// Driven entirely by the <see cref="District"/> palette, so all six districts are a
    /// data swap. Mission 1 is district 0.
    /// </summary>
    public static class SceneEnv
    {
        /// <summary>Radius of the horizon ring. Comfortably past any district's fogFar.</summary>
        public const float HorizonRadius = 640f;

        /// <summary>
        /// Order matters here and it is not the obvious one. The sky needs the sun's
        /// direction to place the disc and light the clouds, so the sun is built first;
        /// the ambient probe is baked from the sky, so it comes after; and the horizon
        /// ring is geometry, so it goes last and occludes the sun when the sun is low.
        /// </summary>
        public static Light Build(District d, Vector3 worldCentre, Transform parent = null)
        {
            ApplyFog(d);
            var sun = BuildSun(d, parent);
            ApplySky(d, -sun.transform.forward);
            ApplyAmbient(d);
            BuildHorizon(d, worldCentre, parent);
            return sun;
        }

        static void ApplySky(District d, Vector3 toSun)
        {
            var sh = Shader.Find("PN3D/SkyGradient");
            if (sh == null)
            {
                // no shader means no sky; a flat camera clear is a visible regression, so
                // say so rather than shipping a blue rectangle nobody can explain
                Debug.LogError("[PN3D] PN3D/SkyGradient not found — sky will fall back to the camera clear colour");
                return;
            }
            var m = new Material(sh) { name = "PN3D_Sky" };
            m.SetColor("_Top", d.SkyTop);
            m.SetColor("_Mid", d.SkyMid);
            m.SetColor("_Horizon", d.SkyHorizon);
            // The gradient was authored for a camera that looks at the sky. This one does
            // not: the chase view puts the top of the frame around nine degrees up, so at
            // the old mid height of 0.24 the zenith blue was never on screen and every
            // shot had a washed-out pale band for a sky. Pulling the mid stop down to
            // about five degrees puts the warm haze where haze actually is and lets the
            // blue start where the player can see it.
            m.SetFloat("_MidHeight", 0.085f);
            m.SetFloat("_Exponent", 0.72f);

            m.SetTexture("_Clouds", ProcTex.CloudDeck());
            m.SetVector("_SunDir", toSun);

            // A cloud is not white and its shaded side is not grey — both are the sky it
            // sits in, one lit by the sun and one lit only by the rest of the sky. Taking
            // them from the district palette is what keeps the deck from looking pasted on
            // in the dusk and night districts, where a white cloud would be the brightest
            // thing in a frame lit by streetlamps.
            var lit = Color.Lerp(Color.white, d.SunColor, 0.35f)
                    * (d.Night ? 0.30f : 1.0f);
            var dark = Color.Lerp(d.SkyMid, new Color(0.34f, 0.39f, 0.50f), 0.74f)
                     * (d.Night ? 0.42f : 0.95f);
            m.SetColor("_CloudLit", lit);
            m.SetColor("_CloudDark", dark);
            m.SetColor("_SunTint", d.SunColor);
            m.SetFloat("_CloudAmount", d.Night ? 0.55f : 1f);
            m.SetFloat("_CloudScale", 0.20f);
            // The sun disc is drawn at roughly a degree and a half rather than the half a
            // degree it really subtends. At the real size it is two pixels on a phone and
            // reads as a stuck sub-pixel; the aureole around it is what the eye actually
            // uses to locate the sun anyway.
            m.SetFloat("_SunSize", 0.99955f);
            m.SetFloat("_SunGlow", d.Night ? 900f : 150f);
            m.SetVector("_Drift", new Vector4(0.00085f, 0.00040f, 0.00036f, 0.00018f));

            RenderSettings.skybox = m;
        }

        static void ApplyFog(District d)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = d.Fog;

            // The reference fogs from 40 m to the district's fogFar (src/n3_d.js:660), and
            // that is deliberately more aggressive than air: it exists to hide the end of
            // the web build's world, which is a short strip. This world reaches the horizon
            // ring at 640 m and has hills and a treeline out there worth seeing, so the
            // same curve just turns the middle distance milky — at 150 m the old numbers
            // were already 42% fog, which is why every house past the second lot looked
            // bleached.
            //
            // Pushed out, not switched off. The ring still lands at essentially full fog,
            // so the world still ends in haze rather than at an edge.
            RenderSettings.fogStartDistance = 85f;
            RenderSettings.fogEndDistance = Mathf.Max(d.FogFar, 520f);
        }

        static void ApplyAmbient(District d)
        {
            // Three.js HemisphereLight has no Unity equivalent as a light, but URP's
            // trilight ambient is the same idea: sky colour from above, ground bounce from
            // below. Intensity folds into the colours because ambient has no multiplier.
            //
            // The equator band is what lights every vertical surface facing away from the
            // sun — the shaded flank of the car, the far side of every house. Taking it
            // straight from the sky colour makes those surfaces read as grey-blue no matter
            // what they are painted, which had the orange hatch looking like bare primer.
            // Mixing in the ground bounce and keeping the band bright fixes that, and it is
            // what a real hemisphere light does anyway.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = d.HemiSky * d.HemiIntensity * 0.78f;
            RenderSettings.ambientEquatorColor =
                Color.Lerp(d.HemiSky, d.HemiGround, 0.45f) * d.HemiIntensity * 0.80f;
            RenderSettings.ambientGroundColor = d.HemiGround * d.HemiIntensity * 0.58f;
            RenderSettings.reflectionIntensity = 0.9f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;

            // Without this the skybox reflection probe is never generated, and everything
            // smooth — car paint, glass, the wheel hubs — renders as a black hole instead
            // of catching the sky. It does not happen on its own when the skybox material
            // is assigned from script, and it certainly does not happen in batch mode.
            DynamicGI.UpdateEnvironment();
        }

        static Light BuildSun(District d, Transform parent)
        {
            var go = new GameObject("PN3D_Sun");
            if (parent != null) go.transform.SetParent(parent, false);

            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = d.SunColor;
            // Three.js DirectionalLight intensity is not URP lux; district 0 authors 2.4,
            // which reads as blown out here. Scaled to keep the same relative ordering
            // between districts without clipping the highlights.
            l.intensity = d.SunIntensity * 0.72f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.78f;
            // Bias is NOT set here. URP reads it from the render pipeline asset unless the
            // light's UniversalAdditionalLightData turns usePipelineSettings off, so the
            // shadowBias and shadowNormalBias this used to assign were dead code — the
            // values actually in force were the 1.0 / 1.0 in Mobile_RPAsset, which is what
            // was detaching every shadow from the thing casting it. Tune them there.

            // The web build positions the sun at (x, y, z) in its own axes; the direction
            // it points is from there toward the origin, mapped through the same handedness
            // flip the rest of the world uses (WorldBuilder.ToWorld).
            var pos = new Vector3(d.SunDir.x, d.SunDir.y, -d.SunDir.z);
            go.transform.rotation = Quaternion.LookRotation(-pos.normalized, Vector3.up);

            RenderSettings.sun = l;
            return l;
        }

        static void BuildHorizon(District d, Vector3 centre, Transform parent)
        {
            var sh = Shader.Find("PN3D/Silhouette");
            if (sh == null) return;

            var go = new GameObject("PN3D_Horizon");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(centre.x, 60f, centre.z);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = OpenCylinder(HorizonRadius, 150f, 64);

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var m = new Material(sh) { name = "PN3D_Horizon" };
            m.SetTexture("_BaseMap", ProcTex.HillsSkyline(d.Fog));
            // Once round, not three times. The skyline carries landmarks now, and a
            // landmark you can see three copies of at once is worse than none.
            m.SetTextureScale("_BaseMap", Vector2.one);
            mr.sharedMaterial = m;
        }

        /// <summary>Open-ended cylinder with UVs running once round per horizontal repeat.</summary>
        static Mesh OpenCylinder(float radius, float height, int segments)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float a = u * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * radius, z = Mathf.Sin(a) * radius;
                verts[i * 2] = new Vector3(x, -height * 0.5f, z);
                verts[i * 2 + 1] = new Vector3(x, height * 0.5f, z);
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int b = i * 2, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }

            var mesh = new Mesh { name = "horizonRing" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
