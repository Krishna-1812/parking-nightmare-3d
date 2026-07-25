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
| 2 | `hatch` with the §3.1 bicycle model at 120 Hz | not started |
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
```

`PN3D.Core` sets `"noEngineReferences": true`. That is load-bearing, not tidiness: it
makes the core compile without `UnityEngine`, which is what lets the desktop .NET
validator below compile the **same source files** the game runs. If someone adds a
`using UnityEngine` to Core, the asmdef fails the build immediately rather than silently
splitting the game and its tests into two code paths.

## Verifying the port

The route compiler is checked against the shipping JavaScript rather than against
hand-written expectations. `tools/gen_golden_routes.js` extracts the real `compileRoute`
and `enrichRoute` out of `src/n3_d.js` **by text** and evaluates them, so the reference
is the actual shipped functions, then dumps exact geometry for all 24 missions.

Regenerate the reference (only needed if `src/n3_d.js` changes):

```bash
node tools/gen_golden_routes.js
```

Run the diff:

```bash
dotnet run --project tools/RouteValidator
```

Current result: **10,968 checks across 24 missions pass**, with a maximum relative
deviation from JavaScript of **3.1e-16** — roughly 1.4 ULP, i.e. the two implementations
agree to the limit of double precision. The harness covers enriched segment lists,
compiled length and point count, intersections, curves, `SampleAt` at 21 arc positions
per mission, and `Project` at 41 probes per mission including the hinted-vs-global
search paths.

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
