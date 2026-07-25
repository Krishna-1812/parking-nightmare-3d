// Golden-reference generator for the Unity parking port.
//
// Extracts the REAL spot geometry (World.buildDestination, n3_d.js:1711) and the REAL
// tolerance check + settle state machine (Game.parkingLogic, n3_e.js:1177) by text and
// runs them against stub objects. Everything those functions touch that is not pure
// maths — THREE, CarFactory, Assets, UI, SFX, the HUD — is replaced by a universal
// no-op stub, so what remains executing is exactly the geometry and the state machine.
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '..');
const SRC = path.join(REPO, 'src');
const STEP = 1 / 120;

// ---- text extraction -----------------------------------------------------------

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

// Returns the source as `name(args) { ... }` — i.e. method form, with no `function`
// keyword — so callers can uniformly prefix `function ` whether the original was a free
// function or a class method.
function grabFn(file, re, label) {
  const src = fs.readFileSync(path.join(SRC, file), 'utf8');
  const m = re.exec(src);
  if (!m) throw new Error(label + ' not found in ' + file);
  const nameStart = m.index + m[0].indexOf(m[1]);
  return sliceBraces(src, nameStart);
}

// ---- helpers, matching src/n3_b.js ----------------------------------------------

const TAU = Math.PI * 2;
const clamp = (v, a, b) => v < a ? a : (v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const dist2 = (x1, y1, x2, y2) => { const dx = x2 - x1, dy = y2 - y1; return dx * dx + dy * dy; };
const angNorm = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };
const rad = d => d * Math.PI / 180;
const deg = r => r * 180 / Math.PI;

const bSrc = fs.readFileSync(path.join(SRC, 'n3_b.js'), 'utf8');
const obbCorners = new Function('return ' + sliceBraces(bSrc, /function obbCorners/.exec(bSrc).index).replace(/^function/, 'function'))();
const pointInObb = new Function('return ' + sliceBraces(bSrc, /function pointInObb/.exec(bSrc).index).replace(/^function/, 'function'))();

// ---- universal stub: callable, constructible, assignable, chainable --------------

function stub(name) {
  const f = function () { return stub(name + '()'); };
  f.__stub = name;
  return new Proxy(f, {
    get(t, k) {
      if (k === '__stub') return name;
      if (k === Symbol.toPrimitive || k === 'valueOf') return () => 0;
      if (k === 'then') return undefined;           // not a thenable
      if (k in t && typeof t[k] !== 'undefined' && k !== 'name') return t[k];
      return stub(name + '.' + String(k));
    },
    set() { return true; },
    construct() { return stub('new ' + name); },
    apply() { return stub(name + '()'); },
    has() { return true; },
  });
}

const THREE = stub('THREE');
const Assets = stub('Assets');
const UI = stub('UI');
const SFX = stub('SFX');
const CarFactory = new Proxy({}, {
  get: () => (kind) => ({ group: stub('group'), len: 4.4, wid: 1.85, kind }),
});
const buzz = () => {};
const Save = { data: { stats: {} } };

// ---- route + spot ---------------------------------------------------------------

const LANE_W = 3.5, PARK_STRIP = 2.3, SIDEWALK_W = 3.0;

const dCtx = {};
new Function('ctx', 'clamp', 'lerp', 'dist2', 'angNorm', 'rad',
  'function ' + grabFn('n3_d.js', /function (compileRoute)\s*\(/, 'compileRoute') +
  '\nfunction ' + grabFn('n3_d.js', /function (enrichRoute)\s*\(/, 'enrichRoute') +
  '\nfunction ' + grabFn('n3_d.js', /function (routePos)\s*\(/, 'routePos') +
  '\nctx.compileRoute = compileRoute; ctx.enrichRoute = enrichRoute; ctx.routePos = routePos;'
)(dCtx, clamp, lerp, dist2, angNorm, rad);
const { compileRoute, enrichRoute, routePos } = dCtx;

const buildDestinationSrc = grabFn('n3_d.js', /^\s{2}(buildDestination)\(\)\s*\{/m, 'buildDestination');
const buildDestination = new Function(
  'THREE', 'Assets', 'CarFactory', 'VEH_DEFS', 'routePos', 'pick', 'LANE_W', 'PARK_STRIP', 'SIDEWALK_W',
  'return function ' + buildDestinationSrc + ';'
)(THREE, Assets, CarFactory, JSON.parse(fs.readFileSync(path.join(REPO, 'design-spec/data/vehicles.json'), 'utf8')),
  routePos, arr => arr[0], LANE_W, PARK_STRIP, SIDEWALK_W);

const parkingLogicSrc = grabFn('n3_e.js', /^\s{2}(parkingLogic)\(dt, proj, prevS\)\s*\{/m, 'parkingLogic');
const parkingLogic = new Function(
  'UI', 'SFX', 'clamp', 'angNorm', 'deg', 'pointInObb',
  'return function ' + parkingLogicSrc + ';'
)(UI, SFX, clamp, angNorm, deg, pointInObb);

// ---- world assembly --------------------------------------------------------------

const MISSIONS = JSON.parse(fs.readFileSync(path.join(REPO, 'design-spec/data/missions.json'), 'utf8'));
const VEHICLES = JSON.parse(fs.readFileSync(path.join(REPO, 'design-spec/data/vehicles.json'), 'utf8'));

function makeWorld(missionId) {
  const level = JSON.parse(JSON.stringify(MISSIONS.find(m => m.id === missionId)));
  enrichRoute(level);
  const route = compileRoute(level.segs);
  const world = {
    route, level, vehKey: level.veh,
    RW: level.lanes * LANE_W + PARK_STRIP,
    dist: { night: false },
    parked: [],
    r: () => 0.5,
    ribbon: () => {},
    place: () => {},
    scene: stub('scene'),
    group: stub('group'),
  };
  buildDestination.call(world);
  return { world, level };
}

function makeGame(world, level) {
  const veh = VEHICLES[level.veh];
  return {
    world, level, vehKey: level.veh,
    player: {
      x: 0, y: 0, h: 0, vx: 0, vy: 0, beam: 0,
      L: veh.len, W: veh.wid,
      get obb() { return { x: this.x, y: this.y, h: this.h, hl: this.L / 2, hw: this.W / 2 }; },
      corners() { return obbCorners(this.obb); },
    },
    playerSpeedAbs: 0,
    state: 'drive', inZone: false, parkT: 0, parkMeasure: null,
    camMode: 1, camTrans: 0,
    hud: { alignW: { classList: { add() {}, remove() {}, toggle() {} } } },
    timer: 0, style: 0,
    succeed() { this.state = 'success'; },
  };
}

function measure(g) {
  const q = g.parkMeasure;
  return {
    inside: q.inside, dAng: q.dAng,
    curbGap: q.curbGap === null ? null : q.curbGap,
    angOk: q.angOk, curbOk: q.curbOk,
  };
}

// ---- suite A: measurement grid over poses around the spot ------------------------

function grid(missionId) {
  const { world, level } = makeWorld(missionId);
  const g = makeGame(world, level);
  const spot = world.spot;
  const out = [];

  // local spot frame -> world
  const ca = Math.cos(spot.h), sa = Math.sin(spot.h);
  const toWorld = (dl, dw) => ({ x: spot.x + ca * dl - sa * dw, y: spot.y + sa * dl + ca * dw });

  for (const dl of [-1.6, -0.8, -0.25, 0, 0.25, 0.8, 1.6]) {
    for (const dw of [-0.9, -0.45, -0.15, 0, 0.15, 0.45, 0.9]) {
      for (const dh of [-0.35, -0.16, -0.05, 0, 0.05, 0.16, 0.35]) {
        const p = toWorld(dl, dw);
        g.state = 'park'; g.inZone = true; g.parkT = 0;
        g.player.x = p.x; g.player.y = p.y; g.player.h = spot.h + dh;
        g.playerSpeedAbs = 0;
        const proj = world.route.project(p.x, p.y);
        parkingLogic.call(g, STEP, proj, proj.s - 0.1);
        out.push(Object.assign({ dl, dw, dh, px: p.x, py: p.y, ph: g.player.h, projIdx: proj.idx }, measure(g)));
      }
    }
  }
  return { spot, out };
}

// ---- suite B: settle state machine over time -------------------------------------

function settleRun(missionId, name, script, steps) {
  const { world, level } = makeWorld(missionId);
  const g = makeGame(world, level);
  const spot = world.spot;
  const ca = Math.cos(spot.h), sa = Math.sin(spot.h);
  const frames = [];

  for (let i = 0; i < steps; i++) {
    const s = script(i);
    const x = spot.x + ca * s.dl - sa * s.dw;
    const y = spot.y + sa * s.dl + ca * s.dw;
    g.player.x = x; g.player.y = y; g.player.h = spot.h + s.dh;
    g.playerSpeedAbs = s.speed;
    const proj = world.route.project(x, y);
    parkingLogic.call(g, STEP, proj, proj.s - 0.01);
    frames.push({
      i, state: g.state, parkT: g.parkT, inZone: g.inZone,
      dl: s.dl, dw: s.dw, dh: s.dh, speed: s.speed,
    });
  }
  return { name, missionId, steps, frames };
}

// mission 1 is parallel (hatch, margin 2.6); mission 3 is a bay (wagon, margin 1.3)
const SCRIPTS = [
  {
    // slot in and hold: should reach success at ~180 steps of settle
    name: 'hold-to-success', mission: 1, steps: 260,
    f: () => ({ dl: 0, dw: 0, dh: 0, speed: 0.1 }),
  },
  {
    // drift out of tolerance mid-hold: must reset and never succeed
    name: 'break-mid-hold', mission: 1, steps: 400,
    f: i => (i >= 100 && i < 160)
      ? { dl: 0, dw: 0, dh: 0.6, speed: 0.1 }
      : { dl: 0, dw: 0, dh: 0, speed: 0.1 },
  },
  {
    // speed in the hysteresis band: enters at 0.3, jitters to 0.45 (below the 0.5
    // break) and must NOT reset — this is the band §6 does not mention
    name: 'hysteresis-band', mission: 1, steps: 300,
    f: i => ({ dl: 0, dw: 0, dh: 0, speed: i < 30 ? 0.3 : 0.45 }),
  },
  {
    // too fast to ever arm the settle
    name: 'never-still', mission: 1, steps: 300,
    f: () => ({ dl: 0, dw: 0, dh: 0, speed: 0.4 }),
  },
  {
    name: 'bay-hold-to-success', mission: 3, steps: 260,
    f: () => ({ dl: 0, dw: 0, dh: 0, speed: 0.1 }),
  },
  {
    name: 'bay-angle-fail', mission: 3, steps: 260,
    f: () => ({ dl: 0, dw: 0, dh: 0.2, speed: 0.1 }),
  },
];

// ---- emit -------------------------------------------------------------------------

const spots = [];
for (const m of MISSIONS) {
  const { world } = makeWorld(m.id);
  const s = world.spot;
  spots.push({
    id: m.id, park: m.park, veh: m.veh, margin: m.margin, lanes: m.lanes,
    RW: world.RW, parkZoneS: world.parkZoneS,
    type: s.type, x: s.x, y: s.y, h: s.h, hl: s.hl, hw: s.hw,
    t: s.t, curbT: s.curbT === undefined ? null : s.curbT, s: s.s,
  });
}

const grids = [1, 3].map(id => {
  const r = grid(id);
  return { id, samples: r.out };
});

const settles = SCRIPTS.map(sc => settleRun(sc.mission, sc.name, sc.f, sc.steps));

const OUT = path.join(REPO, 'tools', 'Validator', 'golden_parking.json');
fs.writeFileSync(OUT, JSON.stringify({ spots, grids, settles }, null, 1));

console.log('spots:');
for (const s of spots.slice(0, 6)) {
  console.log('  m' + String(s.id).padStart(2) + ' ' + s.type.padEnd(9) +
    ' hl=' + s.hl.toFixed(3).padStart(6) + ' hw=' + s.hw.toFixed(3).padStart(6) +
    ' t=' + s.t.toFixed(3).padStart(7) + ' zoneS=' + s.parkZoneS.toFixed(1).padStart(7));
}
console.log('  ... ' + spots.length + ' total');
console.log('\ngrids: ' + grids.map(g => 'm' + g.id + '=' + g.samples.length).join(', ') +
  '  (inside: ' + grids.map(g => g.samples.filter(s => s.inside).length).join(', ') + ')');
console.log('\nsettles:');
for (const s of settles) {
  const f = s.frames[s.frames.length - 1];
  console.log('  ' + s.name.padEnd(22) + ' end state=' + f.state.padEnd(8) + ' parkT=' + f.parkT.toFixed(3));
}
console.log('\nwrote ' + OUT);
