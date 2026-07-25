/* ============================================================
   PART D — route compiler, districts, missions, world builder,
   traffic AI, pedestrians
   Physics plane: (x, y) meters. Render: THREE (x, elev, y_physics→z).
   Heading h: forward = (cos h, sin h). Right of forward = (-sin h, cos h).
   +t = right side of travel direction (player drives on +t side).
   ============================================================ */

const LANE_W = 3.5;
const PARK_STRIP = 2.3;   // curbside parking lane width (each side)
const SIDEWALK_W = 3.0;

// ============================================================
// ROUTE — compiled arc-length centerline
// ============================================================
function compileRoute(segs) {
  const step = 2;
  const pts = [];
  let x = 0, y = 0, h = 0, s = 0;
  const inters = [];
  const zones = []; // {s0,s1,kind:'school'}
  const curves = []; // {s, dir:'L'|'R'} for GPS
  for (let si = 0; si < segs.length; si++) {
    const sg = segs[si];
    if (sg.t === 'S' || sg.t === 'X') {
      const len = sg.t === 'X' ? (sg.w || 26) : sg.len;
      if (sg.t === 'X') inters.push({ s0: s, s1: s + len, cx: x + Math.cos(h) * len / 2, cy: y + Math.sin(h) * len / 2, h, lights: sg.lights !== false, idx: inters.length });
      if (sg.zone) zones.push({ s0: s, s1: s + len, kind: sg.zone });
      const n = Math.max(1, Math.round(len / step));
      for (let i = 0; i < n; i++) {
        pts.push({ x, y, h, s, kind: sg.t === 'X' ? 'inter' : 'road', seg: si });
        x += Math.cos(h) * (len / n);
        y += Math.sin(h) * (len / n);
        s += len / n;
      }
    } else { // curve L/R
      const R = sg.r || 34;
      const ang = rad(sg.a || 90);
      const dir = sg.t === 'L' ? -1 : 1; // with y-down screen convention, right turn = +h
      curves.push({ s, dir: sg.t, end: s + R * ang });
      const arcLen = R * ang;
      const n = Math.max(4, Math.round(arcLen / step));
      for (let i = 0; i < n; i++) {
        pts.push({ x, y, h, s, kind: 'curve', seg: si });
        const dh = dir * ang / n;
        // advance along arc
        x += Math.cos(h + dh / 2) * (arcLen / n);
        y += Math.sin(h + dh / 2) * (arcLen / n);
        h += dh;
        s += arcLen / n;
      }
    }
  }
  pts.push({ x, y, h, s, kind: 'road', seg: segs.length - 1 });
  const length = s;

  function sampleAt(qs) {
    qs = clamp(qs, 0, length - 0.01);
    // binary-ish: samples are near-uniform, guess index
    let i = clamp(Math.floor(qs / step), 0, pts.length - 2);
    while (i > 0 && pts[i].s > qs) i--;
    while (i < pts.length - 2 && pts[i + 1].s <= qs) i++;
    const a = pts[i], b = pts[i + 1];
    const f = (qs - a.s) / Math.max(0.001, b.s - a.s);
    return {
      x: lerp(a.x, b.x, f), y: lerp(a.y, b.y, f),
      h: a.h + angNorm(b.h - a.h) * f,
      kind: a.kind, s: qs,
    };
  }
  function project(px, py, hint) {
    let best = -1, bd = Infinity;
    const lo = hint !== undefined ? Math.max(0, hint - 30) : 0;
    const hi = hint !== undefined ? Math.min(pts.length - 1, hint + 30) : pts.length - 1;
    const stride = hint !== undefined ? 1 : 4;
    for (let i = lo; i <= hi; i += stride) {
      const d = dist2(px, py, pts[i].x, pts[i].y);
      if (d < bd) { bd = d; best = i; }
    }
    if (hint !== undefined && bd > 40 * 40) return project(px, py); // lost — global
    if (stride > 1) { // refine
      const lo2 = Math.max(0, best - 4), hi2 = Math.min(pts.length - 1, best + 4);
      for (let i = lo2; i <= hi2; i++) {
        const d = dist2(px, py, pts[i].x, pts[i].y);
        if (d < bd) { bd = d; best = i; }
      }
    }
    const p = pts[best];
    // project onto local segment direction for accurate s and t
    const dx = px - p.x, dy = py - p.y;
    const fx = Math.cos(p.h), fy = Math.sin(p.h);
    const along = dx * fx + dy * fy;
    const t = dx * -fy + dy * fx; // right normal (-sin, cos)
    return { s: p.s + along, t, h: p.h, idx: best, kind: p.kind };
  }
  return { pts, step, length, inters, zones, curves, sampleAt, project };
}

// ---- route enrichment: sharper turns + a longer haul to the parking bay ----
// Applied once per hand-authored mission (idempotent). Sharpens every curve,
// weaves extra chicane switchbacks into long straights, and stretches the
// final approach so the destination sits further down the road. Par time is
// rescaled by the length gain (plus a small penalty per added turn) so the
// missions stay fair.
function enrichRoute(level) {
  if (!level || !level.segs || level._enriched || level.tutorial) return level;
  const src = level.segs, out = [], last = src.length - 1;
  let oldLen = 0, newLen = 0, added = 0;
  const seglen = (sg) => sg.t === 'S' ? sg.len : sg.t === 'X' ? (sg.w || 26) : (sg.r || 34) * rad(sg.a || 90);
  for (let i = 0; i < src.length; i++) {
    const sg = src[i];
    oldLen += seglen(sg);
    if (sg.t === 'L' || sg.t === 'R') {
      // tighten the radius (smaller r = sharper); nudge lazy 45s up to 60
      const nr = Math.max(20, Math.round((sg.r || 34) * 0.6));
      const na = (sg.a || 90) <= 45 ? 60 : (sg.a || 90);
      const ns = { t: sg.t, r: nr, a: na };
      out.push(ns); newLen += seglen(ns);
    } else if (sg.t === 'X') {
      out.push(sg); newLen += seglen(sg);
    } else { // straight
      const len = sg.len;
      if (i === last) {
        // longer run-in to the parking destination
        const ns = { t: 'S', len: len + 80 };
        out.push(ns); newLen += ns.len;
      } else if (i !== 0 && len >= 105) {
        // carve a long straight into a sharp S-bend chicane (net heading kept)
        const a = len / 3.2;
        const d1 = (i % 2 === 0) ? 'L' : 'R', d2 = d1 === 'L' ? 'R' : 'L';
        const parts = [
          { t: 'S', len: a * 1.25 },
          { t: d1, r: 24, a: 45 },
          { t: 'S', len: a * 0.9 },
          { t: d2, r: 24, a: 45 },
          { t: 'S', len: a * 1.25 },
        ];
        for (const p of parts) { out.push(p); newLen += seglen(p); }
        added += 2;
      } else {
        const ns = { t: 'S', len: len * 1.22 };
        out.push(ns); newLen += ns.len;
      }
    }
  }
  level.segs = out;
  if (Number.isFinite(level.par) && level.par < 9000) {
    level.par = Math.round(level.par * (newLen / Math.max(1, oldLen)) + added * 3);
  }
  level._enriched = true;
  return level;
}

// world position from (s, t): c + r*t where r = (-sin h, cos h)
function routePos(route, s, t) {
  const p = route.sampleAt(s);
  return { x: p.x - Math.sin(p.h) * t, y: p.y + Math.cos(p.h) * t, h: p.h };
}

// ============================================================
// DISTRICTS
// ============================================================
const DISTRICTS = [
  {
    name: 'SLEEPY SUBURBS', tag: 'D1',
    sky: ['#7cc4f0', '#b8e4fa', '#ffedc9'], fog: '#cfe6ef', fogFar: 300,
    hemi: [0xcfe8ff, 0x8fa876, 1.0], sun: [0xfff2d8, 2.4, [60, 90, 30]],
    ground: ['#7fb069', '#94c07d'], night: false,
    bWall: ['#f2e3c9', '#e8cfd8', '#d8e8cf', '#f7d9b8', '#e0e8f0'], bWin: '#7ea8c4',
    houses: true, treeEvery: 14, lampEvery: 0, birds: true,
  },
  {
    name: 'DOWNTOWN CRUNCH', tag: 'D2',
    sky: ['#3f8fdd', '#8fc4ee', '#d8ecf7'], fog: '#b8d4e4', fogFar: 340,
    hemi: [0xd8ecff, 0x8a8d95, 1.05], sun: [0xffffff, 2.6, [40, 110, -50]],
    ground: ['#8a8d92', '#97999e'], night: false,
    bWall: ['#8fa2b8', '#c4b8a5', '#a5b8c4', '#b8a5a0', '#7d8b9e'], bWin: '#4a6c8a',
    houses: false, treeEvery: 40, lampEvery: 28,
  },
  {
    name: 'NEON NIGHTS', tag: 'D3',
    sky: ['#070a1a', '#101736', '#252a55'], fog: '#0d1226', fogFar: 220,
    hemi: [0x3a4a7a, 0x1a1e2e, 0.85], sun: [0xa8bcff, 1.0, [-40, 90, 40]],
    ground: ['#2a2d38', '#33364a'], night: true,
    bWall: ['#2e3346', '#3a3050', '#25293c', '#402d44', '#2a3644'], bWin: '#141824',
    houses: false, treeEvery: 0, lampEvery: 22, neon: true, stars: true,
  },
  {
    name: 'TOTAL NIGHTMARE', tag: 'D4',
    sky: ['#3d1e5c', '#8a3a6e', '#ff9a5c'], fog: '#6e3a5c', fogFar: 280,
    hemi: [0xffb88a, 0x4a2a55, 0.95], sun: [0xff9a5c, 2.0, [-80, 40, 20]],
    ground: ['#6e5a50', '#7d6658'], night: false,
    bWall: ['#8a5a8a', '#a5694a', '#5c6e9e', '#9e5c5c', '#6e8a5a'], bWin: '#3d2e50',
    houses: false, treeEvery: 26, lampEvery: 30, weird: true,
  },
  {
    name: 'SUNSET MARINA', tag: 'D5',
    sky: ['#6ab4e8', '#b0dcf4', '#ffe4c0'], fog: '#dcd4c0', fogFar: 310,
    hemi: [0xf0e4d0, 0xa89c78, 1.0], sun: [0xffe8c0, 2.3, [70, 80, 40]],
    ground: ['#c9b083', '#d6c194'], night: false,
    bWall: ['#f7ead8', '#f0dce8', '#d8ecf0', '#ffe9c8', '#e4f0dc'], bWin: '#8ab8d0',
    houses: true, treeEvery: 15, lampEvery: 26, birds: true, marina: true,
  },
  {
    name: 'FROSTPEAK VILLAGE', tag: 'D6',
    sky: ['#7fb2de', '#c2ddf1', '#f2f7fc'], fog: '#dde9f2', fogFar: 250,
    hemi: [0xeaf4ff, 0xb0becf, 1.08], sun: [0xfff6e4, 2.2, [70, 75, 40]],
    ground: ['#e6edf4', '#f3f8fc'], night: false,
    bWall: ['#8a5a3c', '#7a4f34', '#9c6844', '#6e563e', '#a86e48'], bWin: '#ffd890',
    houses: true, treeEvery: 11, lampEvery: 26, snow: true,
  },
];

// Time-of-day + weather override (Free Roam): derives an effective district
// palette. Everything downstream (windows, lamps, stars, moon, skyline,
// PMREM env) keys off .night / .stars / colors, so one object swap re-mood-s
// the whole world.
function applyMood(D, level) {
  const time = level.time;
  const rain = !!level.rain;
  if (!time && !rain) return D;
  const E = Object.assign({}, D);
  if (time === 'night' && !D.night) {
    E.night = true; E.stars = true;
    E.sky = ['#0a0e20', '#141b3c', '#2a2f58'];
    E.fog = '#10142a'; E.fogFar = Math.max(200, D.fogFar - 60);
    E.hemi = [0x3a4a7a, 0x1a1e2e, 0.85];
    E.sun = [0xa8bcff, 1.0, [-40, 100, 40]]; // moonlight bright enough to read the road
    E.lampEvery = D.lampEvery || 24;
    E.bWin = '#141824';
  } else if (time === 'day' && D.night) {
    E.night = false; E.stars = false;
    E.sky = ['#5aa8e4', '#a8d4f0', '#e8f4d8']; E.fog = '#c4dcea'; E.fogFar = 320;
    E.hemi = [0xd8e8ff, 0x8a8d95, 1.0]; E.sun = [0xfff2d8, 2.4, [60, 100, 30]];
    E.bWin = '#4a6c8a';
  } else if (time === 'dusk') {
    E.night = false; E.stars = false;
    E.sky = ['#4a3a6e', '#c46a7a', '#ffbe7a']; E.fog = '#d99a80'; E.fogFar = 260;
    E.hemi = [0xffd0a8, 0x554a5c, 0.85]; E.sun = [0xff9a4d, 2.1, [-90, 32, 14]];
    E.lampEvery = D.lampEvery || 30;
  }
  if (rain) {
    E.fogFar = Math.min(E.fogFar, 230);
    E.hemi = [E.hemi[0], E.hemi[1], E.hemi[2] * 0.85];
    E.sun = [E.sun[0], E.sun[1] * 0.7, E.sun[2]];
  }
  return E;
}

// ============================================================
// MISSIONS
// ============================================================
const LEVELS = [
  {
    id: 1, district: 0, name: 'Driving School Dropout', veh: 'hatch', lanes: 1,
    brief: '"Just drive to the corner store and park. Gently. GENTLY. My mother is watching from the porch." — Your instructor',
    par: 80, traffic: 0.35, peds: 5, tutorial: true, park: 'parallel', margin: 2.6,
    segs: [{ t: 'S', len: 120 }, { t: 'R', r: 40, a: 45 }, { t: 'S', len: 90 }, { t: 'L', r: 40, a: 45 }, { t: 'S', len: 130 }],
    s2: 1200, s3: 1900,
  },
  {
    id: 2, district: 0, name: 'The Milk Run', veh: 'hatch', lanes: 1,
    brief: 'One stop sign. Three cones. A whole neighborhood of opinions. Get the milk without becoming the neighborhood group chat.',
    par: 105, traffic: 0.5, peds: 7, park: 'parallel', margin: 2.2, cones: 4,
    segs: [{ t: 'S', len: 110 }, { t: 'X', lights: false }, { t: 'S', len: 80 }, { t: 'R', r: 36, a: 90 }, { t: 'S', len: 150 }, { t: 'L', r: 40, a: 45 }, { t: 'S', len: 120 }],
    s2: 1250, s3: 1950,
  },
  {
    id: 3, district: 0, name: 'Yard Sale Frenzy', veh: 'wagon', lanes: 1,
    brief: 'Half the street is a yard sale, the other half is a school zone. The wagon is full of "bargains". Nothing about this is a bargain.',
    par: 115, traffic: 0.55, peds: 10, park: 'bay', margin: 1.3, cones: 8,
    segs: [{ t: 'S', len: 100 }, { t: 'L', r: 36, a: 60 }, { t: 'S', len: 120, zone: 'school' }, { t: 'R', r: 36, a: 60 }, { t: 'S', len: 90 }, { t: 'X', lights: false }, { t: 'S', len: 130 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 4, district: 1, name: 'Rush Hour Rodeo', veh: 'wagon', lanes: 2,
    brief: 'Downtown at lunch. Everyone is angry, everyone is honking, and your dentist appointment was eleven minutes ago.',
    par: 130, traffic: 1.5, peds: 9, park: 'parallel', margin: 1.9,
    segs: [{ t: 'S', len: 140 }, { t: 'X' }, { t: 'S', len: 110 }, { t: 'R', r: 44, a: 90 }, { t: 'S', len: 140 }, { t: 'X' }, { t: 'S', len: 150 }],
    s2: 1350, s3: 2050,
  },
  {
    id: 5, district: 1, name: 'The Limo Job', veh: 'limo', lanes: 2,
    brief: 'A pop star needs to reach the Grand Hotel. The limo is nine meters of pure liability and the paparazzi are already filming.',
    par: 135, traffic: 1.2, peds: 12, park: 'parallel', margin: 2.4,
    segs: [{ t: 'S', len: 120 }, { t: 'L', r: 44, a: 90 }, { t: 'S', len: 130 }, { t: 'X' }, { t: 'S', len: 100 }, { t: 'R', r: 50, a: 45 }, { t: 'S', len: 150 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 6, district: 1, name: 'Meltdown at Noon', veh: 'icecream', lanes: 2,
    brief: 'Deliver the goods across downtown before everything melts. Warning: the jingle summons children. The children summon chaos.',
    par: 140, traffic: 1.3, peds: 14, park: 'bay', margin: 1.4,
    segs: [{ t: 'S', len: 130 }, { t: 'X' }, { t: 'S', len: 90 }, { t: 'R', r: 44, a: 90 }, { t: 'S', len: 120 }, { t: 'L', r: 44, a: 90 }, { t: 'S', len: 160 }],
    s2: 1350, s3: 2050,
  },
  {
    id: 7, district: 2, name: 'Night Shift', veh: 'hatch', lanes: 2,
    brief: 'The city sleeps. The neon does not. Somewhere out there is a parking spot with your name on it, and a cat judging you from a dumpster.',
    par: 130, traffic: 0.9, peds: 6, park: 'parallel', margin: 1.7,
    segs: [{ t: 'S', len: 150 }, { t: 'L', r: 40, a: 60 }, { t: 'S', len: 120 }, { t: 'X' }, { t: 'S', len: 110 }, { t: 'R', r: 40, a: 60 }, { t: 'S', len: 140 }],
    s2: 1350, s3: 2100,
  },
  {
    id: 8, district: 2, name: 'Bus Route Blues', veh: 'bus', lanes: 2,
    brief: 'Night school field trip. Eleven meters of yellow. The mirrors count as part of you. The stop-arm deploys whenever it feels like it.',
    par: 150, traffic: 0.7, peds: 8, park: 'parallel', margin: 3.2,
    segs: [{ t: 'S', len: 140 }, { t: 'R', r: 50, a: 90 }, { t: 'S', len: 130 }, { t: 'X' }, { t: 'S', len: 170 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 9, district: 2, name: 'Downpour Dash', veh: 'wagon', lanes: 2,
    brief: 'Rain slicks the streets, puddles lurk by every curb, and pedestrians have umbrellas but no forgiveness. Soak no one. SOAK. NO ONE.',
    par: 145, traffic: 1.0, peds: 10, park: 'parallel', margin: 1.6, rain: true,
    segs: [{ t: 'S', len: 130 }, { t: 'X' }, { t: 'S', len: 100 }, { t: 'L', r: 40, a: 90 }, { t: 'S', len: 140 }, { t: 'R', r: 40, a: 45 }, { t: 'S', len: 150 }],
    s2: 1350, s3: 2100,
  },
  {
    id: 10, district: 3, name: 'Tank on Main Street', veh: 'tank', lanes: 2,
    brief: 'The city ordered "gentle urban renewal". They sent you a tank. Park it between two priceless sports cars without redecorating the street.',
    par: 160, traffic: 0.8, peds: 10, park: 'parallel', margin: 3.4, cones: 10,
    segs: [{ t: 'S', len: 110 }, { t: 'R', r: 40, a: 45 }, { t: 'S', len: 120 }, { t: 'X', lights: false }, { t: 'S', len: 100 }, { t: 'L', r: 40, a: 45 }, { t: 'S', len: 140 }],
    s2: 1250, s3: 1950,
  },
  {
    id: 11, district: 3, name: 'Close Encounters', veh: 'ufo', lanes: 2,
    brief: 'The mothership demands you fetch snacks. No friction, no brakes, no dignity. Beam down onto the pad — the whole galaxy is streaming this.',
    par: 135, traffic: 0.9, peds: 12, park: 'bay', margin: 2.0,
    segs: [{ t: 'S', len: 130 }, { t: 'L', r: 44, a: 90 }, { t: 'S', len: 110 }, { t: 'X' }, { t: 'S', len: 100 }, { t: 'R', r: 44, a: 90 }, { t: 'S', len: 150 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 12, district: 3, name: 'The Final Exam', veh: 'limo', lanes: 2,
    brief: 'Everything. All of it. At once. Traffic, cones, a school zone, two intersections, and a spot with 55 centimeters of forgiveness. Good luck.',
    par: 210, traffic: 1.4, peds: 14, park: 'parallel', margin: 1.2, cones: 8,
    segs: [{ t: 'S', len: 140 }, { t: 'X' }, { t: 'S', len: 110, zone: 'school' }, { t: 'R', r: 44, a: 90 }, { t: 'S', len: 130 }, { t: 'X' }, { t: 'S', len: 100 }, { t: 'L', r: 40, a: 60 }, { t: 'S', len: 90 }, { t: 'R', r: 40, a: 60 }, { t: 'S', len: 160 }],
    s2: 1400, s3: 2200,
  },
  {
    id: 13, district: 4, name: 'Boardwalk Breakfast', veh: 'hatch', lanes: 1, time: 'dusk',
    brief: 'Sunrise shift at the marina café. The seagulls have unionized, the joggers have opinions, and the croissants will not deliver themselves.',
    par: 110, traffic: 0.5, peds: 9, park: 'parallel', margin: 2.2,
    segs: [{ t: 'S', len: 120 }, { t: 'R', r: 40, a: 45 }, { t: 'S', len: 100 }, { t: 'X', lights: false }, { t: 'S', len: 90 }, { t: 'L', r: 38, a: 60 }, { t: 'S', len: 140 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 14, district: 4, name: 'Kart Courier', veh: 'kart', lanes: 1, time: 'dusk',
    brief: 'The go-kart is "borrowed" from the boardwalk track. It weighs nothing, it turns on a thought, and one solid bonk folds it like a beach chair.',
    par: 100, traffic: 0.7, peds: 10, park: 'bay', margin: 1.0, cones: 8,
    segs: [{ t: 'S', len: 100 }, { t: 'L', r: 34, a: 60 }, { t: 'S', len: 90 }, { t: 'R', r: 34, a: 90 }, { t: 'S', len: 110 }, { t: 'R', r: 36, a: 45 }, { t: 'S', len: 130 }],
    s2: 1350, s3: 2050,
  },
  {
    id: 15, district: 4, name: 'Something Borrowed', veh: 'limo', lanes: 2, time: 'dusk',
    brief: 'The bride is at the pier. The rings are in the limo. The photographer is ALREADY FILMING. Do not become the wedding story.',
    par: 140, traffic: 1.1, peds: 13, park: 'parallel', margin: 1.8,
    segs: [{ t: 'S', len: 130 }, { t: 'X', lights: false }, { t: 'S', len: 100 }, { t: 'R', r: 46, a: 90 }, { t: 'S', len: 120 }, { t: 'L', r: 44, a: 45 }, { t: 'S', len: 150 }],
    s2: 1350, s3: 2100,
  },
  {
    id: 16, district: 4, name: 'Monster Bay', veh: 'monster', lanes: 2, time: 'dusk',
    brief: 'Someone entered a monster truck in the marina boat parade. That someone is you. The wheels are taller than the mayor. Park it like you mean it.',
    par: 135, traffic: 0.9, peds: 10, park: 'bay', margin: 1.6, cones: 6,
    segs: [{ t: 'S', len: 120 }, { t: 'R', r: 42, a: 60 }, { t: 'S', len: 110 }, { t: 'X', lights: false }, { t: 'S', len: 100 }, { t: 'L', r: 42, a: 60 }, { t: 'S', len: 140 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 17, district: 4, name: 'Last Scoop of Summer', veh: 'icecream', lanes: 2, time: 'dusk', rain: true,
    brief: 'A storm rolls in off the water on the season\'s final evening. Warm rain, cold sprinkles, and every kid on the boardwalk knows your jingle.',
    par: 150, traffic: 1.0, peds: 14, park: 'bay', margin: 1.3,
    segs: [{ t: 'S', len: 130 }, { t: 'L', r: 40, a: 45 }, { t: 'S', len: 110 }, { t: 'X', lights: false }, { t: 'S', len: 90 }, { t: 'R', r: 40, a: 90 }, { t: 'S', len: 100 }, { t: 'L', r: 38, a: 45 }, { t: 'S', len: 140 }],
    s2: 1350, s3: 2100,
  },
  {
    id: 18, district: 4, name: 'The Regatta Gauntlet', veh: 'monster', lanes: 2, time: 'night',
    brief: 'Regatta night. The whole marina is out, the cones are out, the kart kids are out. One perfect park to end the season — or one very viral splash.',
    par: 190, traffic: 1.4, peds: 15, park: 'parallel', margin: 1.4, cones: 10,
    segs: [{ t: 'S', len: 130 }, { t: 'X', lights: false }, { t: 'S', len: 100, zone: 'school' }, { t: 'L', r: 42, a: 90 }, { t: 'S', len: 120 }, { t: 'R', r: 40, a: 60 }, { t: 'S', len: 90 }, { t: 'X', lights: false }, { t: 'S', len: 100 }, { t: 'R', r: 40, a: 45 }, { t: 'S', len: 150 }],
    s2: 1400, s3: 2200,
  },
  {
    id: 19, district: 5, name: 'First Frost', veh: 'hatch', lanes: 1, snow: true, ice: 6,
    brief: 'Overnight the village froze solid and your driveway became a luge track. Black ice doesn\'t honk before it gets you. Ease into it.',
    par: 120, traffic: 0.45, peds: 6, park: 'parallel', margin: 2.4,
    segs: [{ t: 'S', len: 120 }, { t: 'R', r: 40, a: 45 }, { t: 'S', len: 100 }, { t: 'L', r: 38, a: 60 }, { t: 'S', len: 90 }, { t: 'X', lights: false }, { t: 'S', len: 140 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 20, district: 5, name: 'The School Run', veh: 'bus', lanes: 1, snow: true, ice: 8,
    brief: 'Snow day was CANCELLED and nobody is happy about it, least of all the bus. Eleven icy meters of yellow through a school zone. Gently now.',
    par: 155, traffic: 0.6, peds: 10, park: 'parallel', margin: 3.2,
    segs: [{ t: 'S', len: 130 }, { t: 'L', r: 44, a: 60 }, { t: 'S', len: 110, zone: 'school' }, { t: 'R', r: 46, a: 90 }, { t: 'S', len: 100 }, { t: 'X', lights: false }, { t: 'S', len: 150 }],
    s2: 1300, s3: 2050,
  },
  {
    id: 21, district: 5, name: 'Powder Express', veh: 'wagon', lanes: 1, snow: true, ice: 10,
    brief: 'Six sleds, four thermoses, one wagon, zero grip. The kids rated your last stop "meh" online. The mountain is watching. Deliver.',
    par: 135, traffic: 0.7, peds: 9, park: 'bay', margin: 1.4, cones: 8,
    segs: [{ t: 'S', len: 110 }, { t: 'R', r: 36, a: 90 }, { t: 'S', len: 100 }, { t: 'L', r: 36, a: 60 }, { t: 'S', len: 90 }, { t: 'L', r: 38, a: 45 }, { t: 'S', len: 140 }],
    s2: 1350, s3: 2100,
  },
  {
    id: 22, district: 5, name: 'Avalanche Avenue', veh: 'monster', lanes: 2, snow: true, ice: 12,
    brief: 'The plow broke. The village called the next best thing: you, and wheels taller than the snowbanks. Big tires, bigger ice, biggest responsibility.',
    par: 145, traffic: 0.8, peds: 8, park: 'bay', margin: 1.7, cones: 6,
    segs: [{ t: 'S', len: 120 }, { t: 'X', lights: false }, { t: 'S', len: 100 }, { t: 'R', r: 42, a: 90 }, { t: 'S', len: 110 }, { t: 'L', r: 42, a: 45 }, { t: 'S', len: 150 }],
    s2: 1300, s3: 2000,
  },
  {
    id: 23, district: 5, name: 'Cold Scoop', veh: 'icecream', lanes: 2, time: 'dusk', snow: true, ice: 10,
    brief: 'Selling ice cream in a snowstorm at dusk. Your accountant quit. The jingle echoes weirdly off the ice. And yet — a line is forming.',
    par: 150, traffic: 0.9, peds: 13, park: 'parallel', margin: 1.6,
    segs: [{ t: 'S', len: 130 }, { t: 'L', r: 40, a: 45 }, { t: 'S', len: 100 }, { t: 'X', lights: false }, { t: 'S', len: 90 }, { t: 'R', r: 40, a: 90 }, { t: 'S', len: 150 }],
    s2: 1350, s3: 2100,
  },
  {
    id: 24, district: 5, name: 'Aurora Nights', veh: 'limo', lanes: 2, time: 'night', snow: true, ice: 12,
    brief: 'The northern lights came out and so did everyone who owns a camera. Nine meters of limousine, a sky on fire, and one impossibly tight spot at the lookout.',
    par: 205, traffic: 1.1, peds: 12, park: 'parallel', margin: 1.3, cones: 6,
    segs: [{ t: 'S', len: 130 }, { t: 'X', lights: false }, { t: 'S', len: 110 }, { t: 'R', r: 44, a: 90 }, { t: 'S', len: 100 }, { t: 'L', r: 40, a: 60 }, { t: 'S', len: 90 }, { t: 'L', r: 40, a: 45 }, { t: 'S', len: 160 }],
    s2: 1400, s3: 2250,
  },
];
// sharpen every campaign mission's turns + lengthen the haul to the bay
LEVELS.forEach(enrichRoute);

// Shared seeded-mission generator: dailies, weeklies and friend challenges
// all derive from a string seed so the same code reproduces the same streets.
function hashSeed(str) {
  let seed = 0;
  for (let i = 0; i < str.length; i++) seed = (seed * 31 + str.charCodeAt(i)) >>> 0;
  return seed;
}
function seededMission(seedStr, opts) {
  opts = opts || {};
  const seed = hashSeed(seedStr);
  const rng = mulberry32(seed);
  const district = Math.floor(rng() * DISTRICTS.length);
  const vehKeys = ['hatch', 'wagon', 'limo', 'icecream', 'bus', 'tank', 'ufo', 'kart', 'monster'];
  const veh = vehKeys[Math.floor(rng() * vehKeys.length)];
  const lanes = district === 0 ? 1 : 2;
  const big = !!opts.big;
  const segs = [{ t: 'S', len: 90 + rng() * 60 }];
  let dist = segs[0].len;
  const nSeg = (big ? 10 : 7) + Math.floor(rng() * 4);
  for (let i = 0; i < nSeg; i++) {
    const r = rng();
    if (r < 0.34) { const L = 80 + rng() * 100; segs.push({ t: 'S', len: L }); dist += L; }
    else if (r < 0.47 && dist > 150) { segs.push({ t: 'X', lights: district !== 0 }); dist += 26; }
    else {
      // sharper (smaller r) and mostly hard 90° corners
      const turn = { t: rng() < 0.5 ? 'L' : 'R', r: 22 + rng() * 12, a: rng() < 0.78 ? 90 : 60 };
      segs.push(turn); dist += turn.r * rad(turn.a);
    }
  }
  segs.push({ t: 'S', len: 190 });
  dist += 190;
  const park = rng() < 0.5 ? 'parallel' : 'bay';
  const margin = park === 'parallel' ? 1.4 + rng() * 1.2 : 1.2 + rng() * 0.8;
  const snowD = !!DISTRICTS[district].snow;
  return {
    district, veh, lanes,
    par: Math.round(dist / 9 + 55) + (big ? 25 : 0),
    traffic: (big ? 0.7 : 0.5) + rng() * 1.0, peds: 6 + Math.floor(rng() * 8),
    park, margin, cones: rng() < (big ? 0.7 : 0.4) ? 6 : 0,
    rain: snowD ? false : (big ? rng() < 0.35 : (district === 2 && rng() < 0.5)),
    snow: snowD || undefined, ice: snowD ? (big ? 12 : 8) : undefined,
    time: big && rng() < 0.4 ? 'night' : undefined,
    segs, s2: big ? 1450 : 1300, s3: big ? 2300 : 2000, seed: seedStr,
  };
}
function dailyLevel() {
  const key = todayKey();
  const lvl = seededMission(key);
  return Object.assign(lvl, {
    id: 'daily', name: 'Daily Nightmare', daily: true,
    brief: `Today's chaos: ${VEH_DEFS[lvl.veh].name} through ${DISTRICTS[lvl.district].name.toLowerCase()}, ${lvl.park} finish. Same streets for everyone — one shot at glory.`,
  });
}
function weeklyLevel() {
  const key = weekKey();
  const lvl = seededMission(key, { big: true });
  return Object.assign(lvl, {
    id: 'weekly', name: 'Weekly Gauntlet', weekly: true,
    brief: `This week's gauntlet: a long ${VEH_DEFS[lvl.veh].name} haul through ${DISTRICTS[lvl.district].name.toLowerCase()}. One best score, all week to beat it. Resets Monday.`,
  });
}
// ---- friend challenge codes (no backend — the code IS the level) ----
// PN.m<missionId>.<score36>  |  PN.s<seed>.<score36>  |  PN.w<seed>.<score36>
function makeChallengeCode(level, score) {
  let payload = null;
  if (level.chalSrc) payload = level.chalSrc; // re-share a challenge you just played
  else if (typeof level.id === 'number') payload = 'm' + level.id;
  else if (level.weekly) payload = 'w' + level.seed;
  else if (level.seed) payload = 's' + level.seed;
  if (!payload) return null;
  return `PN.${payload}.${Math.max(0, Math.round(score)).toString(36).toUpperCase()}`;
}
function parseChallengeCode(str) {
  const m = /^PN\.([mws])([A-Za-z0-9_-]{1,24})\.([A-Z0-9]{1,8})$/i.exec(String(str || '').trim());
  if (!m) return null;
  const kind = m[1].toLowerCase(), data = m[2], target = parseInt(m[3], 36);
  if (!Number.isFinite(target)) return null;
  let lvl = null;
  if (kind === 'm') {
    const src = LEVELS[parseInt(data, 10) - 1];
    if (!src || String(src.id) !== data) return null;
    lvl = Object.assign({}, src);
  } else {
    lvl = seededMission(data, { big: kind === 'w' });
  }
  return Object.assign(lvl, {
    id: 'challenge', name: 'Friend Challenge', daily: false, weekly: false,
    challenge: { target }, chalSrc: kind + data,
    brief: `A friend threw down the gauntlet: beat ${target.toLocaleString()} points on these exact streets. No pressure. (Immense pressure.)`,
  });
}

// Free Roam: a long seeded cruise with a coin trail, no clock, no fail.
// cfg = {seed, district, time: 'day'|'dusk'|'night', wx: 'clear'|'rain', veh}
// — same seed + settings always regenerates the same streets (shareable).
function freeRoamLevel(cfg) {
  cfg = cfg || {};
  const seedStr = String(cfg.seed || Math.random().toString(36).slice(2, 10));
  let seed = 0;
  for (let i = 0; i < seedStr.length; i++) seed = (seed * 31 + seedStr.charCodeAt(i)) >>> 0;
  const rng = mulberry32(seed);
  const district = cfg.district !== undefined ? cfg.district : Math.floor(rng() * DISTRICTS.length);
  const vehKeys = ['hatch', 'wagon', 'limo', 'icecream', 'bus', 'tank', 'ufo', 'kart', 'monster'].filter(vehicleUnlocked);
  const veh = (cfg.veh && vehicleUnlocked(cfg.veh)) ? cfg.veh
    : vehKeys[Math.floor(rng() * vehKeys.length)] || 'hatch';
  const segs = [{ t: 'S', len: 120 + rng() * 60 }];
  let dist = segs[0].len;
  const nSeg = 13 + Math.floor(rng() * 5);
  for (let i = 0; i < nSeg; i++) {
    const r = rng();
    if (r < 0.38) { const L = 90 + rng() * 110; segs.push({ t: 'S', len: L }); dist += L; }
    else if (r < 0.5 && dist > 200) { segs.push({ t: 'X', lights: district !== 0 }); dist += 26; }
    else {
      // sharper corners for a twistier cruise
      const turn = { t: rng() < 0.5 ? 'L' : 'R', r: 24 + rng() * 14, a: rng() < 0.78 ? 90 : 60 };
      segs.push(turn); dist += turn.r * rad(turn.a);
    }
  }
  segs.push({ t: 'S', len: 210 });
  return {
    id: 'roam', district, name: 'Free Roam', veh, lanes: 2,
    brief: 'No timer. No fail. Just you, the open road, and a suspicious amount of loose change floating over it. Park whenever you reach the bay.',
    par: 9999, traffic: 0.45 + rng() * 0.5, peds: 8, park: 'bay', margin: 3.2,
    segs, s2: 900, s3: 1500, free: true,
    rain: DISTRICTS[district].snow ? false : (cfg.wx ? cfg.wx === 'rain' : (district === 2 && rng() < 0.4)),
    snow: DISTRICTS[district].snow || undefined,
    ice: DISTRICTS[district].snow ? 8 : undefined,
    time: cfg.time, seed: seedStr,
  };
}

// ============================================================
// WORLD BUILDER
// ============================================================
class World {
  constructor(scene, level, vehKey) {
    this.scene = scene;
    this.level = level;
    this.dist = applyMood(DISTRICTS[level.district], level);
    this.vehKey = vehKey;
    this.group = new THREE.Group();
    scene.add(this.group);
    this.route = compileRoute(level.segs);
    this.RW = level.lanes * LANE_W + PARK_STRIP; // road half-width incl. parking strip
    this.statics = [];                        // wall/prop obbs {x,y,h,hl,hw,type}
    this.cones = [];                          // knockable
    this.parked = [];                         // parked car obstacles {obb, refs}
    this.potholes = [];
    this.bumps = [];
    this.puddles = [];
    this.icePatches = [];                     // {x,y,r} — low-grip zones (snow district)
    this.lights = [];                         // traffic-light controllers per intersection
    this.trafficLightMeshes = [];
    this.lodProps = [];                       // static props, distance-culled per frame
    this.rng = mulberry32((level.seed || level.id * 7919) >>> 0);
    this.build();
  }

  r() { return this.rng(); }
  rr(a, b) { return a + this.rng() * (b - a); }

  // ---- ribbon strip builder along route ----
  ribbon(s0, s1, tA, tB, y, mat, vLen, yB) {
    const route = this.route;
    const i0 = clamp(Math.floor(s0 / route.step), 0, route.pts.length - 1);
    const i1 = clamp(Math.ceil(s1 / route.step), 0, route.pts.length - 1);
    const n = i1 - i0 + 1;
    if (n < 2) return null;
    const pos = new Float32Array(n * 2 * 3);
    const uv = new Float32Array(n * 2 * 2);
    const idx = [];
    for (let k = 0; k < n; k++) {
      const p = route.pts[i0 + k];
      const rx = -Math.sin(p.h), ry = Math.cos(p.h);
      const ax = p.x + rx * tA, ay = p.y + ry * tA;
      const bx = p.x + rx * tB, by = p.y + ry * tB;
      pos[k * 6] = ax; pos[k * 6 + 1] = y; pos[k * 6 + 2] = ay;
      pos[k * 6 + 3] = bx; pos[k * 6 + 4] = yB !== undefined ? yB : y; pos[k * 6 + 5] = by;
      const v = p.s / (vLen || 6);
      uv[k * 4] = 0; uv[k * 4 + 1] = v;
      uv[k * 4 + 2] = 1; uv[k * 4 + 3] = v;
      if (k < n - 1) {
        const b = k * 2;
        idx.push(b, b + 2, b + 1, b + 1, b + 2, b + 3);
      }
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.setIndex(idx);
    geo.computeVertexNormals();
    mat.side = THREE.DoubleSide; // strip winding flips with tA/tB order
    const mesh = new THREE.Mesh(geo, mat);
    mesh.receiveShadow = true;
    this.group.add(mesh);
    return mesh;
  }

  // dynamic = the object (or its group) is animated later, so keep live matrices
  place(mesh, x, y, h, elev, dynamic) {
    mesh.position.set(x, elev || 0, y);
    mesh.rotation.y = -h;
    this.group.add(mesh);
    if (!dynamic) {
      // Static scenery never moves again: bake each matrix once and stop the
      // renderer recomputing them for thousands of props every single frame.
      // matrixWorldNeedsUpdate makes the next traversal fill in matrixWorld
      // (and force it down the subtree) exactly once.
      mesh.traverse(o => { o.updateMatrix(); o.matrixAutoUpdate = false; });
      mesh.matrixWorldNeedsUpdate = true;
      // register for distance culling (see updateLOD)
      this.lodProps.push({ o: mesh, x, z: y });
    }
    return mesh;
  }

  build() {
    const D = this.dist, L = this.level, route = this.route, RW = this.RW;
    const night = D.night;

    // ---------- sky & fog ----------
    const skyGeo = new THREE.SphereGeometry(900, 32, 20);
    const skyMat = new THREE.MeshBasicMaterial({
      map: Assets.skyTexture(D.sky[0], D.sky[1], D.sky[2]), side: THREE.BackSide, depthWrite: false, fog: false,
    });
    this.sky = new THREE.Mesh(skyGeo, skyMat);
    // center sky roughly mid-route
    const mid = route.sampleAt(route.length / 2);
    this.sky.position.set(mid.x, 0, mid.y);
    this.group.add(this.sky);
    // updateLOD culls static props at this radius. Measured: pulling the fog
    // in on low-end devices gained nothing once culling was in place, so every
    // device keeps the full, intended draw distance.
    this.fogFar = D.fogFar;
    this.scene.fog = new THREE.Fog(new THREE.Color(D.fog), 40, this.fogFar);

    if (D.stars) {
      // two star layers: dense faint field + sparse bright stars
      for (const [count, size, color, op] of [[420, 1.6, 0xb8c6e8, 0.7], [90, 2.8, 0xf0f5ff, 1.0]]) {
        const starGeo = new THREE.BufferGeometry();
        const sp = new Float32Array(count * 3);
        for (let i = 0; i < count; i++) {
          const a = this.rr(0, TAU), e = this.rr(0.1, 1.45);
          sp[i * 3] = mid.x + Math.cos(a) * Math.cos(e) * 800;
          sp[i * 3 + 1] = Math.sin(e) * 800;
          sp[i * 3 + 2] = mid.y + Math.sin(a) * Math.cos(e) * 800;
        }
        starGeo.setAttribute('position', new THREE.BufferAttribute(sp, 3));
        this.group.add(new THREE.Points(starGeo, new THREE.PointsMaterial({
          color, size, sizeAttenuation: false, fog: false, transparent: true, opacity: op,
        })));
      }
    }
    // ---------- distant horizon silhouette ring ----------
    {
      const style = D.weird ? 'mountains' : (D.houses ? 'hills' : (night ? 'nightcity' : 'city'));
      const tex = Assets.skylineTexture(style, D.fog);
      tex.repeat.set(3, 1);
      const ring = new THREE.Mesh(
        new THREE.CylinderGeometry(640, 640, 150, 48, 1, true),
        new THREE.MeshBasicMaterial({ map: tex, transparent: true, side: THREE.BackSide, depthWrite: false, fog: false })
      );
      ring.position.set(mid.x, 60, mid.y);
      this.group.add(ring);
    }

    // sun glow (day) / cratered moon with halo (night)
    if (night) {
      const moon = new THREE.Sprite(new THREE.SpriteMaterial({
        map: Assets.moonTexture(), transparent: true, depthWrite: false, fog: false,
      }));
      moon.scale.setScalar(46);
      moon.position.set(mid.x + D.sun[2][0] * 6, D.sun[2][1] * 5, mid.y + D.sun[2][2] * 6);
      this.group.add(moon);
      const halo = new THREE.Sprite(new THREE.SpriteMaterial({
        map: Assets.radialSprite('rgba(190,205,255,.5)'), transparent: true, opacity: 0.5,
        depthWrite: false, fog: false, blending: THREE.AdditiveBlending,
      }));
      halo.scale.setScalar(130);
      halo.position.copy(moon.position);
      this.group.add(halo);
    } else {
      const sunPos = new THREE.Vector3(mid.x + D.sun[2][0] * 6, D.sun[2][1] * 5, mid.y + D.sun[2][2] * 6);
      const sunSpr = new THREE.Sprite(new THREE.SpriteMaterial({
        map: Assets.radialSprite('rgba(255,244,200,.95)'),
        transparent: true, depthWrite: false, fog: false, blending: THREE.AdditiveBlending,
      }));
      sunSpr.scale.setScalar(150);
      sunSpr.position.copy(sunPos);
      this.group.add(sunSpr);
      // wide atmospheric-scattering halo so the sky brightens around the sun
      const scat = new THREE.Sprite(new THREE.SpriteMaterial({
        map: Assets.radialSprite('rgba(255,232,178,.5)'),
        transparent: true, opacity: 0.42, depthWrite: false, fog: false, blending: THREE.AdditiveBlending,
      }));
      scat.scale.setScalar(430);
      scat.position.copy(sunPos);
      this.group.add(scat);
    }

    // ---------- drifting clouds: shaded cumulus clumps + cirrus veil ----------
    this.clouds = [];
    {
      // sprite tint: sunlit white by day, sunset peach at dusk, candy-pink for
      // WEIRD, moonlit slate at night
      const dusk = this.level.time === 'dusk';
      const tint = night ? 0x55618a : (D.weird ? 0xffd6e8 : (dusk ? 0xffc9a2 : 0xffffff));
      const nClump = night ? 5 : 10;
      for (let i = 0; i < nClump; i++) {
        const clump = new THREE.Group();
        const variant = i % 3;
        const baseW = this.rr(70, 150);
        const mkSpr = (vv, op) => new THREE.Sprite(new THREE.SpriteMaterial({
          map: Assets.cloudPuffTex(vv), color: tint,
          transparent: true, opacity: op, depthWrite: false, fog: false,
        }));
        const hero = mkSpr(variant, night ? this.rr(0.3, 0.42) : this.rr(0.78, 0.95));
        hero.scale.set(baseW, baseW * 0.62, 1);
        clump.add(hero);
        const nSat = randi(2, 4);
        for (let k = 0; k < nSat; k++) {
          const s = mkSpr((variant + k + 1) % 3, night ? 0.24 : this.rr(0.5, 0.75));
          const sw = baseW * this.rr(0.35, 0.62);
          s.scale.set(sw, sw * 0.62, 1);
          s.position.set(this.rr(-0.58, 0.58) * baseW, this.rr(-0.1, 0.06) * baseW, this.rr(-12, 12));
          clump.add(s);
        }
        clump.position.set(mid.x + this.rr(-520, 520), this.rr(100, 230), mid.y + this.rr(-520, 520));
        this.group.add(clump);
        this.clouds.push({ g: clump, vx: this.rr(0.8, 2.2) * (this.r() < 0.5 ? -1 : 1), cx: mid.x });
      }
      if (!night) {
        for (let i = 0; i < 5; i++) {
          const ci = new THREE.Sprite(new THREE.SpriteMaterial({
            map: Assets.cirrusTex(), color: D.weird ? 0xffe2f0 : 0xffffff,
            transparent: true, opacity: this.rr(0.26, 0.48), depthWrite: false, fog: false,
          }));
          ci.scale.set(this.rr(260, 430), this.rr(30, 52), 1);
          ci.position.set(mid.x + this.rr(-600, 600), this.rr(250, 340), mid.y + this.rr(-600, 600));
          this.group.add(ci);
          this.clouds.push({ g: ci, vx: this.rr(0.4, 1.1) * (this.r() < 0.5 ? -1 : 1), cx: mid.x });
        }
      }
    }

    // ---------- fireflies (night ambience) ----------
    this.fireflies = null;
    if (night) {
      const N = 44;
      const fg = new THREE.BufferGeometry();
      const fp = new Float32Array(N * 3);
      this.ffSeed = [];
      for (let i = 0; i < N; i++) {
        const s = this.rr(10, route.length - 10);
        const t = pick([RW + this.rr(1, 6), -RW - this.rr(1, 6)]);
        const p = routePos(route, s, t);
        fp[i * 3] = p.x; fp[i * 3 + 1] = this.rr(0.4, 2.2); fp[i * 3 + 2] = p.y;
        this.ffSeed.push({ x: p.x, y: fp[i * 3 + 1], z: p.y, ph: this.rr(0, TAU), sp: this.rr(0.4, 1.1) });
      }
      fg.setAttribute('position', new THREE.BufferAttribute(fp, 3));
      this.fireflies = new THREE.Points(fg, new THREE.PointsMaterial({
        color: 0xc8ffa8, size: 3.2, sizeAttenuation: false, transparent: true, opacity: 0.9,
        blending: THREE.AdditiveBlending, depthWrite: false,
      }));
      this.group.add(this.fireflies);
    }

    // ---------- lighting ----------
    this.hemi = new THREE.HemisphereLight(D.hemi[0], D.hemi[1], D.hemi[2]);
    this.group.add(this.hemi);
    this.sun = new THREE.DirectionalLight(D.sun[0], D.sun[1]);
    this.sun.position.set(D.sun[2][0], D.sun[2][1], D.sun[2][2]);
    this.sun.castShadow = true;
    const shadowSize = Save.data.settings.hq ? 2048 : 1024;
    this.sun.shadow.mapSize.set(shadowSize, shadowSize);
    this.sun.shadow.camera.near = 10;
    this.sun.shadow.camera.far = 400;
    const sc = 42;
    this.sun.shadow.camera.left = -sc; this.sun.shadow.camera.right = sc;
    this.sun.shadow.camera.top = sc; this.sun.shadow.camera.bottom = -sc;
    this.sun.shadow.camera.updateProjectionMatrix();
    this.sun.shadow.bias = -0.0008;
    this.group.add(this.sun);
    this.group.add(this.sun.target);

    // ---------- ground ----------
    const groundTex = Assets.grassTexture(D.ground[0], D.ground[1]);
    groundTex.repeat.set(240, 240);
    const ground = new THREE.Mesh(
      new THREE.PlaneGeometry(2600, 2600),
      new THREE.MeshLambertMaterial({ map: groundTex })
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.set(mid.x, -0.01, mid.y);
    ground.receiveShadow = true;
    this.group.add(ground);

    // ---------- road / curbs / sidewalks ----------
    const roadTex = Assets.roadTexture(L.lanes, night);
    roadTex.repeat.set(1, 1);
    this.ribbon(0, route.length, -RW, RW, 0.02, Assets.asphaltMat(roadTex, !!L.rain), 14);
    const curbMat = new THREE.MeshLambertMaterial({ map: Assets.curbTexture(night) });
    const walkMat = new THREE.MeshLambertMaterial({ map: Assets.sidewalkTexture(night) });
    // mowed verge: slightly deeper green band hugging the sidewalk so the
    // lawn doesn't crash straight into concrete
    const vergeBase = '#' + new THREE.Color(D.ground[0]).multiplyScalar(0.86).getHexString();
    const vergeMat = new THREE.MeshLambertMaterial({ map: Assets.grassTexture(vergeBase, D.ground[1]) });
    const walkOut = RW + 0.35 + SIDEWALK_W;
    for (const sgn of [1, -1]) {
      // curb face + top sliver
      this.ribbon(0, route.length, sgn * RW, sgn * (RW + 0.35), 0.12, curbMat, 12);
      // sidewalk top
      this.ribbon(0, route.length, sgn * (RW + 0.35), sgn * walkOut, 0.12, walkMat, 4);
      // grass verge shoulder
      this.ribbon(0, route.length, sgn * walkOut, sgn * (walkOut + 2.4), 0.035, vergeMat, 9);
    }

    // ---------- intersections ----------
    const asph = Assets.plainAsphalt(night);
    for (const inter of route.inters) {
      const c = route.sampleAt((inter.s0 + inter.s1) / 2);
      const iw = (inter.s1 - inter.s0);
      // plain patch over the crossing (covers center markings)
      const patch = new THREE.Mesh(
        new THREE.PlaneGeometry(iw, RW * 2 + 0.2),
        Assets.asphaltMat(asph, !!L.rain)
      );
      patch.rotation.x = -Math.PI / 2;
      patch.rotation.z = -c.h;
      patch.position.set(c.x, 0.028, c.y);
      patch.receiveShadow = true;
      this.group.add(patch);
      // cross-street stub (visual, drivable 12m, then barrier)
      const stubLen = 34;
      const crossH = c.h + Math.PI / 2;
      for (const sgn of [1, -1]) {
        const sx = c.x + Math.cos(crossH) * sgn, sy = c.y + Math.sin(crossH) * sgn;
        const stub = new THREE.Mesh(
          new THREE.PlaneGeometry(stubLen, RW * 2),
          Assets.asphaltMat(asph, !!L.rain)
        );
        stub.rotation.x = -Math.PI / 2;
        stub.rotation.z = -crossH;
        stub.position.set(c.x + Math.cos(crossH) * sgn * (stubLen / 2 + RW), 0.018, c.y + Math.sin(crossH) * sgn * (stubLen / 2 + RW));
        stub.receiveShadow = true;
        this.group.add(stub);
        // barrier at stub end
        const bx = c.x + Math.cos(crossH) * sgn * (stubLen + RW - 1.2);
        const by = c.y + Math.sin(crossH) * sgn * (stubLen + RW - 1.2);
        const bar = CarFactory.boxMesh(0.35, 1.0, RW * 2 - 1, 0xe8622d);
        for (let k = 0; k < 3; k++) {
          const stripe = CarFactory.boxMesh(0.37, 0.18, 1.4, 0xffffff);
          stripe.position.set(0, 0.18 - k * 0.02, -RW + 1.4 + k * (RW - 1));
          bar.add(stripe);
        }
        this.place(bar, bx, by, crossH, 0.5);
        this.statics.push({ x: bx, y: by, h: crossH, hl: 0.4, hw: RW - 0.5, type: 'barrier' });
      }
      // crosswalks at both edges of intersection
      for (const es of [inter.s0 - 2.2, inter.s1 + 2.2]) {
        const ep = route.sampleAt(es);
        const cw = new THREE.Mesh(
          new THREE.PlaneGeometry(3.4, RW * 2),
          new THREE.MeshLambertMaterial({ map: Assets.crosswalkTexture(night), transparent: true })
        );
        cw.rotation.x = -Math.PI / 2;
        cw.rotation.z = -ep.h;
        cw.position.set(ep.x, 0.032, ep.y);
        this.group.add(cw);
      }
      // lights or stop signs
      if (inter.lights) {
        const ctrl = { inter, timer: this.rr(0, 6), state: 0 }; // 0 green,1 amber,2 red (for player road)
        // two light posts for player road (far-right corners of each approach)
        const p0 = routePos(route, inter.s0 - 2, RW + 1.4);
        const tl1 = PropFactory.trafficLight();
        this.place(tl1.group, p0.x, p0.y, p0.h + Math.PI);
        const p1 = routePos(route, inter.s1 + 2, -RW - 1.4);
        const tl2 = PropFactory.trafficLight();
        this.place(tl2.group, p1.x, p1.y, p1.h);
        ctrl.meshes = [tl1, tl2];
        this.lights.push(ctrl);
        inter.ctrl = ctrl;
      } else {
        const p0 = routePos(route, inter.s0 - 3, RW + 1.6);
        this.place(PropFactory.signPost(PropFactory.stopSignTex(), 0.75), p0.x, p0.y, p0.h + Math.PI / 2);
        inter.stopSign = true;
      }
    }

    // ---------- school zone signs & paint ----------
    for (const z of route.zones) {
      if (z.kind !== 'school') continue;
      const p = routePos(route, z.s0 - 6, RW + 1.6);
      this.place(PropFactory.signPost(PropFactory.schoolSignTex(), 0.8), p.x, p.y, p.h + Math.PI / 2);
      const p2 = routePos(route, z.s0 + 4, L.lanes * LANE_W * 0.5);
      const paint = new THREE.Mesh(
        new THREE.PlaneGeometry(RW, 5),
        new THREE.MeshBasicMaterial({ map: Assets.textTexture('SLOW', '#ffffff'), transparent: true, opacity: 0.85, depthWrite: false })
      );
      paint.rotation.x = -Math.PI / 2;
      paint.rotation.z = -p2.h + Math.PI / 2;
      paint.position.set(p2.x, 0.035, p2.y);
      this.group.add(paint);
      // speed bump at each end
      this.addBump(z.s0 + 1);
      this.addBump(z.s1 - 1);
    }

    // ---------- buildings, trees, lamps ----------
    this.buildScenery();

    // ---------- district set pieces ----------
    if (D.marina) this.buildWaterfront();
    if (D.snow) this.buildWinter();

    // ---------- gliding birds (day districts flagged birds, plus the marina) ----------
    this.gulls = null;
    if (D.birds && !night) {
      this.gulls = [];
      const gtex = Assets.gullTex();
      for (let i = 0; i < 7; i++) {
        const spr = new THREE.Sprite(new THREE.SpriteMaterial({
          map: gtex, transparent: true, opacity: 0.85, depthWrite: false, fog: false,
        }));
        const sc = this.rr(1.6, 2.6);
        spr.scale.set(sc, sc * 0.6, 1);
        this.group.add(spr);
        const cs = this.rr(20, route.length - 20);
        const cp = routePos(route, cs, this.rr(-60, 60));
        this.gulls.push({
          spr, cx: cp.x, cz: cp.y, r: this.rr(14, 34), alt: this.rr(18, 38),
          ph: this.rr(0, TAU), sp: this.rr(0.14, 0.3) * (this.r() < 0.5 ? -1 : 1),
        });
      }
    }

    // ---------- hazards from level ----------
    if (L.cones) {
      // slalom cones in player lane on a straight mid-route
      const base = route.length * 0.42;
      for (let i = 0; i < L.cones; i++) {
        const s = base + i * 9;
        if (this.nearInter(s, 14)) continue;
        const t = (i % 2 === 0 ? RW * 0.32 : RW * 0.72);
        this.addCone(s, t);
      }
    }
    if (L.rain) {
      for (let i = 0; i < 14; i++) {
        const s = this.rr(40, route.length - 60);
        if (this.nearInter(s, 10)) continue;
        const t = pick([RW - 1.6, -RW + 1.6, RW * 0.4]);
        const p = routePos(route, s, t);
        const r = this.rr(1.4, 2.6);
        const pud = PropFactory.puddle(r);
        pud.position.set(p.x, 0.026, p.y);
        this.group.add(pud);
        this.puddles.push({ x: p.x, y: p.y, r, cd: 0 });
      }
    }
    // ---------- free-roam coin trail ----------
    this.coinList = [];
    if (L.free) {
      const coinGeo = Assets.geo('coin3d', () => new THREE.CylinderGeometry(0.42, 0.42, 0.09, 18));
      const coinMat = new THREE.MeshStandardMaterial({
        color: 0xffd24a, metalness: 0.85, roughness: 0.22,
        emissive: 0xcc8800, emissiveIntensity: 0.6, envMapIntensity: 1.5,
      });
      for (let s = 50; s < route.length - 80; s += 20 + this.rr(0, 16)) {
        if (this.nearInter(s, 9)) continue;
        // one lane center on the driving (+t) side per cluster
        const t = (Math.floor(this.rr(0, L.lanes)) + 0.5) * LANE_W;
        for (let k = 0; k < 3; k++) {
          const p = routePos(route, s + k * 3.4, t);
          const g = new THREE.Group();
          const m = new THREE.Mesh(coinGeo, coinMat);
          m.rotation.x = Math.PI / 2; // upright disc; the group spins on Y
          g.add(m);
          g.position.set(p.x, 1.0, p.y);
          this.group.add(g);
          this.coinList.push({ g, x: p.x, y: p.y, ph: this.rr(0, 6.28), taken: false });
        }
      }
    }

    // potholes in later districts
    if (L.district >= 1) {
      const n = L.district * 3 + 2;
      for (let i = 0; i < n; i++) {
        const s = this.rr(50, route.length - 80);
        if (this.nearInter(s, 12)) continue;
        const t = this.rr(-RW + 1.2, RW - 1.2);
        const p = routePos(route, s, t);
        const hole = new THREE.Mesh(
          Assets.geo('pothole', () => new THREE.CircleGeometry(0.55, 10)),
          Assets.lambert('holeDark', { color: 0x1c1e24 })
        );
        hole.rotation.x = -Math.PI / 2;
        hole.position.set(p.x, 0.03, p.y);
        this.group.add(hole);
        this.potholes.push({ x: p.x, y: p.y, r: 0.75, cd: 0 });
      }
    }

    // ---------- parked cars along curbs (obstacles + decor) ----------
    this.placeParkedCars();

    // ---------- destination & parking spot ----------
    this.buildDestination();

    // ---------- GPS chevrons ----------
    this.chevrons = [];
    const chevGeo = Assets.geo('chev', () => {
      const shape = new THREE.Shape();
      shape.moveTo(-0.7, -0.55); shape.lineTo(0.25, 0); shape.lineTo(-0.7, 0.55);
      shape.lineTo(-0.25, 0); shape.closePath();
      return new THREE.ShapeGeometry(shape);
    });
    for (let i = 0; i < 4; i++) {
      const ch = new THREE.Mesh(chevGeo, new THREE.MeshBasicMaterial({
        color: 0x4de6ff, transparent: true, opacity: 0.85, depthWrite: false, side: THREE.DoubleSide,
      }));
      ch.rotation.x = -Math.PI / 2;
      ch.renderOrder = 3;
      this.group.add(ch);
      this.chevrons.push(ch);
    }
  }

  nearInter(s, pad) {
    for (const it of this.route.inters) {
      if (s > it.s0 - pad && s < it.s1 + pad) return true;
    }
    return false;
  }

  // Winding routes can double back beside themselves: a prop placed at a
  // lateral offset from ITS segment may then sit on top of ANOTHER street.
  // route.project finds the nearest segment overall — if the spot projects
  // meaningfully CLOSER than the offset it was placed at, a different street
  // is underneath it, so don't build there. (Comparing against intendedT
  // rather than a fixed corridor width keeps sidewalk furniture legal.)
  clearOfRoad(x, y, intendedT, slack) {
    const pr = this.route.project(x, y);
    return Math.abs(pr.t) > Math.abs(intendedT) - (slack || 3);
  }

  addCone(s, t) {
    const p = routePos(this.route, s, t);
    const mesh = PropFactory.cone();
    this.place(mesh, p.x, p.y, this.rr(0, TAU), 0, true); // cones get knocked flying
    this.cones.push({ x: p.x, y: p.y, h: 0, hl: 0.21, hw: 0.21, mesh, alive: true, vx: 0, vy: 0, vr: 0, air: 0 });
  }
  addBump(s) {
    const p = routePos(this.route, s, 0);
    const bump = PropFactory.bumpStrip(this.RW * 2 - 0.6);
    this.place(bump, p.x, p.y, p.h, 0.02);
    this.bumps.push({ s, cd: 0 });
  }

  // ---------- SUNSET MARINA: ocean, beach, pier, boats, lighthouse ----------
  // The waterfront hugs the LEFT (-t) side of the route in contiguous runs;
  // runs are broken wherever the route doubles back over the water band.
  buildWaterfront() {
    const route = this.route, RW = this.RW;
    const wallT = RW + 0.35 + SIDEWALK_W;
    // water sits a hair ABOVE the giant ground plane (y -0.01) so it reads,
    // but below the sand (0.014) and road (0.02)
    const waterIn = wallT + 8, waterOut = wallT + 240, waterY = 0.004;
    // shared animated water material
    const wtex = Assets.waterTex();
    wtex.repeat.set(7, 1);
    this.waterTexs = [wtex];
    const waterMat = new THREE.MeshPhongMaterial({
      map: wtex, color: 0xdff2ff, shininess: 170, specular: 0xa8d8f0,
      transparent: true, opacity: 0.96,
    });
    const sandMat = new THREE.MeshLambertMaterial({ map: Assets.grassTexture('#e2cd9c', '#d6bd88') });
    // find clear runs
    const runs = [];
    let runStart = null;
    const probeOk = (s) => {
      const pA = routePos(route, s, -(wallT + 20));
      const pB = routePos(route, s, -(wallT + 60));
      return this.clearOfRoad(pA.x, pA.y, wallT + 20, 12) && this.clearOfRoad(pB.x, pB.y, wallT + 60, 52);
    };
    for (let s = 0; s <= route.length; s += 6) {
      if (s < route.length && probeOk(s)) {
        if (runStart === null) runStart = s;
      } else if (runStart !== null) {
        if (s - runStart > 36) runs.push([runStart, s - 6]);
        runStart = null;
      }
    }
    // a run reaching the end of the route never hits the else branch above
    if (runStart !== null && route.length - runStart > 36) runs.push([runStart, route.length - 2]);
    this.boats = [];
    let pierBuilt = false, lhBuilt = false;
    for (const [s0, s1] of runs) {
      // beach strip easing down to the waterline, then the water sheet
      this.ribbon(s0, s1, -(wallT + 2.2), -(waterIn + 2), 0.014, sandMat, 10, waterY - 0.002);
      this.ribbon(s0, s1, -waterIn, -waterOut, waterY, waterMat, 20);
      // boats bobbing offshore
      for (let s = s0 + 18; s < s1 - 14; s += this.rr(38, 62)) {
        const t = -(wallT + this.rr(18, 46));
        const p = routePos(route, s, t);
        if (!this.clearOfRoad(p.x, p.y, Math.abs(t), 10)) continue;
        const kind = this.r() < 0.35 ? 'yacht' : 'sail';
        const b = PropFactory.boat(kind);
        this.place(b, p.x, p.y, this.rr(0, TAU), 0.02, true); // bobs in updateAmbient
        this.boats.push({ g: b, base: 0.02, ph: this.rr(0, TAU) });
      }
      // buoys near the swim line
      for (let s = s0 + 10; s < s1 - 8; s += this.rr(30, 46)) {
        const p = routePos(route, s, -(wallT + this.rr(10, 14)));
        const bu = PropFactory.buoy();
        this.place(bu, p.x, p.y, 0, -0.02, true); // bobs in updateAmbient
        this.boats.push({ g: bu, base: -0.02, ph: this.rr(0, TAU) });
      }
      // one wooden pier out into the bay
      if (!pierBuilt && s1 - s0 > 80) {
        pierBuilt = true;
        const ps = (s0 + s1) / 2;
        const pc = routePos(route, ps, -(wallT + 16)); // deck center, 26m long over t 3..29
        const deck = new THREE.Mesh(
          new THREE.BoxGeometry(26, 0.3, 3.4),
          new THREE.MeshLambertMaterial({ map: Assets.woodTex() })
        );
        deck.castShadow = true;
        // long axis along the t (offshore) direction
        this.place(deck, pc.x, pc.y, pc.h + Math.PI / 2, 0.55);
        // piles marching out under the deck edges
        for (let k = 0; k <= 5; k++) {
          for (const so of [-1.4, 1.4]) {
            const pp = routePos(route, ps + so, -(wallT + 4 + k * 4.8));
            const pile = new THREE.Mesh(
              Assets.geo('pierPile', () => new THREE.CylinderGeometry(0.14, 0.16, 1.8, 7)),
              PropFactory.woodMat()
            );
            pile.position.set(pp.x, -0.15, pp.y);
            this.group.add(pile);
          }
        }
        // moored sailboat at the pier tip
        const tipP = routePos(route, ps + 5, -(wallT + 28));
        const moored = PropFactory.boat('sail');
        this.place(moored, tipP.x, tipP.y, pc.h + Math.PI / 2 + this.rr(-0.3, 0.3), 0.02, true); // bobs
        this.boats.push({ g: moored, base: 0.02, ph: this.rr(0, TAU) });
      }
      // lighthouse guards the last stretch of water before the destination
      if (!lhBuilt && s1 > route.length * 0.6) {
        lhBuilt = true;
        const ls = Math.min(s1 - 12, route.length - 50);
        const lp = routePos(route, ls, -(wallT + 24));
        const lh = PropFactory.lighthouse();
        this.place(lh.group, lp.x, lp.y, 0, 0);
        this.lhBeam = lh.beam;
        // the tower stays baked; only the sweeping beam needs a live matrix
        this.lhBeam.matrixAutoUpdate = true;
      }
    }
  }

  // ---------- FROSTPEAK: ice patches, snowmen, aurora at night ----------
  buildWinter() {
    const route = this.route, RW = this.RW, L = this.level, D = this.dist;
    const wallT = RW + 0.35 + SIDEWALK_W;
    // ice patches — the road hazard that defines the district
    const nIce = L.ice !== undefined ? L.ice : 8;
    for (let i = 0; i < nIce; i++) {
      const s = this.rr(45, route.length - 70);
      if (this.nearInter(s, 10)) continue;
      const t = this.rr(-RW + 1.3, RW - 1.3);
      const p = routePos(route, s, t);
      const r = this.rr(1.7, 3.0);
      const ice = PropFactory.icePatch(r);
      ice.position.set(p.x, 0.026, p.y);
      this.group.add(ice);
      this.icePatches.push({ x: p.x, y: p.y, r });
    }
    // snowmen guarding front yards
    for (let s = 30; s < route.length - 40; s += this.rr(55, 85)) {
      if (this.nearInter(s, 12)) continue;
      const sgn = this.r() < 0.5 ? 1 : -1;
      const t = sgn * (wallT + this.rr(1.0, 3.5));
      const p = routePos(route, s, t);
      if (!this.clearOfRoad(p.x, p.y, Math.abs(t), 2)) continue;
      const sm = PropFactory.snowman();
      sm.scale.setScalar(this.rr(0.8, 1.15));
      this.place(sm, p.x, p.y, this.rr(0, TAU));
      this.statics.push({ x: p.x, y: p.y, h: 0, hl: 0.5, hw: 0.5, type: 'snowman' });
    }
    // aurora curtains ripple across the night sky
    this.auroras = null;
    if (D.night || this.level.time === 'night') {
      this.auroras = [];
      const mid = route.sampleAt(route.length / 2);
      for (let i = 0; i < 3; i++) {
        const tex = Assets.auroraTex().clone();
        tex.needsUpdate = true;
        tex.repeat.set(this.rr(0.8, 1.4), 1);
        const mat = new THREE.MeshBasicMaterial({
          map: tex, transparent: true, opacity: 0.55, side: THREE.DoubleSide,
          blending: THREE.AdditiveBlending, depthWrite: false, fog: false,
        });
        const mesh = new THREE.Mesh(
          new THREE.CylinderGeometry(560 + i * 70, 560 + i * 70, 200 + i * 40, 40, 1, true, this.rr(0, TAU), this.rr(1.4, 2.2)),
          mat
        );
        mesh.position.set(mid.x, 210 + i * 45, mid.y);
        this.group.add(mesh);
        this.auroras.push({ mesh, sp: this.rr(0.004, 0.012) * (i % 2 === 0 ? 1 : -1), ph: this.rr(0, TAU) });
      }
    }
  }

  buildScenery() {
    const D = this.dist, route = this.route, RW = this.RW;
    const wallT = RW + 0.35 + SIDEWALK_W;
    this.chimSmoke = []; // cozy chimney smoke (snow district)
    // ---- buildings ----
    if (D.houses) {
      // suburbs: cute houses with pyramid roofs
      for (let s = 12; s < route.length - 30; s += 17) {
        if (this.nearInter(s, 20)) continue;
        for (const sgn of [1, -1]) {
          if (D.marina && sgn === -1) continue; // ocean side stays open water
          if (this.r() < 0.18) continue;
          const depth = this.rr(6, 8), wide = this.rr(7, 10);
          const hT = wallT + depth / 2 + 1.5;
          const p = routePos(route, s, sgn * hT);
          if (!this.clearOfRoad(p.x, p.y, hT)) continue;
          const g = new THREE.Group();
          const wallC = pick(D.bWall);
          const bodyH = this.rr(3.2, 4.2);
          const body = new THREE.Mesh(
            new THREE.BoxGeometry(wide, bodyH, depth),
            new THREE.MeshLambertMaterial({ color: wallC, map: Assets.sidingTex() })
          );
          body.castShadow = true;
          body.position.y = bodyH / 2;
          body.receiveShadow = true;
          g.add(body);
          // shingled gable roof with eave overhang (ridge parallel to street)
          // snow district: every roof carries a clean white blanket
          const shingC = D.snow ? pick(['#e9eff7', '#e2eaf3', '#eef3f9']) : pick(['#7a4438', '#5c4632', '#43507a', '#4c6244', '#585450']);
          const gableC = '#' + new THREE.Color(wallC).multiplyScalar(0.92).getHexString();
          const roof = PropFactory.gableRoof(wide + 0.8, depth + 0.9, this.rr(1.5, 2.1), shingC, gableC);
          roof.position.y = bodyH - 0.02;
          g.add(roof);
          // eaves fascia under the roof edge
          const fascia = CarFactory.boxMesh(wide + 0.5, 0.22, depth + 0.5, 0xf2efe6);
          fascia.position.y = bodyH + 0.05;
          g.add(fascia);
          // concrete foundation strip
          const found = CarFactory.boxMesh(wide + 0.12, 0.42, depth + 0.12, 0x9a968c);
          found.position.y = 0.21;
          g.add(found);
          // doors + framed windows on both z faces (one of them faces the street)
          for (const zs of [1, -1]) {
            const zf = zs * (depth / 2 + 0.02);
            const door = CarFactory.boxMesh(1.0, 1.9, 0.1, 0x6b4a2e);
            door.position.set(0, 0.95, zf);
            g.add(door);
            const knob = CarFactory.boxMesh(0.08, 0.08, 0.14, 0xd8c05a);
            knob.position.set(0.32, 0.95, zf);
            g.add(knob);
            // covered porch: slab, posts, sloped hood over the door
            const slab = CarFactory.boxMesh(2.1, 0.16, 1.15, 0xaaa69b);
            slab.position.set(0, 0.08, zs * (depth / 2 + 0.55));
            g.add(slab);
            for (const px of [-0.85, 0.85]) {
              const post = CarFactory.boxMesh(0.1, 2.15, 0.1, 0xf2efe6);
              post.position.set(px, 1.08, zs * (depth / 2 + 0.95));
              g.add(post);
            }
            const hood = CarFactory.boxMesh(2.3, 0.1, 1.35, new THREE.Color(shingC).multiplyScalar(1.08).getHex());
            hood.position.set(0, 2.24, zs * (depth / 2 + 0.5));
            hood.rotation.x = zs * 0.16;
            g.add(hood);
            for (const wx of [-wide * 0.28, wide * 0.28]) {
              const frame = CarFactory.boxMesh(1.3, 1.1, 0.08, 0xf5f2ea);
              frame.position.set(wx, 1.9, zf);
              g.add(frame);
              const win = new THREE.Mesh(
                Assets.geo('houseWin', () => new THREE.BoxGeometry(1.1, 0.9, 0.16)),
                CarFactory.glass()
              );
              win.position.set(wx, 1.9, zf);
              g.add(win);
              const sill = CarFactory.boxMesh(1.4, 0.09, 0.16, 0xe8e4d8);
              sill.position.set(wx, 1.32, zf);
              g.add(sill);
              // shutters
              for (const sx of [-0.78, 0.78]) {
                const sh = CarFactory.boxMesh(0.2, 1.06, 0.06, 0x37423a);
                sh.position.set(wx + sx, 1.9, zf);
                g.add(sh);
              }
            }
          }
          let chimLocal = null;
          if (this.r() < (D.snow ? 0.85 : 0.4)) {
            const chim = CarFactory.boxMesh(0.7, 1.8, 0.7, 0x9a5a44);
            chim.position.set(wide * 0.28, bodyH + 1.1, depth * 0.15);
            g.add(chim);
            const cap = CarFactory.boxMesh(0.82, 0.1, 0.82, 0x6e6a64);
            cap.position.set(wide * 0.28, bodyH + 2.02, depth * 0.15);
            g.add(cap);
            chimLocal = [wide * 0.28, bodyH + 2.1, depth * 0.15];
          }
          this.place(g, p.x, p.y, p.h);
          // cozy smoke drifting from cabin chimneys
          if (D.snow && chimLocal && this.chimSmoke.length < 18) {
            const [lx, ly, lz] = chimLocal;
            const ch = Math.cos(p.h), sh = Math.sin(p.h);
            const wx = p.x + lx * ch - lz * sh;
            const wz = p.y + lx * sh + lz * ch;
            for (let k = 0; k < 2; k++) {
              const spr = new THREE.Sprite(new THREE.SpriteMaterial({
                map: Assets.radialSprite('rgba(225,230,240,.55)'),
                transparent: true, opacity: 0, depthWrite: false,
              }));
              this.group.add(spr);
              this.chimSmoke.push({ spr, x: wx, y: ly, z: wz, t: k * 0.5, sp: this.rr(0.14, 0.22), ph: this.rr(0, TAU) });
            }
          }
          this.statics.push({ x: p.x, y: p.y, h: p.h, hl: wide / 2, hw: depth / 2, type: 'building' });
          // front yard dressing: bushes, flower beds, mailbox, trash bin
          if (this.r() < 0.5) {
            const fp = routePos(route, s + this.rr(-4, 4), sgn * (wallT + 0.6));
            if (this.clearOfRoad(fp.x, fp.y, wallT + 0.6, 2)) {
              const bush = PropFactory.bush(pick([0x4a9c4a, 0x5cb85c, 0x3f7d38, 0xb85a8a]));
              bush.scale.setScalar(this.rr(0.8, 1.4));
              this.place(bush, fp.x, fp.y, this.rr(0, TAU));
            }
          }
          if (this.r() < 0.55) {
            const fT = wallT + this.rr(1.2, 2.4);
            const fp = routePos(route, s + this.rr(2, 6), sgn * fT);
            if (this.clearOfRoad(fp.x, fp.y, fT, 2)) this.place(PropFactory.flowerBed(), fp.x, fp.y, this.rr(0, TAU));
          }
          if (this.r() < 0.45) {
            const mp = routePos(route, s - this.rr(3, 6), sgn * (RW + 0.35 + 0.5));
            if (this.clearOfRoad(mp.x, mp.y, RW + 0.85, 2)) {
              this.place(PropFactory.mailbox(), mp.x, mp.y, mp.h + (sgn > 0 ? Math.PI : 0));
              this.statics.push({ x: mp.x, y: mp.y, h: 0, hl: 0.15, hw: 0.15, type: 'mailbox' });
            }
          }
          if (this.r() < 0.35) {
            const tp = routePos(route, s + this.rr(4, 7), sgn * (wallT - 0.5));
            if (this.clearOfRoad(tp.x, tp.y, wallT - 0.5, 2)) {
              this.place(PropFactory.trashBin(), tp.x, tp.y, this.rr(0, TAU));
              this.statics.push({ x: tp.x, y: tp.y, h: 0, hl: 0.26, hw: 0.26, type: 'bin' });
            }
          }
        }
      }
      // ---- power poles + sagging wires along one verge ----
      const wirePts = [];
      let prevTop = null;
      for (let s = 20; s < route.length - 20; s += 38) {
        if (this.nearInter(s, 14)) { prevTop = null; continue; }
        const p = routePos(route, s, -(wallT + 0.4));
        if (!this.clearOfRoad(p.x, p.y, wallT + 0.4, 2)) { prevTop = null; continue; }
        this.place(PropFactory.powerPole(), p.x, p.y, p.h);
        const top = [p.x, 6.95, p.y];
        if (prevTop) {
          // two catenary wires, sampled as line segments
          for (const off of [-0.9, 0.9]) {
            const a = { x: prevTop[0], y: prevTop[1], z: prevTop[2] };
            const b = { x: top[0], y: top[1], z: top[2] };
            const hh = p.h, ox = -Math.sin(hh) * 0, oz = 0;
            const SEG = 7;
            for (let k = 0; k < SEG; k++) {
              const t0 = k / SEG, t1 = (k + 1) / SEG;
              const sag0 = Math.sin(t0 * Math.PI) * 0.9, sag1 = Math.sin(t1 * Math.PI) * 0.9;
              wirePts.push(
                lerp(a.x, b.x, t0), lerp(a.y, b.y, t0) - sag0, lerp(a.z, b.z, t0),
                lerp(a.x, b.x, t1), lerp(a.y, b.y, t1) - sag1, lerp(a.z, b.z, t1)
              );
            }
          }
        }
        prevTop = top;
      }
      if (wirePts.length) {
        const wg = new THREE.BufferGeometry();
        wg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(wirePts), 3));
        const wires = new THREE.LineSegments(wg, new THREE.LineBasicMaterial({ color: 0x14161c, transparent: true, opacity: 0.85 }));
        this.group.add(wires);
      }
    } else {
      // city blocks: tall boxes with facade textures
      const variants = [];
      for (let i = 0; i < 5; i++) {
        const floors = D.night ? randi(5, 9) : (this.dist.tag === 'D2' ? randi(4, 9) : randi(3, 6));
        const fh = 2.6;
        const wall = pick(D.bWall);
        const tex = Assets.facadeTexture(wall, D.bWin, floors, randi(3, 5), D.night ? 0.55 : 0.05);
        const side = Assets.sideWallTexture(wall, D.night);
        variants.push({ h: floors * fh, tex, side, wall });
      }
      for (let s = 14; s < route.length - 30; s += 19) {
        if (this.nearInter(s, 22)) continue;
        for (const sgn of [1, -1]) {
          if (this.r() < 0.12) continue;
          const v = variants[Math.floor(this.r() * variants.length)];
          const wide = this.rr(12, 17), depth = this.rr(10, 14);
          const bT = wallT + depth / 2 + 0.8;
          const p = routePos(route, s, sgn * bT);
          if (!this.clearOfRoad(p.x, p.y, bT)) continue;
          const sideMat = new THREE.MeshLambertMaterial({
            map: v.side,
            emissiveMap: D.night ? v.side : null,
            emissive: D.night ? 0xffffff : 0x000000, emissiveIntensity: D.night ? 0.9 : 0,
          });
          const roofMat = new THREE.MeshLambertMaterial({ color: new THREE.Color(v.wall).multiplyScalar(0.62) });
          const faceMat = new THREE.MeshLambertMaterial({ map: v.tex, emissiveMap: D.night ? v.tex : null, emissive: D.night ? 0xffffff : 0x000000, emissiveIntensity: D.night ? 1.25 : 0 });
          const mats = [sideMat, sideMat, roofMat, roofMat, faceMat, faceMat];
          const b = new THREE.Mesh(new THREE.BoxGeometry(wide, v.h, depth), mats);
          b.position.y = v.h / 2;
          b.castShadow = true;
          b.receiveShadow = true;
          const g = new THREE.Group();
          g.add(b);
          // parapet lip + rooftop equipment silhouette
          const para = CarFactory.boxMesh(wide + 0.35, 0.45, depth + 0.35, new THREE.Color(v.wall).multiplyScalar(0.8).getHex());
          para.position.y = v.h + 0.12;
          g.add(para);
          const nAC = randi(1, 3);
          for (let a = 0; a < nAC; a++) {
            const ac = CarFactory.boxMesh(this.rr(1.0, 1.7), this.rr(0.6, 1.0), this.rr(0.9, 1.3), 0x9aa0a8);
            ac.position.set(this.rr(-wide * 0.3, wide * 0.3), v.h + 0.6, this.rr(-depth * 0.3, depth * 0.3));
            ac.rotation.y = this.rr(0, TAU);
            g.add(ac);
          }
          if (this.r() < 0.28) { // rooftop water tank
            const tank = new THREE.Group();
            const body = new THREE.Mesh(
              Assets.geo('wtank', () => new THREE.CylinderGeometry(1.05, 1.15, 2.0, 10)),
              Assets.lambert('tankwood', { color: 0x6b4a34 })
            );
            body.position.y = 1.0;
            tank.add(body);
            const cap = new THREE.Mesh(
              Assets.geo('wtankcap', () => new THREE.ConeGeometry(1.2, 0.7, 10)),
              Assets.lambert('tankcap', { color: 0x4e463e })
            );
            cap.position.y = 2.3;
            tank.add(cap);
            tank.position.set(this.rr(-wide * 0.28, wide * 0.28), v.h, this.rr(-depth * 0.25, depth * 0.25));
            g.add(tank);
          }
          if (D.neon && this.r() < 0.5) {
            const sign = PropFactory.neonSign(pick(['NOODLES', '24H', '★ BAR ★', 'HOTEL', 'RAMEN', 'GAME ON', 'PIZZA']), pick(['#ff4dd2', '#4de6ff', '#ffe14d', '#8ef7d2']));
            sign.position.set(0, this.rr(4, Math.max(5, v.h - 4)), sgn > 0 ? -depth / 2 - 0.1 : depth / 2 + 0.1);
            if (sgn > 0) sign.rotation.y = Math.PI;
            g.add(sign);
          }
          // striped storefront awning over the street-facing ground floor
          if (this.r() < 0.72) {
            const aw = PropFactory.awning(this.rr(3.0, 4.4), pick(['#c94f3d', '#2e6fb0', '#3f8f52', '#7a4a8f', '#d99a2b', '#2b8f8a']));
            const zf = sgn > 0 ? -depth / 2 - 0.05 : depth / 2 + 0.05;
            aw.position.set(this.rr(-wide * 0.22, wide * 0.22), this.rr(2.8, 3.2), zf);
            if (sgn > 0) aw.rotation.y = Math.PI;
            g.add(aw);
          }
          this.place(g, p.x, p.y, p.h);
          this.statics.push({ x: p.x, y: p.y, h: p.h, hl: wide / 2, hw: depth / 2, type: 'building' });
        }
      }
    }
    // ---- manhole covers ----
    for (let s = 26; s < route.length - 30; s += 74) {
      if (this.nearInter(s, 10)) continue;
      const p = routePos(route, s + this.rr(-6, 6), this.rr(-2.2, 2.2));
      const mh = PropFactory.manhole();
      mh.position.set(p.x, 0.028, p.y);
      this.group.add(mh);
    }
    // ---- trees ----
    if (D.treeEvery) {
      for (let s = 8; s < route.length - 20; s += D.treeEvery) {
        if (this.nearInter(s, 16)) continue;
        for (const sgn of [1, -1]) {
          if (this.r() < 0.35) continue;
          const p = routePos(route, s + this.rr(-3, 3), sgn * (wallT - 0.9));
          if (!this.clearOfRoad(p.x, p.y, wallT - 0.9, 2)) continue;
          const tint = this.dist.weird ? pick([0xb85ac0, 0xe08a3a, 0x5abfc0]) : pick([0x4a9c4a, 0x5aa855, 0x3f8f46, 0x6aae4f, 0x578f3e, 0x72a844]);
          const tree = D.snow ? PropFactory.pine(this.rr(0.8, 1.5), true) : PropFactory.tree(this.rr(0.8, 1.3), tint);
          this.place(tree, p.x, p.y, this.rr(0, TAU));
          this.statics.push({ x: p.x, y: p.y, h: 0, hl: 0.25, hw: 0.25, type: 'tree' });
        }
      }
    }
    // ---- lampposts ----
    if (D.lampEvery) {
      for (let s = 14; s < route.length - 20; s += D.lampEvery) {
        if (this.nearInter(s, 12)) continue;
        const sgn = (Math.floor(s / D.lampEvery) % 2 === 0) ? 1 : -1;
        const p = routePos(route, s, sgn * (RW + 1.0));
        if (!this.clearOfRoad(p.x, p.y, RW + 1.0, 2)) continue;
        const lamp = PropFactory.lamppost(D.night);
        this.place(lamp, p.x, p.y, p.h + (sgn > 0 ? Math.PI : 0));
        this.statics.push({ x: p.x, y: p.y, h: 0, hl: 0.18, hw: 0.18, type: 'lamp' });
      }
    }
    // ---- hydrants (city) ----
    if (!D.houses) {
      for (let s = 30; s < this.route.length - 40; s += 60) {
        if (this.nearInter(s, 12)) continue;
        const p = routePos(this.route, s, this.RW + 1.1);
        if (!this.clearOfRoad(p.x, p.y, this.RW + 1.1, 2)) continue;
        this.place(PropFactory.hydrant(), p.x, p.y, 0);
        this.statics.push({ x: p.x, y: p.y, h: 0, hl: 0.2, hw: 0.2, type: 'hydrant' });
      }
    }
    // ---- benches (suburbs/parks) ----
    if (D.houses) {
      for (let s = 45; s < this.route.length - 40; s += 90) {
        if (this.nearInter(s, 12)) continue;
        const p = routePos(this.route, s, -(this.RW + 1.6));
        if (!this.clearOfRoad(p.x, p.y, this.RW + 1.6, 2)) continue;
        this.place(PropFactory.bench(), p.x, p.y, p.h + Math.PI);
      }
    }
    // ---- grass tufts scattered on the verges (single instanced draw) ----
    {
      const spots = [];
      for (let s = 6; s < route.length - 10; s += 4.5) {
        if (this.nearInter(s, 10)) continue;
        for (const sgn of [1, -1]) {
          if (this.r() < 0.5) continue;
          const p = routePos(route, s + this.rr(-1.6, 1.6), sgn * (wallT + this.rr(0.35, 2.2)));
          spots.push({ x: p.x, y: p.y, sc: this.rr(0.55, 1.15), rot: this.rr(0, Math.PI) });
        }
      }
      if (spots.length) {
        const geo = new THREE.PlaneGeometry(0.75, 0.42);
        geo.translate(0, 0.18, 0);
        const tuftC = new THREE.Color(this.dist.weird ? 0xb08ab8 : D.ground[0]).multiplyScalar(0.92);
        const mat = new THREE.MeshLambertMaterial({
          map: Assets.grassTuftTex(), color: tuftC, alphaTest: 0.35, side: THREE.DoubleSide,
        });
        const im = new THREE.InstancedMesh(geo, mat, spots.length * 2);
        const m4 = new THREE.Matrix4(), q = new THREE.Quaternion();
        const up = new THREE.Vector3(0, 1, 0), sv = new THREE.Vector3(), pv = new THREE.Vector3();
        let k = 0;
        for (const sp of spots) {
          for (const ang of [sp.rot, sp.rot + Math.PI / 2]) {
            q.setFromAxisAngle(up, ang);
            sv.setScalar(sp.sc);
            pv.set(sp.x, 0.02, sp.y);
            m4.compose(pv, q, sv);
            im.setMatrixAt(k++, m4);
          }
        }
        im.instanceMatrix.needsUpdate = true;
        this.group.add(im);
      }
    }
    // ---- v5 STREET LIFE: sidewalk furniture, vendors, plazas, landmarks ----
    this.flags = this.flags || [];
    {
      const city = !D.houses;
      const okSide = (sgn) => !(D.marina && sgn === -1); // marina -t is open water
      // bus shelters on alternating sides
      for (let s = 66; s < route.length - 60; s += 156) {
        if (this.nearInter(s, 22)) continue;
        const sgn = (Math.floor(s / 156) % 2) ? 1 : -1;
        if (!okSide(sgn)) continue;
        const tt = RW + 0.35 + SIDEWALK_W * 0.5;
        const p = routePos(route, s, sgn * tt);
        if (!this.clearOfRoad(p.x, p.y, tt, 1)) continue;
        this.place(PropFactory.busShelter(), p.x, p.y, p.h + (sgn > 0 ? Math.PI : 0));
        this.statics.push({ x: p.x, y: p.y, h: p.h, hl: 1.5, hw: 0.7, type: 'shelter' });
      }
      // mixed sidewalk dressing: carts, kiosks, café sets, planters, pots
      for (let s = 40; s < route.length - 40; s += 33) {
        if (this.nearInter(s, 15)) continue;
        for (const sgn of [1, -1]) {
          if (!okSide(sgn)) continue;
          if (this.r() < 0.5) continue;
          const tt = RW + 0.35 + SIDEWALK_W * (0.38 + this.r() * 0.42);
          const p = routePos(route, s + this.rr(-3, 3), sgn * tt);
          if (!this.clearOfRoad(p.x, p.y, tt, 1)) continue;
          const face = p.h + (sgn > 0 ? Math.PI : 0);
          const roll = this.r();
          if (city && roll < 0.26) {
            this.place(PropFactory.foodCart(), p.x, p.y, face + this.rr(-0.25, 0.25));
            this.statics.push({ x: p.x, y: p.y, h: p.h, hl: 0.9, hw: 0.6, type: 'cart' });
          } else if (city && roll < 0.4) {
            this.place(PropFactory.kiosk(), p.x, p.y, face);
            this.statics.push({ x: p.x, y: p.y, h: p.h, hl: 1.0, hw: 0.75, type: 'kiosk' });
          } else if (roll < 0.68) {
            this.place(PropFactory.cafeSet(), p.x, p.y, this.rr(0, TAU));
          } else if (roll < 0.86) {
            const pl = PropFactory.planter(this.r() < 0.5);
            this.place(pl, p.x, p.y, p.h);
            this.statics.push({ x: p.x, y: p.y, h: p.h, hl: 1.1, hw: 0.3, type: 'planter' });
          } else {
            this.place(PropFactory.potPlant(), p.x, p.y, 0);
          }
        }
      }
      // bike racks (city)
      if (city) {
        for (let s = 88; s < route.length - 60; s += 124) {
          if (this.nearInter(s, 18)) continue;
          const sgn = this.r() < 0.5 ? 1 : -1;
          if (!okSide(sgn)) continue;
          const tt = RW + 0.35 + SIDEWALK_W * 0.5;
          const p = routePos(route, s, sgn * tt);
          if (!this.clearOfRoad(p.x, p.y, tt, 1)) continue;
          this.place(PropFactory.bikeRack(), p.x, p.y, p.h);
        }
      }
      // bollards guarding sidewalk edges just shy of each intersection
      for (const inter of route.inters) {
        for (const sgn of [1, -1]) {
          for (let k = 0; k < 3; k++) {
            const s = inter.s0 - 3 - k * 1.5;
            if (s < 8) continue;
            const p = routePos(route, s, sgn * (RW + 0.55));
            this.place(PropFactory.bollard(), p.x, p.y, 0);
          }
        }
      }
      // flag poles as plaza / park landmarks (cloth waved in updateAmbient)
      for (let s = 110; s < route.length - 80; s += 250) {
        if (this.nearInter(s, 22)) continue;
        const sgn = this.r() < 0.5 ? 1 : -1;
        if (!okSide(sgn)) continue;
        const tt = RW + 0.35 + SIDEWALK_W * 0.65;
        const p = routePos(route, s, sgn * tt);
        if (!this.clearOfRoad(p.x, p.y, tt, 1)) continue;
        const fp = PropFactory.flagPole(pick([0xd23b34, 0x2e6fb0, 0x3f9f52, 0xf4c430, 0xe85ad0]));
        this.place(fp.group, p.x, p.y, p.h + this.rr(0, TAU));
        this.flags.push({ mesh: fp.flag, ph: this.rr(0, TAU) });
        this.statics.push({ x: p.x, y: p.y, h: 0, hl: 0.15, hw: 0.15, type: 'flagpole' });
      }
      // a single ornate street clock landmark mid-route (city plazas)
      if (city) {
        const s = Math.min(route.length - 40, route.length * 0.55);
        if (!this.nearInter(s, 18)) {
          const tt = RW + 0.35 + SIDEWALK_W * 0.5;
          const p = routePos(route, s, tt);
          if (this.clearOfRoad(p.x, p.y, tt, 1)) {
            this.place(PropFactory.streetClock(), p.x, p.y, p.h);
            this.statics.push({ x: p.x, y: p.y, h: 0, hl: 0.2, hw: 0.2, type: 'clock' });
          }
        }
      }
    }
    // ---- corridor guard walls (invisible) where no buildings gap ----
    // outer fence: keep player within ~outer boundary
    for (let s = 0; s < this.route.length; s += 24) {
      for (const sgn of [1, -1]) {
        if (this.nearInter(s + 12, 26)) continue; // gaps at intersections (stubs have own barriers)
        const p = routePos(this.route, s + 12, sgn * (wallT + 9));
        this.statics.push({ x: p.x, y: p.y, h: p.h, hl: 13, hw: 0.6, type: 'boundary' });
      }
    }
  }

  placeParkedCars() {
    const route = this.route, RW = this.RW, L = this.level;
    const parkT = RW - 1.15;
    const destS = route.length - 24;
    const kinds = ['sedan', 'hatch', 'suv', 'taxi'];
    const density = L.district === 1 ? 0.55 : 0.35;
    for (let s = 30; s < route.length - 70; s += 14) {
      if (this.nearInter(s, 16)) continue;
      if (Math.abs(s - destS) < 40) continue;
      if (this.r() > density) continue;
      const kind = this.r() < 0.08 ? 'police' : pick(kinds);
      const refs = CarFactory.traffic(kind);
      const p = routePos(route, s, parkT);
      this.place(refs.group, p.x, p.y, p.h);
      this.parked.push({ x: p.x, y: p.y, h: p.h, hl: refs.len / 2, hw: refs.wid / 2, refs, kind });
    }
  }

  buildDestination() {
    const route = this.route, RW = this.RW, L = this.level;
    const veh = VEH_DEFS[this.vehKey];
    const sSpot = route.length - 24;
    this.parkZoneS = sSpot - 42;
    let spot;
    if (L.park === 'parallel') {
      const gap = veh.len + L.margin;
      const t = RW - Math.max(1.15, veh.wid / 2 + 0.15);
      const p = routePos(route, sSpot, t);
      spot = { type: 'parallel', x: p.x, y: p.y, h: p.h, hl: gap / 2, hw: Math.max(1.3, veh.wid / 2 + 0.35), t, curbT: RW, s: sSpot };
      // bracket cars
      const carA = L.id === 10 ? 'police' : pick(['sedan', 'suv']);
      const carB = pick(['sedan', 'hatch']);
      for (const [ds, kind] of [[-(gap / 2 + 2.6), carA], [gap / 2 + 2.6, carB]]) {
        const refs = CarFactory.traffic(kind, L.id === 10 ? '#d92b2b' : undefined);
        const pp = routePos(route, sSpot + ds, t);
        this.place(refs.group, pp.x, pp.y, pp.h);
        this.parked.push({ x: pp.x, y: pp.y, h: pp.h, hl: refs.len / 2, hw: refs.wid / 2, refs, kind, precious: L.id === 10 });
      }
    } else {
      // bay parking: paved apron on the right with perpendicular spots
      const apronW = veh.len + 4;
      const aprLen = 34;
      const walkEdge = RW + 0.35 + SIDEWALK_W;
      this.ribbon(sSpot - aprLen / 2, sSpot + aprLen / 2, RW, walkEdge + apronW, 0.021,
        new THREE.MeshLambertMaterial({ map: Assets.plainAsphalt(this.dist.night) }), 8);
      const spotT = RW + 2.2 + veh.len / 2;
      const p = routePos(route, sSpot, spotT);
      const bayH = p.h + Math.PI / 2; // nose-in pointing away from road (+t direction)
      spot = { type: 'bay', x: p.x, y: p.y, h: bayH, hl: veh.len / 2 + 0.6, hw: (veh.wid + L.margin) / 2 + 0.25, t: spotT, s: sSpot };
      // bay lines + neighbor cars
      for (const ds of [-1, 1]) {
        const w = spot.hw * 2;
        const np = routePos(route, sSpot + ds * (w + 1.6), spotT);
        const refs = CarFactory.traffic(pick(['sedan', 'suv', 'hatch']));
        this.place(refs.group, np.x, np.y, bayH + (this.r() < 0.5 ? 0 : Math.PI));
        this.parked.push({ x: np.x, y: np.y, h: bayH, hl: refs.len / 2, hw: refs.wid / 2, refs });
      }
    }
    this.spot = spot;
    // painted spot marking
    const mark = new THREE.Mesh(
      new THREE.PlaneGeometry(spot.hw * 2, spot.hl * 2),
      new THREE.MeshBasicMaterial({ map: Assets.spotTexture(), transparent: true, depthWrite: false })
    );
    mark.rotation.x = -Math.PI / 2;
    mark.rotation.z = -spot.h + Math.PI / 2;
    mark.position.set(spot.x, 0.04, spot.y);
    mark.renderOrder = 4;
    this.group.add(mark);
    this.spotMark = mark;
    // ghost box (translucent target pose)
    const veh2 = VEH_DEFS[this.vehKey];
    const ghost = new THREE.Mesh(
      new THREE.BoxGeometry(veh2.len, veh2.hgt, veh2.wid),
      new THREE.MeshBasicMaterial({ color: 0x4de6a0, transparent: true, opacity: 0.16, depthWrite: false })
    );
    ghost.position.set(spot.x, veh2.hgt / 2 + 0.02, spot.y);
    ghost.rotation.y = -spot.h;
    this.group.add(ghost);
    this.ghost = ghost;
    // beacon column + flag
    const beam = new THREE.Mesh(
      Assets.geo('beacon', () => new THREE.CylinderGeometry(1.4, 1.8, 26, 12, 1, true)),
      new THREE.MeshBasicMaterial({ color: 0xffc23e, transparent: true, opacity: 0.14, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending })
    );
    beam.position.set(spot.x, 13, spot.y);
    this.group.add(beam);
    this.beacon = beam;
    const flag = new THREE.Sprite(new THREE.SpriteMaterial({ map: Assets.emojiTexture('🏁'), transparent: true, depthWrite: false }));
    flag.scale.set(2.4, 2.4, 1);
    flag.position.set(spot.x, 7, spot.y);
    this.group.add(flag);
    this.flag = flag;
  }

  // ambient animation: cloud drift, firefly wander
  updateAmbient(dt) {
    this._ambT = (this._ambT || 0) + dt;
    const t = this._ambT;
    if (this.coinList) {
      for (const c of this.coinList) {
        if (c.taken) continue;
        c.g.rotation.y += dt * 2.6;
        c.g.position.y = 1.0 + Math.sin(t * 2.2 + c.ph) * 0.09;
      }
    }
    if (this.clouds) {
      for (const c of this.clouds) {
        c.g.position.x += c.vx * dt;
        if (c.g.position.x > c.cx + 560) c.g.position.x = c.cx - 560;
        if (c.g.position.x < c.cx - 560) c.g.position.x = c.cx + 560;
      }
    }
    if (this.flags) {
      for (const fl of this.flags) {
        const pos = fl.mesh.geometry.attributes.position;
        for (let i = 0; i < pos.count; i++) {
          const x = pos.getX(i); // 0 at pole → 2.4 at fly end
          const k = x / 2.4;
          pos.setZ(i, Math.sin(x * 2.4 - t * 7 + fl.ph) * 0.22 * k);
        }
        pos.needsUpdate = true;
        fl.mesh.geometry.computeVertexNormals();
      }
    }
    if (this.fireflies) {
      const p = this.fireflies.geometry.attributes.position;
      for (let i = 0; i < this.ffSeed.length; i++) {
        const f = this.ffSeed[i];
        p.setXYZ(i,
          f.x + Math.sin(t * f.sp + f.ph) * 1.4,
          f.y + Math.sin(t * f.sp * 1.7 + f.ph * 2) * 0.5,
          f.z + Math.cos(t * f.sp * 0.8 + f.ph) * 1.4);
      }
      p.needsUpdate = true;
      this.fireflies.material.opacity = 0.55 + Math.sin(t * 2.4) * 0.35;
    }
    // marina: rolling water, bobbing hulls, sweeping lighthouse
    if (this.waterTexs) {
      for (const wt of this.waterTexs) {
        wt.offset.y -= dt * 0.05;
        wt.offset.x = Math.sin(t * 0.35) * 0.03;
      }
    }
    if (this.boats) {
      for (const b of this.boats) {
        b.g.position.y = b.base + Math.sin(t * 0.85 + b.ph) * 0.12;
        b.g.rotation.z = Math.sin(t * 0.65 + b.ph) * 0.045;
        b.g.rotation.x = Math.sin(t * 0.5 + b.ph * 2) * 0.03;
      }
    }
    if (this.lhBeam) this.lhBeam.rotation.y = t * 1.1;
    // gliding birds: lazy circles, wing-flap via vertical squash
    if (this.gulls) {
      for (const gl of this.gulls) {
        const a = t * gl.sp * TAU * 0.16 + gl.ph;
        gl.spr.position.set(
          gl.cx + Math.cos(a) * gl.r,
          gl.alt + Math.sin(t * 0.7 + gl.ph) * 1.6,
          gl.cz + Math.sin(a) * gl.r);
        const flap = 0.6 + Math.abs(Math.sin(t * 6 + gl.ph)) * 0.4;
        gl.spr.scale.y = gl.spr.scale.x * 0.6 * flap;
      }
    }
    // frostpeak: aurora shimmer + chimney smoke
    if (this.auroras) {
      for (const a of this.auroras) {
        a.mesh.material.map.offset.x += a.sp * dt * 8;
        a.mesh.material.opacity = 0.42 + Math.sin(t * 0.45 + a.ph) * 0.2;
        a.mesh.rotation.y += a.sp * dt * 0.5;
      }
    }
    if (this.chimSmoke) {
      for (const s of this.chimSmoke) {
        s.t += dt * s.sp;
        if (s.t > 1) s.t -= 1;
        const k = s.t;
        s.spr.position.set(s.x + Math.sin(k * 6 + s.ph) * (0.3 + k * 0.9), s.y + k * 5.2, s.z);
        s.spr.material.opacity = 0.5 * Math.sin(Math.min(1, k * 4) * Math.PI / 2) * (1 - k);
        const sc = 0.9 + k * 2.6;
        s.spr.scale.set(sc, sc, 1);
      }
    }
    // night skies: the occasional shooting star
    if (this.dist.stars) {
      if (!this.shootSpr) {
        this.shootSpr = new THREE.Sprite(new THREE.SpriteMaterial({
          map: Assets.radialSprite('rgba(235,242,255,.95)'), transparent: true, opacity: 0,
          depthWrite: false, fog: false, blending: THREE.AdditiveBlending,
        }));
        this.shootSpr.scale.set(11, 1.1, 1);
        this.group.add(this.shootSpr);
        this._shootT = this.rr(3, 7);
        this._shootLife = 0;
      }
      if (this._shootLife > 0) {
        this._shootLife -= dt;
        this.shootSpr.position.x += this._shootVx * dt;
        this.shootSpr.position.y += this._shootVy * dt;
        this.shootSpr.material.opacity = Math.max(0, Math.min(1, this._shootLife * 2.2)) * 0.9;
        if (this._shootLife <= 0) this._shootT = this.rr(5, 12);
      } else {
        this._shootT -= dt;
        if (this._shootT <= 0) {
          const mid = this.route.sampleAt(this.route.length / 2);
          const a = this.rr(0, TAU);
          this.shootSpr.position.set(mid.x + Math.cos(a) * this.rr(200, 500), this.rr(280, 460), mid.y + Math.sin(a) * this.rr(200, 500));
          this._shootVx = this.rr(-1, 1) < 0 ? -this.rr(120, 200) : this.rr(120, 200);
          this._shootVy = -this.rr(40, 80);
          this.shootSpr.material.rotation = Math.atan2(-this._shootVy, this._shootVx);
          this._shootLife = 0.8;
        }
      }
    }
  }

  // Distance-cull static scenery. The camera's far plane has to stay huge for
  // the sky dome and horizon ring, so without this every fogged-out building
  // and prop down the route is still submitted as a draw call. Fog is fully
  // opaque at fogFar, so hiding past that boundary is visually lossless —
  // props are already solid fog colour when they wink out.
  updateLOD(cx, cz) {
    const props = this.lodProps;
    if (!props.length) return;
    const r = this.fogFar * 1.04;
    const r2 = r * r;
    for (let i = 0; i < props.length; i++) {
      const p = props[i];
      const dx = p.x - cx, dz = p.z - cz;
      const vis = dx * dx + dz * dz < r2;
      if (p.o.visible !== vis) p.o.visible = vis;
    }
  }

  // traffic light phases
  updateLights(dt) {
    for (const ctrl of this.lights) {
      ctrl.timer += dt;
      const cycle = 15.6; // 7 green, 1.6 amber, 7 red
      const t = ctrl.timer % cycle;
      const st = t < 7 ? 0 : (t < 8.6 ? 1 : 2);
      if (st !== ctrl.state) {
        ctrl.state = st;
        for (const m of ctrl.meshes) m.setState(st);
      }
    }
  }

  dispose() {
    this.scene.remove(this.group);
    this.group.traverse(o => {
      if (o.isMesh || o.isSprite || o.isPoints) {
        if (o.geometry && !Object.values(Assets.geos).includes(o.geometry)) o.geometry.dispose();
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        for (const m of mats) {
          if (m && !Object.values(Assets.mats).includes(m)) m.dispose();
        }
      }
    });
    this.scene.fog = null;
  }
}

// ============================================================
// TRAFFIC — ambient cars + cross traffic
// ============================================================
class Traffic {
  constructor(scene, world, level) {
    this.scene = scene;
    this.world = world;
    this.level = level;
    this.cars = [];
    this.crossers = [];
    this.density = level.traffic || 0.8; // cars per 100m over active window
    this.window = 170;
    this.pool = [];
  }
  targetCount() {
    return Math.min(19, Math.round(this.density * (this.window * 2) / 100 * 2.35)); // both directions
  }
  makeCar() {
    let refs;
    if (this.pool.length) refs = this.pool.pop();
    else {
      const kind = chance(0.12) ? 'taxi' : (chance(0.08) ? 'suv' : pick(['sedan', 'sedan', 'hatch', 'suv']));
      refs = CarFactory.traffic(kind);
      refs.kind = kind;
      this.scene.add(refs.group);
    }
    refs.group.visible = true;
    return refs;
  }
  spawn(playerS, ahead) {
    const RW = this.world.RW, L = this.level;
    // oncoming cars cross the visible window at ~2x relative speed, so they
    // need a spawn bias just to look as numerous as same-direction traffic
    const dir = chance(0.42) ? 1 : -1;
    const lane = (L.lanes === 2 && chance(0.45)) ? 1 : 0;
    // oncoming spawns deep in the window: a steady stream flowing past
    let s = playerS + (ahead
      ? (dir === -1 ? rand(this.window * 0.55, this.window) : rand(50, this.window))
      : -rand(45, this.window));
    if (s < 12 || s > this.world.route.length - 55) return null;
    // avoid spawning near destination parking zone
    if (s > this.world.parkZoneS - 20) return null;
    // spacing check
    for (const c of this.cars) {
      if (c.dir === dir && c.lane === lane && Math.abs(c.s - s) < 18) return null;
    }
    const refs = this.makeCar();
    const cruise = rand(6.5, 10.5) * (this.world.dist.night ? 0.9 : 1);
    const car = {
      refs, s, t: dir * (LANE_W * 0.5 + lane * LANE_W), dir, lane,
      v: cruise * 0.8, cruise, len: refs.len, wid: refs.wid,
      x: 0, y: 0, h: 0, px: 0, py: 0, ph: 0,
      stopT: 0, honkCd: rand(1, 3), blockT: 0, hitT: 0, panicT: 0,
    };
    this.posFrom(car);
    car.px = car.x; car.py = car.y; car.ph = car.h;
    this.cars.push(car);
    return car;
  }
  posFrom(car) {
    const p = this.world.route.sampleAt(car.s);
    const rx = -Math.sin(p.h), ry = Math.cos(p.h);
    car.x = p.x + rx * car.t;
    car.y = p.y + ry * car.t;
    car.h = car.dir === 1 ? p.h : p.h + Math.PI;
  }
  obb(car) { return { x: car.x, y: car.y, h: car.h, hl: car.len / 2, hw: car.wid / 2 }; }

  update(dt, game) {
    const world = this.world, route = world.route;
    const player = game.player;
    const pProj = game.playerProj;
    // maintain population
    const want = this.targetCount();
    if (this.cars.length < want && chance(0.18)) this.spawn(pProj.s, chance(0.7));
    // update each
    for (let i = this.cars.length - 1; i >= 0; i--) {
      const car = this.cars[i];
      car.px = car.x; car.py = car.y; car.ph = car.h;
      // recycle if far away or past route
      const rel = (car.s - pProj.s) * 1;
      if (Math.abs(rel) > this.window + 40 || car.s < 8 || car.s > route.length - 45 || (car.s > world.parkZoneS - 8 && car.dir === 1)) {
        car.refs.group.visible = false;
        this.pool.push(car.refs);
        this.cars.splice(i, 1);
        continue;
      }
      if (car.hitT > 0) { // pulled over after collision
        car.hitT -= dt;
        car.v = Math.max(0, car.v - 8 * dt);
        car.honkCd -= dt;
        if (car.honkCd <= 0 && Math.abs(rel) < 40) { SFX.horn('angry'); car.honkCd = rand(1.5, 3); }
        this.integrate(car, dt);
        continue;
      }
      if (car.panicT > 0) { // fleeing the tank/ufo
        car.panicT -= dt;
        car.v = Math.min(13, car.v + 8 * dt);
        this.integrate(car, dt);
        continue;
      }
      // target speed logic
      let target = car.cruise;
      // leader gap
      let gap = 999;
      for (const o of this.cars) {
        if (o === car || o.dir !== car.dir || o.lane !== car.lane) continue;
        const ds = (o.s - car.s) * car.dir;
        if (ds > 0 && ds < gap) gap = ds - o.len / 2 - car.len / 2;
      }
      // player as obstacle (if roughly in this lane & same general area)
      const laneT = car.t;
      if (Math.abs(pProj.t - laneT) < 1.9) {
        const ds = (pProj.s - car.s) * car.dir;
        if (ds > 0 && ds - 2 < gap) {
          gap = ds - VEH_DEFS[game.vehKey].len / 2 - car.len / 2;
          // honk if blocked by a slow/stopped player
          if (gap < 9 && game.playerSpeedAbs < 1.5 && car.v < 1) {
            car.blockT += dt;
            car.honkCd -= dt;
            if (car.blockT > 2 && car.honkCd <= 0) {
              car.honkCd = rand(2, 4.5);
              SFX.horn('angry');
              game.trafficHonk(car);
            }
          } else car.blockT = 0;
        }
      }
      // red light for own direction (lights only control the main road here)
      for (const inter of route.inters) {
        if (!inter.ctrl) continue;
        const stopS = car.dir === 1 ? inter.s0 - 3 : inter.s1 + 3;
        const ds = (stopS - car.s) * car.dir;
        if (ds > 0 && ds < 26 && inter.ctrl.state === 2) {
          gap = Math.min(gap, ds - car.len / 2);
        }
      }
      // crossing chaos peds
      if (game.peds) {
        for (const ped of game.peds.list) {
          if (!ped.onRoad) continue;
          const dd = dist2(car.x, car.y, ped.x, ped.y);
          if (dd < 15 * 15) {
            const ds = ((ped.proj ? ped.proj.s : car.s) - car.s) * car.dir;
            if (ds > 0 && ds < 14) gap = Math.min(gap, ds - 1.5);
          }
        }
      }
      // speed from gap
      if (gap < 3) target = 0;
      else if (gap < 8) target = Math.min(target, (gap - 3) * 0.9);
      else if (gap < 16) target = Math.min(target, car.cruise * 0.6 + (gap - 8));
      // approach target
      if (car.v < target) car.v = Math.min(target, car.v + 3.2 * dt);
      else car.v = Math.max(target, car.v - 7.5 * dt);
      this.integrate(car, dt);
    }
    // brake lights + wheels
    for (const car of this.cars) {
      const braking = car.v < car.cruise * 0.4;
      for (const bl of car.refs.brakeLights) bl.material.emissiveIntensity = braking ? 2.4 : 0.3;
      for (const w of car.refs.wheels) w.children[0].rotation.z -= Math.min(car.v / ((w.userData && w.userData.r) || 0.34), 24) * dt;
    }
    this.updateCrossers(dt, game);
  }

  // cross traffic through light-controlled intersections (during player red)
  updateCrossers(dt, game) {
    const world = this.world;
    for (const inter of world.route.inters) {
      if (!inter.ctrl) continue;
      inter.crossers = inter.crossers || [];
      const crossGreen = inter.ctrl.state === 2;
      if (crossGreen && inter.crossers.length < 2 && chance(0.012) &&
        Math.abs((inter.s0 + inter.s1) / 2 - game.playerProj.s) < 170) {
        const refs = this.makeCar();
        const side = chance(0.5) ? 1 : -1;
        inter.crossers.push({ refs, u: -46 * side, dir: side, v: rand(7, 10), off: side * 1.9, x: 0, y: 0, h: 0, len: refs.len, wid: refs.wid });
      }
      for (let i = inter.crossers.length - 1; i >= 0; i--) {
        const cr = inter.crossers[i];
        const pd = game.playerProj;
        const iMid = (inter.s0 + inter.s1) / 2;
        const playerInBox = Math.abs(pd.s - iMid) < (inter.s1 - inter.s0) / 2 + 2.5 && Math.abs(pd.t) < world.RW;
        const distToCenter = -cr.u * cr.dir;
        let brake = playerInBox && distToCenter > 2 && distToCenter < 18;
        if (!crossGreen && distToCenter > 8) brake = true;
        if (brake) cr.v = Math.max(0, cr.v - 9 * dt);
        else cr.v = Math.min(9.5, cr.v + 4 * dt);
        cr.u += cr.dir * cr.v * dt;
        if (Math.abs(cr.u) > 48) {
          cr.refs.group.visible = false;
          this.pool.push(cr.refs);
          inter.crossers.splice(i, 1);
          continue;
        }
        const crossH = inter.h + Math.PI / 2;
        cr.x = inter.cx + Math.cos(crossH) * cr.u + Math.cos(inter.h) * cr.off;
        cr.y = inter.cy + Math.sin(crossH) * cr.u + Math.sin(inter.h) * cr.off;
        cr.h = cr.dir === 1 ? crossH : crossH + Math.PI;
        cr.refs.group.position.set(cr.x, 0, cr.y);
        cr.refs.group.rotation.y = -cr.h;
        for (const w of cr.refs.wheels) w.children[0].rotation.z -= Math.min(cr.v / ((w.userData && w.userData.r) || 0.34), 24) * dt;
      }
    }
  }
  crossersNear(x, y, r) {
    const out = [];
    for (const inter of this.world.route.inters) {
      if (!inter.crossers) continue;
      for (const cr of inter.crossers) {
        if (dist2(cr.x, cr.y, x, y) < r * r) out.push(cr);
      }
    }
    return out;
  }
  integrate(car, dt) {
    car.s += car.dir * car.v * dt;
    this.posFrom(car);
  }
  panicNear(x, y, r) {
    for (const car of this.cars) {
      if (dist2(car.x, car.y, x, y) < r * r) car.panicT = Math.max(car.panicT, 2.5);
    }
  }
  // called by game when player hits a traffic car
  onHit(car) {
    car.hitT = 6;
    car.blockT = 0;
  }
  render(alpha) {
    for (const car of this.cars) {
      const g = car.refs.group;
      g.position.set(lerp(car.px, car.x, alpha), 0, lerp(car.py, car.y, alpha));
      g.rotation.y = -(car.ph + angNorm(car.h - car.ph) * alpha);
    }
  }
  clear() {
    for (const car of this.cars) { car.refs.group.visible = false; this.pool.push(car.refs); }
    this.cars = [];
    for (const inter of this.world.route.inters) {
      if (!inter.crossers) continue;
      for (const cr of inter.crossers) { cr.refs.group.visible = false; this.pool.push(cr.refs); }
      inter.crossers = [];
    }
  }
  disposeAll() {
    this.clear();
    for (const refs of this.pool) this.scene.remove(refs.group);
    this.pool = [];
  }
}

// ============================================================
// PEDESTRIANS
// ============================================================
class Peds {
  constructor(scene, world, count) {
    this.scene = scene;
    this.world = world;
    this.list = [];
    const route = world.route;
    for (let i = 0; i < count; i++) {
      const s = rand(25, route.length - 30);
      const side = chance(0.5) ? 1 : -1;
      const t = side * (world.RW + 0.35 + rand(0.8, SIDEWALK_W - 0.6));
      const p = routePos(route, s, t);
      const refs = PedFactory.build();
      scene.add(refs.group);
      this.list.push({
        refs, s, t, side, dir: chance(0.5) ? 1 : -1,
        x: p.x, y: p.y, px: p.x, py: p.y, face: p.h,
        state: 'walk', speed: rand(0.7, 1.3), phase: rand(0, TAU),
        stateT: 0, filmed: false, scandalized: false, soaked: false, proj: null,
        onRoad: false, attracted: false,
      });
    }
  }
  update(dt, game) {
    const world = this.world, route = world.route;
    const px = game.player.x, py = game.player.y;
    for (const ped of this.list) {
      ped.px = ped.x; ped.py = ped.y;
      ped.phase += dt * (ped.state === 'flee' || ped.state === 'dive' ? 14 : 6) * (ped.state === 'walk' || ped.state === 'cross' ? 1 : 0.4);
      ped.stateT -= dt;
      const dd = dist2(px, py, ped.x, ped.y);
      const near = dd < 30 * 30;

      // --- danger: player heading at them fast → dive (never hittable)
      if (dd < 5.5 * 5.5 && game.playerSpeedAbs > 6) {
        const vx = game.player.vx, vy = game.player.vy;
        const toP = { x: ped.x - px, y: ped.y - py };
        const dot = vx * toP.x + vy * toP.y;
        if (dot > 0 && ped.state !== 'dive') {
          this.dive(ped, game, px, py);
        }
      }
      // hard no-overlap guarantee
      if (dd < 1.6 * 1.6) this.dive(ped, game, px, py, true);

      // --- ice cream attraction
      if (game.jingleOn && near && ped.state === 'walk' && chance(0.006)) {
        ped.state = 'cross';
        ped.attracted = true;
        ped.refs.emote.material.map = Assets.emojiTexture('🍦');
        ped.refs.emote.visible = true;
      }

      switch (ped.state) {
        case 'walk': {
          ped.s += ped.dir * ped.speed * dt;
          if (ped.s < 15 || ped.s > route.length - 18) ped.dir *= -1;
          const p = routePos(route, ped.s, ped.t);
          ped.x = p.x; ped.y = p.y;
          ped.face = p.h + (ped.dir === 1 ? 0 : Math.PI);
          ped.onRoad = false;
          // notice shameful player
          if (near && game.recentShameT > 0 && chance(0.03)) {
            ped.state = 'film';
            ped.stateT = rand(2, 4);
            ped.refs.emote.material.map = Assets.emojiTexture(pick(['🎥', '📱', '😳', '🤳']));
            ped.refs.emote.visible = true;
            ped.refs.phone.visible = true;
            if (!ped.filmed) { ped.filmed = true; game.onFilmed(ped); }
          }
          break;
        }
        case 'film': {
          ped.face = Math.atan2(py - ped.y, px - ped.x);
          if (ped.stateT <= 0) {
            ped.state = 'walk';
            ped.refs.emote.visible = false;
            ped.refs.phone.visible = false;
          }
          break;
        }
        case 'cross': { // attracted by jingle — walks toward the truck!
          const ang = Math.atan2(py - ped.y, px - ped.x);
          ped.face = ang;
          ped.x += Math.cos(ang) * 1.9 * dt;
          ped.y += Math.sin(ang) * 1.9 * dt;
          ped.proj = route.project(ped.x, ped.y, ped.proj ? ped.proj.idx : undefined);
          ped.onRoad = Math.abs(ped.proj.t) < world.RW;
          if (dd < 4.5 * 4.5 || !game.jingleOn) {
            ped.state = 'walk';
            ped.attracted = false;
            ped.onRoad = false;
            ped.refs.emote.visible = false;
            // snap back to nearest sidewalk lane
            ped.t = ped.side * (world.RW + 0.35 + 1.4);
            ped.s = clamp(ped.proj.s, 16, route.length - 20);
          }
          break;
        }
        case 'dive': {
          ped.x += ped.dvx * dt; ped.y += ped.dvy * dt;
          ped.dvx *= (1 - 3 * dt); ped.dvy *= (1 - 3 * dt);
          if (ped.stateT <= 0) {
            ped.state = 'film';
            ped.stateT = rand(2.5, 4);
            ped.refs.emote.material.map = Assets.emojiTexture('😤');
            ped.refs.phone.visible = true;
            ped.proj = route.project(ped.x, ped.y, ped.proj ? ped.proj.idx : undefined);
            ped.s = ped.proj.s;
            ped.onRoad = false;
          }
          break;
        }
        case 'soaked': {
          ped.face = Math.atan2(py - ped.y, px - ped.x);
          if (ped.stateT <= 0) { ped.state = 'walk'; ped.refs.emote.visible = false; }
          break;
        }
      }
      // animate — model faces local +x, limb pivots hinge on rotation.z
      const r = ped.refs;
      const g = r.group;
      const walking = ped.state === 'walk' || ped.state === 'cross';
      const bob = Math.abs(Math.sin(ped.phase)) * 0.04;
      g.position.set(ped.x, bob + (ped.state === 'dive' ? 0.3 : 0), ped.y);
      g.rotation.y = -ped.face;
      // slight forward lean while walking, sprawl when diving
      g.rotation.z = ped.state === 'dive' ? 0.5 : (walking ? 0.06 : 0);
      const swing = walking ? Math.sin(ped.phase) * 0.55 : 0;
      r.legL.rotation.z = swing;
      r.legR.rotation.z = -swing;
      r.armL.rotation.z = -swing * 0.75;
      r.armR.rotation.z = swing * 0.75;
      if (ped.state === 'film') {
        r.armR.rotation.z = 1.35; // arm raised forward holding the phone
      }
    }
  }
  dive(ped, game, px, py, hard) {
    if (ped.state === 'dive') {
      if (hard) { // still overlapping — teleport safely
        const ang = Math.atan2(ped.y - py, ped.x - px);
        ped.x = px + Math.cos(ang) * 3.4;
        ped.y = py + Math.sin(ang) * 3.4;
      }
      return;
    }
    const ang = Math.atan2(ped.y - py, ped.x - px) + rand(-0.4, 0.4);
    ped.state = 'dive';
    ped.stateT = 0.8;
    ped.dvx = Math.cos(ang) * 8;
    ped.dvy = Math.sin(ang) * 8;
    ped.refs.emote.material.map = Assets.emojiTexture('😱');
    ped.refs.emote.visible = true;
    game.onPedDive(ped);
  }
  soak(x, y, r, game) {
    let n = 0;
    for (const ped of this.list) {
      if (dist2(x, y, ped.x, ped.y) < r * r && ped.state !== 'dive') {
        ped.state = 'soaked';
        ped.stateT = rand(2.5, 4);
        ped.soaked = true;
        ped.refs.emote.material.map = Assets.emojiTexture('😡');
        ped.refs.emote.visible = true;
        n++;
      }
    }
    return n;
  }
  render(alpha) {
    // positions already set in update (fixed step); smooth via px/py lerp
    for (const ped of this.list) {
      ped.refs.group.position.x = lerp(ped.px, ped.x, alpha);
      ped.refs.group.position.z = lerp(ped.py, ped.y, alpha);
    }
  }
  dispose() {
    for (const ped of this.list) this.scene.remove(ped.refs.group);
    this.list = [];
  }
}
