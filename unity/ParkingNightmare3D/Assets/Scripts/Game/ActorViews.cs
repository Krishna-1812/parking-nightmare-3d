using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Greybox visuals for traffic cars, cross traffic and pedestrians.
    ///
    /// The simulation owns all state; this only mirrors it into transforms. Car body
    /// colour is chosen here from the car's id — deliberately, because the reference
    /// draws that colour from the same RNG the AI uses, and reproducing a purely visual
    /// draw inside the simulation would put render concerns into the deterministic
    /// stream for no benefit. See the note in TrafficSystem.Dims.
    /// </summary>
    public sealed class ActorViews : MonoBehaviour
    {
        public MissionRun Run;

        readonly Dictionary<int, Transform> _cars = new();
        readonly Dictionary<int, Transform> _crossers = new();
        readonly List<Transform> _peds = new();
        readonly HashSet<int> _seen = new();

        Transform _root;

        static readonly Color[] BodyColors =
        {
            new Color(0.82f, 0.28f, 0.26f), new Color(0.25f, 0.45f, 0.78f),
            new Color(0.93f, 0.78f, 0.22f), new Color(0.30f, 0.62f, 0.40f),
            new Color(0.85f, 0.85f, 0.87f), new Color(0.28f, 0.30f, 0.34f),
            new Color(0.75f, 0.45f, 0.20f), new Color(0.55f, 0.35f, 0.65f),
        };

        void Awake()
        {
            _root = new GameObject("PN3D_Actors").transform;
            _root.SetParent(transform, false);
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        static GameObject Box(string name, Transform parent, Vector3 size, Vector3 pos, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = pos;
            Destroy(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
            return go;
        }

        Transform MakeCarView(int id, double len, double wid)
        {
            var go = new GameObject($"Traffic_{id}");
            go.transform.SetParent(_root, false);
            var body = BodyColors[((id % BodyColors.Length) + BodyColors.Length) % BodyColors.Length];
            Box("Hull", go.transform, new Vector3((float)wid, 0.62f, (float)len),
                new Vector3(0, 0.42f, 0), body);
            Box("Cab", go.transform, new Vector3((float)wid * 0.86f, 0.5f, (float)len * 0.46f),
                new Vector3(0, 0.94f, -(float)len * 0.05f), body * 0.82f);
            return go.transform;
        }

        Transform MakePedView()
        {
            var go = new GameObject("Ped");
            go.transform.SetParent(_root, false);
            Box("Body", go.transform, new Vector3(0.38f, 0.72f, 0.28f), new Vector3(0, 0.68f, 0),
                new Color(0.28f, 0.42f, 0.62f));
            Box("Head", go.transform, new Vector3(0.26f, 0.26f, 0.26f), new Vector3(0, 1.2f, 0),
                new Color(0.86f, 0.72f, 0.58f));
            return go.transform;
        }

        /// <summary>
        /// One-shot spawn of the actor boxes at their current positions, for edit-mode
        /// tooling (screenshot capture) where there is no update loop running.
        /// </summary>
        public static void SpawnStatic(MissionRun run, Transform parent)
        {
            var root = new GameObject("PN3D_Actors_Static").transform;
            root.SetParent(parent, false);

            foreach (var car in run.Traffic.Cars)
                PlaceStaticCar(root, car.Id, car.Len, car.Wid, car.X, car.Y, car.H);

            foreach (var kv in run.Traffic.Crossers)
                foreach (var cr in kv.Value)
                    PlaceStaticCar(root, cr.Id, cr.Len, cr.Wid, cr.X, cr.Y, cr.H);

            foreach (var ped in run.Peds.List)
            {
                var go = new GameObject("Ped");
                go.transform.SetParent(root, false);
                StaticBox("Body", go.transform, new Vector3(0.38f, 0.72f, 0.28f),
                          new Vector3(0, 0.68f, 0), new Color(0.28f, 0.42f, 0.62f));
                StaticBox("Head", go.transform, new Vector3(0.26f, 0.26f, 0.26f),
                          new Vector3(0, 1.2f, 0), new Color(0.86f, 0.72f, 0.58f));
                go.transform.position = WorldBuilder.ToWorld(ped.X, ped.Y,
                    ped.State == PedState.Dive ? 0.3 : 0.0);
                go.transform.rotation = WorldBuilder.ToRotation(ped.Face)
                    * Quaternion.Euler(ped.State == PedState.Dive ? 62f : 0f, 0, 0);
            }
        }

        static void PlaceStaticCar(Transform root, int id, double len, double wid,
                                   double x, double y, double h)
        {
            var go = new GameObject($"Traffic_{id}");
            go.transform.SetParent(root, false);
            var body = BodyColors[((id % BodyColors.Length) + BodyColors.Length) % BodyColors.Length];
            StaticBox("Hull", go.transform, new Vector3((float)wid, 0.62f, (float)len),
                      new Vector3(0, 0.42f, 0), body);
            StaticBox("Cab", go.transform, new Vector3((float)wid * 0.86f, 0.5f, (float)len * 0.46f),
                      new Vector3(0, 0.94f, -(float)len * 0.05f), body * 0.82f);
            go.transform.position = WorldBuilder.ToWorld(x, y);
            go.transform.rotation = WorldBuilder.ToRotation(h);
        }

        static void StaticBox(string name, Transform parent, Vector3 size, Vector3 pos, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = pos;
            DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        }

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
                if (!_cars.TryGetValue(car.Id, out var tr) || tr == null)
                {
                    tr = MakeCarView(car.Id, car.Len, car.Wid);
                    _cars[car.Id] = tr;
                }
                tr.gameObject.SetActive(true);
                double x = MathX.Lerp(car.Px, car.X, alpha);
                double y = MathX.Lerp(car.Py, car.Y, alpha);
                double h = car.Ph + MathX.AngNorm(car.H - car.Ph) * alpha;
                tr.position = WorldBuilder.ToWorld(x, y);
                tr.rotation = WorldBuilder.ToRotation(h);
            }
            foreach (var kv in _cars)
                if (!_seen.Contains(kv.Key) && kv.Value != null) kv.Value.gameObject.SetActive(false);

            // ---- cross traffic ----
            _seen.Clear();
            foreach (var kv in Run.Traffic.Crossers)
            {
                foreach (var cr in kv.Value)
                {
                    _seen.Add(cr.Id);
                    if (!_crossers.TryGetValue(cr.Id, out var tr) || tr == null)
                    {
                        tr = MakeCarView(cr.Id, cr.Len, cr.Wid);
                        tr.name = $"Cross_{cr.Id}";
                        _crossers[cr.Id] = tr;
                    }
                    tr.gameObject.SetActive(true);
                    tr.position = WorldBuilder.ToWorld(cr.X, cr.Y);
                    tr.rotation = WorldBuilder.ToRotation(cr.H);
                }
            }
            foreach (var kv in _crossers)
                if (!_seen.Contains(kv.Key) && kv.Value != null) kv.Value.gameObject.SetActive(false);

            // ---- pedestrians ----
            var list = Run.Peds.List;
            while (_peds.Count < list.Count) _peds.Add(MakePedView());
            for (int i = 0; i < list.Count; i++)
            {
                var ped = list[i];
                double x = MathX.Lerp(ped.Px, ped.X, alpha);
                double y = MathX.Lerp(ped.Py, ped.Y, alpha);
                float bob = Mathf.Abs(Mathf.Sin((float)ped.Phase)) * 0.04f;
                float lift = ped.State == PedState.Dive ? 0.3f : 0f;
                _peds[i].position = WorldBuilder.ToWorld(x, y, bob + lift);
                _peds[i].rotation = WorldBuilder.ToRotation(ped.Face)
                                  * Quaternion.Euler(ped.State == PedState.Dive ? 62f : 0f, 0, 0);
            }
        }
    }
}
