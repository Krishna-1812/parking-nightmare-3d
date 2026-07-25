# Parking Nightmare 3D — Design Spec

**Purpose.** This document is the portable definition of the game, written so it can be
rebuilt in Unity (or anywhere) without reading the JavaScript. Every number here was
extracted from the shipping web build, not remembered or re-derived. The web build stays
live at https://krishna-1812.github.io/parking-nightmare-3d/index.html as the reference
implementation — when this document and the code disagree, the code is right and this
document is stale.

Machine-readable data lives beside this file in `data/`:

| File | Contents |
|---|---|
| `data/vehicles.json` | All 9 vehicles with handling constants, unlocks, prices |
| `data/districts.json` | All 6 districts with full colour palettes and lighting |
| `data/missions.json` | All 24 missions with routes, par times, star thresholds |

Those are exported verbatim from the source literals by `extract_spec.js`. Unity can load
them directly with `JsonUtility` or Newtonsoft — they are intended as runtime assets, not
just documentation.

---

## 1. What the game is

An arcade parking game about **social humiliation rather than damage**. You drive a
comically unsuitable vehicle along a city route to a marked parking spot, then park it
precisely. The pressure comes from a **Shame meter** that fills when you embarrass
yourself in public — hitting things, mounting curbs, blocking traffic. Fill it to 100%
and you fail, even with a pristine car.

Core loop, in order:

1. **Brief** — mission card, vehicle, flavour text
2. **Countdown** — 3·2·1
3. **Drive** — follow the GPS route, dodge traffic and pedestrians, earn Style points
4. **Park** — a glowing spot appears; slot in within tolerance and hold still 1.5s
5. **Results** — score breakdown, 1–3 stars, coins, optional replay
6. **Spend** — coins unlock vehicles in the Garage

Tone is deadpan-comic. Vehicle flavour text ("Runs on hope and expired coupons"), comic-book
damage popups (BONK!, INSURANCE!, THE FERRARI!!), an escalating crowd that starts *staring*,
then *filming*, then *gathers*. The humour is core to the product, not decoration — keep it.

---

## 2. Coordinate system and timestep

This is the first thing to get right in Unity, because everything else inherits from it.

**Physics is 2D.** The simulation tracks `x, y` in **metres** on a flat plane plus a heading
`h` in **radians**. Elevation is cosmetic only (ramps, hover, bounce) and never affects the
simulation. Do not model the car in 3D physics.

**Heading convention.** `h` is a standard math-convention angle: forward is
`(cos h, sin h)`, counter-clockwise positive.

**Mapping to Unity.** World X = `x`, world Z = **`−y`**, world Y = elevation.

```csharp
// physics (x, y) in metres, heading h in radians (CCW, +X forward)
transform.position = new Vector3(x, elev, -y);
transform.rotation = Quaternion.Euler(0f, 90f + h * Mathf.Rad2Deg, 0f);
```

**The Z negation is load-bearing.** An earlier version of this section said
`position = (x, elev, y)` with `θ = 90° − h`. That gives the car a correct forward
vector — `(cos h, 0, sin h)` — and looks right in isolation, but it **mirrors the entire
world**, because Unity is left-handed and Three.js is not:

- In Unity a camera looking along +X with up +Y has `right = −Z`, since a +90° yaw sends
  +Z to +X and +X to −Z.
- In Three.js the same setup has `right = forward × up = +Z`.

So `+t` — defined in the route frame as "right side of travel direction, the side the
player drives on" — lands on screen-*left* in Unity and screen-*right* in the web build.
The game silently becomes left-hand-traffic: the player drives on the wrong side and
every curbside parallel spot (all of which sit at `t > 0`) appears on the wrong side of
the road. Nothing in the simulation notices, because the simulation is 2D and internally
consistent either way; only the render is wrong.

Derivation for the corrected form: mapped velocity is `(cos h, 0, −sin h)`, and a Unity
yaw θ sends +Z to `(sin θ, 0, cos θ)`, so `sin θ = cos h` and `cos θ = −sin h`, giving
`θ = 90° + h`. Verified in the Unity build by checking which side of the centre line the
car and the parallel spot render on.

**Fixed timestep is 1/120 s (120 Hz).** The loop is a classic accumulator:

```
accumulator += frameDelta
while accumulator >= 1/120 and substeps < 10:
    FixedStep(1/120)
    accumulator -= 1/120
Render(alpha = accumulator / (1/120))
```

Rendering interpolates between previous and current transform by `alpha`. Unity's default
`fixedDeltaTime` is 0.02 (50 Hz) — **set it to `1f/120f` or the handling will feel wrong**,
particularly the steering rate limit and the lateral grip decay, both of which are
per-step exponential terms. The 10-substep clamp matters too: it is what stops a hitch
from teleporting the car.

**Road geometry constants**

| Constant | Value | Meaning |
|---|---|---|
| `LANE_W` | 3.5 m | One traffic lane |
| `PARK_STRIP` | 2.3 m | Curbside parking lane, each side |
| `SIDEWALK_W` | 3.0 m | Sidewalk width |
| `RW` | `lanes * 3.5 + 2.3` | Road half-width used for curb measurement |

---

## 3. Vehicle physics

Three distinct drive models. **Do not use Unity's `WheelCollider` or PhysX for any of
them.** The game's feel is a hand-tuned kinematic model; substituting real vehicle physics
will change the character of every mission and invalidate every par time and star
threshold. Port these equations literally into a script that runs in `FixedUpdate` and
writes to a kinematic transform.

### 3.1 Cars (`drive: 'car'` — 7 of 9 vehicles)

A kinematic bicycle model in body frame. Per step, with `vF` = forward velocity,
`vL` = lateral velocity, `d` = the vehicle definition:

**Steering** — speed-sensitive, so the car calms down at speed:

```
steerScale = 1 / (1 + |vF| * 0.098)
target     = steerInput * maxSteer * steerScale        // maxSteer = 38°
steer      = MoveTowards(steer, target, d.steerSpeed * dt)   // rate-limited rack
```

**Engine and brakes** — power-limited, tapering near top speed:

```
maxSp      = d.maxSpeed * (surfaceGrip < 1 ? surfaceGrip * 1.1 : 1)
brakeDecel = min(11, 4.5 + d.accel * 0.9)

throttle > 0:  if vF < -0.45 -> vF = min(0, vF + brakeDecel * thr * dt)  (braking out of reverse)
               else          -> spFrac = clamp(vF / maxSp, 0, 1)
                                vF += d.accel * thr * (1.15 - 0.75 * spFrac²) * dt
throttle < 0:  if vF > 0.45  -> vF = max(0, vF + brakeDecel * thr * dt)  (braking)
               else          -> vF += d.accel * 0.55 * thr * dt   (reverse)
```

The `min`/`max` clamps matter: braking stops the car *at* zero rather than punching
through into the opposite direction on a single step.

**Resistance** — quadratic aero calibrated so top speed converges on `maxSpeed`:

```
kAero  = (d.accel * 0.4) / d.maxSpeed²
resist = kAero * vF * |vF| + sign(vF) * 0.35
if throttle == 0: resist += vF * 0.18            // engine braking
if |vF| > 0.05:                                   // gated: no resistance at a standstill
    nv = vF - resist * dt
    vF = (nv * vF < 0) ? 0 : nv                   // never allowed to cross zero
if |vF| < 0.09 and throttle == 0: vF *= (1 - 8 * dt)     // creep kill
vF = clamp(vF, -maxSp * 0.3, maxSp * 1.02)        // reverse capped at 30%
```

**Grip and rotation:**

```
grip = d.grip * surfaceGrip
if slipTimer > 0: grip *= 0.35                    // ice / oil
if handbrake:     grip *= 0.1;  vF *= (1 - 1.9 * dt)
vL *= max(0, 1 - grip * 9.5 * dt)                 // lateral velocity bleed
slideAmt = |vL|                                    // drives tyre smoke + skid audio
if |vF| > 0.08: h += (vF / d.wb) * tan(steer) * dt

// rebuild world velocity from the NEW heading, after the rotation above
vx = cos(h) * vF - sin(h) * vL
vy = sin(h) * vF + cos(h) * vL
```

**Order matters here.** Reconstructing `vx, vy` from the pre-rotation heading is the
classic way to get a port that looks correct and drifts: it injects a phantom lateral
velocity every step, so `slideAmt` sits around 0.2 in a steady turn instead of ~0, and
the car smears sideways out of every corner. Position integrates last, for all three
drive models: `x += vx * dt; y += vy * dt`.

**Cosmetic body attitude** (visual only, but the game reads much worse without it):

```
accF  = lerp(accF, (vF - vFprev) / dt, clamp(7 * dt, 0, 1))
pitch = clamp(accF * 0.0055, -0.055, 0.035)                       // nose dive / squat
latA  = vF² * tan(steer) / d.wb
roll  = lerp(roll, clamp(latA * 0.0042, -0.05, 0.05), clamp(6*dt,0,1))
```

### 3.2 Tank (`drive: 'tank'`)

Differential steer — rotates in place, slows its turn as it speeds up:

```
rotSpeed = 1.6 * (1 - |vF| / (maxSp * 2.2))
h += steerInput * rotSpeed * dt * (vF < -0.45 ? -1 : 1)   // reverse inverts steering
kTr = (d.accel * 0.5) / d.maxSpeed²
res = kTr * vF * |vF| + sign(vF) * 0.6
if throttle == 0: res += vF * 0.4
vF = clamp(vF, -maxSp * 0.6, maxSp)
velocity = forward * vF          // zero lateral slip, always
```

Plus a gag: the turret tracks the nearest pedestrian, and within 8 m with the turret
aligned inside 0.35 rad there is a 2%-per-step chance the pedestrian dives for cover.

### 3.3 UFO (`drive: 'ufo'`)

Frictionless — `grip: 0`, no brakes, thrust in the facing direction. Deliberately awful
and it is one of the funniest things in the game.

```
h += steerInput * 2.4 * dt
velocity += forward * d.accel * throttle * dt
if |velocity| > maxSp: rescale to maxSp
velocity *= (1 - 0.05 * dt)                  // near-zero drag
if handbrake: velocity *= (1 - 1.1 * dt)     // "brake" is barely a suggestion
```

The UFO is also exempt from the parking angle check (§6) because holding a heading is
impossible.

### 3.4 Vehicle roster

Full data in `data/vehicles.json`. `wb` = wheelbase, `fragility` scales damage taken.

| Key | Name | Len | Wid | wb | maxSpeed | accel | steerSpeed | grip | mass | frag | Unlock | Price |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `hatch` | Rusty Hatchback | 3.9 | 1.78 | 2.5 | 19 | 7.5 | 3.4 | 0.95 | 1 | 1 | start | — |
| `wagon` | Family Wagon | 5.0 | 1.88 | 3.1 | 18 | 7 | 3.0 | 0.95 | 1.3 | 1 | level 3 | 900 |
| `limo` | Stretch Limo | 8.6 | 1.95 | 6.3 | 17 | 6 | 2.6 | 0.96 | 2 | 1.2 | level 5 | 1800 |
| `icecream` | Ice Cream Truck | 5.8 | 2.15 | 3.7 | 15 | 6 | 2.8 | 0.93 | 1.7 | 1 | level 6 | 2600 |
| `bus` | School Bus | 10.6 | 2.45 | 7.2 | 14 | 5 | 2.3 | 0.96 | 3 | 0.8 | level 8 | 3600 |
| `tank` | Tank | 6.6 | 3.2 | 4.5 | 9 | 6 | 3.0 | 1 | 8 | 0.2 | level 10 | 5200 |
| `ufo` | UFO | 4.6 | 4.6 | 3 | 18 | 7 | 3.0 | 0 | 0.8 | 1 | 30 stars | 7500 |
| `kart` | Go-Kart | 2.3 | 1.35 | 1.6 | 17 | 9.5 | 4.4 | 1.05 | 0.45 | 1.7 | 12 stars | 1500 |
| `monster` | Monster Pickup | 5.7 | 2.7 | 3.6 | 16 | 8 | 2.7 | 0.9 | 4.2 | 0.4 | level 14 | 5000 |

Speeds are m/s — 19 m/s ≈ 68 km/h. These are deliberately low; the game is about precision,
not speed. Each vehicle also carries a `stats` block (`size / speed / hand / chaos`, 0–100)
used purely for the Garage comparison bars, and a one-line `flavor` string.

**Per-vehicle gimmicks** (small, cheap, and much of the personality):
- `hatch` — backfires every 6–14 s: bang, smoke puff, spark burst
- `bus` — stop-arm swings out on a 3.5–5 s / 7–13 s cycle and **becomes a real collider**;
  mirrors are also separate colliders that count for collisions
- `icecream` — jingle loop that cannot be silenced
- `tank` — turret tracking; takes 15% damage; deals 1.6× shame
- `ufo` — hover bob, tractor beam on successful park

---

## 4. Steering input

Three input paths converge on one signed `steer` value in `[-1, 1]`.

**Keyboard** (`A`/`D`/arrows) gives a hard ±1. It is swept, not applied directly, so the
wheel behaves like a hand is on it — slower to turn in, quicker to return:

```
steerLam = |raw| > |cmd| ? 3.9 : 8.0            // attack : release
cmd += (raw - cmd) * (1 - exp(-steerLam * dt))
if |cmd| < 0.001 and raw == 0: cmd = 0          // snap, or it decays asymptotically
```

**On-screen wheel** (touch drag) maps drag angle over ±120° to ±1 and springs back at
`angle *= 0.76` per frame when released.

**Tilt** (gyroscope) — read this section before reimplementing it, because two bugs here
were shipped and reported by players.

Project gravity onto the screen-right axis rather than switching on orientation. The
original implementation used a per-orientation sign table and **had both landscape cases
inverted**, so tilting right steered left. Do not use a sign table.

```
gx = cos(beta) * sin(gamma)              // gravity in device axes
gy = -sin(beta)
theta = screen rotation angle            // 0 / 90 / 180 / 270
right = gx * cos(theta) - gy * sin(theta)
if invertSetting: right = -right
raw = asin(clamp(right, -1, 1)) in degrees
```

Filter on **elapsed time, not per event** — a fixed per-event coefficient makes lag scale
with the sensor's report rate, so a 30 Hz phone felt twice as laggy as a 60 Hz one:

```
smooth += (raw - smooth) * (1 - exp(-dtMs / 40))
t = smooth - zero                        // zero = calibrated neutral grip
dead = 3°,  span = 26 + (1 - sensitivity) * 44      // sensitivity 0..1, default 0.5
mag = clamp((|t| - dead) / (span - dead), 0, 1)
steer = sign(t) * mag^1.4
```

The `^1.4` exponent matters: `^2.0` made the first third of a lean produce almost nothing,
which players reported as latency even though it was curve shape. At the default
sensitivity a 15° lean gives 16% steering and full lock needs about 51°.

**Critically — tilt must bypass the keyboard sweep.** Tilt is already an absolute wheel
position held by the player's hand; running it through the `λ=3.9` attack added ~590 ms of
pure lag and was the single largest cause of reported tilt latency. Detect analog input and
use `steerLam = 20`:

| Path | Time for car to reach 90% of commanded steer |
|---|---|
| Tilt through keyboard sweep (the shipped bug) | 786 ms |
| Tilt with analog routing | 178 ms |

Keep the neutral-grip calibration ("this is straight ahead") and offer it somewhere the
player is stationary — in the web build it lives in the pause menu. Also recalibrate on
`orientationchange`. Expose **sensitivity** as a slider and **invert direction** as a
toggle; device orientation conventions vary enough that the invert escape hatch is worth
having.

---

## 5. Routes

Routes are authored as a small DSL, then compiled to an arc-length-parameterised
centreline. This is worth keeping — it made 24 hand-tuned missions cheap to write and
is far better than hand-placing spline points in the Unity editor.

| Segment | Meaning |
|---|---|
| `{t:'S', len}` | Straight, `len` metres |
| `{t:'L', r, a}` / `{t:'R', r, a}` | Arc, radius `r` m, sweep `a` degrees |
| `{t:'X', lights}` | Intersection, optionally with traffic lights |

The compiler produces a polyline plus a `project(x, y)` function returning
`{ distanceAlong, lateralOffset }`. Everything downstream depends on that projection:
GPS arrow, distance-to-go, off-road detection, curb-gap measurement, prop placement,
traffic spawning. **Build this first in Unity** — it is load-bearing.

Compiled campaign routes run **0.40–1.12 km**; every one except mission 1 falls in
0.67–1.12 km. Corner radii after the difficulty pass described in §5.1 are **20–30 m**
on all 23 non-tutorial missions; mission 1 keeps its authored 40 m because it is exempt.
Segment counts per mission are in the table in §7. Procedural generators exist for Free
Roam and Daily Challenge — 13–18 segments, radii 24–38 m, always ending in a long
straight (190–210 m) so the parking spot has approach room.

### 5.1 Route enrichment — read this before compiling anything

`data/missions.json` holds the **authored** segments and par times. It is not what the
game plays. At load the web build runs `LEVELS.forEach(enrichRoute)` (src/n3_d.js:424),
which rewrites every non-tutorial mission in place:

| Authored | Becomes |
|---|---|
| curve `{r, a}` | `{r: max(20, round(r * 0.6)), a: a <= 45 ? 60 : a}` — sharper |
| final straight `{len}` | `{len: len + 80}` — longer run-in to the parking spot |
| interior straight, `len >= 105` | a 5-part chicane: `S len/3.2*1.25`, `45° r24`, `S len/3.2*0.9`, `45° r24` (opposite), `S len/3.2*1.25`. Turn direction alternates on segment parity. Net heading is preserved. Counts 2 toward `added` |
| any other straight | `{len: len * 1.22}` |
| intersection `X` | unchanged |

Par is then rescaled by the length gain:
`par = round(par * (newLen / oldLen) + added * 3)`, which inflates it by **1.19–1.36×**.
Mission 1 is `tutorial: true` and is skipped entirely, so it is the only mission whose
authored par is also its real par — which means a vertical slice built on mission 1 alone
will not reveal a mistake here.

**Compiling `missions.json` directly yields the wrong geometry and a par 20–30% too
tight on 23 of 24 missions.** Always enrich first. The flag `_enriched` makes it
idempotent. Ported to C# as `RouteEnricher.Enrich`; `RouteCompiler.CompileMission`
does both steps in the right order.

**Known defect, preserved deliberately.** The rebuilt straights do not carry the `zone`
field across, so the `zone: "school"` authored on missions 3, 12, 18 and 20 is discarded
during enrichment. Every compiled route therefore has an empty zone list and the
school-zone shame rule in §10 never fires in the shipped game. The Unity port reproduces
this so behaviour matches; carrying `Zone` across in `RouteEnricher` is a one-word
change if you decide to actually enable it, but note that doing so makes four missions
harder and their `s2`/`s3` thresholds were tuned without it.

---

## 6. Parking

The spot is an oriented box of type `parallel` or `bay`. To succeed, **all four car corners
must be inside** the box (6 cm tolerance) and:

| Check | Tolerance | Notes |
|---|---|---|
| Heading error | ≤ 8° | UFO exempt. Parallel accepts either facing (±180°) |
| Curb gap | −0.02 m to 0.40 m | Parallel spots only; measured from the furthest corner to `RW` |
| Stationary | speed < 0.35 m/s | To *enter* the hold — see the hysteresis note below |
| Hold | 1.5 s continuous | Leaving tolerance resets to 0 |

**The speed check is hysteretic.** Entering the hold requires speed < 0.35 m/s, but an
in-progress hold is only abandoned above **0.5 m/s**. Collapsing these to one threshold
makes the hold flicker on and off against physics jitter, and a car settling at ~0.4 m/s
never completes a park. The box/angle/curb conditions are not hysteretic: dropping out of
any of those resets the hold immediately, regardless of speed.

`margin` in the mission data is the slack in metres beyond the car's own footprint, but
**which axis it applies to depends on the spot type**, which is why the same number reads
very differently across missions:

| | Parallel | Bay |
|---|---|---|
| half-length `hl` | `(veh.len + margin) / 2` | `veh.len / 2 + 0.6` |
| half-width `hw` | `max(1.3, veh.wid / 2 + 0.35)` | `(veh.wid + margin) / 2 + 0.25` |
| centre offset `t` | `RW - max(1.15, veh.wid / 2 + 0.15)` | `RW + 2.2 + veh.len / 2` |
| heading | route heading | route heading + 90° (nose-in, away from the road) |

So on a parallel spot `margin` is longitudinal — the gap between the bracketing cars —
and lateral slack is fixed. On a bay it is the reverse. Both types sit at
`s = routeLength - 24`, with the zone arming 42 m earlier at `s = routeLength - 66`.

So 1.0 is brutal and 3.4 is generous. The tightest in the game is mission 14
(Kart Courier, 1.0 m) and mission 12 (The Final Exam, 1.2 m with the 8.6 m limo).

A live **alignment widget** shows angle and curb gap during the attempt. Do not omit it —
without live feedback the tolerances read as arbitrary.

`perfect` = heading error < 2° **and** curb gap < 0.15 m. It triggers different confetti,
a different audio stinger, a different haptic pattern, and +50 coins in Free Roam.

---

## 7. Missions

24 missions, 4 per district across 6 districts, 3 stars each = **72 stars maximum**.
Full definitions in `data/missions.json`.

| # | D | Name | Vehicle | Lanes | Par | Park | Margin | Segs | km |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 1 | Driving School Dropout | `hatch` | 1 | 80 | parallel | 2.6 | 5 | 0.40 |
| 2 | 1 | The Milk Run | `hatch` | 1 | 105 → **137** | parallel | 2.2 | 7 → **11** | 0.71 |
| 3 | 1 | Yard Sale Frenzy | `wagon` | 1 | 115 → **150** | bay | 1.3 | 7 → **11** | 0.68 |
| 4 | 2 | Rush Hour Rodeo | `wagon` | 2 | 130 → **176** | parallel | 1.9 | 7 → **15** | 0.83 |
| 5 | 2 | The Limo Job | `limo` | 2 | 135 → **170** | parallel | 2.4 | 7 → **11** | 0.77 |
| 6 | 2 | Meltdown at Noon | `icecream` | 2 | 140 → **171** | bay | 1.4 | 7 → **11** | 0.78 |
| 7 | 3 | Night Shift | `hatch` | 2 | 130 → **177** | parallel | 1.7 | 7 → **15** | 0.80 |
| 8 | 3 | Bus Route Blues | `bus` | 2 | 150 → **190** | parallel | 3.2 | 5 → **9** | 0.67 |
| 9 | 3 | Downpour Dash | `wagon` | 2 | 145 → **184** | parallel | 1.6 | 7 → **11** | 0.79 |
| 10 | 4 | Tank on Main Street | `tank` | 2 | 160 → **211** | parallel | 3.4 | 7 → **11** | 0.72 |
| 11 | 4 | Close Encounters | `ufo` | 2 | 135 → **165** | bay | 2 | 7 → **11** | 0.77 |
| 12 | 4 | The Final Exam | `limo` | 2 | 210 → **263** | parallel | 1.2 | 11 → **19** | 1.12 |
| 13 | 5 | Boardwalk Breakfast | `hatch` | 1 | 110 → **135** | parallel | 2.2 | 7 | 0.67 |
| 14 | 5 | Kart Courier | `kart` | 1 | 100 → **129** | bay | 1 | 7 → **11** | 0.67 |
| 15 | 5 | Something Borrowed | `limo` | 2 | 140 → **177** | parallel | 1.8 | 7 → **11** | 0.77 |
| 16 | 5 | Monster Bay | `monster` | 2 | 135 → **173** | bay | 1.6 | 7 → **11** | 0.72 |
| 17 | 5 | Last Scoop of Summer | `icecream` | 2 | 150 → **189** | bay | 1.3 | 9 → **13** | 0.88 |
| 18 | 5 | The Regatta Gauntlet | `monster` | 2 | 190 → **232** | parallel | 1.4 | 11 → **15** | 1.05 |
| 19 | 6 | First Frost | `hatch` | 1 | 120 → **148** | parallel | 2.4 | 7 | 0.67 |
| 20 | 6 | The School Run | `bus` | 1 | 155 → **192** | parallel | 3.2 | 7 → **11** | 0.76 |
| 21 | 6 | Powder Express | `wagon` | 1 | 135 → **160** | bay | 1.4 | 7 | 0.67 |
| 22 | 6 | Avalanche Avenue | `monster` | 2 | 145 → **184** | bay | 1.7 | 7 → **11** | 0.74 |
| 23 | 6 | Cold Scoop | `icecream` | 2 | 150 → **180** | parallel | 1.6 | 7 | 0.71 |
| 24 | 6 | Aurora Nights | `limo` | 2 | 205 → **250** | parallel | 1.3 | 9 → **13** | 0.90 |

Where two values are shown the first is what `data/missions.json` contains and the
second, in bold, is what the game actually uses after §5.1 enrichment. `km` is the
compiled centreline length. Values verified against the shipping build by
`tools/RouteValidator`.

Each mission also carries: `brief` (flavour text shown pre-run), `traffic` (density 0–1),
`peds` (pedestrian count), `s2`/`s3` (2- and 3-star score thresholds), and optional
`cones`, `rain`, `snow`, `ice`, `time` (`day`/`night`/`dusk`), `tutorial`.

Beyond the campaign there are **Free Roam** (coin collection, never fails), a
**Daily Challenge** (seeded by date), a **Weekly Challenge**, and shareable
**challenge codes** that encode a seed so two players can attempt the same route.

---

## 8. Districts

6 districts, each a complete palette + lighting + content-rule set. Full data in
`data/districts.json`.

| Tag | Name | Mood | Distinctives |
|---|---|---|---|
| D1 | Sleepy Suburbs | Bright day | Houses, trees every 14 m, birds, no streetlights |
| D2 | Downtown Crunch | Overcast city | Tall buildings, lamps every 28 m, dense traffic |
| D3 | Neon Nights | Night | Neon signs, stars, lit windows, 220 m fog |
| D4 | Total Nightmare | Surreal dusk | Purple/orange sky, "weird" prop set |
| D5 | Sunset Marina | Golden hour | Animated ocean, pier, boats, lighthouse beam, gulls |
| D6 | Frostpeak Village | Snow | Snowfall, ice patches (reduced grip), aurora, warm windows |

Each district specifies: 3-stop `sky` gradient, `fog` colour and `fogFar` distance,
hemisphere light (2 colours + intensity), sun (colour, intensity, direction vector),
2-tone `ground`, 5 building wall colours + window colour, and content rules
(`houses`, `treeEvery`, `lampEvery`, `birds`, `neon`, `stars`, `snow`, `marina`, `weird`).

A `applyMood` layer overrides time-of-day and weather for Free Roam — swapping one palette
object re-moods the whole world, because windows, lamps, stars, moon and skyline all key
off `night` / `stars` / the colour fields. **Preserve this indirection in Unity** (a
ScriptableObject per district, swappable at runtime); it is why adding weather was cheap.

Surface grip is modified by terrain and weather rather than by district directly: D6's ice
patches, and the per-mission `rain` / `snow` / `ice` flags, all set `surfaceGrip < 1`, which
feeds into §3.1 and also lowers effective top speed. Rain is on missions 9 and 17; all six
Frostpeak missions (19–24) set `snow: true` plus `ice: 6–12`, where `ice` is a *count of
patches to scatter*, not a boolean. `cones` is likewise a count (4–10).

---

## 8.1 Traffic and pedestrians

Both systems drive shame and style sources in §10, so their constants are part of the
tuned core. In the web build they draw from bare `Math.random()`; the Unity port routes
them through a seeded stream instead, which costs nothing and makes runs reproducible.

**Traffic** (`class Traffic`, src/n3_d.js:1959). Population is
`min(19, round(density * (window*2) / 100 * 2.35))` over a `window` of 170 m either side
of the player — 3 cars at mission 1's density of 0.35. Each step, if under population and
`chance(0.18)`, one spawns, 70% of the time ahead. Direction is `chance(0.42) ? 1 : -1`;
oncoming spawns deep in the window (`rand(0.55*window, window)`) because it crosses at
roughly double relative speed and would otherwise look sparse. Spawns are rejected within
18 m of a same-lane car, and never within 20 m of the parking zone.

| Behaviour | Rule |
|---|---|
| Cruise speed | `rand(6.5, 10.5)`, ×0.9 at night |
| Car following | gap to leader, minus both half-lengths |
| Player as obstacle | when within 1.9 m of the car's lane offset |
| Speed from gap | `<3` → stop; `<8` → `(gap-3)*0.9`; `<16` → `cruise*0.6 + (gap-8)` |
| Accel / brake | +3.2 / −7.5 m/s² |
| Honk at a blocker | gap < 9, player under 1.5 m/s, car under 1 m/s, for over 2 s |
| Red light | stops 3 m before the intersection when its own phase is red |
| After being hit | pulls over for 6 s and leans on the horn |
| Panic (tank / UFO near) | accelerates to 13 m/s for 2.5 s |

Traffic lights cycle **15.6 s — 7 green, 1.6 amber, 7 red**, with a random initial phase
per intersection. While the player's road is red, cross traffic spawns at `chance(0.012)`
per intersection per step, up to 2 at a time, and yields if the player is in the box.

**Pedestrians** (`class Peds`, src/n3_d.js:2212) spawn on the sidewalks with
`rand(0.7, 1.3)` m/s walking speed. States: `walk`, `film`, `cross`, `dive`, `soaked`.

| Trigger | Result |
|---|---|
| Player within 5.5 m above 6 m/s, closing | **dive** — 12 shame, "SO CLOSE!!" |
| Overlap within 1.6 m | hard dive, teleported clear — they are never hittable |
| Near, player recently shamed, `chance(0.03)` | **film** for `rand(2, 4)` s — 2 shame, once each |
| Ice cream jingle on, near, `chance(0.006)` | **cross** — walks into the road toward the truck |
| Splashed by a puddle | **soaked** for `rand(2.5, 4)` s — up to 8 shame |

Pedestrians who wander onto the road become obstacles traffic brakes for, which is how
the ice cream missions turn into gridlock.

---

## 9. Scoring

Computed once on success. All terms are integers.

```
timeDelta  = par - elapsed
timeScore  = timeDelta >= 0 ? min(600, timeDelta * 8)
                            : max(-400, timeDelta * 4)      // over par hurts half as much
styleScore = min(800, style)
parkScore  = 700
             + max(0, 8 - headingErrorDeg) * 25             // up to +200
             + clamp((0.4 - curbGap) / 0.4, 0, 1) * 250     // parallel only, up to +250
dmgScore   = -damage * 4                                     // damage is 0..100
shameScore = -shame * 6                                      // shame is 0..100
cleanBonus = collisions == 0 ? 250 : 0

total = max(0, timeScore + styleScore + parkScore + dmgScore + shameScore + cleanBonus)
```

**Stars:** `total >= s3` → 3, else `total >= s2` → 2, else 1. Reaching the parking spot at
all guarantees 1 star; the game never punishes completion.

**S-Rank:** `total >= s3 + 350` **and** `collisions == 0` **and** `shame < 25`.

**Coins:** `max(25, total / 12) + stars * 25 + (sRank ? 100 : 0)`.
Free Roam instead pays `coinsCollected + 50 + (perfect ? 50 : 0)`.

---

## 10. Shame and Style

**Shame** is the fail condition and the emotional core. Range 0–100; **at 100 you fail
instantly**, regardless of damage or position.

**Impact sources** (one-shot, 0.35 s cooldown between collisions):

```
collision:  5 + severity * 9        (severity 0.1..1, from closing speed / 14)
  + traffic car:    +3
  + "precious" car: +14             ("THE FERRARI!!")
  + driving the tank: whole amount * 1.6
```

| One-shot event | Shame | Popup |
|---|---|---|
| Pedestrian dives clear | 12 | SO CLOSE!! |
| Ran a red light | 10 | RAN THE RED! |
| Soaked a pedestrian (puddle) | up to 8 | SOAKED THEM! |
| Bus stop-arm / mirror strike | 5 | THE ARM!! / MIRROR! |
| Mounting the curb | 4 | CURB CHECK! |
| Coming back off the curb | 1.5 | — |
| Airborne (ramp/bump) | 2.5 | AIRBORNE! |
| Prop bonk | 2 | BONK! |
| Pothole | 1.5 | POTHOLE! |
| Pedestrian films you | 2 | — |
| Sounding your own horn | 2 | — |
| Traffic honks at you | 1.2 | — |

**Sustained sources** (per second while the condition holds):

| Condition | Shame/s |
|---|---|
| Driving into oncoming traffic (car actually near) | 2.4 |
| Driving on the sidewalk | 2.2 |
| Speeding in a school zone (> 6.5 m/s) | 2.2 † |
| Wrong way for more than 1.2 s | 1.6 |
| Driving on grass / lawns | 1.2 |

† Dead code in the shipping build: route enrichment strips the `zone` field, so no
compiled route has a school zone. See §5.1.

Shame **decays at 0.5/s but only after 6 continuous seconds of calm** — any new shame resets
that timer to zero. Recovery is possible but requires visibly composing yourself, which is
the joke.

Public thresholds fire once each, with a banner, a crowd gasp, and a rising murmur bed:

| Shame | Message |
|---|---|
| 25% | PEOPLE ARE STARING |
| 50% | SOMEONE IS FILMING |
| 75% | A CROWD GATHERS |

The HUD face tracks it: 🙂 → 😬 (25) → 😰 (50) → 🤡 (75), and the meter pulses above 75.

**Style** is the reward channel, capped at 800 in scoring. It uses a combo multiplier:
each award within 4 s of the last increments the combo, multiplier `min(4, combo)`.

| Award | Base |
|---|---|
| SMOOTH (sustained clean driving) | 20 |
| OVERTAKE! | 30 |
| CLOSE ONE! (near miss) | 50 |

**Damage** is separate and much gentler: `severity * 13 * fragility`, capped at 100, and
only costs 4 points each. Damage is cosmetic pressure; **shame is the real threat.** Keep
that asymmetry — it is the design.

---

## 11. What to port, what to rebuild, what to drop

**Port faithfully — this is the tuned, validated core:**
- Vehicle physics equations and all constants (§3) — the feel is the product
- Route DSL, the enrichment pass, and the arc-length projection (§5, §5.1)
- Parking tolerances and the 1.5 s hold (§6)
- Scoring formula, star thresholds, coin economy (§9)
- Shame/Style rules, thresholds, decay (§10)
- All 24 mission definitions and 6 district palettes (`data/`)
- The comic tone: flavour text, damage popups, crowd escalation
- Tilt input, including every correction in §4

**Rebuild natively — do not translate the JavaScript:**
- Rendering. Use URP and a post-processing volume.

  > **Revised after building it.** This section originally said to use real GLTF/FBX
  > assets, PBR maps and baked lightmaps, on the theory that the web build looks simple
  > because it has none of those. The vertical slice went the other way and **ported the
  > procedural painters instead**, and the result matches the reference at a comparable
  > level of polish. Three reasons that turned out to be the better trade:
  >
  > 1. In the web build the *painting code* is the art source. Porting it keeps both
  >    builds looking like the same game, and keeps a district palette swap a data change
  >    on both sides. Exported textures start drifting from the code that made them on
  >    day one.
  > 2. Baked lightmaps would pin a scene to one district. The same scene and the same
  >    builders serve all six palettes; a bake for the suburbs sun is wrong for the other
  >    five.
  > 3. No binary art means the whole look stays reviewable in a diff.
  >
  > What is genuinely lost is bounce lighting and contact AO. The slice compensates with
  > trilight ambient plus a skybox reflection probe. Revisit this if a district ever needs
  > interiors or heavy occlusion — and note that imported assets remain the right call for
  > anything a painter cannot express, which today means characters.
- Audio. The web build synthesises everything in WebAudio, including the layered per-district
  music. In Unity use real audio assets; keep the *layering* idea (pads/bass/arp/percussion
  fading by district and intensity) because it works.
- UI. Rebuild in UI Toolkit or Canvas from the existing screens as reference.
- Particles, weather, ocean — use VFX Graph / Shader Graph rather than porting the
  hand-rolled systems.

**Drop:**
- The service worker, PWA manifest, install prompt, and the single-file build pipeline
- The `window.__PPN` headless test harness (replace with Unity Test Framework)
- The 2D `classic.html` original (keep as an easter egg link at most)

**Add, since the stores require or reward it:**
- IAP (coin packs, ad removal), rewarded video for retries or coin doubling
- Native leaderboards and achievements (Game Center / Play Games)
- Analytics on funnel: mission attempts, fail reasons, drop-off per mission
- Cloud save

---

## 12. Suggested first milestone

Do **not** rebuild all 24 missions first. Build a vertical slice:

1. Route DSL + projection, rendered as flat grey geometry
2. `hatch` with the full §3.1 bicycle model at 120 Hz, keyboard input
3. One parking spot with §6 tolerances and the alignment widget
4. Mission 1 (Driving School Dropout) end to end with §9 scoring
5. **Then** take that one mission to final shippable visual quality

Step 5 is the real unknown, and it is the one that tells you whether the professional look
is achievable and how long content actually takes. Everything else is known work.

**All five steps are done** — see `unity/README.md`. What step 5 actually answered:

- **The look is achievable, and the route to it was porting the painters, not importing
  assets.** See the revision note in §11.
- **Content cost is low per mission and front-loaded into the district.** Everything in
  mission 1 is a function of `data/missions.json` and `data/districts.json`: the road
  corridor, the scenery placement, the sky, the fog and the grade all derive from the
  mission's `district`, `lanes` and route. Missions 2–24 in the same district are close to
  free; each *new* district costs a palette entry plus whatever painters its flags imply
  (`houses`, `neon`, `snow`, `marina`, `weird`), which is where the remaining art work
  actually is.
- **Physics is 2D and the art must never quietly become load-bearing.** Nothing in the
  visual pass carries a collider. Every elevation in the corridor — the 16 cm curb
  included — is cosmetic, exactly as §2 requires.
