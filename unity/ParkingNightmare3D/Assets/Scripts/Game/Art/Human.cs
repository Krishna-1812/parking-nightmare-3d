using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// A person, as one continuous skinned surface over a real skeleton.
    ///
    /// The structural argument for skinning is in the git history and still holds: a body
    /// assembled from separate solids shows its seams and cannot deform, and no amount of
    /// surface detail covers either. What this file is about now is the layer above that —
    /// why a correctly skinned figure with a painted face still read as a shop dummy.
    ///
    /// Three causes, all of them measurable, none of them shading:
    ///
    /// **The limbs were half again too thick.** Every radius here is now derived from a
    /// circumference. An upper arm is a 300 mm circumference, so its radius is 48 mm, so on
    /// a 1.72 m figure it is 0.028 of standing height. The previous number was 0.040 — a
    /// 138 mm arm, forty percent over. The same was true of the calf (39% over), the knee
    /// (29%) and the wrist (34%). Uniformly thick simplified limbs are exactly what a doll
    /// has, which is why the figure read as one however well it was lit.
    ///
    /// **The head was an ellipsoid.** A ring swept as an ellipse can only make an egg. It
    /// has no brow, no cheekbone, no jaw corner, no occiput — and a head with none of those
    /// is not a stylised head, it is a thumb. Rings now carry a superellipse exponent and
    /// up to three angular lobes (<see cref="Point"/>), which is enough to shape a skull
    /// out of the same twelve cross-sections. The nose and the ears are separate lofts,
    /// because both project past any convex section through the head.
    ///
    /// **The hair was inside the skull.** Measured: the hair shell was inflated 5% and
    /// pushed back 21 mm, which put its forehead 11.5 mm *under* the head surface and left
    /// only a 5.7 mm crescent visible at the temples. That is the bald man with a grey
    /// smear over one ear in the render before this one. Hair thickness is now added in
    /// millimetres on top of a shared skull profile, so it cannot sink in again.
    ///
    /// Two supporting changes. Occlusion is baked per-vertex (<see cref="Ring.Crease"/>)
    /// and multiplied into the skin shader, because the crease under a jaw and the shadow
    /// behind an ear are what tell you a head is a solid and not a decal — SSAO at this
    /// screen size resolves neither. And every tube is now closed at its buried end: an
    /// unclosed loft leaves an open boundary, and when the arm got its correct (thinner)
    /// radius the old buried ring stopped being buried and that boundary became the dark
    /// notch either side of the neck.
    ///
    /// Roughly 1150 vertices, up from 750. All of the increase is in the head and the
    /// hands, which is where it is looked at from.
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
            public Transform LegL, ShinL, FootL, LegR, ShinR, FootR;
            public Transform HandR;
            public float Height;
        }

        // ------------------------------------------------------------------ rings

        /// <summary>
        /// One cross-section of the body. A loft is a run of these; the mesh is the surface
        /// stitched between consecutive rings.
        ///
        /// The shaping fields are what lift this above a stack of ellipses. <see cref="Box"/>
        /// squares the section off — a chest, a jaw and the sole of a shoe are all far
        /// closer to a rounded rectangle than to an oval. The three lobes push the surface
        /// out over an angular band: front for a brow ridge or a chin, back for an occiput
        /// or a calf, side for a cheekbone or a hip. Between them they cost no vertices at
        /// all, which is the whole reason to shape a body this way rather than by adding
        /// cross-sections.
        /// </summary>
        struct Ring
        {
            public Vector3 C;          // centre, in bind pose
            public float Rx, Rz;       // half-width and half-depth
            public int B0, B1;         // the two bones this ring is weighted to
            public float W1;           // how much of it belongs to B1
            public int Sub;            // submesh, i.e. which material
            public float V;            // texture coordinate along the loft, if the loft says so

            public float Box;          // 0 = ellipse, 1 = rounded rectangle
            public float Front, FrontW;// radial lobe centred on the face, in metres / turns
            public float Back, BackW;  // ... on the back of the body
            public float Side, SideW;  // ... on both flanks at once
            public float Rise, RiseW;  // VERTICAL lobe on the face side: a hairline
            public float Wrap;         // if > 0, shrink-wrap to the skull at this clearance
            public float Crease;       // baked occlusion, 0 open .. 1 buried
        }

        /// <summary>
        /// Head metrics for the current build, for <see cref="Point"/>'s shrink-wrap.
        ///
        /// Static because it is read once per vertex and threading three floats through
        /// every Ring in the body — arms, legs, shoes — to serve the six rings of hair
        /// that need them would be a poor trade. Build runs on the main thread, one
        /// pedestrian at a time.
        /// </summary>
        static float _hc, _hh, _hw;

        sealed class Loft
        {
            public readonly List<Ring> Rings = new();
            public int Segs = 8;
            /// <summary>Take v from <see cref="Ring.V"/> rather than from arc length.</summary>
            public bool RingV;
            /// <summary>
            /// The band of the texture this loft's u sweeps. A limb tiles over the whole
            /// map; the head wraps 0..1 so a painted face lands on the face; a nose is a
            /// 28 mm tube and has to be pinned to the patch of clean skin it sits on, or it
            /// samples the back of the skull.
            /// </summary>
            public float U0 = 0f, U1 = 1f;
            /// <summary>Where this loft sits, for parts built in their own frame. Rotation
            /// only — a mirror would invert the winding.</summary>
            public Matrix4x4 Xf = Matrix4x4.identity;
            public bool Placed;
        }

        // ------------------------------------------------------------------ the skull

        /// <summary>
        /// One cross-section of a head, at a height given as a fraction of head height:
        /// 0 is the underside of the chin, 1 the crown. Radii are fractions of the head's
        /// half-width, and the depth is multiplied by <see cref="Depth"/> on top of that.
        ///
        /// The proportions are the standard thirds — chin to nose base, nose base to brow,
        /// brow to hairline — which put the eye line at 0.49, and that is where the painted
        /// face in <see cref="ProcTex.Face"/> expects it. The two must be changed together.
        /// </summary>
        struct Prof
        {
            public float T, Rx, Rz, Box, Front, FrontW, Back, BackW, Side, SideW, Crease;
        }

        const float Depth = 1.30f;   // a head is 195 mm deep and 150 mm wide

        static Prof P(float t, float rx, float rz, float box = 0f,
                      float front = 0f, float frontW = 0f, float back = 0f, float backW = 0f,
                      float side = 0f, float sideW = 0f, float crease = 0f)
            => new Prof
            {
                T = t, Rx = rx, Rz = rz, Box = box,
                Front = front, FrontW = frontW, Back = back, BackW = backW,
                Side = side, SideW = sideW, Crease = crease,
            };

        /// <summary>
        /// The skull. Twelve sections, and every one of them is doing something an ellipse
        /// cannot: the jaw corners come from Box, the chin and the brow ridge from a front
        /// lobe, the cheekbone from a side lobe, the back of the cranium from a back lobe.
        /// </summary>
        static readonly Prof[] Skull =
        {
            P(0.000f, 0.030f, 0.030f, crease: 0.62f),
            P(0.055f, 0.430f, 0.545f, 0.20f, front: 0.090f, frontW: 0.150f, crease: 0.46f),
            P(0.145f, 0.680f, 0.810f, 0.42f, front: 0.060f, frontW: 0.180f, crease: 0.26f),
            P(0.235f, 0.810f, 0.915f, 0.44f, front: 0.030f, frontW: 0.200f, crease: 0.10f),
            P(0.320f, 0.870f, 0.965f, 0.32f, side: 0.020f, sideW: 0.120f),
            P(0.420f, 0.925f, 0.995f, 0.26f, back: 0.020f, backW: 0.300f,
                                            side: 0.045f, sideW: 0.120f),
            P(0.510f, 0.960f, 1.005f, 0.30f, back: 0.035f, backW: 0.300f),
            P(0.590f, 0.975f, 1.010f, 0.36f, front: 0.055f, frontW: 0.190f,
                                            back: 0.045f, backW: 0.300f),
            P(0.700f, 0.995f, 0.995f, 0.36f, back: 0.050f, backW: 0.320f),
            P(0.820f, 0.960f, 0.930f, 0.30f, back: 0.040f, backW: 0.320f),
            P(0.915f, 0.790f, 0.760f, 0.16f, back: 0.015f, backW: 0.300f),
            P(0.968f, 0.520f, 0.500f),
            // The apex has to be a point. A loft has no end caps, so the last ring is an
            // open boundary: at 0.220 of head width this was a 33 mm hole in the crown, and
            // since backfaces are culled you looked through it and out the front of the
            // skull at the road beyond. It read as a hole in the middle of the head.
            P(1.000f, 0.035f, 0.034f),
        };

        /// <summary>The skull profile between sections, so hair, ears and nose can sit on it.</summary>
        static Vector2 SkullR(float t)
        {
            if (t <= Skull[0].T) return new Vector2(Skull[0].Rx, Skull[0].Rz * Depth);
            for (int i = 1; i < Skull.Length; i++)
            {
                if (t > Skull[i].T) continue;
                float k = (t - Skull[i - 1].T) / (Skull[i].T - Skull[i - 1].T);
                return new Vector2(Mathf.Lerp(Skull[i - 1].Rx, Skull[i].Rx, k),
                                   Mathf.Lerp(Skull[i - 1].Rz, Skull[i].Rz, k) * Depth);
            }
            var last = Skull[Skull.Length - 1];
            return new Vector2(last.Rx, last.Rz * Depth);
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
            int hairStyle = (int)(rng.Next() * 6);
            bool bald = !child && hairStyle == 5 && rng.Chance(0.55);

            // ---- skeleton, in fractions of standing height ----
            // Anthropometric, not invented. Getting these right costs nothing and is most
            // of the difference between a person and a doll.
            var bind = new Vector3[BoneCount];
            bind[BHips] = new Vector3(0, 0.530f * H, 0);
            bind[BSpine] = new Vector3(0, 0.610f * H, 0);
            bind[BChest] = new Vector3(0, 0.700f * H, 0);
            bind[BNeck] = new Vector3(0, 0.800f * H, 0);
            bind[BHead] = new Vector3(0, 0.855f * H, 0);

            // The glenohumeral joint sits about 170 mm off the midline on a 1.72 m adult —
            // 0.099 of standing height — which is outside the ribcage at 0.090 and inside
            // the acromion at 0.131. Get it wrong inward and the forearms emerge from the
            // middle of the chest; wrong outward and the deltoid detaches from the yoke.
            float armX = 0.099f * H * Mathf.Lerp(1f, girth, 0.45f);
            bind[BArmL] = new Vector3(-armX, 0.795f * H, 0);
            bind[BForeL] = new Vector3(-armX, 0.625f * H, 0);
            bind[BHandL] = new Vector3(-armX, 0.475f * H, 0);
            bind[BArmR] = new Vector3(armX, 0.795f * H, 0);
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

            // Head geometry the rest of the body has to agree with.
            float hh = 0.065f * H * headK;              // HALF the head's height
            float hc = 0.870f * H + hh;                 // centre, measured up from the chin
            float hw = 0.0435f * H * headK;             // half-width: a 150 mm head
            float headZ = 0.010f * H;                   // a head sits forward of the shoulders
            _hc = hc; _hh = hh; _hw = hw;               // for Point's shrink-wrap

            // ---------------------------------------------------------------- torso
            //
            // Clothing is this same surface pushed out a few millimetres; the hem rings are
            // emitted twice so the material edge is a crisp step rather than a smear.
            float hemT = 0.006f * H;
            // Sixteen segments, not twelve. A superellipse concentrates all its curvature
            // in the corners, so the flats between them are very flat: at twelve the chest
            // showed as four broad facets with a hard edge down each side, which reads as
            // creased card rather than as cloth over a ribcage. Boxiness and segment count
            // have to go up together.
            var torso = new Loft { Segs = 16 };
            void T(float y, float rx, float rz, int b0, int b1, float w1, int sub,
                   float pad = 0f, float box = 0f, float back = 0f, float backW = 0f,
                   float crease = 0f, float z = 0f)
                => torso.Rings.Add(new Ring
                {
                    C = new Vector3(0, y * H, z * H),
                    Rx = (rx * g + pad) * H, Rz = (rz * g + pad) * H,
                    B0 = b0, B1 = b1, W1 = w1, Sub = sub,
                    Box = box, Back = back * H, BackW = backW, Crease = crease,
                });

            T(0.500f, 0.006f, 0.005f, BHips, BHips, 0f, SubTrouser, crease: 0.60f);
            T(0.514f, 0.086f, 0.062f, BHips, BHips, 0f, SubTrouser, box: 0.15f,
              back: 0.006f, backW: 0.34f);
            T(0.548f, 0.098f, 0.067f, BHips, BHips, 0f, SubTrouser, box: 0.22f,
              back: 0.008f, backW: 0.34f);                                   // seat
            T(0.578f, 0.089f, 0.060f, BHips, BSpine, 0.35f, SubTrouser, box: 0.26f);
            T(0.578f, 0.083f, 0.056f, BHips, BSpine, 0.35f, SubShirt, hemT, box: 0.26f);
            T(0.614f, 0.082f, 0.055f, BSpine, BSpine, 0f, SubShirt, hemT, box: 0.32f);
            T(0.658f, 0.085f, 0.058f, BSpine, BChest, 0.45f, SubShirt, hemT, box: 0.38f);
            T(0.702f, 0.089f, 0.061f, BChest, BChest, 0f, SubShirt, hemT, box: 0.44f);
            T(0.742f, 0.091f, 0.060f, BChest, BChest, 0f, SubShirt, hemT, box: 0.46f);
            T(0.770f, 0.091f, 0.057f, BChest, BChest, 0f, SubShirt, hemT, box: 0.44f);
            // The yoke has to stay wide up to here or the deltoid stops overlapping it and
            // stands off the body as a separate ball — which is what the last build did.
            T(0.786f, 0.089f, 0.054f, BChest, BNeck, 0.20f, SubShirt, hemT, box: 0.40f);
            T(0.800f, 0.076f, 0.049f, BChest, BNeck, 0.32f, SubShirt, hemT, box: 0.32f);
            // The collar is a near-vertical band. Sloped, it is a horizontal annulus facing
            // straight at the sky, and a 20 mm ring of shirt at full sun brightness round
            // the base of the neck reads as a paper ruff.
            T(0.812f, 0.056f, 0.043f, BChest, BNeck, 0.42f, SubShirt, hemT, box: 0.20f);
            T(0.820f, 0.044f, 0.038f, BChest, BNeck, 0.52f, SubShirt, hemT, box: 0.10f);
            // A neck is 375 mm round, so 60 mm in radius, so 0.035 of standing height.
            T(0.824f, 0.036f, 0.034f, BChest, BNeck, 0.60f, SubSkin, crease: 0.46f, z: 0.001f);
            T(0.848f, 0.0345f, 0.0325f, BNeck, BNeck, 0f, SubSkin, crease: 0.20f, z: 0.005f);
            T(0.868f, 0.032f, 0.031f, BNeck, BHead, 0.70f, SubSkin, crease: 0.44f, z: 0.009f);
            // ... and then up INSIDE the jaw, closing to a point. Stopping at 0.868 left
            // the neck an open tube whose mouth was wider than the chin above it, so the
            // bottom of the face was a pale band with the inside of the throat behind it.
            T(0.888f, 0.026f, 0.026f, BNeck, BHead, 0.90f, SubSkin, crease: 0.70f, z: 0.010f);
            T(0.906f, 0.011f, 0.011f, BHead, BHead, 0f, SubSkin, crease: 0.85f, z: 0.010f);
            lofts.Add(torso);

            // ---------------------------------------------------------------- head
            //
            // Its own submesh and its own UV wrap, so a painted face lands where the face
            // is: u = 0.5 is dead centre front and the seam falls at the back. v is the
            // fraction of head height, which is what ProcTex.Face is painted against.
            var head = new Loft { Segs = 16, RingV = true };
            foreach (var p in Skull)
                head.Rings.Add(new Ring
                {
                    C = new Vector3(0, hc + (p.T - 0.5f) * 2f * hh, headZ),
                    Rx = p.Rx * hw, Rz = p.Rz * hw * Depth,
                    B0 = BHead, B1 = BHead, W1 = 0f, Sub = SubFace, V = p.T,
                    Box = p.Box,
                    Front = p.Front * hw, FrontW = p.FrontW,
                    Back = p.Back * hw, BackW = p.BackW,
                    Side = p.Side * hw, SideW = p.SideW,
                    Crease = p.Crease,
                });
            lofts.Add(head);

            // ---------------------------------------------------------------- nose
            //
            // Geometry, not paint. A nose projects 22 mm past the face and casts the one
            // shadow the eye uses to decide a head is three-dimensional; painted onto a
            // convex section it is a smudge, and no three-quarter view survives it.
            //
            // Each ring is an ellipse whose back half is buried inside the skull, the same
            // trick the arms use at the shoulder. Its UVs are pinned to the patch of clean
            // skin between the lower lids and the mouth: sampling by true position would
            // walk the bridge straight into the painted eyebrows.
            {
                var nose = new Loft { Segs = 8, RingV = true, U0 = 0.415f, U1 = 0.585f };
                void N(float t, float rx, float outw, float vv, float crease = 0f)
                {
                    float faceZ = SkullR(t).y * hw;
                    nose.Rings.Add(new Ring
                    {
                        C = new Vector3(0, hc + (t - 0.5f) * 2f * hh, headZ + faceZ * 0.45f),
                        Rx = rx * hw, Rz = faceZ * 0.55f + outw * hw,
                        B0 = BHead, B1 = BHead, W1 = 0f, Sub = SubFace, V = vv,
                        Crease = crease,
                    });
                }
                N(0.560f, 0.115f, 0.010f, 0.440f);       // bridge, all but flush
                N(0.500f, 0.120f, 0.060f, 0.430f);
                N(0.440f, 0.145f, 0.150f, 0.415f);
                N(0.375f, 0.190f, 0.255f, 0.398f);
                N(0.330f, 0.250f, 0.290f, 0.386f);       // tip
                N(0.298f, 0.300f, 0.170f, 0.376f, 0.30f);// alae
                N(0.278f, 0.235f, 0.020f, 0.368f, 0.62f);// under the nose, into the lip
                lofts.Add(nose);
            }

            // ---------------------------------------------------------------- ears
            //
            // Built flat in their own frame and rotated onto the side of the head, because
            // an ear is a disc standing off a sphere and no cross-section through a skull
            // contains one. Both sides get a rotation rather than a mirror: a mirror has a
            // negative determinant and would turn one ear inside out.
            {
                float earT = 0.400f;
                float earX = SkullR(earT).x * hw * 0.84f;
                float earY = hc + (earT - 0.5f) * 2f * hh;
                for (int side = 0; side < 2; side++)
                {
                    float sx = side == 0 ? -1f : 1f;
                    var rot = side == 1
                        ? Quaternion.LookRotation(new Vector3(0, 0, -1), new Vector3(1, 0, 0))
                        : Quaternion.LookRotation(new Vector3(0, 0, 1), new Vector3(-1, 0, 0));
                    var ear = new Loft
                    {
                        Segs = 8, RingV = true, U0 = 0.5f + sx * 0.25f, U1 = 0.5f + sx * 0.25f,
                        Placed = true,
                        Xf = Matrix4x4.TRS(new Vector3(sx * earX, earY, headZ - 0.06f * hw),
                                           Quaternion.Euler(-10f, 0, 0) * rot, Vector3.one),
                    };
                    // Local Y is thickness; local X becomes vertical and local Z front-back.
                    void E(float thick, float rx, float rz, float crease)
                        => ear.Rings.Add(new Ring
                        {
                            C = new Vector3(0, thick * hw, 0),
                            Rx = rx * hw, Rz = rz * hw,
                            B0 = BHead, B1 = BHead, W1 = 0f, Sub = SubFace, V = 0.44f,
                            Box = 0.18f, Crease = crease,
                        });
                    E(-0.060f, 0.300f, 0.150f, 0.85f);
                    E(0.090f, 0.400f, 0.215f, 0.46f);
                    E(0.200f, 0.400f, 0.205f, 0.22f);
                    E(0.285f, 0.330f, 0.160f, 0.10f);
                    E(0.320f, 0.170f, 0.085f, 0f);
                    lofts.Add(ear);
                }
            }

            // ---------------------------------------------------------------- arms
            //
            // Radii from circumferences: upper arm 300 mm round, elbow 250, forearm 270,
            // wrist 170. The hand runs from the wrist at 0.475 of stature to the fingertip
            // at 0.377, which is the anthropometric dactylion height and 169 mm of hand.
            float sleeveEnd = sleeves == 0 ? 0.480f : sleeves == 1 ? 0.640f : 0.780f;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                int bA = side == 0 ? BArmL : BArmR;
                int bF = side == 0 ? BForeL : BForeR;
                int bH = side == 0 ? BHandL : BHandR;
                var arm = new Loft { Segs = 9 };
                float ax = armX / H;

                void A(float y, float x, float r, int b0, int b1, float w1,
                       float rz = -1f, float z = 0f, float box = 0f, float crease = 0f)
                {
                    bool clothed = y > sleeveEnd;
                    arm.Rings.Add(new Ring
                    {
                        C = new Vector3(sx * x * H, y * H, z * H),
                        Rx = (r * g + (clothed ? hemT / H : 0f)) * H,
                        Rz = ((rz < 0 ? r : rz) * g + (clothed ? hemT / H : 0f)) * H,
                        B0 = b0, B1 = b1, W1 = w1, Box = box, Crease = crease,
                        Sub = clothed ? SubShirt : SubSkin,
                    });
                }

                // The cap is pulled inboard to x 0.050 so it lands inside the trapezius. Put
                // it over the joint at 0.099 instead and it is a cone tip in open air above
                // the shoulder — the loft has no end cap of its own.
                A(0.804f, 0.052f, 0.010f, BChest, bA, 0.10f, crease: 0.55f);
                A(0.796f, 0.078f, 0.026f, BChest, bA, 0.45f, 0.032f, crease: 0.30f);
                // A deltoid is 110 mm front to back and 55 mm across, not a sphere. Built
                // round it is a ball balanced on the shoulder; flattened against the ribs
                // it is the slope from the neck to the arm that it actually is.
                A(0.782f, ax, 0.030f, BChest, bA, 0.85f, 0.038f);     // deltoid
                A(0.752f, ax, 0.028f, bA, bA, 0f, 0.030f);
                A(0.700f, ax, 0.028f, bA, bA, 0f);
                A(0.655f, ax, 0.025f, bA, bF, 0.25f);
                A(0.628f, ax, 0.0235f, bA, bF, 0.70f);               // elbow
                A(0.598f, ax, 0.025f, bF, bF, 0f);                   // forearm swell
                A(0.552f, ax, 0.0225f, bF, bF, 0f);
                A(0.500f, ax, 0.0175f, bF, bF, 0f);
                A(0.478f, ax, 0.0157f, bF, bH, 0.65f, 0.0125f);      // wrist
                // A hand is a paddle 90 mm across and 28 mm thick, not a taper to a point,
                // and it curls forward at rest. Both of those read at ten metres; the
                // knuckles do not, so there are none.
                A(0.455f, ax, 0.0245f, bH, bH, 0f, 0.0090f, 0.002f, 0.20f);
                A(0.425f, ax, 0.0260f, bH, bH, 0f, 0.0085f, 0.005f, 0.28f);
                A(0.398f, ax, 0.0240f, bH, bH, 0f, 0.0080f, 0.009f, 0.28f);
                A(0.380f, ax, 0.0130f, bH, bH, 0f, 0.0050f, 0.012f, 0.12f);
                lofts.Add(arm);
            }

            // ---------------------------------------------------------------- legs
            float legEnd = legwear == 0 ? 0.058f : legwear == 1 ? 0.330f : 0.400f;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                int bL = side == 0 ? BLegL : BLegR;
                int bS = side == 0 ? BShinL : BShinR;
                int bFt = side == 0 ? BFootL : BFootR;
                var leg = new Loft { Segs = 9 };
                float lx = legXb / H;

                void L(float y, float r, int b0, int b1, float w1, float rz = -1f,
                       float z = 0f, float box = 0f, float back = 0f, float backW = 0f,
                       float crease = 0f)
                {
                    bool shod = y < 0.062f;
                    bool clothed = !shod && y > legEnd;
                    leg.Rings.Add(new Ring
                    {
                        C = new Vector3(sx * lx * H, y * H, z * H),
                        Rx = (r * g + (clothed || shod ? hemT / H : 0f)) * H,
                        Rz = ((rz < 0 ? r : rz) * g + (clothed || shod ? hemT / H : 0f)) * H,
                        B0 = b0, B1 = b1, W1 = w1,
                        Box = box, Back = back * H, BackW = backW, Crease = crease,
                        Sub = shod ? SubShoe : clothed ? SubTrouser : SubSkin,
                    });
                }

                // Thigh 550 mm round, above-knee 400, knee 370, calf 360, ankle 220.
                L(0.552f, 0.011f, BHips, bL, 0.20f, crease: 0.55f);   // cap, inside the pelvis
                L(0.516f, 0.051f, BHips, bL, 0.80f, crease: 0.25f);
                L(0.452f, 0.046f, bL, bL, 0f);
                L(0.382f, 0.040f, bL, bL, 0f);
                L(0.322f, 0.036f, bL, bS, 0.25f);
                L(0.285f, 0.034f, bL, bS, 0.70f);                     // knee
                L(0.252f, 0.034f, bS, bS, 0f, back: 0.006f, backW: 0.30f);
                L(0.224f, 0.033f, bS, bS, 0f, back: 0.009f, backW: 0.30f);  // calf
                L(0.168f, 0.028f, bS, bS, 0f, back: 0.004f, backW: 0.30f);
                L(0.108f, 0.0225f, bS, bS, 0f);
                L(0.072f, 0.0195f, bS, bFt, 0.60f, crease: 0.30f);    // ankle
                // A shoe is a wedge with a flat sole and a sidewall. The old one was a
                // rounded tube end, which is why it read as a slipper: the sole is where
                // the eye checks that a person is standing on the ground rather than in it.
                L(0.052f, 0.024f, bFt, bFt, 0f, 0.036f, 0.006f, 0.25f);
                L(0.030f, 0.029f, bFt, bFt, 0f, 0.060f, 0.020f, 0.55f);
                L(0.014f, 0.031f, bFt, bFt, 0f, 0.074f, 0.026f, 0.86f);   // welt
                L(0.004f, 0.031f, bFt, bFt, 0f, 0.074f, 0.026f, 0.92f, crease: 0.35f);
                L(0.000f, 0.027f, bFt, bFt, 0f, 0.066f, 0.024f, 0.86f, crease: 0.70f);
                lofts.Add(leg);
            }

            // ---------------------------------------------------------------- hair
            //
            // Thickness is ADDED to the shared skull profile in millimetres. The previous
            // version scaled the head radii by 1.05 and translated the shell back 21 mm,
            // which measured out at 11.5 mm of forehead *inside* the skull and a 5.7 mm
            // crescent showing at the temple — a bald man with a smear over one ear.
            //
            // The hairline is a vertical lobe on the front of the base ring rather than a
            // ring at constant height, which is the whole difference between hair and a
            // swim cap: it climbs the forehead, drops over the ears, and reaches the nape.
            if (!bald)
            {
                var hairL = new Loft { Segs = 16, RingV = true };
                // Depth of the pile, where the hairline starts, and how far round the head
                // it climbs. A crop, a mop and a bob differ in silhouette and in nothing
                // else at this distance, so these three numbers are the whole wardrobe.
                float thick = hw * (hairStyle switch
                {
                    1 => 0.26f, 2 => 0.22f, 3 => 0.31f, 4 => 0.15f, _ => 0.19f,
                });
                // How far round the head the hairline climbs. At 0.45 the lobe puts the
                // temples at roughly half head height, which is just above the ear; wider
                // buries them, narrower gives sideburns down to the jaw.
                float riseW = hairStyle switch { 1 => 0.40f, 3 => 0.50f, 4 => 0.38f, _ => 0.45f };
                float lowT = hairStyle switch { 1 => 0.02f, 2 => 0.20f, 4 => 0.32f, _ => 0.26f };

                /// <param name="riseTo">
                /// Where this ring's FRONT sits, if the hairline is climbing the forehead.
                /// A ring at constant height is a swim cap; lifting only the front over a
                /// cosine band is what makes it a hairline that drops over the ears and
                /// reaches the nape. Wrap then keeps every lifted vertex on the skull.
                /// </param>
                void Hr(float t, float k, float riseTo = -1f, float crease = 0f, float side = 0f)
                    => hairL.Rings.Add(new Ring
                    {
                        C = new Vector3(0, hc + (t - 0.5f) * 2f * hh, headZ),
                        B0 = BHead, B1 = BHead, W1 = 0f, Sub = SubHair, V = t,
                        Box = 0.20f, Wrap = thick * k,
                        Side = side * thick, SideW = 0.22f,
                        Rise = riseTo > 0f ? (riseTo - t) * 2f * hh : 0f,
                        RiseW = riseW, Crease = crease,
                    });

                // The base ring starts at the NAPE and the lobe carries it up to the
                // forehead, so the same ring is the hairline all the way round: 0.845 of
                // head height at the front, about 0.50 at the temples, 0.26 at the back of
                // the neck. Starting it at ear level instead left a band of bare scalp
                // across the back of the head under the hair.
                //
                // The rings above are enough to follow the curve of the skull. Two rings
                // and a straight chord between them let the occipital bulge push through
                // the shell as a patch of bald scalp. The rise targets climb with the
                // rings so the front of the loft stays monotonic — otherwise the hair
                // folds back over itself at the forehead.
                Hr(lowT, 0.42f, 0.845f, 0.55f);
                Hr(0.620f, 0.85f, 0.868f, 0.24f, 0.30f);
                Hr(0.780f, 1.00f, 0.892f, 0.08f, 0.35f);
                Hr(0.890f, 1.00f, side: 0.25f);
                Hr(0.938f, 0.95f);
                Hr(0.975f, 0.70f);
                Hr(1.014f, 0.25f);
                lofts.Add(hairL);
            }

            // ---- stitch ----
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color32>();
            var weights = new List<BoneWeight>();
            var subs = new List<int>[SubCount];
            for (int i = 0; i < SubCount; i++) subs[i] = new List<int>();

            foreach (var lo in lofts) Stitch(lo, verts, norms, uvs, cols, weights, subs);

            var mesh = new Mesh
            {
                name = "human",
                indexFormat = IndexFormat.UInt16,
                subMeshCount = SubCount,
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.boneWeights = weights.ToArray();
            for (int i = 0; i < SubCount; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();     // the skin and cloth normal maps need these
            WeldSeam(mesh, lofts);          // ... and both have to be welded, not just one
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
            materials[SubHair] = MatLib.Hair(hairC);
            materials[SubShoe] = MatLib.Solid(new Color(0.115f, 0.108f, 0.108f), 0.30f);
            smr.sharedMaterials = materials;

            // Eyes are PAINTED, not modelled, and the first attempt here is the argument
            // for it: two spheres at the right anatomical size, placed a couple of
            // millimetres out, and they read as ping-pong balls stuck to the front of the
            // face. An eye only works when it sits in a socket that shades it, and a socket
            // is three pixels of gradient — cheaper, and impossible to get geometrically
            // wrong. The nose went the other way for the opposite reason: it is a
            // projection, and no amount of painting makes a flat surface stick out.

            return new Rig
            {
                Root = root, Hips = bones[BHips], Spine = bones[BSpine], Chest = bones[BChest],
                Neck = bones[BNeck], Head = bones[BHead],
                ArmL = bones[BArmL], ForeArmL = bones[BForeL],
                ArmR = bones[BArmR], ForeArmR = bones[BForeR], HandR = bones[BHandR],
                LegL = bones[BLegL], ShinL = bones[BShinL], FootL = bones[BFootL],
                LegR = bones[BLegR], ShinR = bones[BShinR], FootR = bones[BFootR],
                Height = H,
            };
        }

        // ------------------------------------------------------------------ sections

        /// <summary>
        /// A raised cosine over an angular band, wrapping at u = 0. Smooth at both ends,
        /// which matters: a lobe with a discontinuous derivative shows up as a crease in
        /// the recalculated normals even where the shape itself looks right.
        /// </summary>
        static float Lobe(float u, float centre, float w)
        {
            if (w <= 0f) return 0f;
            float d = Mathf.Abs(u - centre);
            if (d > 0.5f) d = 1f - d;
            if (d >= w) return 0f;
            return 0.5f * (1f + Mathf.Cos(Mathf.PI * d / w));
        }

        /// <summary>
        /// One point on a ring. u = 0 is the back of the body, u = 0.5 the front.
        ///
        /// The superellipse is what turns a section from an oval into a body: raising the
        /// parametric sine and cosine to a power below one pushes the surface toward its
        /// bounding box, so a chest flattens front and back while keeping its corners
        /// round, and a jaw acquires the angle that separates a face from an egg.
        /// </summary>
        static Vector3 Point(in Ring g, float u)
        {
            // The lift comes first, because a shrink-wrapped ring needs to know what height
            // this vertex ended up at before it can ask the skull how wide it is there.
            float y = g.Rise != 0f ? g.Rise * Lobe(u, 0.5f, g.RiseW) : 0f;

            float rx = g.Rx, rz = g.Rz;
            if (g.Wrap > 0f)
            {
                // Hair. A hairline is a ring whose front has been lifted 90 mm up the
                // forehead, and a lifted vertex keeps the radius of the height it came
                // FROM — so the shell ends up inside a skull that has narrowed under it.
                // Correcting only the front, which is the obvious thing to do, leaves the
                // temples exactly as wrong as before and the hair vanishes at the sides:
                // that is the bald-with-a-crescent render, twice. Reading the profile at
                // the height each vertex actually reached is the only version of this that
                // cannot come back.
                var sr = SkullR((g.C.y + y - _hc) / (2f * _hh) + 0.5f);
                rx = sr.x * _hw + g.Wrap;
                rz = sr.y * _hw + g.Wrap;
            }

            float a = u * Mathf.PI * 2f;
            float s = -Mathf.Sin(a), c = -Mathf.Cos(a);
            if (g.Box > 0f)
            {
                float e = Mathf.Lerp(1f, 0.55f, g.Box);
                s = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e);
                c = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);
            }

            var p = new Vector2(s * rx, c * rz);
            float push = g.Front * Lobe(u, 0.5f, g.FrontW)
                       + g.Back * Lobe(u, 0f, g.BackW)
                       + g.Side * (Lobe(u, 0.25f, g.SideW) + Lobe(u, 0.75f, g.SideW));
            if (push != 0f && p.sqrMagnitude > 1e-10f) p += p.normalized * push;

            return g.C + new Vector3(p.x, y, p.y);
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
                           List<Vector2> uvs, List<Color32> cols, List<BoneWeight> weights,
                           List<int>[] subs)
        {
            int cols_ = lo.Segs + 1;
            int baseIndex = verts.Count;

            float run = 0f;
            for (int r = 0; r < lo.Rings.Count; r++)
            {
                var ring = lo.Rings[r];
                if (r > 0) run += Vector3.Distance(ring.C, lo.Rings[r - 1].C);

                byte ao = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - ring.Crease) * 255f);
                for (int i = 0; i < cols_; i++)
                {
                    float u = (float)i / lo.Segs;
                    var p = Point(ring, u);
                    verts.Add(lo.Placed ? lo.Xf.MultiplyPoint3x4(p) : p);
                    norms.Add(Vector3.up);
                    uvs.Add(new Vector2(Mathf.Lerp(lo.U0, lo.U1, u), lo.RingV ? ring.V : run));
                    cols.Add(new Color32(ao, ao, ao, 255));

                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = ring.B0, weight0 = 1f - ring.W1,
                        boneIndex1 = ring.B1, weight1 = ring.W1,
                    });
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

            void Quad(List<int> into, int a0, int b0, int i)
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
                int a0 = baseIndex + r * cols_, b0 = a0 + cols_;
                for (int i = 0; i < lo.Segs; i++) Quad(into, a0, b0, i);
            }
        }

        /// <summary>
        /// Average the normals AND the tangents of the duplicated seam column.
        ///
        /// Every loft is cut open along u = 0 so its UVs can run 0..1, which means the two
        /// halves of that column are separate vertices and RecalculateNormals gives them
        /// different normals. The result is a hard bright line running the full length of
        /// every arm, leg and the back of the head — a lighting artefact, not a shape, and
        /// it is one of those things that looks like "low quality model" rather than like
        /// the bug it is.
        ///
        /// Welding the normals alone was not enough, and the reason is worth keeping: at a
        /// UV seam the two columns also get different TANGENTS, because a tangent is
        /// derived from how u changes across a triangle and u jumps from 1 back to 0 there.
        /// A normal map then tilts the surface two different ways either side of the join,
        /// so the line came back the moment the skin and cloth got relief — fainter, and
        /// therefore harder to attribute. Both frames have to match.
        /// </summary>
        static void WeldSeam(Mesh mesh, List<Loft> lofts)
        {
            var n = mesh.normals;
            var t = mesh.tangents;
            int at = 0;
            foreach (var lo in lofts)
            {
                int cols = lo.Segs + 1;
                for (int r = 0; r < lo.Rings.Count; r++)
                {
                    int first = at + r * cols, last = first + lo.Segs;
                    n[first] = n[last] = (n[first] + n[last]).normalized;

                    var ta = (Vector3)t[first] + (Vector3)t[last];
                    ta = ta.sqrMagnitude > 1e-8f ? ta.normalized : (Vector3)t[first];
                    // Keep the handedness of the first column for both; the two sides face
                    // the same way, so a mismatched w would flip the bitangent instead.
                    t[first] = t[last] = new Vector4(ta.x, ta.y, ta.z, t[first].w);
                }
                at += cols * lo.Rings.Count;
            }
            mesh.normals = n;
            mesh.tangents = t;
        }

        static float Quant(float v, float step) => Mathf.Round(v / step) * step;
    }
}
