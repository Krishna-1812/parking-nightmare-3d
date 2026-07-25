// Golden-reference generator for the Unity route-compiler port.
// Extracts the REAL compileRoute/enrichRoute from the shipping source (by text,
// so we validate against the actual functions, not a transcription) and emits
// exact numeric expectations for the C# port to match.
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '..');
const SRC = path.join(REPO, 'src');

// brace-matcher lifted from design-spec/extract_spec.js
function slice(src, startIdx) {
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
  throw new Error('unbalanced');
}
function fn(file, name) {
  const src = fs.readFileSync(path.join(SRC, file), 'utf8');
  const m = new RegExp('function\\s+' + name + '\\s*\\(').exec(src);
  if (!m) throw new Error(name + ' not found in ' + file);
  return slice(src, m.index);
}

const HELPERS = `
const TAU = Math.PI * 2;
const clamp = (v, a, b) => v < a ? a : (v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const dist2 = (x1, y1, x2, y2) => { const dx = x2 - x1, dy = y2 - y1; return dx * dx + dy * dy; };
const angNorm = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };
const rad = d => d * Math.PI / 180;
`;

const ctx = {};
new Function('ctx', HELPERS + fn('n3_d.js', 'compileRoute') + fn('n3_d.js', 'enrichRoute') +
  '\nctx.compileRoute = compileRoute; ctx.enrichRoute = enrichRoute;')(ctx);
const { compileRoute, enrichRoute } = ctx;

const MISSIONS = require(path.join(REPO, 'design-spec/data/missions.json'));

// deterministic probe points: on-route at lateral offsets, plus far-off points
function probes(route) {
  const out = [];
  const L = route.length;
  for (const sf of [0, 0.13, 0.37, 0.5, 0.66, 0.81, 0.99]) {
    const s = sf * L;
    const p = route.sampleAt(s);
    for (const t of [-8.5, -3.5, 0, 3.5, 8.5]) {
      const px = p.x - Math.sin(p.h) * t, py = p.y + Math.cos(p.h) * t;
      const pr = route.project(px, py);
      out.push({ px, py, s: pr.s, t: pr.t, h: pr.h, idx: pr.idx, kind: pr.kind });
    }
  }
  // off-route probes (global search path)
  for (const [px, py] of [[0, 0], [-50, -50], [200, 40], [500, 300], [-120, 260], [1000, -1000]]) {
    const pr = route.project(px, py);
    out.push({ px, py, s: pr.s, t: pr.t, h: pr.h, idx: pr.idx, kind: pr.kind });
  }
  return out;
}

const golden = MISSIONS.map(raw => {
  const lvl = JSON.parse(JSON.stringify(raw));
  const rawPar = lvl.par, rawSegs = lvl.segs.length;
  enrichRoute(lvl);
  const route = compileRoute(lvl.segs);
  const samples = [];
  for (let i = 0; i <= 20; i++) {
    const p = route.sampleAt(route.length * i / 20);
    samples.push({ s: p.s, x: p.x, y: p.y, h: p.h, kind: p.kind });
  }
  return {
    id: lvl.id, name: lvl.name, tutorial: !!lvl.tutorial,
    rawSegs, rawPar,
    enrichedSegs: lvl.segs.length, enrichedPar: lvl.par, enriched: !!lvl._enriched,
    segs: lvl.segs,
    length: route.length, ptsCount: route.pts.length,
    inters: route.inters, zones: route.zones, curves: route.curves,
    samples, probes: probes(route),
  };
});

const OUT = path.join(REPO, 'tools', 'RouteValidator', 'golden_routes.json');
fs.writeFileSync(OUT, JSON.stringify(golden, null, 1));

console.log('id | name                     | segs raw->enr | par raw->enr | length m | pts');
for (const g of golden) {
  console.log(
    String(g.id).padStart(2) + ' | ' + g.name.padEnd(24) + ' | ' +
    String(g.rawSegs).padStart(4) + ' ->' + String(g.enrichedSegs).padStart(3) + '   | ' +
    String(g.rawPar).padStart(4) + ' ->' + String(g.enrichedPar).padStart(4) + '  | ' +
    g.length.toFixed(1).padStart(8) + ' | ' + String(g.ptsCount).padStart(4) +
    (g.tutorial ? '   (tutorial: not enriched)' : ''));
}
const lens = golden.map(g => g.length);
console.log('\nroute length range: ' + Math.min(...lens).toFixed(0) + '–' + Math.max(...lens).toFixed(0) + ' m');
const radii = golden.flatMap(g => g.segs.filter(s => s.t === 'L' || s.t === 'R').map(s => s.r));
console.log('enriched curve radii: ' + Math.min(...radii) + '–' + Math.max(...radii) + ' m');
console.log('wrote ' + OUT);
