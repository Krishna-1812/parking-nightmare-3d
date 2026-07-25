# Unity rebuild

Vertical slice of Parking Nightmare 3D for Google Play and the App Store, following
`design-spec/DESIGN_SPEC.md` §11–§12. The web build stays the reference implementation:
where this and the JavaScript disagree, the JavaScript is right.

- **Editor:** Unity 6000.5.5f1 (Unity 6 LTS)
- **Project:** `unity/ParkingNightmare3D`
- **Pipeline:** URP, seeded from the editor's own `3d-cross-platform` (mobile) template,
  so the URP assets and quality tiers are the ones Unity ships rather than hand-authored
  ones

**The bundled project template is stale relative to the editor that ships it.** The
`3d-cross-platform-17.0.14` template inside 6000.5.5f1 pins URP 17.0.1, inputsystem
1.12.0 and collab-proxy 2.2.0, but 6000.5.5f1 wants 17.5.0 / 1.19.0 / 2.13.3. The older
`collab-proxy` and `inputsystem` use a `TreeView` API that Unity 6000.5 marked obsolete
*as an error*, so a straight template copy fails to compile with ~770 CS0619 errors —
none of them in your own code — and `packages-lock.json` freezes the bad versions so it
does not self-heal. `Packages/manifest.json` here is pinned to the versions listed in
`Editor/Data/Resources/PackageManager/Editor/manifest.json`, which is the editor's own
statement of what it wants. If you ever re-seed from a template, redo that.

## Milestone progress (spec §12)

| # | Step | State |
|---|---|---|
| 1 | Route DSL + arc-length projection | **done, verified bit-exact** |
| 2 | `hatch` with the §3.1 bicycle model at 120 Hz | **done, verified bit-exact** |
| 3 | One parking spot with §6 tolerances + alignment widget | **done** |
| 4 | Mission 1 end to end with §9 scoring | **done**, including traffic and pedestrian AI |
| 5 | Take mission 1 to final visual quality | not started |

## Layout

```
ParkingNightmare3D/
  Assets/Scripts/Core/        engine-free simulation core (asmdef PN3D.Core)
    MathX.cs                  JS-faithful math (JsRound, AngNorm, the `||` fallback)
    Json.cs                   dependency-free JSON reader
    MissionData.cs            Mission / RouteSeg, parsed from design-spec/data
    RouteEnricher.cs          the difficulty pass — see DESIGN_SPEC §5.1
    RouteCompiler.cs          DSL -> arc-length centreline + Project()
    VehicleDef.cs             handling constants, from design-spec/data/vehicles.json
    VehicleSim.cs             the §3.1 kinematic bicycle model
    Obb.cs                    oriented box: corners + point containment
    RoadGeom.cs               lane / parking-strip / sidewalk widths, road half-width
    ParkingSpot.cs            spot geometry per mission (§6)
    ParkChecker.cs            tolerance check + the 1.5 s settle hold (§6)
    GamePhase.cs              the run's single state value
    ShameSystem.cs            shame accrual, decay, thresholds (§10)
    StyleSystem.cs            style awards + combo multiplier (§10)
    SurfaceRules.cs           surface grip, curb, sidewalk, wrong way (§8, §10)
    Scoring.cs                end-of-run score, stars, S-rank, coins (§9)
    Rng.cs                    mulberry32, the seeded stream the AI draws from
    ObbCollision.cs           separating-axis test with MTV
    TrafficSystem.cs          ambient traffic, cross traffic, lights (§8.1)
    PedSystem.cs              pedestrian states: walk/film/cross/dive/soaked (§8.1)
    MissionRun.cs             owns all of the above in the reference's update order

  Assets/Scripts/Game/        Unity layer (asmdef PN3D.Game)
    Bootstrap.cs              builds the whole slice at runtime, from any scene
    WorldBuilder.cs           greybox road/sidewalk/spot/car geometry
    MissionDriver.cs          120 Hz FixedUpdate + render interpolation
    ChaseCamera.cs            chase view easing to overhead assist in the zone
    ActorViews.cs             greybox traffic cars and pedestrians
    Hud.cs                    shame meter, timer, style, alignment widget (IMGUI)
    DataPaths.cs              locates design-spec/data

  Assets/Scripts/Editor/      batch-mode tools (asmdef PN3D.EditorTools)
    SliceTools.cs             headless smoke test + screenshot capture
```

### Running it

Open the project and press Play in any scene — `Bootstrap` uses
`[RuntimeInitializeOnLoadMethod]` and builds the world, car, camera and HUD itself, so
there is no scene asset with serialized references to rot. WASD or arrows to drive,
space for handbrake. Milestone step 5 replaces this with an authored scene.

Headless, no editor interaction:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe" -batchmode -quit -nographics -projectPath unity/ParkingNightmare3D -executeMethod PN3D.EditorTools.SliceTools.SmokeTest -logFile smoke.log
```

That autopilots mission 1 from the start line to a completed park and prints the full
score breakdown — route, physics, parking and scoring through the same `MissionRun` the
game uses. `SliceTools.Capture` renders greybox screenshots (omit `-nographics`).

### Randomness

The reference's traffic and pedestrian AI call bare `Math.random()`; only the static
level layout uses a seeded stream. The port routes the AI through a seeded
`Rng` (mulberry32) instead. That is a deliberate improvement — it makes a run
reproducible, which the shareable challenge codes in §7 want anyway — and it is what
lets the harness diff actor behaviour frame by frame at all.

It also makes the test unusually strict. **Draw order is part of the contract**: both
sides must consume the same number of randoms in the same sequence, so short-circuit
evaluation has to be reproduced exactly. A car popped from the pool skips its kind roll;
`chance(0.45)` for lane choice is never evaluated on a one-lane road; the crosser spawn
rolls its chance *before* testing distance. Get any of those wrong and the streams
desynchronise permanently rather than drifting slightly — which is why the negative
control produces 30,000 failures rather than a handful.

One draw is deliberately left out: the traffic car's body colour. It has no simulation
effect, and putting a render concern inside the deterministic stream buys nothing — the
Unity layer derives colour from the car id instead. The *kind* draw right beside it is
reproduced, because kind sets the length and width the gap and collision maths use. The
pedestrian emote draw is also reproduced, since the reference takes it from the same
stream and skipping it would shift everything after.

### What is not in the slice yet

Static hazards and props — potholes, ramps, cones, puddles, ice patches — and the
per-vehicle gimmicks (hatch backfire, bus stop-arm and mirrors, tank turret, UFO tractor
beam). Their shame sources are wired (`ShameSystem.Pothole`, `Airborne`, `BusArm`,
`Mirror`, `SoakedPed`) but nothing raises them yet. `MissionRun.RegisterCollision` and
`PedSystem.Soak` are the seams.

`VehicleSim` covers the `car` drive model, which is 7 of the 9 vehicles. `tank` (§3.2)
and `ufo` (§3.3) are not ported yet — they are not needed until missions 10 and 11, and
the slice is mission 1.

`PN3D.Core` sets `"noEngineReferences": true`. That is load-bearing, not tidiness: it
makes the core compile without `UnityEngine`, which is what lets the desktop .NET
validator below compile the **same source files** the game runs. If someone adds a
`using UnityEngine` to Core, the asmdef fails the build immediately rather than silently
splitting the game and its tests into two code paths.

## Verifying the port

The port is checked against the shipping JavaScript rather than against hand-written
expectations. The generators extract the real functions out of `src/*.js` **by text** and
evaluate them, so the reference is the actual shipped code:

- `tools/gen_golden_routes.js` pulls `compileRoute` and `enrichRoute` from `n3_d.js` and
  dumps exact geometry for all 24 missions.
- `tools/gen_golden_physics.js` pulls `Vehicle3D.update` from `n3_e.js`, invokes it
  against a plain state object, and records state traces for 12 deterministic driving
  scenarios. The car branch never touches `game`, so passing null is safe.
- `tools/gen_golden_parking.js` pulls `World.buildDestination` from `n3_d.js` and
  `Game.parkingLogic` from `n3_e.js`. Those touch THREE, CarFactory, Assets, UI, SFX and
  the HUD, so it substitutes a universal no-op stub (a Proxy that is callable,
  constructible and assignable through any chain). What is left executing is exactly the
  geometry and the state machine.
- `tools/gen_golden_scoring.js` re-executes the scoring expression sequence and pulls
  `addShame` / `addStyle` / `surfaceLogic` from `n3_e.js`.
- `tools/gen_golden_actors.js` pulls the `Traffic` and `Peds` classes from `n3_d.js` and
  injects a seeded RNG in place of `Math.random` — see **Randomness** above.

Regenerate the references (only needed when the corresponding `src/*.js` changes):

```bash
for g in routes physics parking scoring actors; do node tools/gen_golden_$g.js; done
```

Run the diff:

```bash
dotnet run --project tools/Validator
```

Add `routes`, `physics`, `parking`, `scoring` or `actors` to run one suite. Current
result: **279,471 checks pass**, with a maximum relative deviation from JavaScript of
**1.7e-13** — and that worst case is `accF`, a finite difference that multiplies error by
120. Geometry and pose agree to around 1 ULP.

Coverage:

- **routes** — enriched segment lists, compiled length and point count, intersections,
  curves, `SampleAt` at 21 arc positions per mission, `Project` at 41 probes per mission
  including the hinted-vs-global search paths.
- **physics** — 12 scenarios (launch, full lock both ways, braking through zero into
  reverse, coast to stop, handbrake turn, keyboard slalom, analog slalom, low grip, slip
  recovery, reverse, creep kill), sampled every 20 steps across ~17,000 simulated steps,
  comparing pose, velocity, steer, swept steer command, slide, body attitude and the
  braking/reversing flags.

- **parking** — spot geometry for all 24 missions (position, heading, half-extents,
  lateral offset, arc position, zone arming distance), a 343-pose grid per spot type
  sweeping longitudinal, lateral and angular offsets through every tolerance boundary,
  and 6 settle scripts replayed frame by frame against the recorded state machine.
- **scoring** — 784 score cases crossing every clamp, sign and star boundary (all six
  line items, total, stars, S-rank, perfect, coins and the formatted times), 6 shame and
  style scripts covering the 25/50/75 thresholds, the 100 clamp, decay gating and the
  combo multiplier, and 5 `surfaceLogic` runs over road, sidewalk, grass, curb hopping
  and wrong-way driving.

Scoring is inlined in `Game.succeed` rather than being a function, so the generator
re-executes that expression sequence — and asserts each line of it is still present in
`n3_e.js` verbatim, so the transcription cannot silently drift from the source.

- **actors** — 6 scenarios (cruising, blocking the lane until traffic honks, driving into
  the oncoming stream, a shameful sidewalk run that gets filmed, a two-lane route with a
  lit intersection, and the ice cream jingle dragging pedestrians into the road), each
  replayed frame by frame: every car's id, kind, arc position, lane, speed, pose and
  honk/block/hit/panic timers; every crosser; every pedestrian's state, pose, phase and
  flags; the light phases; and the full event log of honks, dives and filming.

Every suite has been negative-controlled — perturbing the curve-tightening factor by
1.7%, rebuilding velocity from the pre-rotation heading, collapsing the settle hysteresis
to a single threshold, or eagerly evaluating one short-circuited `chance()` each produce
hundreds to tens of thousands of failures. A suite that cannot fail proves nothing.

## Conventions that must not drift

- **Physics is 2D.** `x, y` metres on a flat plane plus heading `h` in radians.
  Elevation is cosmetic. Do not model the car in 3D physics.
- **Position maps as `(x, elev, -y)` and heading as
  `Quaternion.Euler(0, 90f + h * Mathf.Rad2Deg, 0f)`.** The Z negation is not optional.
  Mapping `y -> +Z` with yaw `90 - h` (what DESIGN_SPEC §2 originally said) still gives
  the car a correct forward vector, but mirrors the whole world, because Unity is
  left-handed and Three.js is not — the player ends up driving on the left with every
  curbside spot on the wrong side of the road. The simulation cannot detect this; it is
  2D and internally consistent either way. Full derivation is in §2.
- **Mirroring flips triangle winding**, so `WorldBuilder.Ribbon` emits the order it does
  *because* of the Z negation. Change one and you must change the other, or every road
  surface faces the ground and is backface-culled.
- **120 Hz fixed step.** `ProjectSettings/TimeManager.asset` is committed with
  `Fixed Timestep: 0.008333334` and `Maximum Allowed Timestep: 0.083333336`. The second
  one reproduces the reference loop's 10-substep clamp
  (`if (n >= 10) _acc = 0`, src/n3_f.js:977), which discards time debt rather than
  letting the simulation spiral.
- **No `WheelCollider`, no PhysX** for vehicles. The feel is a hand-tuned kinematic
  model; substituting real vehicle physics invalidates every par time and star threshold.
- **Enrich before compiling.** `data/missions.json` is pre-enrichment. Use
  `RouteCompiler.CompileMission`, which does both in order. See DESIGN_SPEC §5.1.

## Mission data

`Assets/Scripts/Core` reads `design-spec/data/*.json` — those files stay the single
source of truth and are not duplicated into the project. Wiring them in as a runtime
asset (StreamingAssets or an addressable) comes with milestone step 4; the validator
currently reads them straight off disk.
