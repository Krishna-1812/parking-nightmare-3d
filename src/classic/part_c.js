/* ============================================================
   PART C — particles, skids, float text, vehicles
   ============================================================ */

// ---------- particle system ----------
class ParticleSystem {
  constructor() { this.ps = []; }
  spawn(o) {
    if (this.ps.length > 900) this.ps.shift();
    o.t = 0;
    this.ps.push(o);
  }
  smoke(x, y, big) {
    this.spawn({ type: 'smoke', x, y, vx: rand(-14, 14), vy: rand(-18, -4), life: rand(0.5, 0.9) * (big ? 1.5 : 1), size: rand(3, 6) * (big ? 2.2 : 1), color: big ? '#c9c2b4' : '#aab' });
  }
  dust(x, y, n) {
    const cnt = Save.data.settings.reducedMotion ? Math.min(n, 4) : n;
    for (let i = 0; i < cnt; i++)
      this.spawn({ type: 'smoke', x: x + rand(-8, 8), y: y + rand(-6, 6), vx: rand(-40, 40), vy: rand(-30, 5), life: rand(0.4, 0.9), size: rand(4, 9), color: '#b9ae95' });
  }
  sparks(x, y, n) {
    const cnt = Save.data.settings.reducedMotion ? Math.min(n, 5) : n;
    for (let i = 0; i < cnt; i++) {
      const a = rand(0, TAU), sp = rand(60, 220);
      this.spawn({ type: 'spark', x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, life: rand(0.15, 0.45), size: rand(1.5, 3), color: pick(['#ffd23e', '#ffab2e', '#fff']) });
    }
  }
  confetti(x, y, n) {
    const cnt = Save.data.settings.reducedMotion ? Math.min(n, 20) : n;
    for (let i = 0; i < cnt; i++) {
      const a = rand(-Math.PI, 0), sp = rand(80, 320);
      this.spawn({
        type: 'confetti', x, y, vx: Math.cos(a) * sp * rand(0.4, 1), vy: Math.sin(a) * sp,
        life: rand(1.2, 2.4), size: rand(3, 6), rot: rand(0, TAU), vr: rand(-12, 12), g: 220,
        color: pick(['#ff6b57', '#3aa6ff', '#ffc23e', '#3ecf6e', '#c86bff', '#ff9ecb'])
      });
    }
  }
  splash(x, y, n) {
    for (let i = 0; i < n; i++) {
      const a = rand(0, TAU), sp = rand(30, 130);
      this.spawn({ type: 'splash', x, y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp - 40, life: rand(0.3, 0.6), size: rand(2, 4), g: 300, color: '#7db9e8' });
    }
  }
  feathers(x, y) {
    for (let i = 0; i < 6; i++)
      this.spawn({ type: 'feather', x, y, vx: rand(-50, 50), vy: rand(-80, -20), life: rand(0.6, 1.4), size: rand(2, 4), g: 60, rot: rand(0, TAU), vr: rand(-6, 6), color: pick(['#e8e8ee', '#c9ccd6']) });
  }
  flash(x, y) {
    this.spawn({ type: 'flash', x, y, vx: 0, vy: 0, life: 0.22, size: 26, color: '#fff' });
  }
  snowflake(x, y) {
    this.spawn({ type: 'snow', x, y, vx: rand(-12, 12), vy: rand(18, 45), life: rand(2.5, 4), size: rand(1.5, 3), color: '#fff' });
  }
  exclaim(x, y) {
    this.spawn({ type: 'exclaim', x, y, vx: 0, vy: -26, life: 0.8, size: 15, color: '#ffc23e' });
  }
  update(dt) {
    const ps = this.ps;
    for (let i = ps.length - 1; i >= 0; i--) {
      const p = ps[i];
      p.t += dt;
      if (p.t >= p.life) { ps.splice(i, 1); continue; }
      p.x += p.vx * dt; p.y += p.vy * dt;
      if (p.g) p.vy += p.g * dt;
      if (p.vr) p.rot += p.vr * dt;
      if (p.type === 'smoke') { p.vx *= (1 - 1.6 * dt); p.vy *= (1 - 1.6 * dt); }
      if (p.type === 'feather') { p.vx += Math.sin(p.t * 8 + p.rot) * 30 * dt; }
    }
  }
  draw(c) {
    for (const p of this.ps) {
      const k = p.t / p.life, alpha = 1 - k;
      c.save();
      c.globalAlpha = alpha;
      switch (p.type) {
        case 'smoke':
          c.fillStyle = p.color;
          c.beginPath(); c.arc(p.x, p.y, p.size * (0.6 + k * 1.4), 0, TAU); c.fill();
          break;
        case 'spark':
          c.fillStyle = p.color;
          c.beginPath(); c.arc(p.x, p.y, p.size * (1 - k * 0.5), 0, TAU); c.fill();
          break;
        case 'confetti':
          c.translate(p.x, p.y); c.rotate(p.rot);
          c.fillStyle = p.color;
          c.fillRect(-p.size / 2, -p.size / 3, p.size, p.size * 0.66);
          break;
        case 'splash':
          c.fillStyle = p.color;
          c.beginPath(); c.arc(p.x, p.y, p.size, 0, TAU); c.fill();
          break;
        case 'feather':
          c.translate(p.x, p.y); c.rotate(p.rot);
          c.fillStyle = p.color;
          c.beginPath(); c.ellipse(0, 0, p.size * 1.6, p.size * 0.7, 0, 0, TAU); c.fill();
          break;
        case 'flash':
          c.fillStyle = p.color;
          c.globalAlpha = alpha * 0.9;
          c.beginPath(); c.arc(p.x, p.y, p.size * (0.4 + k * 1.2), 0, TAU); c.fill();
          break;
        case 'snow':
          c.fillStyle = p.color; c.globalAlpha = alpha * 0.85;
          c.beginPath(); c.arc(p.x, p.y, p.size, 0, TAU); c.fill();
          break;
        case 'exclaim':
          c.fillStyle = p.color;
          c.strokeStyle = '#2b2d36'; c.lineWidth = 3;
          c.font = `800 ${p.size + k * 6}px 'Baloo 2', sans-serif`;
          c.textAlign = 'center';
          c.strokeText('!', p.x, p.y); c.fillText('!', p.x, p.y);
          break;
      }
      c.restore();
    }
  }
}

// ---------- persistent skid layer ----------
class SkidLayer {
  constructor(w, h) {
    this.cv = document.createElement('canvas');
    this.cv.width = w; this.cv.height = h;
    this.c = this.cv.getContext('2d');
    this.fadeAcc = 0;
  }
  clear() { this.c.clearRect(0, 0, this.cv.width, this.cv.height); }
  mark(x, y, ang, alpha, w, color) {
    const c = this.c;
    c.save();
    c.translate(x, y); c.rotate(ang);
    c.globalAlpha = alpha;
    c.fillStyle = color || 'rgba(24,25,30,1)';
    c.fillRect(-2.6, -w / 2, 5.2, w);
    c.restore();
  }
  fade(dt) {
    this.fadeAcc += dt;
    if (this.fadeAcc < 0.12) return;
    this.fadeAcc = 0;
    const c = this.c;
    c.save();
    c.globalCompositeOperation = 'destination-out';
    c.globalAlpha = 0.018;
    c.fillRect(0, 0, this.cv.width, this.cv.height);
    c.restore();
  }
}

// ---------- floating comic text ----------
class FloatTexts {
  constructor() { this.list = []; }
  add(x, y, text, color, size) {
    this.list.push({ x, y, text, color: color || '#ffc23e', size: size || 24, t: 0, life: 1.1, rot: rand(-0.18, 0.18) });
    if (this.list.length > 24) this.list.shift();
  }
  update(dt) {
    for (let i = this.list.length - 1; i >= 0; i--) {
      const f = this.list[i];
      f.t += dt; f.y -= 22 * dt;
      if (f.t > f.life) this.list.splice(i, 1);
    }
  }
  draw(c) {
    for (const f of this.list) {
      const k = f.t / f.life;
      const scl = k < 0.18 ? (k / 0.18) * 1.25 : (k < 0.35 ? 1.25 - ((k - 0.18) / 0.17) * 0.25 : 1);
      c.save();
      c.translate(f.x, f.y); c.rotate(f.rot); c.scale(scl, scl);
      c.globalAlpha = k > 0.7 ? 1 - (k - 0.7) / 0.3 : 1;
      c.font = `800 ${f.size}px 'Baloo 2', sans-serif`;
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.lineWidth = 5; c.strokeStyle = '#2b2d36'; c.lineJoin = 'round';
      c.strokeText(f.text, 0, 0);
      c.fillStyle = f.color; c.fillText(f.text, 0, 0);
      c.restore();
    }
  }
}

// ============================================================
// VEHICLE DEFINITIONS
// ============================================================
const VEH_ORDER = ['hatch', 'wagon', 'limo', 'icecream', 'bus', 'tank', 'ufo'];
const VEH_DEFS = {
  hatch: {
    name: 'Rusty Hatchback', horn: 'hatch', drive: 'car',
    flavor: '"Runs on hope and expired coupons. Occasionally explodes a little, as a treat."',
    len: 3.9, wid: 1.78, wb: 2.5, maxSpeed: 9.5, accel: 8, steerSpeed: 3.4, grip: 0.95, mass: 1, fragility: 1,
    stats: { size: 22, speed: 55, hand: 88, chaos: 25 },
    unlock: { type: 'start' },
    body: '#c0563b', roof: '#a84730',
  },
  wagon: {
    name: 'Family Wagon', horn: 'hatch', drive: 'car',
    flavor: '"The kid in the back is your harshest critic. He has seen things. Mostly your parking."',
    len: 5.0, wid: 1.88, wb: 3.1, maxSpeed: 9, accel: 7, steerSpeed: 3.0, grip: 0.95, mass: 1.3, fragility: 1,
    stats: { size: 38, speed: 50, hand: 72, chaos: 30 },
    unlock: { type: 'level', lv: 3 },
    body: '#3f8f8f', roof: '#337575',
  },
  limo: {
    name: 'Stretch Limo', horn: 'limo', drive: 'car',
    flavor: '"Longer than your list of regrets. The middle section has never once seen the curb."',
    len: 8.6, wid: 1.95, wb: 6.3, maxSpeed: 8, accel: 5.5, steerSpeed: 2.6, grip: 0.96, mass: 2, fragility: 1.2,
    stats: { size: 78, speed: 42, hand: 30, chaos: 55 },
    unlock: { type: 'level', lv: 6 },
    body: '#23252e', roof: '#181a21',
  },
  icecream: {
    name: 'Ice Cream Truck', horn: 'icecream', drive: 'car',
    flavor: '"The jingle cannot be stopped. The children cannot be stopped. Nothing can be stopped."',
    len: 5.8, wid: 2.15, wb: 3.7, maxSpeed: 7.5, accel: 6, steerSpeed: 2.8, grip: 0.93, mass: 1.7, fragility: 1,
    stats: { size: 52, speed: 38, hand: 58, chaos: 75 },
    unlock: { type: 'level', lv: 7 },
    body: '#fdf6ee', roof: '#f8b8cf',
  },
  bus: {
    name: 'School Bus', horn: 'bus', drive: 'car',
    flavor: '"The mirrors count. The stop-sign arm has a mind of its own. The children are watching."',
    len: 10.6, wid: 2.45, wb: 7.2, maxSpeed: 7, accel: 5, steerSpeed: 2.3, grip: 0.96, mass: 3, fragility: 0.8,
    stats: { size: 95, speed: 33, hand: 22, chaos: 60 },
    unlock: { type: 'level', lv: 9 },
    body: '#f2b32b', roof: '#dda01f',
  },
  tank: {
    name: 'Tank', horn: 'tank', drive: 'tank',
    flavor: '"Turns on a dime. Crushes the dime. Crushes everything. The turret has opinions."',
    len: 6.6, wid: 3.2, wb: 4.5, maxSpeed: 4.5, accel: 5, steerSpeed: 3, grip: 1, mass: 8, fragility: 0.2,
    stats: { size: 70, speed: 18, hand: 80, chaos: 100 },
    unlock: { type: 'level', lv: 12 },
    body: '#6b7a4a', roof: '#5a6840',
  },
  ufo: {
    name: 'UFO', horn: 'ufo', drive: 'ufo',
    flavor: '"No friction. No brakes. No dignity. Beam down gently — the whole galaxy is filming."',
    len: 4.6, wid: 4.6, wb: 3, maxSpeed: 8, accel: 6, steerSpeed: 3, grip: 0, mass: 0.8, fragility: 1,
    stats: { size: 45, speed: 65, hand: 8, chaos: 90 },
    unlock: { type: 'stars', n: 30 },
    body: '#c8cede', roof: '#9ba3b8',
  },
};
function vehicleUnlocked(key) {
  const u = VEH_DEFS[key].unlock;
  if (u.type === 'start') return true;
  if (u.type === 'level') return Save.data.unlockedLevel >= u.lv;
  if (u.type === 'stars') return Save.totalStars() >= u.n;
  return false;
}
function vehicleUnlockText(key) {
  const u = VEH_DEFS[key].unlock;
  if (u.type === 'start') return '';
  if (u.type === 'level') return `🔒 Reach Level ${u.lv} to unlock`;
  if (u.type === 'stars') return `🔒 Earn ${u.n} total stars to unlock (you have ${Save.totalStars()})`;
  return '';
}

// ============================================================
// VEHICLE
// ============================================================
class Vehicle {
  constructor(key, x, y, h) {
    const d = VEH_DEFS[key];
    this.key = key; this.def = d;
    this.x = x; this.y = y; this.h = h;
    this.px = x; this.py = y; this.ph = h;
    this.vx = 0; this.vy = 0;
    this.steer = 0;
    this.maxSteer = rad(38);
    this.L = d.len * M2P; this.W = d.wid * M2P;
    this.wheelSpin = 0;
    this.braking = false;
    this.reversing = false;
    this.damage = 0;
    this.dents = [];          // {qx,qy} quadrant offsets for HUD diagram
    this.slideAmt = 0;
    this.surfaceGrip = 1;
    this.slipTimer = 0;
    // gimmicks
    this.backfireT = rand(4, 9);
    this.kidMood = 0;         // -1 crying .. 1 cheering
    this.kidMoodT = 0;
    this.armT = rand(5, 9);
    this.armOut = 0;          // 0..1 deployed
    this.armDeployed = false;
    this.turretA = 0;         // world angle
    this.hoverPhase = rand(0, TAU);
    this.beam = 0;            // 0..1 beam-down amount
    this.deflate = 0;         // fail animation
  }
  get speed() { // signed forward speed px/s
    return this.vx * Math.cos(this.h) + this.vy * Math.sin(this.h);
  }
  get speedAbs() { return Math.hypot(this.vx, this.vy); }
  get obb() { return { x: this.x, y: this.y, h: this.h, hl: this.L / 2, hw: this.W / 2 }; }
  extraObbs() {
    const list = [];
    if (this.key === 'bus') {
      // side mirrors near the front
      const c = Math.cos(this.h), s = Math.sin(this.h);
      const fx = this.x + c * (this.L * 0.38), fy = this.y + s * (this.L * 0.38);
      const off = this.W / 2 + 5;
      list.push({ x: fx - s * off, y: fy + c * off, h: this.h, hl: 4, hw: 4, tag: 'mirror' });
      list.push({ x: fx + s * off, y: fy - c * off, h: this.h, hl: 4, hw: 4, tag: 'mirror' });
      if (this.armOut > 0.5) {
        const ax = this.x + c * (this.L * 0.1), ay = this.y + s * (this.L * 0.1);
        const aoff = this.W / 2 + 11;
        // arm is drawn on the local -y side; local (0,-aoff) -> world (+s*aoff, -c*aoff)
        list.push({ x: ax + s * aoff, y: ay - c * aoff, h: this.h, hl: 8, hw: 8, tag: 'arm' });
      }
    }
    return list;
  }
  corners() { return obbCorners(this.obb); }
  stashPrev() { this.px = this.x; this.py = this.y; this.ph = this.h; }
  update(dt, inp, game) {
    this.stashPrev();
    const d = this.def;
    const maxSp = d.maxSpeed * M2P * this.surfaceGrip * (this.surfaceGrip < 1 ? 1.1 : 1);
    const steerIn = inp ? inp.steer : 0;
    const thr = inp ? inp.throttle : 0;
    const hb = inp ? inp.handbrake : false;
    this.braking = false;

    if (d.drive === 'ufo') {
      // hover physics: pure momentum
      this.h += steerIn * 2.4 * dt;
      const th = thr;
      if (th !== 0) {
        this.vx += Math.cos(this.h) * d.accel * M2P * th * dt;
        this.vy += Math.sin(this.h) * d.accel * M2P * th * dt;
        if (chance(0.3)) game.particles.spawn({ type: 'spark', x: this.x - Math.cos(this.h) * 20 * th, y: this.y - Math.sin(this.h) * 20 * th, vx: rand(-20, 20), vy: rand(-20, 20), life: 0.3, size: 2, color: '#8ef7d2' });
      }
      // tiny drag + speed clamp
      const sp = this.speedAbs;
      if (sp > maxSp) { this.vx *= maxSp / sp; this.vy *= maxSp / sp; }
      this.vx *= (1 - 0.05 * dt); this.vy *= (1 - 0.05 * dt);
      if (hb) { this.vx *= (1 - 1.1 * dt); this.vy *= (1 - 1.1 * dt); } // weak retro-thrusters
      this.hoverPhase += dt * 3;
    } else if (d.drive === 'tank') {
      // differential steering: rotate in place
      let vF = this.speed;
      const rotSpeed = 1.6 * (1 - Math.abs(vF) / (maxSp * 2.2));
      this.h += steerIn * rotSpeed * dt * (vF < -6 ? -1 : 1);
      if (thr > 0) vF += d.accel * M2P * thr * dt;
      else if (thr < 0) {
        if (vF > 4) { vF += d.accel * 2.2 * M2P * thr * dt; this.braking = true; }
        else vF += d.accel * 0.8 * M2P * thr * dt;
      }
      vF *= (1 - 2.2 * dt);
      vF = clamp(vF, -maxSp * 0.6, maxSp);
      if (hb) { vF *= (1 - 6 * dt); this.braking = true; }
      const c = Math.cos(this.h), s = Math.sin(this.h);
      this.vx = c * vF; this.vy = s * vF;
      this.slideAmt = 0;
      // turret tracks nearest pedestrian
      if (game && game.peds && game.peds.length) {
        let best = null, bd = Infinity;
        for (const p of game.peds) {
          if (p.state === 'gone') continue;
          const dd = dist2(this.x, this.y, p.x, p.y);
          if (dd < bd) { bd = dd; best = p; }
        }
        if (best) {
          const want = Math.atan2(best.y - this.y, best.x - this.x);
          this.turretA = angLerp(this.turretA, want, clamp(0.9 * dt, 0, 1));
          if (bd < (7 * M2P) ** 2 && Math.abs(angNorm(this.turretA - Math.atan2(best.y - this.y, best.x - this.x))) < 0.35) {
            best.terrify(game);
          }
        }
      }
    } else {
      // ---- bicycle model ----
      const target = steerIn * this.maxSteer;
      const ds = clamp(target - this.steer, -d.steerSpeed * dt, d.steerSpeed * dt);
      this.steer += ds;
      const c = Math.cos(this.h), s = Math.sin(this.h);
      let vF = this.vx * c + this.vy * s;
      let vL = -this.vx * s + this.vy * c;
      // throttle & brake (down = brake while moving fwd, reverse when slow)
      if (thr > 0) {
        if (vF < -6) { vF += d.accel * 2.4 * M2P * thr * dt; this.braking = true; }
        else vF += d.accel * M2P * thr * dt;
      } else if (thr < 0) {
        if (vF > 6) { vF += d.accel * 2.4 * M2P * thr * dt; this.braking = true; }
        else vF += d.accel * 0.62 * M2P * thr * dt;
      }
      // drag / rolling
      vF *= (1 - (0.55 + (thr === 0 ? 1.3 : 0)) * dt);
      if (Math.abs(vF) < 1.2 && thr === 0) vF *= (1 - 8 * dt);
      vF = clamp(vF, -maxSp * 0.55, maxSp);
      // handbrake
      let grip = d.grip * this.surfaceGrip;
      if (this.slipTimer > 0) grip *= 0.35;
      if (hb) { grip *= 0.1; vF *= (1 - 1.9 * dt); this.braking = true; }
      // lateral friction
      vL *= Math.max(0, 1 - grip * 9.5 * dt);
      this.slideAmt = Math.abs(vL);
      // heading (bicycle model — steering geometry flips naturally in reverse)
      if (Math.abs(vF) > 1) {
        this.h += (vF / (d.wb * M2P)) * Math.tan(this.steer) * dt;
      }
      const c2 = Math.cos(this.h), s2 = Math.sin(this.h);
      this.vx = c2 * vF - s2 * vL;
      this.vy = s2 * vF + c2 * vL;
      this.reversing = vF < -4;
    }

    this.x += this.vx * dt;
    this.y += this.vy * dt;
    this.wheelSpin += this.speed * dt * 0.11;
    if (this.slipTimer > 0) this.slipTimer -= dt;

    // ---- gimmick timers ----
    if (this.key === 'hatch' && game && game.state === 'play') {
      this.backfireT -= dt;
      if (this.backfireT <= 0) {
        this.backfireT = rand(6, 14);
        SFX.backfire();
        const bx = this.x - Math.cos(this.h) * this.L * 0.55, by = this.y - Math.sin(this.h) * this.L * 0.55;
        game.particles.smoke(bx, by, true);
        game.particles.sparks(bx, by, 4);
        game.texts.add(bx, by - 14, 'PUTT!', '#c9c2b4', 16);
      }
    }
    if (this.key === 'wagon') {
      this.kidMoodT -= dt;
      if (this.kidMoodT <= 0) this.kidMood = 0;
    }
    if (this.key === 'bus' && game && game.state === 'play') {
      this.armT -= dt;
      if (this.armT <= 0) {
        if (!this.armDeployed) {
          this.armDeployed = true; this.armT = rand(2.5, 4);
          game.texts.add(this.x, this.y - this.W, 'STOP ARM!', '#ff4757', 16);
          SFX.thud();
        } else { this.armDeployed = false; this.armT = rand(6, 11); }
      }
      this.armOut = clamp(this.armOut + (this.armDeployed ? 3 : -3) * dt, 0, 1);
    }
    // exhaust puffs
    if (game && d.drive !== 'ufo' && thr > 0 && chance(dt * 9)) {
      const bx = this.x - Math.cos(this.h) * this.L * 0.55, by = this.y - Math.sin(this.h) * this.L * 0.55;
      game.particles.smoke(bx, by, false);
    }
  }
  applyDamage(amt, impactAngle) {
    this.damage = clamp(this.damage + amt * this.def.fragility, 0, 100);
    if (this.dents.length < 14) {
      this.dents.push({ a: impactAngle, r: rand(0.5, 1) });
    }
  }
  // interpolated pose
  ipose(alpha) {
    return {
      x: lerp(this.px, this.x, alpha),
      y: lerp(this.py, this.y, alpha),
      h: this.ph + angNorm(this.h - this.ph) * alpha,
    };
  }
  wheelOffsets() {
    const d = this.def;
    const fx = d.wb * M2P / 2, w = this.W / 2 - 3;
    return [
      { x: fx, y: -w, front: true }, { x: fx, y: w, front: true },
      { x: -fx, y: -w, front: false }, { x: -fx, y: w, front: false },
    ];
  }
  worldWheels() {
    const c = Math.cos(this.h), s = Math.sin(this.h);
    return this.wheelOffsets().map(o => ({
      x: this.x + o.x * c - o.y * s,
      y: this.y + o.x * s + o.y * c,
      front: o.front,
    }));
  }
  draw(c, pose, opts) {
    opts = opts || {};
    const p = pose || this;
    const d = this.def;
    c.save();
    c.translate(p.x, p.y);
    // shadow
    const hoverLift = this.key === 'ufo' ? 4 + Math.sin(this.hoverPhase) * 2 : 0;
    c.save();
    c.globalAlpha = opts.ghost ? 0.12 : 0.25;
    c.fillStyle = '#000';
    c.beginPath();
    c.ellipse(2, 5 + hoverLift, this.L / 2 + 2, this.W / 2 + 3, p.h, 0, TAU);
    c.fill();
    c.restore();
    c.rotate(p.h);
    if (this.key === 'ufo') c.translate(0, -hoverLift);
    if (this.deflate > 0) {
      c.scale(1 + this.deflate * 0.08, 1 - this.deflate * 0.22);
      c.rotate(this.deflate * 0.05);
    }
    if (opts.ghost) c.globalAlpha = 0.35;
    this.paintBody(c, opts);
    c.restore();
  }
  paintBody(c, opts) {
    const L = this.L, W = this.W, d = this.def;
    const hl = L / 2, hw = W / 2;
    c.lineWidth = 2.5; c.strokeStyle = '#22242a'; c.lineJoin = 'round';
    const night = opts.night;
    // ---------- wheels (under body) ----------
    if (d.drive === 'car') {
      for (const o of this.wheelOffsets()) {
        c.save();
        c.translate(o.x, o.y);
        if (o.front) c.rotate(this.steer);
        c.fillStyle = '#1c1e24';
        roundRectPath(c, -6, -3, 12, 6, 2.5); c.fill();
        // spin marker
        c.fillStyle = '#4d515e';
        const m = ((this.wheelSpin % 1) + 1) % 1;
        c.fillRect(-6 + m * 9, -3, 2.5, 6);
        c.restore();
      }
    }
    const body = d.body, roofC = d.roof;
    switch (this.key) {
      case 'hatch': {
        c.fillStyle = body; roundRectPath(c, -hl, -hw, L, W, 7); c.fill(); c.stroke();
        // rust patches
        c.fillStyle = '#7d3a26';
        c.beginPath(); c.ellipse(-hl * 0.5, hw * 0.55, 5, 3, 0.4, 0, TAU); c.fill();
        c.beginPath(); c.ellipse(hl * 0.3, -hw * 0.6, 4, 2.5, -0.3, 0, TAU); c.fill();
        // cabin
        c.fillStyle = roofC; roundRectPath(c, -hl * 0.55, -hw + 3.5, L * 0.52, W - 7, 5); c.fill(); c.stroke();
        c.fillStyle = night ? '#39506b' : '#bde3ff';
        roundRectPath(c, -hl * 0.05, -hw + 5, L * 0.16, W - 10, 3); c.fill(); c.stroke(); // windshield
        roundRectPath(c, -hl * 0.5, -hw + 5, L * 0.14, W - 10, 3); c.fill(); c.stroke(); // rear window
        break;
      }
      case 'wagon': {
        c.fillStyle = body; roundRectPath(c, -hl, -hw, L, W, 8); c.fill(); c.stroke();
        c.fillStyle = roofC; roundRectPath(c, -hl * 0.72, -hw + 3.5, L * 0.72, W - 7, 6); c.fill(); c.stroke();
        c.fillStyle = night ? '#39506b' : '#bde3ff';
        roundRectPath(c, hl * 0.02, -hw + 5, L * 0.15, W - 10, 3); c.fill(); c.stroke();
        roundRectPath(c, -hl * 0.35, -hw + 5, L * 0.28, W - 10, 3); c.fill(); c.stroke();
        roundRectPath(c, -hl * 0.68, -hw + 5, L * 0.24, W - 10, 3); c.fill(); c.stroke();
        // roof rack
        c.strokeStyle = '#22242a'; c.lineWidth = 2;
        c.beginPath(); c.moveTo(-hl * 0.6, -hw + 6); c.lineTo(-hl * 0.6, hw - 6); c.stroke();
        c.beginPath(); c.moveTo(-hl * 0.25, -hw + 6); c.lineTo(-hl * 0.25, hw - 6); c.stroke();
        c.lineWidth = 2.5;
        // kid in the back window
        c.save();
        c.translate(-hl * 0.55, 0);
        c.fillStyle = '#f5c58f';
        c.beginPath(); c.arc(0, 0, 3.4, 0, TAU); c.fill();
        c.fillStyle = '#5a3a1e';
        c.beginPath(); c.arc(-1, -1.2, 2.6, Math.PI * 0.9, Math.PI * 1.9); c.fill();
        c.restore();
        // kid thought bubble
        if (this.kidMood !== 0) {
          c.save();
          c.rotate(-this.h); // keep upright
          const em = this.kidMood > 0 ? '🎉' : '😭';
          c.font = '14px sans-serif'; c.textAlign = 'center';
          c.fillStyle = 'rgba(255,255,255,.95)';
          c.beginPath(); c.arc(-hl * 0.3, -hw - 14, 10, 0, TAU); c.fill();
          c.strokeStyle = '#22242a'; c.lineWidth = 2; c.stroke();
          c.beginPath(); c.arc(-hl * 0.36, -hw - 3, 2.5, 0, TAU); c.fill(); c.stroke();
          c.fillText(em, -hl * 0.3, -hw - 9.5);
          c.restore();
        }
        break;
      }
      case 'limo': {
        c.fillStyle = body; roundRectPath(c, -hl, -hw, L, W, 9); c.fill(); c.stroke();
        c.fillStyle = roofC; roundRectPath(c, -hl * 0.82, -hw + 3.5, L * 0.86, W - 7, 7); c.fill(); c.stroke();
        // long window strip
        c.fillStyle = night ? '#2c3a52' : '#8fb8d8';
        const winY = -hw + 5, winH = W - 10;
        roundRectPath(c, hl * 0.55, winY, L * 0.14, winH, 3); c.fill(); c.stroke();
        for (let i = 0; i < 5; i++) {
          roundRectPath(c, -hl * 0.78 + i * L * 0.25, winY, L * 0.15, winH, 3); c.fill(); c.stroke();
        }
        // silver trim
        c.fillStyle = '#b9bfcc';
        c.fillRect(-hl + 3, -1, L - 6, 2);
        break;
      }
      case 'icecream': {
        c.fillStyle = body; roundRectPath(c, -hl, -hw, L, W, 8); c.fill(); c.stroke();
        // pink awning stripes on roof
        c.save();
        roundRectPath(c, -hl * 0.75, -hw + 3, L * 0.8, W - 6, 6); c.clip();
        c.fillStyle = roofC;
        c.fillRect(-hl, -hw, L, W);
        c.fillStyle = '#fdf6ee';
        for (let i = -4; i < 5; i++) c.fillRect(i * 11 - 3, -hw, 5.5, W);
        c.restore();
        roundRectPath(c, -hl * 0.75, -hw + 3, L * 0.8, W - 6, 6); c.stroke();
        // windshield
        c.fillStyle = night ? '#39506b' : '#bde3ff';
        roundRectPath(c, hl * 0.55, -hw + 5, L * 0.18, W - 10, 3); c.fill(); c.stroke();
        // giant cone ornament
        c.save();
        c.translate(-hl * 0.2, 0);
        c.fillStyle = '#e8b96b';
        c.beginPath(); c.moveTo(8, 0); c.lineTo(-4, -6); c.lineTo(-4, 6); c.closePath(); c.fill(); c.stroke();
        c.fillStyle = '#ff9ecb';
        c.beginPath(); c.arc(-6, 0, 6, 0, TAU); c.fill(); c.stroke();
        c.fillStyle = '#d9534f';
        c.beginPath(); c.arc(-8, -2, 2, 0, TAU); c.fill();
        c.restore();
        break;
      }
      case 'bus': {
        c.fillStyle = body; roundRectPath(c, -hl, -hw, L, W, 8); c.fill(); c.stroke();
        // black bumper stripes
        c.fillStyle = '#2b2d36';
        c.fillRect(-hl + 2, -hw + 2, 5, W - 4);
        c.fillRect(hl - 7, -hw + 2, 5, W - 4);
        // roof
        c.fillStyle = roofC; roundRectPath(c, -hl * 0.86, -hw + 3.5, L * 0.86 + hl * 0.7, W - 7, 6); c.fill(); c.stroke();
        // windows row
        c.fillStyle = night ? '#39506b' : '#bde3ff';
        for (let i = 0; i < 6; i++) {
          roundRectPath(c, -hl * 0.8 + i * L * 0.14, -hw + 5, L * 0.09, W - 10, 2.5); c.fill(); c.stroke();
        }
        roundRectPath(c, hl * 0.62, -hw + 5, L * 0.14, W - 10, 3); c.fill(); c.stroke();
        // stripe with text
        c.fillStyle = '#2b2d36';
        c.font = `800 ${Math.max(7, W * 0.2)}px 'Baloo 2', sans-serif`;
        c.textAlign = 'center'; c.textBaseline = 'middle';
        c.save(); c.scale(1, 0.9); c.fillText('SCHOOL BUS', -hl * 0.1, 0); c.restore();
        // mirrors
        c.fillStyle = '#2b2d36';
        const fx = this.def.wb * M2P / 2 + 8;
        c.fillRect(hl * 0.72, -hw - 6, 3, 7);
        c.fillRect(hl * 0.72, hw - 1, 3, 7);
        c.fillStyle = '#8fb8d8';
        c.fillRect(hl * 0.70, -hw - 8, 7, 4);
        c.fillRect(hl * 0.70, hw + 4, 7, 4);
        // stop arm (left side = -y? use +y side facing road) draw on top side
        if (this.armOut > 0.03) {
          c.save();
          c.translate(hl * 0.1, -hw);
          c.rotate(-this.armOut * Math.PI / 2);
          c.fillStyle = '#2b2d36'; c.fillRect(0, -1.5, 16, 3);
          c.translate(19, 0);
          c.fillStyle = '#e03a2f';
          c.beginPath();
          for (let i = 0; i < 8; i++) {
            const a = i / 8 * TAU + Math.PI / 8;
            const px = Math.cos(a) * 7, py = Math.sin(a) * 7;
            if (i === 0) c.moveTo(px, py); else c.lineTo(px, py);
          }
          c.closePath(); c.fill(); c.stroke();
          c.fillStyle = '#fff'; c.font = "800 5px 'Baloo 2', sans-serif"; c.textAlign = 'center'; c.textBaseline = 'middle';
          c.fillText('STOP', 0, 0.5);
          c.restore();
        }
        break;
      }
      case 'tank': {
        // treads
        c.fillStyle = '#3a3f33';
        roundRectPath(c, -hl, -hw, L, W * 0.3, 6); c.fill(); c.stroke();
        roundRectPath(c, -hl, hw - W * 0.3, L, W * 0.3, 6); c.fill(); c.stroke();
        // tread links animated
        c.fillStyle = '#22242a';
        const off = ((this.wheelSpin * 6) % 8 + 8) % 8;
        for (let x = -hl + 3 - off; x < hl; x += 8) {
          if (x > -hl) { c.fillRect(x, -hw + 2, 3, W * 0.3 - 4); c.fillRect(x, hw - W * 0.3 + 2, 3, W * 0.3 - 4); }
        }
        // hull
        c.fillStyle = body; roundRectPath(c, -hl * 0.92, -hw * 0.62, L * 0.92, W * 0.62, 6); c.fill(); c.stroke();
        c.fillStyle = darken(body, 0.15);
        roundRectPath(c, -hl * 0.8, -hw * 0.45, L * 0.75, W * 0.45, 5); c.fill();
        // turret (rotates independently)
        c.save();
        c.rotate(this.turretA - this.h);
        c.fillStyle = roofC;
        c.beginPath(); c.arc(0, 0, W * 0.28, 0, TAU); c.fill(); c.stroke();
        c.fillStyle = '#4a5438';
        roundRectPath(c, W * 0.2, -3, L * 0.52, 6, 3); c.fill(); c.stroke();
        c.fillStyle = darken(roofC, 0.2);
        c.beginPath(); c.arc(0, 0, W * 0.13, 0, TAU); c.fill(); c.stroke();
        c.restore();
        break;
      }
      case 'ufo': {
        const R = hl; // circular
        // beam when beaming down
        if (this.beam > 0.02) {
          c.save();
          c.globalAlpha = 0.4 * this.beam;
          const g = c.createRadialGradient(0, 0, R * 0.2, 0, 0, R * 1.5);
          g.addColorStop(0, '#a4ffd8'); g.addColorStop(1, 'rgba(164,255,216,0)');
          c.fillStyle = g;
          c.beginPath(); c.arc(0, 0, R * 1.5, 0, TAU); c.fill();
          c.restore();
        }
        // saucer
        c.fillStyle = body;
        c.beginPath(); c.arc(0, 0, R, 0, TAU); c.fill(); c.stroke();
        c.fillStyle = darken(body, 0.12);
        c.beginPath(); c.arc(0, 0, R * 0.78, 0, TAU); c.fill();
        // perimeter lights
        for (let i = 0; i < 8; i++) {
          const a = i / 8 * TAU + this.hoverPhase * 0.5;
          const on = (i + Math.floor(this.hoverPhase * 2)) % 3 === 0;
          c.fillStyle = on ? '#8ef7d2' : '#5d6478';
          c.beginPath(); c.arc(Math.cos(a) * R * 0.88, Math.sin(a) * R * 0.88, 3, 0, TAU); c.fill();
          if (on) { c.strokeStyle = 'rgba(142,247,210,.4)'; c.lineWidth = 2; c.stroke(); c.strokeStyle = '#22242a'; c.lineWidth = 2.5; }
        }
        // dome
        c.fillStyle = 'rgba(160,220,255,.85)';
        c.beginPath(); c.arc(0, 0, R * 0.42, 0, TAU); c.fill(); c.stroke();
        c.fillStyle = 'rgba(255,255,255,.5)';
        c.beginPath(); c.arc(-R * 0.12, -R * 0.12, R * 0.16, 0, TAU); c.fill();
        // little alien
        c.fillStyle = '#69d98e';
        c.beginPath(); c.arc(0, 0, R * 0.14, 0, TAU); c.fill();
        c.fillStyle = '#22242a';
        c.beginPath(); c.arc(-2, -1, 1.6, 0, TAU); c.arc(2, -1, 1.6, 0, TAU); c.fill();
        // direction pointer
        c.fillStyle = '#ffc23e';
        c.beginPath(); c.moveTo(R * 0.7, 0); c.lineTo(R * 0.5, -4); c.lineTo(R * 0.5, 4); c.closePath(); c.fill();
        break;
      }
    }
    // ---------- lights (cars only) ----------
    if (d.drive !== 'ufo' && this.key !== 'tank') {
      // headlights
      c.fillStyle = night ? '#fff3b0' : '#ffe08a';
      c.beginPath(); c.arc(hl - 3, -hw + 4.5, 2.6, 0, TAU); c.fill(); c.stroke();
      c.beginPath(); c.arc(hl - 3, hw - 4.5, 2.6, 0, TAU); c.fill(); c.stroke();
      if (night) {
        c.save();
        c.globalAlpha = 0.28;
        c.fillStyle = '#fff3b0';
        c.beginPath();
        c.moveTo(hl, -hw + 4); c.lineTo(hl + 60, -hw - 16); c.lineTo(hl + 60, hw + 16); c.lineTo(hl, hw - 4);
        c.closePath(); c.fill();
        c.restore();
      }
      // brake / reverse lights
      const bcol = this.braking ? '#ff2f2f' : (this.reversing ? '#ffffff' : '#8a2f2f');
      c.fillStyle = bcol;
      c.beginPath(); c.arc(-hl + 3, -hw + 4.5, 2.4, 0, TAU); c.fill(); c.stroke();
      c.beginPath(); c.arc(-hl + 3, hw - 4.5, 2.4, 0, TAU); c.fill(); c.stroke();
      if (this.braking) {
        c.save(); c.globalAlpha = 0.35; c.fillStyle = '#ff2f2f';
        c.beginPath(); c.arc(-hl + 3, -hw + 4.5, 6, 0, TAU); c.fill();
        c.beginPath(); c.arc(-hl + 3, hw - 4.5, 6, 0, TAU); c.fill();
        c.restore();
      }
    }
    // ---------- damage scuffs on body ----------
    if (this.dents.length) {
      c.save();
      c.globalAlpha = 0.5;
      c.strokeStyle = '#22242a'; c.lineWidth = 1.6;
      for (const dn of this.dents) {
        const dx = Math.cos(dn.a) * hl * 0.8 * dn.r, dy = Math.sin(dn.a) * hw * 0.8 * dn.r;
        c.beginPath();
        c.moveTo(dx - 4, dy - 2); c.lineTo(dx + 1, dy + 1); c.lineTo(dx - 2, dy + 4);
        c.stroke();
      }
      c.restore();
    }
  }
}

// draw a static parked car (simple painter for obstacles)
function drawParkedCar(c, pc, night) {
  c.save();
  c.translate(pc.x, pc.y);
  c.globalAlpha = 0.25;
  c.fillStyle = '#000';
  c.beginPath(); c.ellipse(2, 5, pc.hl + 2, pc.hw + 3, pc.h, 0, TAU); c.fill();
  c.globalAlpha = 1;
  c.rotate(pc.h);
  const L = pc.hl * 2, W = pc.hw * 2;
  c.lineWidth = 2.5; c.strokeStyle = '#22242a'; c.lineJoin = 'round';
  // wheels
  c.fillStyle = '#1c1e24';
  const fx = L * 0.32, wy = pc.hw - 2;
  roundRectPath(c, fx - 5, -wy - 3, 10, 5, 2); c.fill();
  roundRectPath(c, fx - 5, wy - 2, 10, 5, 2); c.fill();
  roundRectPath(c, -fx - 5, -wy - 3, 10, 5, 2); c.fill();
  roundRectPath(c, -fx - 5, wy - 2, 10, 5, 2); c.fill();
  c.fillStyle = pc.color;
  roundRectPath(c, -pc.hl, -pc.hw, L, W, 7); c.fill(); c.stroke();
  if (pc.kind === 'police') {
    c.fillStyle = '#fff';
    roundRectPath(c, -pc.hl * 0.35, -pc.hw, pc.hl * 0.7, W, 4); c.fill(); c.stroke();
    // light bar
    c.fillStyle = '#e23b4a'; c.fillRect(-6, -pc.hw + 4, 6, W - 8);
    c.fillStyle = '#3a7bff'; c.fillRect(0, -pc.hw + 4, 6, W - 8);
    c.strokeRect(-6, -pc.hw + 4, 12, W - 8);
    c.fillStyle = '#2b2d36'; c.font = "800 6px 'Baloo 2',sans-serif"; c.textAlign = 'center'; c.textBaseline = 'middle';
    c.fillText('POLICE', 0, pc.hw * 0.0);
  } else if (pc.kind === 'surfvan') {
    c.fillStyle = '#7ed3c0';
    roundRectPath(c, -pc.hl, -pc.hw, L, W * 0.5, 7); c.fill(); c.stroke();
    // surfboard on roof
    c.fillStyle = '#ffd88f';
    c.beginPath(); c.ellipse(0, 0, pc.hl * 0.75, 4.5, 0, 0, TAU); c.fill(); c.stroke();
    c.strokeStyle = '#e0a52a'; c.lineWidth = 1.5;
    c.beginPath(); c.moveTo(-pc.hl * 0.6, 0); c.lineTo(pc.hl * 0.6, 0); c.stroke();
  } else if (pc.kind === 'taco') {
    // taco stand: colorful cart with striped awning
    c.fillStyle = '#f2d64e';
    roundRectPath(c, -pc.hl, -pc.hw, L, W, 6); c.fill(); c.stroke();
    c.save();
    roundRectPath(c, -pc.hl + 3, -pc.hw + 3, L - 6, W * 0.45, 4); c.clip();
    c.fillStyle = '#e23b4a'; c.fillRect(-pc.hl, -pc.hw, L, W);
    c.fillStyle = '#fffdf6';
    for (let i = -4; i < 5; i++) c.fillRect(i * 12 - 3, -pc.hw, 6, W);
    c.restore();
    roundRectPath(c, -pc.hl + 3, -pc.hw + 3, L - 6, W * 0.45, 4); c.stroke();
    c.fillStyle = '#2b2d36'; c.font = "800 9px 'Baloo 2',sans-serif"; c.textAlign = 'center'; c.textBaseline = 'middle';
    c.fillText('🌮 TACOS', 0, pc.hw * 0.45);
  } else if (pc.kind === 'newsvan') {
    c.fillStyle = '#f4f4f8';
    roundRectPath(c, -pc.hl, -pc.hw, L, W, 6); c.fill(); c.stroke();
    c.fillStyle = '#e23b4a';
    c.font = "800 9px 'Baloo 2',sans-serif"; c.textAlign = 'center'; c.textBaseline = 'middle';
    c.fillText('NEWS 5', 0, 1);
    // satellite dish
    c.fillStyle = '#b9bfcc';
    c.beginPath(); c.arc(-pc.hl * 0.55, 0, 7, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = '#8a90a0';
    c.beginPath(); c.arc(-pc.hl * 0.55, 0, 3, 0, TAU); c.fill();
  } else {
    // generic sedan detail
    const winC = night ? '#39506b' : '#bde3ff';
    c.fillStyle = darken(pc.color, 0.16);
    roundRectPath(c, -pc.hl * 0.55, -pc.hw + 3, pc.hl * 1.1, W - 6, 5); c.fill(); c.stroke();
    c.fillStyle = winC;
    roundRectPath(c, pc.hl * 0.12, -pc.hw + 4.5, pc.hl * 0.28, W - 9, 2.5); c.fill(); c.stroke();
    roundRectPath(c, -pc.hl * 0.45, -pc.hw + 4.5, pc.hl * 0.4, W - 9, 2.5); c.fill(); c.stroke();
  }
  c.restore();
}
