using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// A few birds, wheeling.
    ///
    /// <c>districts.json</c> has carried a <c>birds</c> flag since the reference and
    /// nothing has ever read it. It is worth reading: outside the traffic, this world is
    /// completely static, and a still world reads as a photograph of a model no matter how
    /// good the model is. Two or three specks turning slowly against the sky is the
    /// cheapest possible correction to that, and it is the only motion the player will see
    /// while sitting still at a red light.
    ///
    /// Deliberately small, high and slow. A bird you can identify is a distraction in a
    /// game about judging a kerb gap; a bird you notice only out of the corner of your eye
    /// is atmosphere. They are unlit and cast nothing.
    /// </summary>
    public sealed class Birds : MonoBehaviour
    {
        struct Flyer
        {
            public Transform T;
            public Vector3 Centre;
            public float Radius, Height, Speed, Phase, Bob, Flap;
        }

        readonly List<Flyer> _flock = new();

        public static void Build(District d, Vector3 centre, uint seed, Transform parent)
        {
            if (!d.Birds || d.Night) return;

            var go = new GameObject("PN3D_Birds");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<Birds>();

            var rng = new Rng(seed ^ 0x8EAD17u);
            // A bird's silhouette against the sky is a dark chevron and nothing else, so
            // that is all it is: no body, no colour, no shading. One material for the lot.
            var mat = MatLib.Flat(new Color(0.16f, 0.17f, 0.20f));
            var mesh = Chevron();

            int n = 3 + (int)(rng.Next() * 3);
            for (int i = 0; i < n; i++)
            {
                var t = new GameObject($"Bird{i}").transform;
                t.SetParent(go.transform, false);
                var mf = t.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                b._flock.Add(new Flyer
                {
                    T = t,
                    Centre = centre + new Vector3((float)rng.Rand(-140, 140), 0,
                                                  (float)rng.Rand(-140, 140)),
                    Radius = (float)rng.Rand(26, 74),
                    Height = (float)rng.Rand(34, 62),
                    Speed = (float)rng.Rand(0.09, 0.19) * (rng.Chance(0.5) ? 1f : -1f),
                    Phase = (float)rng.Rand(0, Mathf.PI * 2),
                    Bob = (float)rng.Rand(1.2, 3.4),
                    Flap = (float)rng.Rand(2.6, 4.4),
                });
            }
        }

        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < _flock.Count; i++)
            {
                var f = _flock[i];
                float a = f.Phase + t * f.Speed;
                var p = f.Centre + new Vector3(Mathf.Cos(a) * f.Radius,
                                               f.Height + Mathf.Sin(a * 2.3f) * f.Bob,
                                               Mathf.Sin(a) * f.Radius);
                var fwd = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * Mathf.Sign(f.Speed);
                f.T.SetPositionAndRotation(
                    p,
                    Quaternion.LookRotation(fwd, Vector3.up)
                    // banking into the turn, and the wings beating
                    * Quaternion.Euler(0, 0, Mathf.Sign(f.Speed) * 16f
                                             + Mathf.Sin(t * f.Flap) * 26f));
            }
        }

        /// <summary>Two triangles in a shallow V. The whole bird.</summary>
        static Mesh Chevron() => Geo.Get("bird", () =>
        {
            const float span = 0.55f, chord = 0.17f, dihedral = 0.14f;
            var v = new[]
            {
                new Vector3(0, 0, chord * 0.5f),
                new Vector3(0, 0, -chord * 0.5f),
                new Vector3(-span, dihedral, -chord * 0.15f),
                new Vector3(span, dihedral, -chord * 0.15f),
            };
            var mesh = new Mesh { name = "bird" };
            mesh.SetVertices(v);
            // both windings: a bird overhead is seen from below and one going away is
            // seen from above, and neither is worth a second draw
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 1, 0, 3, 1, 0, 1, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        });
    }
}
