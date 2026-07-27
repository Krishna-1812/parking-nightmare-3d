using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;
using PN3D.Game.Art;

namespace PN3D.Game
{
    /// <summary>
    /// Views for traffic cars, cross traffic and pedestrians.
    ///
    /// The simulation owns all state; this only mirrors it into transforms and animates
    /// the parts that have no simulation meaning — wheel roll, brake lights, a walk cycle.
    /// Car body colour is chosen here from the car's id, deliberately: the reference draws
    /// that colour from the same RNG the AI uses, and reproducing a purely visual draw
    /// inside the deterministic stream would buy nothing. See the note in
    /// <see cref="TrafficSystem"/>.
    /// </summary>
    public sealed class ActorViews : MonoBehaviour
    {
        public MissionRun Run;

        readonly Dictionary<int, CarView.Rig> _cars = new();
        readonly Dictionary<int, CarView.Rig> _crossers = new();
        readonly Dictionary<int, float> _roll = new();
        readonly List<PedView> _peds = new();
        readonly HashSet<int> _seen = new();

        Transform _root;

        void Awake()
        {
            _root = new GameObject("PN3D_Actors").transform;
            _root.SetParent(transform, false);
        }

        // ------------------------------------------------------------------ builders

        /// <summary>
        /// A traffic car reuses the player's hull generator, sized from the kind's own
        /// length and width — the same numbers the gap and collision maths use, so what
        /// you see is exactly what the simulation is testing against.
        /// </summary>
        static CarView.Rig MakeCar(Transform parent, int id, double len, double wid, string kind)
        {
            var veh = new VehicleDef { Key = kind, Len = len, Wid = wid, Hgt = 1.5 };

            // Style and paint are both keyed off the car's id, so one "sedan" is an
            // executive saloon in gunmetal and the next is a fastback coupe in pearl white.
            // Deterministic on purpose: traffic cars are pooled and re-shown, and a car
            // that changed shape when it was recycled would be very obvious in the mirror.
            var st = CarStyles.ForTraffic(kind, id);
            var body = kind switch
            {
                "taxi" => CarStyles.TaxiYellow,
                "police" => CarStyles.PoliceWhite,
                _ => CarStyles.PaintFor(id),
            };

            var rig = CarView.Build(parent, $"{kind}_{id}", veh, st, body);
            rig.Root.name = $"Traffic_{id}";
            return rig;
        }

        sealed class PedView
        {
            public Transform Root, Torso, Head, LegL, LegR, ArmL, ArmR, Phone;
        }

        // Every colour a pedestrian can be, and no others.
        //
        // The old shirt colour was Color.HSVToRGB(rng.Next(), ...) — a hue off a continuum,
        // which is the exact mistake that put fifteen hundred one-use materials into the
        // tree canopies: MatLib caches on colour, so a free hue mints a material per
        // pedestrian and each one is its own draw call. Quantised, a crowd of thirty shares
        // about a dozen materials with the crowd in every other mission.
        static readonly Color[] Skins =
        {
            new Color(0.945f, 0.804f, 0.678f), new Color(0.878f, 0.694f, 0.514f),
            new Color(0.741f, 0.541f, 0.361f), new Color(0.545f, 0.373f, 0.239f),
            new Color(0.361f, 0.239f, 0.161f),
        };

        static readonly Color[] Shirts =
        {
            new Color(0.82f, 0.30f, 0.28f), new Color(0.27f, 0.44f, 0.72f),
            new Color(0.94f, 0.94f, 0.92f), new Color(0.32f, 0.58f, 0.40f),
            new Color(0.93f, 0.76f, 0.30f), new Color(0.46f, 0.36f, 0.62f),
            new Color(0.22f, 0.24f, 0.28f), new Color(0.90f, 0.56f, 0.36f),
        };

        static readonly Color[] Trousers =
        {
            new Color(0.18f, 0.22f, 0.31f), new Color(0.22f, 0.22f, 0.24f),
            new Color(0.52f, 0.46f, 0.36f), new Color(0.30f, 0.36f, 0.47f),
            new Color(0.30f, 0.31f, 0.25f),
        };

        static readonly Color[] Hairs =
        {
            new Color(0.09f, 0.08f, 0.08f), new Color(0.21f, 0.14f, 0.10f),
            new Color(0.36f, 0.24f, 0.14f), new Color(0.66f, 0.53f, 0.31f),
            new Color(0.55f, 0.55f, 0.56f), new Color(0.44f, 0.20f, 0.12f),
        };

        /// <summary>An egg. Two of them make a person: one for the head, one for the chest.</summary>
        static Mesh Ovoid(string key, float rMax, float top, float bottom, float waist)
            => Geo.Lathe(key, new[]
            {
                new Vector2(0.001f, bottom),
                new Vector2(rMax * waist, bottom + (top - bottom) * 0.16f),
                new Vector2(rMax, bottom + (top - bottom) * 0.45f),
                new Vector2(rMax * 0.93f, bottom + (top - bottom) * 0.72f),
                new Vector2(rMax * 0.52f, bottom + (top - bottom) * 0.93f),
                new Vector2(0.001f, top),
            }, 10);

        /// <summary>
        /// Pedestrian.
        ///
        /// These are not set dressing. The shame system is the game (§10) and it is
        /// expressed entirely through this crowd: they turn, they film, they dive out of
        /// the way, and they do it two metres from the car at the exact moment the player
        /// is concentrating hardest. Everything else in the world can be looked at from
        /// across the street; a pedestrian is looked at from arm's length.
        ///
        /// They were seven boxes and a cube for a head. Now the torso and head are surfaces
        /// of revolution, the limbs taper, the shoulders are a separate mass, and there are
        /// shoes — which sounds like a detail and is not: a leg that ends in a flat cut is
        /// the single thing that reads as "untextured placeholder" from any distance.
        ///
        /// The joint layout is unchanged on purpose. The pivots at the hip and the shoulder
        /// sit exactly where they did, so <see cref="PoseePed"/> drives this the same way it
        /// drove the boxes, and the walk cycle and the phone pose did not have to be
        /// re-tuned against new geometry.
        /// </summary>
        static PedView MakePed(Transform parent, int index)
        {
            var rng = new Rng((uint)(index * 2654435761u + 17u));
            var skinC = Skins[(int)(rng.Next() * Skins.Length)];
            var shirtC = Shirts[(int)(rng.Next() * Shirts.Length)];
            var trouserC = Trousers[(int)(rng.Next() * Trousers.Length)];
            var hairC = Hairs[(int)(rng.Next() * Hairs.Length)];
            bool longHair = rng.Chance(0.38);

            var skin = MatLib.Solid(skinC, 0.16f);
            var shirt = MatLib.Solid(shirtC, 0.10f);
            var trousers = MatLib.Solid(trouserC, 0.08f);
            var hair = MatLib.Solid(hairC, 0.22f);
            var shoe = MatLib.Solid(new Color(0.13f, 0.12f, 0.12f), 0.28f);

            var root = new GameObject("Ped").transform;
            root.SetParent(parent, false);

            var v = new PedView { Root = root };

            // Torso: an ovoid squashed front-to-back, because a person is wider across the
            // shoulders than they are deep and a cylinder reads as a bollard.
            v.Torso = Geo.Node("Torso", root, Ovoid("torso", 0.20f, 0.30f, -0.30f, 0.72f),
                               shirt, new Vector3(0, 1.06f, 0),
                               Quaternion.identity, new Vector3(1f, 1f, 0.62f)).transform;
            Geo.Node("Hips", root, Geo.Cylinder(0.155f, 0.175f, 0.20f, 10), trousers,
                     new Vector3(0, 0.83f, 0), Quaternion.identity, new Vector3(1f, 1f, 0.72f));
            Geo.Node("Shoulders", root, Geo.Cylinder(0.135f, 0.115f, 0.42f, 8), shirt,
                     new Vector3(0, 1.30f, 0), Quaternion.Euler(0, 0, 90f),
                     new Vector3(1f, 1f, 0.72f));
            Geo.Node("Neck", root, Geo.Cylinder(0.052f, 0.058f, 0.09f, 7), skin,
                     new Vector3(0, 1.355f, 0));

            v.Head = Geo.Node("Head", root, Ovoid("head", 0.098f, 0.125f, -0.115f, 0.80f),
                              skin, new Vector3(0, 1.50f, 0),
                              Quaternion.identity, new Vector3(1f, 1f, 0.92f)).transform;
            // Hair is a cap on the crown, not a second head. The first version reused the
            // head ovoid one millimetre larger, which put hair over the ears, the jaw and
            // most of the face — a swim cap, not a haircut.
            Geo.Node("Hair", v.Head, Geo.Lathe("hairdome", new[]
            {
                new Vector2(0.101f, 0.005f), new Vector2(0.100f, 0.045f),
                new Vector2(0.086f, 0.092f), new Vector2(0.048f, 0.122f),
                new Vector2(0.001f, 0.134f),
            }, 10), hair, new Vector3(0, 0f, -0.004f));
            if (longHair)
                Geo.Box("HairBack", v.Head, new Vector3(0.17f, 0.20f, 0.085f),
                        new Vector3(0, -0.055f, -0.072f), hair);

            // Two dots. At the range a pedestrian is actually seen — leaning over a kerb
            // two metres from the car — a blank head reads as a mannequin, and a face is
            // the difference between a crowd watching you and scenery.
            var eyeMat = MatLib.Solid(new Color(0.10f, 0.09f, 0.10f), 0.45f);
            foreach (float ex in new[] { -0.036f, 0.036f })
                Geo.Node("Eye", v.Head, Geo.Pebble, eyeMat,
                         new Vector3(ex, 0.018f, 0.083f), Quaternion.identity,
                         new Vector3(0.030f, 0.020f, 0.016f));

            // Limbs pivot at the hip and the shoulder, so each segment hangs below its own
            // origin and a rotation of the pivot swings the whole limb.
            Transform Limb(string name, float x, float y, float len, float r0, float r1,
                           Material m, bool foot)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root, false);
                pivot.localPosition = new Vector3(x, y, 0);
                Geo.Node("Seg", pivot, Geo.Cylinder(r0, r1, len, 7), m,
                         new Vector3(0, -len / 2f, 0));
                if (foot)
                    Geo.Box("Shoe", pivot, new Vector3(0.10f, 0.06f, 0.24f),
                            new Vector3(0, -len - 0.02f, 0.045f), shoe);
                else
                    Geo.Node("Hand", pivot, Geo.Pebble, skin,
                             new Vector3(0, -len - 0.03f, 0),
                             Quaternion.identity, Vector3.one * 0.085f);
                return pivot;
            }

            // r0 is the top of the segment and r1 the bottom: a thigh is thicker than an
            // ankle and an upper arm is thicker than a wrist.
            v.LegL = Limb("LegL", -0.085f, 0.78f, 0.76f, 0.075f, 0.048f, trousers, true);
            v.LegR = Limb("LegR", 0.085f, 0.78f, 0.76f, 0.075f, 0.048f, trousers, true);
            v.ArmL = Limb("ArmL", -0.215f, 1.30f, 0.54f, 0.052f, 0.038f, shirt, false);
            v.ArmR = Limb("ArmR", 0.215f, 1.30f, 0.54f, 0.052f, 0.038f, shirt, false);

            v.Phone = Geo.Box("Phone", v.ArmR, new Vector3(0.07f, 0.13f, 0.02f),
                              new Vector3(0, -0.58f, 0.05f),
                              MatLib.Emissive(new Color(0.08f, 0.08f, 0.1f),
                                              new Color(0.6f, 0.8f, 1f), 0.8f)).transform;
            v.Phone.gameObject.SetActive(false);
            return v;
        }

        // ------------------------------------------------------------------ static spawn

        /// <summary>
        /// One-shot spawn of the actors at their current poses, for edit-mode tooling
        /// (screenshot capture) where no update loop is running.
        /// </summary>
        public static void SpawnStatic(MissionRun run, Transform parent)
        {
            var root = new GameObject("PN3D_Actors_Static").transform;
            root.SetParent(parent, false);

            foreach (var car in run.Traffic.Cars)
                PlaceCar(MakeCar(root, car.Id, car.Len, car.Wid, car.Kind), car.X, car.Y, car.H);

            foreach (var kv in run.Traffic.Crossers)
                foreach (var cr in kv.Value)
                    PlaceCar(MakeCar(root, cr.Id, cr.Len, cr.Wid, cr.Kind), cr.X, cr.Y, cr.H);

            for (int i = 0; i < run.Peds.List.Count; i++)
            {
                var view = MakePed(root, i);
                PoseePed(view, run.Peds.List[i], run.Peds.List[i].X, run.Peds.List[i].Y);
            }
        }

        static void PlaceCar(CarView.Rig rig, double x, double y, double h)
        {
            rig.Root.position = WorldBuilder.ToWorld(x, y);
            rig.Root.rotation = WorldBuilder.ToRotation(h);
        }

        static void PoseePed(PedView v, Ped ped, double x, double y)
        {
            bool diving = ped.State == PedState.Dive;
            float bob = Mathf.Abs(Mathf.Sin((float)ped.Phase)) * 0.04f;
            v.Root.position = WorldBuilder.ToWorld(x, y, RoadBuilder.CurbY + bob + (diving ? 0.30f : 0f));
            v.Root.rotation = WorldBuilder.ToRotation(ped.Face)
                            * Quaternion.Euler(diving ? 62f : 0f, 0, 0);

            // walk cycle: opposed legs and arms, amplitude scaled by how fast they move
            float swing = diving ? 0f : Mathf.Sin((float)ped.Phase) * (ped.State == PedState.Cross ? 42f : 28f);
            v.LegL.localRotation = Quaternion.Euler(swing, 0, 0);
            v.LegR.localRotation = Quaternion.Euler(-swing, 0, 0);

            bool filming = ped.State == PedState.Film || ped.Filmed;
            if (filming)
            {
                // both arms up holding the phone toward the player
                v.ArmL.localRotation = Quaternion.Euler(-72f, 0, 12f);
                v.ArmR.localRotation = Quaternion.Euler(-72f, 0, -12f);
            }
            else if (diving)
            {
                v.ArmL.localRotation = Quaternion.Euler(-150f, 0, 20f);
                v.ArmR.localRotation = Quaternion.Euler(-150f, 0, -20f);
            }
            else
            {
                v.ArmL.localRotation = Quaternion.Euler(-swing * 0.7f, 0, 0);
                v.ArmR.localRotation = Quaternion.Euler(swing * 0.7f, 0, 0);
            }
            v.Phone.gameObject.SetActive(filming);
        }

        // ------------------------------------------------------------------ live update

        void LateUpdate()
        {
            if (Run?.Traffic == null) return;

            float alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;

            // ---- traffic ----
            _seen.Clear();
            foreach (var car in Run.Traffic.Cars)
            {
                _seen.Add(car.Id);
                if (!_cars.TryGetValue(car.Id, out var rig) || rig?.Root == null)
                {
                    rig = MakeCar(_root, car.Id, car.Len, car.Wid, car.Kind);
                    _cars[car.Id] = rig;
                }
                rig.Root.gameObject.SetActive(true);
                double x = MathX.Lerp(car.Px, car.X, alpha);
                double y = MathX.Lerp(car.Py, car.Y, alpha);
                double h = car.Ph + MathX.AngNorm(car.H - car.Ph) * alpha;
                PlaceCar(rig, x, y, h);

                float roll = _roll.TryGetValue(car.Id, out float r) ? r : 0f;
                CarView.Animate(rig, car.V, 0.0, Time.deltaTime, ref roll);
                _roll[car.Id] = roll;

                // brake lights come on when the car is slowing to its blocker or a red
                SetBrake(rig, car.V < car.Cruise - 1.0 || car.BlockT > 0);
            }
            foreach (var kv in _cars)
                if (!_seen.Contains(kv.Key) && kv.Value?.Root != null)
                    kv.Value.Root.gameObject.SetActive(false);

            // ---- cross traffic ----
            _seen.Clear();
            foreach (var kv in Run.Traffic.Crossers)
                foreach (var cr in kv.Value)
                {
                    _seen.Add(cr.Id);
                    if (!_crossers.TryGetValue(cr.Id, out var rig) || rig?.Root == null)
                    {
                        rig = MakeCar(_root, cr.Id, cr.Len, cr.Wid, cr.Kind);
                        rig.Root.name = $"Cross_{cr.Id}";
                        _crossers[cr.Id] = rig;
                    }
                    rig.Root.gameObject.SetActive(true);
                    PlaceCar(rig, cr.X, cr.Y, cr.H);
                    float roll = _roll.TryGetValue(-cr.Id, out float r) ? r : 0f;
                    CarView.Animate(rig, cr.V, 0.0, Time.deltaTime, ref roll);
                    _roll[-cr.Id] = roll;
                }
            foreach (var kv in _crossers)
                if (!_seen.Contains(kv.Key) && kv.Value?.Root != null)
                    kv.Value.Root.gameObject.SetActive(false);

            // ---- pedestrians ----
            var list = Run.Peds.List;
            while (_peds.Count < list.Count) _peds.Add(MakePed(_root, _peds.Count));
            for (int i = 0; i < list.Count; i++)
            {
                var ped = list[i];
                PoseePed(_peds[i], ped,
                         MathX.Lerp(ped.Px, ped.X, alpha),
                         MathX.Lerp(ped.Py, ped.Y, alpha));
            }
        }

        static void SetBrake(CarView.Rig rig, bool on)
        {
            MatLib.SetGlow(rig.BrakeLight,
                new Color(1f, 0.23f, 0.19f) * (on ? 2.4f : 0.35f));
        }
    }
}
