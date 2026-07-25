using UnityEngine;
using UnityEngine.Rendering;
using PN3D.Core;
using PN3D.Game.Art;

namespace PN3D.Game
{
    /// <summary>
    /// Builds the world for a mission: environment, road corridor, scenery, parking spot
    /// and the player's car.
    ///
    /// The geometry and materials are generated at load rather than imported, because the
    /// web build generates its own too — porting the painters keeps both builds looking
    /// like the same game and keeps the repo free of binary art. See
    /// <see cref="PN3D.Game.Art.ProcTex"/> for the reasoning in full.
    ///
    /// Static and side-effect-free apart from the objects it creates, so the editor
    /// capture tool can call it outside play mode.
    /// </summary>
    public static class WorldBuilder
    {
        // Physics (x, y) -> Unity (x, elev, -y), heading -> yaw 90 + h.
        //
        // The Z NEGATION is load-bearing and is not what DESIGN_SPEC §2 originally said.
        // Mapping y -> +Z with yaw 90 - h does give the car a correct forward vector
        // — (cos h, 0, sin h) either way — but it mirrors the whole world, because Unity
        // is left-handed and Three.js is not. Concretely: a camera looking along +X with
        // up +Y has right = -Z in Unity (yaw +90 sends +Z to +X and +X to -Z), but
        // right = forward x up = +Z in Three.js. So +t, defined as "right side of travel
        // direction, the side the player drives on", ends up on screen-left in Unity and
        // screen-right in the web build. The game becomes left-hand-traffic: the player
        // drives on the wrong side and every curbside parallel spot appears on the wrong
        // side of the road.
        //
        // Negating Z restores chirality, and yaw 90 + h keeps forward consistent with it:
        // yaw sends +Z to (sin θ, 0, cos θ), and sin(90+h) = cos h, cos(90+h) = -sin h,
        // which is exactly the mapped velocity (cos h, 0, -sin h).
        //
        // Two consequences that bite elsewhere: triangle winding flips with the mirror
        // (see RoadBuilder.Strip), and in a car's local frame +X is its right-hand side.
        public static Vector3 ToWorld(double x, double y, double elev = 0.0)
            => new Vector3((float)x, (float)elev, -(float)y);

        public static Quaternion ToRotation(double h)
            => Quaternion.Euler(0f, 90f + (float)(h * Mathf.Rad2Deg), 0f);

        public sealed class Built
        {
            public GameObject Root;
            public Transform Car;
            public Transform CarBody;
            public GameObject SpotMarker;
            public CarView.Rig CarRig;
            public District District;
            public Light Sun;
        }

        /// <summary>
        /// Layout randomness is seeded from the mission id, so a mission always dresses
        /// itself the same way — the reference does the same with its own layout stream
        /// (<c>World.rng</c>, src/n3_d.js:580), and a street that reshuffles between runs
        /// makes par times feel arbitrary.
        /// </summary>
        static uint LayoutSeed(Mission m) => (uint)(0x9E3779B9u ^ (uint)(m.Id * 2654435761u));

        public static Built Build(MissionRun run, Transform parent = null)
        {
            var route = run.Route;

            var root = new GameObject("PN3D_World");
            if (parent != null) root.transform.SetParent(parent, false);

            var district = LoadDistrict(run.Mission);

            // ---- ground ----
            // Sized to the route's bounding box plus a wide margin, so the lawn reaches the
            // fog in every direction and the horizon ring never shows daylight underneath.
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in route.Pts)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
            var centre = ToWorld((minX + maxX) / 2, (minY + maxY) / 2, 0.0);
            float span = Mathf.Max((float)(maxX - minX), (float)(maxY - minY))
                       + Art.SceneEnv.HorizonRadius * 2f;

            BuildGround(root.transform, district, centre, span);

            // ---- environment: sky, sun, fog, horizon, grade ----
            var sun = Art.SceneEnv.Build(district, centre, root.transform);
            PostFx.Build(district, root.transform);

            // ---- road corridor and dressing ----
            RoadBuilder.Build(route, run.Mission.Lanes, district, root.transform);
            Scenery.Build(route, run.Mission.Lanes, district, LayoutSeed(run.Mission), root.transform);

            var spotGo = RoadBuilder.BuildSpot(run.Spot, root.transform);

            // ---- car ----
            var carRoot = new GameObject("Car");
            carRoot.transform.SetParent(root.transform, false);
            var rig = CarView.BuildHatch(carRoot.transform, run.Veh);
            carRoot.transform.position = ToWorld(run.Car.X, run.Car.Y);
            carRoot.transform.rotation = ToRotation(run.Car.H);

            return new Built
            {
                Root = root,
                Car = carRoot.transform,
                CarBody = rig.Body,
                SpotMarker = spotGo,
                CarRig = rig,
                District = district,
                Sun = sun,
            };
        }

        static GameObject BuildGround(Transform parent, District d, Vector3 centre, float span)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;

            float h = span * 0.5f;
            var mesh = new Mesh { name = "ground" };
            mesh.SetVertices(new[]
            {
                new Vector3(-h, 0, -h), new Vector3(h, 0, -h),
                new Vector3(-h, 0,  h), new Vector3(h, 0,  h),
            });
            float reps = span / 26f;   // one grass tile per 26 m
            mesh.SetUVs(0, new[]
            {
                new Vector2(0, 0), new Vector2(reps, 0),
                new Vector2(0, reps), new Vector2(reps, reps),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MatLib.Textured("mat_ground",
                ProcTex.Grass(d.GroundAHex, d.GroundBHex), Color.white, Vector2.one, smoothness: 0.04f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        static District LoadDistrict(Mission m)
        {
            string json = DataPaths.Load("districts.json");
            if (json != null) return District.Load(json, m.District);

            // A missing palette would silently produce a grey world that looks like a bug
            // in the lighting rather than a missing file, so say what happened.
            Debug.LogError("[PN3D] districts.json not found — falling back to the suburbs palette");
            return District.Load(FallbackSuburbs, 0);
        }

        const string FallbackSuburbs = @"{""0"":{""name"":""SLEEPY SUBURBS"",""tag"":""D1"",
            ""sky"":[""#7cc4f0"",""#b8e4fa"",""#ffedc9""],""fog"":""#cfe6ef"",""fogFar"":300,
            ""hemi"":[""#cfe8ff"",""#8fa876"",1],""sun"":[""#fff2d8"",2.4,[60,90,30]],
            ""ground"":[""#7fb069"",""#94c07d""],""night"":false,
            ""bWall"":[""#f2e3c9"",""#e8cfd8"",""#d8e8cf"",""#f7d9b8"",""#e0e8f0""],""bWin"":""#7ea8c4"",
            ""houses"":true,""treeEvery"":14,""lampEvery"":0,""birds"":true}}";

        /// <summary>
        /// Retained so callers that only want light (the capture tool's warm-up pass) keep
        /// working. The full environment now comes from <see cref="Art.SceneEnv"/>.
        /// </summary>
        public static GameObject BuildLighting(Transform parent = null)
        {
            var d = District.Load(FallbackSuburbs, 0);
            var sun = Art.SceneEnv.Build(d, Vector3.zero, parent);
            return sun.gameObject;
        }
    }
}
