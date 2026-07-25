/* ============================================================
   PART E — rendering
   ============================================================ */

Game.prototype.render = function (alpha) {
  const c = this.c, dpr = this.dpr;
  const cw = innerWidth, ch = innerHeight;
  c.setTransform(dpr, 0, 0, dpr, 0, 0);
  const dst = DISTRICTS[this.level ? this.level.district : 0];
  // sky border gradient
  const g = c.createLinearGradient(0, 0, 0, ch);
  g.addColorStop(0, dst.sky[0]); g.addColorStop(1, dst.sky[1]);
  c.fillStyle = g;
  c.fillRect(0, 0, cw, ch);
  if (!this.level) return;
  // stars at night
  if (this.level.night || this.level.district === 3) {
    c.fillStyle = 'rgba(255,255,255,.5)';
    for (let i = 0; i < 40; i++) {
      const sx = (i * 137.5) % cw, sy = (i * 89.7) % (ch * 0.5);
      c.fillRect(sx, sy, 2, 2);
    }
  }
  // camera transform
  const z = this.cam.zoom;
  let shx = 0, shy = 0;
  if (this.cam.shake > 0.2) {
    shx = rand(-this.cam.shake, this.cam.shake);
    shy = rand(-this.cam.shake, this.cam.shake);
  }
  c.save();
  c.translate(cw / 2, ch / 2);
  c.scale(z, z);
  c.translate(-this.cam.x + shx / z, -this.cam.y + shy / z);

  this.drawGround(c);
  // persistent skid marks
  c.drawImage(this.skids.cv, 0, 0);
  this.drawZones(c);
  this.drawSpot(c);
  // flattened / knocked props under everything else
  for (const pr of this.props) if (pr.state !== 'up') pr.draw(c);
  // parked cars
  for (const pc of this.parked) drawParkedCar(c, pc, this.level.night);
  if (this.newsVan) drawParkedCar(c, { x: this.newsVan.x, y: this.newsVan.y, h: 0, hl: 34, hw: 15, color: '#f4f4f8', kind: 'newsvan' }, this.level.night);
  // standing props
  for (const pr of this.props) if (pr.state === 'up') pr.draw(c);
  for (const pg of this.pigeons) pg.draw(c);
  if (this.dog) this.dog.draw(c);
  // traffic
  for (const tc of this.traffic) tc.draw(c, alpha, this.level.night);
  if (this.cyclist) this.cyclist.draw(c);
  // ghost target
  this.drawGhost(c);
  // player vehicle (replay pose if replaying)
  if (this.state === 'replay' && this.replayBuf.length > 1) {
    const idx = clamp(this.replayIdx, 0, this.replayBuf.length - 1.001);
    const f0 = this.replayBuf[Math.floor(idx)], f1 = this.replayBuf[Math.min(Math.floor(idx) + 1, this.replayBuf.length - 1)];
    const k = idx - Math.floor(idx);
    const pose = { x: lerp(f0.x, f1.x, k), y: lerp(f0.y, f1.y, k), h: f0.h + angNorm(f1.h - f0.h) * k };
    const savedSteer = this.veh.steer, savedArm = this.veh.armOut, savedTur = this.veh.turretA, savedBeam = this.veh.beam;
    this.veh.steer = lerp(f0.steer, f1.steer, k);
    this.veh.armOut = f0.arm; this.veh.turretA = f0.tur; this.veh.beam = f0.beam;
    this.veh.draw(c, pose, { night: this.level.night });
    this.veh.steer = savedSteer; this.veh.armOut = savedArm; this.veh.turretA = savedTur; this.veh.beam = savedBeam;
  } else {
    this.veh.draw(c, this.veh.ipose(alpha), { night: this.level.night });
  }
  // settle ring
  if (this.settle > 0 && !this.finished) {
    const v = this.veh;
    const r = Math.max(v.L, v.W) / 2 + 16;
    c.save();
    c.lineWidth = 6; c.lineCap = 'round';
    c.strokeStyle = 'rgba(43,45,54,.3)';
    c.beginPath(); c.arc(v.x, v.y, r, 0, TAU); c.stroke();
    c.strokeStyle = getComputedStyle(document.body).getPropertyValue('--green') || '#3ecf6e';
    c.beginPath(); c.arc(v.x, v.y, r, -Math.PI / 2, -Math.PI / 2 + TAU * clamp(this.settle / 1.5, 0, 1)); c.stroke();
    c.restore();
  }
  // pedestrians on top (they're never under the car)
  for (const p of this.peds) p.draw(c, alpha);
  this.particles.draw(c);
  this.texts.draw(c);
  c.restore();
  // night mask (screen space)
  if (this.level.night) this.drawNight(c, cw, ch, z, shx, shy);
  // snow ambient
  if (this.level.ice && !Save.data.settings.reducedMotion && chance(0.3)) {
    const wx = this.cam.x + rand(-cw / z / 2, cw / z / 2);
    const wy = this.cam.y - ch / z / 2 - 10;
    this.particles.snowflake(wx, wy);
  }
  if (!this.demo) this.renderHUD();
};

Game.prototype.drawGround = function (c) {
  const L = LAYOUT, W = WORLD_W;
  const dst = DISTRICTS[this.level.district];
  const ice = this.level.ice;
  // sidewalks band
  c.fillStyle = ice ? '#dde4ea' : dst.sidewalk;
  c.fillRect(0, L.buildTop, W, L.buildBot - L.buildTop);
  // sidewalk expansion joints
  c.strokeStyle = 'rgba(0,0,0,.09)'; c.lineWidth = 2;
  for (let x = 0; x < W; x += 46) {
    c.beginPath(); c.moveTo(x, L.buildTop); c.lineTo(x, L.roadTop); c.stroke();
    c.beginPath(); c.moveTo(x, L.curbY); c.lineTo(x, L.buildBot); c.stroke();
  }
  // road
  c.fillStyle = ice ? '#565c66' : '#3d4148';
  c.fillRect(0, L.roadTop, W, L.curbY - L.roadTop);
  if (ice) { // icy sheen
    c.fillStyle = 'rgba(200,225,245,.18)';
    for (let i = 0; i < 14; i++) {
      const x = (i * 261.8) % W, y = L.roadTop + ((i * 97.3) % (L.curbY - L.roadTop - 40)) + 20;
      c.beginPath(); c.ellipse(x, y, 60, 16, 0.2, 0, TAU); c.fill();
    }
  }
  // parking lane slightly darker
  c.fillStyle = 'rgba(0,0,0,.12)';
  c.fillRect(0, L.parkTop, W, L.curbY - L.parkTop);
  // parking lane separator (solid white)
  c.strokeStyle = 'rgba(255,255,255,.5)'; c.lineWidth = 3;
  c.beginPath(); c.moveTo(0, L.parkTop); c.lineTo(W, L.parkTop); c.stroke();
  // parking T markings
  c.strokeStyle = 'rgba(245,197,66,.5)'; c.lineWidth = 3;
  for (const pc of this.parked) {
    c.beginPath();
    c.moveTo(pc.x - pc.hl - 6, L.curbY - 2); c.lineTo(pc.x - pc.hl - 6, L.curbY - 34);
    c.moveTo(pc.x + pc.hl + 6, L.curbY - 2); c.lineTo(pc.x + pc.hl + 6, L.curbY - 34);
    c.stroke();
  }
  // center dashed line
  c.strokeStyle = '#f5c542'; c.lineWidth = 5;
  c.setLineDash([34, 26]);
  c.beginPath(); c.moveTo(0, 300); c.lineTo(W, 300); c.stroke();
  c.setLineDash([]);
  // curbs
  c.fillStyle = darken(ice ? '#dde4ea' : dst.sidewalk, 0.25);
  c.fillRect(0, L.roadTop - 5, W, 5);
  c.fillRect(0, L.curbY, W, 5);
  // crosswalks
  c.fillStyle = 'rgba(255,255,255,.55)';
  for (const cxs of [90, W - 150]) {
    for (let i = 0; i < 8; i++) c.fillRect(cxs + i * 8, L.roadTop + 6, 5, L.curbY - L.roadTop - 12);
  }
  // buildings top strip
  if (this.beach) {
    // sea + sand instead of top buildings
    const sea = c.createLinearGradient(0, 0, 0, L.buildTop);
    sea.addColorStop(0, '#2e7fb8'); sea.addColorStop(1, '#5ab8d8');
    c.fillStyle = sea; c.fillRect(0, 0, W, L.buildTop - 40);
    c.fillStyle = 'rgba(255,255,255,.4)';
    for (let i = 0; i < 10; i++) {
      const wx = (i * 197 + Math.sin(performance.now() / 900 + i) * 20) % W;
      c.fillRect(wx, 30 + (i % 3) * 26, 40, 3);
    }
    c.fillStyle = '#eed9a4';
    c.fillRect(0, L.buildTop - 40, W, 40);
    for (const u of this.decor.umbrellas) {
      c.save(); c.translate(u.x, L.buildTop - 20);
      c.globalAlpha = 0.25; c.fillStyle = '#000';
      c.beginPath(); c.ellipse(2, 4, 12, 4, 0, 0, TAU); c.fill();
      c.globalAlpha = 1;
      c.fillStyle = u.c; c.strokeStyle = '#22242a'; c.lineWidth = 2;
      c.beginPath(); c.arc(0, 0, 12, 0, TAU); c.fill(); c.stroke();
      c.strokeStyle = 'rgba(255,255,255,.6)'; c.lineWidth = 2;
      c.beginPath(); c.moveTo(-12, 0); c.lineTo(12, 0); c.moveTo(0, -12); c.lineTo(0, 12); c.stroke();
      c.restore();
    }
  } else {
    for (const b of this.buildings) if (b.top) this.drawBuilding(c, b, 0, L.buildTop);
  }
  for (const b of this.buildings) if (!b.top) this.drawBuilding(c, b, L.buildBot, WORLD_H - L.buildBot);
  // trees & lamps
  for (const t of this.decor.trees) {
    c.save(); c.translate(t.x, t.y);
    c.globalAlpha = 0.22; c.fillStyle = '#000';
    c.beginPath(); c.ellipse(2, 3, t.r + 2, t.r * 0.6, 0, 0, TAU); c.fill();
    c.globalAlpha = 1;
    c.fillStyle = this.level.ice ? '#cfe0d8' : '#5a9a55'; c.strokeStyle = '#22242a'; c.lineWidth = 2.5;
    c.beginPath(); c.arc(0, 0, t.r, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = this.level.ice ? '#b8d0c4' : '#4d8548';
    c.beginPath(); c.arc(-t.r * 0.3, -t.r * 0.25, t.r * 0.5, 0, TAU); c.fill();
    c.restore();
  }
  for (const lp of this.decor.lamps) {
    c.save(); c.translate(lp.x, lp.y);
    c.fillStyle = '#4d515e'; c.strokeStyle = '#22242a'; c.lineWidth = 2;
    c.beginPath(); c.arc(0, 0, 3.5, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = this.level.night ? '#ffe08a' : '#8a90a0';
    c.beginPath(); c.arc(0, 0, 1.8, 0, TAU); c.fill();
    c.restore();
  }
  // district tint
  c.fillStyle = dst.accent;
  c.globalAlpha = 0.05;
  c.fillRect(0, 0, W, WORLD_H);
  c.globalAlpha = 1;
};

Game.prototype.drawBuilding = function (c, b, y0, h) {
  c.fillStyle = b.c;
  c.fillRect(b.x, y0, b.w, h);
  c.strokeStyle = 'rgba(0,0,0,.25)'; c.lineWidth = 3;
  c.strokeRect(b.x + 1, y0 + 1, b.w - 2, h - 2);
  // windows (roof-view skylights / facade hints)
  c.fillStyle = this.level.night ? 'rgba(255,224,138,.5)' : 'rgba(255,255,255,.28)';
  const n = b.win;
  for (let i = 0; i < n; i++) {
    for (let j = 0; j < 2; j++) {
      c.fillRect(b.x + 14 + i * ((b.w - 28) / Math.max(n - 1, 1)) - 6, y0 + 18 + j * 44, 13, 13);
    }
  }
  // awning facing street
  const ay = b.top === undefined || b.top ? y0 + h - 8 : y0;
  c.fillStyle = 'rgba(0,0,0,.2)';
  c.fillRect(b.x + 8, b.top ? y0 + h - 6 : y0, b.w - 16, 6);
};

Game.prototype.drawZones = function (c) {
  for (const s of this.sands) {
    c.save();
    c.fillStyle = 'rgba(238,217,164,.75)';
    c.beginPath(); c.ellipse(s.x, s.y, s.rx, s.ry, 0, 0, TAU); c.fill();
    c.fillStyle = 'rgba(210,185,130,.5)';
    c.beginPath(); c.ellipse(s.x - s.rx * 0.2, s.y + s.ry * 0.2, s.rx * 0.5, s.ry * 0.5, 0, 0, TAU); c.fill();
    c.restore();
  }
  for (const p of this.puddles) {
    c.save();
    c.fillStyle = 'rgba(90,150,200,.55)';
    c.beginPath(); c.ellipse(p.x, p.y, p.rx, p.ry, 0, 0, TAU); c.fill();
    c.fillStyle = 'rgba(255,255,255,.25)';
    c.beginPath(); c.ellipse(p.x - p.rx * 0.25, p.y - p.ry * 0.25, p.rx * 0.4, p.ry * 0.3, 0, 0, TAU); c.fill();
    c.restore();
  }
};

Game.prototype.drawSpot = function (c) {
  const s = this.spot;
  const cb = Save.data.settings.colorblind;
  const okC = cb ? '#3a9bff' : '#3ecf6e';
  const inside = this.parkInfo.inside;
  c.save();
  // glow fill
  const pulse = 0.5 + Math.sin(performance.now() / 400) * 0.15;
  c.fillStyle = inside ? okC : '#f5c542';
  c.globalAlpha = inside ? 0.22 : 0.1 * pulse + 0.06;
  c.fillRect(s.x, s.y, s.w, LAYOUT.curbY - s.y);
  c.globalAlpha = 1;
  // dashes
  c.strokeStyle = inside ? okC : '#f5c542';
  c.lineWidth = 3.5;
  c.setLineDash([12, 9]);
  c.strokeRect(s.x, s.y, s.w, LAYOUT.curbY - s.y);
  c.setLineDash([]);
  // P marking
  c.globalAlpha = 0.5;
  c.fillStyle = inside ? okC : '#f5c542';
  c.font = "800 30px 'Baloo 2', sans-serif";
  c.textAlign = 'center'; c.textBaseline = 'middle';
  c.fillText('P', s.cx, s.y + (LAYOUT.curbY - s.y) / 2);
  c.globalAlpha = 1;
  c.restore();
};

Game.prototype.drawGhost = function (c) {
  if (this.finished) return;
  const v = this.veh, s = this.spot;
  if (Math.hypot(v.x - s.cx, v.y - (s.y + s.h / 2)) > 15 * M2P) return;
  const alpha = 0.35 + Math.sin(performance.now() / 300) * 0.12;
  c.save();
  c.globalAlpha = alpha;
  c.strokeStyle = '#ffffff';
  c.lineWidth = 2.5;
  c.setLineDash([8, 7]);
  if (v.key === 'ufo') {
    const R = v.L / 2;
    c.beginPath(); c.arc(s.cx, LAYOUT.curbY - R - 4, R, 0, TAU); c.stroke();
  } else {
    c.translate(s.cx, LAYOUT.curbY - v.W / 2 - 4);
    roundRectPath(c, -v.L / 2, -v.W / 2, v.L, v.W, 7);
    c.stroke();
    // heading arrow
    c.beginPath(); c.moveTo(v.L * 0.28, 0); c.lineTo(v.L * 0.12, -6); c.lineTo(v.L * 0.12, 6); c.closePath();
    c.setLineDash([]);
    c.fillStyle = '#fff'; c.fill();
  }
  c.restore();
};

Game.prototype.drawNight = function (c, cw, ch, z, shx, shy) {
  const nc = this.nightCv.getContext('2d');
  const dpr = this.dpr;
  nc.setTransform(dpr, 0, 0, dpr, 0, 0);
  nc.globalCompositeOperation = 'source-over';
  nc.clearRect(0, 0, cw, ch);
  nc.fillStyle = 'rgba(8,10,26,.78)';
  nc.fillRect(0, 0, cw, ch);
  // apply camera transform for cutouts
  nc.translate(cw / 2, ch / 2);
  nc.scale(z, z);
  nc.translate(-this.cam.x + shx / z, -this.cam.y + shy / z);
  nc.globalCompositeOperation = 'destination-out';
  const cut = (x, y, r, str) => {
    const g = nc.createRadialGradient(x, y, r * 0.15, x, y, r);
    g.addColorStop(0, `rgba(0,0,0,${str})`); g.addColorStop(1, 'rgba(0,0,0,0)');
    nc.fillStyle = g;
    nc.beginPath(); nc.arc(x, y, r, 0, TAU); nc.fill();
  };
  // street lamps
  for (const lp of this.decor.lamps) cut(lp.x, lp.y, 85, 0.75);
  // spot glow
  cut(this.spot.cx, this.spot.y + 20, 120, 0.55);
  // vehicle glow + headlight cone
  const v = this.veh;
  cut(v.x, v.y, 90, 0.85);
  const hx = v.x + Math.cos(v.h) * v.L / 2, hy = v.y + Math.sin(v.h) * v.L / 2;
  nc.save();
  nc.translate(hx, hy); nc.rotate(v.h);
  const cg = nc.createRadialGradient(0, 0, 10, 130, 0, 150);
  cg.addColorStop(0, 'rgba(0,0,0,.95)'); cg.addColorStop(1, 'rgba(0,0,0,0)');
  nc.fillStyle = cg;
  nc.beginPath(); nc.moveTo(-4, 0); nc.lineTo(190, -75); nc.lineTo(190, 75); nc.closePath(); nc.fill();
  nc.restore();
  // traffic headlights
  for (const tc of this.traffic) cut(tc.x + 40, tc.y, 60, 0.5);
  // composite onto main
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.drawImage(this.nightCv, 0, 0, this.nightCv.width, this.nightCv.height, 0, 0, cw * dpr / dpr, ch);
  c.setTransform(dpr, 0, 0, dpr, 0, 0);
};

// ---------------- HUD DOM sync ----------------
Game.prototype.renderHUD = function () {
  if (this.demo) return;
  // timer
  const tEl = $('timer');
  tEl.textContent = fmtTime(this.t);
  tEl.classList.toggle('overpar', this.t > this.level.par);
  // shame meter
  const fill = $('shame-fill');
  const cur = parseFloat(fill.dataset.v || '0');
  const smoothed = lerp(cur, this.shame, 0.14);
  fill.dataset.v = smoothed;
  fill.style.height = smoothed.toFixed(1) + '%';
  $('shame-pct').textContent = Math.round(this.shame) + '%';
  const face = $('shame-face');
  const em = this.shame >= 90 ? '🤡' : this.shame >= 75 ? '🥵' : this.shame >= 50 ? '😳' : this.shame >= 25 ? '😅' : '🙂';
  if (face.textContent !== em) face.textContent = em;
  $('shame-wrap').classList.toggle('pulse', this.shameRiseT > 0 && this.shame > 20);
  // damage diagram
  if (this._dmgDrawn !== Math.round(this.veh.damage)) {
    this._dmgDrawn = Math.round(this.veh.damage);
    this.drawDamageDiagram();
  }
  // alignment widget
  const aw = $('align-widget');
  const near = Math.hypot(this.veh.x - this.spot.cx, this.veh.y - (this.spot.y + this.spot.h / 2)) < 13 * M2P && !this.finished;
  aw.classList.toggle('show', near);
  if (near) {
    const pi = this.parkInfo;
    const angEl = $('aw-angle'), gapEl = $('aw-gap');
    angEl.querySelector('.v').textContent = pi.angleErr.toFixed(1) + '°';
    angEl.classList.toggle('ok', pi.angleOk); angEl.classList.toggle('bad', !pi.angleOk);
    const gapTxt = pi.gap > 200 ? '—' : Math.max(0, pi.gap).toFixed(0) + 'cm';
    gapEl.querySelector('.v').textContent = gapTxt;
    gapEl.classList.toggle('ok', pi.gapOk); gapEl.classList.toggle('bad', !pi.gapOk);
    this.drawAlignWidget();
  }
};
Game.prototype.drawDamageDiagram = function () {
  const cv = $('dmgCanvas'), c = cv.getContext('2d');
  c.clearRect(0, 0, cv.width, cv.height);
  c.save();
  c.translate(cv.width / 2, cv.height / 2);
  // car outline top view
  c.lineWidth = 2.5; c.strokeStyle = '#2b2d36'; c.fillStyle = '#eee7d6';
  roundRectPath(c, -38, -16, 76, 32, 9); c.fill(); c.stroke();
  roundRectPath(c, -20, -10, 34, 20, 5); c.stroke();
  // dents
  c.strokeStyle = '#e04f3c'; c.lineWidth = 2;
  for (const dn of this.veh.dents) {
    const dx = Math.cos(dn.a) * 32 * dn.r, dy = Math.sin(dn.a) * 13 * dn.r;
    c.beginPath();
    c.moveTo(dx - 4, dy - 3); c.lineTo(dx + 3, dy + 2); c.moveTo(dx + 3, dy - 3); c.lineTo(dx - 4, dy + 2);
    c.stroke();
  }
  c.restore();
  const lbl = $('dmgLabel');
  const dmg = this.veh.damage;
  if (dmg < 10) { lbl.textContent = 'MINT'; lbl.style.color = 'var(--green)'; }
  else if (dmg < 35) { lbl.textContent = 'SCUFFED'; lbl.style.color = '#e0a52a'; }
  else if (dmg < 70) { lbl.textContent = 'DENTED'; lbl.style.color = '#ff8f2e'; }
  else { lbl.textContent = 'WRECKED'; lbl.style.color = 'var(--red)'; }
};
Game.prototype.drawAlignWidget = function () {
  const cv = $('aw-canvas'), c = cv.getContext('2d');
  const w = cv.width, h = cv.height;
  c.clearRect(0, 0, w, h);
  const pi = this.parkInfo;
  const cb = Save.data.settings.colorblind;
  const okC = cb ? '#3a9bff' : '#3ecf6e', badC = cb ? '#ff9d2e' : '#ff4757';
  // curb at bottom
  c.fillStyle = '#8a8f9a';
  c.fillRect(6, h - 14, w - 12, 6);
  // target zone
  c.fillStyle = 'rgba(245,197,66,.25)';
  c.fillRect(14, h - 46, w - 28, 32);
  // car glyph at angle & gap
  const gapN = clamp(pi.gap / 40, -0.2, 2);
  const cy = h - 26 - clamp(gapN, 0, 1.6) * 14 + 6;
  c.save();
  c.translate(w / 2, cy);
  c.rotate(rad(clamp(pi.angleErr, 0, 30)) * (Math.sin(angNorm(this.veh.h)) >= 0 ? 1 : -1) * 0.9);
  c.fillStyle = (pi.angleOk && pi.gapOk) ? okC : badC;
  c.strokeStyle = '#2b2d36'; c.lineWidth = 2.5;
  roundRectPath(c, -34, -11, 68, 22, 6); c.fill(); c.stroke();
  c.restore();
};

// ---------------- postcard ----------------
Game.prototype.renderPostcard = function () {
  const pc = document.createElement('canvas');
  pc.width = 960; pc.height = 720;
  const c = pc.getContext('2d');
  // cream card
  c.fillStyle = '#fffdf6';
  c.fillRect(0, 0, 960, 720);
  c.strokeStyle = '#2b2d36'; c.lineWidth = 8;
  c.strokeRect(10, 10, 940, 700);
  // snapshot of the live canvas (center crop)
  const src = this.cv;
  const targetAR = 880 / 520;
  let sw = src.width, sh = src.height;
  if (sw / sh > targetAR) sw = sh * targetAR; else sh = sw / targetAR;
  const sx = (src.width - sw) / 2, sy = (src.height - sh) / 2;
  c.save();
  roundRectPath(c, 40, 40, 880, 520, 14);
  c.clip();
  c.drawImage(src, sx, sy, sw, sh, 40, 40, 880, 520);
  c.restore();
  c.strokeStyle = '#2b2d36'; c.lineWidth = 5;
  roundRectPath(c, 40, 40, 880, 520, 14); c.stroke();
  // caption
  const caption = pick([
    'Wish you were here. (I\'m still parking.)',
    'Greetings from the curb I definitely didn\'t hit.',
    'Nailed it. The mailbox agrees.',
    'Certified Parallel Parker™ (self-certified)',
    'The crowd went mild.',
  ]);
  c.fillStyle = '#2b2d36';
  c.font = "800 40px 'Baloo 2', sans-serif";
  c.textAlign = 'center';
  c.fillText('PARALLEL PARKING NIGHTMARE', 480, 615);
  c.font = "600 26px 'Baloo 2', sans-serif";
  c.fillStyle = '#6a6d7a';
  c.fillText(`${this.level.name} · ${fmtTime(this.t)} · ${caption}`, 480, 660);
  // stamp
  c.save();
  c.translate(860, 645); c.rotate(0.15);
  c.strokeStyle = '#e04f3c'; c.lineWidth = 4;
  c.strokeRect(-52, -34, 104, 68);
  c.fillStyle = '#e04f3c';
  c.font = "800 26px 'Baloo 2', sans-serif";
  c.fillText(this.result && this.result.grade ? 'GRADE ' + this.result.grade : 'OOPS', 0, 8);
  c.restore();
  return pc;
};
