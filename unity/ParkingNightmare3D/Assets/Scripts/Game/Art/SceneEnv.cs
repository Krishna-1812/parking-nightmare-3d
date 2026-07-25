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

        public static Light Build(District d, Vector3 worldCentre, Transform parent = null)
        {
            ApplySky(d);
            ApplyFog(d);
            ApplyAmbient(d);
            BuildHorizon(d, worldCentre, parent);
            return BuildSun(d, parent);
        }

        static void ApplySky(District d)
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
            m.SetFloat("_MidHeight", 0.24f);
            m.SetFloat("_Exponent", 1.15f);
            RenderSettings.skybox = m;
        }

        static void ApplyFog(District d)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = d.Fog;
            RenderSettings.fogStartDistance = 40f;      // src/n3_d.js:660
            RenderSettings.fogEndDistance = d.FogFar;
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
            l.shadowBias = 0.03f;
            l.shadowNormalBias = 0.35f;

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
            m.SetTextureScale("_BaseMap", new Vector2(3f, 1f));
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
