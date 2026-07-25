# Parking Nightmare 3D

An arcade parking game about social humiliation rather than damage. Drive a comically
unsuitable vehicle to a parking spot while a Shame meter fills every time you embarrass
yourself in public.

**Play:** https://krishna-1812.github.io/parking-nightmare-3d/index.html

The 3D game is a single self-contained `index.html` (Three.js inlined, no CDN, works
offline as a PWA). The 2D original ships alongside it as `classic.html`.

---

## Repo layout

```
build.sh                 concat + syntax-check + stamp the service worker
index.html               BUILT — do not edit directly, it is generated
sw.js                    BUILT — generated from src/sw_template.js
classic.html             the 2D original (built from src/classic/)
manifest.webmanifest     PWA manifest
icons/                   PWA icons

src/
  n3_a.html              shell: markup + all CSS
  n3_b.js                utils, save, audio, input        (STEP = 1/120 lives here)
  n3_c.js                assets, vehicle + prop factories (VEH_DEFS)
  n3_d.js                world, districts, missions       (DISTRICTS, LEVELS)
  n3_e.js                vehicle physics + game loop      (scoring, shame, parking)
  n3_f.js                UI, screens, settings, main loop
  three.min.js           vendored Three.js r158
  sw_template.js         service worker, __BUILD__ is replaced at build time
  server.js              local dev server
  classic/               source for the 2D original (part_a.html + part_b..f.js)

design-spec/             engine-independent spec for the Unity rebuild
  DESIGN_SPEC.md         physics, routes, parking, scoring, shame/style
  data/*.json            24 missions, 9 vehicles, 6 districts — exact, exported
                         (missions are PRE-enrichment; see DESIGN_SPEC §5.1)
  extract_spec.js        regenerates data/ from the src/ literals

unity/                   the Unity 6 rebuild — see unity/README.md
  ParkingNightmare3D/    URP project, Unity 6000.5.5f1
    Assets/Scripts/Core/ engine-free simulation core (asmdef PN3D.Core)

tools/
  gen_golden_routes.js   runs the real JS route compiler, dumps a golden reference
  gen_golden_physics.js  same for the real JS vehicle physics
  gen_golden_parking.js  same for spot geometry + the parking tolerance check
  gen_golden_scoring.js  same for scoring, shame/style, surface rules
  gen_golden_actors.js   same for the traffic + pedestrian AI
  Validator/             diffs the C# port against all of them (dotnet)
```

`src/_combined.js` and `shots/` are build/test artifacts and are gitignored.

---

## Build

Requires only Node (for `node --check`) and a POSIX shell. On Windows use Git Bash.

```bash
bash build.sh
```

This concatenates `n3_b..f.js`, syntax-checks the result, wraps it with `n3_a.html` and
the inlined Three.js into `index.html`, then stamps a datetime version into `sw.js`.
Paths are relative to the script, so it works from any clone location.

## Run locally

```bash
node src/server.js
```

Then open http://localhost:8377. Do **not** open `index.html` via `file://` — the service
worker, module scope, and several APIs need a real HTTP origin.

**Important:** the service worker serves cache-first, including on localhost. After every
rebuild you must unregister service workers and delete caches, or you will be testing the
previous build:

```js
(async () => {
  for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister();
  for (const k of await caches.keys()) await caches.delete(k);
  location.reload();
})()
```

## Testing

The game exposes a handle for headless driving:

```js
window.__PPN  // { game, UI, Save, SFX, LEVELS, VEH_DEFS, DISTRICTS, Assets, ... }
```

`Input` is script-scoped rather than on `window`, but is reachable by bare name from a
console or an evaluated expression. Typical headless run:

```js
const { UI, game: g, LEVELS } = window.__PPN;
UI.startRun(LEVELS[1], 'hatch');
UI._cdToken++;              // cancel the countdown
g.beginDrive();
for (let i = 0; i < 600; i++) g.fixedUpdate(1 / 120);
g.render(1, 1 / 120);
```

`POST /shot?name=foo` on the dev server writes a canvas data-URL to `shots/foo.png`.

---

## Setting up on a new machine

```bash
git clone https://github.com/Krishna-1812/parking-nightmare-3d.git
cd parking-nightmare-3d
bash build.sh
node src/server.js
```

That is the whole setup — Node is the only dependency, and Three.js is vendored.

If you use Claude Code's browser preview, create `.claude/launch.json` (gitignored, since
it is per-machine):

```json
{
  "version": "0.0.1",
  "configurations": [
    { "name": "ppn3d", "runtimeExecutable": "node", "runtimeArgs": ["src/server.js"], "port": 8377 }
  ]
}
```

---

## Status

The web build is feature-complete and live: 24 missions across 6 districts, 9 vehicles,
Free Roam, daily and weekly challenges, shareable challenge codes, tilt steering, PWA
install and offline play.

Active direction is a **Unity rebuild** targeting Google Play and the App Store. See
`design-spec/DESIGN_SPEC.md` — it defines the tuned core (physics constants, route DSL,
parking tolerances, scoring, shame/style rules) in engine-independent terms, with all
mission and vehicle data as JSON that Unity can load directly. The web build remains the
reference implementation; where the spec and the code disagree, the code is right.

The rebuild lives in `unity/` and is following the vertical-slice plan in DESIGN_SPEC
§12. Mission 1 is playable end to end — brief, countdown, drive, park, scored results —
on greybox geometry, with the shame and style systems live. The simulation core (route
DSL and projection, the `hatch` bicycle model at 120 Hz, parking tolerances, scoring,
shame/style) is verified bit-exact against the shipping JavaScript:
`dotnet run --project tools/Validator` diffs the C# against golden references extracted
from `src/n3_*.js` themselves — 74,090 checks. Traffic and pedestrian AI are the
remaining gap. Details in `unity/README.md`.
