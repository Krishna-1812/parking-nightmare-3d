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
| 5 | Take mission 1 to final visual quality | **done** — see **Art pipeline** below |

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
    MissionHost.cs            assembles one mission: world, driver, camera, HUD
    Bootstrap.cs              fallback entry point when a scene has no MissionHost
    AppConfig.cs              process-wide runtime settings; forces 60fps on mobile
    WorldBuilder.cs           orchestrates the world; owns the coordinate mapping
    MissionDriver.cs          120 Hz FixedUpdate + render interpolation
    ChaseCamera.cs            chase view easing to overhead assist in the zone
    ActorViews.cs             traffic cars and jointed pedestrians
    HudUI.cs                  binds the UI Toolkit HUD to the run
    TouchControls.cs          on-screen wheel and pedals, ported from n3_b.js
    FatalOverlay.cs           last-resort IMGUI error display for release builds
    DataPaths.cs              locates the mission data (Resources, then the repo)

  Assets/Scripts/Game/Art/    the art pipeline (see below)
    Raster.cs                 software rasteriser: the Canvas 2D subset the painters need
    ProcTex.cs                the surface painters, ported from Assets.* in n3_c.js
    District.cs               palette from design-spec/data/districts.json
    MatLib.cs                 URP material factory, cached by key
    Geo.cs                    mesh primitives: cube, cylinder, faceted blob, gable roof
    RoadBuilder.cs            carriageway, curbs, sidewalks, cross streets, the bay
    Scenery.cs                houses, trees, mailboxes, hedges, bins, power lines
    CarView.cs                the superellipse hull generator and vehicle assembly
    SceneEnv.cs               sky, sun, ambient, fog, horizon ring
    PostFx.cs                 the volume profile and camera setup

  Assets/Shaders/
    PN3D_SkyGradient.shader   three-stop skybox from the district palette
    PN3D_Silhouette.shader    unlit alpha silhouette for the horizon ring

  Assets/Resources/UI/        HUD assets, under Resources so script-built worlds find them
    Hud.uxml / Hud.uss        the HUD tree and its styling
    PN3D_PanelSettings.asset  generated by PN3D/Rebuild Mission 1 Scene

  Assets/Scenes/
    Mission01.unity           generated and committed; see SceneBuilder.cs

  Assets/Scripts/Editor/      batch-mode tools (asmdef PN3D.EditorTools)
    SliceTools.cs             headless smoke test + world screenshot capture
    SceneBuilder.cs           regenerates Mission01.unity and the panel settings
    PlayCapture.cs            play-mode run that captures the HUD
    AndroidBuild.cs           Android player settings + APK / AAB build
    DesktopBuild.cs           Windows player, the fast diagnostic loop
    DataSync.cs               mirrors design-spec/data into Resources
```

### Running it

Open `Assets/Scenes/Mission01.unity` and press Play. WASD or arrows to drive, space for
handbrake. Pressing Play in any *other* scene also works: `Bootstrap` notices there is no
`MissionHost` and spawns one, which is what the editor tooling relies on.

On a touch platform the HUD also shows a rotary steering wheel and gas / brake /
handbrake pads (`TouchControls`). They are hidden in the editor; call
`Driver.Touch.ForceEnable()` to exercise them with the mouse. Their input does **not**
come from UI Toolkit pointer events — it is read off the `Touchscreen` device and
hit-tested against each element's rect, because UI Toolkit's runtime panel routes one
primary pointer and holding gas while turning the wheel is two fingers at once.

Headless, no editor interaction:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe" -batchmode -quit -nographics -projectPath unity/ParkingNightmare3D -executeMethod PN3D.EditorTools.SliceTools.SmokeTest -logFile smoke.log
```

That autopilots mission 1 from the start line to a completed park and prints the full
score breakdown — route, physics, parking and scoring through the same `MissionRun` the
game uses. `SliceTools.Capture` renders world screenshots (omit `-nographics`).

To capture the HUD you have to really play the scene, because UI Toolkit draws to a
screen overlay panel that a camera render to a RenderTexture cannot see:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe" -projectPath unity/ParkingNightmare3D -executeMethod PN3D.EditorTools.PlayCapture.Run -pn3dOut shots -pn3dExit -logFile hud.log
```

**That one is deliberately not `-batchmode`, and there are two independent reasons.**
`WaitForEndOfFrame` never resumes under `-batchmode`, so a screenshot taken from a
coroutine either hangs or lands before the overlay panel has drawn. And `-quit` cannot be
used either — it tears the editor down before play mode starts — so the runner calls
`EditorApplication.Exit` itself once it is finished. A third trap, if you write anything
similar: entering play mode reloads the domain, which silently drops static event
subscriptions, so the handoff goes through `SessionState` rather than a
`playModeStateChanged` handler registered just before the transition.

### Android build

The Android module is **not** part of a stock editor install — `Unity Hub.exe -- --headless
install-modules --version 6000.5.5f1 --module android --childModules` adds it, together
with the SDK, NDK and OpenJDK, for about 7.7 GB on disk.

Everything Play cares about is set from script rather than clicked, in
`Assets/Scripts/Editor/AndroidBuild.cs`, because the project arrived carrying the URP
template's defaults and several of them produce a binary Google rejects outright:

| Setting | Template default | Here | Why |
|---|---|---|---|
| Architecture | ARMv7 only | ARM64 | 64-bit has been mandatory on Play since 2019 |
| Scripting backend | Mono | IL2CPP | forced by ARM64 |
| Package id | `com.UnityTechnologies.com.unity.template.urpblank` | `com.krishnaladha.parkingnightmare3d` | **permanent after the first Play upload** |
| Min SDK | 22 | 26 | 24 is requested; 6000.5.5f1 clamps up to its own floor of 26 |
| Orientation | auto-rotate, all four | landscape only | portrait would reframe the parking approach mid-mission |
| Signing | debug keystore | env-var keystore, debug if unset | Play rejects debug-signed uploads |

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe" -quit -batchmode -nographics -projectPath unity/ParkingNightmare3D -buildTarget Android -executeMethod PN3D.EditorTools.AndroidBuild.BuildApk -logFile android-build.log
```

`BuildAab` instead of `BuildApk` produces the App Bundle Play wants. Unlike the HUD
capture, a player build **is** happy in `-batchmode`: it never enters play mode, so it
never hits the assembly reload that wedges headless Unity. Pass `-buildTarget Android` so
the editor opens already switched rather than switching mid-`executeMethod`.

Release signing is read from the environment, so no password is ever committed:

```bash
export PN3D_KEYSTORE=/c/Users/<you>/.pn3d-keys/upload.keystore
export PN3D_KEYSTORE_PASS=... PN3D_KEYALIAS=upload PN3D_KEYALIAS_PASS=...
```

With `PN3D_KEYSTORE` unset the build signs with Unity's debug keystore — installable over
`adb`, rejected by Play. Create the real one with the JDK the Android module already
installed, and **keep it somewhere backed up**: lose it and the listing can never be
updated again.

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool" -genkeypair -v -keystore upload.keystore -alias upload -keyalg RSA -keysize 2048 -validity 10000
```

`adb` ships with the module, at
`Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`.

#### Connecting the device over Wi-Fi

Preferred over USB — no cable to be flaky, and a reinstall is ~12 s for a 30 MB APK.
Enable **Developer options → Wireless debugging**, tap *Pair device with pairing code*,
then:

```bash
adb pair <ip>:<pairing-port> <code> && adb mdns services && adb connect <ip>:<connect-port>
```

Two traps, both of which cost time here:

- **The pairing port and the connect port are different**, and the IP the pairing dialog
  shows can be for a *different interface* than the one you can reach. `adb mdns services`
  lists the true address for both `_adb-tls-pairing._tcp` and `_adb-tls-connect._tcp` —
  read it from there rather than off the phone's screen.
- **`adb pair` fails with `protocol fault (couldn't read status message)` when the network
  is blocking client-to-client traffic**, which reads like a bad code but is not. Confirm
  with `ping`: a reply of *"Destination host unreachable"* from your own IP, plus a null
  MAC in `Get-NetNeighbor`, means ARP got no answer and the router has AP isolation on.
  Turning the phone's **hotspot** on and joining the PC to it sidesteps the router
  entirely and is the fastest fix.

Because the phone is then the gateway, its own address is *not* `x.x.x.1` — on the OnePlus
here it took `.140` while the PC got `.179`. Trust mDNS, not the convention.

#### Debugging a player build

**An Android release build logs nothing under the `Unity` tag**, so a crash and a
mis-aimed camera look identical from the outside. `FatalOverlay` exists for this:
`MissionHost` catches anything thrown out of world construction and draws the exception on
screen in IMGUI, which has no dependency on the HUD assets that might themselves be what
failed.

**Filter logcat by PID, not by tag.** An earlier note here claimed ColorOS drops
third-party output entirely. It does not — that conclusion came from filtering on `-s
Unity`, which is silent on a release build, and reading nothing. The framework tags are
all there and they are worth a great deal:

```bash
adb -s <serial> logcat -d --pid=$(adb -s <serial> shell pidof com.krishnaladha.parkingnightmare3d)
```

`VRI[UnityPlayerActivity]` gives surface size and rotation, and
`DynamicFramerate`/`ViewRootImplExtImpl` log every `MotionEvent` the window receives with
its action code. That is how the dead touch controls were pinned down: the events were
provably arriving at the Unity view (`action = 0` then `action = 1`) while the game did
nothing, which ruled out adb injection and pointed straight at the wiring.

The **Windows player** is still the right loop for anything that isn't touch- or
device-specific, because it is five times faster and writes a real `Player.log`:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe" -quit -batchmode -nographics -projectPath unity/ParkingNightmare3D -buildTarget Win64 -executeMethod PN3D.EditorTools.DesktopBuild.BuildWindows -logFile win.log
```

It builds in **under a minute warm** against 12–18 for Android IL2CPP, writes a real
`Player.log`, and reproduces what actually differs from the editor: shader variant
stripping, engine code stripping, Resources-only data loading, and runtime-created
materials with no material assets for the stripper to learn from. It is not a shipping
target — it exists so a device bug can be reproduced in a minute instead of twenty.
Run it with `-logFile <path>` and close it with `CloseMainWindow`, not `Kill`, or the tail
of the log is lost.

### Art pipeline

There are **no imported art assets**. Every texture is painted into a `Texture2D` at load
and every mesh is generated, exactly as the web build does it — `Raster.cs` implements
the slice of the Canvas 2D API the reference's painters use, and `ProcTex.cs` ports those
painters one for one. DESIGN_SPEC §11 originally said to rebuild rendering with imported
GLTF/FBX assets and baked lightmaps; that advice is superseded for this slice, and the
reasoning is worth keeping:

- **The painters are the art source.** Porting them keeps the two builds looking like the
  same game and makes a district palette swap a data change on both sides. Exported PNGs
  would immediately start drifting from the JavaScript that generated them.
- **The repo stays diffable and licence-free.** Nothing binary, nothing to re-export when
  a colour changes.
- **Baked lighting would pin the scene to one district.** Mission 1 is SLEEPY SUBURBS, but
  the same scene and the same builders serve all six palettes; a lightmap baked for the
  suburbs sun is wrong for the other five.

The trade is real: no lightmaps means no bounce lighting or contact AO, and the shading
leans on the trilight ambient plus a skybox reflection probe. Two things that came out of
tuning that and are easy to get wrong again:

- `DynamicGI.UpdateEnvironment()` must be called after assigning the skybox material from
  script, or the reflection probe is never generated and everything smooth — car paint,
  glass, wheel hubs — renders black.
- The ambient equator band lights every surface facing away from the sun. Taking it
  straight from the sky colour makes those surfaces grey-blue whatever they are painted,
  which had the orange hatchback looking like bare primer.

Textures are painted from a seeded `Rng` rather than `Math.random`, so two machines
building the same commit get byte-identical results. The reference re-rolls its noise on
every page load; here the art is part of the build, and art that changes between builds
cannot be reviewed in a diff.

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

Audio is entirely absent, and so is everything in §11 that needs a store account behind
it: IAP, rewarded video, native leaderboards, analytics and cloud save. The Android build
described above is a plain unsigned-for-Play player build — it proves the game runs on
device, not that it is ready to list.

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
- **Every shader must be listed in Always Included Shaders**
  (`ProjectSettings/GraphicsSettings.asset`) — `PN3D/SkyGradient`, `PN3D/Silhouette`,
  **and URP's own `Lit` and `Unlit`.** There is not one material asset in this project;
  `MatLib` creates all of them at runtime, so from the build pipeline's point of view no
  shader is referenced by anything and none get shipped. The first Android build proved
  it: `Shader.Find("Universal Render Pipeline/Lit")` returned null on device,
  `new Material(null)` threw inside `BuildGround`, and the app ran at a locked 60fps
  showing Unity's default skybox and nothing else. `MatLib.Resolve` now throws with the
  reason, and `AndroidBuild.AssertShadersIncluded` fails the build if the list is undone.
- **The on-screen wheel is not analog steering.** `SteerAnalog` stays false for it, only
  tilt sets it true. That looks wrong — the wheel *is* an absolute position — but
  src/n3_b.js:694 does exactly this on purpose so the wheel keeps the keyboard's feel,
  and the par times were tuned against it.
- **Flip Y before `RuntimePanelUtils.ScreenToPanel`.** The Input System reports screen
  space bottom-left origin, UI Toolkit panel space is top-left, and `ScreenToPanel` only
  undoes the panel's *scaling* — it does not flip. Miss it and every hit test against a
  `VisualElement` is mirrored vertically while rendering looks perfect: on device, pressing
  GO where it is drawn did nothing and pressing the empty sky above it pressed GO.
  `TouchControls.ToPanel` is the only place this conversion should happen.
- **Assign fields before `AddComponent` returns, not after.** `AddComponent` runs `OnEnable`
  synchronously, so `host.AddComponent<HudUI>(); hud.Driver = driver;` leaves `Driver` null
  for the whole of `OnEnable`. That silently cost the touch controls their driver reference
  — they rendered and did nothing, because `TouchControls` unhides itself in its own
  constructor. `HudUI.Attach` now collects `hud.Touch` afterwards instead of pushing it
  from inside.

## Mission data

`design-spec/data/*.json` stays the single source of truth and is still never edited in
two places. `PN3D.EditorTools.DataSync` **mirrors** it into `Assets/Resources/Data/*.txt`
on editor load and again at the top of every player build; the mirror is generated and
gitignored, so it cannot drift. `DataPaths.Load` reads the mirror, falling back to the
repo copy on disk so a fresh clone still plays before the sync has ever run. The
validator reads `design-spec/data` directly, as before.

**Resources, not StreamingAssets** — the original plan. On Android, StreamingAssets lives
inside the compressed APK and has no filesystem path at all: `File.Exists` returns false
and the only way in is an async `UnityWebRequest`. The files are small, wanted
synchronously in `Awake`, and never patched at runtime, which is the case Resources is
for. Revisit only if mission data ever needs to ship separately from the binary.
