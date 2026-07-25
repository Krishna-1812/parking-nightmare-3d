/* ============================================================
   PART F — UI manager, screens, achievements, daily, boot loop
   ============================================================ */

const ACHIEVEMENTS = [
  { id: 'first_park', icon: '🎓', name: 'License? What License?', desc: 'Finish your first mission' },
  { id: 's_rank', icon: '🏆', name: 'Show-Off', desc: 'Earn an S rank' },
  { id: 'no_shame', icon: '🥷', name: 'Invisible', desc: 'Finish a mission with under 5% shame' },
  { id: 'overtaker', icon: '💨', name: 'Slipstream', desc: '25 overtakes (lifetime)' },
  { id: 'soaker', icon: '💦', name: 'Sorry! Sorry!!', desc: 'Soak 3 pedestrians with puddles' },
  { id: 'crusher', icon: '🗿', name: 'Modern Art', desc: 'Crush 10 cones with the tank' },
  { id: 'ufo_park', icon: '🛸', name: 'Take Me To Your Parker', desc: 'Beam-park the UFO' },
  { id: 'viral', icon: '📱', name: 'Famous (Wrong Reasons)', desc: 'Hit 100% shame and go viral' },
];

const FAIL_POSTS = [
  'Someone in a #VEH# just achieved 100% public shame on #DIST#. I have never seen a parking attempt become a neighborhood event before. Thread below. 🧵',
  'BREAKING: local driver mistakes #DIST# for a demolition derby. The #VEH# has been asked to leave. The sidewalk has filed a complaint.',
  'day 1 of asking the city to revoke whatever license the #VEH# person on #DIST# has. the cones have formed a support group.',
  'I filmed the whole thing. The #VEH#. The honking. The curb. ESPECIALLY the curb. Posting in 4K at midnight. #DIST# will never forget.',
];

class UIManager {
  constructor(game) {
    this.game = game;
    this.stack = [];
    this.current = null;
    this.inRun = false;
    this.paused = false;
    this.pendingLevel = null;
    this.garageIdx = 0;
    this.vehKeys = Object.keys(VEH_DEFS);
    this.catClicks = 0;
    this.toastQueue = [];
    this.toastBusy = false;
    this._zoneT = null;
    this.bind();
  }

  // ---------- screen plumbing ----------
  show(id, push) {
    if (push !== false && this.current && this.current !== id) this.stack.push(this.current);
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    if (id) {
      const el = $('screen-' + id);
      if (el) el.classList.add('active');
    }
    this.current = id;
  }
  back() {
    SFX.uiBack();
    const prev = this.stack.pop() || 'title';
    if (prev === 'title') this.game.setMenuMode('title');
    if (prev === 'garage') this.game.setMenuMode('garage');
    this.show(prev, false);
  }
  hideAll() {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    this.current = null;
    this.stack = [];
  }

  bind() {
    const g = this.game;
    // global button sfx
    document.querySelectorAll('.btn, .hbtn, .node, .toggle').forEach(() => {});
    document.body.addEventListener('click', e => {
      if (e.target.closest('.btn') || e.target.closest('.hbtn')) SFX.uiClick();
    });
    document.querySelectorAll('[data-back]').forEach(b => b.addEventListener('click', () => this.back()));
    // title
    $('play-classic').addEventListener('click', () => { location.href = 'classic.html'; });
    $('open-privacy').addEventListener('click', () => this.show('privacy'));
    $('tut-skip').addEventListener('click', () => { SFX.uiBack(); this.finishTutorial(true); });
    document.querySelectorAll('[data-menu]').forEach(b => b.addEventListener('click', () => {
      const m = b.dataset.menu;
      if (m === 'play') { this.buildLevels(); this.show('levels'); }
      else if (m === 'daily') { this.buildDaily(); this.show('daily'); }
      else if (m === 'roam') { this.openRoam(); }
      else if (m === 'garage') { this.openGarage(); }
      else if (m === 'stats') { this.buildStats(); this.show('stats'); }
      else if (m === 'settings') { this.show('settings'); }
      else if (m === 'howto') { this.show('howto'); }
      else if (m === 'credits') { this.show('credits'); }
    }));
    $('logo-car').addEventListener('click', () => {
      if (++this.catClicks >= 5) {
        this.catClicks = 0;
        const cat = $('cat');
        cat.classList.remove('walk');
        void cat.offsetWidth;
        cat.classList.add('walk');
        SFX.bell();
        this.toast('🐈‍⬛', 'The Cat', 'It judges your parking. It judges everything.');
      }
    });
    // garage
    $('g-prev').addEventListener('click', () => this.garageMove(-1));
    $('g-buy').addEventListener('click', () => this.buyVehicle());
    $('g-next').addEventListener('click', () => this.garageMove(1));
    $('g-select').addEventListener('click', () => {
      const key = this.vehKeys[this.garageIdx];
      if (!vehicleUnlocked(key)) { SFX.uiBack(); return; }
      // joyride: free run through downtown with chosen wheels
      const lvl = Object.assign({}, LEVELS[3], {
        id: 'free', name: 'Joyride', veh: key, daily: false, free: true,
        brief: 'No stakes. No stars. Just you, the ' + VEH_DEFS[key].name + ', and a city that has no idea what is coming.',
      });
      this.startRun(lvl, key);
    });
    // prelevel
    $('pl-go').addEventListener('click', () => {
      if (this.pendingLevel) this.startRun(this.pendingLevel, this.pendingLevel.veh);
    });
    // pause
    $('pz-resume').addEventListener('click', () => this.pause(false));
    $('pz-retry').addEventListener('click', () => { this.pause(false); this.retry(); });
    // recenter takes the grip you are holding right now as the new neutral,
    // so it only makes sense while paused (mid-drive you would be steering)
    $('pz-recenter').addEventListener('click', () => {
      Input.calibrateTilt();
      this.toast('📱', 'Tilt recentered', 'This grip is now straight ahead');
    });
    $('pz-settings').addEventListener('click', () => this.show('settings'));
    $('pz-quit').addEventListener('click', () => { this.pause(false); this.quitToMenu(); });
    // results
    $('res-replay').addEventListener('click', () => this.beginReplay());
    $('res-photo').addEventListener('click', () => g.postcard());
    $('res-retry').addEventListener('click', () => this.retry());
    $('res-map').addEventListener('click', () => {
      const L = this.game.level;
      this.quitToMenu(L && (L.daily || L.weekly || L.challenge) ? 'daily' : (L && L.free ? undefined : 'levels'));
    });
    $('res-next').addEventListener('click', () => {
      const id = g.level.id;
      if (typeof id === 'number' && id < LEVELS.length) {
        const next = LEVELS[id]; // id is 1-based; LEVELS[id] is the next mission
        this.preLevel(next);
      }
    });
    // hud buttons
    $('btnPause').addEventListener('click', () => this.pause(true));
    $('btnRetry').addEventListener('click', () => this.retry());
    $('btnCam').addEventListener('click', () => g.cycleCamera());
    // daily + weekly + friend challenge
    $('daily-play').addEventListener('click', () => {
      const lvl = dailyLevel();
      this.startRun(lvl, lvl.veh);
    });
    $('daily-share').addEventListener('click', () => this.shareDaily());
    $('weekly-play').addEventListener('click', () => {
      const lvl = weeklyLevel();
      this.startRun(lvl, lvl.veh);
    });
    $('chal-go').addEventListener('click', () => {
      const lvl = parseChallengeCode($('chal-code').value);
      if (!lvl) {
        SFX.uiBack();
        this.toast('🤔', 'Code not recognized', 'Codes look like PN.m3.XYZ — ask your friend to re-share');
        return;
      }
      this.startRun(lvl, lvl.veh);
    });
    $('res-challenge').addEventListener('click', () => this.shareChallenge());
    // settings
    this.bindSettings();
    // confirm
    $('confirm-no').addEventListener('click', () => $('confirm-wrap').classList.add('hidden'));
    // replay skip
    $('replay-ui').addEventListener('click', () => this.endReplay());
    // input handlers
    Input.handlers.any = (e) => {
      if (this.current === 'title' && !$('main-menu').classList.contains('show')) {
        this.revealMenu();
      }
      if (g.state === 'replay') this.endReplay();
    };
    Input.handlers.horn = () => g.honk();
    Input.handlers.camera = () => { if (this.inRun) g.cycleCamera(); };
    Input.handlers.retry = () => { if (this.inRun && (g.state === 'drive' || g.state === 'park' || g.state === 'settle')) this.retry(); };
    Input.handlers.pause = () => {
      if (g.state === 'drive' || g.state === 'park' || g.state === 'settle') this.pause(!this.paused);
      else if (this.current && this.current !== 'title' && !this.inRun) this.back();
    };
    // title tap (touch)
    $('screen-title').addEventListener('pointerdown', () => {
      if (!$('main-menu').classList.contains('show')) this.revealMenu();
    });
    window.addEventListener('resize', () => {
      g.resize();
      this.updateRotateHint();
    });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden && this.inRun && !this.paused &&
        (g.state === 'drive' || g.state === 'park' || g.state === 'settle')) this.pause(true);
    });
  }

  revealMenu() {
    $('press-any').style.display = 'none';
    $('main-menu').classList.add('show');
    SFX.init();
    SFX.uiClick();
    SFX.musicStart(0);
    // daily login gift (once per calendar day)
    const S = Save.data, tk = todayKey();
    if (S.lastGift !== tk) {
      S.lastGift = tk;
      S.coins = (S.coins || 0) + 20;
      Save.save();
      setTimeout(() => this.toast('🎁', 'Daily gift: +20 coins', 'The parking gods smile upon you. Today.'), 1200);
    }
  }

  // ---------- mission select ----------
  buildLevels() {
    const wrap = $('districts');
    wrap.innerHTML = '';
    $('total-stars').textContent = `★ ${Save.totalStars()}/${LEVELS.length * 3}`;
    const groups = DISTRICTS.map(() => []);
    for (const lvl of LEVELS) groups[lvl.district].push(lvl);
    groups.forEach((lvls, di) => {
      const d = DISTRICTS[di];
      const div = document.createElement('div');
      div.className = 'district d' + di;
      div.innerHTML = `<h3><span class="dtag">${d.tag}</span> ${d.name}</h3>`;
      const nodes = document.createElement('div');
      nodes.className = 'nodes';
      for (const lvl of lvls) {
        const locked = lvl.id > Save.data.unlockedLevel;
        const stars = Save.data.stars[lvl.id] || 0;
        const btn = document.createElement('button');
        btn.className = 'node' + (locked ? ' locked' : '');
        const dist = Math.round(compileRouteLength(lvl.segs) / 100) / 10;
        btn.innerHTML = locked
          ? `<div class="n-num">MISSION ${lvl.id} <span class="padlock">🔒</span></div><div class="n-name">${lvl.name}</div><div class="n-info">Complete Mission ${lvl.id - 1}</div>`
          : `<div class="n-num">MISSION ${lvl.id}</div><div class="n-name">${lvl.name}</div>
             <div class="n-info">${dist} km · ${VEH_DEFS[lvl.veh].name.split(' ').pop()}</div>
             <div class="n-stars">${'★'.repeat(stars)}<span class="off">${'★'.repeat(3 - stars)}</span></div>`;
        btn.addEventListener('click', () => {
          if (locked) {
            btn.classList.remove('wiggle'); void btn.offsetWidth; btn.classList.add('wiggle');
            SFX.uiBack();
            return;
          }
          this.preLevel(lvl);
        });
        nodes.appendChild(btn);
      }
      div.appendChild(nodes);
      wrap.appendChild(div);
    });
  }
  preLevel(lvl) {
    this.pendingLevel = lvl;
    $('pl-district').textContent = DISTRICTS[lvl.district].name;
    $('pl-name').textContent = lvl.name;
    $('pl-brief').textContent = lvl.brief;
    $('pl-veh').textContent = VEH_DEFS[lvl.veh].name;
    $('pl-route').textContent = (Math.round(compileRouteLength(lvl.segs) / 100) / 10) + ' km · ' + (lvl.park === 'parallel' ? 'parallel finish' : 'bay finish');
    $('pl-par').textContent = fmtTime(lvl.par);
    const best = Save.data.bestScores[lvl.id];
    $('pl-best').textContent = best ? best + ' pts' : '—';
    this.show('prelevel');
  }

  // ---------- garage ----------
  openGarage() {
    this.game.setMenuMode('garage');
    this.garageIdx = this.vehKeys.indexOf(this.game.vehKey);
    if (this.garageIdx < 0) this.garageIdx = 0;
    this.updateGarage();
    this.show('garage');
  }
  garageMove(dir) {
    this.garageIdx = (this.garageIdx + dir + this.vehKeys.length) % this.vehKeys.length;
    SFX.uiMove();
    this.updateGarage();
  }
  updateGarage() {
    const key = this.vehKeys[this.garageIdx];
    const d = VEH_DEFS[key];
    const unlocked = vehicleUnlocked(key);
    this.game.setGarageVehicle(key);
    $('g-name').textContent = unlocked ? d.name : '???';
    $('g-flavor').textContent = unlocked ? d.flavor : 'Something is under that tarp. It is judging you too.';
    $('gs-size').style.width = d.stats.size + '%';
    $('gs-speed').style.width = d.stats.speed + '%';
    $('gs-hand').style.width = d.stats.hand + '%';
    $('gs-chaos').style.width = d.stats.chaos + '%';
    $('g-lock').textContent = unlocked ? '' : vehicleUnlockText(key);
    $('g-select').disabled = !unlocked;
    $('g-coins').textContent = '🪙 ' + (Save.data.coins || 0).toLocaleString();
    // Dr. Driving-style early unlock with coins
    const buy = $('g-buy');
    if (!unlocked && d.price) {
      buy.classList.remove('hidden');
      buy.textContent = `Buy 🪙 ${d.price.toLocaleString()}`;
      buy.disabled = (Save.data.coins || 0) < d.price;
    } else {
      buy.classList.add('hidden');
    }
    $('g-dots').innerHTML = this.vehKeys.map((k, i) =>
      `<span class="${i === this.garageIdx ? 'on' : ''}">●</span>`).join('');
  }
  buyVehicle() {
    const key = this.vehKeys[this.garageIdx];
    const d = VEH_DEFS[key];
    if (vehicleUnlocked(key) || !d.price || (Save.data.coins || 0) < d.price) return;
    this.confirm(`Buy the ${d.name} for 🪙 ${d.price.toLocaleString()}?`, () => {
      Save.data.coins -= d.price;
      Save.data.owned = Save.data.owned || [];
      Save.data.owned.push(key);
      Save.save();
      SFX.confettiPop();
      this.toast('🎉', 'Purchased!', `The ${d.name} is yours.`);
      this.updateGarage();
    });
  }

  // ---------- run lifecycle ----------
  startRun(level, vehKey) {
    this.hideAll();
    document.body.classList.remove('cine');
    this.inRun = true;
    $('hud').classList.add('active');
    this.game.startLevel(level, vehKey);
    if (Input.usingTouch || Save.data.settings.forceTouch) {
      Input.showTouch(true);
      try {
        const de = document.documentElement;
        if (de.requestFullscreen && !document.fullscreenElement) de.requestFullscreen({ navigationUI: 'hide' }).catch(() => {});
        if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(() => {});
      } catch (e) {}
      this.updateRotateHint();
    }
    $('obj-text').textContent = level.free
      ? '🪙 Cruise, scoop the coin trail, park in the bay whenever you like'
      : level.park === 'parallel'
        ? '🏁 Reach the destination, then parallel park in the glowing spot'
        : '🏁 Reach the destination, then back into the glowing bay';
    $('hud').querySelector('#align-widget').classList.remove('show');
    this.game.hud.failTint.style.opacity = 0;
    this.startTutorial(level);
    this.countdown(() => {
      this.game.beginDrive();
      if (level.tutorial && !this.tutSteps) this.tutTip('Follow the glowing chevrons! WASD / arrows to drive, Space handbrake.', 6);
    });
  }

  // ---------- interactive driving school (first run of Mission 1) ----------
  startTutorial(level) {
    this.tutSteps = null;
    $('tut-card').classList.add('hidden');
    if (!level.tutorial || Save.data.tutorialDone) return;
    const touch = Input.usingTouch || Save.data.settings.forceTouch;
    const gasKey = touch ? 'Hold GAS' : 'Hold W / ↑';
    const brakeKey = touch ? 'hold BRAKE' : 'hold S / ↓';
    const steerKey = touch ? (Input.tiltOn ? 'tilt your phone' : 'turn the wheel') : 'steer with A/D or ←/→';
    this.tutSteps = [
      { icon: '🚗', text: `${gasKey} and get rolling — follow the glowing chevrons`, done: g => g.player.speedAbs > 4.5 },
      { icon: '🛑', text: `Nice! Now ${brakeKey} until you stop completely`, done: g => g.player.speedAbs < 0.35 },
      { icon: '🧭', text: `Roll on and ${steerKey} through the bend ahead`, done: g => g.playerProj.s > g.world.route.length * 0.45 },
      { icon: '😰', text: 'See the face on the right? Bonks, honks & curb-hops feed the Shame meter — 100% means you go viral and FAIL', timed: 5.5 },
      { icon: '🅿️', text: 'At the glowing spot: get fully inside, straighten up (ANGLE), hug the curb, hold still 1.5s', done: g => g.state === 'success' },
    ];
    this.tutIdx = 0;
    this.tutTimer = 0;
    this.renderTutStep();
  }
  renderTutStep() {
    const card = $('tut-card');
    if (!this.tutSteps || this.tutIdx >= this.tutSteps.length) { card.classList.add('hidden'); return; }
    const s = this.tutSteps[this.tutIdx];
    card.classList.remove('hidden');
    $('tut-icon').textContent = s.icon;
    $('tut-text').textContent = s.text;
    $('tut-dots').innerHTML = this.tutSteps.map((_, i) =>
      `<i class="${i < this.tutIdx ? 'done' : (i === this.tutIdx ? 'now' : '')}"></i>`).join('');
    card.classList.remove('pulse'); void card.offsetWidth; card.classList.add('pulse');
  }
  tutorialFrame(g, dt) {
    if (!this.tutSteps || this.tutIdx >= this.tutSteps.length) return;
    const s = this.tutSteps[this.tutIdx];
    let done = false;
    if (s.timed) { this.tutTimer += dt; done = this.tutTimer >= s.timed; }
    else done = s.done(g);
    if (!done) return;
    this.tutTimer = 0;
    this.tutIdx++;
    SFX.uiClick();
    buzz(20);
    if (this.tutIdx >= this.tutSteps.length) this.finishTutorial();
    else this.renderTutStep();
  }
  finishTutorial(skipped) {
    this.tutSteps = null;
    $('tut-card').classList.add('hidden');
    if (skipped) return;
    Save.data.tutorialDone = true;
    Save.data.coins = (Save.data.coins || 0) + 100;
    Save.save();
    SFX.achievementDing();
    this.toast('🎓', 'Driving School: passed!', '+100 coins. The instructor is quietly impressed.');
  }
  countdown(done) {
    const cd = $('countdown'), num = $('cd-num');
    cd.classList.remove('hidden');
    let n = 3;
    const token = (this._cdToken = (this._cdToken || 0) + 1);
    const tick = () => {
      if (!this.inRun || token !== this._cdToken) { cd.classList.add('hidden'); return; }
      if (n > 0) {
        num.textContent = n;
        num.classList.remove('pop'); void num.offsetWidth; num.classList.add('pop');
        SFX.countBeep(false);
        n--; setTimeout(tick, 800);
      } else {
        num.textContent = 'GO!';
        num.classList.remove('pop'); void num.offsetWidth; num.classList.add('pop');
        SFX.countBeep(true);
        setTimeout(() => cd.classList.add('hidden'), 800);
        done();
      }
    };
    tick();
  }
  retry() {
    const lvl = this.game.level;
    const veh = this.game.vehKey;
    $('screen-results').classList.remove('active');
    this.game.hud.failTint.style.opacity = 0;
    this.startRun(lvl, veh);
  }
  // ---------- free roam setup (slowroads-style) ----------
  openRoam() {
    const S = Save.data;
    this.roamCfg = Object.assign(
      { district: 0, time: 'day', wx: 'clear', veh: 'hatch', seed: Math.random().toString(36).slice(2, 10) },
      S.roamCfg || {}
    );
    if (!vehicleUnlocked(this.roamCfg.veh)) this.roamCfg.veh = 'hatch';
    if (!this._roamWired) {
      this._roamWired = true;
      const cfg = () => this.roamCfg;
      const step = (prevId, nextId, set) => {
        $(prevId).addEventListener('click', () => { set(-1); SFX.uiMove(); this.renderRoam(); });
        $(nextId).addEventListener('click', () => { set(1); SFX.uiMove(); this.renderRoam(); });
      };
      step('rm-loc-prev', 'rm-loc-next', d => {
        cfg().district = (cfg().district + d + DISTRICTS.length) % DISTRICTS.length;
      });
      step('rm-veh-prev', 'rm-veh-next', d => {
        const keys = Object.keys(VEH_DEFS).filter(vehicleUnlocked);
        let i = keys.indexOf(cfg().veh); if (i < 0) i = 0;
        cfg().veh = keys[(i + d + keys.length) % keys.length];
      });
      const opts = (id, key) => {
        $(id).querySelectorAll('button').forEach(b =>
          b.addEventListener('click', () => { cfg()[key] = b.dataset.v; SFX.uiClick(); this.renderRoam(); }));
      };
      opts('rm-time', 'time');
      opts('rm-wx', 'wx');
      $('rm-rand').addEventListener('click', () => {
        cfg().seed = Math.random().toString(36).slice(2, 10);
        SFX.uiClick(); this.renderRoam();
      });
      $('rm-seed').addEventListener('input', e => { cfg().seed = e.target.value.trim(); });
      $('rm-go').addEventListener('click', () => {
        cfg().seed = $('rm-seed').value.trim() || Math.random().toString(36).slice(2, 10);
        Save.data.roamCfg = Object.assign({}, cfg());
        Save.save();
        const lvl = freeRoamLevel(cfg());
        this.startRun(lvl, lvl.veh);
      });
    }
    this.renderRoam();
    this.show('roam');
  }
  renderRoam() {
    const c = this.roamCfg;
    $('rm-loc-name').textContent = DISTRICTS[c.district].name;
    $('rm-veh-name').textContent = VEH_DEFS[c.veh].name.toUpperCase();
    $('rm-seed').value = c.seed;
    $('rm-time').querySelectorAll('button').forEach(b => b.classList.toggle('sel', b.dataset.v === c.time));
    $('rm-wx').querySelectorAll('button').forEach(b => b.classList.toggle('sel', b.dataset.v === c.wx));
  }

  quitToMenu(screen) {
    // free-roam coins survive a mid-run quit (success already banked & zeroed them)
    const g = this.game;
    if (g.level && g.level.free && g.coinsRun > 0) {
      Save.data.coins = (Save.data.coins || 0) + g.coinsRun;
      Save.save();
      this.toast('🪙', '+' + g.coinsRun + ' coins', 'Free Roam haul banked');
      g.coinsRun = 0;
    }
    this.inRun = false;
    document.body.classList.remove('cine');
    this.game.hud.failTint.style.opacity = 0;
    $('hud').classList.remove('active');
    $('hud').querySelector('#align-widget').classList.remove('show');
    Input.showTouch(false);
    SFX.stopAllLoops();
    SFX.musicStart(0);
    this.game.setMenuMode('title');
    try { if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {}); } catch (e) {}
    this.hideAll();
    if (screen === 'levels') { this.buildLevels(); this.show('levels'); this.stack = ['title']; }
    else if (screen === 'daily') { this.buildDaily(); this.show('daily'); this.stack = ['title']; }
    else { this.show('title', false); $('press-any').style.display = 'none'; $('main-menu').classList.add('show'); }
  }
  pause(on) {
    if (on && this.paused) return;
    this.paused = on;
    if (on) {
      $('pz-recenter').classList.toggle('hidden', !Input.tiltOn);
      this.show('pause'); SFX.engineUpdate(0, 0);
    }
    else { this.hideAll(); }
  }

  // ---------- results ----------
  showResults(fs, failed) {
    if (!this.inRun) return;
    document.body.classList.remove('cine');
    $('hud').querySelector('#align-widget').classList.remove('show');
    this.zoneBanner('');
    const panel = $('results-panel');
    const social = $('social-post');
    const lines = $('res-lines');
    lines.innerHTML = '';
    $('res-stamp').classList.remove('slam');
    document.querySelectorAll('#res-stars span').forEach(s => { s.classList.remove('slam', 'off'); });
    if (failed) {
      $('res-title').textContent = fs.reason === 'damage' ? 'TOTALED!' : 'YOU WENT VIRAL';
      $('res-sub').textContent = fs.reason === 'damage'
        ? 'The ' + VEH_DEFS[this.game.vehKey].name + ' has transcended repair.'
        : 'The internet has seen everything. Everything.';
      social.classList.remove('hidden');
      const post = pick(FAIL_POSTS)
        .replace(/#VEH#/g, VEH_DEFS[this.game.vehKey].name)
        .replace(/#DIST#/g, DISTRICTS[this.game.level.district].name);
      social.querySelector('.sp-text').textContent = post;
      social.querySelector('.sp-likes').textContent = '❤️ ' + (randi(8, 96) / 10) + 'k';
      $('res-total-v').textContent = '0';
      $('res-best').textContent = '';
      $('res-next').disabled = true;
      const unlockAch = fs.reason === 'shame';
      if (unlockAch) this.unlockAch('viral');
    } else {
      $('res-title').textContent = fs.perfect ? 'PERFECT PARK!' : 'PARKED!';
      $('res-sub').textContent = fs.perfect ? 'The sidewalk applauds. Reluctantly.' : 'Mission complete. Dignity: mostly intact.';
      if (fs.challenge) {
        $('res-title').textContent = fs.challenge.beaten ? 'CHALLENGE WON!' : 'PARKED… BUT';
        $('res-sub').textContent = fs.challenge.beaten
          ? `You beat the target of ${fs.challenge.target.toLocaleString()} pts. Screenshot this. Gloat responsibly.`
          : `Target was ${fs.challenge.target.toLocaleString()} pts — so close. Rematch?`;
      }
      social.classList.add('hidden');
      // coin-machine count-up on the total
      const tv = $('res-total-v');
      const target = fs.total, t0 = performance.now();
      clearInterval(this._cntI);
      this._cntI = setInterval(() => {
        const k = Math.min(1, (performance.now() - t0) / 1100);
        tv.textContent = Math.round(target * (1 - Math.pow(1 - k, 3)));
        if (k >= 1) clearInterval(this._cntI);
      }, 33);
      const id = this.game.level.id;
      const best = Save.data.bestScores[id];
      $('res-best').textContent = this.game.level.free ? 'Joyride — nothing was saved, nothing was learned.' :
        (best && fs.total >= best ? '🎉 NEW BEST!' : (best ? 'Best: ' + best : ''));
      $('res-next').disabled = !(typeof id === 'number' && id < LEVELS.length && Save.data.unlockedLevel > id);
      // animated lines
      fs.lines.forEach((ln, i) => {
        const div = document.createElement('div');
        div.className = 'res-line';
        const v = ln[1];
        div.innerHTML = `<span>${ln[0]}</span><span class="rv ${v < 0 ? 'neg' : (v > 0 ? 'pos' : '')}">${v > 0 ? '+' : ''}${v}</span>`;
        lines.appendChild(div);
        setTimeout(() => div.classList.add('in'), 500 + i * 140);
      });
      if (fs.coins) {
        const div = document.createElement('div');
        div.className = 'res-line';
        div.innerHTML = `<span>🪙 Coins earned</span><span class="rv pos">+${fs.coins} · wallet ${(Save.data.coins || 0).toLocaleString()}</span>`;
        lines.appendChild(div);
        setTimeout(() => div.classList.add('in'), 500 + fs.lines.length * 140);
      }
      // stars
      const starEls = document.querySelectorAll('#res-stars span');
      starEls.forEach((el, i) => {
        setTimeout(() => {
          el.classList.add('slam');
          if (i < fs.stars) SFX.starSlam(i);
          else el.classList.add('off');
        }, 1300 + i * 340);
      });
      if (fs.sRank) {
        setTimeout(() => { $('res-stamp').classList.add('slam'); SFX.stamp(); }, 2500);
      }
    }
    // failed or free-roam runs can't be turned into challenges
    $('res-challenge').classList.toggle('hidden', failed || !!(this.game.level && this.game.level.free));
    this.show('results', false);
    this.stack = [];
  }

  // ---------- replay ----------
  beginReplay() {
    if (!this.game.finalStats || !this.game.replayBuf.length) return;
    document.body.classList.add('cine');
    $('screen-results').classList.remove('active');
    $('replay-ui').classList.remove('hidden');
    requestAnimationFrame(() => $('replay-ui').classList.add('show'));
    this.game.beginReplay();
  }
  endReplay() {
    if (this.game.state !== 'replay') return;
    $('replay-ui').classList.remove('show');
    setTimeout(() => $('replay-ui').classList.add('hidden'), 400);
    this.game.endReplayState();
    if (this.game.finalStats) this.showResults(this.game.finalStats, !!this.game.finalStats.failed);
  }

  // ---------- stats ----------
  buildStats() {
    const st = Save.data.stats;
    const cells = [
      [st.parks, 'MISSIONS DONE'], [Save.totalStars(), 'STARS'],
      [st.collisions, 'COLLISIONS'], [st.overtakes, 'OVERTAKES'],
      [st.nearMisses, 'CLOSE CALLS'], [st.pedsScandalized, 'PEDS SCANDALIZED'],
      [st.propsDestroyed, 'PROPS DESTROYED'], [st.crushes, 'THINGS CRUSHED'],
      [st.redLights, 'REDS RUN'], [Math.round(st.kmDriven * 10) / 10 + ' km', 'DISTANCE DRIVEN'],
      [st.fastestPark ? fmtTime(st.fastestPark) : '—', 'FASTEST MISSION'],
    ];
    $('stats-grid').innerHTML = cells.map(c =>
      `<div class="stat-cell"><div class="sv">${c[0]}</div><div class="sk">${c[1]}</div></div>`).join('');
    $('shame-total-line').textContent = `Lifetime shame accumulated: ${Math.round(st.totalShame)}%. The sidewalks remember.`;
    $('ach-list').innerHTML = ACHIEVEMENTS.map(a => {
      const got = Save.data.achievements[a.id];
      return `<div class="ach ${got ? '' : 'locked'}"><span class="ai">${a.icon}</span><div><div class="an">${a.name}</div><div class="ad">${a.desc}</div></div></div>`;
    }).join('');
  }
  unlockAch(id) {
    if (Save.data.achievements[id]) return;
    Save.data.achievements[id] = Date.now();
    Save.save();
    const a = ACHIEVEMENTS.find(x => x.id === id);
    if (a) {
      SFX.achievementDing();
      this.toast(a.icon, 'Achievement: ' + a.name, a.desc);
    }
  }
  checkAchievements(g) {
    const st = Save.data.stats;
    const fs = g.finalStats;
    if (fs && !fs.failed) {
      this.unlockAch('first_park');
      if (fs.sRank) this.unlockAch('s_rank');
      if (g.shame < 5) this.unlockAch('no_shame');
      if (g.vehKey === 'ufo') this.unlockAch('ufo_park');
    }
    if (st.overtakes >= 25) this.unlockAch('overtaker');
    if (st.pedsScandalized >= 3 && this.game.level && this.game.level.rain) this.unlockAch('soaker');
    if (st.crushes >= 10) this.unlockAch('crusher');
  }

  // ---------- daily + weekly + challenge hub ----------
  buildDaily() {
    const key = todayKey();
    const lvl = dailyLevel();
    const S = Save.data;
    $('daily-date').textContent = key;
    $('daily-desc').textContent = lvl.brief + ` (${VEH_DEFS[lvl.veh].name}, ${DISTRICTS[lvl.district].name.toLowerCase()}, par ${fmtTime(lvl.par)})`;
    // streak flame — only alive if played today or yesterday
    const alive = S.streakLast === key || S.streakLast === dateKeyOffset(-1);
    $('daily-streak').innerHTML = alive && S.streak > 0
      ? `🔥 <b>${S.streak}-day streak</b> — daily bonus 🪙 ${Math.min(100, (S.streakLast === key ? S.streak : S.streak + 1) * 10)}`
      : '🔥 Start a streak — every consecutive day pays a bigger coin bonus';
    const day = S.daily[key];
    const board = $('daily-board');
    if (day && day.board.length) {
      board.innerHTML = day.board.map((r, i) =>
        `<div class="db-row"><span class="rank">#${i + 1}</span><span>${r.score} pts</span><span>${'★'.repeat(r.stars)}</span><span>${fmtTime(r.time)}</span></div>`).join('');
    } else {
      board.innerHTML = '<div class="db-row empty">No attempts yet — be the legend</div>';
    }
    // weekly gauntlet card
    const wlvl = weeklyLevel();
    const wk = S.weekly[weekKey()];
    $('weekly-desc').textContent = `${VEH_DEFS[wlvl.veh].name} · ${DISTRICTS[wlvl.district].name.toLowerCase()} · ${(Math.round(compileRouteLength(wlvl.segs) / 100) / 10)} km${wlvl.rain ? ' · rain' : ''}${wlvl.snow ? ' · snow' : ''}${wlvl.time === 'night' ? ' · night' : ''}`;
    $('weekly-best').textContent = wk ? `Your week's best: ${wk.score} pts ${'★'.repeat(wk.stars)}` : 'Not attempted yet — all week to set a score';
  }
  shareChallenge() {
    const g = this.game, fs = g.finalStats;
    if (!fs || fs.failed || !g.level) return;
    const code = makeChallengeCode(g.level, fs.total);
    if (!code) { this.toast('⚔️', 'Not shareable', 'Free-roam cruises can\'t become challenges'); return; }
    const txt = `⚔️ I scored ${fs.total.toLocaleString()} pts on "${g.level.name}" in Parking Nightmare 3D. Beat me!\n1. Play: ${location.origin + location.pathname}\n2. Daily Challenge → Friend Challenge → enter code: ${code}`;
    const done = () => this.toast('⚔️', 'Challenge copied!', 'Send it to a friend — the code rebuilds your exact streets');
    try {
      if (navigator.share) { navigator.share({ text: txt }).catch(() => {}); done(); }
      else if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(txt).then(done, done);
      else done();
    } catch (e) { done(); }
  }
  shareDaily() {
    const key = todayKey();
    const day = Save.data.daily[key];
    if (!day || !day.board.length) { this.toast('📅', 'Nothing to share', 'Complete today\'s challenge first!'); return; }
    const best = day.board[0];
    const lvl = dailyLevel();
    const txt = `Parking Nightmare 3D — Daily ${key}\n🚗💨 ${VEH_DEFS[lvl.veh].name} · ${DISTRICTS[lvl.district].name}\n🏁 ${best.score} pts ${'⭐'.repeat(best.stars)} in ${fmtTime(best.time)}\nCan you park it better?`;
    const done = () => this.toast('📋', 'Copied!', 'Result copied to clipboard');
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(txt).then(done, done);
      else done();
    } catch (e) { done(); }
  }

  // ---------- settings ----------
  bindSettings() {
    const s = Save.data.settings;
    const g = this.game;
    const slider = (id, key, after) => {
      const el = $(id);
      el.value = s[key] * 100;
      el.addEventListener('input', () => {
        s[key] = el.value / 100;
        Save.save();
        if (after) after(s[key]); else SFX.applyVolumes();
      });
    };
    slider('set-master', 'master');
    slider('set-music', 'music');
    slider('set-sfx', 'sfx');
    // takes effect on the next sensor reading — no rebuild needed
    slider('set-tiltsens', 'tiltSens', () => {});
    const toggle = (id, key, cb) => {
      const el = $(id);
      const apply = () => {
        el.classList.toggle('on', !!s[key]);
        el.setAttribute('aria-checked', !!s[key]);
      };
      apply();
      const flip = () => {
        s[key] = !s[key];
        Save.save();
        apply();
        SFX.uiClick();
        if (cb) cb(s[key]);
      };
      el.addEventListener('click', flip);
      el.addEventListener('keydown', e => { if (e.code === 'Space' || e.code === 'Enter') { e.preventDefault(); flip(); } });
    };
    toggle('set-hq', 'hq', v => {
      Save.data.settings.qAuto = false; // the player has chosen; stop auto-picking
      Save.save();
      g.renderer.shadowMap.enabled = v;
      g.setPost(v);
      this.toast('✨', v ? 'High quality on' : 'Performance mode', v ? 'Shadows + cinematic bloom enabled' : 'Shadows + bloom off — fully applies next mission');
    });
    toggle('set-shake', 'shake');
    toggle('set-cb', 'colorblind', v => document.body.classList.toggle('cb', v));
    toggle('set-rm', 'reducedMotion', v => document.body.classList.toggle('rm', v));
    toggle('set-tilt', 'tilt', v => {
      if (v) Input.enableTilt().then(ok => {
        if (!ok) this.toast('📵', 'No tilt access', 'Motion sensor unavailable — the wheel stays on');
      });
      Input.applyTiltUI();
    });
    toggle('set-tiltinv', 'tiltInvert', v => {
      Input.calibrateTilt(); // re-zero so the flip doesn't leave a standing offset
      this.toast('🔄', v ? 'Tilt inverted' : 'Tilt normal', v ? 'Lean left to steer right' : 'Lean right to steer right');
    });
    toggle('set-touch', 'forceTouch', v => { if (this.inRun) Input.showTouch(v || Input.usingTouch); });
    toggle('set-jingle', 'jingle');
    toggle('set-vib', 'vibrate', v => { if (v) buzz(30); });
    $('set-bug').addEventListener('click', () => {
      let log = [];
      try { log = JSON.parse(localStorage.getItem(ERRLOG_KEY) || '[]'); } catch (e) {}
      const report = [
        'Parking Nightmare 3D — bug report',
        'UA: ' + navigator.userAgent,
        'Screen: ' + screen.width + 'x' + screen.height + ' dpr ' + devicePixelRatio,
        'Touch: ' + Input.usingTouch + ' · tilt: ' + Input.tiltOn,
        'Progress: mission ' + Save.data.unlockedLevel + ', ' + Save.totalStars() + '★, ' + (Save.data.coins || 0) + ' coins',
        log.length ? 'Recent errors:\n' + log.map(e => `  ${e.t} ${e.msg} (${e.src}:${e.line})`).join('\n') : 'Recent errors: none',
      ].join('\n');
      const done = () => this.toast('🐞', 'Bug report copied', 'Paste it wherever you report the problem');
      try {
        if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(report).then(done, done);
        else done();
      } catch (e) { done(); }
    });
    document.body.classList.toggle('cb', !!s.colorblind);
    document.body.classList.toggle('rm', !!s.reducedMotion);
    $('set-reset').addEventListener('click', () => {
      this.confirm('Wipe ALL progress, stars and achievements? The shame, however, is forever.', () => {
        Save.reset();
        this.toast('🗑', 'Save wiped', 'Fresh start. The cones have already forgotten you.');
        this.buildLevels();
      });
    });
  }
  confirm(text, yes) {
    $('confirm-text').textContent = text;
    $('confirm-wrap').classList.remove('hidden');
    const btn = $('confirm-yes');
    const clone = btn.cloneNode(true);
    btn.parentNode.replaceChild(clone, btn);
    clone.addEventListener('click', () => {
      $('confirm-wrap').classList.add('hidden');
      yes();
    });
  }

  // ---------- HUD helpers ----------
  comboPop(text) {
    const el = $('combo-pop');
    el.textContent = text;
    el.classList.remove('show');
    void el.offsetWidth;
    el.classList.add('show');
    clearTimeout(this._comboT);
    this._comboT = setTimeout(() => el.classList.remove('show'), 1300);
  }
  thresholdBanner(text) {
    const el = $('threshold-banner');
    el.textContent = '⚠️ ' + text;
    el.classList.add('show');
    clearTimeout(this._threshT);
    this._threshT = setTimeout(() => el.classList.remove('show'), 2200);
  }
  zoneBanner(text) {
    const el = $('zone-banner');
    if (!text) { el.classList.remove('show'); return; }
    el.textContent = text;
    el.classList.add('show');
    clearTimeout(this._zoneT);
    this._zoneT = setTimeout(() => el.classList.remove('show'), 1800);
  }
  tutTip(text, secs) {
    const el = $('tut-tip');
    el.textContent = '💡 ' + text;
    el.classList.remove('hidden');
    el.style.opacity = 1;
    clearTimeout(this._tipT);
    this._tipT = setTimeout(() => {
      el.style.opacity = 0;
      setTimeout(() => el.classList.add('hidden'), 400);
    }, (secs || 4) * 1000);
  }
  toast(icon, name, desc) {
    this.toastQueue.push({ icon, name, desc });
    this.pumpToasts();
  }
  pumpToasts() {
    if (this.toastBusy || !this.toastQueue.length) return;
    this.toastBusy = true;
    const t = this.toastQueue.shift();
    const el = document.createElement('div');
    el.className = 'toast';
    el.innerHTML = `<span class="ti">${t.icon}</span><div><div class="tn">${t.name}</div><div class="td">${t.desc}</div></div>`;
    $('toasts').appendChild(el);
    requestAnimationFrame(() => el.classList.add('in'));
    setTimeout(() => {
      el.classList.remove('in');
      setTimeout(() => { el.remove(); this.toastBusy = false; this.pumpToasts(); }, 400);
    }, 3400);
  }
  updateRotateHint() {
    const el = $('rotate-hint');
    const portrait = window.innerHeight > window.innerWidth;
    if (this.inRun && portrait && (Input.usingTouch || Save.data.settings.forceTouch)) {
      el.classList.remove('hidden');
      el.style.opacity = 1;
      clearTimeout(this._rotT);
      this._rotT = setTimeout(() => { el.style.opacity = 0; setTimeout(() => el.classList.add('hidden'), 500); }, 4000);
    } else {
      el.classList.add('hidden');
    }
  }
}

// route length helper for menu cards (cheap, no sampling)
function compileRouteLength(segs) {
  let len = 0;
  for (const sg of segs) {
    if (sg.t === 'S') len += sg.len;
    else if (sg.t === 'X') len += sg.w || 26;
    else len += (sg.r || 34) * rad(sg.a || 90);
  }
  return len;
}

// ============================================================
// BOOT
// ============================================================
// Auto graphics tier — must run BEFORE `new Game()`, which reads settings.hq
// to size shadow maps and switch the bloom pipeline on. Shadows + bloom cost
// roughly 1.5x frame time, which on phone hardware is the difference between
// a choppy ~25fps and a smooth ride, so touch devices start in Performance
// mode. Cleared the moment the player picks a side in Settings.
if (Save.data.settings.qAuto) {
  Save.data.settings.hq = !(Input.usingTouch || Save.data.settings.forceTouch);
  Save.save();
}

const game = new Game();
const UI = new UIManager(game);

game.setMenuMode('title');
UI.show('title', false);
if (Input.usingTouch) $('press-any').textContent = '— TAP ANYWHERE —';

// boot splash fades once the first real frame is ready (timeout = backstop
// for browsers that throttle rAF on hidden/background tabs)
const _bootOff = () => {
  const b = $('boot');
  if (b) { b.classList.add('bye'); setTimeout(() => b.remove(), 650); }
};
requestAnimationFrame(() => requestAnimationFrame(_bootOff));
setTimeout(_bootOff, 4000);

// ---------- PWA: service worker + install prompt ----------
if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost')) {
  navigator.serviceWorker.register('./sw.js').then(reg => {
    reg.addEventListener('updatefound', () => {
      const nw = reg.installing;
      if (!nw) return;
      nw.addEventListener('statechange', () => {
        if (nw.state === 'installed' && navigator.serviceWorker.controller) {
          UI.toast('🆕', 'Update ready', 'Close and reopen the game to get the latest version');
        }
      });
    });
  }).catch(() => { /* offline-first is a bonus, not a requirement */ });
}
let _installEv = null;
window.addEventListener('beforeinstallprompt', e => {
  e.preventDefault();
  _installEv = e;
  $('btn-install').classList.remove('hidden');
});
$('btn-install').addEventListener('click', () => {
  if (!_installEv) return;
  _installEv.prompt();
  _installEv.userChoice.then(r => {
    if (r.outcome === 'accepted') $('btn-install').classList.add('hidden');
    _installEv = null;
  });
});
window.addEventListener('appinstalled', () => {
  $('btn-install').classList.add('hidden');
  UI.toast('📲', 'Installed!', 'Parking Nightmare 3D now lives on your home screen');
});

// main loop: fixed timestep + interpolated render
let _last = performance.now() / 1000;
let _acc = 0;
const _dp = () => Math.min(window.devicePixelRatio || 1, game.dpCap);
function frame() {
  requestAnimationFrame(frame);
  const now = performance.now() / 1000;
  let dt = Math.min(0.1, now - _last);
  _last = now;
  // canvas self-heal (mobile URL bar, hidden-at-boot panes)
  const w = Math.floor(window.innerWidth * _dp()), h = Math.floor(window.innerHeight * _dp());
  if ((game.cv.width !== w || game.cv.height !== h) && window.innerWidth > 0 && window.innerHeight > 0) game.resize();
  if (!UI.paused) {
    _acc += dt;
    let n = 0;
    while (_acc >= STEP && n < 10) { game.fixedUpdate(STEP); _acc -= STEP; n++; }
    if (n >= 10) _acc = 0;
    game.render(clamp(_acc / STEP, 0, 1), dt);
  } else {
    game.render(1, 0.0001);
  }
}
requestAnimationFrame(frame);

// debug/test handle
window.__PPN = { game, UI, Save, SFX, LEVELS, VEH_DEFS, DISTRICTS, Assets, dailyLevel, weeklyLevel, makeChallengeCode, parseChallengeCode, seededMission };
