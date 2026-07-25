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

        static readonly Color[] BodyColors =
        {
            new Color(0.82f, 0.28f, 0.26f), new Color(0.25f, 0.45f, 0.78f),
            new Color(0.93f, 0.78f, 0.22f), new Color(0.30f, 0.62f, 0.40f),
            new Color(0.85f, 0.85f, 0.87f), new Color(0.28f, 0.30f, 0.34f),
            new Color(0.75f, 0.45f, 0.20f), new Color(0.55f, 0.35f, 0.65f),
        };

        static Color ColorFor(int id)
            => BodyColors[((id % BodyColors.Length) + BodyColors.Length) % BodyColors.Length];

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
            var body = ColorFor(id);
            bool tall = kind == "truck" || kind == "bus";
            var rig = CarView.BuildStandard(parent, $"{kind}_{id}", veh, body, body * 0.82f,
                                            bodyH: tall ? 1.5f : 0.62f,
                                            cabHeight: tall ? 0.9f : 0.55f,
                                            cabLenFrac: tall ? 0.42f : 0.5f,
                                            wheelR: tall ? 0.44f : 0.34f);
            rig.Root.name = $"Traffic_{id}";
            return rig;
        }

        sealed class PedView
        {
            public Transform Root, Torso, Head, LegL, LegR, ArmL, ArmR, Phone;
        }

        /// <summary>
        /// Pedestrian: a jointed stick figure, not a box. The walk cycle and the raised
        /// phone are what make the crowd read as reacting to you rather than decorating
        /// the pavement — and reacting to you is the whole shame system (§10).
        /// </summary>
        static PedView MakePed(Transform parent, int index)
        {
            var rng = new Rng((uint)(index * 2654435761u + 17u));
            var skin = new Color(0.86f, 0.72f, 0.58f);
            var shirt = Color.HSVToRGB((float)rng.Next(), 0.45f, 0.75f);
            var trousers = new Color(0.20f, 0.24f, 0.32f);

            var root = new GameObject("Ped").transform;
            root.SetParent(parent, false);

            var v = new PedView { Root = root };
            v.Torso = Geo.Box("Torso", root, new Vector3(0.36f, 0.56f, 0.24f),
                              new Vector3(0, 1.05f, 0), MatLib.Solid(shirt)).transform;
            v.Head = Geo.Box("Head", root, new Vector3(0.24f, 0.26f, 0.24f),
                             new Vector3(0, 1.46f, 0), MatLib.Solid(skin)).transform;
            Geo.Box("Hair", root, new Vector3(0.26f, 0.08f, 0.26f),
                    new Vector3(0, 1.58f, 0), MatLib.Solid(new Color(0.18f, 0.13f, 0.10f)));

            // limbs pivot at the hip and shoulder, so the box hangs below its own origin
            Transform Limb(string name, float x, float y, float len, Color c)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root, false);
                pivot.localPosition = new Vector3(x, y, 0);
                Geo.Box("Seg", pivot, new Vector3(0.12f, len, 0.13f), new Vector3(0, -len / 2f, 0),
                        MatLib.Solid(c));
                return pivot;
            }

            v.LegL = Limb("LegL", -0.10f, 0.78f, 0.78f, trousers);
            v.LegR = Limb("LegR", 0.10f, 0.78f, 0.78f, trousers);
            v.ArmL = Limb("ArmL", -0.23f, 1.28f, 0.52f, shirt);
            v.ArmR = Limb("ArmR", 0.23f, 1.28f, 0.52f, shirt);

            v.Phone = Geo.Box("Phone", v.ArmR, new Vector3(0.07f, 0.13f, 0.02f),
                              new Vector3(0, -0.56f, 0.04f),
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
            if (rig.BrakeLight == null) return;
            rig.BrakeLight.SetColor("_EmissionColor",
                new Color(1f, 0.23f, 0.19f) * (on ? 2.4f : 0.35f));
        }
    }
}
