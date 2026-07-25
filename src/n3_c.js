/* ============================================================
   PART C — 3D asset factories: textures, vehicles, buildings,
   props, pedestrians, particles, comic text sprites
   ============================================================ */

// ============================================================
// VEHICLE DEFINITIONS (meters, m/s)
// ============================================================
const VEH_DEFS = {
  hatch: {
    name: 'Rusty Hatchback', horn: 'hatch', drive: 'car',
    flavor: '"Runs on hope and expired coupons. Occasionally explodes a little, as a treat."',
    len: 3.9, wid: 1.78, hgt: 1.5, wb: 2.5, maxSpeed: 19, accel: 7.5, steerSpeed: 3.4, grip: 0.95, mass: 1, fragility: 1,
    stats: { size: 22, speed: 55, hand: 88, chaos: 25 },
    unlock: { type: 'start' },
    body: '#c0563b', roof: '#a84730',
  },
  wagon: {
    name: 'Family Wagon', horn: 'hatch', drive: 'car',
    flavor: '"The kid in the back is your harshest critic. He has seen things. Mostly your parking."',
    len: 5.0, wid: 1.88, hgt: 1.55, wb: 3.1, maxSpeed: 18, accel: 7, steerSpeed: 3.0, grip: 0.95, mass: 1.3, fragility: 1,
    stats: { size: 38, speed: 50, hand: 72, chaos: 30 },
    unlock: { type: 'level', lv: 3 }, price: 900,
    body: '#3f8f8f', roof: '#337575',
  },
  limo: {
    name: 'Stretch Limo', horn: 'limo', drive: 'car',
    flavor: '"Longer than your list of regrets. The middle section has never once seen the curb."',
    len: 8.6, wid: 1.95, hgt: 1.5, wb: 6.3, maxSpeed: 17, accel: 6, steerSpeed: 2.6, grip: 0.96, mass: 2, fragility: 1.2,
    stats: { size: 78, speed: 42, hand: 30, chaos: 55 },
    unlock: { type: 'level', lv: 5 }, price: 1800,
    body: '#23252e', roof: '#181a21',
  },
  icecream: {
    name: 'Ice Cream Truck', horn: 'icecream', drive: 'car',
    flavor: '"The jingle cannot be stopped. The children cannot be stopped. Nothing can be stopped."',
    len: 5.8, wid: 2.15, hgt: 2.6, wb: 3.7, maxSpeed: 15, accel: 6, steerSpeed: 2.8, grip: 0.93, mass: 1.7, fragility: 1,
    stats: { size: 52, speed: 38, hand: 58, chaos: 75 },
    unlock: { type: 'level', lv: 6 }, price: 2600,
    body: '#fdf6ee', roof: '#f8b8cf',
  },
  bus: {
    name: 'School Bus', horn: 'bus', drive: 'car',
    flavor: '"The mirrors count. The stop-sign arm has a mind of its own. The children are watching."',
    len: 10.6, wid: 2.45, hgt: 3.0, wb: 7.2, maxSpeed: 14, accel: 5, steerSpeed: 2.3, grip: 0.96, mass: 3, fragility: 0.8,
    stats: { size: 95, speed: 33, hand: 22, chaos: 60 },
    unlock: { type: 'level', lv: 8 }, price: 3600,
    body: '#f2b32b', roof: '#dda01f',
  },
  tank: {
    name: 'Tank', horn: 'tank', drive: 'tank',
    flavor: '"Turns on a dime. Crushes the dime. Crushes everything. The turret has opinions."',
    len: 6.6, wid: 3.2, hgt: 2.4, wb: 4.5, maxSpeed: 9, accel: 6, steerSpeed: 3, grip: 1, mass: 8, fragility: 0.2,
    stats: { size: 70, speed: 18, hand: 80, chaos: 100 },
    unlock: { type: 'level', lv: 10 }, price: 5200,
    body: '#6b7a4a', roof: '#5a6840',
  },
  ufo: {
    name: 'UFO', horn: 'ufo', drive: 'ufo',
    flavor: '"No friction. No brakes. No dignity. Beam down gently — the whole galaxy is filming."',
    len: 4.6, wid: 4.6, hgt: 1.9, wb: 3, maxSpeed: 18, accel: 7, steerSpeed: 3, grip: 0, mass: 0.8, fragility: 1,
    stats: { size: 45, speed: 65, hand: 8, chaos: 90 },
    unlock: { type: 'stars', n: 30 }, price: 7500,
    body: '#c8cede', roof: '#9ba3b8',
  },
  kart: {
    name: 'Go-Kart', horn: 'hatch', drive: 'car',
    flavor: '"Technically street legal. Emotionally unhinged. Your knees ARE the crumple zone."',
    len: 2.3, wid: 1.35, hgt: 1.0, wb: 1.6, maxSpeed: 17, accel: 9.5, steerSpeed: 4.4, grip: 1.05, mass: 0.45, fragility: 1.7,
    stats: { size: 6, speed: 58, hand: 98, chaos: 45 },
    unlock: { type: 'stars', n: 12 }, price: 1500,
    body: '#ffd23e', roof: '#2b2d36',
  },
  monster: {
    name: 'Monster Pickup', horn: 'bus', drive: 'car',
    flavor: '"The suspension is taller than your first car. Parking spots fear it. Physics respects it."',
    len: 5.7, wid: 2.7, hgt: 3.1, wb: 3.6, maxSpeed: 16, accel: 8, steerSpeed: 2.7, grip: 0.9, mass: 4.2, fragility: 0.4,
    stats: { size: 64, speed: 46, hand: 42, chaos: 88 },
    unlock: { type: 'level', lv: 14 }, price: 5000,
    body: '#7a3fd4', roof: '#5c2ba8',
  },
};
function vehicleUnlocked(key) {
  if (Save.data.owned && Save.data.owned.includes(key)) return true;
  const u = VEH_DEFS[key].unlock;
  if (u.type === 'start') return true;
  if (u.type === 'level') return Save.data.unlockedLevel >= u.lv;
  if (u.type === 'stars') return Save.totalStars() >= u.n;
  return false;
}
function vehicleUnlockText(key) {
  const u = VEH_DEFS[key].unlock;
  if (u.type === 'start') return '';
  if (u.type === 'level') return `🔒 Reach Mission ${u.lv} — or buy it with coins`;
  if (u.type === 'stars') return `🔒 Earn ${u.n} total stars (you have ${Save.totalStars()}) — or buy it with coins`;
  return '';
}

// ============================================================
// ASSETS — shared geometries, materials, canvas textures
// ============================================================
const Assets = {
  geos: {}, mats: {}, texs: {}, fonts: null,

  canvas(w, h, fn) {
    const cv = document.createElement('canvas');
    cv.width = w; cv.height = h;
    fn(cv.getContext('2d'), w, h);
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;
    return tex;
  },

  geo(key, make) {
    if (!this.geos[key]) this.geos[key] = make();
    return this.geos[key];
  },
  lambert(key, opts) {
    if (!this.mats[key]) this.mats[key] = new THREE.MeshLambertMaterial(opts);
    return this.mats[key];
  },

  // grayscale aggregate + undulation noise shared by every asphalt surface
  asphaltBump() {
    if (this.texs.aBump) return this.texs.aBump;
    const tex = this.canvas(256, 256, (c, W, H) => {
      c.fillStyle = '#808080'; c.fillRect(0, 0, W, H);
      for (let i = 0; i < 2600; i++) {
        const v = Math.round(rand(70, 185));
        c.fillStyle = `rgba(${v},${v},${v},${rand(0.25, 0.7)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 3), rand(1, 3));
      }
      for (let i = 0; i < 26; i++) {
        const x = rand(0, W), y = rand(0, H), r = rand(18, 60);
        const g = c.createRadialGradient(x, y, 1, x, y, r);
        g.addColorStop(0, Math.random() < 0.5 ? 'rgba(255,255,255,.10)' : 'rgba(0,0,0,.10)');
        g.addColorStop(1, 'rgba(128,128,128,0)');
        c.fillStyle = g; c.fillRect(x - r, y - r, r * 2, r * 2);
      }
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    this.texs.aBump = tex;
    return tex;
  },

  // PBR asphalt: dry = matte with grain; wet (rain levels) = dark mirror-ish
  // sheen that picks up the sky/neon through the scene envmap
  asphaltMat(map, wet) {
    return new THREE.MeshStandardMaterial({
      map,
      color: wet ? 0x86888f : 0xffffff,
      roughness: wet ? 0.32 : 0.88,
      metalness: wet ? 0.08 : 0.0,
      envMapIntensity: wet ? 1.25 : 0.22,
      bumpMap: this.asphaltBump(),
      bumpScale: wet ? 0.15 : 0.35,
    });
  },

  // ---------- road texture: repeats along route ----------
  // one repeat tile = `repeat` meters of road, full width across
  // paints believable asphalt (aggregate, blotches, cracks) into a canvas region
  _asphaltBase(c, W, H, night) {
    c.fillStyle = night ? '#31343d' : '#484b53';
    c.fillRect(0, 0, W, H);
    // large tonal blotches (weathering)
    for (let i = 0; i < Math.round(W * H / 9000); i++) {
      const x = rand(0, W), y = rand(0, H), r = rand(20, 90);
      const g = c.createRadialGradient(x, y, 2, x, y, r);
      const dark = Math.random() < 0.5;
      g.addColorStop(0, dark ? 'rgba(0,0,0,.09)' : 'rgba(255,255,255,.05)');
      g.addColorStop(1, 'rgba(0,0,0,0)');
      c.fillStyle = g;
      c.fillRect(x - r, y - r, r * 2, r * 2);
    }
    // patch repairs: darker fresh-sealant rectangles with soft edge lines
    for (let i = 0; i < Math.round(W * H / 300000); i++) {
      const x = rand(0, W * 0.8), y = rand(0, H * 0.8), pw = rand(60, 170), ph = rand(50, 130);
      c.fillStyle = 'rgba(0,0,0,.12)';
      c.fillRect(x, y, pw, ph);
      c.strokeStyle = 'rgba(0,0,0,.22)'; c.lineWidth = 3;
      c.strokeRect(x, y, pw, ph);
      c.strokeStyle = 'rgba(255,255,255,.04)'; c.lineWidth = 1;
      c.strokeRect(x - 2, y - 2, pw + 4, ph + 4);
    }
    // fine aggregate speckle
    for (let i = 0; i < Math.round(W * H / 220); i++) {
      c.fillStyle = `rgba(${Math.random() > 0.5 ? '255,255,255' : '0,0,0'},${rand(0.02, 0.07)})`;
      c.fillRect(rand(0, W), rand(0, H), rand(1, 2.5), rand(1, 2.5));
    }
    // hairline cracks
    c.strokeStyle = 'rgba(0,0,0,.28)';
    for (let i = 0; i < Math.round(H / 90); i++) {
      c.lineWidth = rand(0.8, 1.6);
      let x = rand(0, W), y = rand(0, H);
      c.beginPath(); c.moveTo(x, y);
      for (let k = 0; k < 6; k++) { x += rand(-16, 16); y += rand(8, 26); c.lineTo(x, y); }
      c.stroke();
    }
    // tar crack-seal squiggles (glossy dark worm lines road crews leave)
    for (let i = 0; i < Math.round(H / 170); i++) {
      c.strokeStyle = night ? 'rgba(14,16,20,.55)' : 'rgba(24,26,30,.5)';
      c.lineWidth = rand(2.5, 4);
      c.lineCap = 'round';
      let x = rand(0, W), y = rand(0, H);
      c.beginPath(); c.moveTo(x, y);
      for (let k = 0; k < 8; k++) { x += rand(-22, 22); y += rand(10, 30); c.lineTo(x, y); }
      c.stroke();
      // faint highlight beside the seal so it reads embossed
      c.strokeStyle = 'rgba(255,255,255,.05)';
      c.lineWidth = 1;
      c.stroke();
    }
    c.lineCap = 'butt';
  },
  roadTexture(lanesPerSide, night) {
    const key = `road${lanesPerSide}${night ? 'n' : ''}`;
    if (this.texs[key]) return this.texs[key];
    const W = 1024, H = 1024;
    const tex = this.canvas(W, H, (c) => {
      this._asphaltBase(c, W, H, night);
      // geometry: full width spans -RW..RW where RW = lanes*3.5 + 2.3 park strip
      const RW = lanesPerSide * 3.5 + 2.3;
      const u = t => (t + RW) / (2 * RW) * W; // world t -> texture x
      // tire-wear tracks: darkened wheel paths in each lane
      for (let ln = 0; ln < lanesPerSide; ln++) {
        for (const side of [1, -1]) {
          const center = side * (ln * 3.5 + 1.75);
          for (const off of [-0.8, 0.8]) {
            const px = u(center + off), w = W / (2 * RW) * 0.85;
            const g = c.createLinearGradient(px - w, 0, px + w, 0);
            g.addColorStop(0, 'rgba(0,0,0,0)');
            g.addColorStop(0.5, 'rgba(0,0,0,.14)');
            g.addColorStop(1, 'rgba(0,0,0,0)');
            c.fillStyle = g;
            c.fillRect(px - w, 0, w * 2, H);
          }
        }
      }
      // oil-drip stains down each lane center (engines leak between the wheels)
      for (let ln = 0; ln < lanesPerSide; ln++) {
        for (const side of [1, -1]) {
          const px = u(side * (ln * 3.5 + 1.75));
          for (let y = rand(0, 120); y < H; y += rand(120, 330)) {
            const len = rand(40, 130), w = rand(5, 12);
            const g = c.createRadialGradient(px, y + len / 2, 2, px, y + len / 2, len / 2);
            g.addColorStop(0, 'rgba(10,10,14,.28)');
            g.addColorStop(1, 'rgba(10,10,14,0)');
            c.fillStyle = g;
            c.save();
            c.translate(px, y + len / 2); c.scale(w / len, 1); c.translate(-px, -(y + len / 2));
            c.beginPath(); c.arc(px, y + len / 2, len / 2, 0, TAU); c.fill();
            c.restore();
          }
        }
      }
      // gutter grime near the parking strips
      for (const e of [0, W]) {
        const g = c.createLinearGradient(e, 0, e === 0 ? 40 : W - 40, 0);
        g.addColorStop(0, 'rgba(0,0,0,.22)');
        g.addColorStop(1, 'rgba(0,0,0,0)');
        c.fillStyle = g;
        c.fillRect(Math.min(e, e === 0 ? 40 : W - 40), 0, 40, H);
      }
      // center double yellow (weathered, chipped in patches)
      c.fillStyle = 'rgba(228,178,52,.92)';
      c.fillRect(W / 2 - 11, 0, 7, H);
      c.fillRect(W / 2 + 4, 0, 7, H);
      c.fillStyle = night ? 'rgba(49,52,61,.65)' : 'rgba(72,75,83,.65)';
      for (let i = 0; i < 46; i++) { // chips eaten out of the paint
        c.fillRect(W / 2 - 12 + rand(0, 24), rand(0, H), rand(2, 6), rand(3, 14));
      }
      // white edge lines at the parking-strip boundary
      c.fillStyle = 'rgba(255,255,255,.8)';
      c.fillRect(u(-(RW - 2.3)) - 4, 0, 8, H);
      c.fillRect(u(RW - 2.3) - 4, 0, 8, H);
      // parking bay ticks in the strip
      c.fillStyle = 'rgba(255,255,255,.42)';
      for (let y = 0; y < H; y += 340) {
        c.fillRect(u(RW - 2.3), y, W - u(RW - 2.3), 8);
        c.fillRect(0, y, u(-(RW - 2.3)), 8);
      }
      // dashed lane separators (if 2 lanes per side)
      if (lanesPerSide === 2) {
        c.fillStyle = 'rgba(255,255,255,.7)';
        for (let y = 0; y < H; y += 256) {
          c.fillRect(u(-3.5) - 5, y + 40, 10, 128);
          c.fillRect(u(3.5) - 5, y + 40, 10, 128);
        }
      }
      // paint wear: speckle over the markings so they read as worn-in
      for (let i = 0; i < 1400; i++) {
        c.fillStyle = `rgba(${night ? '49,52,61' : '72,75,83'},${rand(0.1, 0.4)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 3), rand(1, 3));
      }
    });
    tex.wrapT = THREE.RepeatWrapping;
    this.texs[key] = tex;
    return tex;
  },
  plainAsphalt(night) {
    const key = `asph${night ? 'n' : ''}`;
    if (this.texs[key]) return this.texs[key];
    const tex = this.canvas(512, 512, (c, W, H) => {
      this._asphaltBase(c, W, H, night);
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    this.texs[key] = tex;
    return tex;
  },
  crosswalkTexture(night) {
    const key = `cross${night ? 'n' : ''}`;
    if (this.texs[key]) return this.texs[key];
    this.texs[key] = this.canvas(512, 256, (c, W, H) => {
      this._asphaltBase(c, W, H, night);
      c.fillStyle = 'rgba(255,255,255,.85)';
      for (let x = 16; x < W; x += 84) c.fillRect(x, 12, 48, H - 24);
      // worn paint
      for (let i = 0; i < 700; i++) {
        c.fillStyle = `rgba(${night ? '49,52,61' : '72,75,83'},${rand(0.12, 0.45)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 3), rand(1, 3));
      }
    });
    return this.texs[key];
  },
  // curb: stained concrete strip with expansion joints + gutter grime.
  // Ribbon UV: u (canvas x) spans curb WIDTH road→walk side, v (canvas y)
  // runs ALONG the road — so joints are horizontal lines in the canvas.
  curbTexture(night) {
    const key = `curb${night ? 'n' : ''}`;
    if (this.texs[key]) return this.texs[key];
    const tex = this.canvas(64, 512, (c, W, H) => {
      c.fillStyle = night ? '#666a78' : '#b6b2a6';
      c.fillRect(0, 0, W, H);
      // concrete speckle
      for (let i = 0; i < 700; i++) {
        c.fillStyle = `rgba(${Math.random() > 0.5 ? '255,255,255' : '0,0,0'},${rand(0.03, 0.09)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 3), rand(1, 3));
      }
      // grime washed up from the gutter (x=0 is the road side)
      const g = c.createLinearGradient(W * 0.55, 0, 0, 0);
      g.addColorStop(0, 'rgba(0,0,0,0)');
      g.addColorStop(1, 'rgba(30,28,24,.42)');
      c.fillStyle = g; c.fillRect(0, 0, W, H);
      // weather stains running along the curb
      for (let i = 0; i < 12; i++) {
        const y = rand(0, H);
        c.fillStyle = `rgba(50,46,40,${rand(0.06, 0.16)})`;
        c.fillRect(rand(0, W * 0.4), y, W, rand(3, 9));
      }
      // expansion joints every ~3m of curb run
      c.fillStyle = 'rgba(0,0,0,.42)';
      for (let y = 40; y < H; y += 128) c.fillRect(0, y, W, 3);
      // sun-caught arris on the road-side edge
      c.fillStyle = 'rgba(255,255,255,.2)';
      c.fillRect(0, 0, 4, H);
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    this.texs[key] = tex;
    return tex;
  },
  sidewalkTexture(night) {
    const key = `swalk${night ? 'n' : ''}`;
    if (this.texs[key]) return this.texs[key];
    const tex = this.canvas(512, 512, (c, W, H) => {
      c.fillStyle = night ? '#5c5f6b' : '#aaa69d';
      c.fillRect(0, 0, W, H);
      // per-slab tone variation
      const slab = 128;
      for (let sy = 0; sy < H; sy += slab) {
        for (let sx = 0; sx < W; sx += slab) {
          c.fillStyle = `rgba(${Math.random() > 0.5 ? '255,255,255' : '0,0,0'},${rand(0.02, 0.07)})`;
          c.fillRect(sx, sy, slab, slab);
        }
      }
      // fine concrete speckle
      for (let i = 0; i < 1100; i++) {
        c.fillStyle = `rgba(0,0,0,${rand(0.02, 0.06)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 3), rand(1, 3));
      }
      // stains
      for (let i = 0; i < 8; i++) {
        const x = rand(0, W), y = rand(0, H), r = rand(14, 44);
        const g = c.createRadialGradient(x, y, 2, x, y, r);
        g.addColorStop(0, 'rgba(60,55,45,.1)');
        g.addColorStop(1, 'rgba(0,0,0,0)');
        c.fillStyle = g;
        c.fillRect(x - r, y - r, r * 2, r * 2);
      }
      // expansion joints both ways + shadow edge
      c.strokeStyle = 'rgba(0,0,0,.3)'; c.lineWidth = 3;
      for (let y = 0; y <= H; y += slab) { c.beginPath(); c.moveTo(0, y); c.lineTo(W, y); c.stroke(); }
      for (let x = 0; x <= W; x += slab) { c.beginPath(); c.moveTo(x, 0); c.lineTo(x, H); c.stroke(); }
      c.strokeStyle = 'rgba(255,255,255,.14)'; c.lineWidth = 1.5;
      for (let y = 2; y <= H; y += slab) { c.beginPath(); c.moveTo(0, y); c.lineTo(W, y); c.stroke(); }
      // hairline cracks
      c.strokeStyle = 'rgba(0,0,0,.2)';
      for (let i = 0; i < 5; i++) {
        c.lineWidth = rand(0.8, 1.4);
        let x = rand(0, W), y = rand(0, H);
        c.beginPath(); c.moveTo(x, y);
        for (let k = 0; k < 4; k++) { x += rand(-14, 14); y += rand(10, 26); c.lineTo(x, y); }
        c.stroke();
      }
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    this.texs[key] = tex;
    return tex;
  },
  grassTexture(base, spot) {
    const key = `grass2${base}`;
    if (this.texs[key]) return this.texs[key];
    const tex = this.canvas(1024, 1024, (c, W, H) => {
      c.fillStyle = base; c.fillRect(0, 0, W, H);
      // big organic patches: dried-out straw, lusher darker growth, worn dirt
      const baseC = new THREE.Color(base);
      const straw = baseC.clone().lerp(new THREE.Color('#c9b96a'), 0.5);
      const lush = baseC.clone().multiplyScalar(0.78);
      const dirt = baseC.clone().lerp(new THREE.Color('#8a6f4d'), 0.55);
      const css = (col, a) => `rgba(${Math.round(col.r * 255)},${Math.round(col.g * 255)},${Math.round(col.b * 255)},${a})`;
      for (let i = 0; i < 34; i++) {
        const x = rand(0, W), y = rand(0, H), r = rand(50, 190);
        const which = Math.random();
        const col = which < 0.4 ? straw : (which < 0.8 ? lush : dirt);
        const g = c.createRadialGradient(x, y, r * 0.15, x, y, r);
        g.addColorStop(0, css(col, which < 0.8 ? 0.32 : 0.4));
        g.addColorStop(1, css(col, 0));
        c.fillStyle = g;
        c.beginPath(); c.arc(x, y, r, 0, TAU); c.fill();
      }
      // fine mid-frequency mottle
      for (let i = 0; i < 90; i++) {
        const x = rand(0, W), y = rand(0, H), r = rand(14, 48);
        const g = c.createRadialGradient(x, y, 2, x, y, r);
        g.addColorStop(0, Math.random() < 0.5 ? 'rgba(0,0,0,.09)' : 'rgba(255,255,235,.07)');
        g.addColorStop(1, 'rgba(0,0,0,0)');
        c.fillStyle = g;
        c.fillRect(x - r, y - r, r * 2, r * 2);
      }
      // faint diagonal mow bands (subtler than before)
      c.save();
      c.translate(W / 2, H / 2); c.rotate(0.18); c.translate(-W / 2, -H / 2);
      for (let x = -W; x < W * 2; x += 256) {
        c.fillStyle = 'rgba(255,255,255,.025)';
        c.fillRect(x, -H, 128, H * 3);
      }
      c.restore();
      // grass blades: short angled strokes in varied greens
      for (let i = 0; i < 9000; i++) {
        const x = rand(0, W), y = rand(0, H), len = rand(3, 8);
        const a = rand(-0.5, 0.5);
        c.strokeStyle = Math.random() < 0.5 ? spot : (Math.random() < 0.5 ? 'rgba(30,60,20,.35)' : 'rgba(195,225,125,.25)');
        c.globalAlpha = rand(0.22, 0.55);
        c.lineWidth = rand(0.7, 1.5);
        c.beginPath();
        c.moveTo(x, y);
        c.lineTo(x + Math.sin(a) * len, y - Math.cos(a) * len);
        c.stroke();
      }
      c.globalAlpha = 1;
      // scattered clover/weed dots
      for (let i = 0; i < 320; i++) {
        c.fillStyle = Math.random() < 0.7 ? 'rgba(24,52,18,.3)' : 'rgba(225,240,160,.3)';
        c.beginPath(); c.arc(rand(0, W), rand(0, H), rand(1.2, 3), 0, TAU); c.fill();
      }
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    this.texs[key] = tex;
    return tex;
  },
  // shingle roof tiles: offset tab rows with per-tab shading
  shingleTexture(base) {
    const key = 'shing' + base;
    if (this.texs[key]) return this.texs[key];
    const tex = this.canvas(256, 256, (c, W, H) => {
      c.fillStyle = base; c.fillRect(0, 0, W, H);
      const bc = new THREE.Color(base);
      const rowH = 22, tabW = 30;
      let row = 0;
      for (let y = 0; y < H + rowH; y += rowH, row++) {
        const off = (row % 2) * (tabW / 2);
        for (let x = -tabW; x < W + tabW; x += tabW) {
          const v = rand(-0.1, 0.12);
          const col = bc.clone().multiplyScalar(1 + v);
          c.fillStyle = `rgb(${Math.round(col.r * 255)},${Math.round(col.g * 255)},${Math.round(col.b * 255)})`;
          c.fillRect(x + off, y, tabW - 2, rowH - 2);
          // tab bottom shadow lip
          c.fillStyle = 'rgba(0,0,0,.3)';
          c.fillRect(x + off, y + rowH - 4, tabW - 2, 2.5);
        }
        // row shadow line
        c.fillStyle = 'rgba(0,0,0,.22)';
        c.fillRect(0, y - 1, W, 2);
      }
      // weathering streaks down the slope
      for (let i = 0; i < 10; i++) {
        c.fillStyle = `rgba(0,0,0,${rand(0.04, 0.1)})`;
        c.fillRect(rand(0, W), 0, rand(3, 10), H);
      }
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    this.texs[key] = tex;
    return tex;
  },
  // city building side wall: subtle brick coursing + sparse dim windows
  sideWallTexture(wall, night) {
    const key = 'sidew' + wall + (night ? 'n' : '');
    if (this.texs[key]) return this.texs[key];
    const tex = this.canvas(256, 512, (c, W, H) => {
      c.fillStyle = wall; c.fillRect(0, 0, W, H);
      // brick coursing
      for (let y = 0; y < H; y += 10) {
        c.fillStyle = 'rgba(0,0,0,.07)';
        c.fillRect(0, y, W, 1.4);
      }
      for (let y = 0, r = 0; y < H; y += 10, r++) {
        for (let x = (r % 2) * 12; x < W; x += 24) {
          c.fillStyle = 'rgba(0,0,0,.05)';
          c.fillRect(x, y, 1.2, 10);
        }
      }
      // weathering
      for (let i = 0; i < 10; i++) {
        const x = rand(0, W), y = rand(0, H), r = rand(24, 70);
        const g = c.createRadialGradient(x, y, 2, x, y, r);
        g.addColorStop(0, Math.random() < 0.6 ? 'rgba(0,0,0,.08)' : 'rgba(255,255,255,.05)');
        g.addColorStop(1, 'rgba(0,0,0,0)');
        c.fillStyle = g;
        c.fillRect(x - r, y - r, r * 2, r * 2);
      }
      // sparse small windows
      for (let fy = 30; fy < H - 50; fy += 62) {
        for (const fx of [W * 0.28, W * 0.66]) {
          if (Math.random() < 0.35) continue;
          c.fillStyle = 'rgba(0,0,0,.4)';
          c.fillRect(fx - 2, fy - 2, 30, 40);
          const lit = night && Math.random() < 0.3;
          c.fillStyle = lit ? '#f5cd7a' : '#2c3440';
          c.fillRect(fx, fy, 26, 36);
          if (!lit) {
            c.fillStyle = 'rgba(200,220,235,.25)';
            c.fillRect(fx, fy, 26, 12);
          }
        }
      }
      // grime base
      const g2 = c.createLinearGradient(0, H - 60, 0, H);
      g2.addColorStop(0, 'rgba(0,0,0,0)'); g2.addColorStop(1, 'rgba(0,0,0,.25)');
      c.fillStyle = g2; c.fillRect(0, H - 60, W, 60);
    });
    this.texs[key] = tex;
    return tex;
  },
  // ---------- building facade ----------
  facadeTexture(wall, win, floors, cols, litChance) {
    return this.canvas(512, 512, (c, W, H) => {
      c.fillStyle = wall; c.fillRect(0, 0, W, H);
      // subtle masonry coursing
      for (let y = 0; y < H; y += 9) {
        c.fillStyle = 'rgba(0,0,0,.05)';
        c.fillRect(0, y, W, 1.2);
      }
      // wall weathering
      for (let i = 0; i < 14; i++) {
        const x = rand(0, W), y = rand(0, H), r = rand(24, 80);
        const g = c.createRadialGradient(x, y, 2, x, y, r);
        g.addColorStop(0, Math.random() < 0.6 ? 'rgba(0,0,0,.06)' : 'rgba(255,255,255,.05)');
        g.addColorStop(1, 'rgba(0,0,0,0)');
        c.fillStyle = g;
        c.fillRect(x - r, y - r, r * 2, r * 2);
      }
      // roof cornice band
      c.fillStyle = 'rgba(0,0,0,.28)';
      c.fillRect(0, 0, W, 7);
      c.fillStyle = 'rgba(255,255,255,.16)';
      c.fillRect(0, 7, W, 3);
      const gx = W / cols, gy = H / floors;
      for (let f = 0; f < floors; f++) {
        if (f === floors - 1 && floors > 2) { // ground floor: storefronts
          const y0 = f * gy;
          // glass band
          c.fillStyle = '#1d242e';
          c.fillRect(0, y0 + gy * 0.22, W, gy * 0.66);
          for (let x = 0; x < W; x += 36) { // mullions
            c.fillStyle = 'rgba(150,160,175,.8)';
            c.fillRect(x, y0 + gy * 0.22, 3, gy * 0.66);
          }
          // diagonal sheen on the glass
          const sg = c.createLinearGradient(0, y0 + gy * 0.22, W * 0.4, y0 + gy * 0.88);
          sg.addColorStop(0, 'rgba(215,230,242,.22)');
          sg.addColorStop(0.5, 'rgba(215,230,242,0)');
          c.fillStyle = sg;
          c.fillRect(0, y0 + gy * 0.22, W, gy * 0.66);
          // awnings + sign boards per bay
          const bays = 2 + Math.floor(Math.random() * 2);
          const bw = W / bays;
          for (let b = 0; b < bays; b++) {
            const ax = b * bw;
            c.fillStyle = pick(['#b8443a', '#2e6e52', '#28518a', '#b07830', '#7a3a68']);
            c.fillRect(ax + 6, y0 + gy * 0.1, bw - 12, gy * 0.16);
            c.fillStyle = 'rgba(0,0,0,.25)'; // awning underside shadow
            c.fillRect(ax + 6, y0 + gy * 0.24, bw - 12, gy * 0.045);
            c.fillStyle = 'rgba(255,255,255,.75)'; // sign text blocks
            const tw = bw * rand(0.3, 0.55);
            c.fillRect(ax + bw / 2 - tw / 2, y0 + gy * 0.135, tw, gy * 0.05);
            // doorway
            c.fillStyle = 'rgba(8,10,14,.85)';
            c.fillRect(ax + bw / 2 - 14, y0 + gy * 0.4, 28, gy * 0.48);
          }
          continue;
        }
        // floor slab line
        c.fillStyle = 'rgba(0,0,0,.13)';
        c.fillRect(0, f * gy + gy * 0.92, W, gy * 0.05);
        for (let k = 0; k < cols; k++) {
          const wx = k * gx + gx * 0.2, wy = f * gy + gy * 0.18, ww = gx * 0.6, wh = gy * 0.58;
          const lit = Math.random() < litChance;
          // frame shadow (window inset)
          c.fillStyle = 'rgba(0,0,0,.4)';
          c.fillRect(wx - 2, wy - 2, ww + 4, wh + 4);
          if (lit) {
            const g = c.createLinearGradient(0, wy, 0, wy + wh);
            g.addColorStop(0, '#ffe6a4'); g.addColorStop(1, '#f5bd5e');
            c.fillStyle = g;
            c.fillRect(wx, wy, ww, wh);
          } else {
            // dark glass with diagonal sky reflection
            const g = c.createLinearGradient(wx, wy, wx + ww, wy + wh);
            g.addColorStop(0, win);
            g.addColorStop(0.45, 'rgba(210,228,240,.4)');
            g.addColorStop(0.55, win);
            g.addColorStop(1, win);
            c.fillStyle = g;
            c.fillRect(wx, wy, ww, wh);
            // some windows have half-drawn blinds
            if (Math.random() < 0.3) {
              c.fillStyle = 'rgba(226,222,210,.85)';
              c.fillRect(wx, wy, ww, wh * rand(0.25, 0.55));
            }
          }
          // mullion + sill
          c.fillStyle = 'rgba(0,0,0,.25)';
          c.fillRect(wx + ww / 2 - 1, wy, 2, wh);
          c.fillStyle = 'rgba(255,255,255,.3)';
          c.fillRect(wx - 3, wy + wh, ww + 6, 3);
          // occasional AC unit under a window
          if (Math.random() < 0.12) {
            c.fillStyle = '#9aa0a8';
            c.fillRect(wx + ww * 0.3, wy + wh + 4, ww * 0.4, gy * 0.14);
            c.strokeStyle = 'rgba(0,0,0,.4)'; c.lineWidth = 1;
            c.strokeRect(wx + ww * 0.3, wy + wh + 4, ww * 0.4, gy * 0.14);
          }
        }
      }
      // pavement shadow line at the very base
      c.fillStyle = 'rgba(0,0,0,.18)'; c.fillRect(0, H - 6, W, 6);
    });
  },
  // full-sphere sky: horizon sits at v=0.5, so the whole gradient is packed
  // into the visible upper half (below-horizon half just holds the horizon
  // tone for PMREM reflections). Atmosphere-style curve: zenith holds deep,
  // colors compress and warm toward the horizon, plus a haze band and dither.
  skyTexture(top, mid, bot) {
    const key = `sky2${top}${mid}${bot}`;
    if (this.texs[key]) return this.texs[key];
    this.texs[key] = this.canvas(1024, 512, (c, W, H) => {
      const cT = new THREE.Color(top), cM = new THREE.Color(mid), cB = new THREE.Color(bot);
      const HZ = H * 0.5; // horizon row
      const g = c.createLinearGradient(0, 0, 0, HZ);
      const col = t => { // t: 0 zenith → 1 horizon, Rayleigh-ish ease
        const e = Math.pow(t, 1.65);
        const o = (e < 0.62 ? cT.clone().lerp(cM, e / 0.62) : cM.clone().lerp(cB, (e - 0.62) / 0.38));
        return `rgb(${Math.round(o.r * 255)},${Math.round(o.g * 255)},${Math.round(o.b * 255)})`;
      };
      for (let i = 0; i <= 10; i++) g.addColorStop(i / 10, col(i / 10));
      c.fillStyle = g; c.fillRect(0, 0, W, HZ + 2);
      // below horizon: hold the horizon tone, gently darkened (env floor)
      const gl = c.createLinearGradient(0, HZ, 0, H);
      gl.addColorStop(0, col(1));
      const dk = cB.clone().multiplyScalar(0.72);
      gl.addColorStop(1, `rgb(${Math.round(dk.r * 255)},${Math.round(dk.g * 255)},${Math.round(dk.b * 255)})`);
      c.fillStyle = gl; c.fillRect(0, HZ, W, H - HZ);
      // warm scattering haze hugging the horizon
      const warm = cB.clone().lerp(new THREE.Color('#fff2dc'), 0.55);
      const hz = c.createLinearGradient(0, HZ - H * 0.14, 0, HZ);
      hz.addColorStop(0, 'rgba(255,244,224,0)');
      hz.addColorStop(1, `rgba(${Math.round(warm.r * 255)},${Math.round(warm.g * 255)},${Math.round(warm.b * 255)},.5)`);
      c.fillStyle = hz; c.fillRect(0, HZ - H * 0.14, W, H * 0.14);
      // milky secondary haze higher up (aerial perspective)
      const hz2 = c.createLinearGradient(0, HZ - H * 0.3, 0, HZ);
      hz2.addColorStop(0, 'rgba(255,255,255,0)');
      hz2.addColorStop(1, 'rgba(255,255,255,.14)');
      c.fillStyle = hz2; c.fillRect(0, HZ - H * 0.3, W, H * 0.3);
      // fine dither so the gradient never bands
      for (let i = 0; i < 2400; i++) {
        c.fillStyle = `rgba(${Math.random() < 0.5 ? '255,255,255' : '0,0,0'},0.022)`;
        c.fillRect(rand(0, W), rand(0, H), 1.5, 1.5);
      }
    });
    return this.texs[key];
  },
  // cumulus puff: soft-lobed silhouette, bright crown, shaded gray-blue base
  cloudPuffTex(v) {
    const key = 'cloudP' + v;
    if (this.texs[key]) return this.texs[key];
    this.texs[key] = this.canvas(256, 160, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      const baseY = H * 0.72, ph = v * 4.7 + 1.3;
      const n = 7 + v * 2;
      for (let i = 0; i < n; i++) {
        const t = (i + 0.5) / n;
        const dome = Math.sin(t * Math.PI);
        const r = H * 0.13 + dome * H * 0.21 * (0.65 + 0.5 * Math.abs(Math.sin(i * 2.63 + ph)));
        const x = W * (0.14 + 0.72 * t) + Math.sin(i * 3.1 + ph) * W * 0.02;
        const y = baseY - r * 0.6 - Math.abs(Math.sin(i * 1.87 + ph)) * H * 0.16 * dome;
        const g = c.createRadialGradient(x, y, r * 0.15, x, y, r);
        g.addColorStop(0, 'rgba(255,255,255,.96)');
        g.addColorStop(0.72, 'rgba(255,255,255,.6)');
        g.addColorStop(1, 'rgba(255,255,255,0)');
        c.fillStyle = g;
        c.beginPath(); c.arc(x, y, r, 0, TAU); c.fill();
      }
      // flat-ish base lobes hugging baseY
      for (let i = 0; i < 4; i++) {
        const x = W * (0.24 + 0.52 * (i / 3)), r = H * rand(0.13, 0.19);
        const g = c.createRadialGradient(x, baseY - r * 0.4, r * 0.2, x, baseY - r * 0.4, r);
        g.addColorStop(0, 'rgba(255,255,255,.9)');
        g.addColorStop(1, 'rgba(255,255,255,0)');
        c.fillStyle = g;
        c.beginPath(); c.arc(x, baseY - r * 0.4, r, 0, TAU); c.fill();
      }
      // shade: sunlit crown → cool shadowed underbelly (only where cloud exists)
      c.globalCompositeOperation = 'source-atop';
      const sh = c.createLinearGradient(0, H * 0.08, 0, baseY + H * 0.08);
      sh.addColorStop(0, 'rgba(255,253,246,0)');
      sh.addColorStop(0.55, 'rgba(214,222,238,.28)');
      sh.addColorStop(0.85, 'rgba(148,164,196,.62)');
      sh.addColorStop(1, 'rgba(120,136,172,.78)');
      c.fillStyle = sh; c.fillRect(0, 0, W, H);
      c.globalCompositeOperation = 'source-over';
    });
    return this.texs[key];
  },
  // high thin cirrus streaks
  cirrusTex() {
    if (this.texs.cirrus) return this.texs.cirrus;
    this.texs.cirrus = this.canvas(512, 96, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      for (let s = 0; s < 3; s++) {
        const y0 = H * (0.25 + s * 0.25), amp = rand(4, 10);
        for (let i = 0; i < 130; i++) {
          const t = i / 130;
          const fade = Math.sin(t * Math.PI);
          c.fillStyle = `rgba(255,255,255,${0.05 + fade * 0.09})`;
          c.beginPath();
          c.ellipse(W * t, y0 + Math.sin(t * 9 + s * 3) * amp, rand(9, 22), rand(1.6, 3.4), 0, 0, TAU);
          c.fill();
        }
      }
    });
    return this.texs.cirrus;
  },
  // ---------- distant horizon silhouette, per district style ----------
  // styles: 'hills' | 'city' | 'nightcity' | 'mountains'
  skylineTexture(style, fogColor) {
    const key = 'skyline' + style + fogColor; // fog tint differs day vs night
    if (this.texs[key]) return this.texs[key];
    const W = 2048, H = 256;
    this.texs[key] = this.canvas(W, H, (c) => {
      c.clearRect(0, 0, W, H);
      const fc = new THREE.Color(fogColor);
      const shade = (mix, alpha) => {
        const col = fc.clone().lerp(new THREE.Color(0x1a2030), mix);
        return `rgba(${Math.round(col.r * 255)},${Math.round(col.g * 255)},${Math.round(col.b * 255)},${alpha})`;
      };
      if (style === 'hills') {
        // two layers of rolling hills + tiny trees + a water tower
        for (const [mix, base, amp, alpha] of [[0.18, 150, 34, 0.85], [0.38, 190, 26, 0.95]]) {
          c.fillStyle = shade(mix, alpha);
          c.beginPath();
          c.moveTo(0, H);
          for (let x = 0; x <= W; x += 16) {
            const y = base - Math.sin(x * 0.006 + mix * 40) * amp - Math.sin(x * 0.017 + mix * 9) * amp * 0.4;
            c.lineTo(x, y);
          }
          c.lineTo(W, H); c.closePath(); c.fill();
          // sparse distant trees on the ridge
          for (let x = 30; x < W; x += 90 + (x % 70)) {
            const y = base - Math.sin(x * 0.006 + mix * 40) * amp - Math.sin(x * 0.017 + mix * 9) * amp * 0.4;
            c.beginPath(); c.arc(x, y - 5, 6, 0, TAU); c.fill();
            c.fillRect(x - 1, y - 4, 2, 6);
          }
        }
        // water tower
        c.fillStyle = shade(0.45, 0.95);
        c.fillRect(600, 128, 5, 46); c.fillRect(636, 128, 5, 46);
        c.beginPath(); c.ellipse(620, 122, 26, 16, 0, 0, TAU); c.fill();
      } else if (style === 'city' || style === 'nightcity') {
        for (const [mix, base, alpha] of [[0.22, 108, 0.8], [0.5, 148, 0.95]]) {
          c.fillStyle = shade(style === 'nightcity' ? mix + 0.35 : mix, alpha);
          let x = 0;
          while (x < W) {
            const bw = 34 + ((x * 7919) % 60), bh = 30 + ((x * 104729) % 110);
            c.fillRect(x, base + (mix * 100) - bh, bw, bh + 200);
            // antenna on some towers
            if ((x % 5) < 2) c.fillRect(x + bw / 2, base + (mix * 100) - bh - 14, 2, 14);
            x += bw + 8;
          }
        }
        if (style === 'nightcity') {
          // lit windows
          c.fillStyle = 'rgba(255,214,122,.55)';
          for (let i = 0; i < 700; i++) {
            const x = (i * 131) % W, y = 96 + ((i * 61) % 140);
            if ((i * 17) % 10 < 4) c.fillRect(x, y, 2.4, 3.2);
          }
        }
      } else { // mountains
        for (const [mix, base, amp, alpha] of [[0.2, 130, 60, 0.85], [0.45, 175, 46, 0.95]]) {
          c.fillStyle = shade(mix, alpha);
          c.beginPath();
          c.moveTo(0, H);
          for (let x = 0; x <= W; x += 46) {
            c.lineTo(x, base - Math.abs(Math.sin(x * 0.011 + mix * 30)) * amp - ((x * 7919) % 23));
          }
          c.lineTo(W, H); c.closePath(); c.fill();
        }
        // rim light on the far ridge
        c.strokeStyle = 'rgba(255,154,92,.4)';
        c.lineWidth = 2.5;
        c.beginPath();
        for (let x = 0; x <= W; x += 46) {
          const y = 130 - Math.abs(Math.sin(x * 0.011 + 6)) * 60 - ((x * 7919) % 23);
          x === 0 ? c.moveTo(x, y) : c.lineTo(x, y);
        }
        c.stroke();
      }
    });
    this.texs[key].wrapS = THREE.RepeatWrapping;
    return this.texs[key];
  },

  // ---------- moon with craters ----------
  moonTexture() {
    if (this.texs.moon) return this.texs.moon;
    this.texs.moon = this.canvas(128, 128, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      const g = c.createRadialGradient(52, 48, 6, 64, 64, 56);
      g.addColorStop(0, '#f4f6ff');
      g.addColorStop(0.75, '#c9d2e8');
      g.addColorStop(1, '#8f9ab8');
      c.fillStyle = g;
      c.beginPath(); c.arc(64, 64, 54, 0, TAU); c.fill();
      c.fillStyle = 'rgba(120,132,160,.4)';
      for (const [x, y, r] of [[46, 44, 9], [78, 66, 12], [58, 88, 7], [88, 38, 5], [38, 72, 6]]) {
        c.beginPath(); c.arc(x, y, r, 0, TAU); c.fill();
      }
    });
    return this.texs.moon;
  },

  spotTexture() {
    if (this.texs.spot) return this.texs.spot;
    this.texs.spot = this.canvas(256, 512, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      c.strokeStyle = '#4de6a0'; c.lineWidth = 14; c.setLineDash([36, 22]);
      c.strokeRect(12, 12, W - 24, H - 24);
      c.setLineDash([]);
      c.fillStyle = 'rgba(77,230,160,.16)';
      c.fillRect(12, 12, W - 24, H - 24);
      c.fillStyle = '#4de6a0';
      c.font = '900 130px "Baloo 2", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText('P', W / 2, H / 2);
    });
    return this.texs.spot;
  },
  radialSprite(color, edge) {
    const key = `rad${color}`;
    if (this.texs[key]) return this.texs[key];
    this.texs[key] = this.canvas(64, 64, (c, W, H) => {
      const g = c.createRadialGradient(W / 2, H / 2, 2, W / 2, H / 2, W / 2);
      g.addColorStop(0, color);
      g.addColorStop(1, edge || 'rgba(0,0,0,0)');
      c.fillStyle = g;
      c.fillRect(0, 0, W, H);
    });
    return this.texs[key];
  },
  // ---------- tire tread: wraps around wheel circumference ----------
  tireTreadTex() {
    if (this.texs.tread) return this.texs.tread;
    const tex = this.canvas(64, 32, (c, W, H) => {
      c.fillStyle = '#17181c'; c.fillRect(0, 0, W, H);
      // tread blocks (grooves parallel to axle)
      c.fillStyle = '#0b0c0f';
      for (let x = 0; x < W; x += 8) c.fillRect(x, 0, 3, H);
      // circumferential grooves
      c.fillRect(0, H * 0.3, W, 2);
      c.fillRect(0, H * 0.64, W, 2);
      // edge highlights on blocks
      c.fillStyle = 'rgba(255,255,255,.06)';
      for (let x = 3; x < W; x += 8) c.fillRect(x, 0, 1, H);
    });
    tex.wrapS = THREE.RepeatWrapping;
    tex.repeat.set(14, 1);
    this.texs.tread = tex;
    return tex;
  },

  // ---------- wheel face: tire sidewall + alloy rim + lugs ----------
  wheelFaceTex() {
    if (this.texs.wface) return this.texs.wface;
    this.texs.wface = this.canvas(128, 128, (c, W, H) => {
      const cx = W / 2, cy = H / 2;
      // tire rubber
      c.fillStyle = '#17181c';
      c.fillRect(0, 0, W, H);
      // sidewall shading rings
      let g = c.createRadialGradient(cx, cy, 34, cx, cy, 64);
      g.addColorStop(0, '#1f2127');
      g.addColorStop(0.55, '#15161a');
      g.addColorStop(1, '#0e0f12');
      c.fillStyle = g;
      c.beginPath(); c.arc(cx, cy, 64, 0, TAU); c.fill();
      // sidewall lettering ring (faint dashes)
      c.strokeStyle = 'rgba(200,205,215,.16)';
      c.lineWidth = 3;
      for (let i = 0; i < 22; i++) {
        const a0 = i / 22 * TAU, a1 = a0 + 0.14;
        c.beginPath(); c.arc(cx, cy, 51, a0, a1); c.stroke();
      }
      // rim dish
      g = c.createRadialGradient(cx - 8, cy - 10, 4, cx, cy, 38);
      g.addColorStop(0, '#f2f4f8');
      g.addColorStop(0.5, '#c3c9d4');
      g.addColorStop(0.85, '#8d94a3');
      g.addColorStop(1, '#5c6472');
      c.fillStyle = g;
      c.beginPath(); c.arc(cx, cy, 38, 0, TAU); c.fill();
      // rim lip
      c.strokeStyle = '#f6f8fb'; c.lineWidth = 2.5;
      c.beginPath(); c.arc(cx, cy, 37, 0, TAU); c.stroke();
      c.strokeStyle = 'rgba(40,44,52,.8)'; c.lineWidth = 2;
      c.beginPath(); c.arc(cx, cy, 39.5, 0, TAU); c.stroke();
      // spoke gaps (6 dark wedges)
      c.fillStyle = '#1c1e24';
      for (let i = 0; i < 6; i++) {
        const a = i / 6 * TAU;
        c.beginPath();
        c.arc(cx, cy, 33, a + 0.16, a + TAU / 6 - 0.16);
        c.arc(cx, cy, 13, a + TAU / 6 - 0.2, a + 0.2, true);
        c.closePath(); c.fill();
      }
      // spoke edge highlights
      c.strokeStyle = 'rgba(255,255,255,.35)'; c.lineWidth = 1.5;
      for (let i = 0; i < 6; i++) {
        const a = i / 6 * TAU + 0.1;
        c.beginPath();
        c.moveTo(cx + Math.cos(a) * 14, cy + Math.sin(a) * 14);
        c.lineTo(cx + Math.cos(a) * 32, cy + Math.sin(a) * 32);
        c.stroke();
      }
      // center cap
      g = c.createRadialGradient(cx - 2, cy - 3, 1, cx, cy, 10);
      g.addColorStop(0, '#ffffff');
      g.addColorStop(0.7, '#aeb5c2');
      g.addColorStop(1, '#6e7583');
      c.fillStyle = g;
      c.beginPath(); c.arc(cx, cy, 10, 0, TAU); c.fill();
      c.strokeStyle = '#3a3f4a'; c.lineWidth = 1.5;
      c.beginPath(); c.arc(cx, cy, 10, 0, TAU); c.stroke();
      // lug nuts
      c.fillStyle = '#4a505c';
      for (let i = 0; i < 5; i++) {
        const a = i / 5 * TAU - Math.PI / 2;
        c.beginPath(); c.arc(cx + Math.cos(a) * 15.5, cy + Math.sin(a) * 15.5, 2.6, 0, TAU); c.fill();
      }
    });
    return this.texs.wface;
  },

  // ---------- bark: vertical fissured streaks ----------
  barkTex() {
    if (this.texs.bark) return this.texs.bark;
    this.texs.bark = this.canvas(64, 128, (c, W, H) => {
      c.fillStyle = '#63452c'; c.fillRect(0, 0, W, H);
      for (let i = 0; i < 46; i++) {
        const x = rand(0, W), y0 = rand(-20, H), len = rand(24, 70), w = rand(1.5, 4.5);
        c.strokeStyle = Math.random() < 0.55 ? `rgba(38,26,15,${rand(0.35, 0.7)})` : `rgba(150,115,76,${rand(0.25, 0.5)})`;
        c.lineWidth = w;
        c.beginPath();
        c.moveTo(x, y0);
        c.quadraticCurveTo(x + rand(-5, 5), y0 + len / 2, x + rand(-4, 4), y0 + len);
        c.stroke();
      }
      // horizontal cracks
      c.strokeStyle = 'rgba(30,20,12,.5)'; c.lineWidth = 1.5;
      for (let i = 0; i < 7; i++) {
        const y = rand(0, H);
        c.beginPath(); c.moveTo(rand(0, W / 2), y); c.lineTo(rand(W / 2, W), y + rand(-4, 4)); c.stroke();
      }
    });
    this.texs.bark.wrapS = this.texs.bark.wrapT = THREE.RepeatWrapping;
    return this.texs.bark;
  },

  // ---------- foliage: bright mottled speckle (tinted by material color) ----------
  leafTex() {
    if (this.texs.leaf) return this.texs.leaf;
    this.texs.leaf = this.canvas(128, 128, (c, W, H) => {
      c.fillStyle = '#dfe6d2'; c.fillRect(0, 0, W, H);
      for (let i = 0; i < 520; i++) {
        const r = rand(2, 6);
        c.fillStyle = Math.random() < 0.5 ? `rgba(96,120,74,${rand(0.12, 0.3)})` : `rgba(255,255,255,${rand(0.1, 0.26)})`;
        c.beginPath();
        c.ellipse(rand(0, W), rand(0, H), r, r * rand(0.5, 0.9), rand(0, TAU), 0, TAU);
        c.fill();
      }
    });
    this.texs.leaf.wrapS = this.texs.leaf.wrapT = THREE.RepeatWrapping;
    return this.texs.leaf;
  },

  // foliage clump card: ragged leaf-cluster silhouette with alpha, grayscale
  // green-ish so the material color tints it (bright crown, dark underside)
  foliageCardTex(v) {
    const key = 'fcard' + v;
    if (this.texs[key]) return this.texs[key];
    this.texs[key] = this.canvas(128, 128, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      for (let i = 0; i < 300; i++) {
        const a = rand(0, TAU), rr = Math.pow(Math.random(), 0.55) * 0.4;
        const x = W / 2 + Math.cos(a + v) * rr * W;
        const y = H / 2 + Math.sin(a + v) * rr * H * 0.88;
        const r = rand(3, 8) * (1 - rr * 0.6);
        const sh = Math.round(150 + (0.5 - y / H) * 140 + rand(-26, 26));
        c.fillStyle = `rgba(${sh},${sh + 12},${Math.round(sh * 0.82)},${rand(0.8, 1)})`;
        c.beginPath();
        c.ellipse(x, y, r, r * rand(0.55, 1), rand(0, TAU), 0, TAU);
        c.fill();
      }
    });
    return this.texs[key];
  },
  // small grass tuft (alpha card, tinted by material color)
  grassTuftTex() {
    if (this.texs.tuft) return this.texs.tuft;
    this.texs.tuft = this.canvas(64, 40, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      for (let i = 0; i < 24; i++) {
        const x0 = W / 2 + rand(-16, 16), lean = rand(-11, 11), len = rand(13, 30);
        const sh = Math.round(rand(115, 235));
        c.strokeStyle = `rgba(${sh},${sh + 18},${Math.round(sh * 0.62)},${rand(0.8, 1)})`;
        c.lineWidth = rand(1.2, 2.5);
        c.lineCap = 'round';
        c.beginPath();
        c.moveTo(x0, H);
        c.quadraticCurveTo(x0 + lean * 0.4, H - len * 0.6, x0 + lean, H - len);
        c.stroke();
      }
      c.lineCap = 'butt';
    });
    return this.texs.tuft;
  },

  // ---------- wood planks ----------
  woodTex() {
    if (this.texs.wood) return this.texs.wood;
    this.texs.wood = this.canvas(128, 64, (c, W, H) => {
      c.fillStyle = '#8a5a3a'; c.fillRect(0, 0, W, H);
      for (let i = 0; i < 40; i++) {
        c.strokeStyle = Math.random() < 0.5 ? `rgba(60,35,18,${rand(0.15, 0.4)})` : `rgba(200,150,100,${rand(0.12, 0.3)})`;
        c.lineWidth = rand(0.8, 2.4);
        const y = rand(0, H);
        c.beginPath();
        c.moveTo(0, y);
        c.bezierCurveTo(W * 0.3, y + rand(-4, 4), W * 0.7, y + rand(-4, 4), W, y + rand(-3, 3));
        c.stroke();
      }
      // knots
      for (let i = 0; i < 3; i++) {
        const x = rand(10, W - 10), y = rand(8, H - 8);
        c.strokeStyle = 'rgba(60,35,18,.5)'; c.lineWidth = 1.5;
        c.beginPath(); c.ellipse(x, y, rand(3, 6), rand(2, 4), 0, 0, TAU); c.stroke();
      }
    });
    this.texs.wood.wrapS = THREE.RepeatWrapping;
    return this.texs.wood;
  },

  // ---------- license plate ----------
  plateTex() {
    if (this.texs.plate) return this.texs.plate;
    this.texs.plate = this.canvas(64, 24, (c, W, H) => {
      c.fillStyle = '#f2f4ee'; c.fillRect(0, 0, W, H);
      c.strokeStyle = '#39415c'; c.lineWidth = 3;
      c.strokeRect(1.5, 1.5, W - 3, H - 3);
      c.fillStyle = '#2b3350';
      c.font = '900 13px "Baloo 2", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText('PN·3D', W / 2, H / 2 + 1);
    });
    return this.texs.plate;
  },

  // ---------- grille: dark with chrome slats ----------
  grilleTex() {
    if (this.texs.grille) return this.texs.grille;
    this.texs.grille = this.canvas(64, 24, (c, W, H) => {
      c.fillStyle = '#101216'; c.fillRect(0, 0, W, H);
      c.fillStyle = 'rgba(190,198,212,.55)';
      for (let y = 3; y < H; y += 6) c.fillRect(2, y, W - 4, 1.6);
      c.strokeStyle = '#2c313c'; c.lineWidth = 2;
      c.strokeRect(1, 1, W - 2, H - 2);
    });
    return this.texs.grille;
  },

  // ---------- horizontal clapboard siding (tinted by material color) ----------
  sidingTex() {
    if (this.texs.siding) return this.texs.siding;
    this.texs.siding = this.canvas(128, 128, (c, W, H) => {
      c.fillStyle = '#ffffff'; c.fillRect(0, 0, W, H);
      for (let y = 0; y < H; y += 14) {
        // board shadow line + subtle top highlight
        c.fillStyle = 'rgba(0,0,0,.16)';
        c.fillRect(0, y + 11, W, 3);
        c.fillStyle = 'rgba(255,255,255,.5)';
        c.fillRect(0, y, W, 1.5);
      }
      for (let i = 0; i < 160; i++) {
        c.fillStyle = `rgba(0,0,0,${rand(0.01, 0.05)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 4), rand(1, 2));
      }
    });
    this.texs.siding.wrapS = this.texs.siding.wrapT = THREE.RepeatWrapping;
    return this.texs.siding;
  },

  // ---------- hazard stripes (speed bumps) ----------
  hazardTex() {
    if (this.texs.hazard) return this.texs.hazard;
    this.texs.hazard = this.canvas(32, 64, (c, W, H) => {
      c.fillStyle = '#e8b83a'; c.fillRect(0, 0, W, H);
      c.fillStyle = '#23252c';
      c.fillRect(0, 0, W, H / 2);
    });
    this.texs.hazard.wrapS = this.texs.hazard.wrapT = THREE.RepeatWrapping;
    return this.texs.hazard;
  },

  emojiTexture(emo) {
    const key = `emo${emo}`;
    if (this.texs[key]) return this.texs[key];
    this.texs[key] = this.canvas(96, 96, (c, W, H) => {
      c.font = '72px sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText(emo, W / 2, H / 2 + 6);
    });
    return this.texs[key];
  },
  textTexture(text, color, stroke) {
    const cv = document.createElement('canvas');
    cv.width = 512; cv.height = 160;
    const c = cv.getContext('2d');
    c.font = '900 88px "Baloo 2", "Comic Sans MS", sans-serif';
    c.textAlign = 'center'; c.textBaseline = 'middle';
    c.lineWidth = 14; c.lineJoin = 'round';
    c.strokeStyle = stroke || '#2b2d36';
    c.strokeText(text, 256, 84);
    c.fillStyle = color || '#ffc23e';
    c.fillText(text, 256, 84);
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    return tex;
  },

  // ---------- water: tiling wave streaks for the marina ----------
  waterTex() {
    const tex = this.canvas(256, 256, (c, W, H) => {
      const g = c.createLinearGradient(0, 0, 0, H);
      g.addColorStop(0, '#1e6fa4'); g.addColorStop(0.5, '#1a679c'); g.addColorStop(1, '#1e6fa4');
      c.fillStyle = g; c.fillRect(0, 0, W, H);
      // layered horizontal wave crests, brighter toward random peaks
      for (let i = 0; i < 90; i++) {
        const y = rand(0, H), w = rand(20, 90), a = rand(0.05, 0.2);
        c.strokeStyle = `rgba(190,230,250,${a})`;
        c.lineWidth = rand(1, 2.4);
        c.beginPath();
        const x0 = rand(0, W);
        c.moveTo(x0, y);
        c.quadraticCurveTo(x0 + w / 2, y - rand(1, 4), x0 + w, y);
        c.stroke();
      }
      for (let i = 0; i < 34; i++) { // dark troughs
        const y = rand(0, H);
        c.strokeStyle = `rgba(10,40,70,${rand(0.08, 0.2)})`;
        c.lineWidth = rand(1.5, 3);
        c.beginPath(); c.moveTo(rand(0, W), y); c.lineTo(rand(0, W), y + rand(-2, 2)); c.stroke();
      }
    });
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    return tex;
  },

  // ---------- aurora curtain: vertical streaks, green -> violet ----------
  auroraTex() {
    if (this.texs.aurora) return this.texs.aurora;
    const tex = this.canvas(512, 256, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      for (let i = 0; i < 90; i++) {
        const x = rand(0, W), w = rand(4, 26);
        const hue = pick([140, 155, 170, 185, 275]);
        const g = c.createLinearGradient(0, 0, 0, H);
        g.addColorStop(0, `hsla(${hue},90%,70%,0)`);
        g.addColorStop(rand(0.15, 0.35), `hsla(${hue},95%,66%,${rand(0.1, 0.3)})`);
        g.addColorStop(rand(0.55, 0.75), `hsla(${hue + 30},85%,60%,${rand(0.05, 0.16)})`);
        g.addColorStop(1, 'hsla(280,80%,60%,0)');
        c.fillStyle = g;
        c.fillRect(x - w / 2, 0, w, H);
      }
    });
    tex.wrapS = THREE.RepeatWrapping;
    this.texs.aurora = tex;
    return tex;
  },

  // ---------- lighthouse: red/white bands ----------
  lightStripeTex() {
    if (this.texs.lhStripe) return this.texs.lhStripe;
    const tex = this.canvas(64, 256, (c, W, H) => {
      for (let i = 0; i < 5; i++) {
        c.fillStyle = i % 2 === 0 ? '#f4f1ea' : '#d9403a';
        c.fillRect(0, (i / 5) * H, W, H / 5 + 1);
      }
      for (let i = 0; i < 160; i++) { // weathering
        c.fillStyle = `rgba(90,70,60,${rand(0.03, 0.1)})`;
        c.fillRect(rand(0, W), rand(0, H), rand(1, 3), rand(2, 8));
      }
    });
    this.texs.lhStripe = tex;
    return tex;
  },

  // ---------- distant gliding bird silhouette ----------
  gullTex() {
    if (this.texs.gull) return this.texs.gull;
    const tex = this.canvas(64, 40, (c) => {
      c.clearRect(0, 0, 64, 40);
      c.strokeStyle = 'rgba(40,44,56,.95)';
      c.lineWidth = 4.5; c.lineCap = 'round';
      c.beginPath();
      c.moveTo(6, 26); c.quadraticCurveTo(20, 10, 32, 22);
      c.quadraticCurveTo(44, 10, 58, 26);
      c.stroke();
    });
    this.texs.gull = tex;
    return tex;
  },
};

// ============================================================
// CAR FACTORY — low-poly cute vehicles
// refs: { wheels:[], steerL, steerR, brakeLights, headlights, extra... }
// ============================================================
const CarFactory = {
  wheelGeo: null, wheelCapGeo: null,

  tireSideMat() {
    if (!Assets.mats.tireSide) Assets.mats.tireSide = new THREE.MeshLambertMaterial({ map: Assets.tireTreadTex() });
    return Assets.mats.tireSide;
  },
  wheelFaceMat() {
    if (!Assets.mats.wheelFace) Assets.mats.wheelFace = new THREE.MeshStandardMaterial({ map: Assets.wheelFaceTex(), roughness: 0.5, metalness: 0.5, envMapIntensity: 0.6 });
    return Assets.mats.wheelFace;
  },
  // Group -> spin Group (children[0], axle = local Z) -> tire mesh.
  // Vehicle/traffic code spins children[0].rotation.z, so the spin group
  // must stay unrotated: the axle alignment lives on the mesh inside it.
  wheel(r, w) {
    const g = new THREE.Group();
    const spin = new THREE.Group();
    const tire = new THREE.Mesh(
      Assets.geo(`wheel${r}_${w}`, () => new THREE.CylinderGeometry(r, r, w, 24, 1)),
      [this.tireSideMat(), this.wheelFaceMat(), this.wheelFaceMat()]
    );
    tire.rotation.x = Math.PI / 2;
    tire.castShadow = true;
    spin.add(tire);
    g.add(spin);
    g.userData.r = r; // wheel radius for physically correct roll rate
    return g;
  },

  boxMesh(l, h, w, color, opts) {
    // geometry cached by dims; material shared per color when no opts —
    // callers that mutate materials (flashers, bulbs) pass opts or use
    // dedicated creators, so shared mats are never written to
    const geo = Assets.geo(`bxm${l}_${h}_${w}`, () => new THREE.BoxGeometry(l, h, w));
    let mat;
    if (opts) {
      mat = new THREE.MeshLambertMaterial(Object.assign({ color }, opts));
    } else {
      const mk = 'lamC' + color;
      mat = Assets.mats[mk] || (Assets.mats[mk] = new THREE.MeshLambertMaterial({ color }));
    }
    const m = new THREE.Mesh(geo, mat);
    m.castShadow = true;
    return m;
  },

  // ---- smooth hull: slowroads-style body generator ----
  // A densely segmented box deformed by a cross-section superellipse
  // (rounded sills/roof corners), a plan-view superellipse (rounded
  // nose/tail), a roofline curve, end taper and tumblehome — then smooth
  // vertex normals. BoxGeometry is indexed, so computeVertexNormals shades
  // the whole shell seamlessly: no facets, no panel gaps.
  // o.key must uniquely identify the SHAPE (top/bot curves aren't hashable).
  hull(len, hgt, wid, o) {
    return Assets.geo(`hull_${o.key}`, () => {
      const geo = new THREE.BoxGeometry(1, 1, 1, 28, 8, 12);
      const pos = geo.attributes.position;
      const pC = o.pCross || 3.2, pP = o.pPlan || 5.0;
      const top = o.top || (() => 1), bot = o.bot || (() => 0);
      const tumble = o.tumble !== undefined ? o.tumble : 0.14;
      const wNose = o.wNose !== undefined ? o.wNose : 0.88;
      const wTail = o.wTail !== undefined ? o.wTail : 0.93;
      for (let i = 0; i < pos.count; i++) {
        let qx = pos.getX(i) * 2, qy = pos.getY(i) * 2, qz = pos.getZ(i) * 2; // [-1,1]
        // plan-view corner rounding (nose/tail)
        let m = Math.max(Math.abs(qx), Math.abs(qz));
        if (m > 1e-4) {
          const pn = Math.pow(Math.pow(Math.abs(qx), pP) + Math.pow(Math.abs(qz), pP), 1 / pP);
          const k = m / pn; qx *= k; qz *= k;
        }
        // cross-section corner rounding (sills, roof edges)
        m = Math.max(Math.abs(qy), Math.abs(qz));
        if (m > 1e-4) {
          const pn = Math.pow(Math.pow(Math.abs(qy), pC) + Math.pow(Math.abs(qz), pC), 1 / pC);
          const k = m / pn; qy *= k; qz *= k;
        }
        const u = qx * 0.5 + 0.5; // 0 = tail, 1 = nose
        // taper the ends in plan
        qz *= lerp(wTail, 1, clamp(u / 0.35, 0, 1)) * lerp(wNose, 1, clamp((1 - u) / 0.35, 0, 1));
        // tumblehome: the upper body leans inward like real sheet metal
        const y01 = qy * 0.5 + 0.5;
        qz *= 1 - tumble * Math.pow(Math.max(0, y01 - 0.35) / 0.65, 1.6);
        // roofline + floor curves
        const yy = bot(u) + y01 * (top(u) - bot(u));
        pos.setXYZ(i, qx * 0.5 * len, (yy - 0.5) * hgt, qz * 0.5 * wid);
      }
      geo.computeVertexNormals();
      return geo;
    });
  },

  // flush cylindrical bar (light bars, bumper lips) lying across the car
  bar(w, r, mat) {
    const mesh = new THREE.Mesh(
      Assets.geo(`bar${w}_${r}`, () => new THREE.CylinderGeometry(r, r, w, 10)),
      mat
    );
    mesh.rotation.x = Math.PI / 2;
    return mesh;
  },

  lightBox(l, h, w, color) {
    const m = new THREE.Mesh(
      new THREE.BoxGeometry(l, h, w),
      new THREE.MeshLambertMaterial({ color: 0x222222, emissive: new THREE.Color(color), emissiveIntensity: 1 })
    );
    return m;
  },

  windowMat() {
    return Assets.lambert('window', { color: 0x9fd8f0 });
  },
  // ---- baked car-body detail textures (per box-face) ----
  // Panel seams, door handles, character lines, rocker + arch AO: painted
  // per color+style and cached. This is what turns a colored blob into a car.
  _carCss(color, f) { // css color, optionally lightened (f>0) / darkened (f<0)
    const c = new THREE.Color(color);
    if (f > 0) c.lerp(new THREE.Color(0xffffff), f);
    if (f < 0) c.lerp(new THREE.Color(0x000000), -f);
    return '#' + c.getHexString();
  },
  carSideTex(color, van) {
    const key = `cside${color}_${van ? 1 : 0}`;
    if (Assets.texs[key]) return Assets.texs[key];
    const tex = Assets.canvas(512, 256, (c, W, H) => {
      // base + vertical sky-to-ground shading
      const g = c.createLinearGradient(0, 0, 0, H);
      g.addColorStop(0, this._carCss(color, 0.16));
      g.addColorStop(0.45, this._carCss(color, 0));
      g.addColorStop(1, this._carCss(color, -0.28));
      c.fillStyle = g; c.fillRect(0, 0, W, H);
      // soft reflection streak
      const rg = c.createLinearGradient(0, H * 0.2, 0, H * 0.42);
      rg.addColorStop(0, 'rgba(255,255,255,0)'); rg.addColorStop(0.5, 'rgba(255,255,255,.10)'); rg.addColorStop(1, 'rgba(255,255,255,0)');
      c.fillStyle = rg; c.fillRect(0, H * 0.2, W, H * 0.24);
      // character line (highlight over shadow)
      c.fillStyle = 'rgba(255,255,255,.16)'; c.fillRect(0, H * 0.4, W, 2);
      c.fillStyle = 'rgba(0,0,0,.2)'; c.fillRect(0, H * 0.4 + 2, W, 3);
      // rocker panel
      c.fillStyle = 'rgba(0,0,0,.5)'; c.fillRect(0, H * 0.9, W, H * 0.1);
      // wheel arch AO (arches sit near the ends)
      for (const ax of [0.2, 0.8]) {
        const gg = c.createRadialGradient(W * ax, H * 1.02, H * 0.1, W * ax, H * 1.02, H * 0.42);
        gg.addColorStop(0, 'rgba(0,0,0,.55)'); gg.addColorStop(1, 'rgba(0,0,0,0)');
        c.fillStyle = gg; c.fillRect(W * ax - H * 0.45, H * 0.55, H * 0.9, H * 0.45);
      }
      // door seams + handles
      const seams = van ? [0.42] : [0.4, 0.63];
      c.strokeStyle = 'rgba(0,0,0,.42)'; c.lineWidth = 2;
      for (const sx of seams) {
        c.beginPath(); c.moveTo(W * sx, H * 0.18); c.lineTo(W * sx, H * 0.88); c.stroke();
      }
      c.fillStyle = 'rgba(0,0,0,.5)';
      for (const sx of seams) c.fillRect(W * (sx + 0.02), H * 0.34, 26, 6);
      c.fillStyle = 'rgba(255,255,255,.18)';
      for (const sx of seams) c.fillRect(W * (sx + 0.02), H * 0.33, 26, 2);
    });
    Assets.texs[key] = tex;
    return tex;
  },
  carEndTex(color, rear) {
    const key = `end${color}_${rear ? 'r' : 'f'}`;
    if (Assets.texs[key]) return Assets.texs[key];
    const tex = Assets.canvas(256, 256, (c, W, H) => {
      const g = c.createLinearGradient(0, 0, 0, H);
      g.addColorStop(0, this._carCss(color, 0.12));
      g.addColorStop(1, this._carCss(color, -0.3));
      c.fillStyle = g; c.fillRect(0, 0, W, H);
      // lower valance / diffuser
      c.fillStyle = 'rgba(0,0,0,.55)';
      c.fillRect(W * 0.06, H * 0.72, W * 0.88, H * 0.28);
      if (rear) {
        c.fillStyle = 'rgba(0,0,0,.3)'; c.fillRect(W * 0.08, H * 0.3, W * 0.84, 4); // trunk seam
      } else {
        c.fillStyle = 'rgba(0,0,0,.45)'; // grille + intakes
        c.fillRect(W * 0.3, H * 0.42, W * 0.4, H * 0.16);
        c.fillRect(W * 0.08, H * 0.56, W * 0.14, H * 0.12);
        c.fillRect(W * 0.78, H * 0.56, W * 0.14, H * 0.12);
      }
    });
    Assets.texs[key] = tex;
    return tex;
  },
  carTopTex(color) {
    const key = `top${color}`;
    if (Assets.texs[key]) return Assets.texs[key];
    const tex = Assets.canvas(256, 256, (c, W, H) => {
      c.fillStyle = this._carCss(color, 0.1); c.fillRect(0, 0, W, H);
      const rg = c.createRadialGradient(W / 2, H / 2, W * 0.1, W / 2, H / 2, W * 0.72);
      rg.addColorStop(0, 'rgba(255,255,255,.1)'); rg.addColorStop(1, 'rgba(0,0,0,.24)');
      c.fillStyle = rg; c.fillRect(0, 0, W, H);
      c.fillStyle = 'rgba(0,0,0,.14)'; c.fillRect(0, H * 0.47, W, 3); // hood crease
    });
    Assets.texs[key] = tex;
    return tex;
  },
  // greenhouse: black pillars + separate glass panes with baked sky streak
  canopySideTex() {
    if (Assets.texs.canSide) return Assets.texs.canSide;
    const tex = Assets.canvas(512, 128, (c, W, H) => {
      c.fillStyle = '#0b0f14'; c.fillRect(0, 0, W, H); // pillar black
      const pane = (x0, x1) => {
        const g = c.createLinearGradient(0, 0, 0, H);
        g.addColorStop(0, '#3d4f63'); g.addColorStop(0.35, '#22303f'); g.addColorStop(1, '#0e151d');
        c.fillStyle = g;
        c.beginPath();
        if (c.roundRect) c.roundRect(W * x0, H * 0.12, W * (x1 - x0), H * 0.82, 7);
        else c.rect(W * x0, H * 0.12, W * (x1 - x0), H * 0.82);
        c.fill();
        c.fillStyle = 'rgba(255,255,255,.14)';
        c.fillRect(W * x0 + 3, H * 0.16, W * (x1 - x0) - 6, 3);
      };
      pane(0.06, 0.44); pane(0.5, 0.72); pane(0.78, 0.95);
    });
    Assets.texs.canSide = tex;
    return tex;
  },
  canopyEndTex() {
    if (Assets.texs.canEnd) return Assets.texs.canEnd;
    const tex = Assets.canvas(256, 128, (c, W, H) => {
      c.fillStyle = '#0b0f14'; c.fillRect(0, 0, W, H); // pillar surround
      const g = c.createLinearGradient(0, 0, 0, H);
      g.addColorStop(0, '#46586c'); g.addColorStop(0.35, '#263442'); g.addColorStop(1, '#0f161e');
      c.fillStyle = g;
      c.beginPath();
      if (c.roundRect) c.roundRect(W * 0.06, H * 0.1, W * 0.88, H * 0.84, 9);
      else c.rect(W * 0.06, H * 0.1, W * 0.88, H * 0.84);
      c.fill();
      // sun strip + wiper sweep hint
      c.fillStyle = 'rgba(8,12,18,.85)'; c.fillRect(W * 0.06, H * 0.1, W * 0.88, H * 0.14);
      c.strokeStyle = 'rgba(255,255,255,.08)'; c.lineWidth = 5;
      c.beginPath(); c.arc(W * 0.35, H * 1.25, H * 0.75, -Math.PI * 0.78, -Math.PI * 0.3); c.stroke();
      c.beginPath(); c.arc(W * 0.72, H * 1.25, H * 0.7, -Math.PI * 0.8, -Math.PI * 0.35); c.stroke();
    });
    Assets.texs.canEnd = tex;
    return tex;
  },

  paint(color) {
    const key = 'paint' + color;
    // clearcoat gives the twin-highlight showroom sheen over the base coat
    if (!Assets.mats[key]) Assets.mats[key] = new THREE.MeshPhysicalMaterial({
      color, roughness: 0.38, metalness: 0.2, envMapIntensity: 0.9,
      clearcoat: 0.7, clearcoatRoughness: 0.12,
    });
    return Assets.mats[key];
  },
  glass() {
    if (!Assets.mats.carGlass) Assets.mats.carGlass = new THREE.MeshStandardMaterial({ color: 0x131c26, roughness: 0.05, metalness: 0.85, envMapIntensity: 1.3 });
    return Assets.mats.carGlass;
  },
  // six-material set for the hull's box groups: [nose, tail, top, bottom, side, side]
  // — each face carries its baked detail texture under the clearcoat
  carBodyMats(color, van) {
    const key = `bmats${color}_${van ? 1 : 0}`;
    if (Assets.mats[key]) return Assets.mats[key];
    const mk = map => new THREE.MeshPhysicalMaterial({
      map, roughness: 0.38, metalness: 0.2, envMapIntensity: 0.9,
      clearcoat: 0.7, clearcoatRoughness: 0.12,
    });
    const side = mk(this.carSideTex(color, van));
    Assets.mats[key] = [
      mk(this.carEndTex(color, false)),
      mk(this.carEndTex(color, true)),
      mk(this.carTopTex(color)),
      Assets.lambert('underbody', { color: 0x14151a }),
      side, side,
    ];
    return Assets.mats[key];
  },
  canopyMats() {
    if (Assets.mats.canMats) return Assets.mats.canMats;
    const mk = map => new THREE.MeshStandardMaterial({ map, roughness: 0.08, metalness: 0.8, envMapIntensity: 1.25 });
    const side = mk(this.canopySideTex());
    const end = mk(this.canopyEndTex());
    const roof = new THREE.MeshStandardMaterial({ color: 0x0d1117, roughness: 0.15, metalness: 0.7, envMapIntensity: 1.1 });
    Assets.mats.canMats = [end, end, roof, roof, side, side];
    return Assets.mats.canMats;
  },
  shadowBlob(len, wid) {
    const m = new THREE.Mesh(
      Assets.geo('ctShadow', () => new THREE.CircleGeometry(1, 20)),
      Assets.mats.ctShadow || (Assets.mats.ctShadow = new THREE.MeshBasicMaterial({
        map: Assets.radialSprite('rgba(0,0,0,.62)'), transparent: true, depthWrite: false,
      }))
    );
    m.rotation.x = -Math.PI / 2;
    m.scale.set(len * 0.62, wid * 0.8, 1);
    m.position.y = 0.015;
    m.renderOrder = 1;
    return m;
  },

  // standard car/van body. cfg: {len,wid,bodyH,cabLen,cabH,cabOff,body,roof,wheelR}
  // Normal-proportioned cars get a curved extruded silhouette + glass cabin;
  // tall/boxy vehicles (bus, ice cream, truck) keep the box construction.
  standard(cfg) {
    const g = new THREE.Group();
    const refs = { wheels: [], steer: [], brakeLights: [], headlights: [] };
    const wheelR = cfg.wheelR || 0.34;
    const bodyY = wheelR + cfg.bodyH / 2 - 0.05;
    const base = wheelR - 0.05;
    const profile = cfg.bodyH <= 0.9 && cfg.cabH > 0.05;
    const cabW = cfg.wid * 0.86;
    let lightY;
    const s3 = (a, b, t) => { t = clamp((t - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
    if (profile) {
      const L = cfg.len, H = cfg.bodyH, half = L / 2;
      const cabF = cfg.cabOff + cfg.cabLen / 2;
      // ---- one seamless painted hull: hood falls to the nose, deck to the
      // tail, floor lifts at both ends (approach/departure), corners rounded
      const bodyGeo = this.hull(L, H, cfg.wid, {
        key: `bd${L}_${H}_${cfg.wid}`,
        pCross: 3.4, pPlan: 5.5, tumble: 0.10, wNose: 0.86, wTail: 0.92,
        top: u => 1 - 0.15 * s3(0.7, 0.98, u) - 0.08 * (1 - s3(0.03, 0.22, u)),
        bot: u => 0.10 * s3(0.82, 1, u) + 0.08 * (1 - s3(0, 0.14, u)),
      });
      const body = new THREE.Mesh(bodyGeo, this.carBodyMats(cfg.body, false));
      body.position.y = base + H / 2;
      g.add(body);
      refs.body = body;
      // ---- glass canopy: one smooth bubble, long windshield rake, peak
      // just behind the driver — sunk into the body so it reads flush
      const canGeo = this.hull(cfg.cabLen + cfg.cabH * 0.9, cfg.cabH * 0.92, cabW, {
        key: `cn${cfg.cabLen}_${cfg.cabH}_${cabW}`,
        pCross: 2.7, pPlan: 3.4, tumble: 0.32, wNose: 0.95, wTail: 0.97,
        // plateau roof with raked windshield/backlight, not a bubble
        top: u => 0.14 + 0.86 * Math.min(1, 1.3 * Math.pow(Math.sin(Math.PI * Math.pow(clamp(u, 0.001, 0.999), 0.9)), 0.8)),
        bot: () => 0,
      });
      const glass = new THREE.Mesh(canGeo, this.canopyMats());
      glass.position.set(cfg.cabOff - cfg.cabH * 0.1, base + H + cfg.cabH * 0.46 - 0.08, 0);
      g.add(glass);
      refs.cab = glass;
      // shark-fin antenna + twin exhaust tips
      const fin = new THREE.Mesh(Assets.geo('fin', () => new THREE.BoxGeometry(0.16, 0.07, 0.05)), Assets.lambert('finDk', { color: 0x101216 }));
      fin.position.set(cfg.cabOff - cfg.cabLen * 0.3, base + H + cfg.cabH * 0.8, 0);
      g.add(fin);
      const exG = Assets.geo('exh', () => new THREE.CylinderGeometry(0.045, 0.045, 0.14, 8));
      for (const z of [cfg.wid * 0.3, cfg.wid * 0.18]) {
        const ex = new THREE.Mesh(exG, Assets.lambert('exhDk', { color: 0x1a1c20 }));
        ex.rotation.z = Math.PI / 2;
        ex.position.set(-cfg.len / 2 + 0.03, wheelR - 0.04, z);
        g.add(ex);
      }
      // ---- side mirrors ----
      for (const zz of [cabW / 2 + 0.09, -(cabW / 2 + 0.09)]) {
        const mir = new THREE.Mesh(Assets.geo('mirrorS', () => new THREE.BoxGeometry(0.08, 0.09, 0.15)), this.paint(cfg.body));
        mir.position.set(cabF - 0.04, base + H + 0.07, zz);
        g.add(mir);
      }
      // ---- grille + license plates ----
      const grille = new THREE.Mesh(
        Assets.geo('grille', () => new THREE.PlaneGeometry(0.44, 0.13)),
        new THREE.MeshLambertMaterial({ map: Assets.grilleTex() })
      );
      grille.rotation.y = Math.PI / 2;
      grille.position.set(half - 0.01, base + H * 0.42, 0);
      g.add(grille);
      const plateGeo = Assets.geo('plate', () => new THREE.PlaneGeometry(0.34, 0.12));
      const plateMat = new THREE.MeshLambertMaterial({ map: Assets.plateTex() });
      const plF = new THREE.Mesh(plateGeo, plateMat);
      plF.rotation.y = Math.PI / 2;
      plF.position.set(half - 0.045, base + H * 0.2, 0);
      g.add(plF);
      const plR = new THREE.Mesh(plateGeo, plateMat);
      plR.rotation.y = -Math.PI / 2;
      plR.position.set(-half + 0.02, base + H * 0.26, 0);
      g.add(plR);
      lightY = base + H * 0.62;
    } else {
      // ---- tall vehicles (bus / ice cream / truck): same smooth hull,
      // boxier parameters — soft edges without losing the silhouette
      const bodyGeo = this.hull(cfg.len, cfg.bodyH, cfg.wid, {
        key: `bx2${cfg.len}_${cfg.bodyH}_${cfg.wid}`,
        pCross: 6.0, pPlan: 9.0, tumble: 0.02, wNose: 0.97, wTail: 0.98,
        top: u => 1 - 0.05 * s3(0.9, 1, u),
        bot: u => 0.04 * s3(0.9, 1, u) + 0.03 * (1 - s3(0, 0.08, u)),
      });
      const body = new THREE.Mesh(bodyGeo, this.carBodyMats(cfg.body, true));
      body.position.y = bodyY;
      g.add(body);
      refs.body = body;
      const cab = this.boxMesh(cfg.cabLen, cfg.cabH, cabW, cfg.roof);
      cab.position.set(cfg.cabOff, bodyY + cfg.bodyH / 2 + cfg.cabH / 2 - 0.02, 0);
      g.add(cab);
      refs.cab = cab;
      const winH = cfg.cabH * 0.62;
      const win = new THREE.Mesh(
        new THREE.BoxGeometry(cfg.cabLen * 0.92, winH, cabW + 0.03),
        this.windowMat()
      );
      win.position.set(cfg.cabOff, cab.position.y + cfg.cabH * 0.06, 0);
      g.add(win);
      const winF = new THREE.Mesh(
        new THREE.BoxGeometry(cfg.cabLen + 0.03, winH * 0.9, cabW * 0.86),
        this.windowMat()
      );
      winF.position.copy(win.position);
      g.add(winF);
      lightY = bodyY + cfg.bodyH * 0.16;
    }
    // wheels — wider tires, flush stance
    const axF = cfg.axF !== undefined ? cfg.axF : cfg.len * 0.32;
    const axR = cfg.axR !== undefined ? cfg.axR : -cfg.len * 0.32;
    const wz = cfg.wid / 2 - 0.15;
    const archGeo = profile ? Assets.geo(`arch${wheelR}`, () => new THREE.TorusGeometry(wheelR + 0.06, 0.06, 6, 12, Math.PI)) : null;
    for (const [ax, z, front] of [[axF, wz, 1], [axF, -wz, 1], [axR, wz, 0], [axR, -wz, 0]]) {
      const w = this.wheel(wheelR, 0.3);
      if (front) {
        const sg = new THREE.Group();
        sg.position.set(ax, wheelR, z);
        sg.add(w);
        g.add(sg);
        refs.steer.push(sg);
      } else {
        w.position.set(ax, wheelR, z);
        g.add(w);
      }
      refs.wheels.push(w);
      // dark fender arch trim over each wheel
      if (archGeo) {
        const arch = new THREE.Mesh(archGeo, Assets.lambert('arch', { color: 0x1b1d22 }));
        arch.position.set(ax, wheelR, z > 0 ? cfg.wid / 2 - 0.02 : -(cfg.wid / 2 - 0.02));
        g.add(arch);
      }
    }
    // wrap-around bumper lips (rounded bars, not boxes)
    const bumMat = Assets.lambert('bumperDk', { color: 0x24262e });
    const bumF = this.bar(cfg.wid * 0.82, 0.07, bumMat);
    bumF.position.set(cfg.len / 2 - 0.03, wheelR + 0.05, 0); g.add(bumF);
    const bumR = this.bar(cfg.wid * 0.82, 0.07, bumMat);
    bumR.position.set(-cfg.len / 2 + 0.03, wheelR + 0.05, 0); g.add(bumR);
    // full-width flush light bars (modern EV signature, blooms at night)
    const hb = this.bar(cfg.wid * (profile ? 0.56 : 0.68), 0.045,
      new THREE.MeshLambertMaterial({ color: 0x222222, emissive: 0xfff2c0, emissiveIntensity: 1.1 }));
    hb.position.set(cfg.len / 2 - (profile ? 0.05 : 0.02), lightY, 0);
    g.add(hb); refs.headlights.push(hb);
    const tb = this.bar(cfg.wid * (profile ? 0.6 : 0.68), 0.04,
      new THREE.MeshLambertMaterial({ color: 0x551111, emissive: 0xff3b30, emissiveIntensity: 0.3 }));
    tb.position.set(-cfg.len / 2 + (profile ? 0.04 : 0.02), lightY + 0.04, 0);
    g.add(tb); refs.brakeLights.push(tb);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    // soft contact shadow grounds the car even without shadow maps
    const blob = this.shadowBlob(cfg.len, cfg.wid);
    blob.castShadow = false; blob.receiveShadow = false;
    g.add(blob);
    refs.group = g;
    return refs;
  },

  build(key) {
    const d = VEH_DEFS[key];
    switch (key) {
      case 'hatch': {
        const r = this.standard({ len: d.len, wid: d.wid, bodyH: 0.62, cabLen: d.len * 0.5, cabH: 0.55, cabOff: -d.len * 0.06, body: d.body, roof: d.roof });
        // rust patches
        const rust = this.boxMesh(0.5, 0.14, 0.04, 0x7a4a2e);
        rust.position.set(-d.len * 0.08, 0.52, d.wid / 2 - 0.08); // hugs the curved hull side
        r.group.add(rust);
        // exhaust ref for backfire
        r.exhaust = new THREE.Vector3(-d.len / 2 - 0.1, 0.3, d.wid * 0.25);
        return r;
      }
      case 'wagon': {
        return this.standard({ len: d.len, wid: d.wid, bodyH: 0.65, cabLen: d.len * 0.62, cabH: 0.52, cabOff: -d.len * 0.1, body: d.body, roof: d.roof });
      }
      case 'limo': {
        const r = this.standard({ len: d.len, wid: d.wid, bodyH: 0.6, cabLen: d.len * 0.78, cabH: 0.46, cabOff: -d.len * 0.02, body: d.body, roof: d.roof, axF: d.len * 0.4, axR: -d.len * 0.4 });
        // mid wheels for comedy
        const wz = d.wid / 2 - 0.12;
        for (const z of [wz, -wz]) {
          const w = this.wheel(0.34, 0.26);
          w.position.set(0, 0.34, z);
          r.group.add(w); r.wheels.push(w);
        }
        // tiny flag
        const pole = this.boxMesh(0.04, 0.5, 0.04, 0xcccccc);
        pole.position.set(d.len / 2 - 0.3, 1.05, d.wid / 2 - 0.2);
        r.group.add(pole);
        const flag = this.boxMesh(0.3, 0.2, 0.02, 0xff4757);
        flag.position.set(d.len / 2 - 0.15, 1.22, d.wid / 2 - 0.2);
        r.group.add(flag);
        return r;
      }
      case 'icecream': {
        const r = this.standard({ len: d.len, wid: d.wid, bodyH: 1.7, cabLen: d.len * 0.3, cabH: 0.01, cabOff: d.len * 0.32, body: d.body, roof: d.body, wheelR: 0.38 });
        // pink roof slab (ends before the rounded corners)
        const roof = this.boxMesh(d.len * 0.88, 0.18, d.wid * 0.98, 0xf8b8cf);
        roof.position.y = 0.38 + 1.7 + 0.02;
        r.group.add(roof);
        // giant cone on top
        const cone = new THREE.Mesh(
          new THREE.ConeGeometry(0.36, 0.9, 10),
          Assets.lambert('icecone', { color: 0xd9a066 })
        );
        cone.rotation.x = Math.PI; cone.position.set(0.4, 2.7, 0);
        r.group.add(cone);
        const scoop = new THREE.Mesh(
          Assets.geo('scoop', () => new THREE.SphereGeometry(0.4, 12, 10)),
          Assets.lambert('scoopPink', { color: 0xf793b4 })
        );
        scoop.position.set(0.4, 3.15, 0);
        r.group.add(scoop);
        // serving window
        const serv = this.boxMesh(1.3, 0.8, 0.06, 0x7ec8e3);
        serv.position.set(-0.3, 1.5, d.wid / 2 - 0.05); // flush with rounded flank
        r.group.add(serv);
        // menu stripe (mid-body only, clear of the tapered ends)
        const stripe = this.boxMesh(d.len * 0.72, 0.3, d.wid + 0.02, 0xf8b8cf);
        stripe.position.y = 1.1;
        r.group.add(stripe);
        r.coneRef = cone; r.scoopRef = scoop;
        return r;
      }
      case 'bus': {
        const r = this.standard({ len: d.len, wid: d.wid, bodyH: 2.1, cabLen: 0.01, cabH: 0.01, cabOff: d.len * 0.45, body: d.body, roof: d.roof, wheelR: 0.45, axF: d.len * 0.36, axR: -d.len * 0.33 });
        // hood
        const hood = this.boxMesh(1.0, 0.9, d.wid * 0.86, d.body);
        hood.position.set(d.len / 2 + 0.2, 0.45 + 0.45, 0); // overlaps the rounded nose
        r.group.add(hood);
        // window band
        const band = new THREE.Mesh(
          new THREE.BoxGeometry(d.len * 0.78, 0.6, d.wid + 0.04),
          this.windowMat()
        );
        band.position.y = 0.45 + 2.1 * 0.62;
        r.group.add(band);
        // black stripe + text board (stops short of the rounded corners)
        const stripe = this.boxMesh(d.len * 0.78, 0.16, d.wid + 0.05, 0x23252c);
        stripe.position.y = 1.25;
        r.group.add(stripe);
        // mirrors on sticks (front)
        r.mirrors = [];
        for (const z of [d.wid / 2 + 0.35, -d.wid / 2 - 0.35]) {
          const stick = this.boxMesh(0.05, 0.05, 0.5, 0x3a3d47);
          stick.position.set(d.len * 0.38, 1.9, z > 0 ? d.wid / 2 + 0.1 : -d.wid / 2 - 0.1);
          r.group.add(stick);
          const mir = this.boxMesh(0.08, 0.4, 0.25, 0x9fd8f0);
          mir.position.set(d.len * 0.38, 1.9, z);
          r.group.add(mir);
          r.mirrors.push(mir);
        }
        // stop arm (hinged on left side, local -z? left of forward(+x) is +z in three... physics right side is -t...)
        const armG = new THREE.Group();
        armG.position.set(d.len * 0.1, 1.4, -d.wid / 2 - 0.02);
        const arm = new THREE.Mesh(
          Assets.geo('stopArm', () => new THREE.CylinderGeometry(0.42, 0.42, 0.06, 8)),
          new THREE.MeshLambertMaterial({ color: 0xd92b2b })
        );
        arm.rotation.x = Math.PI / 2;
        arm.position.z = -0.5;
        armG.add(arm);
        armG.rotation.y = Math.PI / 2.2; // folded
        r.group.add(armG);
        r.armGroup = armG;
        // flashing lights
        r.flashers = [];
        for (const x of [d.len / 2 - 0.6, -d.len / 2 + 0.6]) {
          const f = this.lightBox(0.15, 0.15, 0.3, 0xff3b30);
          f.position.set(x, 0.45 + 2.1 + 0.04, 0);
          r.group.add(f);
          r.flashers.push(f);
        }
        return r;
      }
      case 'tank': {
        const g = new THREE.Group();
        const refs = { wheels: [], steer: [], brakeLights: [], headlights: [], group: g };
        // treads
        for (const z of [d.wid / 2 - 0.5, -d.wid / 2 + 0.5]) {
          const tread = this.boxMesh(d.len * 0.95, 0.7, 0.95, 0x2e3228);
          tread.position.set(0, 0.36, z);
          g.add(tread);
          for (let i = -2; i <= 2; i++) {
            const rl = new THREE.Group();
            const spin = new THREE.Group();
            const rm = new THREE.Mesh(
              Assets.geo('roller', () => new THREE.CylinderGeometry(0.28, 0.28, 1.0, 16)),
              Assets.lambert('roller', { color: 0x454a3c })
            );
            rm.rotation.x = Math.PI / 2;
            rm.castShadow = true;
            spin.add(rm);
            rl.add(spin);
            rl.position.set(i * d.len * 0.18, 0.3, z);
            g.add(rl);
            refs.wheels.push(rl);
          }
        }
        // hull
        const hull = this.boxMesh(d.len * 0.92, 0.65, d.wid * 0.72, d.body);
        hull.position.y = 0.95;
        g.add(hull);
        // turret
        const turretG = new THREE.Group();
        turretG.position.set(-0.3, 1.45, 0);
        const tur = this.boxMesh(1.8, 0.55, 1.6, d.roof);
        turretG.add(tur);
        const barrel = new THREE.Mesh(
          Assets.geo('barrel', () => new THREE.CylinderGeometry(0.09, 0.11, 2.6, 8)),
          Assets.lambert('barrel', { color: 0x4a5238 })
        );
        barrel.rotation.z = -Math.PI / 2;
        barrel.position.set(2.0, 0.1, 0);
        turretG.add(barrel);
        g.add(turretG);
        refs.turret = turretG;
        // hatch + antenna
        const ant = this.boxMesh(0.03, 0.8, 0.03, 0x888888);
        ant.position.set(-0.9, 2.1, 0.4); g.add(ant);
        g.traverse(o => { if (o.isMesh) o.castShadow = true; });
        g.add(this.shadowBlob(d.len, d.wid));
        return refs;
      }
      case 'ufo': {
        const g = new THREE.Group();
        const refs = { wheels: [], steer: [], brakeLights: [], headlights: [], group: g };
        const saucer = new THREE.Mesh(
          Assets.geo('saucer', () => {
            const geo = new THREE.SphereGeometry(d.len / 2, 20, 10);
            geo.scale(1, 0.28, 1);
            return geo;
          }),
          Assets.lambert('saucerBody', { color: d.body })
        );
        saucer.position.y = 0.9;
        g.add(saucer);
        const dome = new THREE.Mesh(
          Assets.geo('dome', () => new THREE.SphereGeometry(1.0, 16, 10, 0, TAU, 0, Math.PI / 2)),
          new THREE.MeshLambertMaterial({ color: 0x9fe8d8, transparent: true, opacity: 0.75, emissive: 0x2a6a58, emissiveIntensity: 0.3 })
        );
        dome.position.y = 1.15;
        g.add(dome);
        // alien in dome
        const alien = new THREE.Mesh(
          Assets.geo('alienHead', () => new THREE.SphereGeometry(0.3, 10, 8)),
          Assets.lambert('alienSkin', { color: 0x77d977 })
        );
        alien.scale.set(0.8, 1.15, 0.8);
        alien.position.y = 1.35;
        g.add(alien);
        // glow ring lights
        refs.ringLights = [];
        for (let i = 0; i < 8; i++) {
          const a = i / 8 * TAU;
          const lt = new THREE.Mesh(
            Assets.geo('ringlt', () => new THREE.SphereGeometry(0.14, 8, 6)),
            new THREE.MeshLambertMaterial({ color: 0x222222, emissive: 0x8ef7d2, emissiveIntensity: 1 })
          );
          lt.position.set(Math.cos(a) * d.len * 0.42, 0.82, Math.sin(a) * d.len * 0.42);
          g.add(lt);
          refs.ringLights.push(lt);
        }
        // beam cone (hidden until beaming)
        const beam = new THREE.Mesh(
          Assets.geo('beam', () => new THREE.ConeGeometry(1.7, 2.4, 18, 1, true)),
          new THREE.MeshBasicMaterial({ color: 0x8ef7d2, transparent: true, opacity: 0, side: THREE.DoubleSide, depthWrite: false })
        );
        beam.position.y = 0;
        g.add(beam);
        refs.beam = beam;
        g.traverse(o => { if (o.isMesh) o.castShadow = true; });
        refs.hover = true;
        return refs;
      }
      case 'kart': {
        // open-wheel go-kart: low box hull, no canopy, roll hoop + helmet driver
        const r = this.standard({ len: d.len, wid: d.wid, bodyH: 0.3, cabLen: 0.01, cabH: 0.01, cabOff: 0, body: d.body, roof: d.roof, wheelR: 0.2 });
        const seat = this.boxMesh(0.5, 0.45, 0.55, 0x2b2d36);
        seat.position.set(-d.len * 0.14, 0.52, 0);
        r.group.add(seat);
        // driver: helmet + torso
        const torso = this.boxMesh(0.4, 0.34, 0.44, 0xd95a4e);
        torso.position.set(-d.len * 0.1, 0.72, 0);
        r.group.add(torso);
        const helmet = new THREE.Mesh(
          Assets.geo('kartHelm', () => new THREE.SphereGeometry(0.19, 10, 8)),
          Assets.lambert('kartHelmW', { color: 0xf2f2f2 })
        );
        helmet.position.set(-d.len * 0.1, 1.0, 0);
        r.group.add(helmet);
        // roll hoop behind the driver
        const hoop = this.boxMesh(0.06, 0.55, 0.5, 0x3a3d47);
        hoop.position.set(-d.len * 0.28, 0.7, 0);
        r.group.add(hoop);
        // steering column + tiny wheel
        const col = this.boxMesh(0.3, 0.05, 0.05, 0x3a3d47);
        col.position.set(d.len * 0.14, 0.55, 0);
        col.rotation.z = -0.5;
        r.group.add(col);
        const sw = new THREE.Mesh(
          Assets.geo('kartWheel', () => new THREE.TorusGeometry(0.13, 0.03, 6, 12)),
          Assets.lambert('kartSw', { color: 0x23252c })
        );
        sw.position.set(d.len * 0.2, 0.62, 0);
        sw.rotation.y = Math.PI / 2;
        sw.rotation.x = 0.5;
        r.group.add(sw);
        return r;
      }
      case 'monster': {
        // lifted pickup on comically large wheels — big wheelR raises the hull
        const r = this.standard({ len: d.len, wid: d.wid * 0.82, bodyH: 0.85, cabLen: d.len * 0.34, cabH: 0.55, cabOff: d.len * 0.08, body: d.body, roof: d.roof, wheelR: 0.72 });
        // swap wheels for monster rubber — wheelR 0.72 is already tall, so only
        // a touch more diameter (scaling past the axle height sinks them)
        for (const w of r.wheels) {
          w.scale.set(1.08, 1.08, 1.75);
        }
        // cargo bed walls at the back
        const bedFloor = this.boxMesh(d.len * 0.36, 0.1, d.wid * 0.76, 0x2b2d36);
        bedFloor.position.set(-d.len * 0.28, 1.35, 0);
        r.group.add(bedFloor);
        for (const z of [d.wid * 0.36, -d.wid * 0.36]) {
          const wall = this.boxMesh(d.len * 0.36, 0.3, 0.06, new THREE.Color(d.body).multiplyScalar(0.8).getHex());
          wall.position.set(-d.len * 0.28, 1.52, z);
          r.group.add(wall);
        }
        const tail = this.boxMesh(0.06, 0.3, d.wid * 0.76, new THREE.Color(d.body).multiplyScalar(0.8).getHex());
        tail.position.set(-d.len * 0.46, 1.52, 0);
        r.group.add(tail);
        // roof light bar
        const bar = this.boxMesh(0.14, 0.1, d.wid * 0.5, 0x23252c);
        bar.position.set(d.len * 0.16, 2.35, 0);
        r.group.add(bar);
        for (let i = -2; i <= 2; i++) {
          const lamp = this.lightBox(0.1, 0.09, 0.12, 0xffe08a);
          lamp.position.set(d.len * 0.16 + 0.06, 2.36, i * d.wid * 0.09);
          r.group.add(lamp);
        }
        // side exhaust stacks
        for (const z of [d.wid * 0.3, -d.wid * 0.3]) {
          const stack = new THREE.Mesh(
            Assets.geo('mtStack', () => new THREE.CylinderGeometry(0.07, 0.09, 0.9, 8)),
            Assets.lambert('mtChrome', { color: 0xb8bec8 })
          );
          stack.position.set(-d.len * 0.06, 1.7, z);
          r.group.add(stack);
        }
        return r;
      }
      default: {
        return this.standard({ len: d.len, wid: d.wid, bodyH: 0.62, cabLen: d.len * 0.5, cabH: 0.52, cabOff: -d.len * 0.05, body: d.body, roof: d.roof });
      }
    }
  },

  // traffic / parked cars (cheap): kind sedan|hatch|suv|taxi|police|truck
  TRAFFIC_COLORS: ['#d95a4e', '#4a90d9', '#e0e3e8', '#5cb85c', '#8a6ab8', '#e8a33a', '#66c6c2', '#c94f7c', '#7a7d8a'],
  traffic(kind, color) {
    const dims = {
      sedan: { len: 4.5, wid: 1.8, bodyH: 0.6, cabLen: 2.1, cabH: 0.5, cabOff: -0.2 },
      hatch: { len: 3.9, wid: 1.75, bodyH: 0.6, cabLen: 1.9, cabH: 0.52, cabOff: -0.3 },
      suv: { len: 4.8, wid: 1.95, bodyH: 0.85, cabLen: 2.8, cabH: 0.5, cabOff: -0.3, wheelR: 0.4 },
      taxi: { len: 4.5, wid: 1.8, bodyH: 0.6, cabLen: 2.1, cabH: 0.5, cabOff: -0.2 },
      police: { len: 4.7, wid: 1.85, bodyH: 0.62, cabLen: 2.2, cabH: 0.5, cabOff: -0.2 },
      truck: { len: 6.4, wid: 2.3, bodyH: 1.0, cabLen: 1.6, cabH: 0.9, cabOff: 2.1, wheelR: 0.45 },
    };
    const cfg = dims[kind] || dims.sedan;
    let body = color || pick(this.TRAFFIC_COLORS);
    if (kind === 'taxi') body = '#f2c531';
    if (kind === 'police') body = '#2a3a5c';
    const roof = kind === 'truck' ? body : new THREE.Color(body).multiplyScalar(0.85).getStyle();
    const r = this.standard(Object.assign({}, cfg, { len: cfg.len, wid: cfg.wid, body, roof }));
    const roofTop = (cfg.wheelR || 0.34) - 0.05 + cfg.bodyH + cfg.cabH;
    if (kind === 'taxi') {
      const sign = this.boxMesh(0.5, 0.2, 0.24, 0x23252c);
      sign.position.y = roofTop + 0.12;
      r.group.add(sign);
    }
    if (kind === 'police') {
      const bar = this.lightBox(0.5, 0.14, 0.9, 0xff3b30);
      bar.position.y = roofTop + 0.1;
      r.group.add(bar);
      r.lightbar = bar;
    }
    if (kind === 'truck') {
      const cargo = this.boxMesh(4.0, 2.0, cfg.wid, 0xd8d4ca);
      cargo.position.set(-1.0, 0.45 + 1.35, 0);
      r.group.add(cargo);
    }
    r.len = cfg.len; r.wid = cfg.wid;
    return r;
  },
};

// ============================================================
// PEDESTRIAN FACTORY
// ============================================================
// Articulated mini-humans, built facing LOCAL +X. Limbs hang from pivot
// groups at hip/shoulder height, so swinging pivot.rotation.z hinges them
// fore-aft like real joints (the old model rotated box centers, which read
// as a sideways waddle). refs.legL/legR/armL/armR are the PIVOT groups.
const PedFactory = {
  SKINS: [0xf2c9a0, 0xd9a06b, 0xa5673f, 0x8a5230, 0xf7d7b6],
  SHIRTS: [0xd95a4e, 0x4a90d9, 0x5cb85c, 0xe8a33a, 0x8a6ab8, 0xf2f2f2, 0x23252c, 0xc94f7c],
  PANTS: [0x33415c, 0x23252c, 0x6b4a2e, 0x555a66, 0x8891a5],
  HAIR: [0x241c12, 0x151210, 0x54371c, 0x8a8a8a, 0xc9a04e, 0x3d2a1a],
  lam(color) { return Assets.lambert('pedc' + color, { color }); },
  bx(l, h, w, color) {
    const m = new THREE.Mesh(Assets.geo(`pbx${l}_${h}_${w}`, () => new THREE.BoxGeometry(l, h, w)), this.lam(color));
    m.castShadow = true;
    return m;
  },
  build() {
    const g = new THREE.Group();
    const shirt = pick(this.SHIRTS), pants = pick(this.PANTS), skin = pick(this.SKINS);
    const hair = pick(this.HAIR), shoes = pick([0x1c1a18, 0x3a2c20, 0xe8e5df, 0x50403a]);
    const pantsDark = new THREE.Color(pants).multiplyScalar(0.8).getHex();
    const longSleeve = Math.random() < 0.5;
    // pelvis + belt
    const pelvis = this.bx(0.24, 0.16, 0.3, pants);
    pelvis.position.y = 0.92;
    g.add(pelvis);
    // legs: pivot at hip, thigh + calf + shoe hang below
    const mkLeg = (zs) => {
      const pivot = new THREE.Group();
      pivot.position.set(0, 0.88, zs * 0.085);
      const thigh = this.bx(0.13, 0.42, 0.14, pants);
      thigh.position.y = -0.21;
      const calf = this.bx(0.11, 0.4, 0.12, pantsDark);
      calf.position.y = -0.6;
      const shoe = this.bx(0.26, 0.09, 0.12, shoes);
      shoe.position.set(0.06, -0.84, 0);
      pivot.add(thigh, calf, shoe);
      g.add(pivot);
      return pivot;
    };
    const legL = mkLeg(1), legR = mkLeg(-1);
    // torso: chest + shoulders
    const torso = this.bx(0.26, 0.5, 0.34, shirt);
    torso.position.y = 1.26;
    g.add(torso);
    const shoulders = this.bx(0.24, 0.12, 0.4, shirt);
    shoulders.position.y = 1.48;
    g.add(shoulders);
    // arms: pivot at shoulder; upper arm shirt, forearm skin if short sleeves
    const mkArm = (zs) => {
      const pivot = new THREE.Group();
      pivot.position.set(0, 1.46, zs * 0.225);
      const upper = this.bx(0.1, 0.3, 0.1, shirt);
      upper.position.y = -0.16;
      const fore = this.bx(0.09, 0.28, 0.09, longSleeve ? shirt : skin);
      fore.position.y = -0.44;
      const hand = this.bx(0.08, 0.09, 0.08, skin);
      hand.position.y = -0.62;
      pivot.add(upper, fore, hand);
      g.add(pivot);
      return pivot;
    };
    const armL = mkArm(1), armR = mkArm(-1);
    // neck + head + hair + face
    const neck = this.bx(0.1, 0.08, 0.1, skin);
    neck.position.y = 1.56;
    g.add(neck);
    const head = new THREE.Mesh(Assets.geo('phead', () => new THREE.SphereGeometry(0.145, 12, 10)), this.lam(skin));
    head.position.y = 1.72;
    head.castShadow = true;
    g.add(head);
    if (Math.random() < 0.18) { // cap
      const capDome = new THREE.Mesh(Assets.geo('pcap', () => {
        const geo = new THREE.SphereGeometry(0.15, 10, 6, 0, TAU, 0, Math.PI / 2);
        return geo;
      }), this.lam(pick(this.SHIRTS)));
      capDome.position.y = 1.76;
      g.add(capDome);
      const brim = this.bx(0.16, 0.025, 0.16, 0x23252c);
      brim.position.set(0.14, 1.77, 0);
      g.add(brim);
    } else { // hair cap: squashed hemisphere, nudged back
      const hairM = new THREE.Mesh(Assets.geo('phair', () => new THREE.SphereGeometry(0.152, 10, 6, 0, TAU, 0, Math.PI / 1.7)), this.lam(hair));
      hairM.position.set(-0.02, 1.74, 0);
      hairM.scale.y = 0.82;
      g.add(hairM);
    }
    // eyes on the +x face
    for (const zs of [0.055, -0.055]) {
      const eye = this.bx(0.02, 0.035, 0.035, 0x1c1a18);
      eye.position.set(0.135, 1.74, zs);
      eye.castShadow = false;
      g.add(eye);
    }
    // accessories: shopping bag in hand, or backpack
    if (Math.random() < 0.22) {
      const bag = this.bx(0.2, 0.26, 0.16, pick([0xc9803a, 0xd9d5cc, 0x7a4a8a]));
      bag.position.set(0, -0.78, 0);
      armL.add(bag);
    } else if (Math.random() < 0.25) {
      const pack = this.bx(0.14, 0.36, 0.3, pick([0x9c3a34, 0x33415c, 0x3f6d44]));
      pack.position.set(-0.2, 1.26, 0);
      g.add(pack);
    }
    // phone (shown when filming) — in front, roughly at raised right hand
    const phone = new THREE.Mesh(Assets.geo('pphone', () => new THREE.BoxGeometry(0.05, 0.16, 0.1)), Assets.lambert('phone', { color: 0x23252c }));
    phone.position.set(0.42, 1.5, -0.22);
    phone.visible = false;
    g.add(phone);
    // emote sprite
    const emote = new THREE.Sprite(new THREE.SpriteMaterial({ map: Assets.emojiTexture('😱'), transparent: true, depthWrite: false }));
    emote.scale.set(0.7, 0.7, 1);
    emote.position.y = 2.15;
    emote.visible = false;
    g.add(emote);
    // body-type variety
    g.scale.set(rand(0.92, 1.05), rand(0.92, 1.06), rand(0.86, 1.02));
    return { group: g, legL, legR, armL, armR, head, torso, phone, emote };
  },
};

// ============================================================
// PROP FACTORY
// ============================================================
const PropFactory = {
  conePaint() {
    if (!Assets.mats.conePaint) Assets.mats.conePaint = new THREE.MeshPhongMaterial({ color: 0xf07818, shininess: 28, specular: 0x553311 });
    return Assets.mats.conePaint;
  },
  cone() {
    const g = new THREE.Group();
    const base = new THREE.Mesh(Assets.geo('coneBase', () => {
      const geo = new THREE.BoxGeometry(0.42, 0.05, 0.42);
      return geo;
    }), this.conePaint());
    base.position.y = 0.025;
    const base2 = new THREE.Mesh(Assets.geo('coneBase2', () => new THREE.BoxGeometry(0.3, 0.05, 0.3)), this.conePaint());
    base2.position.y = 0.07;
    const body = new THREE.Mesh(Assets.geo('coneBody', () => new THREE.ConeGeometry(0.17, 0.62, 14)), this.conePaint());
    body.position.y = 0.38;
    // two retro-reflective collars
    const bandMat = Assets.mats.coneBand || (Assets.mats.coneBand = new THREE.MeshPhongMaterial({ color: 0xf4f6f8, shininess: 90, specular: 0xbbccdd }));
    const band1 = new THREE.Mesh(Assets.geo('coneBand1', () => new THREE.CylinderGeometry(0.115, 0.14, 0.11, 14)), bandMat);
    band1.position.y = 0.42;
    const band2 = new THREE.Mesh(Assets.geo('coneBand2', () => new THREE.CylinderGeometry(0.148, 0.165, 0.07, 14)), bandMat);
    band2.position.y = 0.26;
    g.add(base, base2, body, band1, band2);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },
  hydrant() {
    const g = new THREE.Group();
    const red = Assets.mats.hydRed || (Assets.mats.hydRed = new THREE.MeshPhongMaterial({ color: 0xc9342b, shininess: 36, specular: 0x662222 }));
    const flange = new THREE.Mesh(Assets.geo('hydFlange', () => new THREE.CylinderGeometry(0.2, 0.22, 0.08, 12)), red);
    flange.position.y = 0.04;
    const body = new THREE.Mesh(Assets.geo('hyd', () => new THREE.CylinderGeometry(0.145, 0.175, 0.5, 12)), red);
    body.position.y = 0.32;
    const collar = new THREE.Mesh(Assets.geo('hydCollar', () => new THREE.CylinderGeometry(0.17, 0.17, 0.05, 12)), red);
    collar.position.y = 0.57;
    const bonnet = new THREE.Mesh(Assets.geo('hydBonnet', () => new THREE.SphereGeometry(0.15, 12, 8, 0, TAU, 0, Math.PI / 2)), red);
    bonnet.position.y = 0.59;
    const nut = new THREE.Mesh(Assets.geo('hydNut', () => new THREE.CylinderGeometry(0.045, 0.045, 0.08, 6)), red);
    nut.position.y = 0.74;
    g.add(flange, body, collar, bonnet, nut);
    // side + front nozzle caps
    for (const [rx, ry, px, pz] of [[Math.PI / 2, 0, 0, 0.16], [Math.PI / 2, 0, 0, -0.16], [0, 0, 0.16, 0]]) {
      const noz = new THREE.Mesh(Assets.geo('hydNoz', () => new THREE.CylinderGeometry(0.075, 0.075, 0.12, 8)), red);
      if (pz !== 0) noz.rotation.x = rx;
      else noz.rotation.z = Math.PI / 2;
      noz.position.set(px, 0.38, pz);
      g.add(noz);
      const cap = new THREE.Mesh(Assets.geo('hydCap', () => new THREE.CylinderGeometry(0.05, 0.05, 0.05, 5)), red);
      if (pz !== 0) { cap.rotation.x = rx; cap.position.set(0, 0.38, pz > 0 ? pz + 0.07 : -(0.16 + 0.07)); }
      else { cap.rotation.z = Math.PI / 2; cap.position.set(0.23, 0.38, 0); }
      g.add(cap);
    }
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },
  woodMat() {
    if (!Assets.mats.woodTex) Assets.mats.woodTex = new THREE.MeshLambertMaterial({ map: Assets.woodTex() });
    return Assets.mats.woodTex;
  },
  bench() {
    const g = new THREE.Group();
    // slatted seat
    for (const zo of [-0.16, 0, 0.16]) {
      const slat = new THREE.Mesh(Assets.geo('benchSlat', () => new THREE.BoxGeometry(1.6, 0.05, 0.13)), this.woodMat());
      slat.position.set(0, 0.45, zo);
      g.add(slat);
    }
    // slatted back, slightly reclined
    for (const yo of [0.62, 0.78]) {
      const slat = new THREE.Mesh(Assets.geo('benchBackSlat', () => new THREE.BoxGeometry(1.6, 0.12, 0.045)), this.woodMat());
      slat.position.set(0, yo, -0.21 - (yo - 0.62) * 0.22);
      slat.rotation.x = -0.18;
      g.add(slat);
    }
    // cast-iron end frames with armrests
    const iron = Assets.lambert('ink', { color: 0x2b2d36 });
    for (const x of [-0.68, 0.68]) {
      const leg = new THREE.Mesh(Assets.geo('benchLeg', () => new THREE.BoxGeometry(0.07, 0.45, 0.42)), iron);
      leg.position.set(x, 0.22, 0);
      g.add(leg);
      const arm = new THREE.Mesh(Assets.geo('benchArm', () => new THREE.BoxGeometry(0.06, 0.05, 0.44)), iron);
      arm.position.set(x, 0.58, 0);
      g.add(arm);
      const post = new THREE.Mesh(Assets.geo('benchPost', () => new THREE.BoxGeometry(0.05, 0.14, 0.05)), iron);
      post.position.set(x, 0.5, 0.16);
      g.add(post);
    }
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },
  mailbox() {
    const g = new THREE.Group();
    const post = new THREE.Mesh(Assets.geo('mbPost', () => new THREE.BoxGeometry(0.07, 1.1, 0.07)), this.woodMat());
    post.position.y = 0.55;
    const box = new THREE.Mesh(
      Assets.geo('mbBox', () => new THREE.CylinderGeometry(0.14, 0.14, 0.5, 10, 1, false, 0, Math.PI)),
      Assets.mats.mbBox || (Assets.mats.mbBox = new THREE.MeshStandardMaterial({ color: 0x3a5a8c, roughness: 0.5, metalness: 0.5 }))
    );
    box.rotation.z = Math.PI / 2;
    box.position.y = 1.14;
    const floor = new THREE.Mesh(Assets.geo('mbFloor', () => new THREE.BoxGeometry(0.5, 0.04, 0.28)), Assets.mats.mbBox);
    floor.position.y = 1.12;
    const flag = new THREE.Mesh(Assets.geo('mbFlag', () => new THREE.BoxGeometry(0.03, 0.16, 0.05)), Assets.lambert('mbFlag', { color: 0xd92b2b }));
    flag.position.set(0.18, 1.3, 0.15);
    g.add(post, box, floor, flag);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },
  trashBin() {
    const g = new THREE.Group();
    const mat = Assets.mats.bin || (Assets.mats.bin = new THREE.MeshStandardMaterial({ color: 0x4a5560, roughness: 0.6, metalness: 0.35 }));
    const body = new THREE.Mesh(Assets.geo('binBody', () => new THREE.CylinderGeometry(0.26, 0.22, 0.72, 12)), mat);
    body.position.y = 0.36;
    const lid = new THREE.Mesh(Assets.geo('binLid', () => new THREE.CylinderGeometry(0.28, 0.28, 0.07, 12)), mat);
    lid.position.y = 0.75;
    const knob = new THREE.Mesh(Assets.geo('binKnob', () => new THREE.SphereGeometry(0.05, 8, 6)), mat);
    knob.position.y = 0.82;
    g.add(body, lid, knob);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },
  flowerBed() {
    const g = new THREE.Group();
    const leaf = Assets.geo('fbLeaf', () => new THREE.IcosahedronGeometry(0.16, 0));
    const bloom = Assets.geo('fbBloom', () => new THREE.SphereGeometry(0.07, 7, 6));
    for (let i = 0; i < 5; i++) {
      const a = i / 5 * TAU + rand(0, 1);
      const rr = rand(0.1, 0.4);
      const bush = new THREE.Mesh(leaf, new THREE.MeshLambertMaterial({ color: pick([0x3f8f46, 0x5aa855, 0x4a9c4a]) }));
      bush.position.set(Math.cos(a) * rr, 0.12, Math.sin(a) * rr);
      bush.scale.setScalar(rand(0.8, 1.4));
      bush.castShadow = true;
      g.add(bush);
      const fl = new THREE.Mesh(bloom, new THREE.MeshLambertMaterial({ color: pick([0xff6b8a, 0xffc23e, 0xff8a5c, 0xd95ac0, 0xf2f2f2]) }));
      fl.position.set(Math.cos(a) * rr, 0.26 * bush.scale.x, Math.sin(a) * rr);
      g.add(fl);
    }
    return g;
  },
  powerPole() {
    const g = new THREE.Group();
    const pole = new THREE.Mesh(Assets.geo('ppPole', () => new THREE.CylinderGeometry(0.09, 0.13, 7.4, 8)), this.barkMat());
    pole.position.y = 3.7;
    pole.castShadow = true;
    const arm = new THREE.Mesh(Assets.geo('ppArm', () => new THREE.BoxGeometry(0.1, 0.12, 2.2)), this.barkMat());
    arm.position.y = 6.9;
    const arm2 = new THREE.Mesh(Assets.geo('ppArm2', () => new THREE.BoxGeometry(0.1, 0.12, 1.6)), this.barkMat());
    arm2.position.y = 6.3;
    g.add(pole, arm, arm2);
    // insulators
    const insGeo = Assets.geo('ppIns', () => new THREE.CylinderGeometry(0.05, 0.06, 0.12, 6));
    const insMat = Assets.lambert('ppInsM', { color: 0x8fa8c4 });
    for (const [y, z] of [[7.0, -1.0], [7.0, 1.0], [6.4, -0.7], [6.4, 0.7]]) {
      const ins = new THREE.Mesh(insGeo, insMat);
      ins.position.set(0, y, z);
      g.add(ins);
    }
    return g;
  },
  manhole() {
    const m = new THREE.Mesh(
      Assets.geo('manhole', () => new THREE.CircleGeometry(0.55, 18)),
      Assets.mats.manhole || (Assets.mats.manhole = new THREE.MeshStandardMaterial({
        map: Assets.canvas(64, 64, (c, W, H) => {
          c.fillStyle = '#2e3138'; c.fillRect(0, 0, W, H);
          c.strokeStyle = 'rgba(0,0,0,.6)'; c.lineWidth = 2;
          c.beginPath(); c.arc(32, 32, 28, 0, TAU); c.stroke();
          c.beginPath(); c.arc(32, 32, 20, 0, TAU); c.stroke();
          for (let i = 0; i < 8; i++) {
            const a = i / 8 * TAU;
            c.beginPath();
            c.moveTo(32 + Math.cos(a) * 8, 32 + Math.sin(a) * 8);
            c.lineTo(32 + Math.cos(a) * 27, 32 + Math.sin(a) * 27);
            c.stroke();
          }
        }), roughness: 0.55, metalness: 0.7,
      }))
    );
    m.rotation.x = -Math.PI / 2;
    m.position.y = 0.028;
    return m;
  },
  signPost(texFn, size) {
    const g = new THREE.Group();
    const pole = new THREE.Mesh(Assets.geo('signPole', () => new THREE.CylinderGeometry(0.05, 0.05, 2.4, 6)), Assets.lambert('poleGrey', { color: 0x8891a5 }));
    pole.position.y = 1.2;
    const face = new THREE.Mesh(
      new THREE.PlaneGeometry(size || 0.7, size || 0.7),
      new THREE.MeshLambertMaterial({ map: texFn, transparent: true, side: THREE.DoubleSide })
    );
    face.position.y = 2.35;
    g.add(pole, face);
    pole.castShadow = true;
    return g;
  },
  stopSignTex() {
    if (Assets.texs.stopSign) return Assets.texs.stopSign;
    Assets.texs.stopSign = Assets.canvas(128, 128, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      c.fillStyle = '#d92b2b';
      c.beginPath();
      for (let i = 0; i < 8; i++) {
        const a = i / 8 * TAU + TAU / 16;
        const x = W / 2 + Math.cos(a) * 60, y = H / 2 + Math.sin(a) * 60;
        i ? c.lineTo(x, y) : c.moveTo(x, y);
      }
      c.closePath(); c.fill();
      c.strokeStyle = '#fff'; c.lineWidth = 5; c.stroke();
      c.fillStyle = '#fff'; c.font = '900 36px "Baloo 2", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText('STOP', W / 2, H / 2 + 2);
    });
    return Assets.texs.stopSign;
  },
  schoolSignTex() {
    if (Assets.texs.schoolSign) return Assets.texs.schoolSign;
    Assets.texs.schoolSign = Assets.canvas(128, 128, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      c.fillStyle = '#ffc23e';
      c.beginPath();
      c.moveTo(W / 2, 6); c.lineTo(W - 6, H / 2); c.lineTo(W / 2, H - 6); c.lineTo(6, H / 2);
      c.closePath(); c.fill();
      c.strokeStyle = '#2b2d36'; c.lineWidth = 5; c.stroke();
      c.fillStyle = '#2b2d36'; c.font = '900 30px "Baloo 2", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText('SLOW', W / 2, H / 2 - 12);
      c.font = '38px sans-serif';
      c.fillText('🚸', W / 2, H / 2 + 24);
    });
    return Assets.texs.schoolSign;
  },
  lamppost(night) {
    const g = new THREE.Group();
    const dark = Assets.lambert('poleDark', { color: 0x3a3d47 });
    const foot = new THREE.Mesh(Assets.geo('lampFoot', () => new THREE.CylinderGeometry(0.15, 0.19, 0.45, 10)), dark);
    foot.position.y = 0.22;
    const pole = new THREE.Mesh(Assets.geo('lampPole', () => new THREE.CylinderGeometry(0.055, 0.095, 4.3, 10)), dark);
    pole.position.y = 2.35;
    pole.castShadow = true;
    // swan-neck arm: quarter torus from the pole (0.1,3.6) up-and-out to the head apex (1.0,4.5)
    const arm = new THREE.Mesh(Assets.geo('lampArc', () => new THREE.TorusGeometry(0.9, 0.045, 6, 12, Math.PI / 2)), dark);
    arm.position.set(1.0, 3.6, 0);
    arm.rotation.z = Math.PI / 2;
    const shade = new THREE.Mesh(Assets.geo('lampShade', () => new THREE.CylinderGeometry(0.09, 0.28, 0.18, 10)), dark);
    shade.position.set(1.0, 4.46, 0);
    const bulb = new THREE.Mesh(
      Assets.geo('lampBulb', () => new THREE.SphereGeometry(0.13, 10, 8)),
      new THREE.MeshLambertMaterial({ color: 0x555555, emissive: 0xffe6a8, emissiveIntensity: night ? 2.8 : 0.05 })
    );
    bulb.position.set(1.0, 4.36, 0);
    g.add(foot, pole, arm, shade, bulb);
    if (night) {
      const glow = new THREE.Sprite(new THREE.SpriteMaterial({
        map: Assets.radialSprite('rgba(255,225,160,.9)'), transparent: true, depthWrite: false,
        blending: THREE.AdditiveBlending, opacity: 0.55,
      }));
      glow.scale.set(2.2, 2.2, 1);
      glow.position.set(1.0, 4.4, 0);
      g.add(glow);
      // soft radial light pool (a flat circle has a hard rim that reads as
      // a tan blob at night — gradient texture falls off to nothing)
      const poolLt = new THREE.Mesh(
        Assets.geo('lightPool2', () => new THREE.PlaneGeometry(7, 7)),
        new THREE.MeshBasicMaterial({
          map: Assets.radialSprite('rgba(255,224,160,.85)'),
          transparent: true, opacity: 0.4, depthWrite: false, blending: THREE.AdditiveBlending,
        })
      );
      poolLt.rotation.x = -Math.PI / 2;
      poolLt.position.set(1.0, 0.03, 0);
      g.add(poolLt);
    }
    return g;
  },
  // organic blob geometry: jittered sphere, position-hashed noise (no seam tears)
  blobGeo(key, seed) {
    return Assets.geo(key, () => {
      const geo = new THREE.SphereGeometry(1, 10, 8);
      const p = geo.attributes.position;
      for (let i = 0; i < p.count; i++) {
        const x = p.getX(i), y = p.getY(i), z = p.getZ(i);
        const n = Math.sin(x * 3.7 + seed) * Math.sin(y * 4.3 + seed * 2.1) * Math.sin(z * 3.1 + seed * 3.3);
        const d = 1 + n * 0.24;
        p.setXYZ(i, x * d, y * d, z * d);
      }
      geo.computeVertexNormals();
      return geo;
    });
  },
  barkMat() {
    if (!Assets.mats.bark) Assets.mats.bark = new THREE.MeshLambertMaterial({ map: Assets.barkTex() });
    return Assets.mats.bark;
  },
  // gable roof: two shingled slopes + siding gable ends + ridge cap.
  // Ridge runs along local X; sits with eaves at y=0.
  gableRoof(wide, depth, rise, shingleBase, gableColor) {
    const g = new THREE.Group();
    const w = wide / 2, d = depth / 2;
    const pos = [
      // +z slope
      -w, 0, d, w, 0, d, w, rise, 0, -w, rise, 0,
      // -z slope
      w, 0, -d, -w, 0, -d, -w, rise, 0, w, rise, 0,
      // +x gable end
      w, 0, d, w, 0, -d, w, rise, 0,
      // -x gable end
      -w, 0, -d, -w, 0, d, -w, rise, 0,
    ];
    const ru = wide / 1.7, rv = Math.hypot(rise, d) / 1.5;
    const uv = [
      0, 0, ru, 0, ru, rv, 0, rv,
      0, 0, ru, 0, ru, rv, 0, rv,
      0, 0, 1, 0, 0.5, 1,
      0, 0, 1, 0, 0.5, 1,
    ];
    const idx = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 11, 12, 13];
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    geo.setIndex(idx);
    geo.addGroup(0, 12, 0);  // slopes → shingles
    geo.addGroup(12, 6, 1);  // ends → siding
    geo.computeVertexNormals();
    const shingKey = 'shingM' + shingleBase;
    if (!Assets.mats[shingKey]) Assets.mats[shingKey] = new THREE.MeshLambertMaterial({ map: Assets.shingleTexture(shingleBase) });
    const mesh = new THREE.Mesh(geo, [Assets.mats[shingKey], new THREE.MeshLambertMaterial({ color: gableColor })]);
    mesh.castShadow = true;
    g.add(mesh);
    // ridge cap
    const cap = CarFactory.boxMesh(wide + 0.06, 0.1, 0.34, new THREE.Color(shingleBase).multiplyScalar(0.75).getHex());
    cap.position.y = rise + 0.02;
    g.add(cap);
    return g;
  },
  // shared per-tint leaf materials: light crown / dark underside / alpha card
  leafMats(tint) {
    const key = 'leafM' + tint;
    if (!Assets.mats[key]) {
      const dark = new THREE.Color(tint).multiplyScalar(0.66);
      Assets.mats[key] = {
        light: new THREE.MeshLambertMaterial({ color: tint, map: Assets.leafTex(), flatShading: true }),
        dark: new THREE.MeshLambertMaterial({ color: dark, map: Assets.leafTex(), flatShading: true }),
        cards: [0, 1, 2].map(v => new THREE.MeshLambertMaterial({
          color: tint, map: Assets.foliageCardTex(v), alphaTest: 0.45, side: THREE.DoubleSide,
        })),
      };
    }
    return Assets.mats[key];
  },
  tree(scale, tint) {
    const g = new THREE.Group();
    const lm = this.leafMats(tint || 0x4a9c4a);
    if (Math.random() < 0.22) {
      // ---- conifer: tapered trunk + jittered cone tiers, dark base tier ----
      const trunk = new THREE.Mesh(Assets.geo('pTrunk', () => new THREE.CylinderGeometry(0.09, 0.17, 1.9, 7)), this.barkMat());
      trunk.position.y = 0.95;
      trunk.castShadow = true;
      g.add(trunk);
      const coneGeo = Assets.geo('pineTier', () => {
        const geo = new THREE.ConeGeometry(1.05, 1.5, 9);
        const p = geo.attributes.position;
        for (let i = 0; i < p.count; i++) {
          const x = p.getX(i), z = p.getZ(i);
          const n = Math.sin(x * 5.1 + 2.7) * Math.sin(z * 4.7 + 1.3);
          p.setXYZ(i, x * (1 + n * 0.14), p.getY(i), z * (1 + n * 0.14));
        }
        geo.computeVertexNormals();
        return geo;
      });
      let ti = 0;
      for (const [y, s] of [[1.75, 1.0], [2.6, 0.72], [3.3, 0.46]]) {
        const tier = new THREE.Mesh(coneGeo, ti++ === 0 ? lm.dark : lm.light);
        tier.position.y = y;
        tier.scale.setScalar(s * rand(0.92, 1.08));
        tier.rotation.y = rand(0, TAU);
        tier.castShadow = true;
        g.add(tier);
      }
    } else {
      // ---- broadleaf: bark trunk + branches + volume blobs + foliage cards ----
      const trunk = new THREE.Mesh(Assets.geo('trunk3', () => {
        // root flare: wider foot reads far more like a real tree
        const geo = new THREE.CylinderGeometry(0.13, 0.3, 1.7, 8);
        const p = geo.attributes.position;
        for (let i = 0; i < p.count; i++) {
          const y = p.getY(i);
          if (y < -0.6) { // flare only the base
            const f = 1 + (-0.6 - y) * 0.35;
            p.setX(i, p.getX(i) * f); p.setZ(i, p.getZ(i) * f);
          }
        }
        geo.computeVertexNormals();
        return geo;
      }), this.barkMat());
      trunk.position.y = 0.85;
      trunk.castShadow = true;
      g.add(trunk);
      const brGeo = Assets.geo('branch', () => new THREE.CylinderGeometry(0.045, 0.085, 0.9, 5));
      for (const [bx, rz] of [[0.24, -0.75], [-0.22, 0.7]]) {
        const br = new THREE.Mesh(brGeo, this.barkMat());
        br.position.set(bx, 1.5, rand(-0.12, 0.12));
        br.rotation.z = rz;
        br.castShadow = true;
        g.add(br);
      }
      const blobs = [this.blobGeo('blob1', 1.3), this.blobGeo('blob2', 4.7), this.blobGeo('blob3', 8.1)];
      const clusters = [
        [0, 2.15, 0, 1.05, 0], [0.58, 1.85, 0.3, 0.66, 1], [-0.52, 1.9, -0.26, 0.7, 1],
        [0.16, 2.6, -0.36, 0.58, 0], [-0.2, 2.55, 0.4, 0.54, 0],
      ];
      for (const [cx, cy, cz, cs, lower] of clusters) {
        const b = new THREE.Mesh(pick(blobs), lower ? lm.dark : lm.light);
        b.position.set(cx + rand(-0.12, 0.12), cy + rand(-0.1, 0.1), cz + rand(-0.12, 0.12));
        b.scale.setScalar(cs * rand(0.9, 1.15) * 1.12);
        b.rotation.y = rand(0, TAU);
        b.castShadow = true;
        g.add(b);
      }
      // ragged foliage cards break the blob silhouette (no shadow: alphaTest
      // isn't respected by the depth material, they'd cast square shadows)
      const cardGeo = Assets.geo('fcard', () => new THREE.PlaneGeometry(1.9, 1.9));
      for (let i = 0; i < 5; i++) {
        const card = new THREE.Mesh(cardGeo, lm.cards[i % 3]);
        const a = rand(0, TAU), rr = rand(0.2, 0.75);
        card.position.set(Math.cos(a) * rr, rand(1.9, 2.75), Math.sin(a) * rr);
        card.rotation.set(rand(-0.35, 0.35), rand(0, TAU), rand(-0.2, 0.2));
        card.scale.setScalar(rand(0.8, 1.25));
        g.add(card);
      }
    }
    g.scale.setScalar(scale || 1);
    return g;
  },
  // layered bush: dark volume blob + light crown blob + a foliage card
  bush(tint) {
    const g = new THREE.Group();
    const lm = this.leafMats(tint || 0x4a8c3f);
    const base = new THREE.Mesh(this.blobGeo('blob2', 4.7), lm.dark);
    base.scale.set(0.55, 0.4, 0.55);
    base.position.y = 0.32;
    base.castShadow = true;
    g.add(base);
    const crown = new THREE.Mesh(this.blobGeo('blob1', 1.3), lm.light);
    crown.scale.set(0.38, 0.3, 0.38);
    crown.position.set(rand(-0.1, 0.1), 0.5, rand(-0.1, 0.1));
    crown.rotation.y = rand(0, TAU);
    crown.castShadow = true;
    g.add(crown);
    const card = new THREE.Mesh(Assets.geo('fcard', () => new THREE.PlaneGeometry(1.9, 1.9)), lm.cards[randi(0, 2)]);
    card.scale.setScalar(0.5);
    card.position.set(0, 0.5, 0);
    card.rotation.y = rand(0, TAU);
    g.add(card);
    return g;
  },
  trafficLight() {
    // returns {group, setState(0=green,1=amber,2=red)} facing -x by default
    const g = new THREE.Group();
    const pole = new THREE.Mesh(Assets.geo('tlPole', () => new THREE.CylinderGeometry(0.08, 0.1, 5.4, 6)), Assets.lambert('poleDark', { color: 0x3a3d47 }));
    pole.position.y = 2.7;
    pole.castShadow = true;
    const arm = new THREE.Mesh(Assets.geo('tlArm', () => new THREE.BoxGeometry(0.1, 0.1, 3.4)), Assets.lambert('poleDark', { color: 0x3a3d47 }));
    arm.position.set(0, 5.3, -1.6);
    const box = new THREE.Mesh(Assets.geo('tlBox', () => new THREE.BoxGeometry(0.34, 0.95, 0.34)), Assets.lambert('ink', { color: 0x23252c }));
    box.position.set(0, 4.85, -3.1);
    g.add(pole, arm, box);
    const bulbs = [];
    const colors = [0x3ecf6e, 0xffc23e, 0xff4757];
    const visorGeo = Assets.geo('tlVisor', () => new THREE.CylinderGeometry(0.14, 0.14, 0.2, 8, 1, true, 0, Math.PI));
    const visorMat = Assets.lambert('tlVisorM', { color: 0x1a1c22, side: THREE.DoubleSide });
    for (let i = 0; i < 3; i++) {
      const b = new THREE.Mesh(
        Assets.geo('tlBulb', () => new THREE.SphereGeometry(0.11, 10, 8)),
        new THREE.MeshLambertMaterial({ color: 0x333333, emissive: colors[i], emissiveIntensity: 0.06 })
      );
      b.position.set(-0.18, 5.15 - i * 0.3, -3.1);
      g.add(b);
      bulbs.push(b);
      // hood/visor over each lens
      const visor = new THREE.Mesh(visorGeo, visorMat);
      visor.rotation.z = Math.PI / 2;
      visor.position.set(-0.2, 5.17 - i * 0.3, -3.1);
      g.add(visor);
    }
    return {
      group: g,
      setState(s) { // 0 green 1 amber 2 red
        bulbs[0].material.emissiveIntensity = s === 0 ? 2.6 : 0.06;
        bulbs[1].material.emissiveIntensity = s === 1 ? 2.6 : 0.06;
        bulbs[2].material.emissiveIntensity = s === 2 ? 2.6 : 0.06;
      },
    };
  },
  bumpStrip(width) {
    const tex = Assets.hazardTex().clone();
    tex.needsUpdate = true;
    tex.repeat.set(1, Math.max(2, Math.round(width / 0.55)));
    const m = new THREE.Mesh(
      new THREE.CylinderGeometry(0.16, 0.16, width, 10, 1, false, 0, Math.PI),
      new THREE.MeshLambertMaterial({ map: tex })
    );
    m.rotation.z = Math.PI / 2;
    m.rotation.y = Math.PI / 2;
    m.position.y = 0.0;
    return m;
  },
  puddle(r) {
    const m = new THREE.Mesh(
      new THREE.CircleGeometry(r, 18),
      new THREE.MeshPhongMaterial({ color: 0x2e3e4e, shininess: 160, specular: 0xbfd8ea, transparent: true, opacity: 0.78 })
    );
    m.rotation.x = -Math.PI / 2;
    m.position.y = 0.025;
    return m;
  },
  neonSign(text, color) {
    const tex = Assets.canvas(256, 96, (c, W, H) => {
      c.clearRect(0, 0, W, H);
      c.font = '900 52px "Baloo 2", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.shadowColor = color; c.shadowBlur = 18;
      c.fillStyle = color;
      c.fillText(text, W / 2, H / 2);
      c.shadowBlur = 0;
      c.fillStyle = '#fff';
      c.globalAlpha = 0.8;
      c.font = '900 48px "Baloo 2", sans-serif';
      c.fillText(text, W / 2, H / 2);
      c.globalAlpha = 1;
    });
    const m = new THREE.Mesh(
      new THREE.PlaneGeometry(3.2, 1.2),
      // color > 1 pushes the sign into HDR so the bloom pass picks it up
      new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, color: new THREE.Color(2.2, 2.2, 2.2) })
    );
    return m;
  },

  // ---------- winter: layered pine, optional snow caps ----------
  pine(scale, snowy) {
    const g = new THREE.Group();
    const trunk = new THREE.Mesh(
      Assets.geo('pineTrunk', () => new THREE.CylinderGeometry(0.14, 0.26, 1.6, 7)),
      this.barkMat()
    );
    trunk.position.y = 0.8;
    trunk.castShadow = true;
    g.add(trunk);
    const layers = [[1.7, 1.9, 1.15], [1.35, 1.6, 2.15], [1.0, 1.4, 3.05], [0.62, 1.2, 3.9]];
    for (let i = 0; i < layers.length; i++) {
      const [r, h, y] = layers[i];
      const cone = new THREE.Mesh(
        Assets.geo('pineL' + i, () => new THREE.ConeGeometry(r, h, 9)),
        Assets.lambert('pineG' + (i % 2), { color: i % 2 === 0 ? 0x2c5c3c : 0x346a48 })
      );
      cone.position.y = y;
      cone.castShadow = true;
      g.add(cone);
      if (snowy) {
        const cap = new THREE.Mesh(
          Assets.geo('pineS' + i, () => new THREE.ConeGeometry(r * 0.82, h * 0.45, 9)),
          Assets.lambert('pineSnow', { color: 0xf2f7fc })
        );
        cap.position.y = y + h * 0.3;
        g.add(cap);
      }
    }
    g.scale.setScalar(scale || 1);
    return g;
  },

  snowman() {
    const g = new THREE.Group();
    const white = Assets.lambert('snowmanW', { color: 0xf4f8fd });
    const balls = [[0.52, 0.45], [0.38, 1.18], [0.27, 1.7]];
    for (let i = 0; i < 3; i++) {
      const [r, y] = balls[i];
      const b = new THREE.Mesh(Assets.geo('snowB' + i, () => new THREE.SphereGeometry(r, 12, 10)), white);
      b.position.y = y;
      b.castShadow = true;
      g.add(b);
    }
    const nose = new THREE.Mesh(
      Assets.geo('snowNose', () => new THREE.ConeGeometry(0.06, 0.34, 7)),
      Assets.lambert('carrot', { color: 0xe8792e })
    );
    nose.rotation.x = Math.PI / 2;
    nose.position.set(0, 1.72, 0.36);
    g.add(nose);
    const hatMat = Assets.lambert('snowHat', { color: 0x23252e });
    const brim = new THREE.Mesh(Assets.geo('snowBrim', () => new THREE.CylinderGeometry(0.3, 0.3, 0.04, 12)), hatMat);
    brim.position.y = 1.92;
    g.add(brim);
    const top = new THREE.Mesh(Assets.geo('snowTop', () => new THREE.CylinderGeometry(0.19, 0.2, 0.3, 12)), hatMat);
    top.position.y = 2.08;
    g.add(top);
    for (const sgn of [-1, 1]) { // stick arms
      const arm = CarFactory.boxMesh(0.05, 0.05, 0.62, 0x6b4a2e);
      arm.position.set(sgn * 0.58, 1.28, 0);
      arm.rotation.x = Math.PI / 2;
      arm.rotation.z = sgn * 0.5;
      g.add(arm);
    }
    return g;
  },

  // ---------- marina: sailboat / small yacht, bobbed by updateAmbient ----------
  boat(kind, color) {
    const g = new THREE.Group();
    const isYacht = kind === 'yacht';
    const len = isYacht ? 9 : 6, wid = isYacht ? 2.6 : 1.9;
    const hullC = color || (isYacht ? '#f4f6f8' : pick(['#d95a4e', '#3a76b8', '#3f8f5c', '#e8a33a', '#f4f6f8']));
    // tapered hull: box scaled narrower at bow via simple wedge stack
    const hull = CarFactory.boxMesh(len, 1.0, wid, hullC);
    hull.position.y = 0.5;
    hull.castShadow = true;
    g.add(hull);
    const bow = new THREE.Mesh(
      Assets.geo('boatBow' + (isYacht ? 'Y' : 'S'), () => new THREE.ConeGeometry(1, 2.2, 4)),
      new THREE.MeshLambertMaterial({ color: hullC })
    );
    bow.scale.set(wid / 2, 1, 1.0);
    bow.rotation.z = -Math.PI / 2;
    bow.rotation.x = Math.PI / 4;
    bow.position.set(len / 2 + 0.9, 0.5, 0);
    g.add(bow);
    const deck = CarFactory.boxMesh(len - 0.5, 0.14, wid - 0.4, 0xdcc9a0);
    deck.position.y = 1.05;
    g.add(deck);
    // waterline stripe
    const stripe = CarFactory.boxMesh(len + 0.04, 0.14, wid + 0.04, 0x23252e);
    stripe.position.y = 0.18;
    g.add(stripe);
    if (isYacht) {
      const cabin = CarFactory.boxMesh(len * 0.42, 1.0, wid * 0.72, 0xf4f6f8);
      cabin.position.set(-len * 0.08, 1.6, 0);
      g.add(cabin);
      const win = CarFactory.boxMesh(len * 0.42 + 0.06, 0.34, wid * 0.72 - 0.2, 0x2e4a66);
      win.position.set(-len * 0.08, 1.72, 0);
      g.add(win);
      const arch = CarFactory.boxMesh(0.16, 0.9, wid * 0.6, 0xe8eaee);
      arch.position.set(-len * 0.34, 2.4, 0);
      g.add(arch);
    } else {
      const mast = new THREE.Mesh(
        Assets.geo('boatMast', () => new THREE.CylinderGeometry(0.05, 0.07, 6.4, 7)),
        Assets.lambert('mastC', { color: 0xd8d4c8 })
      );
      mast.position.set(len * 0.1, 4.1, 0);
      g.add(mast);
      // main sail (triangle) + jib
      const sailMat = Assets.mats.sail || (Assets.mats.sail = new THREE.MeshLambertMaterial({ color: 0xfbfaf4, side: THREE.DoubleSide }));
      const sailGeo = Assets.geo('sailMain', () => {
        const sh = new THREE.Shape();
        sh.moveTo(0, 0); sh.lineTo(0, 5.4); sh.lineTo(-2.6, 0); sh.closePath();
        return new THREE.ShapeGeometry(sh);
      });
      const sail = new THREE.Mesh(sailGeo, sailMat);
      sail.position.set(len * 0.1 - 0.06, 1.35, 0);
      g.add(sail);
      const jibGeo = Assets.geo('sailJib', () => {
        const sh = new THREE.Shape();
        sh.moveTo(0, 0); sh.lineTo(0, 4.2); sh.lineTo(1.9, 0); sh.closePath();
        return new THREE.ShapeGeometry(sh);
      });
      const jib = new THREE.Mesh(jibGeo, sailMat);
      jib.position.set(len * 0.1 + 0.12, 1.35, 0);
      g.add(jib);
      const flag = CarFactory.boxMesh(0.5, 0.28, 0.03, 0xd9403a);
      flag.position.set(len * 0.1 - 0.28, 7.15, 0);
      g.add(flag);
    }
    return g;
  },

  buoy() {
    const g = new THREE.Group();
    const body = new THREE.Mesh(
      Assets.geo('buoyB', () => new THREE.SphereGeometry(0.5, 10, 8)),
      Assets.lambert('buoyR', { color: 0xd9403a })
    );
    body.position.y = 0.3;
    g.add(body);
    const pole = new THREE.Mesh(
      Assets.geo('buoyP', () => new THREE.CylinderGeometry(0.05, 0.05, 1.1, 6)),
      Assets.lambert('buoyW', { color: 0xf4f1ea })
    );
    pole.position.y = 1.0;
    g.add(pole);
    const lamp = CarFactory.lightBox(0.16, 0.16, 0.16, 0xffe14d);
    lamp.position.y = 1.6;
    lamp.material.emissiveIntensity = 1.6;
    g.add(lamp);
    return g;
  },

  // returns { group, beam } — beam is a Group of two additive fans, rotated by updateAmbient
  lighthouse() {
    const g = new THREE.Group();
    const tower = new THREE.Mesh(
      Assets.geo('lhTower', () => new THREE.CylinderGeometry(1.5, 2.3, 15, 14)),
      new THREE.MeshLambertMaterial({ map: Assets.lightStripeTex() })
    );
    tower.position.y = 7.5;
    tower.castShadow = true;
    g.add(tower);
    const gallery = new THREE.Mesh(
      Assets.geo('lhGal', () => new THREE.CylinderGeometry(1.9, 1.9, 0.4, 14)),
      Assets.lambert('lhDark', { color: 0x2e3038 })
    );
    gallery.position.y = 15.2;
    g.add(gallery);
    const lampRoom = new THREE.Mesh(
      Assets.geo('lhLamp', () => new THREE.CylinderGeometry(1.1, 1.1, 1.6, 10)),
      new THREE.MeshStandardMaterial({ color: 0xfff2b8, emissive: 0xffdf80, emissiveIntensity: 1.8, roughness: 0.3 })
    );
    lampRoom.position.y = 16.2;
    g.add(lampRoom);
    const roof = new THREE.Mesh(
      Assets.geo('lhRoof', () => new THREE.ConeGeometry(1.5, 1.3, 10)),
      Assets.lambert('lhRed', { color: 0xd9403a })
    );
    roof.position.y = 17.6;
    g.add(roof);
    // rotating light fans
    const beam = new THREE.Group();
    const fanGeo = Assets.geo('lhFan', () => {
      const geo = new THREE.PlaneGeometry(30, 3.2);
      geo.translate(15, 0, 0); // pivot at lamp, fan reaches outward
      return geo;
    });
    const fanMat = new THREE.MeshBasicMaterial({
      map: Assets.radialSprite('rgba(255,236,170,.8)'), transparent: true, opacity: 0.4,
      blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide, fog: false,
    });
    for (const rot of [0, Math.PI]) {
      const fan = new THREE.Mesh(fanGeo, fanMat);
      fan.rotation.y = rot;
      beam.add(fan);
    }
    beam.position.y = 16.2;
    g.add(beam);
    return { group: g, beam };
  },

  icePatch(r) {
    const m = new THREE.Mesh(
      new THREE.CircleGeometry(r, 18),
      new THREE.MeshPhongMaterial({
        color: 0xcfe9f8, shininess: 210, specular: 0xffffff,
        transparent: true, opacity: 0.5,
      })
    );
    m.rotation.x = -Math.PI / 2;
    m.position.y = 0.026;
    return m;
  },

  // ============================================================
  // v5 STREET LIFE — sidewalk furniture, vendors, plazas, landmarks
  // ============================================================
  // striped awning / canopy fabric, cached by colour pair + direction
  stripeTex(a, b, vert) {
    const key = 'stripe_' + a + '_' + b + (vert ? 'v' : 'h');
    if (Assets.texs[key]) return Assets.texs[key];
    const tex = Assets.canvas(64, 64, (c, W, H) => {
      for (let i = 0; i < 8; i++) {
        c.fillStyle = i % 2 ? a : b;
        if (vert) c.fillRect(i * W / 8, 0, W / 8, H);
        else c.fillRect(0, i * H / 8, W, H / 8);
      }
    });
    Assets.texs[key] = tex;
    return tex;
  },
  clockFaceTex() {
    if (Assets.texs.clockFace) return Assets.texs.clockFace;
    const tex = Assets.canvas(128, 128, (c) => {
      c.fillStyle = '#f4f1e8'; c.beginPath(); c.arc(64, 64, 60, 0, TAU); c.fill();
      c.strokeStyle = '#1f2833'; c.lineWidth = 5; c.beginPath(); c.arc(64, 64, 60, 0, TAU); c.stroke();
      for (let i = 0; i < 12; i++) {
        const a = i / 12 * TAU;
        c.lineWidth = i % 3 === 0 ? 5 : 2;
        c.beginPath();
        c.moveTo(64 + Math.cos(a) * 52, 64 + Math.sin(a) * 52);
        c.lineTo(64 + Math.cos(a) * 45, 64 + Math.sin(a) * 45);
        c.stroke();
      }
      c.strokeStyle = '#1f2833'; c.lineCap = 'round';
      c.lineWidth = 6; c.beginPath(); c.moveTo(64, 64); c.lineTo(64 + Math.cos(-2.0) * 28, 64 + Math.sin(-2.0) * 28); c.stroke();
      c.lineWidth = 4; c.beginPath(); c.moveTo(64, 64); c.lineTo(64 + Math.cos(0.5) * 42, 64 + Math.sin(0.5) * 42); c.stroke();
      c.fillStyle = '#1f2833'; c.beginPath(); c.arc(64, 64, 4, 0, TAU); c.fill();
    });
    Assets.texs.clockFace = tex;
    return tex;
  },

  // outdoor café: bistro table, two chairs, striped parasol
  cafeSet() {
    const g = new THREE.Group();
    const metal = Assets.lambert('cafeMetal', { color: 0x3a3d44 });
    const top = new THREE.Mesh(Assets.geo('cafeTop', () => new THREE.CylinderGeometry(0.42, 0.42, 0.05, 16)), Assets.lambert('cafeTopM', { color: 0xf2efe6 }));
    top.position.y = 0.72; g.add(top);
    const stem = new THREE.Mesh(Assets.geo('cafeStem', () => new THREE.CylinderGeometry(0.05, 0.05, 0.72, 8)), metal);
    stem.position.y = 0.36; g.add(stem);
    const foot = new THREE.Mesh(Assets.geo('cafeFoot', () => new THREE.CylinderGeometry(0.26, 0.26, 0.04, 12)), metal);
    foot.position.y = 0.02; g.add(foot);
    const chairC = pick([0xc94f3d, 0x2e6fb0, 0x3f9f52, 0xe8a33a]);
    for (const ang of [0.5, Math.PI - 0.5]) {
      const ch = new THREE.Group();
      const seat = new THREE.Mesh(Assets.geo('cafeSeat', () => new THREE.CylinderGeometry(0.2, 0.2, 0.04, 12)), Assets.lambert('cafeChair' + chairC, { color: chairC }));
      seat.position.y = 0.44; ch.add(seat);
      const back = CarFactory.boxMesh(0.36, 0.34, 0.04, chairC);
      back.position.set(0, 0.62, -0.18); ch.add(back);
      for (const [lx, lz] of [[-0.15, -0.15], [0.15, -0.15], [-0.15, 0.15], [0.15, 0.15]]) {
        const leg = new THREE.Mesh(Assets.geo('cafeLeg', () => new THREE.CylinderGeometry(0.02, 0.02, 0.44, 6)), metal);
        leg.position.set(lx, 0.22, lz); ch.add(leg);
      }
      ch.position.set(Math.cos(ang) * 0.62, 0, Math.sin(ang) * 0.62);
      ch.rotation.y = -ang;
      g.add(ch);
    }
    const pole = new THREE.Mesh(Assets.geo('parPole', () => new THREE.CylinderGeometry(0.04, 0.04, 2.4, 8)), Assets.lambert('parPoleM', { color: 0x6b6f78 }));
    pole.position.y = 1.2; g.add(pole);
    const canopy = new THREE.Mesh(
      Assets.geo('parCanopy', () => new THREE.ConeGeometry(1.5, 0.6, 16)),
      new THREE.MeshLambertMaterial({ map: this.stripeTex('#e8574a', '#f6f2e8', true), side: THREE.DoubleSide })
    );
    canopy.position.y = 2.35; g.add(canopy);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // glass-sided bus shelter with a colour roof, bench + info totem
  busShelter() {
    const g = new THREE.Group();
    const glass = CarFactory.glass();
    const metal = Assets.lambert('busMetal', { color: 0x3f4650 });
    const back = new THREE.Mesh(Assets.geo('busBack', () => new THREE.BoxGeometry(2.6, 1.9, 0.06)), glass);
    back.position.set(0, 1.1, -0.5); g.add(back);
    for (const sx of [-1.3, 1.3]) {
      const side = new THREE.Mesh(Assets.geo('busSide', () => new THREE.BoxGeometry(0.06, 1.9, 1.0)), glass);
      side.position.set(sx, 1.1, 0); g.add(side);
    }
    for (const [px, pz] of [[-1.3, -0.5], [1.3, -0.5], [-1.3, 0.5], [1.3, 0.5]]) {
      const post = new THREE.Mesh(Assets.geo('busPost', () => new THREE.BoxGeometry(0.09, 2.1, 0.09)), metal);
      post.position.set(px, 1.05, pz); g.add(post);
    }
    const roof = new THREE.Mesh(Assets.geo('busRoof', () => new THREE.BoxGeometry(2.9, 0.12, 1.3)), Assets.lambert('busRoofM', { color: 0xd23b34 }));
    roof.position.y = 2.16; g.add(roof);
    const bseat = CarFactory.boxMesh(2.2, 0.06, 0.32, 0x6b7078);
    bseat.position.set(0, 0.5, -0.32); g.add(bseat);
    const tp = new THREE.Mesh(Assets.geo('busTotemP', () => new THREE.CylinderGeometry(0.06, 0.06, 2.6, 8)), metal);
    tp.position.set(1.62, 1.3, 0.3); g.add(tp);
    const panel = new THREE.Mesh(Assets.geo('busPanel', () => new THREE.BoxGeometry(0.55, 0.8, 0.05)),
      new THREE.MeshStandardMaterial({ color: 0x1e6fb0, emissive: 0x1e6fb0, emissiveIntensity: 0.4, roughness: 0.5 }));
    panel.position.set(1.62, 2.35, 0.3); g.add(panel);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // wooden planter box with clipped hedge + blooms; long doubles the length
  planter(long) {
    const g = new THREE.Group();
    const L = long ? 2.2 : 1.0;
    const box = new THREE.Mesh(Assets.geo('planterBox' + (long ? 'L' : 'S'), () => new THREE.BoxGeometry(L, 0.5, 0.5)), this.woodMat());
    box.position.y = 0.25; g.add(box);
    const rim = CarFactory.boxMesh(L + 0.08, 0.08, 0.58, 0x6b4a2e);
    rim.position.y = 0.5; g.add(rim);
    const hedge = new THREE.Mesh(Assets.geo('planterHedge' + (long ? 'L' : 'S'), () => new THREE.BoxGeometry(L - 0.1, 0.36, 0.4)), Assets.lambert('hedgeG', { color: 0x3f8f46 }));
    hedge.position.y = 0.66; g.add(hedge);
    const n = long ? 6 : 3;
    for (let i = 0; i < n; i++) {
      const bl = new THREE.Mesh(Assets.geo('planterBloom', () => new THREE.SphereGeometry(0.08, 7, 6)), new THREE.MeshLambertMaterial({ color: pick([0xff5a7a, 0xffd24a, 0xff8a3a, 0xe85ad0]) }));
      bl.position.set(-L / 2 + 0.2 + i * (L - 0.4) / (n - 1 || 1), 0.86, rand(-0.12, 0.12));
      g.add(bl);
    }
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // short protective post with a reflective band
  bollard() {
    const g = new THREE.Group();
    const dark = Assets.lambert('bollardM', { color: 0x2b2d36 });
    const post = new THREE.Mesh(Assets.geo('bollardP', () => new THREE.CylinderGeometry(0.1, 0.12, 0.9, 10)), dark);
    post.position.y = 0.45; g.add(post);
    const cap = new THREE.Mesh(Assets.geo('bollardC', () => new THREE.SphereGeometry(0.1, 10, 6)), dark);
    cap.position.y = 0.9; g.add(cap);
    const band = new THREE.Mesh(Assets.geo('bollardB', () => new THREE.CylinderGeometry(0.11, 0.11, 0.13, 10)),
      Assets.lambert('bollardBand', { color: 0xf4d03a }));
    band.position.y = 0.72; g.add(band);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // U-hoop bike rack with one leaning bicycle
  bikeRack() {
    const g = new THREE.Group();
    const metal = Assets.lambert('rackM', { color: 0x5a6068 });
    for (const rx of [-0.55, 0.55]) {
      const bar = new THREE.Mesh(Assets.geo('rackBar', () => new THREE.TorusGeometry(0.4, 0.03, 6, 14, Math.PI)), metal);
      bar.position.set(rx, 0.0, 0); g.add(bar);
    }
    const bcol = pick([0xd23b34, 0x2e6fb0, 0x3f9f52, 0xf4a72e]);
    const bike = new THREE.Group();
    for (const wx of [-0.5, 0.5]) {
      const wheel = new THREE.Mesh(Assets.geo('bikeWheel', () => new THREE.TorusGeometry(0.32, 0.035, 6, 16)), Assets.lambert('bikeTire', { color: 0x1a1c22 }));
      wheel.position.set(wx, 0.32, 0); bike.add(wheel);
    }
    const frame = CarFactory.boxMesh(0.85, 0.05, 0.05, bcol);
    frame.position.set(0, 0.5, 0); bike.add(frame);
    const seatpost = CarFactory.boxMesh(0.05, 0.32, 0.05, bcol);
    seatpost.position.set(-0.34, 0.6, 0); bike.add(seatpost);
    const bars = CarFactory.boxMesh(0.05, 0.36, 0.05, bcol);
    bars.position.set(0.45, 0.62, 0); bike.add(bars);
    const seat = CarFactory.boxMesh(0.22, 0.05, 0.1, 0x1a1c22);
    seat.position.set(-0.34, 0.78, 0); bike.add(seat);
    bike.rotation.z = 0.07; bike.position.z = 0.12;
    g.add(bike);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // street-food vendor cart with striped canopy + menu board
  foodCart() {
    const g = new THREE.Group();
    const body = CarFactory.boxMesh(1.6, 0.9, 0.9, 0xf0ede4);
    body.position.y = 0.75; g.add(body);
    const trim = CarFactory.boxMesh(1.64, 0.2, 0.94, 0xd23b34);
    trim.position.y = 1.12; g.add(trim);
    const counter = CarFactory.boxMesh(1.72, 0.06, 1.02, 0xcaa46a);
    counter.position.y = 1.26; g.add(counter);
    const canopy = new THREE.Mesh(Assets.geo('cartCanopy', () => new THREE.BoxGeometry(1.9, 0.06, 1.2)),
      new THREE.MeshLambertMaterial({ map: this.stripeTex('#e8574a', '#f6f2e8', true) }));
    canopy.position.y = 2.0; g.add(canopy);
    for (const px of [-0.82, 0.82]) {
      const post = new THREE.Mesh(Assets.geo('cartPost', () => new THREE.CylinderGeometry(0.03, 0.03, 0.74, 6)), Assets.lambert('cartMetal', { color: 0x9aa0a8 }));
      post.position.set(px, 1.63, 0.4); g.add(post);
    }
    for (const wx of [-0.55, 0.55]) {
      const wheel = new THREE.Mesh(Assets.geo('cartWheel', () => new THREE.CylinderGeometry(0.28, 0.28, 0.08, 12)), Assets.lambert('cartTire', { color: 0x24262e }));
      wheel.rotation.x = Math.PI / 2; wheel.position.set(wx, 0.28, -0.5); g.add(wheel);
    }
    const board = CarFactory.boxMesh(0.7, 0.42, 0.04, 0x2e2620);
    board.position.set(0, 1.72, -0.48); g.add(board);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // corner newsstand / kiosk with a striped awning
  kiosk() {
    const g = new THREE.Group();
    const body = CarFactory.boxMesh(2.0, 2.3, 1.4, 0x3f5a4a);
    body.position.y = 1.15; g.add(body);
    const open = CarFactory.boxMesh(1.6, 1.0, 0.06, 0x14161a);
    open.position.set(0, 1.45, 0.71); g.add(open);
    const counter = CarFactory.boxMesh(1.7, 0.1, 0.4, 0xcaa46a);
    counter.position.set(0, 0.92, 0.85); g.add(counter);
    const aw = new THREE.Mesh(Assets.geo('kioskAw', () => new THREE.BoxGeometry(2.3, 0.06, 0.7)),
      new THREE.MeshLambertMaterial({ map: this.stripeTex('#2e6fb0', '#f6f2e8', true) }));
    aw.position.set(0, 2.02, 0.92); aw.rotation.x = 0.3; g.add(aw);
    const roof = new THREE.Mesh(Assets.geo('kioskRoof', () => new THREE.BoxGeometry(2.2, 0.14, 1.6)), Assets.lambert('kioskRoofM', { color: 0x2b3a30 }));
    roof.position.y = 2.4; g.add(roof);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // striped storefront awning to graft onto a city building's ground floor
  awning(width, colorA) {
    const g = new THREE.Group();
    const w = width || 3.2;
    const mat = new THREE.MeshLambertMaterial({ map: this.stripeTex(colorA || '#c94f3d', '#f6f2e8', true), side: THREE.DoubleSide });
    const cloth = new THREE.Mesh(Assets.geo('awnCloth' + Math.round(w * 10), () => new THREE.BoxGeometry(w, 0.06, 1.15)), mat);
    cloth.rotation.x = 0.5; cloth.position.set(0, 0, 0.5); g.add(cloth);
    const val = new THREE.Mesh(Assets.geo('awnVal' + Math.round(w * 10), () => new THREE.BoxGeometry(w, 0.24, 0.04)), mat);
    val.position.set(0, -0.3, 1.02); g.add(val);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // potted shrub for doorways / storefronts
  potPlant() {
    const g = new THREE.Group();
    const pot = new THREE.Mesh(Assets.geo('potP', () => new THREE.CylinderGeometry(0.24, 0.18, 0.42, 12)), Assets.lambert('potTerra', { color: 0xb0653f }));
    pot.position.y = 0.21; g.add(pot);
    const foliage = new THREE.Mesh(Assets.geo('potFol', () => new THREE.IcosahedronGeometry(0.34, 0)), Assets.lambert('potGreen', { color: 0x3f8f46 }));
    foliage.position.y = 0.72; foliage.scale.y = 1.3; g.add(foliage);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // ornate two-faced street clock — a plaza landmark
  streetClock() {
    const g = new THREE.Group();
    const dark = Assets.lambert('clockDark', { color: 0x1f2833 });
    const post = new THREE.Mesh(Assets.geo('clkPost', () => new THREE.CylinderGeometry(0.1, 0.15, 3.2, 12)), dark);
    post.position.y = 1.6; g.add(post);
    const base = new THREE.Mesh(Assets.geo('clkBase', () => new THREE.CylinderGeometry(0.28, 0.34, 0.4, 12)), dark);
    base.position.y = 0.2; g.add(base);
    const gold = new THREE.MeshStandardMaterial({ color: 0xd8b24a, metalness: 0.8, roughness: 0.35 });
    for (const sx of [-1, 1]) {
      const head = new THREE.Mesh(Assets.geo('clkHead', () => new THREE.CylinderGeometry(0.44, 0.44, 0.18, 20)), gold);
      head.rotation.z = Math.PI / 2;
      head.position.set(sx * 0.42, 3.35, 0);
      g.add(head);
      const face = new THREE.Mesh(Assets.geo('clkFace', () => new THREE.CircleGeometry(0.37, 20)),
        new THREE.MeshBasicMaterial({ map: this.clockFaceTex() }));
      face.position.set(sx * 0.52, 3.35, 0);
      face.rotation.y = sx > 0 ? Math.PI / 2 : -Math.PI / 2;
      g.add(face);
    }
    const finial = new THREE.Mesh(Assets.geo('clkFin', () => new THREE.ConeGeometry(0.14, 0.4, 10)), gold);
    finial.position.y = 3.95; g.add(finial);
    g.traverse(o => { if (o.isMesh) o.castShadow = true; });
    return g;
  },

  // flag on a pole — returns { group, flag } so the world can wave the cloth
  flagPole(color) {
    const g = new THREE.Group();
    const pole = new THREE.Mesh(Assets.geo('flagPole', () => new THREE.CylinderGeometry(0.06, 0.08, 6.0, 10)),
      new THREE.MeshStandardMaterial({ color: 0xdadfe6, metalness: 0.6, roughness: 0.4 }));
    pole.position.y = 3.0; pole.castShadow = true; g.add(pole);
    const ball = new THREE.Mesh(Assets.geo('flagBall', () => new THREE.SphereGeometry(0.12, 10, 8)),
      new THREE.MeshStandardMaterial({ color: 0xd8b24a, metalness: 0.8, roughness: 0.3 }));
    ball.position.y = 6.05; g.add(ball);
    const fgeo = new THREE.PlaneGeometry(2.4, 1.4, 10, 1);
    fgeo.translate(1.2, 0, 0); // pivot at the pole
    const flag = new THREE.Mesh(fgeo, new THREE.MeshLambertMaterial({ color: color || 0xd23b34, side: THREE.DoubleSide }));
    flag.position.set(0.06, 5.2, 0);
    g.add(flag);
    return { group: g, flag };
  },
};

// ============================================================
// COMIC TEXT — 3D popup sprites ("BONK!", "+50 STYLE")
// ============================================================
class ComicText3D {
  constructor(scene) {
    this.scene = scene;
    this.pool = [];
    this.cache = {};
  }
  spawn(text, x, y, z, color, big) {
    let tex = this.cache[text + (color || '')];
    if (!tex) {
      tex = Assets.textTexture(text, color || '#ffc23e');
      const keys = Object.keys(this.cache);
      if (keys.length > 40) { this.cache[keys[0]].dispose(); delete this.cache[keys[0]]; }
      this.cache[text + (color || '')] = tex;
    }
    let sp = this.pool.find(p => !p.active);
    if (!sp) {
      sp = { sprite: new THREE.Sprite(new THREE.SpriteMaterial({ transparent: true, depthTest: false })), active: false };
      this.scene.add(sp.sprite);
      this.pool.push(sp);
      if (this.pool.length > 14) { const old = this.pool.shift(); this.scene.remove(old.sprite); }
    }
    sp.active = true;
    sp.life = 0;
    sp.maxLife = 1.15;
    sp.baseY = y;
    sp.big = big ? 1.5 : 1;
    sp.sprite.material.map = tex;
    sp.sprite.material.opacity = 1;
    sp.sprite.position.set(x, y, z);
    sp.sprite.visible = true;
    sp.sprite.renderOrder = 999;
  }
  update(dt) {
    for (const sp of this.pool) {
      if (!sp.active) continue;
      sp.life += dt;
      const t = sp.life / sp.maxLife;
      if (t >= 1) { sp.active = false; sp.sprite.visible = false; continue; }
      const pop = t < 0.18 ? (t / 0.18) * 1.15 : (t < 0.3 ? 1.15 - (t - 0.18) : 1);
      const s = 3.2 * sp.big * pop;
      sp.sprite.scale.set(s, s * 0.31, 1);
      sp.sprite.position.y = sp.baseY + t * 1.6;
      sp.sprite.material.opacity = t > 0.7 ? 1 - (t - 0.7) / 0.3 : 1;
    }
  }
  clear() { for (const sp of this.pool) { sp.active = false; sp.sprite.visible = false; } }
}

// ============================================================
// PARTICLES — sparks (instanced), smoke sprites, confetti, rain
// ============================================================
class Particles3D {
  constructor(scene) {
    this.scene = scene;
    // sparks / debris: instanced cubes
    this.sparkN = 100;
    this.sparkMesh = new THREE.InstancedMesh(
      new THREE.BoxGeometry(0.09, 0.09, 0.09),
      new THREE.MeshBasicMaterial({ color: 0xffffff }),
      this.sparkN
    );
    this.sparkMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.sparkMesh.frustumCulled = false;
    this.sparks = [];
    for (let i = 0; i < this.sparkN; i++) this.sparks.push({ life: 0 });
    this.sparkMesh.count = this.sparkN;
    if (!this.sparkMesh.instanceColor) this.sparkMesh.setColorAt(0, new THREE.Color(1, 1, 1));
    scene.add(this.sparkMesh);
    this._dummy = new THREE.Object3D();
    this._col = new THREE.Color();
    // smoke: sprites
    this.smoke = [];
    for (let i = 0; i < 40; i++) {
      const sp = new THREE.Sprite(new THREE.SpriteMaterial({
        map: Assets.radialSprite('rgba(200,200,205,.65)'), transparent: true, depthWrite: false, opacity: 0,
      }));
      sp.visible = false;
      scene.add(sp);
      this.smoke.push({ sprite: sp, life: 0, maxLife: 1 });
    }
    // confetti
    this.confN = 150;
    this.confMesh = new THREE.InstancedMesh(
      new THREE.PlaneGeometry(0.16, 0.1),
      new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, vertexColors: false, color: 0xffffff }),
      this.confN
    );
    this.confMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    this.confMesh.frustumCulled = false;
    this.confetti = [];
    for (let i = 0; i < this.confN; i++) this.confetti.push({ life: 0 });
    scene.add(this.confMesh);
    const confColors = [0xff6b57, 0x3aa6ff, 0xffc23e, 0x3ecf6e, 0xc94f7c, 0x8a6ab8];
    for (let i = 0; i < this.confN; i++) this.confMesh.setColorAt(i, this._col.setHex(pick(confColors)));
    if (this.confMesh.instanceColor) this.confMesh.instanceColor.needsUpdate = true;
    this.hideAll();
  }
  hideAll() {
    this._dummy.position.set(0, -100, 0);
    this._dummy.scale.setScalar(0.001);
    this._dummy.updateMatrix();
    for (let i = 0; i < this.sparkN; i++) this.sparkMesh.setMatrixAt(i, this._dummy.matrix);
    for (let i = 0; i < this.confN; i++) this.confMesh.setMatrixAt(i, this._dummy.matrix);
    this.sparkMesh.instanceMatrix.needsUpdate = true;
    this.confMesh.instanceMatrix.needsUpdate = true;
  }
  burst(x, y, z, n, opts) {
    opts = opts || {};
    let spawned = 0;
    for (const p of this.sparks) {
      if (p.life > 0) continue;
      p.life = p.maxLife = rand(0.3, opts.life || 0.7);
      p.x = x; p.y = y; p.z = z;
      const a = rand(0, TAU), sp = rand(2, opts.speed || 7);
      p.vx = Math.cos(a) * sp; p.vz = Math.sin(a) * sp;
      p.vy = rand(1.5, opts.up || 5);
      p.color = opts.color || 0xffd77a;
      if (++spawned >= n) break;
    }
  }
  puff(x, y, z, scale) {
    for (const s of this.smoke) {
      if (s.life > 0) continue;
      s.life = s.maxLife = rand(0.5, 0.9);
      s.sprite.position.set(x, y, z);
      s.scale0 = (scale || 1) * rand(0.5, 0.8);
      s.sprite.visible = true;
      return;
    }
  }
  confettiBurst(x, y, z) {
    for (const p of this.confetti) {
      p.life = p.maxLife = rand(1.4, 2.6);
      p.x = x + rand(-1, 1); p.y = y + rand(0, 2); p.z = z + rand(-1, 1);
      const a = rand(0, TAU), sp = rand(1, 5);
      p.vx = Math.cos(a) * sp; p.vz = Math.sin(a) * sp;
      p.vy = rand(3, 8);
      p.rot = rand(0, TAU); p.rotV = rand(-6, 6);
    }
  }
  update(dt) {
    const d = this._dummy;
    let dirty = false;
    for (let i = 0; i < this.sparkN; i++) {
      const p = this.sparks[i];
      if (p.life <= 0) continue;
      dirty = true;
      p.life -= dt;
      p.vy -= 14 * dt;
      p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
      if (p.y < 0.04) { p.y = 0.04; p.vy *= -0.4; p.vx *= 0.7; p.vz *= 0.7; }
      if (p.life <= 0) { d.position.set(0, -100, 0); d.scale.setScalar(0.001); }
      else {
        d.position.set(p.x, p.y, p.z);
        d.scale.setScalar(clamp(p.life / p.maxLife, 0.2, 1));
        d.rotation.set(p.life * 7, p.life * 9, 0);
      }
      d.updateMatrix();
      this.sparkMesh.setMatrixAt(i, d.matrix);
    }
    if (dirty) this.sparkMesh.instanceMatrix.needsUpdate = true;
    for (const s of this.smoke) {
      if (s.life <= 0) continue;
      s.life -= dt;
      if (s.life <= 0) { s.sprite.visible = false; continue; }
      const t = 1 - s.life / s.maxLife;
      s.sprite.scale.setScalar(s.scale0 * (0.6 + t * 2.2));
      s.sprite.material.opacity = 0.55 * (1 - t);
      s.sprite.position.y += dt * 0.8;
    }
    let cDirty = false;
    for (let i = 0; i < this.confN; i++) {
      const p = this.confetti[i];
      if (p.life <= 0) continue;
      cDirty = true;
      p.life -= dt;
      p.vy -= 6 * dt;
      p.vx *= (1 - 0.8 * dt); p.vz *= (1 - 0.8 * dt);
      p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
      p.rot += p.rotV * dt;
      if (p.y < 0.03) { p.y = 0.03; p.vy = 0; p.rotV *= 0.9; }
      if (p.life <= 0) { d.position.set(0, -100, 0); d.scale.setScalar(0.001); }
      else {
        d.position.set(p.x, p.y, p.z);
        d.scale.setScalar(1);
        d.rotation.set(p.rot, p.rot * 0.7, p.rot * 1.3);
      }
      d.updateMatrix();
      this.confMesh.setMatrixAt(i, d.matrix);
    }
    if (cDirty) this.confMesh.instanceMatrix.needsUpdate = true;
  }
}

// ============================================================
// SKID MARKS — one dynamic geometry, quads under sliding wheels
// ============================================================
class SkidMarks {
  constructor(scene, max) {
    this.max = max || 260;
    this.idx = 0;
    const geo = new THREE.BufferGeometry();
    const pos = new Float32Array(this.max * 4 * 3);
    const idxArr = [];
    for (let i = 0; i < this.max; i++) {
      const b = i * 4;
      idxArr.push(b, b + 1, b + 2, b, b + 2, b + 3);
    }
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3).setUsage(THREE.DynamicDrawUsage));
    geo.setIndex(idxArr);
    this.mesh = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({
      color: 0x1a1c22, transparent: true, opacity: 0.34, depthWrite: false,
      polygonOffset: true, polygonOffsetFactor: -2,
    }));
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = 2;
    scene.add(this.mesh);
    this.lastPos = {};
    this.clear();
  }
  clear() {
    const pos = this.mesh.geometry.attributes.position.array;
    pos.fill(0);
    this.mesh.geometry.attributes.position.needsUpdate = true;
    this.lastPos = {};
  }
  // lay a quad from last position to current for wheel id
  add(id, x, z, w) {
    const lp = this.lastPos[id];
    this.lastPos[id] = { x, z };
    if (!lp) return;
    const dx = x - lp.x, dz = z - lp.z;
    const len = Math.hypot(dx, dz);
    if (len < 0.06 || len > 3) return;
    const nx = -dz / len * (w / 2), nz = dx / len * (w / 2);
    const pos = this.mesh.geometry.attributes.position.array;
    const b = this.idx * 12;
    const y = 0.028;
    pos[b] = lp.x + nx; pos[b + 1] = y; pos[b + 2] = lp.z + nz;
    pos[b + 3] = lp.x - nx; pos[b + 4] = y; pos[b + 5] = lp.z - nz;
    pos[b + 6] = x - nx; pos[b + 7] = y; pos[b + 8] = z - nz;
    pos[b + 9] = x + nx; pos[b + 10] = y; pos[b + 11] = z + nz;
    this.mesh.geometry.attributes.position.needsUpdate = true;
    this.idx = (this.idx + 1) % this.max;
  }
  release(id) { delete this.lastPos[id]; }
}

// ============================================================
// RAIN
// ============================================================
class Rain {
  constructor(scene) {
    this.n = 700;
    const geo = new THREE.BufferGeometry();
    const pos = new Float32Array(this.n * 3);
    for (let i = 0; i < this.n; i++) {
      pos[i * 3] = rand(-40, 40);
      pos[i * 3 + 1] = rand(0, 26);
      pos[i * 3 + 2] = rand(-40, 40);
    }
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3).setUsage(THREE.DynamicDrawUsage));
    this.pts = new THREE.Points(geo, new THREE.PointsMaterial({
      color: 0x9db8d9, size: 0.14, transparent: true, opacity: 0.6, sizeAttenuation: true,
      map: Assets.radialSprite('rgba(255,255,255,1)'), depthWrite: false, // soft round drops/flakes
    }));
    this.pts.frustumCulled = false;
    this.pts.visible = false;
    this.mode = 'rain';
    scene.add(this.pts);
  }
  // 'rain' (fast blue streak-dots) or 'snow' (slow white drifting flakes)
  setMode(mode) {
    if (mode === this.mode) return;
    this.mode = mode;
    const m = this.pts.material;
    if (mode === 'snow') { m.color.set(0xffffff); m.size = 0.22; m.opacity = 0.9; }
    else { m.color.set(0x9db8d9); m.size = 0.14; m.opacity = 0.6; }
  }
  update(dt, cx, cz) {
    if (!this.pts.visible) return;
    const pos = this.pts.geometry.attributes.position.array;
    const snow = this.mode === 'snow';
    const t = performance.now() / 1000;
    for (let i = 0; i < this.n; i++) {
      pos[i * 3 + 1] -= (snow ? 2.6 + (i % 5) * 0.35 : 22) * dt;
      if (snow) { // lazy sideways drift, phase per flake
        pos[i * 3] += Math.sin(t * 1.2 + i * 0.7) * dt * 1.1;
        pos[i * 3 + 2] += Math.cos(t * 0.9 + i * 1.3) * dt * 0.8;
      }
      if (pos[i * 3 + 1] < 0) {
        pos[i * 3] = cx + rand(-40, 40);
        pos[i * 3 + 1] = snow ? rand(12, 22) : rand(20, 26);
        pos[i * 3 + 2] = cz + rand(-40, 40);
      }
    }
    this.pts.geometry.attributes.position.needsUpdate = true;
  }
}
