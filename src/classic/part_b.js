'use strict';
/* ============================================================
   PART B — utils, save, audio, input
   ============================================================ */

// ---------- constants ----------
const M2P = 14;                    // pixels per meter
const STEP = 1 / 120;              // fixed physics timestep
const TAU = Math.PI * 2;
const WORLD_W = 1680, WORLD_H = 760;

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
function fmtTime(s) {
  s = Math.max(0, s);
  const m = Math.floor(s / 60), sec = Math.floor(s % 60), t = Math.floor((s % 1) * 10);
  return `${m}:${String(sec).padStart(2, '0')}.${t}`;
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
function darken(hex, f) { // hex '#rrggbb', f 0..1 darken amount
  const n = parseInt(hex.slice(1), 16);
  const r = Math.round(((n >> 16) & 255) * (1 - f)), g = Math.round(((n >> 8) & 255) * (1 - f)), b = Math.round((n & 255) * (1 - f));
  return `rgb(${r},${g},${b})`;
}
function roundRectPath(c, x, y, w, h, r) {
  r = Math.min(r, w / 2, h / 2);
  c.beginPath();
  c.moveTo(x + r, y);
  c.arcTo(x + w, y, x + w, y + h, r);
  c.arcTo(x + w, y + h, x, y + h, r);
  c.arcTo(x, y + h, x, y, r);
  c.arcTo(x, y, x + w, y, r);
  c.closePath();
}
const $ = id => document.getElementById(id);

// ---------- OBB collision (SAT with MTV) ----------
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

// ============================================================
// SAVE MANAGER
// ============================================================
const SAVE_KEY = 'ppn_save';
const SAVE_VERSION = 1;
function defaultSave() {
  return {
    version: SAVE_VERSION,
    stars: {},           // levelId -> 0..3
    bestScores: {},      // levelId -> score
    bestTimes: {},       // levelId -> seconds
    unlockedLevel: 1,    // highest playable level id
    settings: {
      master: 0.8, music: 0.6, sfx: 0.8,
      shake: true, colorblind: false, reducedMotion: false,
      forceTouch: false, jingle: true,
    },
    stats: {
      parks: 0, collisions: 0, propsDestroyed: 0, pedsScandalized: 0,
      totalShame: 0, fastestPark: 0, pigeonsScattered: 0, crushes: 0,
      vehicleUse: {},
    },
    achievements: {},
    daily: {},           // dateKey -> {board:[{score,stars,time}], shared:false}
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
          // shallow-merge sections so new fields get defaults
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
  // generic tone helper
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
  crash(v) { // v 0..1 severity
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
  screechSet(amt) { // 0..1
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
      default: // hatch / wagon
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
  bark() {
    this.tone({ type: 'sawtooth', f0: 340, f1: 180, dur: 0.09, vol: 0.2 });
    this.tone({ type: 'sawtooth', f0: 380, f1: 200, dur: 0.09, vol: 0.18, delay: 0.16 });
  }
  pigeonFlap() { this.noise({ filter: 'highpass', f0: 2500, dur: 0.18, vol: 0.12 }); this.tone({ type: 'sine', f0: 900, f1: 1300, dur: 0.12, vol: 0.06 }); }
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
      base = 70 + speed01 * 190 + (throttle > 0 ? 20 : 0);
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
  murmurSet(level) { // 0..1
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
    // original happy jingle in C major
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
  musicStart(district) {
    if (!this.ready) return;
    if (this.musicDistrict === district && this.musicTimer) return;
    this.musicStop();
    this.musicDistrict = district;
    // patterns: [melodyNotes(hz or 0), bassNotes]
    const P = [
      { mel: [392, 0, 440, 494, 392, 0, 330, 0, 392, 0, 440, 523, 494, 440, 392, 0], bass: [98, 98, 131, 131, 110, 110, 98, 98], tempo: 260, type: 'triangle' },
      { mel: [330, 392, 440, 0, 494, 440, 392, 0, 330, 392, 523, 0, 494, 0, 440, 392], bass: [82, 82, 110, 110, 98, 98, 73, 73], tempo: 220, type: 'square' },
      { mel: [523, 0, 587, 659, 0, 587, 523, 0, 440, 0, 523, 587, 0, 523, 440, 0], bass: [131, 131, 110, 110, 87, 87, 98, 98], tempo: 240, type: 'triangle' },
      { mel: [294, 0, 294, 349, 330, 0, 262, 0, 294, 0, 392, 0, 349, 330, 294, 0], bass: [73, 73, 65, 65, 87, 87, 73, 73], tempo: 300, type: 'sawtooth' },
    ];
    const p = P[clamp(district, 0, 3)];
    this.musicStepIdx = 0;
    this.musicTimer = setInterval(() => {
      const i = this.musicStepIdx++;
      const m = p.mel[i % p.mel.length];
      if (m > 0) this.tone({ type: p.type, f0: m, dur: 0.18, vol: 0.055, dest: this.musicGain });
      if (i % 2 === 0) {
        const b = p.bass[(i / 2) % p.bass.length];
        this.tone({ type: 'triangle', f0: b, dur: 0.3, vol: 0.09, dest: this.musicGain });
      }
      if (i % 4 === 0) this.noise({ filter: 'highpass', f0: 6000, dur: 0.04, vol: 0.02, dest: this.musicGain });
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
    this.steerTouch = 0;        // -1..1 from wheel
    this.gasTouch = false;
    this.brakeTouch = false;
    this.hbTouch = false;
    this.usingTouch = false;
    this.wheelPointer = null;
    this.wheelAngle = 0;
    this.handlers = {};         // edge callbacks: horn, retry, pause, camera, any
    window.addEventListener('keydown', e => this.onKeyDown(e));
    window.addEventListener('keyup', e => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());
    this.setupTouch();
    if ('ontouchstart' in window && navigator.maxTouchPoints > 0) this.usingTouch = true;
    // late detection: some hybrids only reveal touch on first contact
    window.addEventListener('touchstart', () => {
      SFX.init();
      if (!this.usingTouch) {
        this.usingTouch = true;
        if (typeof UI !== 'undefined' && UI.inRun) this.showTouch(true);
      }
    }, { passive: true });
    // block page scroll/bounce during play, but let menu panels scroll
    document.addEventListener('touchmove', e => {
      if (!e.target.closest('.panel')) e.preventDefault();
    }, { passive: false });
    // no long-press context menu on game surfaces
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
    return clamp(s, -1, 1);
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
      this.wheelPointer = e.pointerId;
      startA = getAngle(e); baseA = this.wheelAngle;
      wheel.setPointerCapture(e.pointerId);
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
      this.wheelAngle = 0; this.steerTouch = 0;
      gfx.style.transform = 'rotate(0deg)';
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
  }
}
const Input = new InputSys();
