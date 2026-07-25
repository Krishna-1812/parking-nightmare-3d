// Golden-reference generator for the Unity vehicle-physics port.
//
// Extracts the REAL Vehicle3D.update method out of src/n3_e.js by text and invokes it
// against a plain state object, so the reference is the shipped physics rather than a
// transcription of it. The car branch never touches `game`, so passing null is safe and
// skips the rendering/gimmick tails.
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '..');
const SRC = path.join(REPO, 'src');
const STEP = 1 / 120;

// ---- extract the method source -------------------------------------------------

function sliceBraces(src, startIdx) {
  let i = startIdx;
  while (src[i] !== '{') i++;
  let depth = 0, inStr = null, esc = false;
  for (let j = i; j < src.length; j++) {
    const c = src[j];
    if (esc) { esc = false; continue; }
    if (inStr) {
      if (c === '\\') esc = true; else if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') { inStr = c; continue; }
    if (c === '/' && src[j + 1] === '/') { while (j < src.length && src[j] !== '\n') j++; continue; }
    if (c === '{') depth++;
    else if (c === '}') { depth--; if (depth === 0) return src.slice(startIdx, j + 1); }
  }
  throw new Error('unbalanced braces');
}

const eSrc = fs.readFileSync(path.join(SRC, 'n3_e.js'), 'utf8');
const m = /^\s*update\(dt, inp, game\)\s*\{/m.exec(eSrc);
if (!m) throw new Error('Vehicle3D.update not found in n3_e.js');
const methodSrc = sliceBraces(eSrc, m.index + m[0].indexOf('update'));

// ---- helpers, matching src/n3_b.js ---------------------------------------------

const TAU = Math.PI * 2;
const clamp = (v, a, b) => v < a ? a : (v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const dist2 = (x1, y1, x2, y2) => { const dx = x2 - x1, dy = y2 - y1; return dx * dx + dy * dy; };
const angNorm = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };
const angLerp = (a, b, t) => a + angNorm(b - a) * t;
const rad = d => d * Math.PI / 180;
// deterministic: the car branch never calls these, but keep them total
const chance = () => false;
const rand = (a, b) => (a + b) / 2;
const SFX = new Proxy({}, { get: () => () => {} });

const update = new Function(
  'clamp', 'lerp', 'rad', 'dist2', 'angNorm', 'angLerp', 'chance', 'rand', 'TAU', 'SFX',
  'function ' + methodSrc + '; return update;'
)(clamp, lerp, rad, dist2, angNorm, angLerp, chance, rand, TAU, SFX);

// ---- state object mirroring the Vehicle3D fields the car branch touches ---------

const VEHICLES = JSON.parse(fs.readFileSync(path.join(REPO, 'design-spec/data/vehicles.json'), 'utf8'));

function makeState(key, x = 0, y = 0, h = 0) {
  const d = VEHICLES[key];
  return {
    key, def: d,
    x, y, h, px: x, py: y, ph: h,
    vx: 0, vy: 0,
    steer: 0, steerCmd: 0, maxSteer: rad(38),
    L: d.len, W: d.wid,
    braking: false, reversing: false,
    damage: 0, slideAmt: 0,
    surfaceGrip: 1, slipTimer: 0,
    bounce: 0, bounceV: 0,
    accF: 0, pitch: 0, roll: 0,
    turretA: h, hoverPhase: 0, beam: 0,
    backfireT: 1e9, armT: 1e9, armOut: 0, armDeployed: false,
    get speed() { return this.vx * Math.cos(this.h) + this.vy * Math.sin(this.h); },
    get speedAbs() { return Math.hypot(this.vx, this.vy); },
    stashPrev() { this.px = this.x; this.py = this.y; this.ph = this.h; },
  };
}

function snapshot(s) {
  return {
    x: s.x, y: s.y, h: s.h, vx: s.vx, vy: s.vy,
    steer: s.steer, steerCmd: s.steerCmd,
    slideAmt: s.slideAmt, accF: s.accF, pitch: s.pitch, roll: s.roll,
    braking: !!s.braking, reversing: !!s.reversing,
    slipTimer: s.slipTimer, bounce: s.bounce, bounceV: s.bounceV,
    surfaceGrip: s.surfaceGrip,
  };
}

// ---- scenarios ------------------------------------------------------------------
// Each is a deterministic (steer, throttle, handbrake, analog) program over N steps,
// chosen to exercise every branch of the car model.

const SCENARIOS = [
  {
    name: 'launch-straight', veh: 'hatch', steps: 1200,
    inp: () => ({ steer: 0, throttle: 1, handbrake: false }),
  },
  {
    name: 'launch-steady-steer', veh: 'hatch', steps: 1200,
    inp: () => ({ steer: 0.6, throttle: 1, handbrake: false }),
  },
  {
    name: 'full-lock-both-ways', veh: 'hatch', steps: 1440,
    inp: i => ({ steer: i < 480 ? 1 : (i < 960 ? -1 : 0), throttle: 1, handbrake: false }),
  },
  {
    // accelerate, then brake hard through zero into reverse, then brake out of it
    name: 'brake-through-zero', veh: 'hatch', steps: 1800,
    inp: i => ({ steer: 0, throttle: i < 500 ? 1 : (i < 1300 ? -1 : 1), handbrake: false }),
  },
  {
    name: 'coast-to-stop', veh: 'hatch', steps: 2400,
    inp: i => ({ steer: 0, throttle: i < 400 ? 1 : 0, handbrake: false }),
  },
  {
    name: 'handbrake-turn', veh: 'hatch', steps: 1200,
    inp: i => ({ steer: i < 300 ? 0 : 1, throttle: i < 300 ? 1 : 0, handbrake: i >= 300 && i < 700 }),
  },
  {
    // oscillating steer exercises the asymmetric attack/release sweep
    name: 'slalom-keyboard', veh: 'hatch', steps: 1800,
    inp: i => ({ steer: Math.floor(i / 90) % 2 === 0 ? 1 : -1, throttle: 1, handbrake: false }),
  },
  {
    // same shape but analog: must take the steerLam = 20 path
    name: 'slalom-analog', veh: 'hatch', steps: 1800,
    inp: i => ({ steer: Math.sin(i * 0.01) * 0.8, throttle: 1, handbrake: false, steerAnalog: true }),
  },
  {
    name: 'low-grip-surface', veh: 'hatch', steps: 1500, surfaceGrip: 0.55,
    inp: i => ({ steer: i < 400 ? 0 : 0.8, throttle: 1, handbrake: false }),
  },
  {
    // slipTimer decays over 0.45 s and multiplies grip by 0.35 while active
    name: 'slip-recovery', veh: 'hatch', steps: 900, slipAt: 300,
    inp: i => ({ steer: 0.7, throttle: 1, handbrake: false }),
  },
  {
    name: 'reverse-out', veh: 'hatch', steps: 1500,
    inp: i => ({ steer: i < 600 ? 0 : -0.5, throttle: -1, handbrake: false }),
  },
  {
    name: 'creep-kill', veh: 'hatch', steps: 1200,
    inp: i => ({ steer: 0, throttle: i < 60 ? 0.35 : 0, handbrake: false }),
  },
];

const out = [];
for (const sc of SCENARIOS) {
  const s = makeState(sc.veh);
  if (sc.surfaceGrip !== undefined) s.surfaceGrip = sc.surfaceGrip;
  const frames = [];
  for (let i = 0; i < sc.steps; i++) {
    if (sc.slipAt !== undefined && i === sc.slipAt) s.slipTimer = 0.45;
    update.call(s, STEP, sc.inp(i), null);
    if (i % 20 === 0 || i === sc.steps - 1) frames.push(Object.assign({ i }, snapshot(s)));
  }
  out.push({
    name: sc.name, veh: sc.veh, steps: sc.steps,
    surfaceGrip: sc.surfaceGrip ?? 1, slipAt: sc.slipAt ?? -1,
    frames,
  });
  const f = frames[frames.length - 1];
  console.log(
    sc.name.padEnd(22) + ' ' + String(sc.steps).padStart(5) + ' steps  ' +
    'x=' + f.x.toFixed(3).padStart(10) + ' y=' + f.y.toFixed(3).padStart(9) +
    ' h=' + f.h.toFixed(4).padStart(8) + ' v=' + Math.hypot(f.vx, f.vy).toFixed(3).padStart(7));
}

const OUT = path.join(REPO, 'tools', 'Validator', 'golden_physics.json');
fs.writeFileSync(OUT, JSON.stringify(out, null, 1));
console.log('\nwrote ' + OUT);
