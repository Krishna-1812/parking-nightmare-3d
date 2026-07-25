using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PN3D.Game.Art
{
    /// <summary>
    /// The post-processing stack, built as a global volume at load.
    ///
    /// Authored in code rather than as a .asset for the same reason the materials are:
    /// the grade is tuned per district (the night and dusk districts want a different
    /// exposure and tint from the suburbs), so it has to be a function of the palette. A
    /// committed profile would only ever be right for one of the six.
    ///
    /// The grade is deliberately gentle. This is a bright, readable, deadpan-comic game —
    /// the player has to judge a curb gap in centimetres from a chase camera, so anything
    /// that crushes shadows or blooms the road markings is actively hostile to the parking
    /// tolerances in §6.
    /// </summary>
    public static class PostFx
    {
        /// <summary>
        /// Put a camera into the state the grade expects: HDR, skybox clear, a far plane
        /// past the horizon ring, post-processing on and SMAA.
        ///
        /// Lives here rather than at each call site because the editor capture tool is in
        /// its own assembly and cannot see URP types directly — and because a camera that
        /// silently has post-processing off makes every screenshot a lie about the build.
        /// </summary>
        public static void SetupCamera(Camera cam, float fov)
        {
            cam.fieldOfView = fov;
            cam.farClipPlane = SceneEnv.HorizonRadius * 2.5f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;

            var data = cam.GetComponent<UniversalAdditionalCameraData>()
                       ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        public static Volume Build(District d, Transform parent = null)
        {
            var go = new GameObject("PN3D_PostFx");
            if (parent != null) go.transform.SetParent(parent, false);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "PN3D_Mission";
            volume.sharedProfile = profile;

            // ---- tonemapping ----
            // Neutral, not ACES: ACES pushes saturated paint toward orange and would shift
            // the district palettes away from the values they were authored at.
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);

            // ---- exposure and colour ----
            var grade = profile.Add<ColorAdjustments>();
            grade.postExposure.Override(d.Night ? 0.25f : 0.05f);
            grade.contrast.Override(d.Night ? 8f : 6f);
            grade.saturation.Override(8f);
            // pull the whole frame very slightly toward the district's own sky, which is
            // what makes the fog, the horizon ring and the lit surfaces agree
            grade.colorFilter.Override(Color.Lerp(Color.white, d.SkyMid, 0.06f));

            var wb = profile.Add<WhiteBalance>();
            wb.temperature.Override(d.Night ? -8f : 4f);

            // ---- bloom ----
            // High threshold on purpose: only the emissive light bars, the spot markers
            // and specular hits on the paint should bloom. A low threshold would smear the
            // white edge lines the player is aligning against.
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(d.Night ? 0.75f : 1.15f);
            bloom.intensity.Override(d.Night ? 0.9f : 0.42f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(Color.white);

            // ---- vignette ----
            var vig = profile.Add<Vignette>();
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.06f, 0.07f, 0.10f));

            // ---- lens ----
            var ca = profile.Add<ChromaticAberration>();
            ca.intensity.Override(0.06f);

            var grain = profile.Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.14f);
            grain.response.Override(0.8f);

            return volume;
        }
    }
}
