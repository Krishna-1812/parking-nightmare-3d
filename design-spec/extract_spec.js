// Pulls the pure-data literals out of the game source so the Unity rebuild
// gets exact numbers instead of hand-transcribed ones. Brace-matches from the
// declaration to its closing token, then evals the literal in isolation.
const fs = require('fs');
const path = require('path');

const DIR = __dirname;
const OUT = path.join(DIR, 'spec');
fs.mkdirSync(OUT, { recursive: true });

function literal(file, name) {
  const src = fs.readFileSync(path.join(DIR, file), 'utf8');
  const decl = new RegExp('const\\s+' + name + '\\s*=\\s*');
  const m = decl.exec(src);
  if (!m) throw new Error(name + ' not found in ' + file);
  let i = m.index + m[0].length;
  const open = src[i];
  const close = open === '[' ? ']' : '}';
  let depth = 0, inStr = null, esc = false;
  for (let j = i; j < src.length; j++) {
    const c = src[j];
    if (esc) { esc = false; continue; }
    if (inStr) {
      if (c === '\\') esc = true;
      else if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') { inStr = c; continue; }
    if (c === '/' && src[j + 1] === '/') { while (j < src.length && src[j] !== '\n') j++; continue; }
    if (c === open) depth++;
    else if (c === close) {
      depth--;
      if (depth === 0) return src.slice(i, j + 1);
    }
  }
  throw new Error('unbalanced literal for ' + name);
}

const data = {};
for (const [file, name] of [['n3_c.js', 'VEH_DEFS'], ['n3_d.js', 'DISTRICTS'], ['n3_d.js', 'LEVELS']]) {
  const text = literal(file, name);
  // eslint-disable-next-line no-eval
  data[name] = eval('(' + text + ')');
  console.log(name.padEnd(10), '->', Array.isArray(data[name]) ? data[name].length + ' entries' : Object.keys(data[name]).length + ' keys');
}

// hex colour ints (0x…) survive eval as numbers; re-emit them as #rrggbb so the
// JSON is readable and Unity-friendly
const hexKeys = new Set(['hemi', 'sun']);
function toHex(n) { return '#' + n.toString(16).padStart(6, '0'); }
for (const d of data.DISTRICTS) {
  if (Array.isArray(d.hemi)) d.hemi = [toHex(d.hemi[0]), toHex(d.hemi[1]), d.hemi[2]];
  if (Array.isArray(d.sun)) d.sun = [toHex(d.sun[0]), d.sun[1], d.sun[2]];
}

fs.writeFileSync(path.join(OUT, 'vehicles.json'), JSON.stringify(data.VEH_DEFS, null, 2));
fs.writeFileSync(path.join(OUT, 'districts.json'), JSON.stringify(data.DISTRICTS, null, 2));
fs.writeFileSync(path.join(OUT, 'missions.json'), JSON.stringify(data.LEVELS, null, 2));

// ---- summary tables for the written spec ----
const L = data.LEVELS;
console.log('\nmissions:', L.length, '| districts used:', [...new Set(L.map(l => l.district))].sort().join(','));
console.log('park types:', [...new Set(L.map(l => l.park))].join(', '));
console.log('mission keys:', [...new Set(L.flatMap(l => Object.keys(l)))].sort().join(', '));
console.log('vehicle keys:', Object.keys(data.VEH_DEFS).join(', '));
console.log('district keys:', [...new Set(data.DISTRICTS.flatMap(d => Object.keys(d)))].sort().join(', '));

const rows = L.map(l => [l.id, l.district + 1, l.name, l.veh, l.lanes, l.par, l.park || '-', l.margin ?? '-', (l.segs || []).length].join(' | '));
fs.writeFileSync(path.join(OUT, 'mission_table.txt'),
  'id | D | name | vehicle | lanes | par | park | margin | segs\n' + rows.join('\n'));
console.log('\nwrote', fs.readdirSync(OUT).join(', '));
