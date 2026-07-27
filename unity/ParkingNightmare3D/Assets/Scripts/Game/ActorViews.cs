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
            /// <summary>Second joint in each limb. A straight tube limb is a mannequin.</summary>
            public Transform KneeL, KneeR, ElbowL, ElbowR;
            /// <summary>Total height in metres, so the bob and the stride scale with the person.</summary>
            public float Height;
        }

        /// <summary>
        /// Pedestrian.
        ///
        /// These are not set dressing. The shame system is the game (§10) and it is
        /// expressed entirely through this crowd: they turn, they film, they dive out of
        /// the way, and they do it two metres from the car at the exact moment the player
        /// is concentrating hardest. Everything else in the world can be looked at from
        /// across the street; a pedestrian is looked at from arm's length.
        ///
        /// All the geometry now lives in <see cref="Human"/>, which builds one continuous
        /// skinned surface over a real skeleton. What this leaves behind is the mapping
        /// from that rig to the joints the animation drives — and the mapping is
        /// deliberately the same set of names it has always been, so the walk cycle did
        /// not have to be rewritten when the body underneath it changed from a pile of
        /// primitives into a mesh that deforms.
        /// </summary>
        static PedView MakePed(Transform parent, int index)
        {
            var rig = Human.Build(parent, (uint)(index * 2654435761u + 17u), out _);

            var v = new PedView
            {
                Root = rig.Root,
                Torso = rig.Chest,
                Head = rig.Head,
                LegL = rig.LegL, LegR = rig.LegR,
                KneeL = rig.ShinL, KneeR = rig.ShinR,
                ArmL = rig.ArmL, ArmR = rig.ArmR,
                ElbowL = rig.ForeArmL, ElbowR = rig.ForeArmR,
                Height = rig.Height,
            };

            // The phone hangs off the hand bone, so it follows the whole arm chain — the
            // shoulder swings it, the elbow raises it, and nothing has to be re-derived.
            float H = rig.Height;
            v.Phone = Geo.Box("Phone", rig.HandR,
                              new Vector3(0.040f * H, 0.075f * H, 0.012f * H),
                              new Vector3(0, -0.020f * H, 0.045f * H),
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
            float p = (float)ped.Phase;
            // The bob scales with the person, so a child does not bounce like an adult.
            float bob = Mathf.Abs(Mathf.Sin(p)) * 0.032f * (v.Height / 1.72f);
            v.Root.position = WorldBuilder.ToWorld(x, y, RoadBuilder.CurbY + bob + (diving ? 0.30f : 0f));
            v.Root.rotation = WorldBuilder.ToRotation(ped.Face)
                            * Quaternion.Euler(diving ? 62f : 0f, 0, 0);

            // walk cycle: opposed legs and arms, amplitude scaled by how fast they move
            float amp = ped.State == PedState.Cross ? 40f : 26f;
            float swing = diving ? 0f : Mathf.Sin(p) * amp;

            // Knees, which is the thing a pendulum leg cannot do. The knee flexes hard
            // just after the foot leaves the ground and straightens again before it lands,
            // so the swinging leg clears the pavement instead of scything through it —
            // and a leg that stays straight through the whole cycle is most of why the
            // old walk read as a wind-up toy. Peak flexion trails maximum hip extension
            // by about half a radian.
            float flex = amp * 1.45f + 4f;
            float kneeL = diving ? 62f : Mathf.Max(0f, Mathf.Sin(p - 0.5f)) * flex + 4f;
            float kneeR = diving ? 62f : Mathf.Max(0f, Mathf.Sin(p + Mathf.PI - 0.5f)) * flex + 4f;

            v.LegL.localRotation = Quaternion.Euler(swing, 0, 0);
            v.LegR.localRotation = Quaternion.Euler(-swing, 0, 0);
            v.KneeL.localRotation = Quaternion.Euler(kneeL, 0, 0);
            v.KneeR.localRotation = Quaternion.Euler(kneeR, 0, 0);

            bool filming = ped.State == PedState.Film || ped.Filmed;
            if (filming)
            {
                // Phone held up toward the player. The upper arms come forward and the
                // elbows fold, because that is how a person holds a phone up; swinging
                // straight arms overhead is a salute.
                v.ArmL.localRotation = Quaternion.Euler(-44f, 0, 14f);
                v.ArmR.localRotation = Quaternion.Euler(-44f, 0, -14f);
                v.ElbowL.localRotation = Quaternion.Euler(-64f, 0, 0);
                v.ElbowR.localRotation = Quaternion.Euler(-64f, 0, 0);
            }
            else if (diving)
            {
                v.ArmL.localRotation = Quaternion.Euler(-150f, 0, 20f);
                v.ArmR.localRotation = Quaternion.Euler(-150f, 0, -20f);
                v.ElbowL.localRotation = Quaternion.Euler(-30f, 0, 0);
                v.ElbowR.localRotation = Quaternion.Euler(-30f, 0, 0);
            }
            else
            {
                v.ArmL.localRotation = Quaternion.Euler(-swing * 0.65f, 0, 0);
                v.ArmR.localRotation = Quaternion.Euler(swing * 0.65f, 0, 0);
                // An arm is never straight, even at rest. A locked elbow is the other half
                // of the mannequin look and it costs one constant to fix.
                v.ElbowL.localRotation = Quaternion.Euler(-13f - Mathf.Max(0f, swing) * 0.5f, 0, 0);
                v.ElbowR.localRotation = Quaternion.Euler(-13f - Mathf.Max(0f, -swing) * 0.5f, 0, 0);
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
