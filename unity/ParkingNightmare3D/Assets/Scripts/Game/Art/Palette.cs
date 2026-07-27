using UnityEngine;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Every colour a person can be, and no others.
    ///
    /// Quantised on purpose, and it is not an art decision. <see cref="MatLib"/> caches on
    /// colour, so a hue drawn off a continuum mints a material per pedestrian and each one
    /// is its own draw call — the same mistake that put fifteen hundred one-use materials
    /// into the tree canopies. A crowd of thirty shares about a dozen materials here, and
    /// shares them with the crowd in every other mission.
    /// </summary>
    public static class Palette
    {
        /// <summary>
        /// Skin, on the Fitzpatrick range. These are diffuse albedo values in linear-ish
        /// sRGB, not the colour skin appears under a bright sun — the shader's subsurface
        /// term lifts and warms them considerably.
        /// </summary>
        public static readonly Color[] Skins =
        {
            new Color(0.902f, 0.757f, 0.647f),
            new Color(0.831f, 0.659f, 0.518f),
            new Color(0.710f, 0.522f, 0.373f),
            new Color(0.529f, 0.361f, 0.243f),
            new Color(0.365f, 0.239f, 0.169f),
            new Color(0.259f, 0.169f, 0.129f),
        };

        /// <summary>
        /// Hair. The darkest entry is deliberately not as dark as hair measures.
        ///
        /// A dielectric reflects about 4% of the sky whatever its albedo, so at a true
        /// black-hair albedo of 0.06 the sky reflection is roughly half of what leaves the
        /// surface — and a bright blue sky over a near-black diffuse renders as navy. It is
        /// physically right and it looks wrong, because real black hair is also carrying a
        /// strong warm specular from the sun that one broad lobe does not reproduce.
        /// Lifting the diffuse is the cheap end of that trade.
        /// </summary>
        public static readonly Color[] Hairs =
        {
            new Color(0.118f, 0.100f, 0.086f), new Color(0.180f, 0.122f, 0.086f),
            new Color(0.322f, 0.216f, 0.129f), new Color(0.596f, 0.475f, 0.278f),
            new Color(0.541f, 0.537f, 0.545f), new Color(0.404f, 0.180f, 0.106f),
        };

        public static readonly Color[] Shirts =
        {
            new Color(0.780f, 0.286f, 0.267f), new Color(0.255f, 0.420f, 0.690f),
            new Color(0.918f, 0.914f, 0.898f), new Color(0.302f, 0.553f, 0.384f),
            new Color(0.910f, 0.741f, 0.290f), new Color(0.443f, 0.345f, 0.596f),
            new Color(0.208f, 0.227f, 0.267f), new Color(0.882f, 0.541f, 0.345f),
            new Color(0.365f, 0.443f, 0.478f), new Color(0.639f, 0.643f, 0.667f),
        };

        public static readonly Color[] Trousers =
        {
            new Color(0.169f, 0.208f, 0.294f), new Color(0.208f, 0.208f, 0.227f),
            new Color(0.502f, 0.443f, 0.345f), new Color(0.286f, 0.345f, 0.451f),
            new Color(0.286f, 0.298f, 0.239f), new Color(0.365f, 0.294f, 0.243f),
        };

        /// <summary>Eyes. Brown is most of the world, so brown is most of the list.</summary>
        public static readonly Color[] Irises =
        {
            new Color(0.180f, 0.106f, 0.055f), new Color(0.271f, 0.169f, 0.078f),
            new Color(0.106f, 0.075f, 0.055f), new Color(0.361f, 0.286f, 0.145f),
            new Color(0.290f, 0.404f, 0.412f), new Color(0.271f, 0.392f, 0.514f),
        };
    }
}
