/* ============================================================
   PART E — Vehicle physics + Game (states, collisions, shame,
   style, parking, cameras, replay, HUD)
   ============================================================ */

// ============================================================
// VEHICLE — physics body + 3D mesh refs
// ============================================================
class Vehicle3D {
  constructor(key, x, y, h, scene) {
    const d = VEH_DEFS[key];
    this.key = key; this.def = d;
    this.x = x; this.y = y; this.h = h;
    this.px = x; this.py = y; this.ph = h;
    this.vx = 0; this.vy = 0;
    this.steer = 0;
    this.steerCmd = 0;
    this.maxSteer = rad(38);
    this.L = d.len; this.W = d.wid;
    this.braking = false;
    this.reversing = false;
    this.damage = 0;
    this.slideAmt = 0;
    this.surfaceGrip = 1;
    this.slipTimer = 0;
    this.bounce = 0;          // vertical bump anim
    this.bounceV = 0;
    // gimmicks
    this.backfireT = rand(4, 9);
    this.armT = rand(6, 11);
    this.armOut = 0;
    this.armDeployed = false;
    this.turretA = h;
    this.hoverPhase = rand(0, TAU);
    this.beam = 0;
    this.refs = CarFactory.build(key);
    this.scene = scene;
    scene.add(this.refs.group);
  }
  get speed() { return this.vx * Math.cos(this.h) + this.vy * Math.sin(this.h); }
  get speedAbs() { return Math.hypot(this.vx, this.vy); }
  get obb() { return { x: this.x, y: this.y, h: this.h, hl: this.L / 2, hw: this.W / 2 }; }
  corners() { return obbCorners(this.obb); }
  extraObbs() {
    const list = [];
    if (this.key === 'bus') {
      const c = Math.cos(this.h), s = Math.sin(this.h);
      const fx = this.x + c * (this.L * 0.38), fy = this.y + s * (this.L * 0.38);
      const off = this.W / 2 + 0.36;
      list.push({ x: fx - s * off, y: fy + c * off, h: this.h, hl: 0.3, hw: 0.3, tag: 'mirror' });
      list.push({ x: fx + s * off, y: fy - c * off, h: this.h, hl: 0.3, hw: 0.3, tag: 'mirror' });
      if (this.armOut > 0.5) {
        const ax = this.x + c * (this.L * 0.1), ay = this.y + s * (this.L * 0.1);
        const aoff = this.W / 2 + 0.8;
        list.push({ x: ax + s * aoff, y: ay - c * aoff, h: this.h, hl: 0.55, hw: 0.55, tag: 'arm' });
      }
    }
    return list;
  }
  stashPrev() { this.px = this.x; this.py = this.y; this.ph = this.h; }
  update(dt, inp, game) {
    this.stashPrev();
    const d = this.def;
    const maxSp = d.maxSpeed * (this.surfaceGrip < 1 ? this.surfaceGrip * 1.1 : 1);
    // keyboard steer arrives as a hard ±1; sweep the command toward it so
    // the wheel turns like a hand is on it (slower attack, quicker return).
    // Tilt already IS an absolute wheel position held by the player's hand, so
    // it gets a near-instant follow instead — the slow attack on top of it was
    // the bulk of the reported tilt latency.
    const steerRaw = inp ? inp.steer : 0;
    const analog = !!(inp && inp.steerAnalog);
    const steerLam = analog ? 20 : (Math.abs(steerRaw) > Math.abs(this.steerCmd) ? 3.9 : 8.0);
    this.steerCmd += (steerRaw - this.steerCmd) * (1 - Math.exp(-steerLam * dt));
    if (Math.abs(this.steerCmd) < 0.001 && steerRaw === 0) this.steerCmd = 0;
    const steerIn = this.steerCmd;
    const thr = inp ? inp.throttle : 0;
    const hb = inp ? inp.handbrake : false;
    this.braking = false;

    if (d.drive === 'ufo') {
      this.h += steerIn * 2.4 * dt;
      if (thr !== 0) {
        this.vx += Math.cos(this.h) * d.accel * thr * dt;
        this.vy += Math.sin(this.h) * d.accel * thr * dt;
        if (chance(0.25) && game) game.particles.puff(this.x - Math.cos(this.h) * 2 * thr, 0.6, this.y - Math.sin(this.h) * 2 * thr, 0.5);
      }
      const sp = this.speedAbs;
      if (sp > maxSp) { this.vx *= maxSp / sp; this.vy *= maxSp / sp; }
      this.vx *= (1 - 0.05 * dt); this.vy *= (1 - 0.05 * dt);
      if (hb) { this.vx *= (1 - 1.1 * dt); this.vy *= (1 - 1.1 * dt); }
      this.hoverPhase += dt * 3;
      this.slideAmt = 0;
    } else if (d.drive === 'tank') {
      let vF = this.speed;
      const rotSpeed = 1.6 * (1 - Math.abs(vF) / (maxSp * 2.2));
      this.h += steerIn * rotSpeed * dt * (vF < -0.45 ? -1 : 1);
      if (thr > 0) {
        const spFrac = clamp(vF / maxSp, 0, 1);
        vF += d.accel * thr * (1.1 - 0.6 * spFrac * spFrac) * dt;
      } else if (thr < 0) {
        if (vF > 0.3) { vF = Math.max(0, vF + d.accel * 2.2 * thr * dt); this.braking = true; }
        else vF += d.accel * 0.8 * thr * dt;
      }
      // tracked resistance: calibrated so the tank actually reaches maxSp
      const kTr = (d.accel * 0.5) / (d.maxSpeed * d.maxSpeed);
      let res = kTr * vF * Math.abs(vF) + Math.sign(vF) * 0.6;
      if (thr === 0) res += vF * 0.4;
      if (Math.abs(vF) > 0.05) {
        const nv = vF - res * dt;
        vF = (nv * vF < 0) ? 0 : nv;
      }
      vF = clamp(vF, -maxSp * 0.6, maxSp);
      if (hb) { vF *= (1 - 6 * dt); this.braking = true; }
      const c = Math.cos(this.h), s = Math.sin(this.h);
      this.vx = c * vF; this.vy = s * vF;
      this.slideAmt = 0;
      // turret tracks nearest pedestrian
      if (game && game.peds && game.peds.list.length) {
        let best = null, bd = Infinity;
        for (const p of game.peds.list) {
          const dd = dist2(this.x, this.y, p.x, p.y);
          if (dd < bd) { bd = dd; best = p; }
        }
        if (best) {
          const want = Math.atan2(best.y - this.y, best.x - this.x);
          this.turretA = angLerp(this.turretA, want, clamp(0.9 * dt, 0, 1));
          if (bd < 8 * 8 && Math.abs(angNorm(this.turretA - want)) < 0.35 && best.state === 'walk' && chance(0.02)) {
            game.peds.dive(best, game, this.x, this.y);
          }
        }
      }
    } else {
      // ---- bicycle model (speed-sensitive steering for road speeds) ----
      const c = Math.cos(this.h), s = Math.sin(this.h);
      let vF = this.vx * c + this.vy * s;
      let vL = -this.vx * s + this.vy * c;
      const vF0 = vF;
      const steerScale = 1 / (1 + Math.abs(vF) * 0.098);
      const target = steerIn * this.maxSteer * steerScale;
      const ds = clamp(target - this.steer, -d.steerSpeed * dt, d.steerSpeed * dt);
      this.steer += ds;
      // dedicated brake decel (not just "negative engine"): strong, speed-independent
      const brakeDecel = Math.min(11, 4.5 + d.accel * 0.9);
      if (thr > 0) {
        if (vF < -0.45) { vF = Math.min(0, vF + brakeDecel * thr * dt); this.braking = true; }
        else {
          // power-limited engine: full punch off the line, tapers near top speed
          const spFrac = clamp(vF / maxSp, 0, 1);
          vF += d.accel * thr * (1.15 - 0.75 * spFrac * spFrac) * dt;
        }
      } else if (thr < 0) {
        if (vF > 0.45) { vF = Math.max(0, vF + brakeDecel * thr * dt); this.braking = true; }
        else vF += d.accel * 0.55 * thr * dt; // reverse
      }
      // resistive forces: quadratic aero (calibrated so top speed == maxSp),
      // constant rolling resistance, light engine braking when coasting
      const kAero = (d.accel * 0.4) / (d.maxSpeed * d.maxSpeed);
      let resist = kAero * vF * Math.abs(vF) + Math.sign(vF) * 0.35;
      if (thr === 0) resist += vF * 0.18;
      if (Math.abs(vF) > 0.05) {
        const nv = vF - resist * dt;
        vF = (nv * vF < 0) ? 0 : nv; // resistance never reverses direction
      }
      if (Math.abs(vF) < 0.09 && thr === 0) vF *= (1 - 8 * dt);
      vF = clamp(vF, -maxSp * 0.3, maxSp * 1.02);
      let grip = d.grip * this.surfaceGrip;
      if (this.slipTimer > 0) grip *= 0.35;
      if (hb) { grip *= 0.1; vF *= (1 - 1.9 * dt); this.braking = true; }
      vL *= Math.max(0, 1 - grip * 9.5 * dt);
      this.slideAmt = Math.abs(vL);
      if (Math.abs(vF) > 0.08) {
        this.h += (vF / d.wb) * Math.tan(this.steer) * dt;
      }
      const c2 = Math.cos(this.h), s2 = Math.sin(this.h);
      this.vx = c2 * vF - s2 * vL;
      this.vy = s2 * vF + c2 * vL;
      this.reversing = vF < -0.3;
      // weight transfer: smoothed longitudinal accel -> nose pitch
      this.accF = lerp(this.accF || 0, (vF - vF0) / dt, clamp(7 * dt, 0, 1));
      this.pitch = clamp(this.accF * 0.0055, -0.055, 0.035);
      // cornering body roll: lean away from the turn on centripetal accel
      const latA = vF * vF * Math.tan(this.steer) / this.def.wb;
      this.roll = lerp(this.roll || 0, clamp(latA * 0.0042, -0.05, 0.05), clamp(6 * dt, 0, 1));
    }

    this.x += this.vx * dt;
    this.y += this.vy * dt;
    if (this.slipTimer > 0) this.slipTimer -= dt;
    // bounce spring (bumps/potholes)
    this.bounceV -= this.bounce * 90 * dt;
    this.bounceV *= (1 - 8 * dt);
    this.bounce += this.bounceV * dt;

    // ---- gimmick timers ----
    if (game && (game.state === 'drive' || game.state === 'park')) {
      if (this.key === 'hatch') {
        this.backfireT -= dt;
        if (this.backfireT <= 0) {
          this.backfireT = rand(6, 14);
          SFX.backfire();
          const bx = this.x - Math.cos(this.h) * this.L * 0.55, by = this.y - Math.sin(this.h) * this.L * 0.55;
          game.particles.puff(bx, 0.4, by, 0.8);
          game.particles.burst(bx, 0.4, by, 4, { color: 0xffaa44, speed: 3 });
        }
      }
      if (this.key === 'bus') {
        this.armT -= dt;
        if (this.armT <= 0) {
          this.armDeployed = !this.armDeployed;
          this.armT = this.armDeployed ? rand(3.5, 5) : rand(7, 13);
          if (this.armDeployed) { SFX.bell(); game.comic.spawn('STOP ARM!', this.x, 3.4, this.y, '#ff6b57'); }
        }
      }
      this.armOut = clamp(this.armOut + (this.armDeployed ? 1.8 : -1.8) * dt, 0, 1);
    }
  }
  // sync 3D mesh from interpolated pose
  render(alpha, night, dt) {
    const r = this.refs;
    const x = lerp(this.px, this.x, alpha), y = lerp(this.py, this.y, alpha);
    const h = this.ph + angNorm(this.h - this.ph) * alpha;
    let elev = clamp(this.bounce, -0.12, 0.3);
    if (this.def.drive === 'ufo') {
      elev += 0.55 + Math.sin(this.hoverPhase) * 0.14 - this.beam * 0.55;
    }
    r.group.position.set(x, elev, y);
    r.group.rotation.y = -h;
    // wheels: physically correct roll (omega = v / r), clamped below the
    // 60fps strobe threshold so spokes never alias into tumbling
    const sp = this.speed;
    const fdt = dt || 1 / 60;
    for (const w of r.wheels) {
      const wm = (w.children && w.children.length) ? w.children[0] : w;
      const wr = (w.userData && w.userData.r) || 0.34;
      wm.rotation.z -= clamp(sp / wr, -24, 24) * fdt;
    }
    for (const sg of r.steer) sg.rotation.y = -this.steer * 1.1;
    // brake lights
    for (const bl of r.brakeLights) bl.material.emissiveIntensity = this.braking ? 2.8 : 0.3;
    // slide lean + accel/brake weight-transfer pitch + cornering roll
    r.group.rotation.z = clamp(-this.slideAmt * 0.02, -0.06, 0.06) + (this.pitch || 0);
    r.group.rotation.x = this.roll || 0;
    // gimmick visuals
    if (this.key === 'bus' && r.armGroup) {
      r.armGroup.rotation.y = lerp(Math.PI / 2.2, 0, this.armOut);
      const flash = this.armOut > 0.5 && (Math.floor(performance.now() / 300) % 2 === 0);
      for (const f of r.flashers) f.material.emissiveIntensity = flash ? 2.6 : 0.15;
    }
    if (this.key === 'tank' && r.turret) r.turret.rotation.y = -(this.turretA - this.h);
    if (this.key === 'ufo') {
      const t = performance.now() / 1000;
      r.ringLights.forEach((lt, i) => {
        lt.material.emissiveIntensity = 1.0 + Math.sin(t * 5 + i * 0.8) * 0.9 + this.beam * 2.0;
      });
      if (r.beam) {
        r.beam.material.opacity = this.beam * 0.4;
        r.beam.scale.y = 0.4 + this.beam;
      }
    }
  }
  dispose() {
    this.scene.remove(this.refs.group);
  }
}

// ============================================================
// POST-PROCESSING — hand-rolled HDR pipeline, zero dependencies.
// Scene renders linear-HDR into a multisampled half-float target;
// a progressive 3-mip bloom chain feeds a composite pass that does
// ACES filmic tonemapping (identical math to three.js), a gentle
// cinematic grade, and sRGB encoding straight to the canvas.
// ============================================================
class PostFX {
  constructor(renderer) {
    this.renderer = renderer;
    this.enabled = false;
    this.ok = false;
    try {
      this.ok = renderer.capabilities.isWebGL2 && renderer.extensions.has('EXT_color_buffer_float');
    } catch (e) { this.ok = false; }
    if (!this.ok) return;
    const T = THREE;
    this.sceneRT = new T.WebGLRenderTarget(2, 2, { type: T.HalfFloatType, samples: 4, depthBuffer: true });
    const mk = () => new T.WebGLRenderTarget(2, 2, { type: T.HalfFloatType, depthBuffer: false });
    this.mips = [{ a: mk(), b: mk() }, { a: mk(), b: mk() }, { a: mk(), b: mk() }];
    this.cam = new T.OrthographicCamera(-1, 1, 1, -1, 0, 1);
    const geo = new T.BufferGeometry();
    geo.setAttribute('position', new T.BufferAttribute(new Float32Array([-1, -1, 0, 3, -1, 0, -1, 3, 0]), 3));
    geo.setAttribute('uv', new T.BufferAttribute(new Float32Array([0, 0, 2, 0, 0, 2]), 2));
    this.quad = new T.Mesh(geo, null);
    this.quad.frustumCulled = false;
    this.quadScene = new T.Scene();
    this.quadScene.add(this.quad);
    const VS = 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }';
    this.brightMat = new T.ShaderMaterial({
      uniforms: { tex: { value: null }, threshold: { value: 1.0 }, knee: { value: 0.45 } },
      vertexShader: VS,
      fragmentShader: `
        uniform sampler2D tex; uniform float threshold, knee; varying vec2 vUv;
        void main(){
          vec3 c = texture2D(tex, vUv).rgb;
          float l = max(max(c.r, c.g), c.b);
          float w = smoothstep(threshold - knee, threshold + knee, l);
          gl_FragColor = vec4(c * w, 1.0);
        }`,
      depthTest: false, depthWrite: false,
    });
    // dir = texel-size-scaled direction; dir == vec2(0) degenerates to a copy,
    // which is how the downsample between mip levels reuses this material
    this.blurMat = new T.ShaderMaterial({
      uniforms: { tex: { value: null }, dir: { value: new T.Vector2(0, 0) } },
      vertexShader: VS,
      fragmentShader: `
        uniform sampler2D tex; uniform vec2 dir; varying vec2 vUv;
        void main(){
          vec3 s = texture2D(tex, vUv).rgb * 0.227027;
          vec2 o1 = dir * 1.3846, o2 = dir * 3.2308;
          s += (texture2D(tex, vUv + o1).rgb + texture2D(tex, vUv - o1).rgb) * 0.3162162;
          s += (texture2D(tex, vUv + o2).rgb + texture2D(tex, vUv - o2).rgb) * 0.0702703;
          gl_FragColor = vec4(s, 1.0);
        }`,
      depthTest: false, depthWrite: false,
    });
    this.compMat = new T.ShaderMaterial({
      uniforms: {
        tScene: { value: null }, tB0: { value: null }, tB1: { value: null }, tB2: { value: null },
        exposure: { value: 1.02 }, bloomStr: { value: 0.5 }, sat: { value: 1.06 },
      },
      vertexShader: VS,
      fragmentShader: `
        uniform sampler2D tScene, tB0, tB1, tB2;
        uniform float exposure, bloomStr, sat;
        varying vec2 vUv;
        vec3 RRTAndODTFit(vec3 v){
          vec3 a = v * (v + 0.0245786) - 0.000090537;
          vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
          return a / b;
        }
        void main(){
          vec3 hdr = texture2D(tScene, vUv).rgb;
          vec3 bloom = texture2D(tB0, vUv).rgb * 0.8
                     + texture2D(tB1, vUv).rgb * 0.6
                     + texture2D(tB2, vUv).rgb * 0.45;
          hdr += bloom * bloomStr;
          // ACES filmic — same constants as three.js ACESFilmicToneMapping
          const mat3 inM = mat3(
            vec3(0.59719, 0.07600, 0.02840),
            vec3(0.35458, 0.90834, 0.13383),
            vec3(0.04823, 0.01566, 0.83777));
          const mat3 outM = mat3(
            vec3( 1.60475, -0.10208, -0.00327),
            vec3(-0.53108,  1.10813, -0.07276),
            vec3(-0.07367, -0.00605,  1.07602));
          vec3 c = hdr * (exposure / 0.6);
          c = clamp(outM * RRTAndODTFit(inM * c), 0.0, 1.0);
          float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
          c = mix(vec3(lum), c, sat);
          // linear -> sRGB (render target bypasses the renderer's own encode)
          c = mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
          gl_FragColor = vec4(c, 1.0);
        }`,
      depthTest: false, depthWrite: false,
    });
  }
  setSize(w, h) {
    if (!this.ok) return;
    w = Math.max(4, w | 0); h = Math.max(4, h | 0);
    this.sceneRT.setSize(w, h);
    for (let i = 0; i < 3; i++) {
      const mw = Math.max(2, w >> (i + 1)), mh = Math.max(2, h >> (i + 1));
      this.mips[i].a.setSize(mw, mh);
      this.mips[i].b.setSize(mw, mh);
      this.mips[i].w = mw; this.mips[i].h = mh;
    }
  }
  render(scene, camera) {
    const r = this.renderer;
    r.setRenderTarget(this.sceneRT);
    r.render(scene, camera);
    // bright pass -> mip0
    this.quad.material = this.brightMat;
    this.brightMat.uniforms.tex.value = this.sceneRT.texture;
    r.setRenderTarget(this.mips[0].a);
    r.render(this.quadScene, this.cam);
    // progressive blur down the chain: each mip blurs the previous result
    this.quad.material = this.blurMat;
    const u = this.blurMat.uniforms;
    let src = this.mips[0].a;
    for (let i = 0; i < 3; i++) {
      const m = this.mips[i];
      if (i > 0) {
        u.tex.value = src.texture; u.dir.value.set(0, 0);
        r.setRenderTarget(m.a);
        r.render(this.quadScene, this.cam);
      }
      u.tex.value = m.a.texture; u.dir.value.set(1 / m.w, 0);
      r.setRenderTarget(m.b);
      r.render(this.quadScene, this.cam);
      u.tex.value = m.b.texture; u.dir.value.set(0, 1 / m.h);
      r.setRenderTarget(m.a);
      r.render(this.quadScene, this.cam);
      src = m.a;
    }
    // composite -> canvas
    this.quad.material = this.compMat;
    const cu = this.compMat.uniforms;
    cu.tScene.value = this.sceneRT.texture;
    cu.tB0.value = this.mips[0].a.texture;
    cu.tB1.value = this.mips[1].a.texture;
    cu.tB2.value = this.mips[2].a.texture;
    r.setRenderTarget(null);
    r.render(this.quadScene, this.cam);
  }
}

// ============================================================
// GAME
// ============================================================
class Game {
  constructor() {
    this.cv = $('game');
    this.renderer = new THREE.WebGLRenderer({ canvas: this.cv, antialias: true, powerPreference: 'high-performance' });
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.02;
    this.renderer.shadowMap.enabled = Save.data.settings.hq;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.dpCap = Input.usingTouch ? 1.6 : 1.85;
    this.scene = new THREE.Scene();
    this.postfx = new PostFX(this.renderer);
    // far plane must exceed maxCamDistToRouteMid + sky radius (900), or the
    // far plane slices the sky sphere into a clear-color "black dome" hole
    this.camera = new THREE.PerspectiveCamera(60, 1, 0.1, 2600);
    this.camPos = new THREE.Vector3(0, 6, -10);
    this.camLook = new THREE.Vector3(0, 0, 0);
    this.camFov = 60;
    this.camMode = 0; // 0 chase, 1 far, 2 top
    this.shakeAmt = 0;
    this.setPost(Save.data.settings.hq); // also performs the initial resize()

    this.comic = new ComicText3D(this.scene);
    this.particles = new Particles3D(this.scene);
    this.skids = new SkidMarks(this.scene);
    this.rain = new Rain(this.scene);

    this.state = 'boot';        // boot|menu|garage|countdown|drive|park|settle|success|fail|replay
    this.world = null;
    this.player = null;
    this.traffic = null;
    this.peds = null;
    this.level = null;
    this.vehKey = 'hatch';
    this.playerProj = { s: 0, t: 0, h: 0, idx: 0, kind: 'road' };
    this.timeScale = 1;
    this.menuCamT = 0;
    this.garageAngle = 0;

    this.hud = {
      timer: $('timer'), parLabel: $('parLabel'), distLeft: $('dist-left'),
      objText: $('obj-text'), dmgFill: $('dmg-fill'), styleRow: $('style-row'),
      shameFill: $('shame-fill'), shameFace: $('shame-face'), shamePct: $('shame-pct'), shameWrap: $('shame-wrap'),
      gps: $('gps-banner'), warn: $('warn-banner'), zone: $('zone-banner'),
      alignW: $('align-widget'), awAngle: $('aw-angle'), awGap: $('aw-gap'),
      awCanvas: $('aw-canvas'), speedo: $('speedo'),
      failTint: $('fail-tint'), flash: $('flash'),
    };
    this.awCtx = this.hud.awCanvas.getContext('2d');
    this.spCtx = this.hud.speedo.getContext('2d');
    this._gpsText = ''; this._warnText = '';
    this.resetRunVars();
  }

  resize() {
    // bloom adds ~1/3 render cost; trade a little supersampling for it
    const cap = this.postfx && this.postfx.enabled ? Math.min(this.dpCap, 1.6) : this.dpCap;
    const dp = Math.min(window.devicePixelRatio || 1, cap);
    const w = Math.max(2, window.innerWidth), h = Math.max(2, window.innerHeight);
    this.renderer.setSize(w, h, false);
    this.renderer.setPixelRatio(dp);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    if (this.postfx) this.postfx.setSize(Math.round(w * dp), Math.round(h * dp));
  }

  // toggling post moves ACES from the renderer into the composite shader,
  // so every lit material must recompile against the new tone mapping
  setPost(on) {
    const use = !!on && this.postfx.ok;
    this.postfx.enabled = use;
    this.renderer.toneMapping = use ? THREE.NoToneMapping : THREE.ACESFilmicToneMapping;
    this.resize();
    this.scene.traverse(o => {
      if (o.material) {
        const ms = Array.isArray(o.material) ? o.material : [o.material];
        for (const m of ms) m.needsUpdate = true;
      }
    });
  }

  renderScene() {
    if (this.postfx.enabled) this.postfx.render(this.scene, this.camera);
    else this.renderer.render(this.scene, this.camera);
  }

  resetRunVars() {
    this.timer = 0;
    this.coinsRun = 0;
    this.shame = 0;
    this.style = 0;
    this.styleCombo = 0;
    this.styleComboT = 0;
    this.collisions = 0;
    this.recentShameT = 0;
    this.calmT = 0;
    this.colCd = 0;
    this.curbCd = 0;
    this.hornCd = 0;
    this.warnT = 0;
    this.thresholdsHit = new Set();
    this.parkT = 0;           // settle progress
    this.inZone = false;
    this.jingleOn = false;
    this.wrongWayT = 0;
    this.smoothMark = 0;
    this.overtakeSet = new Map();
    this.nearMissCd = new Map();
    this.hydrantT = 0;
    this.hydrantPos = null;
    this.endT = 0;
    this.replayBuf = [];
    this.replayHead = 0;
    this.replayT = 0;
    this.finalStats = null;
    this.lastCurbS = -99;
    this.redRegistered = new Set();
    this.distDriven = 0;
    this.parkQuality = null;
  }

  // ---------- world lifecycle ----------
  buildWorld(level, vehKey) {
    this.disposeWorld();
    this.level = level;
    this.vehKey = vehKey || level.veh;
    this.world = new World(this.scene, level, this.vehKey);
    // image-based lighting: PMREM the district sky so car paint/glass/rims
    // pick up believable reflections (MeshStandardMaterial only)
    try {
      if (this.scene.environment) this.scene.environment.dispose();
      const pmrem = new THREE.PMREMGenerator(this.renderer);
      const es = new THREE.Scene();
      const skyClone = this.world.sky.clone();
      skyClone.material = this.world.sky.material.clone();
      es.add(skyClone);
      const D = this.world.dist;
      const eg = new THREE.Mesh(
        new THREE.PlaneGeometry(4000, 4000),
        // desaturate the env floor so metallic rims/paint don't tint green
        new THREE.MeshBasicMaterial({ color: new THREE.Color(D.ground[0]).lerp(new THREE.Color(0x8a8a8a), 0.65) })
      );
      eg.rotation.x = -Math.PI / 2;
      eg.position.y = -4;
      es.add(eg);
      this.scene.environment = pmrem.fromScene(es, 0.06).texture;
      pmrem.dispose();
    } catch (e) { /* env lighting is progressive enhancement */ }
    this.traffic = new Traffic(this.scene, this.world, level);
    this.peds = new Peds(this.scene, this.world, level.peds || 6);
    const start = routePos(this.world.route, 14, (level.lanes - 0.5) * LANE_W);
    this.player = new Vehicle3D(this.vehKey, start.x, start.y, start.h, this.scene);
    this.playerProj = this.world.route.project(start.x, start.y);
    this.rain.setMode(level.snow ? 'snow' : 'rain');
    this.rain.pts.visible = !!(level.rain || level.snow);
    // night/dusk headlights — bright, wide, long, plus a soft near-field
    // fill so the road directly ahead always reads clearly in the dark
    if (this.world.dist.night || level.time === 'dusk') {
      this.headL = new THREE.SpotLight(0xfff2c8, 130, 60, 0.62, 0.6, 1.2);
      this.headL.position.set(this.player.L / 2, 0.8, 0.5);
      this.headR = this.headL.clone();
      this.headR.position.z = -0.5;
      const tgt = new THREE.Object3D();
      tgt.position.set(16, 0, 0);
      const fill = new THREE.PointLight(0xffe8c8, 12, 18, 1.6);
      fill.position.set(this.player.L / 2 + 1.5, 1.4, 0);
      this.player.refs.group.add(tgt, this.headL, this.headR, fill);
      this.headL.target = tgt;
      this.headR.target = tgt;
    }
    // prime traffic
    for (let i = 0; i < 30; i++) this.traffic.spawn(this.playerProj.s, chance(0.7));
    this.skids.clear();
  }
  disposeWorld() {
    if (this.player) { this.player.dispose(); this.player = null; }
    if (this.traffic) { this.traffic.disposeAll(); this.traffic = null; }
    if (this.peds) { this.peds.dispose(); this.peds = null; }
    if (this.world) { this.world.dispose(); this.world = null; }
    this.comic.clear();
    this.headL = this.headR = null;
  }

  startLevel(level, vehKey) {
    this.buildWorld(level, vehKey);
    this.resetRunVars();
    this.hud.parLabel.textContent = level.free ? 'FREE ROAM' : 'PAR ' + fmtTime(level.par);
    this.state = 'countdown';
    this.camMode = 0;
    SFX.musicStart(level.district);
    SFX.engineStart(this.vehKey);
    if (this.vehKey === 'icecream') { this.jingleOn = true; SFX.jingleStart(); }
    const st = Save.data.stats;
    st.vehicleUse[this.vehKey] = (st.vehicleUse[this.vehKey] || 0) + 1;
    Save.save();
  }

  beginDrive() { this.state = 'drive'; Input.calibrateTilt(); }

  // menu attract mode: world for level 1 with ambient traffic
  setMenuMode(mode) {
    if (mode === 'title' && (!this.world || this.level !== LEVELS[0] || this.state === 'garage' || this.player === null)) {
      if (!this.world || this.level !== LEVELS[0]) {
        this.buildWorld(LEVELS[0], 'hatch');
      }
    }
    if (mode === 'title') {
      // park the hero car curbside for the vignette
      const p = routePos(this.world.route, 46, this.world.RW - 1.2);
      this.player.x = p.x; this.player.y = p.y; this.player.h = p.h;
      this.player.px = p.x; this.player.py = p.y; this.player.ph = p.h;
      this.player.vx = this.player.vy = 0;
      this.playerProj = this.world.route.project(p.x, p.y);
      this.state = 'menu';
    } else if (mode === 'garage') {
      this.state = 'garage';
      this.garageAngle = 0.6;
      this.setGarageVehicle(this.vehKey);
    }
  }
  setGarageVehicle(key) {
    if (!this.world) return;
    // showroom pose: dead center of the street — traffic politely queues behind
    const p = routePos(this.world.route, 60, 0);
    if (this.player) this.player.dispose();
    this.vehKey = key;
    this.player = new Vehicle3D(key, p.x, p.y, p.h, this.scene);
    this.playerProj = this.world.route.project(p.x, p.y);
  }

  // ---------- shame ----------
  addShame(amt, label, color) {
    if (this.state !== 'drive' && this.state !== 'park' && this.state !== 'settle') return;
    this.shame = clamp(this.shame + amt, 0, 100);
    Save.data.stats.totalShame += Math.max(0, amt);
    this.recentShameT = 3;
    this.calmT = 0;
    if (label) this.comic.spawn(label, this.player.x, 2.2 + this.player.def.hgt, this.player.y, color || '#ff6b57');
    this.checkThresholds();
    if (this.shame >= 100) this.failShame();
  }
  checkThresholds() {
    const marks = [[25, 'PEOPLE ARE STARING'], [50, 'SOMEONE IS FILMING'], [75, 'A CROWD GATHERS']];
    for (const [pct, msg] of marks) {
      if (this.shame >= pct && !this.thresholdsHit.has(pct)) {
        this.thresholdsHit.add(pct);
        UI.thresholdBanner(msg);
        SFX.gasp();
        if (pct >= 50) SFX.murmurSet((pct - 25) / 75);
      }
    }
  }
  addStyle(amt, label) {
    if (this.state !== 'drive') return;
    this.styleComboT = 4;
    this.styleCombo++;
    const mult = Math.min(4, this.styleCombo);
    const total = amt * mult;
    this.style += total;
    UI.comboPop(`${label} +${total}${mult > 1 ? ' x' + mult : ''}`);
    this.comic.spawn(`+${total}`, this.player.x, 2 + this.player.def.hgt, this.player.y, '#ffc23e');
  }
  trafficHonk(car) {
    this.addShame(1.2, null);
    if (chance(0.5)) this.comic.spawn(pick(['HONK!', 'MOVE IT!', 'BEEP!!']), car.x, 2.2, car.y, '#3aa6ff');
  }
  onFilmed(ped) {
    Save.data.stats.pedsScandalized++;
    this.addShame(2, null);
  }
  onPedDive(ped) {
    SFX.nearMiss();
    Save.data.stats.nearMisses++;
    this.addShame(12, 'SO CLOSE!!', '#ff4757');
    this.comic.spawn('NOPE!', ped.x, 2, ped.y, '#fff');
  }
  honk() {
    if (this.state !== 'drive' && this.state !== 'park') return;
    if (this.hornCd > 0) return;
    this.hornCd = 0.5;
    SFX.horn(this.player.def.horn);
    this.addShame(2, null);
    // peds notice
    for (const ped of this.peds.list) {
      if (dist2(ped.x, ped.y, this.player.x, this.player.y) < 20 * 20 && ped.state === 'walk' && chance(0.4)) {
        ped.state = 'film'; ped.stateT = rand(1.5, 3);
        ped.refs.emote.material.map = Assets.emojiTexture('😒');
        ped.refs.emote.visible = true;
      }
    }
  }
  cycleCamera() {
    this.camMode = (this.camMode + 1) % 3;
    this.camTrans = 1.5; // ease between views instead of snapping
    SFX.uiClick();
  }

  // ---------- fixed update ----------
  fixedUpdate(dt) {
    if (!this.world) return;
    dt *= this.timeScale;
    this.world.updateLights(dt);
    const st = this.state;
    if (UI.tutSteps && (st === 'drive' || st === 'park' || st === 'settle' || st === 'success')) UI.tutorialFrame(this, dt);

    if (st === 'menu' || st === 'garage') {
      this.menuCamT += dt;
      if (this.traffic) {
        this.traffic.update(dt, this);
      }
      if (this.peds) this.peds.update(dt, this);
      if (this.player) this.player.stashPrev();
      return;
    }
    if (st === 'countdown') {
      this.player.stashPrev();
      if (this.traffic) this.traffic.update(dt, this);
      return;
    }
    if (st === 'success' || st === 'fail') {
      this.endT += dt;
      if (this.peds) this.peds.update(dt, this);
      if (st === 'fail') this.convergePeds(dt);
      this.player.stashPrev();
      return;
    }
    if (st === 'replay') {
      this.replayT += dt;
      const fps = 60;
      const idx = Math.min(this.replayBuf.length - 1, Math.floor(this.replayT * fps));
      if (this.replayBuf.length && idx >= 0) {
        const f = this.replayBuf[idx];
        this.player.px = this.player.x; this.player.py = this.player.y; this.player.ph = this.player.h;
        this.player.x = f[0]; this.player.y = f[1]; this.player.h = f[2];
      }
      if (idx >= this.replayBuf.length - 1) UI.endReplay();
      return;
    }
    if (st !== 'drive' && st !== 'park' && st !== 'settle') return;

    // ---- player physics ----
    const inp = (st === 'settle') ? null : Input;
    this.player.update(dt, inp, this);
    this.playerSpeedAbs = this.player.speedAbs;
    const prevS = this.playerProj.s;
    this.playerProj = this.world.route.project(this.player.x, this.player.y, this.playerProj.idx);
    const proj = this.playerProj;
    this.distDriven += Math.abs(proj.s - prevS);

    // ---- timers ----
    this.timer += dt;
    if (this.hornCd > 0) this.hornCd -= dt;
    if (this.colCd > 0) this.colCd -= dt;
    if (this.curbCd > 0) this.curbCd -= dt;
    if (this.recentShameT > 0) this.recentShameT -= dt;
    if (this.styleComboT > 0) { this.styleComboT -= dt; if (this.styleComboT <= 0) this.styleCombo = 0; }
    this.calmT += dt;
    if (this.calmT > 6 && this.shame > 0 && st === 'drive') this.shame = Math.max(0, this.shame - 0.5 * dt);

    // ---- surfaces & zone shame ----
    this.surfaceLogic(dt, proj, prevS);

    // ---- collisions ----
    this.collide(dt);

    // ---- hazards ----
    this.hazards(dt);

    // ---- traffic & peds ----
    this.traffic.update(dt, this);
    this.peds.update(dt, this);
    this.styleScan();

    // ---- audio ----
    SFX.engineUpdate(clamp(this.playerSpeedAbs / this.player.def.maxSpeed, 0, 1), Input.throttle);
    const slide = this.player.slideAmt;
    SFX.screechSet(clamp((slide - 1.2) / 5, 0, 1) * (this.player.surfaceGrip > 0.8 ? 1 : 0.4));
    // skid marks
    if (slide > 1.6 || (Input.handbrake && this.playerSpeedAbs > 3)) {
      const c = Math.cos(this.player.h), s = Math.sin(this.player.h);
      const bx = this.player.x - c * this.player.L * 0.32, by = this.player.y - s * this.player.L * 0.32;
      const off = this.player.W / 2 - 0.15;
      this.skids.add('l', bx - s * off, by + c * off, 0.24);
      this.skids.add('r', bx + s * off, by - c * off, 0.24);
    } else { this.skids.release('l'); this.skids.release('r'); }

    // ---- jingle chaos ----
    this.jingleOn = this.vehKey === 'icecream' && Save.data.settings.jingle && (st === 'drive' || st === 'park');

    // ---- replay ring buffer (last ~14s) ----
    if ((this.frameCt = (this.frameCt || 0) + 1) % 2 === 0) {
      this.replayBuf.push([this.player.x, this.player.y, this.player.h]);
      if (this.replayBuf.length > 840) this.replayBuf.shift();
    }

    // ---- damage fail ----
    if (this.player.damage >= 100 && st !== 'settle') this.failDamage();

    // ---- parking phase ----
    this.parkingLogic(dt, proj, prevS);
  }

  surfaceLogic(dt, proj, prevS) {
    const RW = this.world.RW;
    const absT = Math.abs(proj.t);
    const road = absT < RW;
    const sidewalk = absT >= RW && absT < RW + 0.35 + SIDEWALK_W;
    const spd = this.playerSpeedAbs;
    // surface grip: rain slicks the road, snow more so, off-road is loose
    this.player.surfaceGrip = road || sidewalk ? (this.level.rain ? 0.78 : (this.level.snow ? 0.88 : 1)) : 0.55;
    // black ice: near-zero lateral grip while any wheel is over a patch
    this._onIce = false;
    for (const ice of this.world.icePatches) {
      if (dist2(ice.x, ice.y, this.player.x, this.player.y) < (ice.r + 0.5) * (ice.r + 0.5)) {
        this.player.surfaceGrip *= 0.3;
        this._onIce = true;
        if (spd > 5 && chance(0.05)) this.comic.spawn('ICE!', this.player.x, 2.2, this.player.y, '#9fdcff');
        break;
      }
    }
    // curb hop
    const wasRoad = Math.abs(this._lastT !== undefined ? this._lastT : proj.t) < RW;
    if (road !== wasRoad && spd > 1.5 && this.curbCd <= 0 && this.state !== 'settle') {
      this.curbCd = 1;
      SFX.thud();
      this.player.damage = clamp(this.player.damage + 1.5 * this.player.def.fragility, 0, 100);
      this.player.bounceV = 1.6;
      if (!road) this.addShame(4, 'CURB CHECK!', '#ff8f5e');
      else this.addShame(1.5, null);
    }
    this._lastT = proj.t;
    // sidewalk driving
    let warn = '';
    if (sidewalk && spd > 1 && this.state === 'drive') {
      this.addShame(2.2 * dt, null);
      if (chance(0.02)) this.comic.spawn('OFF THE SIDEWALK!', this.player.x, 2.5, this.player.y, '#ff4757');
      warn = '🚶 SIDEWALK!';
    }
    // grass
    if (!road && !sidewalk && spd > 1 && this.state === 'drive') {
      this.addShame(1.2 * dt, null);
      warn = '🌱 LAWN VIOLATION';
      if (chance(0.1)) this.particles.puff(this.player.x, 0.2, this.player.y, 0.5);
    }
    // wrong way (only mid-route, moving forward against traffic flow)
    const hd = Math.cos(this.player.h - proj.h);
    if (road && proj.t < -0.4 && this.state === 'drive') {
      // oncoming side
      let danger = false;
      for (const car of this.traffic.cars) {
        if (car.dir === -1 && (car.s - proj.s) > -4 && (car.s - proj.s) < 42 && Math.abs(car.t - proj.t) < 2.2) { danger = true; break; }
      }
      if (danger && spd > 2) {
        warn = '⚠️ ONCOMING TRAFFIC!';
        this.addShame(2.4 * dt, null);
      }
    }
    if (road && hd < -0.35 && spd > 3 && this.state === 'drive') {
      this.wrongWayT += dt;
      if (this.wrongWayT > 1) warn = '⛔ WRONG WAY!';
      if (this.wrongWayT > 1.2) this.addShame(1.6 * dt, null);
    } else this.wrongWayT = 0;
    // school zone
    for (const z of this.world.route.zones) {
      if (proj.s > z.s0 && proj.s < z.s1 && z.kind === 'school') {
        if (spd > 6.5) {
          warn = '🚸 SCHOOL ZONE — SLOW!';
          this.addShame(2.2 * dt, null);
        }
      }
    }
    // red light running
    for (const inter of this.world.route.inters) {
      if (!inter.ctrl) continue;
      const line = inter.s0 - 2.5;
      if (prevS < line && proj.s >= line && inter.ctrl.state === 2 && spd > 2 && !this.redRegistered.has(inter.idx)) {
        this.redRegistered.add(inter.idx);
        Save.data.stats.redLights++;
        this.addShame(10, 'RAN THE RED!', '#ff4757');
        SFX.gasp();
      }
      if (inter.ctrl.state === 2 && proj.s < line && line - proj.s < 30 && spd > 8) warn = '🚦 RED LIGHT AHEAD';
    }
    // smooth driving bonus
    if (this.state === 'drive' && this.calmT > 4 && proj.s - this.smoothMark > 180) {
      this.smoothMark = proj.s;
      this.addStyle(20, 'SMOOTH');
    }
    this._warnText = warn;
  }

  collide(dt) {
    if (this.state === 'settle') return;
    const p = this.player;
    const pObb = p.obb;
    const extras = p.extraObbs();
    const isTank = this.vehKey === 'tank';

    const hitEffects = (mtv, other, kind, sev) => {
      // positional fix
      p.x += mtv.nx * mtv.depth;
      p.y += mtv.ny * mtv.depth;
      // velocity response
      const vn = p.vx * mtv.nx + p.vy * mtv.ny;
      if (vn < 0) {
        p.vx -= (1 + 0.38) * vn * mtv.nx;
        p.vy -= (1 + 0.38) * vn * mtv.ny;
        p.vx *= 0.72; p.vy *= 0.72;
      }
      if (this.colCd > 0) return;
      this.colCd = 0.35;
      sev = clamp(sev, 0.1, 1);
      SFX.crash(sev);
      buzz(Math.round(15 + sev * 45));
      this.shakeAmt = Math.min(1, this.shakeAmt + sev * 0.7);
      this.collisions++;
      Save.data.stats.collisions++;
      const cx = p.x + mtv.nx * -p.L * 0.4, cy = p.y + mtv.ny * -p.L * 0.4;
      this.particles.burst(cx, 0.7, cy, Math.round(4 + sev * 10), { color: 0xffd77a, speed: 6 });
      this.particles.puff(cx, 0.6, cy, 1);
      const dmg = sev * 13 * p.def.fragility * (isTank ? 0.15 : 1);
      p.damage = clamp(p.damage + dmg, 0, 100);
      let shame = 5 + sev * 9;
      let label = pick(['BONK!', 'CRUNCH!', 'THUD!', 'OOF!']);
      if (kind === 'traffic') { label = pick(['CRUNCH!', 'INSURANCE!', 'MY BUMPER!']); shame += 3; }
      if (kind === 'precious') { label = 'THE FERRARI!!'; shame += 14; }
      if (kind === 'hydrant') { label = 'GEYSER!'; }
      if (isTank) { shame *= 1.6; label = 'TANK!!'; }
      this.addShame(shame, label);
    };

    // traffic cars
    for (const car of this.traffic.cars) {
      if (dist2(car.x, car.y, p.x, p.y) > 15 * 15) continue;
      const mtv = obbVsObb(pObb, this.traffic.obb(car));
      if (mtv) {
        const rel = Math.hypot(p.vx - Math.cos(car.h) * car.v * car.dir * 0 - 0, p.vy); // player-dominant severity
        hitEffects(mtv, car, 'traffic', (this.playerSpeedAbs + car.v) / 14);
        this.traffic.onHit(car);
        car.v = Math.max(0, car.v - 4);
      }
    }
    // cross traffic
    for (const cr of this.traffic.crossersNear(p.x, p.y, 14)) {
      const mtv = obbVsObb(pObb, { x: cr.x, y: cr.y, h: cr.h, hl: cr.len / 2, hw: cr.wid / 2 });
      if (mtv) {
        hitEffects(mtv, cr, 'traffic', (this.playerSpeedAbs + cr.v) / 14);
        cr.v = 0;
      }
    }
    // parked cars
    for (const pk of this.world.parked) {
      if (dist2(pk.x, pk.y, p.x, p.y) > 14 * 14) continue;
      const mtv = obbVsObb(pObb, pk);
      if (mtv) {
        hitEffects(mtv, pk, pk.precious ? 'precious' : 'parked', this.playerSpeedAbs / 11);
        // wobble the victim
        pk.refs.group.rotation.z = rand(-0.03, 0.03);
      }
    }
    // statics
    for (const stat of this.world.statics) {
      if (dist2(stat.x, stat.y, p.x, p.y) > (stat.hl + 8) * (stat.hl + 8)) continue;
      const mtv = obbVsObb(pObb, stat);
      if (mtv) {
        if (stat.type === 'hydrant') {
          if (!this.hydrantPos) {
            this.hydrantT = 5;
            this.hydrantPos = { x: stat.x, y: stat.y };
            SFX.splash();
            Save.data.stats.propsDestroyed++;
          }
          hitEffects(mtv, stat, 'hydrant', this.playerSpeedAbs / 12);
        } else if (stat.type === 'tree' || stat.type === 'lamp') {
          hitEffects(mtv, stat, 'prop', this.playerSpeedAbs / 13);
        } else {
          hitEffects(mtv, stat, 'wall', this.playerSpeedAbs / 12);
        }
      }
    }
    // cones
    for (const cone of this.world.cones) {
      if (!cone.alive) continue;
      if (dist2(cone.x, cone.y, p.x, p.y) > 7 * 7) continue;
      const mtv = obbVsObb({ x: cone.x, y: cone.y, h: 0, hl: cone.hl, hw: cone.hw }, pObb);
      if (mtv) {
        cone.alive = false;
        if (isTank) {
          SFX.crush();
          cone.crushed = true;
          cone.mesh.scale.y = 0.08;
          cone.mesh.position.y = 0.02;
          Save.data.stats.crushes++;
          this.comic.spawn('CRUNCH', cone.x, 1.2, cone.y, '#f28b30');
        } else {
          SFX.coneBonk();
          cone.vx = p.vx * 0.7 + rand(-2, 2);
          cone.vy = p.vy * 0.7 + rand(-2, 2);
          cone.vz = rand(3, 6);
          cone.z = 0.1;
          cone.vr = rand(-8, 8);
          this.addShame(2, 'BONK!', '#f28b30');
          Save.data.stats.propsDestroyed++;
        }
      }
    }
    // extra obbs (bus mirrors/arm)
    for (const ex of extras) {
      for (const pk of this.world.parked) {
        if (dist2(pk.x, pk.y, ex.x, ex.y) > 10 * 10) continue;
        if (obbVsObb(ex, pk) && this.colCd <= 0) {
          this.colCd = 0.35;
          SFX.thud();
          p.damage = clamp(p.damage + 3, 0, 100);
          this.addShame(5, ex.tag === 'arm' ? 'THE ARM!!' : 'MIRROR!', '#ffc23e');
        }
      }
      for (const car of this.traffic.cars) {
        if (dist2(car.x, car.y, ex.x, ex.y) > 10 * 10) continue;
        if (obbVsObb(ex, this.traffic.obb(car)) && this.colCd <= 0) {
          this.colCd = 0.35;
          SFX.thud();
          this.addShame(5, 'MIRROR!', '#ffc23e');
          this.traffic.onHit(car);
        }
      }
    }
    // bus stop arm halts traffic behind
    if (this.vehKey === 'bus' && p.armOut > 0.5) {
      for (const car of this.traffic.cars) {
        if (car.dir === 1) {
          const ds = this.playerProj.s - car.s;
          if (ds > 0 && ds < 26 && Math.abs(car.t - this.playerProj.t) < 5) car.v = Math.max(0, car.v - 12 * dt);
        }
      }
    }
    // tank/ufo panic aura
    if ((isTank || this.vehKey === 'ufo') && this.playerSpeedAbs > 3) {
      this.traffic.panicNear(p.x, p.y, 16);
    }
  }

  hazards(dt) {
    const p = this.player;
    const spd = this.playerSpeedAbs;
    // flying cones physics
    for (const cone of this.world.cones) {
      if (cone.alive || cone.crushed || cone.done) continue;
      cone.x += cone.vx * dt; cone.y += cone.vy * dt;
      cone.z += cone.vz * dt; cone.vz -= 12 * dt;
      cone.vx *= (1 - 1.2 * dt); cone.vy *= (1 - 1.2 * dt);
      if (cone.z <= 0) { cone.z = 0; cone.done = true; }
      cone.mesh.position.set(cone.x, cone.z, cone.y);
      cone.mesh.rotation.x += cone.vr * dt;
    }
    // free-roam coin pickup
    if (this.level.free && this.world.coinList) {
      for (const c of this.world.coinList) {
        if (c.taken) continue;
        if (dist2(c.x, c.y, p.x, p.y) < 3.2) {
          c.taken = true;
          c.g.visible = false;
          this.coinsRun = (this.coinsRun || 0) + 5;
          SFX.bell();
          this.comic.spawn('+5', c.x, 2.3, c.y, '#ffd24a');
          this.particles.sparkBurst && this.particles.sparkBurst(c.x, 1.1, c.y);
        }
      }
    }
    // potholes
    for (const hole of this.world.potholes) {
      if (hole.cd > 0) { hole.cd -= dt; continue; }
      if (spd > 6 && dist2(hole.x, hole.y, p.x, p.y) < (hole.r + 0.8) * (hole.r + 0.8)) {
        hole.cd = 2;
        SFX.pothole();
        p.bounceV = 2.2;
        p.damage = clamp(p.damage + 2 * p.def.fragility, 0, 100);
        this.shakeAmt = Math.min(1, this.shakeAmt + 0.25);
        this.addShame(1.5, 'POTHOLE!', '#8891a5');
      }
    }
    // speed bumps
    for (const bump of this.world.bumps) {
      if (bump.cd > 0) { bump.cd -= dt; continue; }
      if (Math.abs(this.playerProj.s - bump.s) < 1 && Math.abs(this.playerProj.t) < this.world.RW) {
        bump.cd = 1.5;
        if (spd > 6) {
          SFX.pothole();
          p.bounceV = 3.2;
          p.damage = clamp(p.damage + 2.5 * p.def.fragility, 0, 100);
          this.addShame(2.5, 'AIRBORNE!', '#ffc23e');
        } else if (spd > 1) p.bounceV = 0.9;
      }
    }
    // puddles
    for (const pud of this.world.puddles) {
      if (pud.cd > 0) { pud.cd -= dt; continue; }
      if (spd > 4 && dist2(pud.x, pud.y, p.x, p.y) < (pud.r + 0.6) * (pud.r + 0.6)) {
        pud.cd = 1.6;
        SFX.splash();
        for (let i = 0; i < 6; i++) this.particles.puff(pud.x + rand(-1, 1), 0.3, pud.y + rand(-1, 1), 0.6);
        p.slipTimer = 0.45;
        const soaked = this.peds.soak(pud.x, pud.y, 4.5, this);
        if (soaked > 0) {
          this.addShame(8 * soaked, 'SOAKED THEM!', '#3aa6ff');
          Save.data.stats.pedsScandalized += soaked;
        }
      }
    }
    // hydrant geyser
    if (this.hydrantT > 0 && this.hydrantPos) {
      this.hydrantT -= dt;
      if (chance(0.5)) {
        this.particles.burst(this.hydrantPos.x, 1.2, this.hydrantPos.y, 3, { color: 0x9fd8f0, speed: 3, up: 9, life: 1.1 });
      }
      if (this.hydrantT <= 0) this.hydrantPos = null;
    }
  }

  styleScan() {
    const proj = this.playerProj;
    const pSpd = this.playerSpeedAbs;
    for (const car of this.traffic.cars) {
      const key = car.refs.group.id;
      const rel = car.s - proj.s;
      // overtake: same dir, was ahead, now behind
      if (car.dir === 1) {
        const was = this.overtakeSet.get(key);
        if (was === undefined) this.overtakeSet.set(key, rel);
        else {
          if (was > 2 && rel < -2 && pSpd > car.v + 0.5 && this.colCd <= 0 && this.state === 'drive') {
            this.addStyle(30, 'OVERTAKE!');
            SFX.whoosh();
            Save.data.stats.overtakes++;
          }
          this.overtakeSet.set(key, rel);
        }
      }
      // near miss: close & fast, no collision
      const cd = this.nearMissCd.get(key) || 0;
      if (cd > 0) { this.nearMissCd.set(key, cd - STEP); continue; }
      const d2 = dist2(car.x, car.y, this.player.x, this.player.y);
      const minDim = (car.wid + this.player.W) / 2 + 0.85;
      const closing = Math.abs(pSpd) + car.v;
      if (d2 < (minDim + car.len / 2) * (minDim + car.len / 2) && closing > 9 && this.colCd <= 0 && this.state === 'drive') {
        // finer check: OBB distance approx via slightly inflated obb
        const inflated = { x: car.x, y: car.y, h: car.h, hl: car.len / 2 + 0.8, hw: car.wid / 2 + 0.8 };
        if (obbVsObb(this.player.obb, inflated)) {
          this.nearMissCd.set(key, 3);
          this.addStyle(50, 'CLOSE ONE!');
          SFX.nearMiss();
          Save.data.stats.nearMisses++;
        }
      }
    }
  }

  // ---------- parking ----------
  parkingLogic(dt, proj, prevS) {
    const world = this.world;
    if (!this.inZone && proj.s > world.parkZoneS && this.state === 'drive') {
      this.inZone = true;
      this.state = 'park';
      UI.zoneBanner('🅿️ PARKING ZONE');
      SFX.bell();
      if (this.camMode === 0) { this.camMode = 2; this.camTrans = 3.2; } // long glide into assist view, no snap
      this.hud.alignW.classList.add('show');
      if (this.level.tutorial) UI.tutTip('Slot into the glowing spot — watch the ANGLE and CURB readouts, then hold still!', 6);
    }
    if (!this.inZone) return;

    const spot = world.spot;
    const p = this.player;
    // measure
    const corners = p.corners();
    let inside = true;
    for (const c of corners) if (!pointInObb(c.x, c.y, spot, 0.06)) { inside = false; break; }
    let dAng = angNorm(p.h - spot.h);
    if (spot.type === 'parallel') { // either facing ok
      if (dAng > Math.PI / 2) dAng -= Math.PI;
      if (dAng < -Math.PI / 2) dAng += Math.PI;
    }
    const angOk = this.vehKey === 'ufo' ? true : Math.abs(deg(dAng)) <= 8;
    let curbGap = null;
    if (spot.type === 'parallel') {
      let maxT = -99;
      for (const c of corners) {
        const pr = world.route.project(c.x, c.y, proj.idx);
        if (pr.t > maxT) maxT = pr.t;
      }
      curbGap = world.RW - maxT;
    }
    const curbOk = spot.type !== 'parallel' || (curbGap !== null && curbGap >= -0.02 && curbGap <= 0.4);
    const still = this.playerSpeedAbs < 0.35;
    this.parkMeasure = { inside, dAng, curbGap, angOk, curbOk };

    if (this.state === 'park') {
      if (inside && angOk && curbOk && still) {
        this.state = 'settle';
        this.parkT = 0;
        if (this.vehKey === 'ufo') SFX.beamHum();
      }
    } else if (this.state === 'settle') {
      if (!(inside && angOk && curbOk) || this.playerSpeedAbs > 0.5) {
        this.state = 'park';
        this.player.beam = 0;
        return;
      }
      this.parkT += dt;
      if (this.vehKey === 'ufo') this.player.beam = clamp(this.parkT / 1.5, 0, 1);
      UI.zoneBanner(this.parkT > 0.1 ? '⏳ ' + '▪'.repeat(Math.ceil(this.parkT / 0.375)) + '▫'.repeat(Math.max(0, 4 - Math.ceil(this.parkT / 0.375))) : '');
      if (this.parkT >= 1.5) this.succeed();
    }
  }

  succeed() {
    this.state = 'success';
    this.endT = 0;
    this.player.vx = this.player.vy = 0;
    this.player.beam = 0;
    if (this.world.ghost) this.world.ghost.visible = false;
    if (this.world.beacon) this.world.beacon.visible = false;
    if (this.world.flag) this.world.flag.visible = false;
    document.body.classList.add('cine');
    SFX.stopAllLoops();
    const q = this.parkMeasure;
    // score
    const L = this.level;
    const timeD = L.par - this.timer;
    const timeScore = timeD >= 0 ? Math.min(600, Math.round(timeD * 8)) : Math.max(-400, Math.round(timeD * 4));
    const styleScore = Math.min(800, Math.round(this.style));
    let parkScore = 700;
    const angDeg = Math.abs(deg(q.dAng));
    parkScore += Math.round(Math.max(0, 8 - angDeg) * 25);
    if (q.curbGap !== null) parkScore += Math.round(clamp((0.4 - q.curbGap) / 0.4, 0, 1) * 250);
    const dmgScore = -Math.round(this.player.damage * 4);
    const shameScore = -Math.round(this.shame * 6);
    const cleanBonus = this.collisions === 0 ? 250 : 0;
    const total = Math.max(0, timeScore + styleScore + parkScore + dmgScore + shameScore + cleanBonus);
    const stars = total >= L.s3 ? 3 : (total >= L.s2 ? 2 : 1);
    const sRank = total >= L.s3 + 350 && this.collisions === 0 && this.shame < 25;
    const perfect = angDeg < 2 && (q.curbGap === null || q.curbGap < 0.15);
    const roamCoins = this.coinsRun || 0;
    const coins = L.free
      ? roamCoins + 50 + (perfect ? 50 : 0)
      : Math.max(25, Math.round(total / 12)) + stars * 25 + (sRank ? 100 : 0);
    Save.data.coins = (Save.data.coins || 0) + coins;
    this.coinsRun = 0; // banked — quitToMenu must not bank it again
    this.finalStats = {
      coins,
      lines: [
        L.free ? ['Road coins scooped', roamCoins]
               : ['Time ' + fmtTime(this.timer) + ' (par ' + fmtTime(L.par) + ')', timeScore],
        ['Style points', styleScore],
        ['Parking precision', parkScore],
        ['Damage', dmgScore],
        ['Shame', shameScore],
        ['Clean driving bonus', cleanBonus],
      ],
      total, stars, sRank, perfect, time: this.timer,
      angDeg, curbGap: q.curbGap,
    };
    SFX.successStinger(perfect);
    buzz(perfect ? [30, 50, 30, 50, 120] : [30, 60, 100]);
    this.particles.confettiBurst(this.player.x, 1, this.player.y);
    SFX.confettiPop();
    this.comic.spawn(perfect ? 'PERFECT!!' : 'PARKED!', this.player.x, 3.5, this.player.y, '#3ecf6e', true);
    // challenge verdict rides along to the results screen
    if (L.challenge) {
      this.finalStats.challenge = { target: L.challenge.target, beaten: total > L.challenge.target };
    }
    // save
    const S = Save.data, id = L.id;
    if (L.free) {
      S.stats.parks++;
      S.stats.kmDriven += this.distDriven / 1000;
    } else if (L.weekly) {
      S.stats.parks++;
      S.stats.kmDriven += this.distDriven / 1000;
      const wk = weekKey();
      if (!S.weekly[wk] || total > S.weekly[wk].score) {
        S.weekly[wk] = { score: total, stars, time: Math.round(this.timer * 10) / 10 };
      }
    } else if (L.challenge) {
      S.stats.parks++;
      S.stats.kmDriven += this.distDriven / 1000;
    } else if (!L.daily) {
      S.stats.parks++;
      if (!S.stats.fastestPark || this.timer < S.stats.fastestPark) S.stats.fastestPark = this.timer;
      S.stats.kmDriven += this.distDriven / 1000;
      if ((S.stars[id] || 0) < stars) S.stars[id] = stars;
      if ((S.bestScores[id] || 0) < total) S.bestScores[id] = total;
      if (!S.bestTimes[id] || this.timer < S.bestTimes[id]) S.bestTimes[id] = this.timer;
      if (typeof id === 'number' && id >= S.unlockedLevel && id < LEVELS.length) S.unlockedLevel = id + 1;
    } else {
      const day = (S.daily[todayKey()] = S.daily[todayKey()] || { board: [], shared: false });
      day.board.push({ score: total, stars, time: Math.round(this.timer * 10) / 10 });
      day.board.sort((a, b) => b.score - a.score);
      day.board = day.board.slice(0, 8);
      // streak: one bonus per calendar day, consecutive days compound
      const tk = todayKey();
      if (S.streakLast !== tk) {
        S.streak = (S.streakLast === dateKeyOffset(-1)) ? (S.streak || 0) + 1 : 1;
        S.streakLast = tk;
        const bonus = Math.min(100, S.streak * 10);
        S.coins = (S.coins || 0) + bonus;
        this.finalStats.lines.push(['🔥 Daily streak — day ' + S.streak, bonus]);
      }
    }
    Save.save();
    UI.checkAchievements(this);
    setTimeout(() => UI.showResults(this.finalStats, false), 2400);
  }

  failShame() {
    if (this.level && this.level.free) { this.shame = 96; return; } // free roam never fails
    if (this.state === 'fail') return;
    this.state = 'fail';
    this.endT = 0;
    document.body.classList.add('cine');
    SFX.stopAllLoops();
    SFX.failTrombone();
    buzz([80, 60, 80, 60, 180]);
    setTimeout(() => SFX.laughter(), 700);
    SFX.murmurSet(1);
    this.hud.failTint.style.opacity = 1;
    this.comic.spawn('VIRAL!!', this.player.x, 3.6, this.player.y, '#ff4757', true);
    this.finalStats = { failed: true, reason: 'shame', total: 0, stars: 0, time: this.timer };
    setTimeout(() => UI.showResults(this.finalStats, true), 3400);
  }
  failDamage() {
    if (this.level && this.level.free) { this.player.damage = 96; return; } // free roam never fails
    if (this.state === 'fail') return;
    this.state = 'fail';
    this.endT = 0;
    document.body.classList.add('cine');
    SFX.stopAllLoops();
    SFX.crash(1);
    SFX.failTrombone();
    this.hud.failTint.style.opacity = 1;
    for (let i = 0; i < 8; i++) this.particles.puff(this.player.x + rand(-1, 1), 0.8, this.player.y + rand(-1, 1), 1.4);
    this.comic.spawn('TOTALED!', this.player.x, 3.4, this.player.y, '#ff4757', true);
    buzz([120, 80, 200]);
    this.finalStats = { failed: true, reason: 'damage', total: 0, stars: 0, time: this.timer };
    setTimeout(() => UI.showResults(this.finalStats, true), 3000);
  }
  convergePeds(dt) {
    // crowd gathers around the player to point and film
    for (const ped of this.peds.list) {
      const dd = Math.sqrt(dist2(ped.x, ped.y, this.player.x, this.player.y));
      if (dd > 6 && dd < 60) {
        const ang = Math.atan2(this.player.y - ped.y, this.player.x - ped.x);
        ped.px = ped.x; ped.py = ped.y;
        ped.x += Math.cos(ang) * 2.4 * dt;
        ped.y += Math.sin(ang) * 2.4 * dt;
        ped.face = ang;
        ped.refs.phone.visible = true;
        ped.refs.emote.material.map = Assets.emojiTexture(pick(['🎥', '😂', '📱']));
        ped.refs.emote.visible = true;
      }
    }
  }

  beginReplay() {
    if (!this.replayBuf.length || !this.finalStats) return;
    this.state = 'replay';
    this.replayT = 0;
    // move player to first frame
    const f = this.replayBuf[0];
    this.player.x = this.player.px = f[0];
    this.player.y = this.player.py = f[1];
    this.player.h = this.player.ph = f[2];
    this.hud.failTint.style.opacity = 0;
  }
  endReplayState() {
    this.state = this.finalStats && this.finalStats.failed ? 'fail' : 'success';
    // restore final pose
    if (this.replayBuf.length) {
      const f = this.replayBuf[this.replayBuf.length - 1];
      this.player.x = this.player.px = f[0];
      this.player.y = this.player.py = f[1];
      this.player.h = this.player.ph = f[2];
    }
  }

  // ---------- camera & render ----------
  updateCamera(dt, alpha) {
    const cam = this.camera;
    const p = this.player;
    if (!p) return;
    const x = lerp(p.px, p.x, alpha), y = lerp(p.py, p.y, alpha);
    const h = p.ph + angNorm(p.h - p.ph) * alpha;
    const fwd = { x: Math.cos(h), y: Math.sin(h) };
    let tx, ty, tz, lx, ly, lz, fov = 60;
    const st = this.state;

    if (st === 'menu') {
      // slow cinematic dolly along the street
      const t = this.menuCamT * 0.06;
      const s0 = 30 + Math.sin(t) * 18;
      const a = routePos(this.world.route, s0 + 26, -this.world.RW - 1.7);
      tx = a.x; ty = 5.2 + Math.sin(t * 1.7) * 0.8; tz = a.y;
      lx = x; ly = 1; lz = y;
      fov = 52;
    } else if (st === 'garage') {
      this.garageAngle += dt * 0.32;
      const r = Math.max(6.5, p.L * 1.15);
      tx = x + Math.cos(this.garageAngle) * r;
      ty = 2.6 + Math.sin(this.menuCamT * 0.7) * 0.4;
      tz = y + Math.sin(this.garageAngle) * r;
      lx = x; ly = 0.9; lz = y;
      fov = 46;
    } else if (st === 'success' || st === 'fail') {
      const a = this.endT * 0.5 + 1;
      const r = Math.max(10, p.L * 1.8);
      tx = x + Math.cos(a) * r; ty = 5.5 + this.endT * 0.8; tz = y + Math.sin(a) * r;
      lx = x; ly = 1; lz = y;
      fov = 50;
    } else if (st === 'replay') {
      // trackside cinematic: camera at fixed offset near spot, tracking car
      const spot = this.world.spot;
      const phase = Math.floor(this.replayT / 4.5) % 2;
      if (phase === 0) {
        tx = spot.x + 12; ty = 2.2; tz = spot.y + 9;
      } else {
        tx = spot.x - 10; ty = 6.5; tz = spot.y - 7;
      }
      lx = x; ly = 0.8; lz = y;
      fov = 44;
    } else {
      // gameplay cameras
      const mode = this.camMode;
      const spd01 = clamp(this.playerSpeedAbs / p.def.maxSpeed, 0, 1);
      if (mode === 2) { // parking assist: raised chase (Dr. Driving style), still behind the car
        tx = x - fwd.x * 6.5; ty = 8.5 + p.L * 0.6; tz = y - fwd.y * 6.5;
        lx = x + fwd.x * 3.0; ly = 0.2; lz = y + fwd.y * 3.0;
        fov = 52;
      } else {
        const back = (mode === 1 ? 1.55 : 1) * Math.max(7.5, p.L * 1.55);
        const up = (mode === 1 ? 1.5 : 1) * (3.1 + p.L * 0.22);
        // blend heading with the actual travel direction at speed so the
        // camera arcs through corners instead of whipping with the nose
        let dx = fwd.x, dy = fwd.y;
        const vF = p.vx * fwd.x + p.vy * fwd.y;
        if (vF > 2) {
          const sp = Math.hypot(p.vx, p.vy) || 1;
          const w = clamp((vF - 2) / 10, 0, 0.4);
          dx = fwd.x * (1 - w) + (p.vx / sp) * w;
          dy = fwd.y * (1 - w) + (p.vy / sp) * w;
          const n = Math.hypot(dx, dy) || 1; dx /= n; dy /= n;
        }
        tx = x - dx * back; ty = up; tz = y - dy * back;
        lx = x + dx * 7; ly = 1.1; lz = y + dy * 7;
        fov = 58 + spd01 * 13;
      }
    }
    // damping — during a mode transition, drop the stiffness so the camera
    // glides between views (Dr. Driving style) instead of snapping
    let lam = (st === 'drive' || st === 'park' || st === 'settle') ? 4.6 : 2.2;
    const dtc = Math.min(dt, 0.05);
    if (this.camTrans > 0) {
      const tFrac = clamp(this.camTrans / 2.4, 0, 1);
      // ease: open slowly, recover stiffness gently toward the end
      lam *= lerp(1, 0.2, tFrac * (2 - tFrac));
      this.camTrans -= dtc;
    }
    this.camPos.x = damp(this.camPos.x, tx, lam, dtc);
    this.camPos.y = damp(this.camPos.y, ty, lam, dtc);
    this.camPos.z = damp(this.camPos.z, tz, lam, dtc);
    this.camLook.x = damp(this.camLook.x, lx, lam * 1.25, dtc);
    this.camLook.y = damp(this.camLook.y, ly, lam * 1.25, dtc);
    this.camLook.z = damp(this.camLook.z, lz, lam * 1.25, dtc);
    this.camFov = damp(this.camFov, fov, 4, dtc);
    // shake
    let sx = 0, sy = 0, sz = 0;
    if (this.shakeAmt > 0.01 && Save.data.settings.shake && !Save.data.settings.reducedMotion) {
      sx = rand(-1, 1) * this.shakeAmt * 0.35;
      sy = rand(-1, 1) * this.shakeAmt * 0.25;
      sz = rand(-1, 1) * this.shakeAmt * 0.35;
    }
    this.shakeAmt *= Math.exp(-5 * dtc);
    cam.position.set(this.camPos.x + sx, this.camPos.y + sy, this.camPos.z + sz);
    cam.lookAt(this.camLook.x, this.camLook.y, this.camLook.z);
    if (Math.abs(cam.fov - this.camFov) > 0.1) { cam.fov = this.camFov; cam.updateProjectionMatrix(); }
    // shadow box follows the camera's look target (not the player), so
    // menu/garage dolly views get sun shadows too — big first-impression win
    if (this.world) {
      const fx = this.camLook.x, fy = this.camLook.z;
      this.world.sun.position.set(fx + this.world.dist.sun[2][0], this.world.dist.sun[2][1], fy + this.world.dist.sun[2][2]);
      this.world.sun.target.position.set(fx, 0, fy);
    }
  }

  render(alpha, dt) {
    if (!this.world || !this.player) return;
    this.player.render(alpha, this.world.dist.night, dt);
    this.traffic.render(alpha);
    this.peds.render(alpha);
    this.comic.update(dt);
    this.particles.update(dt);
    this.rain.update(dt, this.camPos.x, this.camPos.z);
    if (this.world.updateAmbient) this.world.updateAmbient(dt || 1 / 60);
    this.updateCamera(dt, alpha);
    if (this.world.updateLOD) this.world.updateLOD(this.camera.position.x, this.camera.position.z);
    // sky dome tracks the camera so its surface stays a constant 900m away
    // (its gradient is horizontally uniform, so the follow is invisible)
    if (this.world.sky) this.world.sky.position.set(this.camera.position.x, 0, this.camera.position.z);
    // spot pulse + chevrons
    const t = performance.now() / 1000;
    if (this.world.spotMark) {
      this.world.spotMark.material.opacity = 0.75 + Math.sin(t * 3.5) * 0.25;
      this.world.beacon.material.opacity = this.inZone ? 0.05 : 0.13 + Math.sin(t * 2) * 0.04;
      this.world.flag.position.y = 7 + Math.sin(t * 1.8) * 0.5;
      if (this.world.ghost) this.world.ghost.material.opacity = this.inZone ? 0.2 + Math.sin(t * 4) * 0.1 : 0.12;
    }
    if ((this.state === 'drive' || this.state === 'park') && this.world.chevrons) {
      const proj = this.playerProj;
      const route = this.world.route;
      for (let i = 0; i < this.world.chevrons.length; i++) {
        const ch = this.world.chevrons[i];
        const s = proj.s + 11 + i * 8;
        if (s > route.length - 22) { ch.visible = false; continue; }
        ch.visible = this.state === 'drive';
        const lanT = clamp(proj.t, LANE_W * 0.45, this.world.RW - PARK_STRIP - LANE_W * 0.45);
        const pp = routePos(route, s, lanT);
        ch.position.set(pp.x, 0.06 + Math.sin(t * 4 - i) * 0.03, pp.y);
        ch.rotation.z = pp.h; // ShapeGeometry rotated flat: rotation.x=-90 then z rotates in plane
        ch.material.opacity = 0.55 + Math.sin(t * 4 - i * 0.9) * 0.3;
      }
    } else if (this.world.chevrons) {
      for (const ch of this.world.chevrons) ch.visible = false;
    }
    this.renderScene();
    this.renderHUD(dt);
  }

  // ---------- HUD ----------
  renderHUD(dt) {
    const H = this.hud;
    const st = this.state;
    const active = st === 'drive' || st === 'park' || st === 'settle' || st === 'countdown';
    if (!active) return;
    // timer
    if (this.level.free) {
      H.timer.textContent = '🪙 ' + (this.coinsRun || 0);
      H.timer.classList.remove('overpar');
    } else {
      H.timer.textContent = fmtTime(this.timer);
      H.timer.classList.toggle('overpar', this.timer > this.level.par);
    }
    const distLeft = Math.max(0, this.world.route.length - 24 - this.playerProj.s);
    H.distLeft.textContent = this.inZone ? '🅿️ PARK NOW' : '🏁 ' + fmtDist(distLeft);
    // shame
    H.shameFill.style.height = this.shame + '%';
    H.shamePct.textContent = Math.round(this.shame) + '%';
    H.shameFace.textContent = this.shame < 25 ? '🙂' : this.shame < 50 ? '😬' : this.shame < 75 ? '😰' : '🤡';
    H.shameWrap.classList.toggle('pulse', this.shame >= 75);
    // damage
    const dm = this.player.damage;
    H.dmgFill.style.width = dm + '%';
    H.dmgFill.style.background = dm < 35 ? 'var(--green)' : dm < 70 ? 'var(--gold)' : 'var(--red)';
    // style
    H.styleRow.textContent = `✨ STYLE ${Math.round(this.style)}${this.styleCombo > 1 ? '  🔥x' + Math.min(4, this.styleCombo) : ''}`;
    // gps banner
    let gps = '';
    if (st === 'drive' && !this.inZone) {
      const route = this.world.route;
      const proj = this.playerProj;
      if (distLeft < 90) gps = '🏁 DESTINATION AHEAD';
      else {
        for (const cv of route.curves) {
          const ds = cv.s - proj.s;
          if (ds > 8 && ds < 60) { gps = cv.dir === 'L' ? '⬅ LEFT TURN AHEAD' : 'RIGHT TURN AHEAD ➡'; break; }
        }
        if (!gps) for (const it of route.inters) {
          const ds = it.s0 - proj.s;
          if (ds > 5 && ds < 55) { gps = it.ctrl ? '🚦 INTERSECTION' : '🛑 STOP AHEAD'; break; }
        }
      }
    }
    if (gps !== this._gpsText) {
      this._gpsText = gps;
      H.gps.textContent = gps;
      H.gps.classList.toggle('show', !!gps);
    }
    // warn banner
    if (this._warnText !== this._warnShown) {
      this._warnShown = this._warnText;
      H.warn.textContent = this._warnText;
      H.warn.classList.toggle('show', !!this._warnText);
    }
    // speedometer
    this.drawSpeedo();
    // align widget
    if (this.inZone && this.parkMeasure) this.drawAlign();
  }
  drawSpeedo() {
    const c = this.spCtx, W = 180, H = 180;
    const kmh = this.playerSpeedAbs * 3.6;
    const max = this.player.def.maxSpeed * 3.6 * 1.05;
    c.clearRect(0, 0, W, H);
    const cx = W / 2, cy = H / 2, R = 78;
    // glass dial
    const bg = c.createRadialGradient(cx, cy - 20, 8, cx, cy, R);
    bg.addColorStop(0, 'rgba(38,46,66,.92)');
    bg.addColorStop(0.75, 'rgba(16,20,32,.92)');
    bg.addColorStop(1, 'rgba(10,13,22,.95)');
    c.beginPath(); c.arc(cx, cy, R, 0, TAU);
    c.fillStyle = bg;
    c.fill();
    c.lineWidth = 2.5; c.strokeStyle = 'rgba(255,255,255,.3)'; c.stroke();
    c.beginPath(); c.arc(cx, cy, R - 4, 0, TAU);
    c.lineWidth = 1; c.strokeStyle = 'rgba(255,255,255,.08)'; c.stroke();
    const a0 = Math.PI * 0.75, a1 = Math.PI * 2.25;
    const frac = clamp(kmh / max, 0, 1);
    const na = a0 + (a1 - a0) * frac;
    // track arc + colored progress arc with glow
    c.beginPath(); c.arc(cx, cy, R - 11, a0, a1);
    c.lineWidth = 7; c.lineCap = 'round';
    c.strokeStyle = 'rgba(255,255,255,.1)';
    c.stroke();
    if (frac > 0.01) {
      const arcCol = frac < 0.55 ? '#3ecf6e' : (frac < 0.8 ? '#ffc23e' : '#ff4757');
      c.beginPath(); c.arc(cx, cy, R - 11, a0, na);
      c.strokeStyle = arcCol;
      c.shadowColor = arcCol; c.shadowBlur = 10;
      c.stroke();
      c.shadowBlur = 0;
    }
    // ticks
    for (let i = 0; i <= 8; i++) {
      const a = a0 + (a1 - a0) * i / 8;
      c.beginPath();
      c.moveTo(cx + Math.cos(a) * (R - 20), cy + Math.sin(a) * (R - 20));
      c.lineTo(cx + Math.cos(a) * (R - 27), cy + Math.sin(a) * (R - 27));
      c.lineWidth = i % 2 ? 1.5 : 3;
      c.strokeStyle = i >= 6 ? 'rgba(255,71,87,.9)' : 'rgba(255,255,255,.45)';
      c.stroke();
    }
    // needle
    c.beginPath();
    c.moveTo(cx - Math.cos(na) * 12, cy - Math.sin(na) * 12);
    c.lineTo(cx + Math.cos(na) * (R - 26), cy + Math.sin(na) * (R - 26));
    c.lineWidth = 4; c.lineCap = 'round';
    c.strokeStyle = '#ff6b57';
    c.shadowColor = '#ff6b57'; c.shadowBlur = 8;
    c.stroke();
    c.shadowBlur = 0;
    c.beginPath(); c.arc(cx, cy, 7, 0, TAU);
    c.fillStyle = '#e8ecf6'; c.fill();
    c.beginPath(); c.arc(cx, cy, 3.5, 0, TAU);
    c.fillStyle = '#1a1e2c'; c.fill();
    // digital
    c.fillStyle = '#fff';
    c.font = '900 30px "Baloo 2", sans-serif';
    c.textAlign = 'center';
    c.shadowColor = 'rgba(120,180,255,.5)'; c.shadowBlur = 10;
    c.fillText(Math.round(kmh), cx, cy + 42);
    c.shadowBlur = 0;
    c.font = '800 13px "Baloo 2", sans-serif';
    c.fillStyle = 'rgba(255,255,255,.55)';
    c.fillText('km/h', cx, cy + 58);
    // gear
    c.font = '900 16px "Baloo 2", sans-serif';
    c.fillStyle = this.player.reversing ? '#ff6b78' : '#4fdc82';
    c.fillText(this.player.reversing ? 'R' : 'D', cx + 46, cy + 44);
  }
  drawAlign() {
    const q = this.parkMeasure;
    const H = this.hud;
    const angDeg = Math.abs(deg(q.dAng));
    const aEl = H.awAngle;
    aEl.querySelector('.v').textContent = angDeg.toFixed(1) + '°';
    aEl.classList.toggle('ok', angDeg <= 8);
    aEl.classList.toggle('bad', angDeg > 8);
    const gEl = H.awGap;
    if (q.curbGap === null) {
      gEl.querySelector('.v').textContent = q.inside ? 'IN' : 'OUT';
      gEl.classList.toggle('ok', q.inside);
      gEl.classList.toggle('bad', !q.inside);
    } else {
      const cm = Math.round(q.curbGap * 100);
      gEl.querySelector('.v').textContent = (cm < 0 ? 'ON CURB' : cm + 'cm');
      gEl.classList.toggle('ok', cm >= 0 && cm <= 40);
      gEl.classList.toggle('bad', cm < 0 || cm > 40);
    }
    // mini top-down diagram in spot frame
    const c = this.awCtx, W = 128, Hh = 88;
    c.clearRect(0, 0, W, Hh);
    const spot = this.world.spot;
    const scale = 46 / spot.hl; // fit
    const toLocal = (wx, wy) => {
      const dx = wx - spot.x, dy = wy - spot.y;
      const ca = Math.cos(-spot.h), sa = Math.sin(-spot.h);
      return { x: W / 2 + (dx * ca - dy * sa) * scale, y: Hh / 2 + (dx * sa + dy * ca) * scale };
    };
    // spot rect
    c.strokeStyle = q.inside ? '#3ecf6e' : 'rgba(255,255,255,.5)';
    c.lineWidth = 2.5;
    c.setLineDash([5, 4]);
    c.strokeRect(W / 2 - spot.hl * scale, Hh / 2 - spot.hw * scale, spot.hl * 2 * scale, spot.hw * 2 * scale);
    c.setLineDash([]);
    // car
    const corners = this.player.corners();
    c.beginPath();
    corners.forEach((cr, i) => {
      const l = toLocal(cr.x, cr.y);
      i ? c.lineTo(l.x, l.y) : c.moveTo(l.x, l.y);
    });
    c.closePath();
    c.fillStyle = q.inside && q.angOk && q.curbOk ? 'rgba(62,207,110,.75)' : 'rgba(255,107,87,.75)';
    c.fill();
    c.strokeStyle = 'rgba(255,255,255,.85)';
    c.lineWidth = 2;
    c.stroke();
  }

  // photo postcard: render + compose + download
  postcard() {
    try {
      this.renderScene();
      const shot = this.renderer.domElement.toDataURL('image/png');
      const img = new Image();
      img.onload = () => {
        const cv = document.createElement('canvas');
        cv.width = 1000; cv.height = 700;
        const c = cv.getContext('2d');
        c.fillStyle = '#fffdf6';
        c.fillRect(0, 0, 1000, 700);
        const iw = 920, ih = 540;
        c.save();
        c.translate(500, 320);
        c.rotate(-0.012);
        c.drawImage(img, -iw / 2, -ih / 2, iw, ih);
        c.strokeStyle = '#2b2d36'; c.lineWidth = 6;
        c.strokeRect(-iw / 2, -ih / 2, iw, ih);
        c.restore();
        c.fillStyle = '#2b2d36';
        c.font = '900 44px "Baloo 2", sans-serif';
        c.textAlign = 'center';
        const fs = this.finalStats;
        c.fillText(fs && fs.failed ? 'wish you were here (I am in trouble)' : 'GREETINGS FROM ' + DISTRICTS[this.level.district].name + '!', 500, 645);
        c.font = '800 22px "Baloo 2", sans-serif';
        c.fillStyle = '#454857';
        c.fillText(this.level.name + ' · ' + (fs ? fs.total + ' pts · ' + '⭐'.repeat(fs.stars || 0) : ''), 500, 678);
        const a = document.createElement('a');
        a.download = 'parking-nightmare-3d-postcard.png';
        a.href = cv.toDataURL('image/png');
        a.click();
        SFX.cameraClick();
      };
      img.src = shot;
    } catch (e) { /* canvas blocked — ignore */ }
  }
}
