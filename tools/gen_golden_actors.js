// Golden-reference generator for the traffic and pedestrian AI.
//
// Extracts the REAL Traffic and Peds classes out of src/n3_d.js by text and runs them
// against stubs. The one deliberate substitution: the shipping code calls bare
// Math.random() via rand/chance/pick, so this injects a seeded mulberry32 in its place.
// That is exactly what the C# port does, which is what makes a frame-by-frame diff
// possible at all — and it is a strict test, because draw ORDER is part of the contract:
// any branch consuming a different number of draws desynchronises the whole stream and
// every subsequent value diverges.
//
// CarFactory is stubbed to consume ZERO draws. The real one picks a body colour, but
// colour has no simulation effect; the KIND draw, which sets length and width, happens
// in Traffic.makeCar itself and IS reproduced.
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '..');
const SRC = path.join(REPO, 'src');
const STEP = 1 / 120;

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
function grabClass(file, name) {
  const src = fs.readFileSync(path.join(SRC, file), 'utf8');
  const m = new RegExp('^class ' + name + '\\s*\\{', 'm').exec(src);
  if (!m) throw new Error('class ' + name + ' not found in ' + file);
  return src.slice(m.index, m.index + sliceBraces(src, m.index).length);
}
function grabFn(file, re, label) {
  const src = fs.readFileSync(path.join(SRC, file), 'utf8');
  const m = re.exec(src);
  if (!m) throw new Error(label + ' not found in ' + file);
  return sliceBraces(src, m.index + m[0].indexOf(m[1]));
}

// ---- seeded RNG, identical to the C# Rng ------------------------------------------

function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

let RNG = mulberry32(1);
const rand = (a, b) => a + RNG() * (b - a);
const chance = p => RNG() < p;
const pick = arr => arr[Math.floor(RNG() * arr.length)];

const TAU = Math.PI * 2;
const clamp = (v, a, b) => v < a ? a : (v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const dist2 = (x1, y1, x2, y2) => { const dx = x2 - x1, dy = y2 - y1; return dx * dx + dy * dy; };
const angNorm = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };
const rad = d => d * Math.PI / 180;
const LANE_W = 3.5, PARK_STRIP = 2.3, SIDEWALK_W = 3.0;

// ---- stubs -------------------------------------------------------------------------

function stub(name) {
  const f = function () { return stub(name + '()'); };
  return new Proxy(f, {
    get(t, k) {
      if (k === Symbol.toPrimitive || k === 'valueOf') return () => 0;
      if (k === 'then') return undefined;
      if (k in t && k !== 'name') return t[k];
      return stub(name + '.' + String(k));
    },
    set() { return true; },
    construct() { return stub('new ' + name); },
    apply() { return stub(name + '()'); },
    has() { return true; },
  });
}

const DIMS = {
  sedan: { len: 4.5, wid: 1.8 }, hatch: { len: 3.9, wid: 1.75 },
  suv: { len: 4.8, wid: 1.95 }, taxi: { len: 4.5, wid: 1.8 },
  police: { len: 4.7, wid: 1.85 }, truck: { len: 6.4, wid: 2.3 },
};
let nextRefId = 1;
const CarFactory = {
  // zero draws: colour is a render concern, see the header note
  traffic(kind) {
    const d = DIMS[kind] || DIMS.sedan;
    return { group: { id: nextRefId++, visible: true, position: stub('pos'), rotation: stub('rot') },
             len: d.len, wid: d.wid, brakeLights: [], wheels: [], kind };
  },
};
const PedFactory = { build: () => ({ group: stub('g'), emote: stub('emote'), phone: stub('phone'),
                                     legL: stub('l'), legR: stub('l'), armL: stub('a'), armR: stub('a') }) };
const Assets = stub('Assets');
const SFX = stub('SFX');
const Save = { data: { stats: { pedsScandalized: 0 } } };
const VEH_DEFS = JSON.parse(fs.readFileSync(path.join(REPO, 'design-spec/data/vehicles.json'), 'utf8'));

// ---- route + spot ------------------------------------------------------------------

const dCtx = {};
new Function('ctx', 'clamp', 'lerp', 'dist2', 'angNorm', 'rad',
  'function ' + grabFn('n3_d.js', /function (compileRoute)\s*\(/, 'compileRoute') +
  '\nfunction ' + grabFn('n3_d.js', /function (enrichRoute)\s*\(/, 'enrichRoute') +
  '\nfunction ' + grabFn('n3_d.js', /function (routePos)\s*\(/, 'routePos') +
  '\nctx.compileRoute = compileRoute; ctx.enrichRoute = enrichRoute; ctx.routePos = routePos;'
)(dCtx, clamp, lerp, dist2, angNorm, rad);
const { compileRoute, enrichRoute, routePos } = dCtx;

const buildDestination = new Function(
  'THREE', 'Assets', 'CarFactory', 'VEH_DEFS', 'routePos', 'pick', 'LANE_W', 'PARK_STRIP', 'SIDEWALK_W',
  'return function ' + grabFn('n3_d.js', /^\s{2}(buildDestination)\(\)\s*\{/m, 'buildDestination') + ';'
)(stub('THREE'), Assets, CarFactory, VEH_DEFS, routePos, a => a[0], LANE_W, PARK_STRIP, SIDEWALK_W);

// ---- the classes under test ----------------------------------------------------------

const mk = new Function(
  'rand', 'chance', 'pick', 'dist2', 'clamp', 'lerp', 'angNorm', 'TAU',
  'LANE_W', 'SIDEWALK_W', 'CarFactory', 'PedFactory', 'Assets', 'SFX', 'Save', 'VEH_DEFS', 'routePos',
  grabClass('n3_d.js', 'Traffic') + '\n' + grabClass('n3_d.js', 'Peds') + '\nreturn { Traffic, Peds };'
)(rand, chance, pick, dist2, clamp, lerp, angNorm, TAU,
  LANE_W, SIDEWALK_W, CarFactory, PedFactory, Assets, SFX, Save, VEH_DEFS, routePos);
const { Traffic, Peds } = mk;

// ---- harness -------------------------------------------------------------------------

const MISSIONS = JSON.parse(fs.readFileSync(path.join(REPO, 'design-spec/data/missions.json'), 'utf8'));

function buildWorld(missionId) {
  const level = JSON.parse(JSON.stringify(MISSIONS.find(m => m.id === missionId)));
  enrichRoute(level);
  const route = compileRoute(level.segs);
  const world = {
    route, level, vehKey: level.veh,
    RW: level.lanes * LANE_W + PARK_STRIP,
    dist: { night: level.time === 'night' },
    parked: [], icePatches: [],
    r: () => 0.5, ribbon: () => {}, place: () => {},
    scene: stub('scene'), group: stub('group'),
  };
  buildDestination.call(world);
  // light controllers, one per lit intersection, seeded like World.build's rr(0, 6)
  for (const inter of route.inters) {
    if (!inter.lights) continue;
    inter.ctrl = { inter, timer: rand(0, 6), state: 0 };
  }
  return { world, level };
}

function tickLights(world, dt) {
  for (const inter of world.route.inters) {
    if (!inter.ctrl) continue;
    inter.ctrl.timer += dt;
    const t = inter.ctrl.timer % 15.6;
    inter.ctrl.state = t < 7 ? 0 : (t < 8.6 ? 1 : 2);
  }
}

function run(missionId, seed, steps, drive) {
  RNG = mulberry32(seed);
  const { world, level } = buildWorld(missionId);
  // buildDestination also mints refs for the bracket / neighbour cars, which would
  // offset every traffic id. Those are static scenery, not traffic, so restart the
  // counter here — ids only ever serve as identity keys for overtake and near-miss.
  nextRefId = 1;
  const veh = VEH_DEFS[level.veh];

  const traffic = new Traffic(stub('scene'), world, level);
  const peds = new Peds(stub('scene'), world, level.peds || 0);

  const game = {
    world, level, vehKey: level.veh,
    player: { x: 0, y: 0, h: 0, vx: 0, vy: 0, W: veh.wid, L: veh.len, def: veh },
    playerSpeedAbs: 0, playerProj: null,
    recentShameT: 0, jingleOn: level.veh === 'icecream',
    state: 'drive',
    traffic, peds,
    events: [],
    // In the real game each of these routes through addShame, which sets
    // recentShameT = 3 — and pedestrians gate their "start filming" roll on exactly
    // that. Reproducing the side effect is not optional: without it the two sides
    // draw a different number of randoms and the whole stream desynchronises.
    trafficHonk(car) { this.events.push({ k: 'honk', i: this._i }); this.recentShameT = 3; },
    onFilmed(ped) { this.events.push({ k: 'filmed', i: this._i }); this.recentShameT = 3; },
    onPedDive(ped) { this.events.push({ k: 'dive', i: this._i }); this.recentShameT = 3; },
  };

  const frames = [];
  for (let i = 0; i < steps; i++) {
    game._i = i;
    const d = drive(i, world);
    game.player.x = d.x; game.player.y = d.y; game.player.h = d.h;
    game.player.vx = d.vx; game.player.vy = d.vy;
    game.playerSpeedAbs = Math.hypot(d.vx, d.vy);
    game.recentShameT = d.recentShameT || 0;
    game.playerProj = world.route.project(d.x, d.y, game.playerProj ? game.playerProj.idx : undefined);

    tickLights(world, STEP);
    traffic.update(STEP, game);
    peds.update(STEP, game);

    if (i % 20 === 0 || i === steps - 1) {
      frames.push({
        i,
        lights: world.route.inters.filter(x => x.ctrl).map(x => x.ctrl.state),
        cars: traffic.cars.map(c => ({
          id: c.refs.group.id, kind: c.refs.kind, s: c.s, t: c.t, dir: c.dir, lane: c.lane,
          v: c.v, cruise: c.cruise, len: c.len, wid: c.wid,
          x: c.x, y: c.y, h: c.h, honkCd: c.honkCd, blockT: c.blockT, hitT: c.hitT, panicT: c.panicT,
        })),
        crossers: world.route.inters.flatMap((x, xi) => (x.crossers || []).map(c => ({
          inter: xi, id: c.refs.group.id, u: c.u, dir: c.dir, v: c.v, x: c.x, y: c.y, h: c.h,
        }))),
        peds: peds.list.map(p => ({
          s: p.s, t: p.t, side: p.side, dir: p.dir, x: p.x, y: p.y, face: p.face,
          state: p.state, speed: p.speed, phase: p.phase, stateT: p.stateT,
          onRoad: p.onRoad, filmed: p.filmed, attracted: p.attracted,
          dvx: p.dvx === undefined ? null : p.dvx, dvy: p.dvy === undefined ? null : p.dvy,
        })),
      });
    }
  }
  return { missionId, seed, steps, frames, events: game.events };
}

// ---- scenarios -------------------------------------------------------------------------

// drive programs are pure functions of the step index, so C# can replay them exactly
const SCEN = [
  {
    name: 'm1-cruise', mission: 1, seed: 12345, steps: 3600,
    drive: (i, w) => {
      const s = 20 + i * 0.09;
      const p = w.route.sampleAt(Math.min(s, w.route.length - 1));
      const t = w.RW * 0.5;
      return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h,
               vx: Math.cos(p.h) * 10.8, vy: Math.sin(p.h) * 10.8 };
    },
  },
  {
    // sit still in the lane: traffic should pile up behind and start honking
    name: 'm1-blocking', mission: 1, seed: 2024, steps: 7200,
    drive: (i, w) => {
      const p = w.route.sampleAt(120);
      const t = w.RW * 0.5;
      return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h, vx: 0, vy: 0 };
    },
  },
  {
    // wrong side of the road, into the oncoming stream
    name: 'm1-oncoming', mission: 1, seed: 4242, steps: 3000,
    drive: (i, w) => {
      const s = 30 + i * 0.075;
      const p = w.route.sampleAt(Math.min(s, w.route.length - 1));
      const t = -w.RW * 0.45;
      return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h,
               vx: Math.cos(p.h) * 9, vy: Math.sin(p.h) * 9 };
    },
  },
  {
    // shameful driver on the sidewalk: pedestrians should notice and film
    name: 'm1-shameful-sidewalk', mission: 1, seed: 99, steps: 3000,
    drive: (i, w) => {
      const s = 30 + i * 0.06;
      const p = w.route.sampleAt(Math.min(s, w.route.length - 1));
      const t = w.RW + 1.6;
      return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h,
               vx: Math.cos(p.h) * 7.2, vy: Math.sin(p.h) * 7.2, recentShameT: 2 };
    },
  },
  {
    // two lanes plus a lit intersection: exercises lane choice and crossers
    name: 'm2-intersection', mission: 2, seed: 31337, steps: 4200,
    drive: (i, w) => {
      const s = 20 + i * 0.085;
      const p = w.route.sampleAt(Math.min(s, w.route.length - 1));
      const t = w.RW * 0.5;
      return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h,
               vx: Math.cos(p.h) * 10.2, vy: Math.sin(p.h) * 10.2 };
    },
  },
  {
    // ice cream truck: the jingle drags pedestrians into the road
    name: 'm6-jingle', mission: 6, seed: 606, steps: 3600,
    drive: (i, w) => {
      const s = 25 + i * 0.05;
      const p = w.route.sampleAt(Math.min(s, w.route.length - 1));
      const t = w.RW * 0.5;
      return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h,
               vx: Math.cos(p.h) * 6, vy: Math.sin(p.h) * 6 };
    },
  },
];

const out = SCEN.map(sc => Object.assign({ name: sc.name }, run(sc.mission, sc.seed, sc.steps, sc.drive)));

const OUT = path.join(REPO, 'tools', 'Validator', 'golden_actors.json');
fs.writeFileSync(OUT, JSON.stringify(out, null, 1));

for (const s of out) {
  const f = s.frames[s.frames.length - 1];
  const st = {};
  for (const p of f.peds) st[p.state] = (st[p.state] || 0) + 1;
  console.log(s.name.padEnd(24) + ' cars=' + String(f.cars.length).padStart(2) +
    ' crossers=' + String(f.crossers.length).padStart(2) +
    ' peds=' + JSON.stringify(st).padEnd(30) +
    ' events=' + s.events.length +
    ' (' + [...new Set(s.events.map(e => e.k))].join(',') + ')');
}
console.log('\nwrote ' + OUT);
