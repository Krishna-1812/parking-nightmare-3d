using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Builds the greybox world for a mission: ground, road ribbon, sidewalks, centre
    /// line, the parking spot and the car. Deliberately flat untextured geometry —
    /// milestone step 5 is where this is replaced with real assets, and mixing that in
    /// now would hide whether the simulation is right.
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
        public static Vector3 ToWorld(double x, double y, double elev = 0.0)
            => new Vector3((float)x, (float)elev, -(float)y);

        public static Quaternion ToRotation(double h)
            => Quaternion.Euler(0f, 90f + (float)(h * Mathf.Rad2Deg), 0f);

        static Material Mat(Color c, float smoothness = 0.1f)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        /// <summary>
        /// A flat ribbon following the centreline between two lateral offsets. This is
        /// how the road, sidewalks and lane markings are all drawn.
        /// </summary>
        static Mesh Ribbon(CompiledRoute route, double tFrom, double tTo, double elev,
                           double sStart = 0.0, double sEnd = -1.0)
        {
            if (sEnd < 0) sEnd = route.Length;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            const double stride = 2.0;
            int steps = Mathf.Max(2, Mathf.CeilToInt((float)((sEnd - sStart) / stride)));

            for (int i = 0; i <= steps; i++)
            {
                double s = sStart + (sEnd - sStart) * i / steps;
                route.PosAt(s, tFrom, out double ax, out double ay, out _);
                route.PosAt(s, tTo, out double bx, out double by, out _);
                verts.Add(ToWorld(ax, ay, elev));
                verts.Add(ToWorld(bx, by, elev));
                float v = (float)(s / 8.0);
                uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(1, v));

                if (i > 0)
                {
                    // Winding matters: URP/Lit is single-sided, so getting this backwards
                    // makes every road surface face the ground and vanish from above.
                    // Note this order is only correct BECAUSE ToWorld negates Z — the
                    // mirror flips handedness, and therefore flips winding too. Change
                    // one of these and you must change the other.
                    int b = (i - 1) * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            var mesh = new Mesh { name = "ribbon" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(color, 0.25f);
            return go;
        }

        public sealed class Built
        {
            public GameObject Root;
            public Transform Car;
            public Transform CarBody;
            public GameObject SpotMarker;
        }

        public static Built Build(MissionRun run, Transform parent = null)
        {
            var route = run.Route;
            double rw = RoadGeom.HalfWidth(run.Mission.Lanes);
            double walk = rw + 0.35 + RoadGeom.SidewalkW;

            var root = new GameObject("PN3D_World");
            if (parent != null) root.transform.SetParent(parent, false);

            // ---- ground ----
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(ground.GetComponent<MeshCollider>());
            // centre it on the route's bounding box so it covers the whole level
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in route.Pts)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
            var centre = ToWorld((minX + maxX) / 2, (minY + maxY) / 2, -0.02);
            double span = Mathf.Max((float)(maxX - minX), (float)(maxY - minY)) + 240.0;
            ground.transform.position = centre;
            ground.transform.localScale = Vector3.one * (float)(span / 10.0); // Plane is 10 u
            ground.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.42f, 0.55f, 0.35f));

            // ---- sidewalks, road, lane markings ----
            MeshObject("Sidewalk_L", root.transform, Ribbon(route, -walk, -rw, 0.10), Mat(new Color(0.62f, 0.62f, 0.60f)));
            MeshObject("Sidewalk_R", root.transform, Ribbon(route, rw, walk, 0.10), Mat(new Color(0.62f, 0.62f, 0.60f)));
            MeshObject("Road", root.transform, Ribbon(route, -rw, rw, 0.04), Mat(new Color(0.20f, 0.20f, 0.22f)));
            MeshObject("CentreLine", root.transform, Ribbon(route, -0.09, 0.09, 0.05), Mat(new Color(0.85f, 0.80f, 0.35f)));

            // parking-strip edge line, so the curb reference the gap is measured to is visible
            double strip = rw - RoadGeom.ParkStrip;
            MeshObject("ParkLine_R", root.transform, Ribbon(route, strip - 0.07, strip + 0.07, 0.05),
                       Mat(new Color(0.80f, 0.80f, 0.82f)));

            // ---- parking spot ----
            var spot = run.Spot;
            var spotGo = new GameObject("ParkingSpot");
            spotGo.transform.SetParent(root.transform, false);
            spotGo.transform.position = ToWorld(spot.X, spot.Y, 0.07);
            spotGo.transform.rotation = ToRotation(spot.H);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "SpotPad";
            pad.transform.SetParent(spotGo.transform, false);
            Object.DestroyImmediate(pad.GetComponent<BoxCollider>());
            // spot half-length runs along the heading, which maps to local +Z here
            pad.transform.localScale = new Vector3((float)(spot.Hw * 2), 0.02f, (float)(spot.Hl * 2));
            pad.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.20f, 0.75f, 0.38f));

            // corner posts, so the box reads in 3D
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? 1 : -1;
                float sz = (i < 2) ? 1 : -1;
                var post = Box($"Post{i}", spotGo.transform, new Vector3(0.16f, 1.1f, 0.16f),
                               new Color(0.25f, 0.85f, 0.45f));
                post.transform.localPosition = new Vector3(sx * (float)spot.Hw, 0.55f, sz * (float)spot.Hl);
            }

            // ---- car ----
            var carRoot = new GameObject("Car");
            carRoot.transform.SetParent(root.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(carRoot.transform, false);

            var veh = run.Veh;
            // the mesh faces +Z, which the heading conversion expects
            var hull = Box("Hull", body.transform,
                           new Vector3((float)veh.Wid, (float)(veh.Hgt * 0.55), (float)veh.Len),
                           new Color(0.75f, 0.34f, 0.23f));
            hull.transform.localPosition = new Vector3(0, (float)(veh.Hgt * 0.32), 0);

            var roof = Box("Roof", body.transform,
                           new Vector3((float)(veh.Wid * 0.86), (float)(veh.Hgt * 0.42), (float)(veh.Len * 0.48)),
                           new Color(0.62f, 0.26f, 0.17f));
            roof.transform.localPosition = new Vector3(0, (float)(veh.Hgt * 0.74), -(float)(veh.Len * 0.06));

            // a nose marker, so facing is unambiguous in a screenshot
            var nose = Box("Nose", body.transform, new Vector3((float)(veh.Wid * 0.5), 0.12f, 0.3f),
                           new Color(0.95f, 0.85f, 0.4f));
            nose.transform.localPosition = new Vector3(0, (float)(veh.Hgt * 0.5), (float)(veh.Len * 0.5));

            carRoot.transform.position = ToWorld(run.Car.X, run.Car.Y);
            carRoot.transform.rotation = ToRotation(run.Car.H);

            return new Built
            {
                Root = root,
                Car = carRoot.transform,
                CarBody = body.transform,
                SpotMarker = spotGo,
            };
        }

        /// <summary>Sun + ambient, so the greybox reads without relying on a scene's lights.</summary>
        public static GameObject BuildLighting(Transform parent = null)
        {
            var go = new GameObject("PN3D_Sun");
            if (parent != null) go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.96f, 0.88f);
            l.intensity = 2.0f;
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.68f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.26f, 0.22f);
            return go;
        }
    }
}
