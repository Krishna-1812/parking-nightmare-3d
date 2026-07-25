/* ============================================================
   PART F — UI manager, screens, boot
   ============================================================ */

const ACHS = {
  butter: { n: 'Butter Smooth', d: 'Land a perfect Surgeon Park', i: '🧈' },
  menace: { n: 'Menace', d: 'Hit 100% shame in under 20 seconds', i: '😈' },
  pigeon: { n: 'Pigeon Apocalypse', d: 'Scatter 10 pigeons in one level', i: '🐦' },
  officer: { n: 'Sorry Officer', d: 'Bump a police car', i: '🚔' },
  speedrun: { n: 'Speedrun', d: 'Park in under 10 seconds', i: '⏱️' },
  pacifist: { n: 'Pacifist Tank', d: 'Finish the parade crushing nothing', i: '☮️' },
};
const VEH_EMOJI = { hatch: '🚗', wagon: '🚙', limo: '🚗', icecream: '🍦', bus: '🚌', tank: '🛡️', ufo: '🛸' };

class UIManager {
  constructor() {
    this.screen = null;
    this.stack = [];
    this.inRun = false;
    this.paused = false;
    this.cdActive = false;
    this.cdTimer = null;
    this.currentDef = null;
    this.garageIdx = 0;
    this.gVeh = null;
    this.titleMenuShown = false;
    this.titleHonks = 0;
    this.settingsReturn = null;
    this.bannerTimer = null;
    this.comboTimer = null;
    this.resultTimers = [];
    this.bind();
  }
  // ---------------- screen management ----------------
  el(id) { return $('screen-' + id); }
  show(id, noStack) {
    if (this.screen && this.screen !== id && !noStack) this.stack.push(this.screen);
    for (const s of document.querySelectorAll('.screen')) s.classList.remove('active');
    this.screen = id;
    const el = this.el(id);
    if (el) el.classList.add('active');
    if (id === 'levels') this.buildLevels();
    if (id === 'garage') this.updateGarage();
    if (id === 'stats') this.buildStats();
    if (id === 'daily') this.buildDaily();
    if (id === 'settings') this.syncSettingsUI();
    this.collectFocus();
  }
  back() {
    SFX.uiBack();
    if (this.settingsReturn && this.screen === 'settings') {
      const r = this.settingsReturn; this.settingsReturn = null;
      if (r === 'pause') { this.show('pause', true); return; }
    }
    const prev = this.stack.pop() || 'title';
    this.show(prev, true);
  }
  collectFocus() {
    const el = this.el(this.screen);
    this.focusables = el ? [...el.querySelectorAll('button:not(:disabled), .toggle, input[type=range]')].filter(b => b.offsetParent !== null) : [];
    this.focusIdx = -1;
  }
  moveFocus(dir) {
    if (!this.focusables || !this.focusables.length) return;
    this.focusIdx = (this.focusIdx + dir + this.focusables.length) % this.focusables.length;
    this.focusables[this.focusIdx].focus();
    SFX.uiMove();
  }
  onAnyKey(e) {
    // replay skip
    if (game && game.state === 'replay') { game.skipReplay(); return; }
    // title press-any
    if (this.screen === 'title' && !this.titleMenuShown && e.code !== 'KeyH') { this.showTitleMenu(); return; }
    // menu keyboard nav (not while driving)
    if (!this.inRun || this.paused || (game && game.state === 'post')) {
      const tag = document.activeElement ? document.activeElement.tagName : '';
      if (e.code === 'ArrowDown') { e.preventDefault(); this.moveFocus(1); }
      else if (e.code === 'ArrowUp') { e.preventDefault(); this.moveFocus(-1); }
      else if (e.code === 'ArrowRight' && tag !== 'INPUT') this.moveFocus(1);
      else if (e.code === 'ArrowLeft' && tag !== 'INPUT') this.moveFocus(-1);
      else if ((e.code === 'Enter' || e.code === 'Space') && document.activeElement && document.activeElement.classList.contains('toggle')) {
        e.preventDefault(); document.activeElement.click();
      }
    }
  }
  // ---------------- title ----------------
  showTitleMenu() {
    this.titleMenuShown = true;
    $('press-any').style.display = 'none';
    $('main-menu').classList.add('show');
    this.collectFocus();
    SFX.uiClick();
  }
  titleHonk() {
    SFX.horn('hatch');
    this.titleHonks++;
    if (this.titleHonks >= 5) {
      this.titleHonks = 0;
      const cat = $('cat');
      cat.classList.remove('hidden', 'walk');
      void cat.offsetWidth;
      cat.classList.add('walk');
      SFX.tone({ type: 'sine', f0: 700, f1: 900, dur: 0.3, vol: 0.15 });
      setTimeout(() => cat.classList.remove('walk'), 7200);
    }
  }
  // ---------------- level select ----------------
  buildLevels() {
    const wrap = $('districts');
    wrap.innerHTML = '';
    $('total-stars').textContent = `★ ${Save.totalStars()}/36`;
    for (let d = 0; d < 4; d++) {
      const div = document.createElement('div');
      div.className = `district d${d}`;
      div.innerHTML = `<h3><span class="dtag">${d + 1}</span>${DISTRICTS[d].name}</h3><div class="nodes"></div>`;
      const nodes = div.querySelector('.nodes');
      for (const lv of LEVELS.filter(l => l.district === d)) {
        const locked = lv.id > Save.data.unlockedLevel;
        const stars = Save.data.stars[lv.id] || 0;
        const btn = document.createElement('button');
        btn.className = 'node' + (locked ? ' locked' : '');
        btn.innerHTML = locked
          ? `<span class="padlock">🔒</span><div class="n-num">LEVEL ${lv.id}</div><div class="n-name">???</div><div class="n-stars">&nbsp;</div>`
          : `<div class="n-num">LEVEL ${lv.id}</div><div class="n-name">${lv.name}</div><div class="n-stars">${'★'.repeat(stars)}<span class="off">${'★'.repeat(3 - stars)}</span></div>`;
        btn.addEventListener('click', () => {
          if (locked) {
            btn.classList.remove('wiggle'); void btn.offsetWidth; btn.classList.add('wiggle');
            SFX.thud();
          } else {
            SFX.uiClick();
            this.preLevel(lv);
          }
        });
        nodes.appendChild(btn);
      }
      wrap.appendChild(div);
    }
    this.collectFocus();
  }
  // ---------------- garage ----------------
  updateGarage() {
    const key = VEH_ORDER[this.garageIdx];
    const d = VEH_DEFS[key];
    const unlocked = vehicleUnlocked(key);
    $('g-name').textContent = unlocked ? d.name : '???';
    $('g-flavor').textContent = unlocked ? d.flavor : '"Locked away. For everyone\'s safety."';
    $('gs-size').style.width = d.stats.size + '%';
    $('gs-speed').style.width = d.stats.speed + '%';
    $('gs-hand').style.width = d.stats.hand + '%';
    $('gs-chaos').style.width = d.stats.chaos + '%';
    $('g-lock').textContent = unlocked ? '' : vehicleUnlockText(key);
    $('g-select').disabled = !unlocked;
    $('garage-card').classList.toggle('locked-card', !unlocked);
    $('g-dots').innerHTML = VEH_ORDER.map((k, i) => `<span class="${i === this.garageIdx ? 'on' : ''}">●</span>`).join('');
    this.gVeh = new Vehicle(key, 0, 0, 0);
    this.gSpin = 0;
  }
  renderGaragePreview(dt) {
    if (this.screen !== 'garage' || !this.gVeh) return;
    this.gSpin += dt * 0.8;
    const cv = $('g-preview'), c = cv.getContext('2d');
    c.clearRect(0, 0, cv.width, cv.height);
    c.save();
    c.translate(cv.width / 2, cv.height / 2 + 8);
    // turntable
    c.fillStyle = 'rgba(43,45,54,.08)';
    c.beginPath(); c.ellipse(0, 10, 150, 40, 0, 0, TAU); c.fill();
    const scl = clamp(150 / this.gVeh.L, 0.7, 1.9);
    c.scale(scl, scl);
    this.gVeh.hoverPhase += dt * 3;
    this.gVeh.draw(c, { x: 0, y: 0, h: this.gSpin }, {});
    c.restore();
  }
  garageNav(dir) {
    this.garageIdx = (this.garageIdx + dir + VEH_ORDER.length) % VEH_ORDER.length;
    SFX.uiMove();
    this.updateGarage();
  }
  // ---------------- pre-level ----------------
  preLevel(def) {
    this.currentDef = def;
    $('pl-district').textContent = DISTRICTS[def.district].name + (def.id === 'daily' ? ' · DAILY' : def.id === 'free' ? ' · FREE PARK' : ` · LEVEL ${def.id}`);
    $('pl-name').textContent = def.name;
    $('pl-brief').textContent = def.brief;
    $('pl-veh').textContent = VEH_EMOJI[def.veh] + ' ' + VEH_DEFS[def.veh].name;
    $('pl-par').textContent = fmtTime(def.par);
    const best = typeof def.id === 'number' ? Save.data.bestScores[def.id] : (def.id === 'daily' && Save.data.daily[todayKey()] ? (Save.data.daily[todayKey()].board[0] || {}).score : null);
    $('pl-best').textContent = best ? best + ' pts' : '—';
    this.show('prelevel');
  }
  // ---------------- run lifecycle ----------------
  startRun(def) {
    this.currentDef = def;
    this.inRun = true;
    this.paused = false;
    this.stack = [];
    for (const s of document.querySelectorAll('.screen')) s.classList.remove('active');
    this.screen = 'hud';
    $('hud').classList.add('active');
    $('fail-tint').style.opacity = '0';
    $('shame-fill').dataset.v = '0';
    const touchy = Input.usingTouch || Save.data.settings.forceTouch;
    Input.showTouch(touchy);
    if (Input.usingTouch) {
      // best-effort immersive mode on real touch devices (inside the GO tap gesture)
      try {
        const de = document.documentElement;
        if (de.requestFullscreen && !document.fullscreenElement) {
          const p = de.requestFullscreen({ navigationUI: 'hide' });
          if (p && p.catch) p.catch(() => {});
        }
        if (screen.orientation && screen.orientation.lock) {
          const p2 = screen.orientation.lock('landscape');
          if (p2 && p2.catch) p2.catch(() => {});
        }
      } catch (e) { /* not supported (e.g. iOS Safari) — fine */ }
    }
    game.startLevel(def, {});
    this.updateRotateHint();
    this.countdown(() => { if (game.state === 'countdown') game.state = 'play'; });
  }
  updateRotateHint() {
    const el = $('rotate-hint');
    const wantHint = this.inRun && (Input.usingTouch || Save.data.settings.forceTouch) && innerHeight > innerWidth * 1.15;
    if (wantHint) {
      el.classList.remove('hidden');
      el.style.opacity = '1';
      clearTimeout(this._rotT);
      this._rotT = setTimeout(() => { el.style.opacity = '0'; }, 4000);
    } else {
      el.classList.add('hidden');
    }
  }
  countdown(cb) {
    this.cdActive = true;
    const cd = $('countdown'), num = $('cd-num');
    cd.classList.remove('hidden');
    let n = 3;
    const step = () => {
      if (!this.inRun) { cd.classList.add('hidden'); this.cdActive = false; return; }
      if (n > 0) {
        num.textContent = n;
        num.classList.remove('pop'); void num.offsetWidth; num.classList.add('pop');
        SFX.countBeep(false);
        n--;
        this.cdTimer = setTimeout(step, 750);
      } else {
        num.textContent = 'GO!';
        num.classList.remove('pop'); void num.offsetWidth; num.classList.add('pop');
        SFX.countBeep(true);
        SFX.noise({ filter: 'bandpass', f0: 1900, dur: 0.5, vol: 0.16, q: 4 });
        this.cdActive = false;
        cb();
        this.cdTimer = setTimeout(() => cd.classList.add('hidden'), 800);
      }
    };
    step();
  }
  retryLevel() {
    if (!this.currentDef) return;
    clearTimeout(this.cdTimer);
    SFX.uiClick();
    this.hideResultsFx();
    this.startRun(this.currentDef);
  }
  quitToMap() {
    clearTimeout(this.cdTimer);
    this.inRun = false;
    this.paused = false;
    $('rotate-hint').classList.add('hidden');
    SFX.stopAllLoops();
    SFX.musicStop();
    $('fail-tint').style.opacity = '0';
    Input.showTouch(false);
    this.hideResultsFx();
    this.showReplay(false);
    UI.tutTip(null);
    const def = this.currentDef;
    this.stack = ['title'];
    this.ensureDemo();
    if (def && def.id === 'daily') this.show('daily', true);
    else if (def && def.id === 'free') this.show('garage', true);
    else this.show('levels', true);
  }
  ensureDemo() {
    if (this.inRun) return;
    const demoDef = Object.assign({}, LEVELS[0], { tutorial: false, peds: 5, pigeons: 2 });
    game.startLevel(demoDef, { demo: true });
  }
  // ---------------- pause ----------------
  togglePause() {
    if (!this.inRun) {
      if (this.screen !== 'title') this.back();
      return;
    }
    if (game.state !== 'play' && game.state !== 'countdown' && !this.paused) return;
    if (this.paused) this.resumeGame();
    else this.pauseGame();
  }
  pauseGame() {
    if (this.paused) return;
    this.paused = true;
    SFX.engineStop(); SFX.screechStop(); SFX.jingleStop();
    this.show('pause', true);
  }
  resumeGame() {
    this.paused = false;
    for (const s of document.querySelectorAll('.screen')) s.classList.remove('active');
    this.screen = 'hud';
    $('hud').classList.add('active');
    SFX.engineStart(game.veh.key);
    if (game.veh.key === 'icecream') SFX.jingleStart();
    SFX.uiClick();
  }
  // ---------------- replay UI ----------------
  showReplay(show) {
    const r = $('replay-ui');
    if (show) { r.classList.remove('hidden'); void r.offsetWidth; r.classList.add('show'); }
    else { r.classList.remove('show'); setTimeout(() => r.classList.add('hidden'), 400); }
  }
  // ---------------- results ----------------
  hideResultsFx() {
    for (const t of this.resultTimers) clearTimeout(t);
    this.resultTimers = [];
  }
  showResults(g) {
    this.hideResultsFx();
    const r = g.result;
    const fail = !!r.fail;
    const T = (fn, ms) => this.resultTimers.push(setTimeout(fn, ms));
    $('res-title').textContent = fail ? 'TOTAL HUMILIATION' : pick(['PARKED!', 'NAILED IT!', 'LEGALLY PARKED!', 'IT FITS!']);
    $('res-title').style.color = fail ? 'var(--red)' : 'var(--ink)';
    $('res-sub').textContent = `${g.level.name} · ${fmtTime(g.t)}${fail ? '' : ' · shame ' + Math.round(g.shame) + '%'}`;
    // stars
    const starEls = [...$('res-stars').children];
    starEls.forEach((s, i) => {
      s.classList.remove('slam');
      s.classList.toggle('off', i >= r.stars);
    });
    // social post
    const post = $('social-post');
    post.classList.toggle('hidden', !fail);
    if (fail) {
      const vn = VEH_DEFS[g.level.veh].name.toLowerCase();
      const texts = [
        `Someone took ${fmtTime(g.t)} to park a ${vn} and the whole street watched 💀`,
        `BREAKING: local driver vs. one (1) ${vn}. The ${vn} is winning 💀`,
        `day ${randi(2, 9)}: they are STILL trying to park the ${vn}. bring snacks 🍿`,
        `the ${vn} has claimed another victim. shame levels: catastrophic 📉`,
      ];
      post.querySelector('.sp-text').textContent = pick(texts);
      post.querySelector('.sp-likes').textContent = `❤️ ${(rand(5, 40)).toFixed(1)}k`;
    }
    // line items
    const lines = $('res-lines');
    lines.innerHTML = '';
    lines.style.display = fail ? 'none' : '';
    $('res-total').style.display = fail ? 'none' : '';
    if (!fail) {
      for (const ln of r.lines) {
        const div = document.createElement('div');
        div.className = 'res-line';
        div.innerHTML = `<span>${ln.k}</span><span class="rv ${ln.v < 0 ? 'neg' : 'pos'}">${ln.v > 0 ? '+' : ''}${ln.v}</span>`;
        lines.appendChild(div);
      }
    }
    $('res-total-v').textContent = '0';
    // best comparison
    const prevBest = g.prevBest || 0;
    $('res-best').textContent = fail ? 'The internet never forgets.' :
      (r.total > prevBest ? (prevBest ? `NEW BEST! (was ${prevBest})` : 'First clear — new best!') : `Best: ${prevBest}`);
    // stamp
    const stamp = $('res-stamp');
    stamp.classList.remove('slam');
    stamp.textContent = fail ? 'F' : r.grade;
    stamp.style.color = stamp.style.borderColor = fail ? 'var(--red)' : (r.grade === 'S' || r.grade === 'A' ? '#2a9d4a' : r.grade === 'B' ? '#e0a52a' : 'var(--red)');
    // buttons
    $('res-photo').classList.toggle('hidden', fail);
    const isNum = typeof g.level.id === 'number';
    $('res-next').classList.toggle('hidden', fail || !isNum || g.level.id >= 12);
    $('res-retry').textContent = fail ? '😤 Redeem Yourself' : '↺ Retry';
    $('res-retry').classList.toggle('primary', fail);
    $('res-map').textContent = g.level.id === 'daily' ? 'Daily' : g.level.id === 'free' ? 'Garage' : 'Map';
    this.show('results', true);
    // animations
    starEls.forEach((s, i) => {
      if (i < r.stars) T(() => { s.classList.add('slam'); SFX.starSlam(i); game.shake(4); }, 400 + i * 350);
      else T(() => { s.classList.add('slam'); }, 400 + i * 350);
    });
    if (!fail) {
      const lineEls = [...lines.children];
      lineEls.forEach((el, i) => T(() => { el.classList.add('in'); SFX.uiMove(); }, 1400 + i * 130));
      // count up total
      T(() => {
        const start = performance.now(), dur = 900;
        const tick = () => {
          const k = clamp((performance.now() - start) / dur, 0, 1);
          $('res-total-v').textContent = Math.round(r.total * (k * k * (3 - 2 * k)));
          if (k < 1) requestAnimationFrame(tick);
        };
        tick();
      }, 1400 + r.lines.length * 130);
    }
    T(() => { stamp.classList.add('slam'); SFX.stamp(); }, fail ? 900 : 1900 + r.lines.length * 130);
  }
  // ---------------- stats ----------------
  buildStats() {
    const s = Save.data.stats;
    const fav = Object.entries(s.vehicleUse).sort((a, b) => b[1] - a[1])[0];
    const cells = [
      [s.parks, 'TOTAL PARKS'],
      [s.collisions, 'COLLISIONS'],
      [s.propsDestroyed, 'PROPS DESTROYED'],
      [s.pedsScandalized, 'PEDESTRIANS SCANDALIZED'],
      [s.pigeonsScattered, 'PIGEONS SCATTERED'],
      [s.crushes, 'THINGS CRUSHED'],
      [s.fastestPark ? fmtTime(s.fastestPark) : '—', 'FASTEST PARK'],
      [fav ? VEH_EMOJI[fav[0]] + ' ' + VEH_DEFS[fav[0]].name : '—', 'FAVORITE VEHICLE'],
    ];
    $('stats-grid').innerHTML = cells.map(cc => `<div class="stat-cell"><div class="sv">${cc[0]}</div><div class="sk">${cc[1]}</div></div>`).join('');
    $('shame-total-line').textContent = `You have generated ${Math.round(s.totalShame).toLocaleString()} units of public shame. The city remembers.`;
    $('ach-list').innerHTML = Object.entries(ACHS).map(([id, a]) => {
      const got = !!Save.data.achievements[id];
      return `<div class="ach ${got ? '' : 'locked'}"><span class="ai">${a.i}</span><div><div class="an">${a.n}</div><div class="ad">${a.d}</div></div></div>`;
    }).join('');
  }
  // ---------------- daily ----------------
  buildDaily() {
    const dk = todayKey();
    const def = dailyLevel(dk);
    this.dailyDef = def;
    $('daily-date').textContent = dk;
    $('daily-desc').innerHTML = `${VEH_EMOJI[def.veh]} <b>${VEH_DEFS[def.veh].name}</b> in <b>${DISTRICTS[def.district].name}</b><br>` +
      `Spot ${def.ratio.toFixed(2)}× vehicle length · par ${fmtTime(def.par)}` +
      (def.night ? ' · 🌙 night' : '') + (def.ice ? ' · 🧊 ice' : '') + (def.traffic ? ' · 🚗 traffic' : '');
    const rec = Save.data.daily[dk];
    const board = rec ? rec.board : [];
    $('daily-board').innerHTML = board.length
      ? board.map((b, i) => `<div class="db-row"><span class="rank">#${i + 1}</span><span>${'⭐'.repeat(b.stars) || '💀'}</span><span>${b.score} pts</span><span>${fmtTime(b.time)}</span></div>`).join('')
      : '<div class="db-row empty">No attempts yet today. Be the legend.</div>';
    $('daily-share').disabled = !(rec && rec.last);
  }
  shareDaily() {
    const dk = todayKey();
    const rec = Save.data.daily[dk];
    if (!rec || !rec.last) return;
    const l = rec.last;
    const def = this.dailyDef || dailyLevel(dk);
    const boom = '💥'.repeat(Math.min(l.coll || 0, 5));
    const grid = l.score > 0
      ? `${VEH_EMOJI[def.veh]}${boom}${'⭐'.repeat(l.stars)}`
      : `${VEH_EMOJI[def.veh]}${boom}🙈`;
    const text = `🅿️ Parking Nightmare Daily ${dk}\n${grid}\n${l.score} pts · ${fmtTime(l.time)}\ncan you park a ${VEH_DEFS[def.veh].name.toLowerCase()}?`;
    const done = () => { this.toast('📋', 'Copied!', 'Share your shame with the world'); };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done).catch(() => this.fallbackCopy(text, done));
    } else this.fallbackCopy(text, done);
  }
  fallbackCopy(text, done) {
    try {
      const ta = document.createElement('textarea');
      ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
      document.body.appendChild(ta); ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
      done();
    } catch (e) { this.toast('😵', 'Copy failed', 'Your browser said no'); }
  }
  // ---------------- settings ----------------
  syncSettingsUI() {
    const s = Save.data.settings;
    $('set-master').value = Math.round(s.master * 100);
    $('set-music').value = Math.round(s.music * 100);
    $('set-sfx').value = Math.round(s.sfx * 100);
    this.setToggle('set-shake', s.shake);
    this.setToggle('set-cb', s.colorblind);
    this.setToggle('set-rm', s.reducedMotion);
    this.setToggle('set-touch', s.forceTouch);
    this.setToggle('set-jingle', s.jingle);
  }
  setToggle(id, on) {
    const el = $(id);
    el.classList.toggle('on', on);
    el.setAttribute('aria-checked', on);
  }
  applySettingsClasses() {
    const s = Save.data.settings;
    document.body.classList.toggle('cb', s.colorblind);
    document.body.classList.toggle('rm', s.reducedMotion);
    if (this.inRun) Input.showTouch(Input.usingTouch || s.forceTouch);
  }
  // ---------------- toasts / achievements ----------------
  toast(icon, name, desc) {
    const t = document.createElement('div');
    t.className = 'toast';
    t.innerHTML = `<span class="ti">${icon}</span><div><div class="tn">${name}</div><div class="td">${desc}</div></div>`;
    $('toasts').appendChild(t);
    requestAnimationFrame(() => t.classList.add('in'));
    setTimeout(() => { t.classList.remove('in'); setTimeout(() => t.remove(), 400); }, 3400);
  }
  unlockAch(id) {
    if (Save.data.achievements[id]) return;
    Save.data.achievements[id] = true;
    Save.save();
    const a = ACHS[id];
    this.toast(a.i, 'Achievement: ' + a.n, a.d);
    SFX.achievementDing();
  }
  // ---------------- HUD helpers ----------------
  banner(text, color) {
    const b = $('threshold-banner');
    b.textContent = text;
    b.style.background = color;
    b.classList.remove('show'); void b.offsetWidth; b.classList.add('show');
    clearTimeout(this.bannerTimer);
    this.bannerTimer = setTimeout(() => b.classList.remove('show'), 1800);
  }
  combo(text) {
    const el = $('combo-pop');
    el.textContent = text;
    el.classList.remove('show'); void el.offsetWidth; el.classList.add('show');
    clearTimeout(this.comboTimer);
    this.comboTimer = setTimeout(() => el.classList.remove('show'), 1000);
  }
  tutTip(html) {
    const t = $('tut-tip');
    if (html === null || html === undefined) { t.classList.add('hidden'); return; }
    if (t.dataset.cur === html) return;
    t.dataset.cur = html;
    t.innerHTML = html;
    t.classList.remove('hidden');
  }
  confirm(text, onYes) {
    $('confirm-text').textContent = text;
    $('confirm-wrap').classList.remove('hidden');
    this._confirmCb = onYes;
  }
  // ---------------- photo ----------------
  savePostcard() {
    try {
      const pc = game.renderPostcard();
      const a = document.createElement('a');
      a.download = `parking-nightmare-${Date.now()}.png`;
      a.href = pc.toDataURL('image/png');
      a.click();
      this.toast('📸', 'Postcard saved!', 'Frame it. You earned it.');
      SFX.cameraClick();
    } catch (e) { this.toast('😵', 'Snapshot failed', 'The camera is also ashamed'); }
  }
  // ---------------- how to play ----------------
  drawHowTo() {
    // panel 1: controls
    let c = $('ht1').getContext('2d');
    c.clearRect(0, 0, 248, 150);
    c.fillStyle = '#3d4148'; c.fillRect(0, 0, 248, 150);
    c.fillStyle = '#4a4e57'; c.fillRect(0, 110, 248, 40);
    const key = (x, y, t, on) => {
      c.fillStyle = on ? '#ffc23e' : '#fffdf6';
      c.strokeStyle = '#22242a'; c.lineWidth = 2.5;
      roundRectPath(c, x, y, 30, 30, 6); c.fill(); c.stroke();
      c.fillStyle = '#2b2d36'; c.font = "800 15px 'Baloo 2',sans-serif";
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText(t, x + 15, y + 16);
    };
    key(48, 20, 'W', true); key(14, 54, 'A'); key(48, 54, 'S'); key(82, 54, 'D');
    // car with motion lines
    c.save(); c.translate(180, 60); c.rotate(-0.15);
    c.fillStyle = '#ff6b57'; c.strokeStyle = '#22242a'; c.lineWidth = 2.5;
    roundRectPath(c, -28, -13, 56, 26, 7); c.fill(); c.stroke();
    c.fillStyle = '#bde3ff'; roundRectPath(c, -4, -8, 12, 16, 3); c.fill(); c.stroke();
    c.strokeStyle = '#fff';
    c.beginPath(); c.moveTo(-40, -8); c.lineTo(-56, -8); c.moveTo(-40, 0); c.lineTo(-62, 0); c.moveTo(-40, 8); c.lineTo(-56, 8); c.stroke();
    c.restore();
    c.fillStyle = '#fff'; c.font = "700 12px 'Baloo 2',sans-serif"; c.textAlign = 'center';
    c.fillText('SPACE = handbrake · H = honk', 124, 132);
    // panel 2: parking
    c = $('ht2').getContext('2d');
    c.clearRect(0, 0, 248, 150);
    c.fillStyle = '#3d4148'; c.fillRect(0, 0, 248, 150);
    c.fillStyle = '#b8d8c7'; c.fillRect(0, 108, 248, 42);
    drawParkedCar(c, { x: 40, y: 88, h: 0, hl: 26, hw: 12, color: '#4e7ab0', kind: 'sedan' }, false);
    drawParkedCar(c, { x: 208, y: 88, h: 0, hl: 26, hw: 12, color: '#6a9a55', kind: 'sedan' }, false);
    c.strokeStyle = '#f5c542'; c.lineWidth = 3; c.setLineDash([8, 6]);
    c.strokeRect(78, 70, 92, 36);
    c.setLineDash([5, 5]); c.strokeStyle = '#fff';
    roundRectPath(c, 96, 76, 56, 24, 6); c.stroke(); c.setLineDash([]);
    c.save(); c.translate(124, 36); c.rotate(0.5);
    c.fillStyle = '#ff6b57'; c.strokeStyle = '#22242a'; c.lineWidth = 2.5;
    roundRectPath(c, -28, -12, 56, 24, 7); c.fill(); c.stroke();
    c.restore();
    c.strokeStyle = '#3ecf6e'; c.lineWidth = 3;
    c.beginPath(); c.moveTo(150, 30); c.arc(160, 55, 30, -1.2, 1.2); c.stroke();
    c.fillStyle = '#3ecf6e';
    c.beginPath(); c.moveTo(142, 90); c.lineTo(158, 82); c.lineTo(152, 98); c.closePath(); c.fill();
    // panel 3: shame
    c = $('ht3').getContext('2d');
    c.clearRect(0, 0, 248, 150);
    c.fillStyle = '#3d4148'; c.fillRect(0, 0, 248, 150);
    // thermometer
    c.fillStyle = '#fffdf6'; c.strokeStyle = '#22242a'; c.lineWidth = 3;
    roundRectPath(c, 24, 14, 24, 120, 12); c.fill(); c.stroke();
    const gr = c.createLinearGradient(0, 130, 0, 20);
    gr.addColorStop(0, '#ffc23e'); gr.addColorStop(1, '#ff4757');
    c.fillStyle = gr;
    roundRectPath(c, 28, 55, 16, 75, 8); c.fill();
    c.font = '20px sans-serif'; c.textAlign = 'center';
    c.fillText('😳', 36, 40);
    // watchers with phones
    for (let i = 0; i < 3; i++) {
      const x = 110 + i * 45, y = 100;
      c.fillStyle = SHIRT[i + 1]; c.strokeStyle = '#22242a'; c.lineWidth = 2;
      c.beginPath(); c.ellipse(x, y, 9, 8, 0, 0, TAU); c.fill(); c.stroke();
      c.fillStyle = SKIN[i]; c.beginPath(); c.arc(x, y - 10, 6, 0, TAU); c.fill(); c.stroke();
      if (i === 1) {
        c.fillStyle = '#22242a'; c.fillRect(x + 8, y - 16, 6, 10);
        c.fillStyle = '#8ef7ff'; c.fillRect(x + 9.5, y - 14, 3, 6);
        c.fillStyle = '#fff'; c.font = '10px sans-serif'; c.fillText('📸', x + 20, y - 20);
      }
    }
    c.fillStyle = '#fff'; c.font = "700 12px 'Baloo 2',sans-serif"; c.textAlign = 'center';
    c.fillText('100% shame = instant fail', 124, 138);
  }
  // ---------------- event binding ----------------
  bind() {
    const click = (id, fn) => { const el = $(id); if (el) el.addEventListener('click', () => { SFX.init(); fn(); }); };
    // title & any-click to open menu
    $('screen-title').addEventListener('click', () => {
      SFX.init();
      if (!this.titleMenuShown) this.showTitleMenu();
    });
    document.querySelectorAll('#main-menu [data-menu]').forEach(btn => {
      btn.addEventListener('click', e => {
        e.stopPropagation();
        SFX.uiClick();
        const m = btn.dataset.menu;
        if (m === 'play') this.show('levels');
        else if (m === 'daily') this.show('daily');
        else if (m === 'garage') this.show('garage');
        else if (m === 'stats') this.show('stats');
        else if (m === 'settings') this.show('settings');
        else if (m === 'howto') this.show('howto');
        else if (m === 'credits') this.show('credits');
      });
    });
    document.querySelectorAll('[data-back]').forEach(b => b.addEventListener('click', e => { e.stopPropagation(); this.back(); }));
    // garage
    click('g-prev', () => this.garageNav(-1));
    click('g-next', () => this.garageNav(1));
    click('g-select', () => {
      SFX.uiClick();
      this.preLevel(freeParkLevel(VEH_ORDER[this.garageIdx]));
    });
    // pre-level
    click('pl-go', () => { SFX.uiClick(); this.startRun(this.currentDef); });
    // HUD buttons
    click('btnRetry', () => this.retryLevel());
    click('btnPause', () => this.togglePause());
    // pause
    click('pz-resume', () => this.resumeGame());
    click('pz-retry', () => { this.paused = false; this.retryLevel(); });
    click('pz-settings', () => { this.settingsReturn = 'pause'; this.show('settings', true); });
    click('pz-quit', () => { SFX.uiClick(); this.quitToMap(); });
    // results
    click('res-retry', () => { this.retryLevel(); });
    click('res-map', () => { SFX.uiClick(); this.quitToMap(); });
    click('res-next', () => {
      SFX.uiClick();
      const cur = this.currentDef;
      const next = LEVELS.find(l => l.id === cur.id + 1);
      if (next) { this.hideResultsFx(); this.preLevel(next); this.inRun = false; SFX.stopAllLoops(); }
      else this.quitToMap();
    });
    click('res-replay', () => {
      SFX.uiClick();
      this.hideResultsFx();
      for (const s of document.querySelectorAll('.screen')) s.classList.remove('active');
      this.screen = 'hud'; $('hud').classList.add('active');
      game.beginReplay();
    });
    click('res-photo', () => this.savePostcard());
    // replay skip on click
    $('replay-ui').addEventListener('click', () => { if (game.state === 'replay') game.skipReplay(); });
    // daily
    click('daily-play', () => { SFX.uiClick(); this.preLevel(this.dailyDef || dailyLevel(todayKey())); });
    click('daily-share', () => this.shareDaily());
    // settings
    const bindRange = (id, key) => {
      $(id).addEventListener('input', e => {
        Save.data.settings[key] = e.target.value / 100;
        Save.save();
        SFX.applyVolumes();
      });
      $(id).addEventListener('change', () => SFX.uiClick());
    };
    bindRange('set-master', 'master');
    bindRange('set-music', 'music');
    bindRange('set-sfx', 'sfx');
    const bindToggle = (id, key, after) => {
      $(id).addEventListener('click', () => {
        SFX.init();
        Save.data.settings[key] = !Save.data.settings[key];
        this.setToggle(id, Save.data.settings[key]);
        Save.save();
        SFX.uiClick();
        this.applySettingsClasses();
        if (after) after(Save.data.settings[key]);
      });
    };
    bindToggle('set-shake', 'shake');
    bindToggle('set-cb', 'colorblind');
    bindToggle('set-rm', 'reducedMotion');
    bindToggle('set-touch', 'forceTouch');
    bindToggle('set-jingle', 'jingle');
    click('set-reset', () => {
      this.confirm('Wipe ALL progress, stars, unlocks and stats? This cannot be undone.', () => {
        Save.reset();
        this.applySettingsClasses();
        this.syncSettingsUI();
        SFX.applyVolumes();
        this.toast('🗑', 'Save data wiped', 'A fresh start. A clean shame slate.');
      });
    });
    // confirm dialog
    $('confirm-yes').addEventListener('click', () => {
      $('confirm-wrap').classList.add('hidden');
      SFX.uiClick();
      if (this._confirmCb) this._confirmCb();
      this._confirmCb = null;
    });
    $('confirm-no').addEventListener('click', () => {
      $('confirm-wrap').classList.add('hidden');
      SFX.uiBack();
      this._confirmCb = null;
    });
  }
}
const UI = new UIManager();

// ============================================================
// BOOT + MAIN LOOP
// ============================================================
const game = new Game($('game'));

Input.handlers.any = e => UI.onAnyKey(e);
Input.handlers.horn = () => {
  if (UI.inRun && !UI.paused && game.state === 'play') game.honk();
  else if (UI.screen === 'title') UI.titleHonk();
};
Input.handlers.retry = () => {
  if (UI.inRun && !UI.paused && (game.state === 'play' || game.state === 'countdown')) UI.retryLevel();
};
Input.handlers.camera = () => {
  if (UI.inRun && !UI.paused && game.state === 'play') game.toggleZoom();
};
Input.handlers.pause = () => UI.togglePause();

window.addEventListener('resize', () => { game.resize(); UI.updateRotateHint(); });
window.addEventListener('orientationchange', () => { game.resize(); UI.updateRotateHint(); });
if (window.visualViewport) visualViewport.addEventListener('resize', () => game.resize());
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    if (UI.inRun && !UI.paused && (game.state === 'play' || game.state === 'countdown')) UI.pauseGame();
    if (SFX.ctx && SFX.ctx.state === 'running') SFX.ctx.suspend();
  } else {
    if (SFX.ctx && SFX.ctx.state === 'suspended') SFX.ctx.resume();
  }
});

let _last = performance.now(), _acc = 0, _prevFrame = performance.now();
function frame(now) {
  requestAnimationFrame(frame);
  let dt = (now - _last) / 1000;
  _last = now;
  if (dt > 0.25) dt = 0.25;
  // self-heal canvas size (covers mobile address-bar collapse & panes that resize without an event)
  const _dp = Math.min(window.devicePixelRatio || 1, 2);
  if ((game.cv.width !== Math.floor(innerWidth * _dp) || game.cv.height !== Math.floor(innerHeight * _dp)) && innerWidth > 0 && innerHeight > 0) game.resize();
  if (UI.paused) return;
  _acc += dt * game.timeScale;
  let n = 0;
  while (_acc >= STEP && n < 24) {
    game.fixedUpdate(STEP);
    _acc -= STEP;
    n++;
  }
  if (n >= 24) _acc = 0;
  game.render(clamp(_acc / STEP, 0, 1));
  UI.renderGaragePreview(dt);
}

// apply saved settings & go
UI.applySettingsClasses();
UI.syncSettingsUI();
UI.drawHowTo();
Input.showTouch(false);
// touch-device copy tweaks
if (Input.usingTouch) {
  $('press-any').textContent = '— TAP ANYWHERE —';
  const ht1p = document.querySelector('#howto-panels .ht-card p');
  if (ht1p) ht1p.innerHTML = 'Drag the <b>wheel</b> to steer, hold the <b>GAS</b>/<b>BRAKE</b> pedals. 📢 honks, ✋ handbrake, 🔍 zoom.';
  $('title-footer').textContent = 'v1.0 — tap the little car 5× for a surprise';
}
// title logo car: tapping it honks (cat easter egg, touch-friendly)
$('logo-car').style.pointerEvents = 'auto';
$('logo-car').addEventListener('pointerdown', () => { SFX.init(); if (UI.screen === 'title' && UI.titleMenuShown) UI.titleHonk(); });
UI.ensureDemo();
UI.show('title', true);
requestAnimationFrame(frame);

// debug handle (harmless)
window.__PPN = { game, UI, Save, SFX, LEVELS, VEH_DEFS };
