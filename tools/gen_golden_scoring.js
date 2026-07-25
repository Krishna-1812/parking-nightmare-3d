// Golden-reference generator for scoring (§9) and the shame / style systems (§10).
//
// Scoring is inlined in Game.succeed, so rather than extract a function that does not
// exist, this re-executes the exact expression sequence from src/n3_e.js:1246-1264 —
// asserted below against the source text so it cannot silently drift out of sync.
// addShame / addStyle / checkThresholds and surfaceLogic ARE functions and are extracted
// by text and run against stubs, same as the other suites.
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '..');
const SRC = path.join(REPO, 'src');
const STEP = 1 / 120;

// ---- extraction helpers ---------------------------------------------------------

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
function grabFn(file, re, label) {
  const src = fs.readFileSync(path.join(SRC, file), 'utf8');
  const m = re.exec(src);
  if (!m) throw new Error(label + ' not found in ' + file);
  return sliceBraces(src, m.index + m[0].indexOf(m[1]));
}

const TAU = Math.PI * 2;
const clamp = (v, a, b) => v < a ? a : (v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const dist2 = (x1, y1, x2, y2) => { const dx = x2 - x1, dy = y2 - y1; return dx * dx + dy * dy; };
const angNorm = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };
const rad = d => d * Math.PI / 180;
const deg = r => r * 180 / Math.PI;
const chance = () => false;
const pick = arr => arr[0];
function fmtTime(s) {
  s = Math.max(0, s);
  const m = Math.floor(s / 60), sec = Math.floor(s % 60), t = Math.floor((s % 1) * 10);
  return `${m}:${String(sec).padStart(2, '0')}.${t}`;
}

// ---- scoring: guard the transcription against source drift -----------------------

const eSrc = fs.readFileSync(path.join(SRC, 'n3_e.js'), 'utf8');
const SCORE_ANCHORS = [
  'const timeScore = timeD >= 0 ? Math.min(600, Math.round(timeD * 8)) : Math.max(-400, Math.round(timeD * 4));',
  'const styleScore = Math.min(800, Math.round(this.style));',
  'parkScore += Math.round(Math.max(0, 8 - angDeg) * 25);',
  "if (q.curbGap !== null) parkScore += Math.round(clamp((0.4 - q.curbGap) / 0.4, 0, 1) * 250);",
  'const dmgScore = -Math.round(this.player.damage * 4);',
  'const shameScore = -Math.round(this.shame * 6);',
  'const cleanBonus = this.collisions === 0 ? 250 : 0;',
  'const total = Math.max(0, timeScore + styleScore + parkScore + dmgScore + shameScore + cleanBonus);',
  'const stars = total >= L.s3 ? 3 : (total >= L.s2 ? 2 : 1);',
  'const sRank = total >= L.s3 + 350 && this.collisions === 0 && this.shame < 25;',
];
for (const a of SCORE_ANCHORS) {
  if (!eSrc.includes(a)) throw new Error('scoring source drifted, anchor missing:\n  ' + a);
}

function score(par, timer, style, angDeg, curbGap, damage, shame, collisions, s2, s3) {
  const L = { par, s2, s3 };
  const q = { curbGap };
  const timeD = L.par - timer;
  const timeScore = timeD >= 0 ? Math.min(600, Math.round(timeD * 8)) : Math.max(-400, Math.round(timeD * 4));
  const styleScore = Math.min(800, Math.round(style));
  let parkScore = 700;
  parkScore += Math.round(Math.max(0, 8 - angDeg) * 25);
  if (q.curbGap !== null) parkScore += Math.round(clamp((0.4 - q.curbGap) / 0.4, 0, 1) * 250);
  const dmgScore = -Math.round(damage * 4);
  const shameScore = -Math.round(shame * 6);
  const cleanBonus = collisions === 0 ? 250 : 0;
  const total = Math.max(0, timeScore + styleScore + parkScore + dmgScore + shameScore + cleanBonus);
  const stars = total >= L.s3 ? 3 : (total >= L.s2 ? 2 : 1);
  const sRank = total >= L.s3 + 350 && collisions === 0 && shame < 25;
  const perfect = angDeg < 2 && (q.curbGap === null || q.curbGap < 0.15);
  const coins = Math.max(25, Math.round(total / 12)) + stars * 25 + (sRank ? 100 : 0);
  return { timeScore, styleScore, parkScore, dmgScore, shameScore, cleanBonus, total, stars, sRank, perfect, coins,
           timeStr: fmtTime(timer), parStr: fmtTime(par) };
}

// ---- shame / style: extract the real methods --------------------------------------

const UIrec = [];
const UI = {
  thresholdBanner: m => UIrec.push({ k: 'threshold', m }),
  comboPop: m => UIrec.push({ k: 'combo', m }),
  zoneBanner: () => {}, tutTip: () => {},
};
const SFX = new Proxy({}, { get: () => () => {} });
const Save = { data: { stats: { totalShame: 0, redLights: 0 } } };

const addShameSrc = grabFn('n3_e.js', /^\s{2}(addShame)\(amt, label, color\)\s*\{/m, 'addShame');
const checkThresholdsSrc = grabFn('n3_e.js', /^\s{2}(checkThresholds)\(\)\s*\{/m, 'checkThresholds');
const addStyleSrc = grabFn('n3_e.js', /^\s{2}(addStyle)\(amt, label\)\s*\{/m, 'addStyle');
const surfaceLogicSrc = grabFn('n3_e.js', /^\s{2}(surfaceLogic)\(dt, proj, prevS\)\s*\{/m, 'surfaceLogic');

const mk = (src) => new Function('UI', 'SFX', 'Save', 'clamp', 'dist2', 'chance', 'pick', 'SIDEWALK_W',
  'return function ' + src + ';')(UI, SFX, Save, clamp, dist2, chance, pick, 3.0);

const addShame = mk(addShameSrc);
const checkThresholds = mk(checkThresholdsSrc);
const addStyle = mk(addStyleSrc);
const surfaceLogic = mk(surfaceLogicSrc);

function makeGame(state) {
  return {
    state: state || 'drive',
    shame: 0, style: 0,
    styleCombo: 0, styleComboT: 0,
    calmT: 0, recentShameT: 0,
    thresholdsHit: new Set(),
    collisions: 0, wrongWayT: 0, smoothMark: 0, curbCd: 0,
    comic: { spawn: () => {} },
    player: { x: 0, y: 0, h: 0, def: { hgt: 1.5, fragility: 1 }, damage: 0, bounceV: 0, surfaceGrip: 1 },
    playerSpeedAbs: 0,
    addShame, checkThresholds, addStyle, surfaceLogic,
    failShame() { this.state = 'fail'; },
    particles: { puff: () => {} },
    level: {},
    world: { RW: 5.8, icePatches: [], route: { zones: [], inters: [] } },
    traffic: { cars: [] },
    redRegistered: new Set(),
  };
}

// ---- emit -------------------------------------------------------------------------

// scoring: a spread that crosses every clamp, sign and threshold boundary
const scoreCases = [];
for (const [par, timer] of [[80, 40], [80, 80], [80, 130], [80, 300], [210, 120], [137, 137.5], [80, 79.94]]) {
  for (const style of [0, 137, 800, 1200]) {
    for (const [angDeg, curbGap] of [[0, 0], [1.9, 0.14], [4.5, 0.2], [8, 0.4], [12, 0.55], [3, null], [0, -0.02]]) {
      for (const [damage, shame, collisions] of [[0, 0, 0], [37, 24.9, 0], [100, 25, 3], [12.5, 99.5, 1]]) {
        scoreCases.push({
          par, timer, style, angDeg, curbGap, damage, shame, collisions, s2: 1200, s3: 1900,
          out: score(par, timer, style, angDeg, curbGap, damage, shame, collisions, 1200, 1900),
        });
      }
    }
  }
}

// shame: a scripted sequence of adds and ticks crossing all three thresholds and decay
const shameScripts = [];
function shameRun(name, ops) {
  const g = makeGame();
  UIrec.length = 0;
  const frames = [];
  for (const op of ops) {
    if (op.t !== undefined) {
      // tick: mirrors the ordering in fixedUpdate
      for (let k = 0; k < op.t; k++) {
        if (g.recentShameT > 0) g.recentShameT -= STEP;
        if (g.styleComboT > 0) { g.styleComboT -= STEP; if (g.styleComboT <= 0) g.styleCombo = 0; }
        g.calmT += STEP;
        if (g.calmT > 6 && g.shame > 0 && g.state === 'drive') g.shame = Math.max(0, g.shame - 0.5 * STEP);
      }
    }
    if (op.shame !== undefined) g.addShame(op.shame, op.label || null);
    if (op.style !== undefined) g.addStyle(op.style, op.label || 'X');
    if (op.state !== undefined) g.state = op.state;
    frames.push({
      op: { t: op.t ?? 0, shame: op.shame ?? null, style: op.style ?? null, state: op.state ?? null },
      shame: g.shame, style: g.style, combo: g.styleCombo, comboT: g.styleComboT,
      calmT: g.calmT, recentShameT: g.recentShameT, state: g.state,
      thresholds: [...g.thresholdsHit].sort((a, b) => a - b),
    });
  }
  shameScripts.push({ name, frames, ui: UIrec.slice() });
}

shameRun('thresholds-and-decay', [
  { shame: 10 }, { t: 120 }, { shame: 20 }, { t: 60 }, { shame: 25 },
  { t: 900 }, { t: 900 }, { shame: 30 }, { shame: 20 }, { t: 1200 },
]);
shameRun('clamp-at-100', [{ shame: 60 }, { shame: 30 }, { shame: 30 }]);
shameRun('gated-outside-drive', [
  { shame: 10 }, { state: 'success' }, { shame: 50 }, { state: 'park' }, { shame: 15 },
]);
shameRun('decay-only-in-drive', [
  { shame: 40 }, { state: 'park' }, { t: 1200 }, { state: 'drive' }, { t: 1200 },
]);
shameRun('style-combo', [
  { style: 20, label: 'SMOOTH' }, { style: 30, label: 'OVERTAKE!' }, { style: 50, label: 'CLOSE ONE!' },
  { style: 20, label: 'SMOOTH' }, { style: 20, label: 'SMOOTH' }, { t: 600 }, { style: 20, label: 'SMOOTH' },
]);
shameRun('style-gated-outside-drive', [
  { style: 20 }, { state: 'park' }, { style: 50 }, { state: 'drive' }, { style: 30 },
]);

// surfaceLogic: sweep lateral offset and speed across road / sidewalk / grass
const surfaceRuns = [];
function surfaceRun(name, program, steps) {
  const g = makeGame();
  const frames = [];
  let prevS = 0;
  for (let i = 0; i < steps; i++) {
    const p = program(i);
    g.player.x = p.x !== undefined ? p.x : 0;
    g.player.y = 0;
    g.player.h = p.h || 0;
    g.playerSpeedAbs = p.spd;
    g.state = p.state || 'drive';
    if (g.curbCd > 0) g.curbCd -= STEP;
    g.calmT += STEP;
    const proj = { s: p.s, t: p.t, h: 0, idx: 0, kind: 'road' };
    g.surfaceLogic(STEP, proj, prevS);
    prevS = p.s;
    frames.push({
      i, in: { s: p.s, t: p.t, spd: p.spd, h: p.h || 0, state: p.state || 'drive' },
      shame: g.shame, style: g.style, warn: g._warnText,
      grip: g.player.surfaceGrip, damage: g.player.damage, bounceV: g.player.bounceV,
      curbCd: g.curbCd, wrongWayT: g.wrongWayT, smoothMark: g.smoothMark,
    });
  }
  surfaceRuns.push({ name, steps, frames });
}

// drive straight down the road, then swing onto the sidewalk, then onto grass
surfaceRun('road-sidewalk-grass', i => ({
  s: i * 0.1, spd: 8,
  t: i < 200 ? 2.0 : (i < 500 ? 7.0 : 12.0),
}), 800);
// repeatedly cross the curb line to exercise the 1 s cooldown
surfaceRun('curb-hopping', i => ({
  s: i * 0.1, spd: 6,
  t: Math.floor(i / 60) % 2 === 0 ? 2.0 : 7.0,
}), 600);
// wrong way: heading reversed relative to the route
surfaceRun('wrong-way', i => ({ s: i * 0.05, spd: 6, t: 2.0, h: Math.PI }), 500);
// smooth bonus: long clean run
surfaceRun('smooth-bonus', i => ({ s: i * 0.5, spd: 12, t: 2.0 }), 1200);
// too slow to trip anything
surfaceRun('slow-crawl', i => ({ s: i * 0.005, spd: 0.6, t: 12.0 }), 300);

const OUT = path.join(REPO, 'tools', 'Validator', 'golden_scoring.json');
fs.writeFileSync(OUT, JSON.stringify({ scoreCases, shameScripts, surfaceRuns }, null, 1));

console.log('score cases   : ' + scoreCases.length);
console.log('  sample total=' + scoreCases[0].out.total + ' stars=' + scoreCases[0].out.stars +
            ' coins=' + scoreCases[0].out.coins);
console.log('shame scripts : ' + shameScripts.length);
for (const s of shameScripts) {
  const f = s.frames[s.frames.length - 1];
  console.log('  ' + s.name.padEnd(26) + ' shame=' + f.shame.toFixed(2).padStart(6) +
              ' style=' + String(f.style).padStart(5) + ' thresholds=[' + f.thresholds.join(',') + ']');
}
console.log('surface runs  : ' + surfaceRuns.length);
for (const s of surfaceRuns) {
  const f = s.frames[s.frames.length - 1];
  console.log('  ' + s.name.padEnd(26) + ' shame=' + f.shame.toFixed(3).padStart(7) +
              ' style=' + String(f.style).padStart(4) + ' dmg=' + f.damage.toFixed(1));
}
console.log('\nwrote ' + OUT);
