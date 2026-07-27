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

        static readonly Color[] Jackets =
        {
            new Color(0.17f, 0.19f, 0.24f), new Color(0.33f, 0.26f, 0.20f),
            new Color(0.20f, 0.28f, 0.24f), new Color(0.42f, 0.16f, 0.16f),
            new Color(0.55f, 0.52f, 0.46f),
        };

        /// <summary>An egg, as a surface of revolution.</summary>
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
        /// A torso: broad across the chest, drawn in at the waist, flaring again at the
        /// hips. The waist is the point — a barrel with arms is a snowman, and a straight
        /// taper is a bollard.
        /// </summary>
        static Mesh Trunk(string key, float half) => Geo.Lathe(key, new[]
        {
            new Vector2(0.001f, -half),
            new Vector2(0.140f, -half * 0.94f),
            new Vector2(0.132f, -half * 0.40f),   // waist
            new Vector2(0.158f, half * 0.20f),
            new Vector2(0.166f, half * 0.60f),    // chest
            new Vector2(0.140f, half * 0.92f),
            new Vector2(0.001f, half),
        }, 12);

        /// <summary>
        /// A skull: brow shelf, cheek, a jaw that narrows to a chin. Eight radii is not
        /// many, but the profile of a head is most of what makes it a head rather than an
        /// egg, and the profile is the part a silhouette shows.
        ///
        /// The radii are HALF-WIDTHS, and they are 0.075 at the widest because an adult
        /// head is about 150 mm across. The first version used 0.097, which is a 194 mm
        /// skull — that alone would have kept the crowd looking wrong no matter what was
        /// added to the face, and it is the sort of error that is invisible until you
        /// write the number down next to the real one.
        ///
        /// Depth comes from the node scale, not from here: a head is deeper than it is
        /// wide (about 195 mm) and a surface of revolution cannot be.
        /// </summary>
        static Mesh Skull(float k) => Geo.Lathe($"skull{k:0.00}", new[]
        {
            new Vector2(0.001f, -0.112f * k),              // under the chin
            new Vector2(0.040f * k, -0.096f * k),
            new Vector2(0.059f * k, -0.058f * k),          // jaw
            new Vector2(0.071f * k, -0.011f * k),          // cheekbone
            new Vector2(0.075f * k, 0.031f * k),           // brow
            new Vector2(0.072f * k, 0.069f * k),
            new Vector2(0.048f * k, 0.101f * k),
            new Vector2(0.001f, 0.112f * k),
        }, 12);

        /// <summary>
        /// A tapered limb segment, with the dimensions rounded before the mesh cache sees
        /// them.
        ///
        /// <see cref="Geo.Cylinder"/> keys its cache on its arguments, and every dimension
        /// on a pedestrian is derived from a height and a girth drawn off a continuum — so
        /// without this every single person in the crowd mints eight meshes nobody else
        /// will ever use, and the cache holds them for the life of the domain. This is the
        /// same leak the conifer tiers had. Four millimetres and a centimetre are far below
        /// what anyone can see on an arm.
        /// </summary>
        static float Quant(float v, float step) => Mathf.Round(v / step) * step;

        static Mesh Seg(float r0, float r1, float len)
            => Geo.Cylinder(Mathf.Round(r0 * 250f) / 250f,
                            Mathf.Round(r1 * 250f) / 250f,
                            Mathf.Round(len * 100f) / 100f, 8);

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

            // ---- who this person is ----
            bool child = rng.Chance(0.11);
            // Adults between about five foot one and six foot two. A crowd where everyone
            // is the same height is the first thing that reads as cloned, well before any
            // individual figure does.
            // Both are quantised, and that is a cache decision rather than an art one:
            // nearly every mesh on the figure is derived from these two numbers, so drawn
            // off a continuum each pedestrian would mint a set nobody else ever reuses.
            // Four centimetres of height and six percent of girth are steps no one can see
            // in a crowd.
            float H = Quant(child ? (float)rng.Rand(1.06, 1.34) : (float)rng.Rand(1.55, 1.88), 0.04f);
            // Girth multiplies every radius. Children are proportionally chunkier.
            float girth = Quant((float)rng.Rand(0.86, 1.24) * (child ? 1.12f : 1f), 0.06f);
            // A child's head is a much larger fraction of them — this one ratio does more
            // for reading age than anything else on the model.
            float headK = child ? 1.34f : 1f;

            var skinC = Skins[(int)(rng.Next() * Skins.Length)];
            var shirtC = Shirts[(int)(rng.Next() * Shirts.Length)];
            var trouserC = Trousers[(int)(rng.Next() * Trousers.Length)];
            var hairC = Hairs[(int)(rng.Next() * Hairs.Length)];

            int sleeves = (int)(rng.Next() * 3);        // 0 long, 1 short, 2 none
            int legwear = (int)(rng.Next() * 3);        // 0 trousers, 1 shorts, 2 skirt
            bool jacket = !child && rng.Chance(0.24);
            int hairStyle = (int)(rng.Next() * 5);      // 0 crop 1 bob 2 tail 3 bun 4 thin
            bool cap = rng.Chance(0.13);
            bool bag = rng.Chance(0.22);

            var torsoC = jacket ? Jackets[(int)(rng.Next() * Jackets.Length)] : shirtC;

            var skin = MatLib.Skin(skinC);
            var shirt = MatLib.Solid(shirtC, 0.10f);
            var body = MatLib.Solid(torsoC, jacket ? 0.16f : 0.10f);
            var trousers = MatLib.Solid(trouserC, 0.08f);
            var hair = MatLib.Solid(hairC, 0.22f);
            var shoe = MatLib.Solid(new Color(0.13f, 0.12f, 0.12f), 0.28f);
            var dark = MatLib.Solid(new Color(0.16f, 0.15f, 0.15f), 0.18f);

            // ---- the skeleton, in fractions of standing height ----
            // These are anthropometric, not invented: hip at 0.530, knee 0.285, shoulder
            // 0.818, chin 0.870, eyes 0.935. Getting them right is most of the difference
            // between a person and a doll, and none of it costs a triangle.
            float hipY = 0.530f * H, kneeY = 0.285f * H, ankleY = 0.045f * H;
            float shoulderY = 0.818f * H, elbowY = 0.632f * H, wristY = 0.480f * H;
            float chinY = 0.870f * H, headTop = 1.000f * H;
            float thigh = hipY - kneeY, calf = kneeY - ankleY;
            float upperArm = shoulderY - elbowY, forearm = elbowY - wristY;
            float headH = (headTop - chinY) * headK;
            float headMid = chinY + headH * 0.5f;
            float hipHalf = 0.114f * H * girth;

            var root = new GameObject("Ped").transform;
            root.SetParent(parent, false);
            var v = new PedView { Root = root, Height = H };

            // ---- trunk ----
            // 0.52, not 0.62. At 0.62 the top of the trunk reached 1.497 m on a 1.72 m
            // figure — which is the chin — so the shirt swallowed the neck entirely and
            // the head sat straight on the shoulders. A visible neck is worth more than it
            // sounds: it is what lets the head read as a separate thing that could turn.
            float trunkHalf = (shoulderY - hipY) * 0.52f;
            float trunkMid = (shoulderY + hipY) * 0.5f + trunkHalf * 0.06f;
            v.Torso = Geo.Node("Torso", root, Trunk($"trunk{trunkHalf:0.000}", trunkHalf), body,
                               new Vector3(0, trunkMid, 0), Quaternion.identity,
                               // squashed front to back: a person is wider than they are deep
                               new Vector3(girth * H / 1.72f, 1f, girth * 0.66f * H / 1.72f))
                      .transform;

            Geo.Node("Pelvis", root, Seg(0.140f, 0.152f, 0.15f * H / 1.72f),
                     legwear == 2 ? MatLib.Solid(trouserC, 0.08f) : trousers,
                     new Vector3(0, hipY + 0.035f * H, 0), Quaternion.identity,
                     new Vector3(girth * H / 1.72f, 1f, girth * 0.74f * H / 1.72f));

            // Shoulders as their own mass. Without them the arms grow straight out of the
            // ribcage and the figure has no yoke — which is the shape a shirt actually has.
            float shWidth = 0.235f * H * girth;
            Geo.Node("Shoulders", root, Seg(0.060f, 0.060f, shWidth * 0.70f), body,
                     new Vector3(0, shoulderY + 0.010f * H, 0), Quaternion.Euler(0, 0, 90f),
                     new Vector3(H / 1.72f * girth, 1f, 0.78f * H / 1.72f * girth));
            // Deltoids, sat exactly on the ends of that cylinder. A cylinder laid across
            // the body terminates in two flat discs, and those discs catch the light as
            // hard bright rectangles from any three-quarter angle — the red slabs sticking
            // out of the figure's shoulders in the first render were nothing but the caps.
            foreach (float sx in new[] { -1f, 1f })
                Geo.Node("Deltoid", root, Geo.Pebble, body,
                         new Vector3(sx * shWidth * 0.35f, shoulderY + 0.004f * H, 0),
                         Quaternion.identity,
                         new Vector3(0.078f * H * girth, 0.072f * H, 0.070f * H * girth));

            if (jacket)
                // an open jacket: a strip of the shirt underneath, down the front
                Geo.Box("Placket", root, new Vector3(0.085f * H, trunkHalf * 1.5f, 0.02f),
                        new Vector3(0, trunkMid - trunkHalf * 0.15f,
                                    0.108f * H * girth), shirt);

            if (legwear != 2)
                // 0.175 of height across and 0.115 deep, which is a 300 mm waist on a
                // 1.72 m adult. The first pass had 0.28 and 0.20 — a 480 mm belt standing
                // 340 mm proud of the body, which is what the shelf sticking out of the
                // figure's middle in the first render was.
                Geo.Box("Belt", root, new Vector3(0.175f * H * girth, 0.020f * H,
                                                  0.115f * H * girth),
                        new Vector3(0, hipY + 0.072f * H, 0), dark);
            else
                // a skirt: a flared cone from the waist to mid-thigh
                Geo.Node("Skirt", root, Seg(0.145f, 0.215f, 0.19f * H),
                         MatLib.Solid(trouserC, 0.10f),
                         new Vector3(0, hipY - 0.045f * H, 0), Quaternion.identity,
                         new Vector3(girth * H / 1.72f, 1f, girth * 0.80f * H / 1.72f));

            Geo.Node("Neck", root, Seg(0.042f, 0.048f, 0.055f * H), skin,
                     new Vector3(0, chinY - 0.030f * H, 0));

            // ---- head ----
            // headK quantised too, so a child's skull shares a mesh with every other
            // child's. The z scale is the one thing a lathe cannot give: a head is about
            // 195 mm front to back against 150 mm across, and a surface of revolution is
            // round by definition.
            float headScale = Quant(headH / 0.224f, 0.05f);
            v.Head = Geo.Node("Head", root, Skull(headScale), skin,
                              new Vector3(0, headMid, 0), Quaternion.identity,
                              new Vector3(1f, 1f, 1.30f)).transform;
            Face(v.Head, headScale, skin, hair, dark, hairStyle, cap, rng);

            // ---- limbs ----
            // Every limb is two segments with a joint between them, and the joint is not
            // decoration: a leg that bends only at the hip swings like a pendulum, which is
            // exactly what a shop mannequin on a turntable does.
            Transform Limb(string name, float x, float topY, float lenA, float lenB,
                           float rTop, float rMid, float rEnd,
                           Material matA, Material matB, bool foot, out Transform joint)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root, false);
                pivot.localPosition = new Vector3(x, topY, 0);
                Geo.Node("Upper", pivot, Seg(rTop, rMid, lenA), matA,
                         new Vector3(0, -lenA / 2f, 0));

                joint = new GameObject(name + "Joint").transform;
                joint.SetParent(pivot, false);
                joint.localPosition = new Vector3(0, -lenA, 0);
                Geo.Node("Lower", joint, Seg(rMid, rEnd, lenB), matB,
                         new Vector3(0, -lenB / 2f, 0));

                if (foot)
                {
                    // heel, arch and toe rather than a slab: a leg that ends in a flat cut
                    // is the loudest "placeholder" signal on the whole figure
                    Geo.Box("Heel", joint, new Vector3(0.085f * H, 0.045f * H, 0.075f * H),
                            new Vector3(0, -lenB - 0.018f * H, -0.020f * H), shoe);
                    Geo.Box("Foot", joint, new Vector3(0.082f * H, 0.036f * H, 0.145f * H),
                            new Vector3(0, -lenB - 0.026f * H, 0.045f * H), shoe);
                    Geo.Node("Toe", joint, Geo.Pebble, shoe,
                             new Vector3(0, -lenB - 0.026f * H, 0.115f * H),
                             Quaternion.identity,
                             new Vector3(0.082f * H, 0.030f * H, 0.055f * H));
                }
                else
                {
                    Geo.Node("Hand", joint, Geo.Pebble, skin,
                             new Vector3(0, -lenB - 0.018f * H, 0), Quaternion.identity,
                             new Vector3(0.042f * H, 0.052f * H, 0.019f * H));
                }
                return pivot;
            }

            var thighMat = legwear == 2 ? skin : trousers;
            var calfMat = legwear == 0 ? trousers : skin;
            var upperArmMat = sleeves == 2 ? skin : body;
            var foreArmMat = sleeves == 0 ? body : skin;

            // Limb radii, checked against a tape measure rather than chosen by eye. On a
            // 1.72 m adult these come out at a 90 mm upper arm, a 56 mm wrist, a 150 mm
            // thigh and a 76 mm ankle. The first pass was about forty percent over on
            // every one of them, which is not a look — it is a inflatable costume.
            float legX = 0.048f * H * girth;
            float armX = shWidth * 0.44f;
            float rThigh = 0.044f * H * girth, rKnee = 0.030f * H * girth,
                  rAnkle = 0.022f * H * girth;
            float rShoulder = 0.026f * H * girth, rElbow = 0.021f * H * girth,
                  rWrist = 0.016f * H * girth;

            v.LegL = Limb("LegL", -legX, hipY, thigh, calf, rThigh, rKnee, rAnkle,
                          thighMat, calfMat, true, out var kL);
            v.LegR = Limb("LegR", legX, hipY, thigh, calf, rThigh, rKnee, rAnkle,
                          thighMat, calfMat, true, out var kR);
            v.ArmL = Limb("ArmL", -armX, shoulderY, upperArm, forearm,
                          rShoulder, rElbow, rWrist, upperArmMat, foreArmMat, false, out var eL);
            v.ArmR = Limb("ArmR", armX, shoulderY, upperArm, forearm,
                          rShoulder, rElbow, rWrist, upperArmMat, foreArmMat, false, out var eR);
            v.KneeL = kL; v.KneeR = kR; v.ElbowL = eL; v.ElbowR = eR;

            if (sleeves == 1)
                // Parented to the ELBOW, not to the root. A cuff pinned to the body would
                // sit still while the arm it belongs to swung out from under it.
                foreach (var joint in new[] { eL, eR })
                    Geo.Node("Cuff", joint, Seg(rElbow * 1.22f, rElbow * 1.18f, 0.022f * H),
                             body, new Vector3(0, 0.008f * H, 0));

            if (bag)
            {
                // strap over one shoulder, bag on the opposite hip — the way one hangs
                float side = rng.Chance(0.5) ? 1f : -1f;
                var bagMat = MatLib.Solid(Jackets[(int)(rng.Next() * Jackets.Length)], 0.14f);
                var strap = Geo.Box("Strap", root, new Vector3(0.035f * H, 0.30f * H, 0.030f * H),
                                    new Vector3(side * 0.055f * H,
                                                (shoulderY + hipY) * 0.5f + 0.03f * H,
                                                0.055f * H * girth), bagMat);
                strap.transform.localRotation = Quaternion.Euler(0, 0, side * 16f);
                Geo.Box("Bag", root, new Vector3(0.20f * H, 0.20f * H, 0.075f * H),
                        new Vector3(-side * 0.135f * H, hipY + 0.02f * H,
                                    -0.055f * H * girth), bagMat);
            }

            v.Phone = Geo.Box("Phone", v.ElbowR, new Vector3(0.040f * H, 0.075f * H, 0.012f * H),
                              new Vector3(0, -forearm - 0.035f * H, 0.030f * H),
                              MatLib.Emissive(new Color(0.08f, 0.08f, 0.1f),
                                              new Color(0.6f, 0.8f, 1f), 0.8f)).transform;
            v.Phone.gameObject.SetActive(false);
            return v;
        }

        /// <summary>
        /// The face, and the hair on top of it.
        ///
        /// Everything here is small and none of it is optional. A head with no nose reads
        /// as a mannequin from any angle where the silhouette matters, and the silhouette
        /// always matters — the crowd is mostly seen in profile, walking past. Eyes get a
        /// white as well as a pupil because a dark dot alone reads as a hole.
        ///
        /// Every coordinate here is written against a standard 224 mm adult head and then
        /// multiplied by <paramref name="k"/>, so a child's face shrinks with their skull
        /// instead of a full-size nose arriving on a small head. Depth is NOT multiplied by
        /// the head node's 1.30 z stretch — these are children of that node, so they
        /// inherit it, and pre-multiplying would apply it twice.
        /// </summary>
        static void Face(Transform head, float k, Material skin, Material hair,
                         Material dark, int style, bool cap, Rng rng)
        {
            var sclera = MatLib.Solid(new Color(0.88f, 0.87f, 0.85f), 0.55f);
            var iris = MatLib.Solid(new Color(0.13f, 0.11f, 0.10f), 0.60f);

            Vector3 P(float x, float y, float z) => new Vector3(x * k, y * k, z * k);
            Vector3 S(float x, float y, float z) => new Vector3(x * k, y * k, z * k);

            // The eyes have to sit ON the skull, not inside it. The head node stretches z
            // by 1.30 and these are its children, so a coordinate of 0.056 lands at 0.073
            // — well behind a surface that is at 0.075 x 1.30. Buried eyes are why the
            // first face was a blank oval with two white specks showing through it.
            foreach (float ex in new[] { -0.030f, 0.030f })
            {
                Geo.Node("Sclera", head, Geo.Pebble, sclera,
                         P(ex, 0.014f, 0.068f), Quaternion.identity, S(0.026f, 0.015f, 0.013f));
                Geo.Node("Iris", head, Geo.Pebble, iris,
                         P(ex, 0.013f, 0.074f), Quaternion.identity, S(0.013f, 0.012f, 0.008f));
                // The eyebrow is hair and it is THIN. The first version put a nine
                // millimetre hair-coloured bar over each eye and the pair of them read as
                // one black band across the whole face.
                Geo.Box("Brow", head, S(0.030f, 0.005f, 0.008f),
                        P(ex, 0.030f, 0.070f), hair);
                Geo.Node("Ear", head, Geo.Pebble, skin,
                         P(Mathf.Sign(ex) * 0.070f, 0.002f, -0.006f), Quaternion.identity,
                         S(0.014f, 0.044f, 0.020f));
            }

            Geo.Node("Nose", head, Geo.Pebble, skin,
                     P(0, -0.008f, 0.072f), Quaternion.Euler(14f, 0, 0),
                     S(0.024f, 0.044f, 0.026f));
            Geo.Box("Mouth", head, S(0.030f, 0.006f, 0.008f),
                    P(0, -0.048f, 0.066f),
                    MatLib.Solid(new Color(0.44f, 0.24f, 0.22f), 0.30f));

            if (cap)
            {
                // A cap does come down to the brow, which is the difference between a cap
                // and hair.
                var capMat = MatLib.Solid(Shirts[(int)(rng.Next() * Shirts.Length)], 0.14f);
                Geo.Node("Cap", head, Dome("cap", 0.079f, 0.014f, k), capMat, Vector3.zero);
                Geo.Box("Peak", head, S(0.096f, 0.010f, 0.060f),
                        P(0, 0.024f, 0.080f), capMat);
                return;
            }

            // The hairline.
            //
            // A lathe is round, so it cannot start higher at the front than at the back —
            // and the first version started the dome at y = 0.002, two millimetres above
            // the eyes, which put hair over the entire forehead. It read as a helmet, or
            // worse, as one dark band across the top of a blank face.
            //
            // Pushing the whole dome back in z is the trick a surface of revolution allows:
            // the front edge rides up the curve of the skull and the back edge drops down
            // it, which is what a hairline does.
            Geo.Node("Hair", head, Dome(style == 4 ? "thin" : "crop",
                                        style == 4 ? 0.072f : 0.079f,
                                        style == 4 ? 0.062f : 0.042f, k),
                     hair, P(0, 0, style == 4 ? -0.016f : -0.010f));

            switch (style)
            {
                case 1:     // bob, down to the jaw
                    Geo.Box("Bob", head, S(0.160f, 0.118f, 0.082f),
                            P(0, -0.028f, -0.036f), hair);
                    break;
                case 2:     // ponytail
                    Geo.Node("Tail", head, Seg(0.026f * k, 0.015f * k, 0.135f * k), hair,
                             P(0, -0.028f, -0.086f), Quaternion.Euler(28f, 0, 0));
                    Geo.Node("Tie", head, Geo.Pebble, dark,
                             P(0, 0.034f, -0.066f), Quaternion.identity, S(0.026f, 0.026f, 0.026f));
                    break;
                case 3:     // bun
                    Geo.Node("Bun", head, Geo.Pebble, hair,
                             P(0, 0.064f, -0.062f), Quaternion.identity,
                             S(0.072f, 0.072f, 0.072f));
                    break;
            }
        }

        /// <summary>A skull cap: hair or a hat, sitting on the crown and not over the face.</summary>
        static Mesh Dome(string key, float r, float from, float k)
            => Geo.Lathe($"dome{key}{k:0.00}", new[]
            {
                new Vector2(r * k, from * k),
                new Vector2(r * 0.99f * k, 0.048f * k),
                new Vector2(r * 0.86f * k, 0.092f * k),
                new Vector2(r * 0.48f * k, 0.120f * k),
                new Vector2(0.001f, 0.132f * k),
            }, 10);

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
