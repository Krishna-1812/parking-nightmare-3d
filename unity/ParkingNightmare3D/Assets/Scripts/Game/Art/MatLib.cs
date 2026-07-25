using System.Collections.Generic;
using UnityEngine;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Material factory for the runtime-generated art.
    ///
    /// Materials are created in code rather than authored as assets because their maps are
    /// painted at load by <see cref="ProcTex"/> — an authored .mat could only reference a
    /// texture asset that does not exist. Everything is cached by key, so the world builder
    /// can ask for the same surface repeatedly and still get one draw-call batch.
    /// </summary>
    public static class MatLib
    {
        static readonly Dictionary<string, Material> Cache = new();

        static Shader _lit, _unlit;

        public static Shader Lit => _lit != null ? _lit
            : _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        public static Shader Unlit => _unlit != null ? _unlit
            : _unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        public static Material Get(string key, System.Func<Material> make)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            var made = make();
            made.name = key;
            Cache[key] = made;
            return made;
        }

        public static void Clear() => Cache.Clear();

        /// <summary>Plain painted surface, no map.</summary>
        public static Material Solid(Color c, float smoothness = 0.12f, float metallic = 0f)
            => Get($"solid{ColorUtility.ToHtmlStringRGBA(c)}_{smoothness:0.00}_{metallic:0.00}", () =>
            {
                var m = new Material(Lit);
                SetBase(m, c);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", metallic);
                return m;
            });

        /// <summary>Textured surface with an optional normal map and UV tiling.</summary>
        public static Material Textured(string key, Texture2D map, Color tint, Vector2 tiling,
                                        float smoothness = 0.1f, Texture2D normal = null,
                                        float normalScale = 1f)
            => Get(key, () =>
            {
                var m = new Material(Lit);
                SetBase(m, tint);
                m.SetTexture("_BaseMap", map);
                m.SetTextureScale("_BaseMap", tiling);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", 0f);
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.SetTextureScale("_BumpMap", tiling);
                    m.SetFloat("_BumpScale", normalScale);
                    m.EnableKeyword("_NORMALMAP");
                }
                return m;
            });

        /// <summary>Emissive bar or bulb. Bloom in the volume profile does the rest.</summary>
        public static Material Emissive(Color body, Color emission, float intensity)
            => Get($"emit{ColorUtility.ToHtmlStringRGB(body)}_{ColorUtility.ToHtmlStringRGB(emission)}_{intensity:0.00}", () =>
            {
                var m = new Material(Lit);
                SetBase(m, body);
                m.SetFloat("_Smoothness", 0.6f);
                m.SetColor("_EmissionColor", emission * intensity);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return m;
            });

        /// <summary>
        /// Car glass. Kept opaque and dark rather than alpha-blended: the reference reads
        /// as a tinted canopy from the outside, and an opaque canopy sorts correctly
        /// against the body panels at every camera angle without a transparent queue.
        /// </summary>
        public static Material Glass(Color tint) => Get("glass" + ColorUtility.ToHtmlStringRGB(tint), () =>
        {
            var m = new Material(Lit);
            SetBase(m, tint);
            m.SetFloat("_Smoothness", 0.94f);
            m.SetFloat("_Metallic", 0.1f);
            return m;
        });

        /// <summary>Unlit, used by the ground plane skirt and the distant backdrop.</summary>
        public static Material Flat(Color c) => Get("flat" + ColorUtility.ToHtmlStringRGBA(c), () =>
        {
            var m = new Material(Unlit);
            SetBase(m, c);
            return m;
        });

        static void SetBase(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}
