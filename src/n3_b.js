'use strict';
/* ============================================================
   PART B — utils, save, audio, input  (Parking Nightmare 3D)
   All world units are METERS and SECONDS.
   ============================================================ */

// ---------- constants ----------
const STEP = 1 / 120;              // fixed physics timestep
const TAU = Math.PI * 2;

// ---------- utils ----------
const clamp = (v, a, b) => v < a ? a : (v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const rand = (a, b) => a + Math.random() * (b - a);
const randi = (a, b) => Math.floor(rand(a, b + 1));
const chance = p => Math.random() < p;
const pick = arr => arr[Math.floor(Math.random() * arr.length)];
const dist2 = (x1, y1, x2, y2) => { const dx = x2 - x1, dy = y2 - y1; return dx * dx + dy * dy; };
const angNorm = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };
const angLerp = (a, b, t) => a + angNorm(b - a) * t;
const deg = r => r * 180 / Math.PI;
const rad = d => d * Math.PI / 180;
const damp = (cur, target, lambda, dt) => lerp(target, cur, Math.exp(-lambda * dt));
function fmtTime(s) {
  s = Math.max(0, s);
  const m = Math.floor(s / 60), sec = Math.floor(s % 60), t = Math.floor((s % 1) * 10);
  return `${m}:${String(sec).padStart(2, '0')}.${t}`;
}
function fmtDist(m) {
  return m >= 950 ? (m / 1000).toFixed(1) + ' km' : Math.max(0, Math.round(m / 10) * 10) + ' m';
}
function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function todayKey() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function dateKeyOffset(days) {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function weekKey() {
  // ISO week: Thursday of the current week determines the year/week number
  const d = new Date();
  const t = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const day = (t.getDay() + 6) % 7; // Mon=0
  t.setDate(t.getDate() - day + 3);
  const jan4 = new Date(t.getFullYear(), 0, 4);
  const wk = 1 + Math.round(((t - jan4) / 86400000 - 3 + ((jan4.getDay() + 6) % 7)) / 7);
  return `${t.getFullYear()}-W${String(wk).padStart(2, '0')}`;
}
// haptics — no-op on desktop or when the setting is off
function buzz(pattern) {
  try {
    if (navigator.vibrate && typeof Save !== 'undefined' && Save.data.settings.vibrate) navigator.vibrate(pattern);
  } catch (e) { /* ignore */ }
}
// crash log ring buffer — surfaced via Settings → Copy bug report
const ERRLOG_KEY = 'ppn3d_errlog';
function logError(msg, src, line) {
  try {
    const log = JSON.parse(localStorage.getItem(ERRLOG_KEY) || '[]');
    log.push({ t: new Date().toISOString(), msg: String(msg).slice(0, 300), src: String(src || '').slice(0, 80), line: line || 0 });
    localStorage.setItem(ERRLOG_KEY, JSON.stringify(log.slice(-12)));
  } catch (e) { /* storage full/blocked — nothing to do */ }
}
window.addEventListener('error', e => logError(e.message, e.filename, e.lineno));
window.addEventListener('unhandledrejection', e => logError('Promise: ' + (e.reason && e.reason.message || e.reason), '', 0));
const $ = id => document.getElementById(id);

// ---------- OBB collision (SAT with MTV) — 2D ground plane ----------
function obbCorners(o) { // {x,y,h,hl,hw} -> 4 corners
  const c = Math.cos(o.h), s = Math.sin(o.h);
  const lx = c * o.hl, ly = s * o.hl, wx = -s * o.hw, wy = c * o.hw;
  return [
    { x: o.x + lx + wx, y: o.y + ly + wy },
    { x: o.x + lx - wx, y: o.y + ly - wy },
    { x: o.x - lx - wx, y: o.y - ly - wy },
    { x: o.x - lx + wx, y: o.y - ly + wy },
  ];
}
// returns null or {nx,ny,depth} — push A out of B along (nx,ny)
function obbVsObb(a, b) {
  const ca = obbCorners(a), cb = obbCorners(b);
  const axes = [
    { x: Math.cos(a.h), y: Math.sin(a.h) }, { x: -Math.sin(a.h), y: Math.cos(a.h) },
    { x: Math.cos(b.h), y: Math.sin(b.h) }, { x: -Math.sin(b.h), y: Math.cos(b.h) },
  ];
  let minDepth = Infinity, best = null;
  for (const ax of axes) {
    let aMin = Infinity, aMax = -Infinity, bMin = Infinity, bMax = -Infinity;
    for (const p of ca) { const d = p.x * ax.x + p.y * ax.y; if (d < aMin) aMin = d; if (d > aMax) aMax = d; }
    for (const p of cb) { const d = p.x * ax.x + p.y * ax.y; if (d < bMin) bMin = d; if (d > bMax) bMax = d; }
    const overlap = Math.min(aMax, bMax) - Math.max(aMin, bMin);
    if (overlap <= 0) return null;
    if (overlap < minDepth) {
      minDepth = overlap;
      const aC = (aMin + aMax) / 2, bC = (bMin + bMax) / 2;
      best = aC < bC ? { x: -ax.x, y: -ax.y } : { x: ax.x, y: ax.y };
    }
  }
  return { nx: best.x, ny: best.y, depth: minDepth };
}
function pointInRect(px, py, r, eps) { // axis-aligned {x,y,w,h}
  eps = eps || 0;
  return px >= r.x - eps && px <= r.x + r.w + eps && py >= r.y - eps && py <= r.y + r.h + eps;
}
// point in oriented rect {x,y,h,hl,hw}
function pointInObb(px, py, o, eps) {
  eps = eps || 0;
  const c = Math.cos(o.h), s = Math.sin(o.h);
  const dx = px - o.x, dy = py - o.y;
  const lx = dx * c + dy * s, ly = -dx * s + dy * c;
  return Math.abs(lx) <= o.hl + eps && Math.abs(ly) <= o.hw + eps;
}

// ============================================================
// SAVE MANAGER
// ============================================================
const SAVE_KEY = 'ppn3d_save';
const SAVE_VERSION = 1;
function defaultSave() {
  return {
    version: SAVE_VERSION,
    stars: {},           // levelId -> 0..3
    bestScores: {},      // levelId -> score
    bestTimes: {},       // levelId -> seconds
    unlockedLevel: 1,    // highest playable mission id
    coins: 0,            // Dr. Driving-style currency earned from missions
    owned: [],           // vehicle keys bought early with coins
    settings: {
      master: 0.8, music: 0.6, sfx: 0.8,
      hq: true, shake: true, colorblind: false, reducedMotion: false,
      forceTouch: false, jingle: true, tilt: true, vibrate: true,
      tiltSens: 0.5,       // 0 = very gentle (70° for full lock), 1 = twitchy (26°)
      tiltInvert: false,   // flip tilt steering direction
      qAuto: true,         // graphics tier still auto-picked (cleared once the player chooses)
    },
    stats: {
      parks: 0, collisions: 0, propsDestroyed: 0, pedsScandalized: 0,
      totalShame: 0, fastestPark: 0, overtakes: 0, nearMisses: 0,
      crushes: 0, kmDriven: 0, redLights: 0,
      vehicleUse: {},
    },
    achievements: {},
    daily: {},           // dateKey -> {board:[{score,stars,time}], shared:false}
    weekly: {},          // weekKey -> {score, stars, time}
    tutorialDone: false, // driving school completed once
    streak: 0,           // consecutive daily-challenge days
    streakLast: '',      // dateKey of last daily completion
    lastGift: '',        // dateKey of last login gift
  };
}
class SaveManager {
  constructor() {
    this.data = defaultSave();
    this.ok = true;
    this.load();
  }
  load() {
    try {
      const raw = localStorage.getItem(SAVE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed === 'object') {
          const d = defaultSave();
          for (const k of Object.keys(d)) {
            if (parsed[k] !== undefined) {
              if (typeof d[k] === 'object' && d[k] !== null && !Array.isArray(d[k])) {
                d[k] = Object.assign({}, d[k], parsed[k]);
              } else d[k] = parsed[k];
            }
          }
          d.version = SAVE_VERSION;
          d.settings = Object.assign(defaultSave().settings, parsed.settings || {});
          d.stats = Object.assign(defaultSave().stats, parsed.stats || {});
          this.data = d;
        }
      }
    } catch (e) { this.ok = false; }
  }
  save() {
    try { localStorage.setItem(SAVE_KEY, JSON.stringify(this.data)); }
    catch (e) { this.ok = false; }
  }
  reset() {
    this.data = defaultSave();
    try { localStorage.removeItem(SAVE_KEY); } catch (e) { /* ignore */ }
    this.save();
  }
  totalStars() {
    let t = 0;
    for (const k in this.data.stars) t += this.data.stars[k];
    return t;
  }
}
const Save = new SaveManager();

// ============================================================
// AUDIO ENGINE — everything synthesized with Web Audio
// ============================================================
class AudioEngine {
  constructor() {
    this.ctx = null;
    this.ready = false;
    this.engineNodes = null;
    this.screechNodes = null;
    this.murmurNodes = null;
    this.jingleTimer = null;
    this.musicTimer = null;
    this.musicStepIdx = 0;
    this.musicDistrict = -1;
    this._noiseBuf = null;
  }
  init() {
    if (this.ready) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    try {
      const AC = window.AudioContext || window.webkitAudioContext;
      this.ctx = new AC();
      const c = this.ctx;
      this.master = c.createGain();
      this.comp = c.createDynamicsCompressor();
      this.comp.threshold.value = -14; this.comp.ratio.value = 5;
      this.master.connect(this.comp); this.comp.connect(c.destination);
      this.musicGain = c.createGain(); this.musicGain.connect(this.master);
      this.sfxGain = c.createGain(); this.sfxGain.connect(this.master);
      this.applyVolumes();
      this.ready = true;
      if (c.state === 'suspended') c.resume();
    } catch (e) { this.ready = false; }
  }
  applyVolumes() {
    if (!this.ready) return;
    const s = Save.data.settings;
    this.master.gain.value = s.master * s.master;
    this.musicGain.gain.value = s.music * s.music * 0.55;
    this.sfxGain.gain.value = s.sfx * s.sfx;
  }
  now() { return this.ctx ? this.ctx.currentTime : 0; }
  noiseBuffer() {
    if (this._noiseBuf) return this._noiseBuf;
    const c = this.ctx, len = c.sampleRate * 1.2;
    const buf = c.createBuffer(1, len, c.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    this._noiseBuf = buf;
    return buf;
  }
  tone(opts) {
    if (!this.ready) return;
    const c = this.ctx, t = this.now() + (opts.delay || 0);
    const o = c.createOscillator(), g = c.createGain();
    o.type = opts.type || 'sine';
    o.frequency.setValueAtTime(opts.f0 || 440, t);
    if (opts.f1) o.frequency.exponentialRampToValueAtTime(Math.max(20, opts.f1), t + (opts.dur || 0.2));
    const vol = (opts.vol || 0.2);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(vol, t + (opts.attack || 0.008));
    g.gain.exponentialRampToValueAtTime(0.0001, t + (opts.dur || 0.2));
    o.connect(g); g.connect(opts.dest || this.sfxGain);
    o.start(t); o.stop(t + (opts.dur || 0.2) + 0.05);
  }
  noise(opts) {
    if (!this.ready) return;
    const c = this.ctx, t = this.now() + (opts.delay || 0);
    const src = c.createBufferSource(); src.buffer = this.noiseBuffer(); src.loop = true;
    const f = c.createBiquadFilter();
    f.type = opts.filter || 'lowpass';
    f.frequency.setValueAtTime(opts.f0 || 800, t);
    if (opts.f1) f.frequency.exponentialRampToValueAtTime(Math.max(40, opts.f1), t + (opts.dur || 0.25));
    f.Q.value = opts.q || 0.8;
    const g = c.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(opts.vol || 0.3, t + (opts.attack || 0.005));
    g.gain.exponentialRampToValueAtTime(0.0001, t + (opts.dur || 0.25));
    src.connect(f); f.connect(g); g.connect(opts.dest || this.sfxGain);
    src.start(t); src.stop(t + (opts.dur || 0.25) + 0.05);
  }
  duck(amount, dur) {
    if (!this.ready) return;
    const s = Save.data.settings, t = this.now();
    const base = s.music * s.music * 0.55;
    this.musicGain.gain.cancelScheduledValues(t);
    this.musicGain.gain.setValueAtTime(base * (amount || 0.3), t);
    this.musicGain.gain.linearRampToValueAtTime(base, t + (dur || 1.2));
  }
  // ---------- UI ----------
  uiClick() { this.tone({ type: 'triangle', f0: 620, f1: 900, dur: 0.07, vol: 0.14 }); }
  uiMove() { this.tone({ type: 'triangle', f0: 440, f1: 520, dur: 0.05, vol: 0.08 }); }
  uiBack() { this.tone({ type: 'triangle', f0: 500, f1: 300, dur: 0.09, vol: 0.12 }); }
  // ---------- game SFX ----------
  crash(v) {
    v = clamp(v, 0.15, 1);
    this.noise({ filter: 'lowpass', f0: 2200 * v + 300, f1: 120, dur: 0.28 + 0.2 * v, vol: 0.5 * v + 0.12, q: 1.2 });
    this.tone({ type: 'square', f0: 90 + 60 * v, f1: 40, dur: 0.22, vol: 0.3 * v });
    if (v > 0.5) this.noise({ filter: 'highpass', f0: 3000, dur: 0.15, vol: 0.2 * v, delay: 0.01 });
    this.duck(0.25, 0.8);
  }
  thud() { this.tone({ type: 'sine', f0: 120, f1: 50, dur: 0.16, vol: 0.3 }); }
  crush() {
    this.noise({ filter: 'lowpass', f0: 900, f1: 100, dur: 0.35, vol: 0.4, q: 2 });
    this.tone({ type: 'sawtooth', f0: 140, f1: 35, dur: 0.3, vol: 0.22 });
  }
  coneBonk() { this.tone({ type: 'square', f0: 220, f1: 90, dur: 0.12, vol: 0.2 }); this.noise({ filter: 'bandpass', f0: 900, dur: 0.08, vol: 0.12, q: 2 }); }
  pothole() { this.tone({ type: 'sine', f0: 90, f1: 40, dur: 0.14, vol: 0.35 }); this.noise({ filter: 'lowpass', f0: 500, f1: 120, dur: 0.1, vol: 0.2 }); }
  screechStart() {
    if (!this.ready || this.screechNodes) return;
    const c = this.ctx;
    const src = c.createBufferSource(); src.buffer = this.noiseBuffer(); src.loop = true;
    const bp = c.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 1900; bp.Q.value = 4;
    const g = c.createGain(); g.gain.value = 0.0001;
    src.connect(bp); bp.connect(g); g.connect(this.sfxGain);
    src.start();
    this.screechNodes = { src, bp, g };
  }
  screechSet(amt) {
    if (!this.screechNodes) { if (amt > 0.05) this.screechStart(); else return; }
    if (!this.screechNodes) return;
    const t = this.now();
    this.screechNodes.g.gain.setTargetAtTime(clamp(amt, 0, 1) * 0.24, t, 0.05);
    this.screechNodes.bp.frequency.setTargetAtTime(1500 + amt * 900, t, 0.08);
  }
  screechStop() {
    if (!this.screechNodes) return;
    const n = this.screechNodes; this.screechNodes = null;
    n.g.gain.setTargetAtTime(0.0001, this.now(), 0.06);
    setTimeout(() => { try { n.src.stop(); } catch (e) {} }, 400);
  }
  horn(type) {
    const d = this.sfxGain;
    switch (type) {
      case 'limo':
        this.tone({ type: 'square', f0: 440, dur: 0.22, vol: 0.16, dest: d });
        this.tone({ type: 'square', f0: 554, dur: 0.28, vol: 0.16, delay: 0.16, dest: d });
        break;
      case 'tank':
        this.tone({ type: 'sawtooth', f0: 90, f1: 70, dur: 1.1, vol: 0.35, attack: 0.05 });
        this.noise({ filter: 'lowpass', f0: 300, dur: 1.0, vol: 0.18, attack: 0.05 });
        break;
      case 'bus':
        this.tone({ type: 'square', f0: 300, dur: 0.5, vol: 0.24 });
        this.tone({ type: 'square', f0: 375, dur: 0.5, vol: 0.2 });
        break;
      case 'icecream':
        this.tone({ type: 'square', f0: 880, dur: 0.12, vol: 0.14 });
        this.tone({ type: 'square', f0: 1108, dur: 0.12, vol: 0.14, delay: 0.11 });
        this.tone({ type: 'square', f0: 1318, dur: 0.2, vol: 0.14, delay: 0.22 });
        break;
      case 'ufo':
        this.tone({ type: 'sine', f0: 1200, f1: 300, dur: 0.7, vol: 0.25 });
        this.tone({ type: 'sine', f0: 1800, f1: 450, dur: 0.7, vol: 0.15 });
        break;
      case 'angry': // traffic honking back at you
        this.tone({ type: 'square', f0: 330, dur: 0.16, vol: 0.13 });
        this.tone({ type: 'square', f0: 330, dur: 0.3, vol: 0.13, delay: 0.22 });
        break;
      default:
        this.tone({ type: 'square', f0: 370, dur: 0.28, vol: 0.2 });
        this.tone({ type: 'square', f0: 466, dur: 0.28, vol: 0.14 });
    }
  }
  backfire() {
    this.noise({ filter: 'lowpass', f0: 500, f1: 90, dur: 0.12, vol: 0.4 });
    this.tone({ type: 'square', f0: 70, f1: 40, dur: 0.1, vol: 0.25 });
  }
  splash() { this.noise({ filter: 'bandpass', f0: 1200, f1: 400, dur: 0.3, vol: 0.25, q: 1.5 }); }
  bell() { this.tone({ type: 'triangle', f0: 1567, dur: 0.3, vol: 0.16 }); this.tone({ type: 'triangle', f0: 2093, dur: 0.35, vol: 0.1, delay: 0.12 }); }
  whoosh() { this.noise({ filter: 'bandpass', f0: 400, f1: 2600, dur: 0.35, vol: 0.22, q: 1.4 }); }
  cameraClick() { this.noise({ filter: 'highpass', f0: 4000, dur: 0.05, vol: 0.2 }); this.tone({ type: 'square', f0: 2200, dur: 0.03, vol: 0.1, delay: 0.04 }); }
  nearMiss() { this.noise({ filter: 'bandpass', f0: 600, f1: 2400, dur: 0.3, vol: 0.25, q: 2 }); }
  confettiPop() {
    this.noise({ filter: 'highpass', f0: 1500, dur: 0.2, vol: 0.3 });
    this.tone({ type: 'triangle', f0: 500, f1: 1500, dur: 0.18, vol: 0.2 });
  }
  starSlam(i) {
    this.tone({ type: 'triangle', f0: 523 * Math.pow(1.26, i), dur: 0.3, vol: 0.28 });
    this.noise({ filter: 'lowpass', f0: 700, f1: 150, dur: 0.15, vol: 0.22 });
  }
  stamp() { this.noise({ filter: 'lowpass', f0: 500, f1: 80, dur: 0.2, vol: 0.4 }); this.tone({ type: 'sine', f0: 100, f1: 45, dur: 0.2, vol: 0.3 }); }
  countBeep(final) {
    if (final) { this.tone({ type: 'square', f0: 880, dur: 0.4, vol: 0.22 }); this.noise({ filter: 'bandpass', f0: 1800, dur: 0.5, vol: 0.18, q: 3 }); }
    else this.tone({ type: 'square', f0: 440, dur: 0.15, vol: 0.18 });
  }
  successStinger(perfect) {
    const notes = perfect ? [523, 659, 784, 1046, 1318] : [523, 659, 784, 1046];
    notes.forEach((f, i) => this.tone({ type: 'triangle', f0: f, dur: 0.32, vol: 0.2, delay: i * 0.09 }));
    if (perfect) notes.forEach((f, i) => this.tone({ type: 'square', f0: f * 2, dur: 0.2, vol: 0.06, delay: i * 0.09 + 0.02 }));
    this.duck(0.2, 1.4);
  }
  failTrombone() {
    const notes = [392, 370, 349, 311];
    notes.forEach((f, i) => this.tone({ type: 'sawtooth', f0: f, f1: i === 3 ? f * 0.94 : f, dur: i === 3 ? 0.9 : 0.3, vol: 0.16, delay: i * 0.32 }));
    this.duck(0.15, 2.5);
  }
  laughter() {
    if (!this.ready) return;
    for (let i = 0; i < 14; i++) {
      const f = rand(180, 420);
      this.tone({ type: 'square', f0: f, f1: f * 0.8, dur: 0.09, vol: rand(0.05, 0.12), delay: i * rand(0.07, 0.13) });
    }
    this.noise({ filter: 'bandpass', f0: 800, dur: 1.6, vol: 0.1, q: 0.6, attack: 0.2 });
  }
  gasp() { this.noise({ filter: 'bandpass', f0: 900, f1: 1600, dur: 0.25, vol: 0.14, q: 1.2 }); }
  achievementDing() {
    this.tone({ type: 'triangle', f0: 880, dur: 0.25, vol: 0.2 });
    this.tone({ type: 'triangle', f0: 1318, dur: 0.35, vol: 0.18, delay: 0.1 });
  }
  beamHum() { this.tone({ type: 'sine', f0: 200, f1: 600, dur: 0.8, vol: 0.14 }); this.tone({ type: 'sine', f0: 205, f1: 610, dur: 0.8, vol: 0.1 }); }
  // ---------- engine loop ----------
  engineStart(vehKey) {
    if (!this.ready) return;
    this.engineStop();
    const c = this.ctx;
    const g = c.createGain(); g.gain.value = 0.0001; g.connect(this.sfxGain);
    const f = c.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = 500; f.connect(g);
    const o1 = c.createOscillator(), o2 = c.createOscillator();
    let lfo = null, lfoG = null;
    if (vehKey === 'ufo') {
      o1.type = 'sine'; o1.frequency.value = 320;
      o2.type = 'sine'; o2.frequency.value = 323;
      lfo = c.createOscillator(); lfo.frequency.value = 5;
      lfoG = c.createGain(); lfoG.gain.value = 18;
      lfo.connect(lfoG); lfoG.connect(o1.frequency); lfo.start();
      f.frequency.value = 2000;
    } else if (vehKey === 'tank') {
      o1.type = 'square'; o1.frequency.value = 38;
      o2.type = 'sawtooth'; o2.frequency.value = 57;
      f.frequency.value = 260;
    } else {
      o1.type = 'sawtooth'; o1.frequency.value = 75;
      o2.type = 'square'; o2.frequency.value = 150;
    }
    o1.connect(f); o2.connect(f);
    o1.start(); o2.start();
    this.engineNodes = { o1, o2, f, g, lfo, lfoG, key: vehKey };
  }
  engineUpdate(speed01, throttle) {
    const n = this.engineNodes;
    if (!n || !this.ready) return;
    const t = this.now();
    let base, vol;
    if (n.key === 'ufo') {
      base = 320 + speed01 * 500;
      vol = 0.06 + speed01 * 0.1;
      n.o1.frequency.setTargetAtTime(base, t, 0.1);
      n.o2.frequency.setTargetAtTime(base * 1.011, t, 0.1);
      if (n.lfo) n.lfo.frequency.setTargetAtTime(4 + speed01 * 8, t, 0.1);
    } else if (n.key === 'tank') {
      base = 38 + speed01 * 30 + (throttle > 0 ? 6 : 0);
      vol = 0.13 + speed01 * 0.1 + Math.abs(throttle) * 0.05;
      n.o1.frequency.setTargetAtTime(base, t, 0.08);
      n.o2.frequency.setTargetAtTime(base * 1.5, t, 0.08);
    } else {
      // simulated gears: pitch rises and drops through 4 bands
      const gearPos = (speed01 * 3.6) % 1;
      base = 70 + (0.25 + gearPos * 0.75) * 190 * clamp(speed01 * 1.6, 0.12, 1) + (throttle > 0 ? 20 : 0);
      vol = 0.05 + speed01 * 0.11 + Math.abs(throttle) * 0.04;
      n.o1.frequency.setTargetAtTime(base, t, 0.06);
      n.o2.frequency.setTargetAtTime(base * 2.02, t, 0.06);
      n.f.frequency.setTargetAtTime(400 + speed01 * 1600, t, 0.1);
    }
    n.g.gain.setTargetAtTime(vol, t, 0.09);
  }
  engineStop() {
    const n = this.engineNodes;
    if (!n) return;
    this.engineNodes = null;
    try {
      n.g.gain.setTargetAtTime(0.0001, this.now(), 0.08);
      setTimeout(() => { try { n.o1.stop(); n.o2.stop(); if (n.lfo) n.lfo.stop(); } catch (e) {} }, 400);
    } catch (e) {}
  }
  // ---------- crowd murmur ----------
  murmurSet(level) {
    if (!this.ready) return;
    if (level > 0.02 && !this.murmurNodes) {
      const c = this.ctx;
      const src = c.createBufferSource(); src.buffer = this.noiseBuffer(); src.loop = true;
      const f = c.createBiquadFilter(); f.type = 'bandpass'; f.frequency.value = 500; f.Q.value = 0.5;
      const g = c.createGain(); g.gain.value = 0.0001;
      src.connect(f); f.connect(g); g.connect(this.sfxGain);
      src.start();
      this.murmurNodes = { src, f, g };
    }
    if (this.murmurNodes) {
      this.murmurNodes.g.gain.setTargetAtTime(level * 0.12, this.now(), 0.4);
      if (level <= 0.02) {
        const n = this.murmurNodes; this.murmurNodes = null;
        setTimeout(() => { try { n.src.stop(); } catch (e) {} }, 800);
      }
    }
  }
  // ---------- ice cream jingle ----------
  jingleStart() {
    if (!this.ready || this.jingleTimer) return;
    const mel = [523, 659, 784, 659, 523, 659, 784, 880, 784, 659, 523, 587, 659, 587, 523, 0];
    let i = 0;
    const playNote = () => {
      if (!Save.data.settings.jingle) return;
      const f = mel[i % mel.length]; i++;
      if (f > 0) {
        this.tone({ type: 'square', f0: f, dur: 0.16, vol: 0.05, dest: this.musicGain });
        this.tone({ type: 'triangle', f0: f * 2, dur: 0.14, vol: 0.03, dest: this.musicGain });
      }
    };
    this.jingleTimer = setInterval(playNote, 210);
  }
  jingleStop() { if (this.jingleTimer) { clearInterval(this.jingleTimer); this.jingleTimer = null; } }
  // ---------- district music ----------
  // Layered step sequencer at 16th-note resolution: chord pads sustain per
  // bar (16 steps), bass walks a per-bar pattern of chord tones, an optional
  // arp shimmers on top, a lead melody loops over 32 steps, and a tiny drum
  // kit (kick / hat / rim) keeps time. One distinct mood per district.
  musicStart(district) {
    if (!this.ready) return;
    if (this.musicDistrict === district && this.musicTimer) return;
    this.musicStop();
    this.musicDistrict = district;
    const THEMES = [
      { // D1 SLEEPY SUBURBS — front-porch major, easy sway
        tempo: 138, padType: 'triangle', padVol: 0.034, melType: 'triangle', melVol: 0.05,
        chords: [[262, 330, 392], [220, 262, 330], [175, 220, 262], [196, 247, 294]],
        bassPat: [0, -1, 2, -1, 0, -1, 1, 2], arp: 0, kick: true, rim: false, hatEvery: 4,
        mel: [523, 0, 659, 0, 587, 523, 0, 440, 523, 0, 392, 0, 440, 494, 523, 0,
              659, 0, 587, 0, 523, 0, 440, 392, 440, 0, 523, 587, 523, 0, 0, 0],
      },
      { // D2 DOWNTOWN CRUNCH — walking jazz-ish shuffle
        tempo: 128, padType: 'triangle', padVol: 0.03, melType: 'square', melVol: 0.032,
        chords: [[220, 262, 330], [175, 220, 262], [196, 247, 294], [165, 208, 247]],
        bassPat: [0, 2, 1, 2, 0, 2, 1, -1], arp: 0, kick: true, rim: true, hatEvery: 2,
        mel: [440, 0, 494, 523, 0, 494, 440, 0, 392, 0, 440, 0, 330, 0, 392, 0,
              440, 494, 523, 0, 587, 0, 523, 494, 440, 0, 392, 330, 392, 0, 0, 0],
      },
      { // D3 NEON NIGHTS — minor synthwave, driving arps
        tempo: 118, padType: 'sawtooth', padVol: 0.02, melType: 'square', melVol: 0.035,
        chords: [[220, 262, 330], [175, 220, 262], [131, 165, 196], [196, 247, 294]],
        bassPat: [0, 0, -1, 0, 0, -1, 0, 0], arp: 2, kick: true, rim: true, hatEvery: 2,
        mel: [0, 0, 440, 0, 523, 0, 440, 0, 0, 0, 392, 440, 0, 0, 330, 0,
              0, 0, 440, 0, 587, 0, 523, 0, 0, 440, 0, 392, 0, 0, 0, 0],
      },
      { // D4 TOTAL NIGHTMARE — lopsided waltz through the weird
        tempo: 148, padType: 'sawtooth', padVol: 0.018, melType: 'sawtooth', melVol: 0.03,
        chords: [[233, 294, 349], [208, 262, 311], [220, 277, 330], [185, 233, 277]],
        bassPat: [0, -1, -1, 1, -1, 0, 2, -1], arp: 0, kick: true, rim: false, hatEvery: 8,
        mel: [294, 0, 0, 349, 330, 0, 262, 0, 294, 0, 392, 0, 370, 0, 294, 0,
              311, 0, 0, 370, 349, 0, 277, 0, 311, 0, 415, 0, 392, 349, 311, 0],
      },
      { // D5 SUNSET MARINA — bossa major-7ths, salt air
        tempo: 142, padType: 'triangle', padVol: 0.036, melType: 'triangle', melVol: 0.045,
        chords: [[175, 220, 262, 330], [165, 196, 247, 294], [147, 175, 220, 262], [131, 165, 196, 247]],
        bassPat: [0, -1, -1, 3, -1, 0, -1, 3], arp: 0, kick: true, rim: true, hatEvery: 4,
        mel: [523, 0, 0, 494, 0, 440, 0, 0, 392, 0, 440, 0, 494, 0, 0, 0,
              523, 0, 587, 0, 523, 0, 494, 0, 440, 0, 0, 392, 0, 0, 0, 0],
      },
      { // D6 FROSTPEAK — airy bells over slow lydian pads
        tempo: 168, padType: 'triangle', padVol: 0.042, melType: 'sine', melVol: 0.055,
        chords: [[262, 330, 392, 494], [294, 370, 440], [220, 277, 330], [247, 294, 370]],
        bassPat: [0, -1, -1, -1, 1, -1, -1, -1], arp: 3, kick: false, rim: false, hatEvery: 8,
        mel: [784, 0, 0, 0, 659, 0, 0, 0, 587, 0, 659, 0, 494, 0, 0, 0,
              784, 0, 880, 0, 659, 0, 0, 0, 587, 0, 0, 0, 0, 0, 0, 0],
      },
    ];
    const p = THEMES[clamp(district, 0, THEMES.length - 1)];
    const stepSec = p.tempo / 1000;
    this.musicStepIdx = 0;
    this.musicTimer = setInterval(() => {
      const i = this.musicStepIdx++;
      const chord = p.chords[Math.floor(i / 16) % p.chords.length];
      // sustained pad, one voice per chord tone, refreshed each bar
      if (i % 16 === 0) {
        for (let v = 0; v < chord.length; v++) {
          this.tone({ type: p.padType, f0: chord[v], dur: stepSec * 15.4, vol: p.padVol, attack: 0.5, dest: this.musicGain });
          if (p.padType === 'sawtooth') // subtle detune thickens synth pads
            this.tone({ type: p.padType, f0: chord[v] * 1.006, dur: stepSec * 15.4, vol: p.padVol * 0.7, attack: 0.5, dest: this.musicGain });
        }
      }
      // bass: chord-tone pattern, one octave down
      if (i % 2 === 0) {
        const bi = p.bassPat[(i / 2) % p.bassPat.length];
        if (bi >= 0 && chord[bi]) this.tone({ type: 'triangle', f0: chord[bi] / 2, dur: stepSec * 1.9, vol: 0.1, dest: this.musicGain });
      }
      // arp: rolling chord tones up top (arp = octave multiplier)
      if (p.arp) {
        const an = chord[i % chord.length] * p.arp;
        this.tone({ type: 'triangle', f0: an, dur: stepSec * 1.1, vol: 0.024, dest: this.musicGain });
      }
      // lead
      const m = p.mel[i % p.mel.length];
      if (m > 0) {
        this.tone({ type: p.melType, f0: m, dur: 0.22, vol: p.melVol, dest: this.musicGain });
        if (district === 5) // bell partial for the frost theme
          this.tone({ type: 'sine', f0: m * 3, dur: 0.3, vol: p.melVol * 0.22, dest: this.musicGain });
      }
      // drums
      if (p.kick && i % 8 === 0) this.tone({ type: 'sine', f0: 120, f1: 44, dur: 0.13, vol: 0.12, dest: this.musicGain });
      if (i % p.hatEvery === 0) this.noise({ filter: 'highpass', f0: 6800, dur: 0.035, vol: i % 8 === 4 ? 0.028 : 0.016, dest: this.musicGain });
      if (p.rim && i % 16 === 8) this.noise({ filter: 'bandpass', f0: 1900, dur: 0.06, vol: 0.04, q: 3, dest: this.musicGain });
    }, p.tempo);
  }
  musicStop() {
    if (this.musicTimer) { clearInterval(this.musicTimer); this.musicTimer = null; }
    this.musicDistrict = -1;
  }
  stopAllLoops() {
    this.engineStop(); this.screechStop(); this.jingleStop(); this.murmurSet(0);
  }
}
const SFX = new AudioEngine();

// ============================================================
// INPUT — keyboard + touch
// ============================================================
class InputSys {
  constructor() {
    this.keys = new Set();
    this.steerTouch = 0;
    this.gasTouch = false;
    this.brakeTouch = false;
    this.hbTouch = false;
    this.usingTouch = false;
    this.wheelPointer = null;
    this.wheelAngle = 0;
    this.handlers = {};
    // tilt steering (deviceorientation)
    this.tiltRaw = null;      // latest mapped tilt in degrees, null until sensor speaks
    this.tiltSmooth = 0;
    this.tiltZero = 0;        // neutral-grip calibration offset
    this.tiltSteer = 0;       // -1..1 output
    this.tiltHooked = false;
    this._lastTiltOn = false;
    window.addEventListener('keydown', e => this.onKeyDown(e));
    window.addEventListener('keyup', e => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());
    this.setupTouch();
    if ('ontouchstart' in window && navigator.maxTouchPoints > 0) this.usingTouch = true;
    // platforms without a permission gate (Android) can hook the sensor at boot
    const needsPerm = typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function';
    if (this.usingTouch && !needsPerm) this.enableTilt();
    window.addEventListener('touchstart', () => {
      SFX.init();
      // iOS needs a user gesture for the motion-permission prompt
      if (!this.tiltHooked && typeof Save !== 'undefined' && Save.data.settings.tilt) this.enableTilt();
      if (!this.usingTouch) {
        this.usingTouch = true;
        if (typeof UI !== 'undefined' && UI.inRun) this.showTouch(true);
      }
    }, { passive: true });
    window.addEventListener('orientationchange', () => setTimeout(() => this.calibrateTilt(), 400));
    document.addEventListener('touchmove', e => {
      if (!e.target.closest('.panel')) e.preventDefault();
    }, { passive: false });
    document.addEventListener('contextmenu', e => {
      if (e.target.closest('#touch') || e.target.closest('#game') || e.target.closest('#hud')) e.preventDefault();
    });
  }
  onKeyDown(e) {
    const t = e.target;
    const formish = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'BUTTON' || (t.classList && t.classList.contains('toggle')));
    const navKey = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space'].includes(e.code);
    if (e.repeat) { if (navKey && !formish) e.preventDefault(); return; }
    if (navKey && !formish) e.preventDefault();
    this.keys.add(e.code);
    SFX.init();
    const h = this.handlers;
    if (h.any) h.any(e);
    if (e.code === 'KeyH' && h.horn) h.horn();
    if (e.code === 'KeyR' && h.retry) h.retry();
    if (e.code === 'KeyC' && h.camera) h.camera();
    if ((e.code === 'KeyP' || e.code === 'Escape') && h.pause) h.pause();
  }
  down(code) { return this.keys.has(code); }
  get steer() {
    let s = 0;
    if (this.down('ArrowLeft') || this.down('KeyA')) s -= 1;
    if (this.down('ArrowRight') || this.down('KeyD')) s += 1;
    if (s === 0) s = this.steerTouch;
    if (s === 0 && this.wheelPointer === null && this.tiltOn) s = this.tiltSteer;
    return clamp(s, -1, 1);
  }
  // True when `steer` resolved to tilt — an ABSOLUTE analog position rather
  // than a keyboard on/off. The physics uses this to skip the slow "a hand is
  // sweeping the wheel" attack, which is right for a key press but is pure
  // added latency when the player's own hand is already the wheel.
  get steerAnalog() {
    if (this.down('ArrowLeft') || this.down('KeyA') || this.down('ArrowRight') || this.down('KeyD')) return false;
    if (this.steerTouch !== 0) return false; // on-screen wheel keeps its current feel
    return this.wheelPointer === null && this.tiltOn;
  }
  // ---- tilt steering ----
  get tiltOn() {
    return this.usingTouch && this.tiltRaw !== null && typeof Save !== 'undefined' && !!Save.data.settings.tilt;
  }
  async enableTilt() {
    if (this.tiltHooked) return true;
    try {
      if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function') {
        const r = await DeviceOrientationEvent.requestPermission();
        if (r !== 'granted') return false;
      }
    } catch (e) { return false; }
    this.tiltHooked = true;
    window.addEventListener('deviceorientation', e => this.onOrient(e));
    return true;
  }
  onOrient(e) {
    if (e.beta === null || e.gamma === null) return;
    // Steer by where GRAVITY points relative to the screen's own "right"
    // direction — not by hand-picking beta/gamma signs per orientation, which
    // is how the two landscape cases ended up swapped (tilting right steered
    // left on a real phone).
    //
    // deviceorientation is an intrinsic Z(alpha)-X'(beta)-Y''(gamma) rotation,
    // so gravity in device axes works out to:
    //   gx = cos(beta) * sin(gamma)   (+x = natural right edge)
    //   gy = -sin(beta)               (+y = natural top edge)
    // The axis the player sees as "right" on a screen rotated by theta is
    // (cos theta, -sin theta), so projecting gravity onto it gives one signed
    // number: positive = the right-hand side of the screen is tilted downward.
    // This also stops the screen's pitch (leaning it toward your face) from
    // leaking into steering the way raw beta did.
    const b = rad(e.beta), gm = rad(e.gamma);
    const gx = Math.cos(b) * Math.sin(gm);
    const gy = -Math.sin(b);
    const so = (screen.orientation && typeof screen.orientation.angle === 'number') ? screen.orientation.angle
      : (typeof window.orientation === 'number' ? (360 - window.orientation) % 360 : 0);
    const th = rad(so);
    const S = (typeof Save !== 'undefined') ? Save.data.settings : null;
    let right = gx * Math.cos(th) - gy * Math.sin(th);
    if (S && S.tiltInvert) right = -right;
    const raw = Math.asin(clamp(right, -1, 1)) * 180 / Math.PI; // degrees of lean
    if (this.tiltRaw === null) { this.tiltSmooth = raw; this.tiltZero = clamp(raw, -45, 45); }
    this.tiltRaw = raw;
    // Low-pass on a TIME constant, not per event: sensors fire anywhere from
    // ~30 to ~120Hz, and a fixed per-event factor made the lag scale with the
    // device's report rate (a 30Hz phone felt twice as laggy). 40ms kills
    // sensor jitter while staying well inside "instant" to the hand.
    const now = performance.now();
    const dtms = this._tiltT ? clamp(now - this._tiltT, 3, 100) : 16;
    this._tiltT = now;
    this.tiltSmooth += (raw - this.tiltSmooth) * (1 - Math.exp(-dtms / 40));
    const t = this.tiltSmooth - this.tiltZero;
    // Sensitivity = how far you must lean for full lock. Default asks for a
    // deliberate ~48 degrees instead of the old twitchy 26, and the squared
    // curve keeps small corrections small.
    const sens = (S && typeof S.tiltSens === 'number') ? clamp(S.tiltSens, 0, 1) : 0.5;
    const dead = 3, span = 26 + (1 - sens) * 44;
    const mag = clamp((Math.abs(t) - dead) / (span - dead), 0, 1);
    // 1.4, not 2.0: squaring made the first third of the lean produce almost
    // nothing, which reads as lag rather than as gentleness. Full lock still
    // needs a committed lean because the span stays wide.
    this.tiltSteer = Math.sign(t) * Math.pow(mag, 1.4);
    const on = this.tiltOn;
    if (on !== this._lastTiltOn) { this._lastTiltOn = on; this.applyTiltUI(); }
  }
  calibrateTilt() {
    if (this.tiltRaw !== null) this.tiltZero = clamp(this.tiltSmooth, -45, 45);
  }
  applyTiltUI() {
    const el = $('touch');
    if (el) el.classList.toggle('tilt', this.tiltOn);
  }
  get throttle() {
    let t = 0;
    if (this.down('ArrowUp') || this.down('KeyW')) t += 1;
    if (this.down('ArrowDown') || this.down('KeyS')) t -= 1;
    if (t === 0) { if (this.gasTouch) t += 1; if (this.brakeTouch) t -= 1; }
    return t;
  }
  get handbrake() { return this.down('Space') || this.hbTouch; }
  setupTouch() {
    const wheel = $('wheel'), gfx = $('wheel-gfx');
    if (!wheel) return;
    const getAngle = e => {
      const r = wheel.getBoundingClientRect();
      const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      return Math.atan2(e.clientY - cy, e.clientX - cx);
    };
    let startA = 0, baseA = 0;
    wheel.addEventListener('pointerdown', e => {
      e.preventDefault(); SFX.init();
      if (this._wheelRaf) { cancelAnimationFrame(this._wheelRaf); this._wheelRaf = null; }
      this.wheelPointer = e.pointerId;
      startA = getAngle(e); baseA = this.wheelAngle;
      try { wheel.setPointerCapture(e.pointerId); } catch (err) { /* synthetic events */ }
    });
    wheel.addEventListener('pointermove', e => {
      if (this.wheelPointer !== e.pointerId) return;
      let d = angNorm(getAngle(e) - startA);
      this.wheelAngle = clamp(baseA + d, -rad(120), rad(120));
      this.steerTouch = this.wheelAngle / rad(120);
      gfx.style.transform = `rotate(${deg(this.wheelAngle)}deg)`;
    });
    const release = e => {
      if (this.wheelPointer !== e.pointerId) return;
      this.wheelPointer = null;
      // spring back to center instead of snapping — reads as the wheel
      // self-straightening and keeps the steering value continuous
      const step = () => {
        this._wheelRaf = null;
        if (this.wheelPointer !== null) return; // regrabbed mid-spring
        this.wheelAngle *= 0.76;
        if (Math.abs(this.wheelAngle) < 0.01) this.wheelAngle = 0;
        this.steerTouch = this.wheelAngle / rad(120);
        gfx.style.transform = `rotate(${deg(this.wheelAngle)}deg)`;
        if (this.wheelAngle !== 0) this._wheelRaf = requestAnimationFrame(step);
      };
      this._wheelRaf = requestAnimationFrame(step);
    };
    wheel.addEventListener('pointerup', release);
    wheel.addEventListener('pointercancel', release);
    const bindHold = (id, prop) => {
      const el = $(id);
      if (!el) return;
      el.addEventListener('pointerdown', e => { e.preventDefault(); SFX.init(); this[prop] = true; el.classList.add('down'); });
      const up = () => { this[prop] = false; el.classList.remove('down'); };
      el.addEventListener('pointerup', up);
      el.addEventListener('pointercancel', up);
      el.addEventListener('pointerleave', up);
    };
    bindHold('pGas', 'gasTouch');
    bindHold('pBrake', 'brakeTouch');
    bindHold('tHb', 'hbTouch');
    const hornBtn = $('tHorn');
    if (hornBtn) hornBtn.addEventListener('pointerdown', e => {
      e.preventDefault(); SFX.init();
      if (this.handlers.horn) this.handlers.horn();
    });
    const zoomBtn = $('tZoom');
    if (zoomBtn) zoomBtn.addEventListener('pointerdown', e => {
      e.preventDefault(); SFX.init();
      if (this.handlers.camera) this.handlers.camera();
    });
  }
  showTouch(show) {
    const el = $('touch');
    if (el) el.classList.toggle('show', show);
    document.body.classList.toggle('touch-mode', show);
    this.applyTiltUI();
  }
}
const Input = new InputSys();
