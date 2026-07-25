/* ============================================================
   PART D — world entities, levels, Game core
   ============================================================ */

const LAYOUT = {
  buildTop: 130, sideTopY: 130, roadTop: 200,
  parkTop: 460, curbY: 540, sideBotY: 540, buildBot: 620,
  trafficLaneY: 252, cycleLaneY: 322, driveLaneY: 395,
};

const DISTRICTS = [
  { name: 'QUIET SUBURBS', accent: '#8fbf6f', sky: ['#aee3f5', '#dff3e4'], sidewalk: '#b8d8c7', build: ['#d9c9a8', '#c9b696', '#b9a886'] },
  { name: 'DOWNTOWN RUSH', accent: '#5a79c9', sky: ['#3c4a7a', '#c98fa0'], sidewalk: '#a9bfc9', build: ['#6a7899', '#59688c', '#7c88a8'] },
  { name: 'BEACH BOARDWALK', accent: '#ff9d6f', sky: ['#ff9d6f', '#ffd9a0'], sidewalk: '#e8d5a8', build: ['#f0c9a0', '#e8b98f', '#d9a97f'] },
  { name: 'CHAOS DISTRICT', accent: '#8f7fd9', sky: ['#1c2038', '#4a3a68'], sidewalk: '#9fa8c9', build: ['#4a4f6a', '#3d425c', '#585e7d'] },
];

// ============================================================
// PEDESTRIAN
// ============================================================
const SKIN = ['#f5c58f', '#e0a86f', '#c98a5a', '#8a5a3a', '#6b4226'];
const SHIRT = ['#e05a4e', '#4e8fe0', '#4ec97a', '#e0b34e', '#9a6fd9', '#e07fb0', '#5ac9c9', '#8a8f9a'];
class Pedestrian {
  constructor(x, y, opts) {
    opts = opts || {};
    this.x = x; this.y = y; this.px = x; this.py = y;
    this.homeY = y;
    this.dir = chance(0.5) ? 1 : -1;
    this.speed = rand(16, 26);
    this.r = 7;
    this.skin = pick(SKIN);
    this.shirt = opts.band ? '#d94a3a' : pick(SHIRT);
    this.kid = opts.kid || false;
    this.band = opts.band || false;
    this.blader = opts.blader || false;
    if (this.kid) { this.r = 5.5; this.speed *= 1.2; }
    if (this.blader) this.speed = rand(55, 75);
    if (this.band) { this.speed = 30; this.dir = 1; }
    this.state = 'walk';
    this.t = 0; this.phase = rand(0, TAU);
    this.faceA = this.dir > 0 ? 0 : Math.PI;
    this.stateT = 0;
    this.filmed = false;
    this.diveVx = 0; this.diveVy = 0;
    this.watchSpot = null;
    this.fleeA = 0;
    this.pointT = 0;
    this.abductT = 0;
  }
  setState(s) { this.state = s; this.stateT = 0; }
  scatter(fx, fy, game) {
    if (this.state === 'gone' || this.state === 'dive' || this.state === 'abduct') return;
    this.fleeA = Math.atan2(this.y - fy, this.x - fx);
    this.setState('flee');
    if (game) game.particles.exclaim(this.x, this.y - 14);
  }
  terrify(game) {
    if (this.state === 'flee' || this.state === 'gone' || this.state === 'dive' || this.state === 'abduct') return;
    this.scatter(game.veh.x, game.veh.y, game);
  }
  dive(veh, game) {
    if (this.state === 'dive' || this.state === 'gone' || this.state === 'abduct') return;
    const a = Math.atan2(this.y - veh.y, this.x - veh.x);
    this.diveVx = Math.cos(a) * 150 + veh.vx * 0.3;
    this.diveVy = Math.sin(a) * 150 + veh.vy * 0.3;
    this.setState('dive');
  }
  update(dt, game) {
    this.px = this.x; this.py = this.y;
    this.t += dt; this.stateT += dt;
    const veh = game.veh;
    switch (this.state) {
      case 'walk': {
        this.x += this.dir * this.speed * dt;
        this.faceA = this.dir > 0 ? 0 : Math.PI;
        // stay on sidewalk band, drift to home lane
        this.y += (this.homeY - this.y) * dt * 2;
        if (this.x < 20) { this.dir = 1; }
        if (this.x > WORLD_W - 20) { this.dir = -1; }
        if (this.band) { if (this.x > WORLD_W + 20) this.x = -20; }
        // ice cream attraction
        if (game.jingleOn && !this.band) {
          const d2v = dist2(this.x, this.y, veh.x, veh.y);
          if (d2v < (24 * M2P) ** 2) {
            // walk toward the truck, up to the curb edge
            const targX = veh.x, curb = this.homeY > LAYOUT.curbY ? LAYOUT.curbY + 8 : LAYOUT.roadTop - 8;
            this.x += clamp(targX - this.x, -1, 1) * this.speed * 1.4 * dt;
            this.y += (curb - this.y) * dt * (this.kid ? 2.2 : 0.8);
            this.faceA = Math.atan2(veh.y - this.y, veh.x - this.x);
          }
        }
        break;
      }
      case 'notice': {
        this.faceA = Math.atan2(veh.y - this.y, veh.x - this.x);
        if (this.stateT > rand(1.5, 2.5)) this.setState(game.shame >= 25 ? 'watch' : 'walk');
        break;
      }
      case 'watch': {
        // shuffle to curb edge & stare
        const curb = this.homeY >= LAYOUT.curbY ? LAYOUT.curbY + 9 : LAYOUT.roadTop - 9;
        if (this.watchSpot === null) this.watchSpot = clamp(veh.x + rand(-90, 90), 30, WORLD_W - 30);
        this.x += clamp(this.watchSpot - this.x, -1, 1) * this.speed * 1.2 * dt;
        this.y += (curb - this.y) * dt * 1.6;
        this.faceA = Math.atan2(veh.y - this.y, veh.x - this.x);
        this.pointT -= dt;
        if (this.pointT <= 0) this.pointT = rand(1.4, 3.2);
        if (game.shame < 18 && this.stateT > 4) { this.setState('walk'); this.watchSpot = null; }
        break;
      }
      case 'film': {
        const curb = this.homeY >= LAYOUT.curbY ? LAYOUT.curbY + 9 : LAYOUT.roadTop - 9;
        this.y += (curb - this.y) * dt * 1.6;
        this.faceA = Math.atan2(veh.y - this.y, veh.x - this.x);
        this.pointT -= dt;
        if (this.pointT <= 0) {
          this.pointT = rand(1.2, 2.6);
          game.particles.flash(this.x + Math.cos(this.faceA) * 10, this.y + Math.sin(this.faceA) * 10);
          SFX.cameraClick();
        }
        break;
      }
      case 'flee': {
        this.x += Math.cos(this.fleeA) * 95 * dt;
        this.y += Math.sin(this.fleeA) * 60 * dt;
        // clamp back to sidewalks vertically
        this.faceA = this.fleeA;
        if (this.stateT > 2.2) { this.setState('walk'); this.watchSpot = null; }
        break;
      }
      case 'dive': {
        this.x += this.diveVx * dt; this.y += this.diveVy * dt;
        this.diveVx *= (1 - 4 * dt); this.diveVy *= (1 - 4 * dt);
        if (this.stateT > 1.4) this.setState(this.band ? 'walk' : 'watch');
        break;
      }
      case 'abduct': {
        this.abductT += dt;
        if (this.abductT > 1.2) this.setState('gone');
        break;
      }
    }
    // keep in world & off the buildings
    this.x = clamp(this.x, 6, WORLD_W - 6);
    if (this.state !== 'dive' && this.state !== 'flee') {
      if (this.homeY >= LAYOUT.curbY) this.y = clamp(this.y, LAYOUT.curbY + 6, LAYOUT.buildBot - 4);
      else if (!this.band) this.y = clamp(this.y, LAYOUT.buildTop + 4, LAYOUT.roadTop - 6);
    } else {
      this.y = clamp(this.y, LAYOUT.buildTop + 4, LAYOUT.buildBot - 4);
    }
  }
  draw(c, alpha) {
    if (this.state === 'gone') return;
    const x = lerp(this.px, this.x, alpha), y = lerp(this.py, this.y, alpha);
    const bob = (this.state === 'walk' || this.state === 'flee' || this.blader) ? Math.sin(this.t * (this.state === 'flee' ? 16 : 9) + this.phase) : Math.sin(this.t * 2 + this.phase) * 0.4;
    const lying = this.state === 'dive' && this.stateT < 1.0;
    const abd = this.state === 'abduct' ? this.abductT / 1.2 : 0;
    c.save();
    c.translate(x, y - Math.abs(bob) * 1.5 - abd * 30);
    if (abd > 0) { c.globalAlpha = 1 - abd; c.scale(1 - abd * 0.5, 1 - abd * 0.5); }
    // shadow
    c.save();
    c.globalAlpha = 0.22 * (1 - abd);
    c.fillStyle = '#000';
    c.beginPath(); c.ellipse(0, 4 + Math.abs(bob) * 1.5 + abd * 30, this.r + 2, (this.r + 2) * 0.5, 0, 0, TAU); c.fill();
    c.restore();
    if (lying) c.rotate(Math.PI / 2 * (this.diveVx >= 0 ? 1 : -1) * 0.9);
    c.lineWidth = 2; c.strokeStyle = '#22242a';
    // feet
    if (!lying && (this.state === 'walk' || this.state === 'flee')) {
      c.fillStyle = '#33353d';
      const step = Math.sin(this.t * 9 + this.phase) * 3.5;
      c.beginPath(); c.arc(step, 3.5, 2, 0, TAU); c.fill();
      c.beginPath(); c.arc(-step, 3.5, 2, 0, TAU); c.fill();
    }
    if (this.blader) {
      c.fillStyle = '#ffc23e';
      c.fillRect(-5, 4, 4, 2.4); c.fillRect(1, 4, 4, 2.4);
    }
    // body
    c.fillStyle = this.shirt;
    c.beginPath(); c.ellipse(0, 0, this.r, this.r * 0.82, 0, 0, TAU); c.fill(); c.stroke();
    // arms: pointing / filming
    const fa = this.faceA;
    if (this.state === 'film') {
      c.fillStyle = this.skin;
      const hx = Math.cos(fa) * (this.r + 4), hy = Math.sin(fa) * (this.r + 4);
      c.beginPath(); c.arc(hx * 0.6, hy * 0.6, 2.2, 0, TAU); c.fill();
      c.fillStyle = '#22242a';
      c.save(); c.translate(hx, hy); c.rotate(fa + Math.PI / 2);
      roundRectPath(c, -2.6, -4, 5.2, 8, 1.5); c.fill();
      c.fillStyle = '#8ef7ff'; c.fillRect(-1.6, -2.8, 3.2, 5.6);
      c.restore();
    } else if (this.state === 'watch' && this.pointT > 0 && this.pointT < 0.7) {
      c.strokeStyle = this.skin; c.lineWidth = 3;
      c.beginPath(); c.moveTo(0, 0); c.lineTo(Math.cos(fa) * (this.r + 6), Math.sin(fa) * (this.r + 6)); c.stroke();
      c.strokeStyle = '#22242a'; c.lineWidth = 2;
    }
    // head
    const hx = Math.cos(fa) * 1.8, hy = Math.sin(fa) * 1.8 - 3;
    c.fillStyle = this.skin;
    c.beginPath(); c.arc(hx, hy, this.r * 0.62, 0, TAU); c.fill(); c.stroke();
    if (this.band) { // marching band hat
      c.fillStyle = '#d94a3a';
      c.beginPath(); c.arc(hx, hy - 2, this.r * 0.5, Math.PI, 0); c.fill(); c.stroke();
      c.fillStyle = '#ffc23e';
      c.beginPath(); c.arc(hx, hy - this.r * 0.62, 1.8, 0, TAU); c.fill();
    }
    // head-shake when watching
    if (this.state === 'watch' && Math.sin(this.t * 6) > 0.7) {
      c.fillStyle = '#22242a';
      c.font = "800 8px 'Baloo 2',sans-serif"; c.textAlign = 'center';
      c.fillText('...', hx, hy - this.r);
    }
    c.restore();
  }
}

// ---------- pigeon ----------
class Pigeon {
  constructor(x, y) { this.x = x; this.y = y; this.a = rand(0, TAU); this.state = 'ground'; this.t = 0; this.pt = rand(1, 3); }
  update(dt, game) {
    this.t += dt;
    if (this.state === 'ground') {
      this.pt -= dt;
      if (this.pt <= 0) { this.pt = rand(0.8, 2.5); this.a = rand(0, TAU); }
      this.x += Math.cos(this.a) * 8 * dt; this.y += Math.sin(this.a) * 8 * dt;
      const v = game.veh;
      if (dist2(this.x, this.y, v.x, v.y) < (3.2 * M2P + v.L / 2) ** 2 && v.speedAbs > 15) this.fly(v.x, v.y, game);
    } else if (this.state === 'fly') {
      this.x += this.vx * dt; this.y += this.vy * dt;
      this.ft += dt;
      if (this.ft > 1.6) this.state = 'gone';
    }
  }
  fly(fx, fy, game) {
    if (this.state !== 'ground') return;
    const a = Math.atan2(this.y - fy, this.x - fx) + rand(-0.5, 0.5);
    this.vx = Math.cos(a) * 130; this.vy = Math.sin(a) * 130 - 30;
    this.state = 'fly'; this.ft = 0;
    game.particles.feathers(this.x, this.y);
    SFX.pigeonFlap();
    game.pigeonsScattered++;
    Save.data.stats.pigeonsScattered++;
  }
  draw(c) {
    if (this.state === 'gone') return;
    const lift = this.state === 'fly' ? this.ft * 26 : 0;
    c.save();
    c.translate(this.x, this.y - lift);
    c.globalAlpha = 0.2; c.fillStyle = '#000';
    c.beginPath(); c.ellipse(0, 3 + lift, 5, 2.5, 0, 0, TAU); c.fill();
    c.globalAlpha = 1;
    c.lineWidth = 1.5; c.strokeStyle = '#22242a';
    c.fillStyle = '#8a90a0';
    c.beginPath(); c.ellipse(0, 0, 5, 3.6, this.a, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = '#6a7080';
    c.beginPath(); c.arc(Math.cos(this.a) * 4.5, Math.sin(this.a) * 3, 2.2, 0, TAU); c.fill();
    if (this.state === 'fly') {
      const w = Math.sin(this.ft * 30) * 5;
      c.strokeStyle = '#8a90a0'; c.lineWidth = 2.5;
      c.beginPath(); c.moveTo(-2, 0); c.lineTo(-6, -w); c.moveTo(2, 0); c.lineTo(6, -w); c.stroke();
    }
    c.restore();
  }
}

// ---------- dog ----------
class Dog {
  constructor(x, y) { this.x = x; this.y = y; this.hx = x; this.hy = y; this.state = 'idle'; this.t = 0; this.chaseT = 0; this.cool = 0; this.a = 0; this.barkT = 0; }
  update(dt, game) {
    this.t += dt;
    const v = game.veh;
    if (this.cool > 0) this.cool -= dt;
    if (this.state === 'idle') {
      const d = dist2(this.x, this.y, v.x, v.y);
      if (this.cool <= 0 && d < (9 * M2P) ** 2 && v.speedAbs > 40) { this.state = 'chase'; this.chaseT = 0; SFX.bark(); }
      // amble home
      this.x += (this.hx - this.x) * dt * 0.8;
      this.y += (this.hy - this.y) * dt * 0.8;
    } else {
      this.chaseT += dt;
      const tx = v.x - Math.cos(v.h) * (v.L / 2 + 26);
      const ty = v.y - Math.sin(v.h) * (v.L / 2 + 26);
      this.a = Math.atan2(ty - this.y, tx - this.x);
      const sp = 95;
      this.x += Math.cos(this.a) * sp * dt;
      this.y += Math.sin(this.a) * sp * dt;
      this.barkT -= dt;
      if (this.barkT <= 0) { this.barkT = rand(0.8, 1.6); SFX.bark(); game.texts.add(this.x, this.y - 14, 'WOOF!', '#e0b34e', 13); }
      if (this.chaseT > 4 || v.speedAbs < 15) { this.state = 'idle'; this.cool = 7; }
    }
    this.x = clamp(this.x, 10, WORLD_W - 10);
    this.y = clamp(this.y, LAYOUT.buildTop + 6, LAYOUT.buildBot - 6);
  }
  draw(c) {
    c.save();
    c.translate(this.x, this.y);
    c.globalAlpha = 0.2; c.fillStyle = '#000';
    c.beginPath(); c.ellipse(0, 4, 8, 3.5, 0, 0, TAU); c.fill();
    c.globalAlpha = 1;
    c.rotate(this.state === 'chase' ? this.a : Math.sin(this.t) * 0.2);
    c.lineWidth = 2; c.strokeStyle = '#22242a';
    c.fillStyle = '#a5713d';
    c.beginPath(); c.ellipse(0, 0, 8, 4.5, 0, 0, TAU); c.fill(); c.stroke();
    // tail wag
    const wag = Math.sin(this.t * 14) * 4;
    c.strokeStyle = '#a5713d'; c.lineWidth = 3;
    c.beginPath(); c.moveTo(-8, 0); c.lineTo(-13, wag); c.stroke();
    c.strokeStyle = '#22242a'; c.lineWidth = 2;
    // head + ears
    c.fillStyle = '#a5713d';
    c.beginPath(); c.arc(8, 0, 4, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = '#7d5228';
    c.beginPath(); c.arc(7, -3.4, 1.8, 0, TAU); c.fill();
    c.beginPath(); c.arc(7, 3.4, 1.8, 0, TAU); c.fill();
    c.restore();
  }
}

// ---------- cyclist ----------
class Cyclist {
  constructor() { this.reset(); this.active = false; this.t = 0; }
  reset() { this.x = -60; this.y = LAYOUT.cycleLaneY; this.speed = 0; this.base = 75; this.rung = false; this.active = true; }
  update(dt, game) {
    if (!this.active) return;
    this.t += dt;
    const v = game.veh;
    // blocked check: player obb close in front
    const aheadX = this.x + 70;
    const blocked = Math.abs(v.y - this.y) < 40 && v.x > this.x + 10 && v.x < aheadX + v.L;
    if (blocked) {
      this.speed = Math.max(0, this.speed - 300 * dt);
      if (!this.rung && this.speed < 10) {
        this.rung = true;
        SFX.bell();
        game.addShame(4, 'cyclist');
        game.texts.add(this.x, this.y - 18, 'RING RING!', '#5ac9c9', 15);
      }
    } else {
      this.speed = Math.min(this.base, this.speed + 90 * dt);
      if (this.speed > 30) this.rung = false;
    }
    this.x += this.speed * dt;
    if (this.x > WORLD_W + 80) this.active = false;
  }
  draw(c) {
    if (!this.active) return;
    c.save();
    c.translate(this.x, this.y);
    c.globalAlpha = 0.2; c.fillStyle = '#000';
    c.beginPath(); c.ellipse(0, 4, 12, 4, 0, 0, TAU); c.fill();
    c.globalAlpha = 1;
    c.lineWidth = 2; c.strokeStyle = '#22242a';
    // wheels
    c.fillStyle = '#33353d';
    c.beginPath(); c.arc(-8, 0, 4.5, 0, TAU); c.fill(); c.stroke();
    c.beginPath(); c.arc(8, 0, 4.5, 0, TAU); c.fill(); c.stroke();
    // frame
    c.strokeStyle = '#e05a4e'; c.lineWidth = 2.5;
    c.beginPath(); c.moveTo(-8, 0); c.lineTo(0, -2); c.lineTo(8, 0); c.stroke();
    // rider
    c.lineWidth = 2; c.strokeStyle = '#22242a';
    c.fillStyle = '#4e8fe0';
    c.beginPath(); c.ellipse(0, -3, 5.5, 4, 0, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = '#f5c58f';
    c.beginPath(); c.arc(2.5, -6, 3, 0, TAU); c.fill(); c.stroke();
    c.fillStyle = '#ffc23e'; // helmet
    c.beginPath(); c.arc(2.5, -7, 2.8, Math.PI, 0); c.fill(); c.stroke();
    c.restore();
  }
}

// ---------- traffic car ----------
class TrafficCar {
  constructor(x, color) {
    this.x = x; this.y = LAYOUT.trafficLaneY;
    this.px = x; this.speed = 0; this.base = rand(85, 110);
    this.hl = 27; this.hw = 12; this.h = 0;
    this.color = color || pick(['#c94e6a', '#4e9ac9', '#6ac94e', '#c9a24e', '#7a6ac9']);
    this.honkT = rand(0.5, 1.5);
  }
  get obb() { return { x: this.x, y: this.y, h: 0, hl: this.hl, hw: this.hw }; }
  update(dt, game) {
    this.px = this.x;
    const v = game.veh;
    // blocked if the player is in our lane ahead
    const blocked = Math.abs(v.y - this.y) < 48 && v.x > this.x + this.hl && v.x < this.x + 150 + v.L / 2;
    if (blocked) {
      this.speed = Math.max(0, this.speed - 260 * dt);
      this.honkT -= dt;
      if (this.honkT <= 0 && this.speed < 15) {
        this.honkT = rand(2.2, 3.5);
        SFX.horn('hatch');
        game.addShame(4, 'traffic honk');
        game.texts.add(this.x + this.hl, this.y - 16, 'HONK!', '#ff8f5e', 16);
      }
    } else {
      this.speed = Math.min(this.base, this.speed + 110 * dt);
    }
    this.x += this.speed * dt;
    if (this.x > WORLD_W + 120) { this.x = -120; this.px = this.x; }
  }
  draw(c, alpha, night) {
    drawParkedCar(c, { x: lerp(this.px, this.x, alpha), y: this.y, h: 0, hl: this.hl, hw: this.hw, color: this.color, kind: 'sedan' }, night);
  }
}

// ---------- prop ----------
const PROP_DEFS = {
  cone: { hl: 4.5, hw: 4.5, name: 'cone' },
  pot: { hl: 5.5, hw: 5.5, name: 'flower pot' },
  mailbox: { hl: 6, hw: 5, name: 'mailbox' },
  meter: { hl: 3.2, hw: 3.2, name: 'parking meter' },
  hydrant: { hl: 5, hw: 5, name: 'hydrant' },
  trash: { hl: 6.5, hw: 6, name: 'trash can' },
};
class Prop {
  constructor(type, x, y) {
    const d = PROP_DEFS[type];
    this.type = type; this.x = x; this.y = y; this.h = rand(0, TAU);
    this.hl = d.hl; this.hw = d.hw;
    this.state = 'up'; // up | tumble | down | flat
    this.vx = 0; this.vy = 0; this.vr = 0;
  }
  get obb() { return { x: this.x, y: this.y, h: this.h, hl: this.hl, hw: this.hw }; }
  knock(vx, vy, game) {
    if (this.state !== 'up') return;
    this.state = 'tumble';
    this.vx = vx; this.vy = vy; this.vr = rand(-10, 10);
    game.propsHit++;
    Save.data.stats.propsDestroyed++;
    game.addShame(6, 'prop');
    game.texts.add(this.x, this.y - 12, pick(['CLONK!', 'TIP!', 'OOPS!']), '#ffc23e', 17);
    SFX.thud();
    game.particles.dust(this.x, this.y, 6);
  }
  crushFlat(game) {
    if (this.state === 'flat') return;
    this.state = 'flat';
    game.crushes++;
    Save.data.stats.crushes++;
    game.propsHit++;
    Save.data.stats.propsDestroyed++;
    game.addShame(6, 'crush');
    game.texts.add(this.x, this.y - 12, 'CRUNCH!', '#ff4757', 18);
    SFX.crush();
    game.particles.dust(this.x, this.y, 8);
  }
  update(dt) {
    if (this.state === 'tumble') {
      this.x += this.vx * dt; this.y += this.vy * dt; this.h += this.vr * dt;
      this.vx *= (1 - 3.5 * dt); this.vy *= (1 - 3.5 * dt); this.vr *= (1 - 3 * dt);
      if (Math.hypot(this.vx, this.vy) < 6) this.state = 'down';
    }
  }
  draw(c) {
    c.save();
    c.translate(this.x, this.y);
    const flat = this.state === 'flat';
    const down = this.state === 'down' || this.state === 'tumble';
    c.globalAlpha = 0.22; c.fillStyle = '#000';
    c.beginPath(); c.ellipse(1, 3, this.hl + 2, this.hl * 0.7, 0, 0, TAU); c.fill();
    c.globalAlpha = 1;
    c.rotate(this.h);
    if (flat) c.scale(1.4, 1.4);
    c.lineWidth = 2; c.strokeStyle = '#22242a'; c.lineJoin = 'round';
    const squash = flat ? 0.45 : 1;
    switch (this.type) {
      case 'cone':
        c.fillStyle = '#ff7a3d';
        if (down) { // lying on side
          c.beginPath(); c.moveTo(-4, -4 * squash); c.lineTo(6, 0); c.lineTo(-4, 4 * squash); c.closePath(); c.fill(); c.stroke();
        } else {
          c.beginPath(); c.arc(0, 0, 5, 0, TAU); c.fill(); c.stroke();
          c.fillStyle = '#fff'; c.beginPath(); c.arc(0, 0, 2.6, 0, TAU); c.fill();
          c.fillStyle = '#ff7a3d'; c.beginPath(); c.arc(0, 0, 1.2, 0, TAU); c.fill();
        }
        break;
      case 'pot':
        c.fillStyle = '#c9773d';
        c.beginPath(); c.arc(0, 0, 5.5 * (flat ? 1.1 : 1), 0, TAU); c.fill(); c.stroke();
        if (!down && !flat) {
          c.fillStyle = '#4e9a4e';
          c.beginPath(); c.arc(-2, -2, 2.6, 0, TAU); c.arc(2.4, -1, 2.4, 0, TAU); c.arc(0, 2, 2.4, 0, TAU); c.fill();
          c.fillStyle = '#e05a8a';
          c.beginPath(); c.arc(0, -1, 1.6, 0, TAU); c.fill();
        } else {
          c.fillStyle = '#8a5a3a';
          c.beginPath(); c.ellipse(3, 0, 4, 2.5, 0.3, 0, TAU); c.fill();
          c.fillStyle = '#4e9a4e';
          c.beginPath(); c.arc(6, 1, 2, 0, TAU); c.fill();
        }
        break;
      case 'mailbox':
        c.fillStyle = '#3a6ac9';
        roundRectPath(c, -6, -5 * squash, 12, 10 * squash, 3); c.fill(); c.stroke();
        c.fillStyle = '#2a4e94';
        roundRectPath(c, -4, -3 * squash, 8, 3 * squash, 1.5); c.fill();
        break;
      case 'meter':
        c.fillStyle = '#8a90a0';
        c.beginPath(); c.arc(0, 0, 3.4, 0, TAU); c.fill(); c.stroke();
        c.fillStyle = down || flat ? '#666' : '#e23b4a';
        c.beginPath(); c.arc(0, 0, 1.6, 0, TAU); c.fill();
        break;
      case 'hydrant':
        c.fillStyle = '#e23b4a';
        c.beginPath(); c.arc(0, 0, 5 * (flat ? 1.15 : 1), 0, TAU); c.fill(); c.stroke();
        c.fillStyle = '#f4d949';
        c.beginPath(); c.arc(0, 0, 2.2, 0, TAU); c.fill(); c.stroke();
        break;
      case 'trash':
        c.fillStyle = '#5a8a5a';
        c.beginPath(); c.arc(0, 0, 6 * (flat ? 1.15 : 1), 0, TAU); c.fill(); c.stroke();
        c.fillStyle = '#4a704a';
        c.beginPath(); c.arc(0, 0, 3.6, 0, TAU); c.fill();
        if (down) {
          c.fillStyle = '#c9c2b4';
          c.beginPath(); c.arc(7, 2, 2, 0, TAU); c.arc(9, -2, 1.6, 0, TAU); c.fill();
        }
        break;
    }
    c.restore();
  }
}

// ============================================================
// LEVELS
// ============================================================
const LEVELS = [
  { id: 1, name: 'Baby Steps', district: 0, veh: 'hatch', par: 50, ratio: 1.9, peds: 4,
    brief: '"A nice quiet street. A huge spot. Even you can\'t mess this up. ...Right?"',
    props: ['cone'], tutorial: true },
  { id: 2, name: 'The Neighbors Are Watching', district: 0, veh: 'hatch', par: 45, ratio: 1.6, peds: 6, dog: true,
    brief: '"Mrs. Henderson has binoculars and a group chat. The dog is also judging you."',
    props: ['pot', 'cone', 'trash'] },
  { id: 3, name: 'Family Outing', district: 0, veh: 'wagon', par: 50, ratio: 1.5, peds: 6,
    brief: '"New wagon, same you. The kid in the back seat believes in you. He\'s the only one."',
    props: ['pot', 'mailbox', 'trash'] },
  { id: 4, name: 'Downtown Debut', district: 1, veh: 'wagon', par: 50, ratio: 1.45, peds: 8, pigeons: 6, traffic: 1,
    brief: '"Big city, small spot, infinite pigeons. They have seen a thousand parkers fail."',
    props: ['meter', 'meter', 'trash', 'cone'] },
  { id: 5, name: 'Rush Hour Squeeze', district: 1, veh: 'hatch', par: 42, ratio: 1.35, peds: 9, pigeons: 5, traffic: 2, cyclist: true,
    brief: '"Everyone is late. Everyone is honking. Everyone is watching you specifically."',
    props: ['meter', 'meter', 'cone', 'trash'] },
  { id: 6, name: 'The Long Goodbye', district: 1, veh: 'limo', par: 80, ratio: 1.12, peds: 8, pigeons: 4, traffic: 1,
    brief: '"The spot is barely longer than the limo. Physics says it fits. Physics is lying."',
    props: ['meter', 'meter', 'cone'] },
  { id: 7, name: 'Jingle Hell', district: 2, veh: 'icecream', par: 60, ratio: 1.4, peds: 10, sand: true, kids: 3,
    brief: '"Level 7: The ice cream jingle cannot be turned off. We\'re sorry."',
    props: ['cone', 'trash'] },
  { id: 8, name: 'Sundae Driver', district: 2, veh: 'icecream', par: 58, ratio: 1.3, peds: 11, sand: true, puddles: 2, bladers: 2, kids: 3,
    brief: '"Rollerbladers, puddles, and children who can smell soft-serve from 400 meters."',
    props: ['cone', 'pot', 'trash'] },
  { id: 9, name: 'Big Yellow', district: 2, veh: 'bus', par: 75, ratio: 1.25, peds: 9, sand: true, bladers: 1, beachSpot: true,
    brief: '"Park the bus between a surf van and a taco stand. The mirrors count. Good luck."',
    props: ['cone', 'trash'] },
  { id: 10, name: 'Lights Out', district: 3, veh: 'bus', par: 75, ratio: 1.3, peds: 8, traffic: 2, night: true, pigeons: 3,
    brief: '"The streetlight budget was cut. Your headlights are the whole show now."',
    props: ['meter', 'cone', 'trash'] },
  { id: 11, name: 'Black Ice', district: 3, veh: 'limo', par: 80, ratio: 1.3, peds: 7, ice: true,
    brief: '"A stretch limo. On ice. The city would like you to know this is technically legal."',
    props: ['cone', 'cone', 'trash'] },
  { id: 12, name: 'The Parade', district: 3, veh: 'tank', par: 95, ratio: 1.35, peds: 10, parade: true, police: true, band: 6,
    brief: '"Park the tank between two police cars. During a parade. Do NOT disturb the band."',
    props: ['cone', 'cone'] },
];
function freeParkLevel(vehKey) {
  return {
    id: 'free', name: 'Free Park', district: 0, veh: vehKey, par: 60,
    ratio: vehKey === 'ufo' ? 1.9 : 1.6, peds: 5, pigeons: 3,
    brief: '"No pressure. Well. Normal pressure."', props: ['cone', 'pot', 'trash'],
  };
}
function dailyLevel(dateKey) {
  let seedNum = 0;
  for (let i = 0; i < dateKey.length; i++) seedNum = (seedNum * 31 + dateKey.charCodeAt(i)) >>> 0;
  const rng = mulberry32(seedNum);
  const vehPool = ['hatch', 'wagon', 'limo', 'icecream', 'bus', 'tank'];
  const veh = vehPool[Math.floor(rng() * vehPool.length)];
  const district = Math.floor(rng() * 4);
  const ratio = 1.15 + rng() * 0.45;
  const lv = {
    id: 'daily', name: 'Daily Challenge', district, veh,
    par: Math.round(40 + VEH_DEFS[veh].len * 4 + (1.6 - ratio) * 60),
    ratio, peds: 6 + Math.floor(rng() * 7),
    pigeons: rng() < 0.5 ? 2 + Math.floor(rng() * 5) : 0,
    traffic: rng() < 0.4 ? 1 + Math.floor(rng() * 2) : 0,
    night: district === 3 && rng() < 0.5,
    ice: district === 3 && rng() < 0.35,
    sand: district === 2 && rng() < 0.7,
    puddles: rng() < 0.3 ? 2 : 0,
    dog: rng() < 0.25, cyclist: rng() < 0.3,
    brief: `"Seed of the day: ${dateKey}. Same nightmare for everyone. No excuses."`,
    props: ['cone', 'trash', 'pot', 'meter'].slice(0, 2 + Math.floor(rng() * 3)),
    seed: seedNum,
  };
  return lv;
}

// ============================================================
// GAME
// ============================================================
const COMIC = ['BONK!', 'CRUNCH!', 'OOF!', 'WHAM!', 'THUD!', 'YIKES!'];
class Game {
  constructor(canvas) {
    this.cv = canvas;
    this.c = canvas.getContext('2d');
    this.particles = new ParticleSystem();
    this.texts = new FloatTexts();
    this.skids = new SkidLayer(WORLD_W, WORLD_H);
    this.nightCv = document.createElement('canvas');
    this.state = 'idle';
    this.demo = false;
    this.timeScale = 1;
    this.slowT = 0;
    this.cam = { x: 800, y: 400, zoom: 1, zoomMul: 1, wide: false, shake: 0 };
    this.veh = null;
    this.level = null;
    this.resize();
  }
  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.cv.width = Math.floor(innerWidth * dpr);
    this.cv.height = Math.floor(innerHeight * dpr);
    this.dpr = dpr;
    this.nightCv.width = this.cv.width; this.nightCv.height = this.cv.height;
  }
  // ---------------- level setup ----------------
  startLevel(def, opts) {
    opts = opts || {};
    this.level = def;
    this.demo = !!opts.demo;
    this.daily = def.id === 'daily';
    const rng = def.seed !== undefined ? mulberry32(def.seed + 7) : null;
    this.rnd = rng ? rng : Math.random;
    this.buildWorld(def);
    // state
    this.state = opts.demo ? 'play' : 'countdown';
    this.t = 0;
    this.shame = 0;
    this.shamePrev = 0;
    this.collisions = 0;
    this.curbMounts = 0;
    this.propsHit = 0;
    this.honks = 0;
    this.crushes = 0;
    this.pigeonsScattered = 0;
    this.comboChain = 0;
    this.comboPoints = 0;
    this.maneuverT = 0;
    this.maneuverSteer = 0;
    this.stillT = 0;
    this.smoothT = 0;
    this.lastCollisionT = -99;
    this.stallT = 0;
    this.reverseT = 0;
    this.reverseClean = false;
    this.collCooldown = 0;
    this.nearMissCd = 0;
    this.curbMounted = false;
    this.settle = 0;
    this.parkInfo = { inside: false, angleErr: 99, gap: 999, stopped: false };
    this.replayBuf = [];
    this.replayT = 0;
    this.finished = false;
    this.result = null;
    this.tutStage = def.tutorial && !this.demo ? 0 : -1;
    this.tutTimer = 0;
    this.newsVan = null;
    this.filmerAssigned = false;
    this.demoAI = opts.demo ? { t: 0, phase: 0 } : null;
    this.freezeInput = false;
    this.failT = 0;
    this.successT = 0;
    this.skids.clear();
    this.particles.ps.length = 0;
    this.texts.list.length = 0;
    this.cam.x = this.veh.x; this.cam.y = this.veh.y;
    this.cam.zoomMul = 1; this.cam.wide = false; this.cam.shake = 0;
    this.timeScale = 1; this.slowT = 0;
    this.jingleOn = false;
    if (!this.demo) {
      SFX.engineStart(this.veh.key);
      SFX.musicStart(def.district);
      if (this.veh.key === 'icecream') { SFX.jingleStart(); this.jingleOn = Save.data.settings.jingle; }
      const vu = Save.data.stats.vehicleUse;
      vu[this.veh.key] = (vu[this.veh.key] || 0) + 1;
      Save.save();
    }
  }
  buildWorld(def) {
    const rnd = this.rnd;
    const R = (a, b) => a + rnd() * (b - a);
    // vehicle
    const veh = new Vehicle(def.veh, 230, LAYOUT.driveLaneY, 0);
    this.veh = veh;
    // spot geometry
    const spotW = veh.L * def.ratio;
    const spotD = clamp(veh.W + 26, 54, 86);
    const spotX = WORLD_W * 0.58 + R(-60, 60);
    this.spot = { x: spotX, y: LAYOUT.curbY - spotD, w: spotW, h: spotD, cx: spotX + spotW / 2 };
    // static colliders: building strips + world edge walls
    this.statics = [];
    this.statics.push({ x: WORLD_W / 2, y: LAYOUT.buildTop / 2, h: 0, hl: WORLD_W / 2 + 300, hw: LAYOUT.buildTop / 2, kind: 'building' });
    this.statics.push({ x: WORLD_W / 2, y: (LAYOUT.buildBot + WORLD_H) / 2, h: 0, hl: WORLD_W / 2 + 300, hw: (WORLD_H - LAYOUT.buildBot) / 2, kind: 'building' });
    this.statics.push({ x: -20, y: WORLD_H / 2, h: 0, hl: 20, hw: WORLD_H, kind: 'wall' });
    this.statics.push({ x: WORLD_W + 20, y: WORLD_H / 2, h: 0, hl: 20, hw: WORLD_H, kind: 'wall' });
    // parked cars along the curb, leaving the spot open
    this.parked = [];
    const carColors = ['#b0524e', '#4e7ab0', '#6a9a55', '#b09a4e', '#7a5ab0', '#b07a9a', '#5aa5a5', '#8a8f9a'];
    const laneCY = LAYOUT.curbY - 20;
    const placeCar = (cx, kind, hl) => {
      hl = hl || R(26, 32);
      const col = kind === 'police' ? '#2a3a5c' : carColors[Math.floor(rnd() * carColors.length)];
      const pc = { x: cx, y: LAYOUT.curbY - 15, h: 0, hl, hw: kind === 'taco' ? 20 : 12.5, color: col, kind: kind || 'sedan' };
      pc.y = LAYOUT.curbY - pc.hw - 4;
      this.parked.push(pc);
      this.statics.push(Object.assign({}, pc, { kind: kind === 'police' ? 'police' : 'car' }));
      return pc;
    };
    // neighbors
    const leftKind = def.police ? 'police' : (def.beachSpot ? 'surfvan' : 'sedan');
    const rightKind = def.police ? 'police' : (def.beachSpot ? 'taco' : 'sedan');
    const nl = placeCar(this.spot.x - R(28, 33), leftKind);
    const nr = placeCar(this.spot.x + spotW + R(28, 33), rightKind, rightKind === 'taco' ? 34 : undefined);
    // fill rest of curb
    let cx = 90;
    while (cx < nl.x - 95) { placeCar(cx); cx += R(75, 130); }
    cx = nr.x + nr.hl + 70;
    while (cx < WORLD_W - 80) { placeCar(cx); cx += R(75, 140); }
    // props on sidewalks
    this.props = [];
    const propTypes = def.props || [];
    for (let i = 0; i < propTypes.length * 3; i++) {
      const type = propTypes[i % propTypes.length];
      const top = rnd() < 0.5;
      const px = R(60, WORLD_W - 60);
      const py = top ? R(LAYOUT.buildTop + 16, LAYOUT.roadTop - 14) : R(LAYOUT.curbY + 14, LAYOUT.buildBot - 14);
      if (Math.abs(px - this.spot.cx) < 130 && !top) continue;
      this.props.push(new Prop(type, px, py));
    }
    // a couple of cones near the spot on tutorial
    if (def.id === 1) {
      this.props.push(new Prop('cone', this.spot.x - 60, LAYOUT.curbY + 20));
      this.props.push(new Prop('cone', this.spot.x + spotW + 60, LAYOUT.curbY + 20));
    }
    // pedestrians
    this.peds = [];
    const nped = def.peds || 6;
    for (let i = 0; i < nped; i++) {
      const top = rnd() < 0.5;
      const y = top ? R(LAYOUT.buildTop + 20, LAYOUT.roadTop - 20) : R(LAYOUT.curbY + 16, LAYOUT.buildBot - 16);
      this.peds.push(new Pedestrian(R(50, WORLD_W - 50), y, {}));
    }
    for (let i = 0; i < (def.kids || 0); i++) {
      this.peds.push(new Pedestrian(R(50, WORLD_W - 50), R(LAYOUT.curbY + 16, LAYOUT.buildBot - 16), { kid: true }));
    }
    for (let i = 0; i < (def.bladers || 0); i++) {
      this.peds.push(new Pedestrian(R(50, WORLD_W - 50), LAYOUT.roadTop - 16, { blader: true }));
    }
    if (def.band) {
      for (let i = 0; i < def.band; i++) {
        const p = new Pedestrian(150 + i * 55, LAYOUT.cycleLaneY + (i % 2) * 30 - 15, { band: true });
        p.homeY = p.y;
        this.peds.push(p);
      }
    }
    // pigeons
    this.pigeons = [];
    for (let i = 0; i < (def.pigeons || 0); i++) {
      this.pigeons.push(new Pigeon(R(100, WORLD_W - 100), R(LAYOUT.roadTop + 20, LAYOUT.curbY - 60)));
    }
    // dog
    this.dog = def.dog ? new Dog(R(200, WORLD_W - 200), LAYOUT.curbY + 40) : null;
    // cyclist
    this.cyclist = def.cyclist ? new Cyclist() : null;
    if (this.cyclist) { this.cyclist.active = false; this.cyclistT = R(6, 12); }
    // traffic
    this.traffic = [];
    for (let i = 0; i < (def.traffic || 0); i++) {
      this.traffic.push(new TrafficCar(R(0, WORLD_W)));
    }
    // zones
    this.puddles = [];
    for (let i = 0; i < (def.puddles || 0); i++) {
      this.puddles.push({ x: R(250, WORLD_W - 250), y: R(LAYOUT.roadTop + 50, LAYOUT.curbY - 90), rx: R(30, 55), ry: R(18, 30), splashed: false });
    }
    this.sands = [];
    if (def.sand) {
      for (let i = 0; i < 3; i++) {
        this.sands.push({ x: R(200, WORLD_W - 200), y: R(LAYOUT.roadTop + 60, LAYOUT.curbY - 70), rx: R(45, 80), ry: R(28, 45) });
      }
    }
    // decorations (non-colliding): trees, lamps, buildings windows precomputed
    this.decor = { trees: [], lamps: [], bwinTop: [], bwinBot: [], umbrellas: [] };
    for (let x = 100; x < WORLD_W; x += R(180, 260)) {
      if (def.district === 2) this.decor.umbrellas.push({ x, y: LAYOUT.buildTop - R(30, 60), c: pick(['#ff6b57', '#3aa6ff', '#ffc23e']) });
      else this.decor.trees.push({ x, y: R(LAYOUT.buildTop + 8, LAYOUT.buildTop + 20), r: R(12, 18) });
    }
    for (let x = 160; x < WORLD_W; x += 300) {
      this.decor.lamps.push({ x, y: LAYOUT.roadTop - 8 });
      this.decor.lamps.push({ x: x + 150, y: LAYOUT.curbY + 8 });
    }
    // building blocks
    this.buildings = [];
    const bcols = DISTRICTS[def.district].build;
    let bx = 0;
    while (bx < WORLD_W) {
      const bw = R(120, 220);
      this.buildings.push({ x: bx, w: bw, top: true, c: bcols[Math.floor(rnd() * bcols.length)], win: Math.floor(R(2, 5)) });
      bx += bw + 6;
    }
    bx = 0;
    while (bx < WORLD_W) {
      const bw = R(120, 220);
      this.buildings.push({ x: bx, w: bw, top: false, c: bcols[Math.floor(rnd() * bcols.length)], win: Math.floor(R(2, 5)) });
      bx += bw + 6;
    }
    this.beach = def.district === 2;
    this.snowDrift = def.ice ? true : false;
  }
  // ---------------- shame ----------------
  addShame(n, src) {
    if (this.finished || this.demo) return;
    this.shame = clamp(this.shame + n, 0, 100);
    Save.data.stats.totalShame += Math.max(0, n);
    this.shameRiseT = 0.6;
  }
  reduceShame(n) { this.shame = clamp(this.shame - n, 0, 100); }
  checkThresholds() {
    const prev = this.shamePrev, cur = this.shame;
    const cross = th => prev < th && cur >= th;
    if (cross(25)) {
      this.assignWatchers(1);
      UI.banner('MILDLY EMBARRASSING', '#e0a52a');
      SFX.gasp();
    }
    if (cross(50)) {
      this.assignWatchers(randi(3, 5));
      UI.banner('PUBLIC SPECTACLE', '#ff8f2e');
    }
    if (cross(75)) {
      this.assignWatchers(2);
      this.startFilming();
      UI.banner('LOCAL NEWS MATERIAL', '#ff4757');
      if (!this.newsVan && this.level.id !== 'free') {
        this.newsVan = { x: -80, y: LAYOUT.roadTop + 26, targX: clamp(this.spot.cx - 40, 200, WORLD_W - 200), arrived: false };
      }
    }
    if (cur >= 100 && !this.finished) this.failShame();
    this.shamePrev = cur;
  }
  assignWatchers(n) {
    let count = 0;
    const sorted = this.peds.filter(p => p.state === 'walk' || p.state === 'notice')
      .sort((a, b) => dist2(a.x, a.y, this.veh.x, this.veh.y) - dist2(b.x, b.y, this.veh.x, this.veh.y));
    for (const p of sorted) {
      if (count >= n) break;
      if (p.band) continue;
      p.setState('watch'); p.watchSpot = null;
      count++;
    }
  }
  startFilming() {
    const w = this.peds.filter(p => p.state === 'watch');
    if (w.length) {
      const p = w[0];
      p.setState('film');
      if (!p.filmed) { p.filmed = true; this.addShame(3, 'film'); Save.data.stats.pedsScandalized++; }
    }
  }
  get watchers() {
    return this.peds.filter(p => p.state === 'watch' || p.state === 'film').length;
  }
  // ---------------- horn ----------------
  honk() {
    if (this.state !== 'play' || this.freezeInput) return;
    SFX.horn(this.veh.def.horn);
    this.honks++;
    if (this.veh.key === 'ufo') {
      // abduction beam: remove one watcher
      const w = this.peds.filter(p => (p.state === 'watch' || p.state === 'film') && dist2(p.x, p.y, this.veh.x, this.veh.y) < (12 * M2P) ** 2);
      if (w.length) {
        const p = w.sort((a, b) => dist2(a.x, a.y, this.veh.x, this.veh.y) - dist2(b.x, b.y, this.veh.x, this.veh.y))[0];
        p.setState('abduct');
        this.addShame(20, 'abduct');
        Save.data.stats.pedsScandalized++;
        this.texts.add(p.x, p.y - 20, 'ABDUCTED!', '#8ef7d2', 18);
        SFX.beamHum();
        return;
      }
      this.addShame(5, 'honk');
      return;
    }
    this.addShame(5, 'honk');
    const r2 = (8 * M2P) ** 2;
    let scattered = 0;
    for (const p of this.peds) {
      if (dist2(p.x, p.y, this.veh.x, this.veh.y) < r2) { p.scatter(this.veh.x, this.veh.y, this); scattered++; }
    }
    if (scattered) Save.data.stats.pedsScandalized += scattered;
    for (const pg of this.pigeons) {
      if (pg.state === 'ground' && dist2(pg.x, pg.y, this.veh.x, this.veh.y) < r2) pg.fly(this.veh.x, this.veh.y, this);
    }
    this.texts.add(this.veh.x, this.veh.y - this.veh.W - 10, this.veh.key === 'tank' ? 'BRRRMMM!' : 'HONK!', '#ffc23e', 18);
  }
  toggleZoom() {
    this.cam.wide = !this.cam.wide;
  }
  slowMo(scale, dur) {
    if (Save.data.settings.reducedMotion) return;
    this.timeScale = scale;
    this.slowT = dur;
  }
  // ---------------- fixed update ----------------
  fixedUpdate(dt) {
    if (this.state === 'idle') return;
    // slow-mo recovery uses real dt
    const realDt = dt;
    if (this.slowT > 0) {
      this.slowT -= realDt / Math.max(this.timeScale, 0.05);
      if (this.slowT <= 0) this.timeScale = 1;
    }
    if (this.state === 'play' && !this.finished) this.t += dt;
    // input (or demo AI, or frozen)
    let inp = null;
    if (this.state === 'play' && !this.freezeInput) {
      inp = this.demo ? this.demoInput(dt) : Input;
    }
    // vehicle
    const v = this.veh;
    v.surfaceGrip = this.level.ice ? 0.42 : 1;
    for (const s of this.sands) {
      const dx = (v.x - s.x) / s.rx, dy = (v.y - s.y) / s.ry;
      if (dx * dx + dy * dy < 1) { v.surfaceGrip = Math.min(v.surfaceGrip, 0.6); break; }
    }
    v.update(dt, inp, this);
    this.vehicleCollisions(dt);
    this.checkCurbMount(dt);
    this.checkZones(dt);
    // entities
    for (const p of this.peds) p.update(dt, this);
    for (const pg of this.pigeons) pg.update(dt, this);
    if (this.dog) this.dog.update(dt, this);
    if (this.cyclist) {
      if (!this.cyclist.active) {
        this.cyclistT -= dt;
        if (this.cyclistT <= 0) { this.cyclist.reset(); this.cyclistT = rand(14, 24); }
      } else this.cyclist.update(dt, this);
    }
    for (const tc of this.traffic) tc.update(dt, this);
    for (const pr of this.props) pr.update(dt);
    this.particles.update(dt);
    this.texts.update(dt);
    this.skids.fade(dt);
    // news van arrival
    if (this.newsVan && !this.newsVan.arrived) {
      this.newsVan.x += 130 * dt;
      if (this.newsVan.x >= this.newsVan.targX) {
        this.newsVan.arrived = true;
        this.statics.push({ x: this.newsVan.x, y: this.newsVan.y, h: 0, hl: 34, hw: 15, kind: 'car', news: true });
        this.texts.add(this.newsVan.x, this.newsVan.y - 26, 'NEWS 5 IS HERE', '#e23b4a', 15);
      }
    }
    // near-miss detection
    this.checkNearMiss(dt);
    // skid marks & screech
    this.updateSkids(dt);
    // engine sound
    if (!this.demo) {
      SFX.engineUpdate(clamp(v.speedAbs / (v.def.maxSpeed * M2P), 0, 1), inp ? inp.throttle : 0);
      SFX.murmurSet(clamp(this.watchers / 6, 0, 1) * (this.shame > 30 ? 1 : 0.4));
    }
    // gameplay logic
    if (this.state === 'play' && !this.finished && !this.demo) {
      this.jingleOn = this.veh.key === 'icecream' && Save.data.settings.jingle;
      if (this.pigeonsScattered >= 10) UI.unlockAch('pigeon');
      this.updateShameSources(dt, inp);
      this.updateCombo(dt, inp);
      this.updateParking(dt);
      this.updateTutorial();
      this.recordReplay(dt);
      this.checkThresholds();
    }
    if (this.demo) this.updateParkingDemo(dt);
    // success / fail sequences
    if (this.state === 'success') {
      this.successT += realDt;
      if (this.successT > 1.4) this.beginReplay();
    }
    if (this.state === 'fail') {
      this.failT += realDt;
      this.veh.deflate = clamp(this.failT / 1.2, 0, 1);
      if (this.failT > 2.4) this.beginReplay();
    }
    if (this.state === 'replay') this.updateReplay(realDt);
    this.updateCamera(dt);
  }
  // demo AI for the title screen: repeatedly fails to park
  demoInput(dt) {
    const ai = this.demoAI;
    ai.t += dt;
    const v = this.veh;
    const phase = Math.floor(ai.t / 3.2) % 4;
    let steer = 0, throttle = 0;
    const dx = this.spot.cx - v.x;
    if (phase === 0) { throttle = dx > 30 ? 1 : 0; steer = 0; }
    else if (phase === 1) { throttle = -0.8; steer = 0.9; }
    else if (phase === 2) { throttle = 0.7; steer = -0.9; }
    else { throttle = -0.6; steer = -0.5; }
    if (ai.t > 13) { // reset demo
      ai.t = 0;
      v.x = 230; v.y = LAYOUT.driveLaneY; v.h = 0; v.vx = 0; v.vy = 0; v.damage = 0; v.dents.length = 0;
      this.shame = 0;
    }
    return { steer, throttle, handbrake: false };
  }
  updateParkingDemo() { /* demo never parks */ }
  // ---------------- collisions ----------------
  vehicleCollisions(dt) {
    const v = this.veh;
    if (this.collCooldown > 0) this.collCooldown -= dt;
    const bodies = [v.obb, ...v.extraObbs()];
    // vs props (knockable)
    for (const pr of this.props) {
      if (pr.state !== 'up') continue;
      const mtv = obbVsObb(v.obb, pr.obb);
      if (mtv) {
        if (v.key === 'tank') { pr.crushFlat(this); continue; }
        const sp = v.speedAbs;
        if (sp > 45) {
          pr.knock(v.vx * 0.9 + mtv.nx * -40, v.vy * 0.9 + mtv.ny * -40, this);
        } else {
          v.x += mtv.nx * mtv.depth; v.y += mtv.ny * mtv.depth;
          const vn = v.vx * mtv.nx + v.vy * mtv.ny;
          if (vn < 0) { v.vx -= 1.2 * vn * mtv.nx; v.vy -= 1.2 * vn * mtv.ny; }
        }
      }
    }
    // tank crushes tumbled props too
    if (v.key === 'tank') {
      for (const pr of this.props) {
        if (pr.state === 'down' || pr.state === 'tumble') {
          if (obbVsObb(v.obb, pr.obb)) pr.crushFlat(this);
        }
      }
    }
    // vs statics & traffic
    const solids = this.statics.concat(this.traffic.map(t => Object.assign({ kind: 'traffic' }, t.obb)));
    for (const body of bodies) {
      for (const s of solids) {
        const mtv = obbVsObb(body, s);
        if (!mtv) continue;
        // push vehicle out
        v.x += mtv.nx * mtv.depth; v.y += mtv.ny * mtv.depth;
        const vn = v.vx * mtv.nx + v.vy * mtv.ny;
        if (vn < 0) {
          const rest = 0.38;
          v.vx -= (1 + rest) * vn * mtv.nx;
          v.vy -= (1 + rest) * vn * mtv.ny;
          const impact = -vn;
          if (impact > 30 && this.collCooldown <= 0) {
            this.collisionEvent(impact, body.x + mtv.nx * -body.hl * 0.7, body.y + mtv.ny * -body.hw * 0.7, s);
          }
        }
      }
    }
  }
  collisionEvent(impact, cx, cy, s) {
    this.collCooldown = 0.3;
    const v = this.veh;
    const sev = clamp(impact / (v.def.maxSpeed * M2P), 0.1, 1);
    if (!this.demo && this.state === 'play' && !this.finished) {
      this.collisions++;
      Save.data.stats.collisions++;
      this.addShame(8 + 12 * sev, 'collision');
      this.lastCollisionT = this.t;
      this.comboChain = 0;
      this.smoothT = 0;
      if (v.key === 'wagon') { v.kidMood = -1; v.kidMoodT = 2.5; }
      if (s && s.kind === 'police') UI.unlockAch('officer');
      // panic nearby walkers into noticing
      for (const p of this.peds) {
        if (p.state === 'walk' && dist2(p.x, p.y, v.x, v.y) < (14 * M2P) ** 2) p.setState('notice');
      }
    }
    v.applyDamage(6 + 22 * sev, Math.atan2(cy - v.y, cx - v.x) - v.h);
    this.shake(4 + sev * 14);
    this.particles.sparks(cx, cy, Math.round(4 + sev * 12));
    this.texts.add(cx, cy - 12, sev > 0.6 ? pick(['CRUNCH!', 'WHAM!', 'SMASH!']) : pick(['BONK!', 'OOF!', 'THUD!']), sev > 0.6 ? '#ff4757' : '#ffc23e', 16 + sev * 12);
    SFX.crash(sev);
  }
  shake(mag) {
    if (!Save.data.settings.shake || Save.data.settings.reducedMotion) return;
    this.cam.shake = Math.max(this.cam.shake, mag);
  }
  checkCurbMount(dt) {
    const v = this.veh;
    if (v.def.drive === 'ufo') { this.curbMounted = false; return; }
    let mounted = false;
    for (const w of v.worldWheels()) {
      if (w.y > LAYOUT.curbY + 2 || w.y < LAYOUT.roadTop - 2) { mounted = true; break; }
    }
    if (mounted && !this.curbMounted && v.speedAbs > 20) {
      if (!this.demo && this.state === 'play' && !this.finished) {
        this.curbMounts++;
        this.addShame(10, 'curb');
        this.texts.add(v.x, v.y - v.W, 'CURB!', '#ff8f2e', 18);
        // drop a flower pot from the nearest building edge
        const py = v.y < WORLD_H / 2 ? LAYOUT.buildTop + 10 : LAYOUT.buildBot - 10;
        const pot = new Prop('pot', clamp(v.x + rand(-40, 40), 30, WORLD_W - 30), py);
        pot.knock(rand(-20, 20), v.y < WORLD_H / 2 ? 40 : -40, this);
        this.props.push(pot);
        // scare nearby peds
        for (const p of this.peds) {
          if (dist2(p.x, p.y, v.x, v.y) < (5 * M2P) ** 2) { p.scatter(v.x, v.y, this); Save.data.stats.pedsScandalized++; }
        }
      }
      this.shake(8);
      SFX.thud();
      this.particles.dust(v.x, v.y, 8);
    }
    this.curbMounted = mounted;
  }
  checkZones(dt) {
    const v = this.veh;
    for (const pd of this.puddles) {
      const dx = (v.x - pd.x) / pd.rx, dy = (v.y - pd.y) / pd.ry;
      const inside = dx * dx + dy * dy < 1;
      if (inside && !pd.inNow && v.speedAbs > 30) {
        this.particles.splash(v.x, v.y, 14);
        SFX.splash();
        v.slipTimer = 0.9;
      }
      pd.inNow = inside;
    }
  }
  checkNearMiss(dt) {
    if (this.nearMissCd > 0) { this.nearMissCd -= dt; return; }
    const v = this.veh;
    if (v.speedAbs < 30) return;
    const o = v.obb;
    const c = Math.cos(-o.h), s = Math.sin(-o.h);
    for (const p of this.peds) {
      if (p.state === 'dive' || p.state === 'gone' || p.state === 'abduct') continue;
      // point-to-OBB distance
      const lx = (p.x - o.x) * c - (p.y - o.y) * s;
      const ly = (p.x - o.x) * s + (p.y - o.y) * c;
      const qx = clamp(lx, -o.hl, o.hl), qy = clamp(ly, -o.hw, o.hw);
      const d = Math.hypot(lx - qx, ly - qy);
      if (d < p.r + 4) {
        p.dive(v, this);
        this.nearMissCd = 0.8;
        if (!this.demo && this.state === 'play' && !this.finished) {
          const isBand = p.band;
          this.addShame(isBand ? 25 : 15, 'nearmiss');
          Save.data.stats.pedsScandalized++;
          this.texts.add(p.x, p.y - 20, isBand ? 'THE BAND!!' : 'NEAR MISS!', '#ff4757', 20);
          this.slowMo(0.3, 0.5);
          SFX.nearMiss();
          if (isBand) SFX.failTrombone();
        }
        break;
      }
    }
  }
  updateSkids(dt) {
    const v = this.veh;
    if (v.def.drive === 'ufo') { if (!this.demo) SFX.screechSet(0); return; }
    const sliding = v.slideAmt > 26 || (Input.handbrake && v.speedAbs > 30 && this.state === 'play' && !this.freezeInput && !this.demo);
    const hardBrake = v.braking && v.speedAbs > 90;
    if (sliding || hardBrake) {
      const col = this.level.ice ? 'rgba(255,255,255,.8)' : (this.level.sand ? 'rgba(120,100,70,.9)' : 'rgba(24,25,30,1)');
      for (const w of v.worldWheels()) {
        this.skids.mark(w.x, w.y, v.h, clamp((v.slideAmt - 15) / 120 + (hardBrake ? 0.3 : 0), 0.08, 0.5), 4.5, col);
      }
      if (hardBrake && chance(dt * 20)) this.particles.dust(v.x - Math.cos(v.h) * v.L / 2, v.y - Math.sin(v.h) * v.L / 2, 3);
    }
    if (!this.demo) SFX.screechSet(sliding ? clamp(v.slideAmt / 140, 0, 1) : (hardBrake ? 0.3 : 0));
  }
  // ---------------- shame sources over time ----------------
  updateShameSources(dt, inp) {
    const v = this.veh;
    // stall in road while watched
    const inRoad = v.y > LAYOUT.roadTop && v.y < LAYOUT.parkTop;
    if (v.speedAbs < 8 && inRoad) {
      this.stallT += dt;
      if (this.stallT > 4 && this.watchers > 0) this.addShame(2 * dt, 'stall');
    } else this.stallT = 0;
    // over par trickle
    if (this.t > this.level.par) this.addShame(0.7 * dt, 'slow');
    // ufo constant filming
    if (v.key === 'ufo' && this.peds.some(p => p.state !== 'gone')) this.addShame(0.4 * dt, 'ufofilm');
    // smooth driving decay
    const smooth = v.speedAbs > 25 && v.slideAmt < 40 && (this.t - this.lastCollisionT) > 5;
    if (smooth) {
      this.smoothT += dt;
      if (this.smoothT > 5) this.reduceShame(2 * dt);
    } else this.smoothT = 0;
    // clean reverse maneuver
    if (v.reversing && (this.t - this.lastCollisionT) > 1) {
      this.reverseT += dt;
      if (this.reverseT > 1.6 && !this.reverseClean) {
        this.reverseClean = true;
        this.reduceShame(5);
        this.texts.add(v.x, v.y - v.W - 8, '-5 shame: clean reverse', '#3ecf6e', 13);
      }
    } else { this.reverseT = 0; this.reverseClean = false; }
    // shame pulse anim decay
    if (this.shameRiseT > 0) this.shameRiseT -= dt;
  }
  updateCombo(dt, inp) {
    const v = this.veh;
    const moving = v.speedAbs > 30;
    if (moving && (this.t - this.lastCollisionT) > 1.5) {
      this.maneuverT += dt;
      this.maneuverSteer = Math.max(this.maneuverSteer, Math.abs(v.steer));
      this.stillT = 0;
    } else {
      if (this.maneuverT > 1.3 && this.maneuverSteer > 0.25) {
        this.comboChain++;
        if (this.comboChain >= 2) {
          this.comboPoints += 50;
          UI.combo(`SMOOTH x${this.comboChain}`);
          SFX.tone({ type: 'triangle', f0: 600 + this.comboChain * 80, dur: 0.15, vol: 0.15 });
        }
      }
      this.maneuverT = 0; this.maneuverSteer = 0;
      this.stillT += dt;
      if (this.stillT > 2.5) this.comboChain = 0;
    }
  }
  // ---------------- parking ----------------
  updateParking(dt) {
    const v = this.veh, spot = this.spot;
    let inside, angleErr, gap;
    if (v.key === 'ufo') {
      const R = v.L / 2;
      inside = v.x - R >= spot.x - 2 && v.x + R <= spot.x + spot.w + 2 && v.y - R >= spot.y - 2 && v.y + R <= LAYOUT.curbY + 2;
      angleErr = 0;
      gap = (LAYOUT.curbY - (v.y + R)) / M2P * 100;
    } else {
      const cs = v.corners();
      inside = cs.every(p => p.x >= spot.x - 2 && p.x <= spot.x + spot.w + 2 && p.y >= spot.y - 3 && p.y <= LAYOUT.curbY + 2);
      const a = Math.abs(angNorm(v.h));
      angleErr = deg(Math.min(a, Math.PI - a));
      let maxY = -Infinity;
      for (const p of cs) maxY = Math.max(maxY, p.y);
      gap = (LAYOUT.curbY - maxY) / M2P * 100;
    }
    const stopped = v.speedAbs < 3.5;
    const angleOk = angleErr <= 8, gapOk = gap >= -1 && gap <= 40;
    this.parkInfo = { inside, angleErr, gap, stopped, angleOk, gapOk };
    const all = inside && angleOk && gapOk && stopped;
    if (all) {
      this.settle += dt;
      if (v.key === 'ufo') v.beam = clamp(v.beam + dt * 1.2, 0, 1);
      if (this.settle >= 1.5) this.succeed();
    } else {
      this.settle = 0;
      if (v.key === 'ufo') v.beam = clamp(v.beam - dt * 2, 0, 1);
    }
  }
  recordReplay(dt) {
    this.replayAcc = (this.replayAcc || 0) + dt;
    if (this.replayAcc >= 1 / 40) {
      this.replayAcc = 0;
      const v = this.veh;
      this.replayBuf.push({ x: v.x, y: v.y, h: v.h, steer: v.steer, arm: v.armOut, tur: v.turretA, beam: v.beam });
      if (this.replayBuf.length > 200) this.replayBuf.shift();
    }
  }
  updateTutorial() {
    if (this.tutStage < 0) return;
    const v = this.veh;
    const tips = [
      'Drive with <b>WASD</b> or <b>arrow keys</b>',
      'Hold <b>↓/S</b> to reverse — steering flips, like a real car',
      'Head to the <b>glowing spot</b> on the right, along the curb',
      'Match the <b>ghost outline</b>: fully inside, parallel, near the curb',
      'Now <b>hold still 1.5s</b>… don\'t breathe…',
    ];
    let stage = this.tutStage;
    if (stage === 0 && v.speedAbs > 40) stage = 1;
    else if (stage === 1 && v.reversing) stage = 2;
    else if (stage === 2 && Math.abs(v.x - this.spot.cx) < 260) stage = 3;
    else if (stage === 3 && this.parkInfo.inside) stage = 4;
    else if (stage === 4 && this.settle > 0.2) stage = 5;
    if (stage !== this.tutStage) { this.tutStage = stage; }
    UI.tutTip(stage < tips.length ? tips[stage] : null);
  }
  // ---------------- success / fail ----------------
  succeed() {
    if (this.finished) return;
    this.finished = true;
    this.state = 'success';
    this.successT = 0;
    this.freezeInput = true;
    const perfect = this.parkInfo.angleErr <= 2 && this.parkInfo.gap <= 15 && this.parkInfo.gap >= -1;
    this.result = this.computeScore(perfect);
    UI.tutTip(null);
    SFX.screechStop();
    SFX.successStinger(perfect);
    const v = this.veh;
    this.particles.confetti(v.x, v.y - 20, perfect ? 80 : 45);
    SFX.confettiPop();
    if (perfect) {
      this.slowMo(0.3, 1);
      this.texts.add(v.x, v.y - v.W - 16, 'SURGEON PARK!', '#3ecf6e', 26);
      UI.unlockAch('butter');
    } else {
      this.texts.add(v.x, v.y - v.W - 16, 'PARKED!', '#ffc23e', 26);
    }
    if (v.key === 'wagon') { v.kidMood = 1; v.kidMoodT = 4; }
    if (this.t < 10) UI.unlockAch('speedrun');
    if (this.level.id === 12 && this.crushes === 0) UI.unlockAch('pacifist');
    if (this.pigeonsScattered >= 10) UI.unlockAch('pigeon');
    // persist
    const st = Save.data.stats;
    st.parks++;
    if (!st.fastestPark || this.t < st.fastestPark) st.fastestPark = this.t;
    if (typeof this.level.id === 'number') {
      const id = this.level.id;
      this.prevBest = Save.data.bestScores[id] || 0;
      Save.data.stars[id] = Math.max(Save.data.stars[id] || 0, this.result.stars);
      Save.data.bestScores[id] = Math.max(Save.data.bestScores[id] || 0, this.result.total);
      if (!Save.data.bestTimes[id] || this.t < Save.data.bestTimes[id]) Save.data.bestTimes[id] = this.t;
      if (id + 1 > Save.data.unlockedLevel && id < 12) Save.data.unlockedLevel = id + 1;
    } else if (this.daily) {
      const dk = todayKey();
      if (!Save.data.daily[dk]) Save.data.daily[dk] = { board: [] };
      this.prevBest = (Save.data.daily[dk].board[0] || {}).score || 0;
      Save.data.daily[dk].board.push({ score: this.result.total, stars: this.result.stars, time: Math.round(this.t * 10) / 10, coll: this.collisions });
      Save.data.daily[dk].board.sort((a, b) => b.score - a.score);
      Save.data.daily[dk].board = Save.data.daily[dk].board.slice(0, 10);
      Save.data.daily[dk].last = { score: this.result.total, stars: this.result.stars, time: Math.round(this.t * 10) / 10, coll: this.collisions };
    }
    Save.save();
  }
  failShame() {
    if (this.finished) return;
    this.finished = true;
    this.state = 'fail';
    this.failT = 0;
    this.freezeInput = true;
    this.shame = 100;
    this.result = { fail: true, stars: 0, total: 0, lines: [] };
    UI.tutTip(null);
    UI.banner('TOTAL HUMILIATION', '#ff4757');
    SFX.screechStop();
    SFX.laughter();
    SFX.failTrombone();
    $('fail-tint').style.opacity = '1';
    this.shake(10);
    if (this.t < 20) UI.unlockAch('menace');
    // crowd gathers to laugh
    for (const p of this.peds) {
      if (p.state !== 'gone' && p.state !== 'abduct' && !p.band) { p.setState('watch'); p.watchSpot = null; }
    }
    // record daily attempt (a fail still counts, painfully)
    if (this.daily) {
      const dk = todayKey();
      if (!Save.data.daily[dk]) Save.data.daily[dk] = { board: [] };
      const entry = { score: 0, stars: 0, time: Math.round(this.t * 10) / 10, coll: this.collisions };
      Save.data.daily[dk].board.push(entry);
      Save.data.daily[dk].board.sort((a, b) => b.score - a.score);
      Save.data.daily[dk].board = Save.data.daily[dk].board.slice(0, 10);
      Save.data.daily[dk].last = entry;
    }
    Save.save();
  }
  computeScore(perfect) {
    const par = this.level.par;
    const lines = [];
    let total = 1000;
    lines.push({ k: 'Base park', v: 1000 });
    const tb = this.t <= par ? Math.round(500 * (par - this.t) / par) : 0;
    if (tb > 0) { total += tb; lines.push({ k: `Speed bonus (${fmtTime(this.t)})`, v: tb }); }
    if (this.collisions === 0) { total += 300; lines.push({ k: '"Untouched" — zero collisions', v: 300 }); }
    if (perfect) { total += 400; lines.push({ k: '"Surgeon Park" precision', v: 400 }); }
    if (this.shame < 25) { total += 200; lines.push({ k: 'Dignity intact (low shame)', v: 200 }); }
    if (this.comboPoints > 0) { total += this.comboPoints; lines.push({ k: 'Style combos', v: this.comboPoints }); }
    if (this.collisions > 0) { const p = -50 * this.collisions; total += p; lines.push({ k: `Collisions ×${this.collisions}`, v: p }); }
    if (this.curbMounts > 0) { const p = -40 * this.curbMounts; total += p; lines.push({ k: `Curb mounts ×${this.curbMounts}`, v: p }); }
    if (this.propsHit > 0) { const p = -30 * this.propsHit; total += p; lines.push({ k: `Property damage ×${this.propsHit}`, v: p }); }
    if (this.honks > 0) { const p = -10 * this.honks; total += p; lines.push({ k: `Honks ×${this.honks}`, v: p }); }
    total = Math.max(100, Math.round(total));
    const underPar = this.t <= par, noCol = this.collisions === 0;
    let stars = 1;
    if (underPar || noCol) stars = 2;
    if (underPar && noCol && this.shame < 50) stars = 3;
    const grade = total >= 1900 ? 'S' : total >= 1500 ? 'A' : total >= 1100 ? 'B' : total >= 700 ? 'C' : 'D';
    return { total, lines, stars, grade, perfect, time: this.t };
  }
  // ---------------- replay ----------------
  beginReplay() {
    if (this.state === 'replay') return;
    this.preReplayState = this.state;
    this.state = 'replay';
    this.replayT = 0;
    this.replayIdx = 0;
    UI.showReplay(true);
  }
  updateReplay(dt) {
    this.replayT += dt;
    // 0.5x speed: buffer recorded at 40Hz → advance 20 idx/s
    this.replayIdx = this.replayT * 20;
    if (this.replayIdx >= this.replayBuf.length - 1) this.endReplay();
  }
  endReplay() {
    if (this.state !== 'replay') return;
    this.state = 'post';
    UI.showReplay(false);
    UI.showResults(this);
  }
  skipReplay() { this.endReplay(); }
  // ---------------- camera ----------------
  updateCamera(dt) {
    const v = this.veh;
    const distToSpot = Math.hypot(v.x - this.spot.cx, v.y - (this.spot.y + this.spot.h / 2));
    const near = distToSpot < 10 * M2P;
    const targMul = (near ? 1.12 : 1) * (this.cam.wide ? 0.72 : 1);
    this.cam.zoomMul = lerp(this.cam.zoomMul, targMul, clamp(2.2 * dt, 0, 1));
    let base = clamp(Math.min(innerWidth / 880, innerHeight / 540), 0.5, 2.6);
    if (document.body.classList.contains('touch-mode')) base = clamp(base * 1.15, 0.55, 2.6);
    this.cam.zoom = base * this.cam.zoomMul;
    // follow
    const lookAhead = clamp(v.speedAbs * 0.5, 0, 70);
    const tx = v.x + Math.cos(v.h) * lookAhead * (v.speed >= 0 ? 1 : -1);
    const ty = v.y + Math.sin(v.h) * lookAhead * (v.speed >= 0 ? 1 : -1);
    this.cam.x = lerp(this.cam.x, tx, clamp(3.2 * dt, 0, 1));
    this.cam.y = lerp(this.cam.y, ty, clamp(3.2 * dt, 0, 1));
    // clamp to world (with margin for sky border)
    const vw = innerWidth / this.cam.zoom / 2, vh = innerHeight / this.cam.zoom / 2;
    this.cam.x = clamp(this.cam.x, vw - 100, WORLD_W - vw + 100);
    this.cam.y = clamp(this.cam.y, vh - 80, WORLD_H - vh + 80);
    if (WORLD_W < vw * 2) this.cam.x = WORLD_W / 2;
    if (WORLD_H < vh * 2) this.cam.y = WORLD_H / 2;
    if (this.cam.shake > 0) this.cam.shake = Math.max(0, this.cam.shake - 60 * dt);
  }
}
