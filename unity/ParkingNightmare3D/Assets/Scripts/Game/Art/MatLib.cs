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

        public static Shader Lit => _lit != null ? _lit : _lit = Resolve("Universal Render Pipeline/Lit");

        public static Shader Unlit => _unlit != null ? _unlit : _unlit = Resolve("Universal Render Pipeline/Unlit");

        /// <summary>
        /// Find a shader, or fail with the reason rather than with a null.
        ///
        /// This exists because of how it failed the first time. A player build ships only
        /// the shaders something references, and "something" means a material ASSET —
        /// every material here is created at runtime, so from the build pipeline's point
        /// of view URP/Lit is unused and gets dropped. Shader.Find then returned null,
        /// `new Material(null)` threw from inside BuildGround, and the app ran happily at
        /// 60fps rendering nothing but the default skybox.
        ///
        /// The old code masked it further by falling back to Shader.Find("Standard"),
        /// which cannot exist in a URP project either, so the null propagated instead of
        /// the lookup failing where it went wrong. The fix is the Always Included Shaders
        /// list in ProjectSettings/GraphicsSettings.asset; this is the tripwire that says
        /// so out loud if anyone removes them.
        /// </summary>
        static Shader Resolve(string name)
        {
            var s = Shader.Find(name);
            if (s == null)
                throw new System.InvalidOperationException(
                    $"Shader '{name}' is not in this build. Every material in PN3D is created " +
                    "at runtime, so no material asset references it and the build stripped it. " +
                    "Add it to Always Included Shaders in ProjectSettings/GraphicsSettings.asset.");
            return s;
        }

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

        /// <summary>
        /// Automotive paint.
        ///
        /// The distinction that matters: <see cref="Textured"/> pins metallic at 0, which
        /// is correct for siding and asphalt and completely wrong for a car. A dielectric
        /// at metallic 0 reflects the environment at grazing angles only, so the body
        /// caught no sky at all and read as matte plastic — which is most of why the cars
        /// looked like bath toys.
        ///
        /// Real metallic paint is a dielectric base with aluminium flake under clear coat,
        /// and URP/Lit has one specular lobe to spend. Biasing metallic high and smoothness
        /// just short of a mirror buys the flake sparkle and the clear-coat sheen out of
        /// that single lobe. Reflections come from the skybox: SceneEnv already sets
        /// DefaultReflectionMode.Skybox at 0.9 intensity, and a curved hull sweeping the
        /// sky gradient is what sells painted metal.
        /// </summary>
        /// <remarks>
        /// Metallic is deliberately modest. The first attempt used 0.82, on the theory that
        /// "metallic paint" wants a metallic surface, and it came out wrong: a metal has no
        /// diffuse term, so the hull went from rusty orange to dark maroon and the colour
        /// survived only in the specular tint. Real coloured paint is a pigmented dielectric
        /// under clear coat — the hue lives in the diffuse, and the *gloss* is what reads as
        /// automotive. Low metallic with very high smoothness is the honest model of that,
        /// and it keeps the livery colours the missions actually author.
        /// </remarks>
        public static Material CarPaint(string key, Color tint, Texture2D map = null,
                                        float metallic = 0.25f, float smoothness = 0.84f)
            => Get(key, () =>
            {
                var m = new Material(Lit);
                SetBase(m, tint);
                if (map != null)
                {
                    m.SetTexture("_BaseMap", map);
                    m.SetTextureScale("_BaseMap", Vector2.one);
                }
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", smoothness);
                // Specular highlights and environment reflections are both on by default,
                // but say so: with metallic this high, a build that stripped either would
                // render the whole fleet flat black rather than merely dull.
                m.SetFloat("_SpecularHighlights", 1f);
                m.SetFloat("_EnvironmentReflections", 1f);
                return m;
            });

        static Shader _skin;

        /// <summary>
        /// Skin. Not <see cref="Solid"/> with a flesh colour — see the header of
        /// <c>PN3D_Skin.shader</c> for why that renders a person as painted plastic.
        /// </summary>
        public static Material Skin(Color tone, Texture2D map = null, Texture2D normal = null,
                                    string key = null)
            => Get("skin" + (key ?? ColorUtility.ToHtmlStringRGB(tone)), () =>
            {
                _skin = _skin != null ? _skin : Resolve("PN3D/Skin");
                var m = new Material(_skin);
                SetBase(m, tone);
                if (map != null) m.SetTexture("_BaseMap", map);
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.SetFloat("_BumpScale", 1f);
                }
                // The subsurface tint is the tone's own hue driven to blood red, so a
                // darker tone scatters a deeper red rather than turning pink.
                Color.RGBToHSV(tone, out float h, out float s, out float v);
                m.SetColor("_SSSColor", Color.HSVToRGB(Mathf.Repeat(h - 0.015f, 1f),
                                                       Mathf.Clamp01(s * 1.9f + 0.30f),
                                                       Mathf.Clamp01(v * 0.85f)));
                m.SetFloat("_Wrap", 0.42f);
                m.SetFloat("_SSSScale", 0.50f);
                m.SetFloat("_TransScale", 0.32f);
                m.SetFloat("_SpecPower", 26f);
                m.SetFloat("_SpecScale", 0.10f);
                return m;
            });

        /// <summary>
        /// Clothing. URP/Lit with a weave normal and almost no gloss — cotton scatters, it
        /// does not reflect, and a shirt with a specular highlight on it reads as vinyl.
        /// </summary>
        public static Material Cloth(Color tint)
            => Get("cloth" + ColorUtility.ToHtmlStringRGB(tint), () =>
            {
                var m = new Material(Lit);
                SetBase(m, tint);
                // No base map: the weave lives entirely in the relief. Handing the normal
                // map in as an albedo — which is what the first version of this did — makes
                // every shirt in the game the flat violet of an unpacked tangent normal.
                m.SetFloat("_Smoothness", 0.06f);
                m.SetFloat("_Metallic", 0f);
                m.SetTexture("_BumpMap", ProcTex.ClothNormal());
                m.SetTextureScale("_BumpMap", new Vector2(7f, 7f));
                m.SetFloat("_BumpScale", 0.55f);
                m.EnableKeyword("_NORMALMAP");
                return m;
            });

        static Shader _foliage;

        /// <summary>
        /// Leaves. Sways in the wind and passes light, neither of which URP/Lit can do —
        /// see the header of <c>PN3D_Foliage.shader</c>.
        /// </summary>
        /// <param name="sway">
        /// How far the top of this thing moves, in metres. A crown four metres up wants
        /// about 0.16; a shrub wants a fraction of that, and a tuft of grass by the kerb
        /// wants almost none, because a blade of grass that swings 16 cm is a flag.
        /// </param>
        /// <param name="fromY">
        /// Where the bending starts, as 1/metres. Sway is weighted by the square of
        /// (height x this), clamped to one, so a tall crown is fully mobile at its top and
        /// planted at the trunk. It has to be per-material because a tuft's "top" is 0.3 m
        /// and a tree's is 5 m.
        /// </param>
        public static Material Foliage(Color leaf, float sway = 0.16f, float fromY = 0.34f)
            => Get($"leaf{ColorUtility.ToHtmlStringRGB(leaf)}_{sway:0.00}_{fromY:0.00}", () =>
            {
                _foliage = _foliage != null ? _foliage : Resolve("PN3D/Foliage");
                var m = new Material(_foliage);
                SetBase(m, leaf);
                m.SetFloat("_Smoothness", 0.06f);
                m.SetFloat("_WindAmp", sway);
                m.SetFloat("_SwayFromY", fromY);
                // Transmission is the leaf colour opened right up. Sap green passes light
                // yellow-green, never the dark green it looks in shade.
                Color.RGBToHSV(leaf, out float h, out float s, out float v);
                m.SetColor("_TransColor", Color.HSVToRGB(Mathf.Repeat(h - 0.035f, 1f),
                                                         Mathf.Clamp01(s * 0.88f),
                                                         Mathf.Clamp01(v * 2.6f + 0.18f)));
                return m;
            });

        /// <summary>Polished metal for bumper blades, exhaust tips and rim lips.</summary>
        public static Material Chrome(float smoothness = 0.93f)
            => Get($"chrome{smoothness:0.00}", () =>
            {
                var m = new Material(Lit);
                SetBase(m, new Color(0.86f, 0.88f, 0.92f));
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_Smoothness", smoothness);
                return m;
            });

        /// <summary>
        /// Tyre rubber. Deliberately not pure black: a real tyre in daylight sits around
        /// 0.05–0.08 albedo with a faint sheen on the sidewall, and clamping it to 0 makes
        /// the wheel a silhouette-shaped hole with no form at all.
        /// </summary>
        public static Material Rubber()
            => Get("rubber", () =>
            {
                var m = new Material(Lit);
                SetBase(m, new Color(0.055f, 0.058f, 0.065f));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.32f);
                return m;
            });

        /// <summary>
        /// Self-illuminated lens: headlight bars, tail bars. Bloom in the volume profile
        /// turns an over-1 colour into an actual glow.
        ///
        /// Unlit rather than Lit-with-_EMISSION, and that is a deliberate retreat from the
        /// obvious approach. No material ASSET in this project carries the _EMISSION
        /// keyword — every material is born at runtime — so Unity's built-in stripper drops
        /// the emissive variant of URP/Lit. That is the 12288 -> 24 cut visible in the
        /// build log, and it is why the brake lights did not light on the first device
        /// build no matter what the driver wrote into _EmissionColor.
        ///
        /// The tempting fix, clearing StripUnusedVariants in the URP global settings, is a
        /// trap worth recording: it does not surgically keep _EMISSION, it disables keyword
        /// stripping wholesale and takes the build from 48 shader variants to 248,832. That
        /// compiles at roughly 1.5 variants a second — a two-day build. It was measured,
        /// not estimated.
        ///
        /// A lamp does not need shading anyway. Unlit ships unconditionally, costs less,
        /// and looks the same, because what reads as "glowing" is the bloom.
        /// </summary>
        public static Material Emissive(Color body, Color emission, float intensity)
            => Get($"emit{ColorUtility.ToHtmlStringRGB(body)}_{ColorUtility.ToHtmlStringRGB(emission)}_{intensity:0.00}", () =>
            {
                var m = new Material(Unlit);
                SetBase(m, body + emission * intensity);
                return m;
            });

        /// <summary>
        /// Drive a lamp's brightness. The property name lives here and nowhere else,
        /// because the lamps moved from Lit's _EmissionColor to Unlit's _BaseColor and the
        /// two call sites that flare the brake lights should not have to know that.
        /// </summary>
        public static void SetGlow(Material m, Color c)
        {
            if (m == null) return;
            m.SetColor("_BaseColor", c);
            m.color = c;
        }

        /// <summary>
        /// Car glass. Still opaque, deliberately: there is no interior geometry behind the
        /// canopy, so a transparent one would show the road through the cabin, and opaque
        /// also sorts correctly against the body at every camera angle with no transparent
        /// queue. What makes it read as glass is not alpha, it is that a windscreen is
        /// almost a mirror — dark base, very high smoothness, and enough metallic to keep
        /// the reflection strong when the surface faces you rather than only at grazing
        /// angles. Tinted near-black plus a bright sky reflection is exactly how glass
        /// photographs from outside.
        /// </summary>
        /// <remarks>
        /// <paramref name="metallic"/> is how hard the pane mirrors. The car wants 0.22:
        /// enough to catch the sky over a curved screen without the tint disappearing. A
        /// house window wants far more, because a flat pane seen from across the street is
        /// almost entirely a reflection of whatever is behind you — at 0.22 they render as
        /// black rectangles, which is exactly how the first pass of these houses looked.
        /// </remarks>
        public static Material Glass(Color tint, Texture2D map = null, float metallic = 0.22f)
            => Get("glass" + ColorUtility.ToHtmlStringRGB(tint) + (map != null ? "_m" : "")
                   + $"_{metallic:0.00}", () =>
        {
            var m = new Material(Lit);
            SetBase(m, tint);
            if (map != null) m.SetTexture("_BaseMap", map);
            m.SetFloat("_Smoothness", 0.94f);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_EnvironmentReflections", 1f);
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
