using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// A person, as one continuous skinned surface over a real skeleton.
    ///
    /// This replaces a pile of overlapping primitives, and the reason is structural rather
    /// than cosmetic. A body assembled from separate meshes has two faults that no amount
    /// of extra detail can cover:
    ///
    /// - **The seams show.** Where a deltoid sits on an upper arm there is a hard edge
    ///   between two closed surfaces, and the eye finds it instantly because nothing on a
    ///   real body has one. Detail added elsewhere makes it worse, not better: a
    ///   well-shaded object with a construction seam reads as a well-made toy.
    ///
    /// - **Nothing deforms.** Rotating a cylinder about a pivot is a hinge. A real elbow
    ///   keeps its skin continuous — the outside stretches, the inside gathers — and the
    ///   absence of that is most of what "mannequin" means. Skinning is the only fix;
    ///   there is no way to fake it with rigid parts.
    ///
    /// So: a bone hierarchy, a mesh lofted along it as rings of vertices, each vertex
    /// weighted to the two bones nearest it, and a SkinnedMeshRenderer. The animation code
    /// rotates exactly the same transforms it rotated before — it just now bends a surface
    /// instead of swinging a limb past a socket.
    ///
    /// Clothing is not separate geometry either. A shirt is the same lofted surface a few
    /// millimetres further out, in a different submesh, with the ring at the hem emitted
    /// twice so the edge is crisp. That is how it is done on real game characters at this
    /// budget, and it means a sleeve can never float off the arm inside it.
    ///
    /// About seven hundred and fifty vertices, which is what a crowd of thirty on a phone
    /// can afford.
    /// </summary>
    public static class Human
    {
        /// <summary>
        /// The joints the animation drives. Names follow the body, not Unity's humanoid
        /// rig — there is no avatar here and no retargeting, just transforms.
        /// </summary>
        public sealed class Rig
        {
            public Transform Root, Hips, Spine, Chest, Neck, Head;
            public Transform ArmL, ForeArmL, ArmR, ForeArmR;
            public Transform LegL, ShinL, LegR, ShinR;
            public Transform HandR;
            public float Height;
        }

        // ------------------------------------------------------------------ rings

        /// <summary>
        /// One cross-section of the body. A loft is a run of these; the mesh is the surface
        /// stitched between consecutive rings.
        /// </summary>
        struct Ring
        {
            public Vector3 C;          // centre, in bind pose
            public float Rx, Rz;       // half-width and half-depth
            public int B0, B1;         // the two bones this ring is weighted to
            public float W1;           // how much of it belongs to B1
            public int Sub;            // submesh, i.e. which material
            public float V;            // texture coordinate along the loft
        }

        sealed class Loft
        {
            public readonly List<Ring> Rings = new();
            public int Segs = 8;
            /// <summary>Head UVs wrap so u = 0.5 is dead centre front; limbs just tile.</summary>
            public bool FaceUV;
        }

        // ------------------------------------------------------------------ build

        /// <summary>
        /// Bone indices. Order matters only in that the bindposes array must match.
        /// </summary>
        const int BHips = 0, BSpine = 1, BChest = 2, BNeck = 3, BHead = 4,
                  BArmL = 5, BForeL = 6, BHandL = 7,
                  BArmR = 8, BForeR = 9, BHandR = 10,
                  BLegL = 11, BShinL = 12, BFootL = 13,
                  BLegR = 14, BShinR = 15, BFootR = 16,
                  BoneCount = 17;

        const int SubSkin = 0, SubFace = 1, SubShirt = 2, SubTrouser = 3, SubHair = 4,
                  SubShoe = 5, SubCount = 6;

        public static Rig Build(Transform parent, uint seed, out Material[] materials)
        {
            var rng = new Rng(seed);

            // ---- who ----
            bool child = rng.Chance(0.11);
            float H = Quant(child ? (float)rng.Rand(1.06, 1.34) : (float)rng.Rand(1.56, 1.88), 0.04f);
            float girth = Quant((float)rng.Rand(0.88, 1.22) * (child ? 1.10f : 1f), 0.06f);
            // A child's head is a much larger fraction of them. One ratio, and it does more
            // for reading age than any amount of modelling.
            float headK = child ? 1.30f : 1f;

            int skinIdx = (int)(rng.Next() * Palette.Skins.Length);
            var skinC = Palette.Skins[skinIdx];
            var hairC = Palette.Hairs[(int)(rng.Next() * Palette.Hairs.Length)];
            var shirtC = Palette.Shirts[(int)(rng.Next() * Palette.Shirts.Length)];
            var trouserC = Palette.Trousers[(int)(rng.Next() * Palette.Trousers.Length)];

            int sleeves = (int)(rng.Next() * 3);      // 0 long, 1 short, 2 vest
            int legwear = (int)(rng.Next() * 3);      // 0 trousers, 1 shorts, 2 skirt
            bool stubble = !child && rng.Chance(0.30);
            int hairStyle = (int)(rng.Next() * 5);

            // ---- skeleton, in fractions of standing height ----
            // Anthropometric, not invented. Getting these right costs nothing and is most
            // of the difference between a person and a doll.
            var bind = new Vector3[BoneCount];
            bind[BHips] = new Vector3(0, 0.530f * H, 0);
            bind[BSpine] = new Vector3(0, 0.610f * H, 0);
            bind[BChest] = new Vector3(0, 0.700f * H, 0);
            bind[BNeck] = new Vector3(0, 0.800f * H, 0);
            bind[BHead] = new Vector3(0, 0.855f * H, 0);

            // The shoulder joint has to sit OUTSIDE the ribcage. The torso is 0.099 of
            // height in half-width, so an arm axis at 0.078 is inside it — which is why
            // the first build had forearms emerging from the middle of the chest and hands
            // resting inside the hips. At 0.100 plus a 0.040 deltoid the figure measures
            // about 480 mm across the shoulders, which is an adult.
            float armX = 0.100f * H * Mathf.Lerp(1f, girth, 0.5f);
            bind[BArmL] = new Vector3(-armX, 0.800f * H, 0);
            bind[BForeL] = new Vector3(-armX, 0.625f * H, 0);
            bind[BHandL] = new Vector3(-armX, 0.475f * H, 0);
            bind[BArmR] = new Vector3(armX, 0.800f * H, 0);
            bind[BForeR] = new Vector3(armX, 0.625f * H, 0);
            bind[BHandR] = new Vector3(armX, 0.475f * H, 0);

            float legXb = 0.048f * H * Mathf.Lerp(1f, girth, 0.5f);
            bind[BLegL] = new Vector3(-legXb, 0.520f * H, 0);
            bind[BShinL] = new Vector3(-legXb, 0.285f * H, 0);
            bind[BFootL] = new Vector3(-legXb, 0.045f * H, 0);
            bind[BLegR] = new Vector3(legXb, 0.520f * H, 0);
            bind[BShinR] = new Vector3(legXb, 0.285f * H, 0);
            bind[BFootR] = new Vector3(legXb, 0.045f * H, 0);

            var root = new GameObject("Ped").transform;
            root.SetParent(parent, false);

            var bones = new Transform[BoneCount];
            Transform Bone(int i, string name, int parentBone)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parentBone < 0 ? root : bones[parentBone], false);
                t.localPosition = parentBone < 0 ? bind[i] : bind[i] - bind[parentBone];
                bones[i] = t;
                return t;
            }

            Bone(BHips, "Hips", -1);
            Bone(BSpine, "Spine", BHips);
            Bone(BChest, "Chest", BSpine);
            Bone(BNeck, "Neck", BChest);
            Bone(BHead, "Head", BNeck);
            Bone(BArmL, "ArmL", BChest); Bone(BForeL, "ForeArmL", BArmL); Bone(BHandL, "HandL", BForeL);
            Bone(BArmR, "ArmR", BChest); Bone(BForeR, "ForeArmR", BArmR); Bone(BHandR, "HandR", BForeR);
            Bone(BLegL, "LegL", BHips); Bone(BShinL, "ShinL", BLegL); Bone(BFootL, "FootL", BShinL);
            Bone(BLegR, "LegR", BHips); Bone(BShinR, "ShinR", BLegR); Bone(BFootR, "FootR", BShinR);

            // ---- the surface ----
            float g = girth;
            var lofts = new List<Loft>();

            // Torso, from the crotch to the top of the neck. The clothing is this same
            // surface pushed out a few millimetres; the hem rings are emitted twice so the
            // material edge is a crisp step rather than a smear.
            float hemT = 0.006f * H;
            var torso = new Loft { Segs = 12 };
            void T(float y, float rx, float rz, int b0, int b1, float w1, int sub, float pad = 0f)
                => torso.Rings.Add(new Ring
                {
                    C = new Vector3(0, y * H, 0), Rx = (rx * g + pad) * H, Rz = (rz * g + pad) * H,
                    B0 = b0, B1 = b1, W1 = w1, Sub = sub,
                });

            int torsoSub = legwear == 2 ? SubTrouser : SubTrouser;
            T(0.500f, 0.005f, 0.004f, BHips, BHips, 0f, torsoSub);
            T(0.512f, 0.088f, 0.062f, BHips, BHips, 0f, torsoSub);
            T(0.545f, 0.098f, 0.068f, BHips, BHips, 0f, torsoSub);
            T(0.578f, 0.092f, 0.064f, BHips, BSpine, 0.35f, torsoSub);       // waistband
            T(0.578f, 0.086f, 0.060f, BHips, BSpine, 0.35f, SubShirt, hemT); // shirt hem
            T(0.620f, 0.083f, 0.058f, BSpine, BSpine, 0f, SubShirt, hemT);
            T(0.665f, 0.090f, 0.062f, BSpine, BChest, 0.45f, SubShirt, hemT);
            T(0.710f, 0.097f, 0.066f, BChest, BChest, 0f, SubShirt, hemT);   // chest
            T(0.755f, 0.099f, 0.064f, BChest, BChest, 0f, SubShirt, hemT);
            T(0.782f, 0.104f, 0.060f, BChest, BNeck, 0.25f, SubShirt, hemT); // shoulder yoke
            T(0.798f, 0.062f, 0.050f, BChest, BNeck, 0.35f, SubShirt, hemT); // collar
            // A neck is about 115 mm across. These are HALF-widths, so 0.034 of a 1.72 m
            // height is 58 mm — the first version used 0.040 and put a 138 mm column of
            // skin up past the chin, which is as wide as the head and swallowed it.
            T(0.802f, 0.036f, 0.034f, BChest, BNeck, 0.55f, SubSkin);        // neck
            T(0.840f, 0.033f, 0.031f, BNeck, BNeck, 0f, SubSkin);
            T(0.866f, 0.030f, 0.029f, BNeck, BHead, 0.70f, SubSkin);         // under the jaw
            lofts.Add(torso);

            // Head. Its own submesh and its own UV wrap, so a painted face lands where the
            // face is — u = 0.5 is dead centre front and the seam falls at the back.
            var head = new Loft { Segs = 14, FaceUV = true };
            // hh is the HALF height. A head is 0.130 of standing height overall, so half of
            // it is 0.065 — the first version passed the whole thing and produced a
            // 450 mm skull, which is the wedge in the first render of this rig. The centre
            // then follows from the chin at 0.870, not the other way round, so a child's
            // larger head grows upward off the same jaw instead of sinking into the chest.
            float hh = 0.065f * H * headK;
            float hc = 0.870f * H + hh;
            float hw = 0.0435f * H * headK;
            void Hd(float t, float rx, float rz)
                => head.Rings.Add(new Ring
                {
                    C = new Vector3(0, hc + (t - 0.5f) * 2f * hh, 0),
                    Rx = rx * hw, Rz = rz * hw * 1.30f,
                    B0 = BHead, B1 = BHead, W1 = 0f, Sub = SubFace,
                });
            Hd(0.00f, 0.02f, 0.02f);
            // A jaw is very nearly as wide as the neck under it. Narrower, and the neck
            // pokes out below the chin as a pale collar.
            Hd(0.07f, 0.56f, 0.62f);      // under the chin
            Hd(0.20f, 0.82f, 0.92f);      // jaw
            Hd(0.36f, 0.90f, 1.02f);      // cheek
            Hd(0.52f, 0.99f, 1.06f);      // brow
            Hd(0.68f, 1.00f, 1.04f);
            Hd(0.84f, 0.88f, 0.90f);
            Hd(0.95f, 0.55f, 0.56f);
            Hd(1.00f, 0.06f, 0.06f);
            lofts.Add(head);

            // Arms. The top ring sits inside the chest, which is why there is no shoulder
            // seam: the join is buried under the surface it joins to.
            float sleeveEnd = sleeves == 0 ? 0.480f : sleeves == 1 ? 0.640f : 0.780f;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                int bA = side == 0 ? BArmL : BArmR;
                int bF = side == 0 ? BForeL : BForeR;
                int bH = side == 0 ? BHandL : BHandR;
                var arm = new Loft { Segs = 9 };

                void A(float y, float x, float r, int b0, int b1, float w1, float rz = -1f)
                {
                    bool clothed = y > sleeveEnd;
                    arm.Rings.Add(new Ring
                    {
                        C = new Vector3(sx * x * H, y * H, 0),
                        Rx = (r * g + (clothed ? hemT / H : 0f)) * H,
                        Rz = ((rz < 0 ? r : rz) * g + (clothed ? hemT / H : 0f)) * H,
                        B0 = b0, B1 = b1, W1 = w1,
                        Sub = clothed ? SubShirt : SubSkin,
                    });
                }

                float ax = armX / H;
                A(0.812f, ax * 0.62f, 0.052f, BChest, bA, 0.25f);   // buried in the chest
                A(0.796f, ax, 0.048f, BChest, bA, 0.75f);           // deltoid
                A(0.755f, ax, 0.040f, bA, bA, 0f);
                A(0.700f, ax, 0.034f, bA, bA, 0f);
                A(0.648f, ax, 0.031f, bA, bF, 0.30f);
                A(0.625f, ax, 0.030f, bA, bF, 0.70f);               // elbow
                A(0.585f, ax, 0.028f, bF, bF, 0f);
                A(0.520f, ax, 0.024f, bF, bF, 0f);
                A(0.483f, ax, 0.021f, bF, bH, 0.60f);               // wrist
                A(0.462f, ax, 0.026f, bH, bH, 0f, 0.016f);          // palm
                A(0.432f, ax, 0.024f, bH, bH, 0f, 0.014f);
                A(0.412f, ax, 0.010f, bH, bH, 0f, 0.008f);          // fingertips
                lofts.Add(arm);
            }

            // Legs. Same trick at the top: the first ring is inside the pelvis.
            float legEnd = legwear == 0 ? 0.040f : legwear == 1 ? 0.330f : 0.400f;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                int bL = side == 0 ? BLegL : BLegR;
                int bS = side == 0 ? BShinL : BShinR;
                int bFt = side == 0 ? BFootL : BFootR;
                var leg = new Loft { Segs = 9 };
                float lx = legXb / H;

                void L(float y, float r, int b0, int b1, float w1, float rz = -1f, float z = 0f)
                {
                    bool shod = y < 0.058f;
                    bool clothed = !shod && y > legEnd;
                    leg.Rings.Add(new Ring
                    {
                        C = new Vector3(sx * lx * H, y * H, z * H),
                        Rx = (r * g + (clothed || shod ? hemT / H : 0f)) * H,
                        Rz = ((rz < 0 ? r : rz) * g + (clothed || shod ? hemT / H : 0f)) * H,
                        B0 = b0, B1 = b1, W1 = w1,
                        Sub = shod ? SubShoe : clothed ? SubTrouser : SubSkin,
                    });
                }

                L(0.545f, 0.055f, BHips, bL, 0.30f);
                L(0.505f, 0.062f, BHips, bL, 0.85f);
                L(0.440f, 0.056f, bL, bL, 0f);
                L(0.360f, 0.049f, bL, bL, 0f);
                L(0.305f, 0.045f, bL, bS, 0.30f);
                L(0.285f, 0.044f, bL, bS, 0.70f);                   // knee
                L(0.240f, 0.046f, bS, bS, 0f);
                L(0.160f, 0.038f, bS, bS, 0f);
                L(0.075f, 0.026f, bS, bFt, 0.55f);                  // ankle
                L(0.045f, 0.030f, bFt, bFt, 0f, 0.038f, 0.010f);
                L(0.018f, 0.030f, bFt, bFt, 0f, 0.058f, 0.030f);    // instep
                L(0.010f, 0.026f, bFt, bFt, 0f, 0.050f, 0.062f);    // toe
                lofts.Add(leg);
            }

            // Hair, as a shell over the crown. Not a lathe sat on top of a head — a run of
            // rings following the same profile, offset back, so the hairline rides up at
            // the front and drops at the back the way a real one does.
            if (hairStyle != 4 || rng.Chance(0.5))
            {
                var hairL = new Loft { Segs = 14 };
                float back = 0.012f * H * headK;
                void Hr(float t, float rx, float rz)
                    => hairL.Rings.Add(new Ring
                    {
                        C = new Vector3(0, hc + (t - 0.5f) * 2f * hh, -back),
                        Rx = rx * hw * 1.05f, Rz = rz * hw * 1.30f * 1.05f,
                        B0 = BHead, B1 = BHead, W1 = 0f, Sub = SubHair,
                    });
                // A hairline sits at about 0.72 of head height, not 0.58 — the lower start
                // put the crown down over the eyebrows and made every face look like it
                // was wearing a swim cap.
                float from = hairStyle == 1 ? 0.24f : 0.72f;   // a bob comes down to the jaw
                Hr(from, hairStyle == 1 ? 0.86f : 0.97f, hairStyle == 1 ? 0.96f : 1.02f);
                Hr(0.80f, 0.94f, 0.98f);
                Hr(0.84f, 0.88f, 0.90f);
                Hr(0.95f, 0.55f, 0.56f);
                Hr(1.00f, 0.08f, 0.08f);
                lofts.Add(hairL);
            }

            // ---- stitch ----
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var subs = new List<int>[SubCount];
            for (int i = 0; i < SubCount; i++) subs[i] = new List<int>();

            foreach (var lo in lofts) Stitch(lo, verts, norms, uvs, weights, subs);

            var mesh = new Mesh
            {
                name = "human",
                indexFormat = IndexFormat.UInt16,
                subMeshCount = SubCount,
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            for (int i = 0; i < SubCount; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateNormals();
            WeldSeamNormals(mesh, lofts);
            mesh.RecalculateTangents();     // the skin and cloth normal maps need these
            mesh.RecalculateBounds();

            var binds = new Matrix4x4[BoneCount];
            for (int i = 0; i < BoneCount; i++)
                binds[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = binds;

            var smr = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[BHips];
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(new Vector3(0, H * 0.5f, 0),
                                         new Vector3(H * 0.8f, H * 1.1f, H * 0.8f));

            materials = new Material[SubCount];
            materials[SubSkin] = MatLib.Skin(skinC, ProcTex.SkinDetail(), ProcTex.SkinNormal());
            var irisC = Palette.Irises[(int)(rng.Next() * Palette.Irises.Length)];
            materials[SubFace] = MatLib.Skin(
                skinC, ProcTex.Face(skinIdx, hairC, irisC, stubble), ProcTex.SkinNormal(),
                "face" + skinIdx + ColorUtility.ToHtmlStringRGB(hairC)
                       + ColorUtility.ToHtmlStringRGB(irisC) + stubble);
            materials[SubShirt] = MatLib.Cloth(shirtC);
            materials[SubTrouser] = MatLib.Cloth(trouserC);
            materials[SubHair] = MatLib.Solid(hairC, 0.30f);
            materials[SubShoe] = MatLib.Solid(new Color(0.115f, 0.108f, 0.108f), 0.30f);
            smr.sharedMaterials = materials;

            // Eyes are PAINTED, not modelled, and the first attempt here is the argument
            // for it: two spheres at the right anatomical size, placed a couple of
            // millimetres out, and they read as ping-pong balls stuck to the front of the
            // face. An eye only works when it sits in a socket that shades it, and a socket
            // is three pixels of gradient — cheaper, and impossible to get geometrically
            // wrong. See ProcTex.Face.

            return new Rig
            {
                Root = root, Hips = bones[BHips], Spine = bones[BSpine], Chest = bones[BChest],
                Neck = bones[BNeck], Head = bones[BHead],
                ArmL = bones[BArmL], ForeArmL = bones[BForeL],
                ArmR = bones[BArmR], ForeArmR = bones[BForeR], HandR = bones[BHandR],
                LegL = bones[BLegL], ShinL = bones[BShinL],
                LegR = bones[BLegR], ShinR = bones[BShinR],
                Height = H,
            };
        }

        // ------------------------------------------------------------------ stitching

        /// <summary>
        /// Turn a run of rings into a tube.
        ///
        /// The seam column is duplicated so u can run 0..1 without wrapping; that leaves a
        /// crease in the normals, which <see cref="WeldSeamNormals"/> then removes. A
        /// material change between two rings does NOT get a quad, which is what makes a
        /// hem an edge instead of a smear — the two rings at a hem are at the same height
        /// and different radii, so the step between them is the fold of the cloth.
        /// </summary>
        static void Stitch(Loft lo, List<Vector3> verts, List<Vector3> norms,
                           List<Vector2> uvs, List<BoneWeight> weights, List<int>[] subs)
        {
            int cols = lo.Segs + 1;
            int baseIndex = verts.Count;

            float run = 0f;
            for (int r = 0; r < lo.Rings.Count; r++)
            {
                var ring = lo.Rings[r];
                if (r > 0) run += Vector3.Distance(ring.C, lo.Rings[r - 1].C);

                for (int i = 0; i < cols; i++)
                {
                    float u = (float)i / lo.Segs;
                    // u = 0.5 faces +Z. On the head that puts the middle of the texture on
                    // the middle of the face and the seam at the back of the skull.
                    float a = u * Mathf.PI * 2f;
                    verts.Add(ring.C + new Vector3(-Mathf.Sin(a) * ring.Rx, 0,
                                                   -Mathf.Cos(a) * ring.Rz));
                    norms.Add(Vector3.up);
                    uvs.Add(new Vector2(u, lo.FaceUV ? (float)r / (lo.Rings.Count - 1) : run));

                    var bw = new BoneWeight
                    {
                        boneIndex0 = ring.B0, weight0 = 1f - ring.W1,
                        boneIndex1 = ring.B1, weight1 = ring.W1,
                    };
                    weights.Add(bw);
                }
            }

            // Which way round the triangles go depends on which way the loft runs.
            //
            // The ring parameterisation turns one way; if the rings then ascend, the cross
            // product of "along the ring" and "along the loft" points outward, and if they
            // descend it points inward. The torso, head and hair run upward and the arms
            // and legs run downward, so a single winding rule turns exactly half the body
            // inside out — and an inside-out tube does not look inside out. Backface
            // culling hides the near surface and you see the INSIDE of the far one, which
            // for a head means looking through the face at the back of the skull. That is
            // what a blank face and a painted texture that verified fine turned out to be.
            bool ascends = lo.Rings[lo.Rings.Count - 1].C.y >= lo.Rings[0].C.y;

            void Quad(System.Collections.Generic.List<int> into, int a0, int b0, int i)
            {
                if (ascends)
                {
                    into.Add(a0 + i); into.Add(a0 + i + 1); into.Add(b0 + i);
                    into.Add(a0 + i + 1); into.Add(b0 + i + 1); into.Add(b0 + i);
                }
                else
                {
                    into.Add(a0 + i); into.Add(b0 + i); into.Add(a0 + i + 1);
                    into.Add(a0 + i + 1); into.Add(b0 + i); into.Add(b0 + i + 1);
                }
            }

            for (int r = 0; r + 1 < lo.Rings.Count; r++)
            {
                // A material change is a hem: it gets its own quads below, in the outer
                // material, so the fold has thickness and reads as an edge.
                int sub = lo.Rings[r].Sub;
                if (sub != lo.Rings[r + 1].Sub)
                    sub = lo.Rings[r].Rx >= lo.Rings[r + 1].Rx
                        ? lo.Rings[r].Sub : lo.Rings[r + 1].Sub;

                var into = subs[sub];
                int a0 = baseIndex + r * cols, b0 = a0 + cols;
                for (int i = 0; i < lo.Segs; i++) Quad(into, a0, b0, i);
            }
        }

        /// <summary>
        /// Average the normals of the duplicated seam column.
        ///
        /// Every loft is cut open along u = 0 so its UVs can run 0..1, which means the two
        /// halves of that column are separate vertices and RecalculateNormals gives them
        /// different normals. The result is a hard bright line running the full length of
        /// every arm, leg and the back of the head — a lighting artefact, not a shape, and
        /// it is one of those things that looks like "low quality model" rather than like
        /// the bug it is.
        /// </summary>
        static void WeldSeamNormals(Mesh mesh, List<Loft> lofts)
        {
            var n = mesh.normals;
            int at = 0;
            foreach (var lo in lofts)
            {
                int cols = lo.Segs + 1;
                for (int r = 0; r < lo.Rings.Count; r++)
                {
                    int first = at + r * cols, last = first + lo.Segs;
                    var avg = (n[first] + n[last]).normalized;
                    n[first] = avg;
                    n[last] = avg;
                }
                at += cols * lo.Rings.Count;
            }
            mesh.normals = n;
        }

        static float Quant(float v, float step) => Mathf.Round(v / step) * step;
    }
}
