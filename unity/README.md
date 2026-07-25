# Unity rebuild

Vertical slice of Parking Nightmare 3D for Google Play and the App Store, following
`design-spec/DESIGN_SPEC.md` §11–§12. The web build stays the reference implementation:
where this and the JavaScript disagree, the JavaScript is right.

- **Editor:** Unity 6000.5.5f1 (Unity 6 LTS)
- **Project:** `unity/ParkingNightmare3D`
- **Pipeline:** URP, from the editor's own `3d-cross-platform` (mobile) template, so the
  URP assets and quality tiers are the ones Unity ships rather than hand-authored ones

## Milestone progress (spec §12)

| # | Step | State |
|---|---|---|
| 1 | Route DSL + arc-length projection | **done, verified bit-exact** |
| 2 | `hatch` with the §3.1 bicycle model at 120 Hz | **done, verified bit-exact** |
| 3 | One parking spot with §6 tolerances + alignment widget | not started |
| 4 | Mission 1 end to end with §9 scoring | not started |
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
```

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

Regenerate the references (only needed when the corresponding `src/*.js` changes):

```bash
node tools/gen_golden_routes.js && node tools/gen_golden_physics.js
```

Run the diff:

```bash
dotnet run --project tools/Validator
```

Add `routes` or `physics` to run one suite. Current result: **24,603 checks pass**, with
a maximum relative deviation from JavaScript of **1.7e-13** — and that worst case is
`accF`, a finite difference that multiplies error by 120. Geometry and pose agree to
around 1 ULP.

Coverage:

- **routes** — enriched segment lists, compiled length and point count, intersections,
  curves, `SampleAt` at 21 arc positions per mission, `Project` at 41 probes per mission
  including the hinted-vs-global search paths.
- **physics** — 12 scenarios (launch, full lock both ways, braking through zero into
  reverse, coast to stop, handbrake turn, keyboard slalom, analog slalom, low grip, slip
  recovery, reverse, creep kill), sampled every 20 steps across ~17,000 simulated steps,
  comparing pose, velocity, steer, swept steer command, slide, body attitude and the
  braking/reversing flags.

Both suites have been negative-controlled — perturbing the curve-tightening factor by
1.7%, or rebuilding velocity from the pre-rotation heading, each produce thousands of
failures. A suite that cannot fail proves nothing.

## Conventions that must not drift

- **Physics is 2D.** `x, y` metres on a flat plane plus heading `h` in radians.
  Elevation is cosmetic. Do not model the car in 3D physics.
- **Heading maps as `Quaternion.Euler(0, 90f - h * Mathf.Rad2Deg, 0f)`**, position as
  `(x, elev, y)`. Unity is left-handed — this is *not* the Three.js `-h`.
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
