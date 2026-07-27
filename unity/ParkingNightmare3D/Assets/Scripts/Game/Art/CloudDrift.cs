using UnityEngine;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Moves the sun sideways so its cloud cookie drifts.
    ///
    /// This looks like it should not work and it does. A directional light's position has
    /// no effect on lighting — only its rotation does — but the cookie is projected in the
    /// light's own space, so translating the light slides the cookie across the world
    /// without touching the direction of the sun or the shape of a single shadow. There is
    /// no other handle on it: URP builds the cookie matrix from the light transform, and
    /// exposes no offset.
    ///
    /// The drift is deliberately slow. Real cloud shadows cross a field at walking pace
    /// and the mission lasts about a minute, so anything faster reads as a strobing
    /// pattern rather than as weather.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudDrift : MonoBehaviour
    {
        /// <summary>Metres per second, in world XZ. About 3 m/s is a light breeze.</summary>
        public Vector3 Velocity = new Vector3(2.6f, 0f, 1.5f);

        void Update() => transform.position += Velocity * Time.deltaTime;
    }
}
