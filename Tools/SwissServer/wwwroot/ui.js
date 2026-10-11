'use strict';
/* Swiss Police Simulator – web UI. Runs embedded in the Unreal client (?embedded=1, transparent) or standalone as a top-down sandbox that drives the same server. */
const $ = id => document.getElementById(id);
const params = new URLSearchParams(location.search), embedded = params.has('embedded');
if (embedded) { document.documentElement.classList.add('embedded'); document.body.classList.add('embedded'); }
let ws = null, S = null, W = null, started = false, curWin = null, enc = null, route = [], routeInfo = null, pois = [], measure = null, lastMeasureAt = 0;
const send = o => { if (ws && ws.readyState === 1) ws.send(JSON.stringify(o)); };
const esc = s => String(s == null ? '' : s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fmtKm = m => m >= 1000 ? (m / 1000).toFixed(1) + ' km' : Math.round(m / 10) * 10 + ' m';

// ---------------------------------------------------------------- connection
function connect() {
  ws = new WebSocket((location.protocol === 'https:' ? 'wss' : 'ws') + '://' + location.host + '/ws'); ws.binaryType = 'arraybuffer';
  ws.onopen = () => { $('conn').classList.add('hidden'); send({ t: 'hello', role: embedded ? 'ui' : 'sandbox', name: 'web' }); };
  ws.onclose = () => { $('conn').textContent = 'Verbindung getrennt – neuer Versuch …'; $('conn').classList.remove('hidden'); setTimeout(connect, 1500); };
  ws.onmessage = ev => { if (typeof ev.data === 'string') onText(JSON.parse(ev.data)); else onBinary(ev.data); };
}

function onText(m) {
  switch (m.t) {
    case 'welcome': W = m; if (!m.playing) showMenu(); break;
    case 'started': started = true; $('menu').classList.add('hidden'); $('hud').classList.remove('hidden'); if (!embedded) sim.start(m); closeWin(); break;
    case 'state': S = m; renderHud(); if (curWin === 'career' || curWin === 'pause') refreshWin(); break;
    case 'note': toast(m); break;
    case 'encounter': enc = m.enc; if (m.open && enc && curWin !== 'encounter') openWin('encounter'); else if (curWin === 'encounter') updateEncounter(); break;
    case 'encounterEnd': enc = null; if (curWin === 'encounter') closeWin(); break;
    case 'computerResult': showComputer(m); break;
    case 'computerLog': showComputerLog(m.items); break;
    case 'searchResult': mapSearch.items = m.items; renderMapResults(); break;
    case 'route': route = m.points || []; routeInfo = m; break;
    case 'measure': measure = m; lastMeasureAt = performance.now(); renderHud(); break;
    case 'speak': speak(m); break;
    case 'beep': beep(); break;
    case 'career': showCareer(m); break;
    case 'notebook': showNotebook(m); break;
    case 'pois': pois = m.items; break;
    case 'spawn': case 'despawn': case 'props': case 'actors': sim.onEntityMsg(m); break;
    case 'ui': if (m.open) openWin(m.open); if (m.close) closeWin(); break;
    case 'error': toast({ kind: 'bad', text: m.message, seconds: 6 }); break;
  }
}

// ---------------------------------------------------------------- binary snapshot (sandbox only)
const ent = { cars: [], peds: [], actors: [] };
function onBinary(buf) {
  const dv = new DataView(buf); if (dv.getUint8(0) !== 2) return; let o = 1; o += 8;
  const rd = { i32() { const v = dv.getInt32(o, true); o += 4; return v; }, f64() { const v = dv.getFloat64(o, true); o += 8; return v; }, f32() { const v = dv.getFloat32(o, true); o += 4; return v; }, u8() { return dv.getUint8(o++); }, u16() { const v = dv.getUint16(o, true); o += 2; return v; } };
  const nc = rd.i32(); ent.cars = []; for (let i = 0; i < nc; i++) { const id = rd.i32(), e = rd.f64(), n = rd.f64(); rd.f32(); const yaw = rd.f32(), v = rd.f32(), fl = rd.u16(); ent.cars.push({ id, e, n, yaw, v, fl }); }
  const np = rd.i32(); ent.peds = []; for (let i = 0; i < np; i++) { const id = rd.i32(), e = rd.f64(), n = rd.f64(); rd.f32(); rd.f32(); rd.u8(); rd.f32(); ent.peds.push({ id, e, n }); }
  const na = rd.i32(); ent.actors = []; for (let i = 0; i < na; i++) { const id = rd.i32(), e = rd.f64(), n = rd.f64(); rd.f32(); rd.f32(); rd.u8(); const fl = rd.u8(); ent.actors.push({ id, e, n, fl }); }
}

// ---------------------------------------------------------------- toasts
function toast(m) { const d = document.createElement('div'); d.className = 'note ' + (m.kind || 'info'); d.textContent = m.text; $('notes').appendChild(d); while ($('notes').children.length > 6) $('notes').firstChild.remove(); setTimeout(() => d.remove(), (m.seconds || 6) * 1000); }

// ---------------------------------------------------------------- HUD
function renderHud() {
  const s = S; if (!s) return;
  const jur = s.jurisdiction.unknown ? '' : (s.jurisdiction.own ? '<span class="own">eigenes Gebiet</span>' : '<span class="foreign">fremdes Gebiet</span>');
  $('topbar').innerHTML = `<span>${esc(s.corps)} · ${esc(s.rank)}</span><span><b>${esc(s.clock)}</b> ×${s.timeScale}</span><span>${esc(s.place || '—')} ${jur}</span>`;
  const o = s.offered, a = s.active, off = $('offer'), inc = $('incident');
  if (o) { off.classList.remove('hidden'); off.innerHTML = `<h3 style="color:${o.urgency >= 3 ? 'var(--bad)' : 'var(--warn)'}">Funkspruch · ${esc(o.type)}${o.urgency >= 3 ? ' (dringend)' : ''}</h3><div>${esc(o.text)}</div><div class="dim small" style="margin:6px 0">${esc(o.address)}, ${esc(o.muni)} · noch ${Math.ceil(o.left)} s</div><div class="row"><button class="good" id="bAcc">Annehmen [Enter]</button><button id="bDec">Ablehnen [Rück]</button></div>`; $('bAcc').onclick = () => send({ t: 'accept' }); $('bDec').onclick = () => send({ t: 'decline' }); } else off.classList.add('hidden');
  if (a && !o) { inc.classList.remove('hidden'); const st = a.state === 'OnScene' ? 'Vor Ort – [P] Rapport' : 'Anfahrt: ' + esc(a.address) + ', ' + esc(a.muni); inc.innerHTML = `<h3 style="color:var(--warn)">${esc(a.type)}</h3><div>${st}</div>`; } else inc.classList.add('hidden');
  const nv = s.nav, nb = $('navbanner'); if (nv.active && nv.next) { nb.classList.remove('hidden'); nb.innerHTML = `<div class="dist">${fmtKm(nv.next.dist)}</div><div><div style="font-size:18px;font-weight:600">${esc(nv.next.text)}</div><div class="dim small">Ziel: ${esc(nv.dest)} · ${(nv.remaining / 1000).toFixed(1)} km · ${Math.ceil(nv.eta / 60)} min</div></div>`; } else if (nv.computing) { nb.classList.remove('hidden'); nb.textContent = 'Route wird berechnet …'; } else nb.classList.add('hidden');
  const p = s.player, over = s.limit > 0 && p.speed > s.limit + 2;
  $('speedo').innerHTML = p.inVehicle ? `<div class="num ${over ? 'over' : ''}">${Math.round(p.speed)}<div class="small dim" style="text-align:right;font-size:14px;font-weight:400">km/h</div></div>${s.limit > 0 ? `<div class="sign">${s.limit}</div>` : ''}<div class="small dim" style="flex:1">${esc(s.street)}</div>` : `<div><b>Zu Fuss</b><div class="small dim">[E] ansprechen · [F] einsteigen<br>[R] Radar · [G] Anhalten<br>[Tab] Computer · [M] Karte</div></div>`;
  const rd = $('radar'); if (measure && performance.now() - lastMeasureAt < 25000) { rd.classList.remove('hidden'); rd.innerHTML = `<div class="kmh" style="color:${measure.violation ? 'var(--bad)' : 'var(--good)'}">${measure.kmh} <span style="font-size:16px" class="dim">km/h</span></div><div class="small">${esc(measure.plate)} · Limite ${measure.limit}${measure.violation ? ' · +' + measure.excess + ' km/h' + (measure.fine ? ' · ' + measure.fine + ' CHF' : '') : ''}</div>`; } else rd.classList.add('hidden');
  $('crosshair').classList.toggle('hidden', !(s.radar.on && started));
  const pu = $('pursuit'); if (s.pursuit.active) { pu.classList.remove('hidden'); pu.innerHTML = `<b>VERFOLGUNG</b> · Verhältnismässigkeit beachten<div class="bar" style="margin-top:6px"><i style="width:${Math.min(100, s.pursuit.risk * 25)}%;background:var(--warn)"></i></div><div class="row" style="margin-top:8px"><button id="bHeli">Helikopter</button><button id="bBlock" ${s.pursuit.roadblockReady ? '' : 'disabled'}>Strassensperre</button><button id="bAbort" class="bad">Abbrechen</button></div>`; $('bHeli').onclick = () => send({ t: 'pursuit', op: 'heli' }); $('bBlock').onclick = () => send({ t: 'pursuit', op: 'roadblock' }); $('bAbort').onclick = () => send({ t: 'pursuit', op: 'abort' }); } else pu.classList.add('hidden');
  if (s.career && s.career.suspended && curWin !== 'suspended') openWin('suspended'); if (s.career && s.career.dismissed && curWin !== 'gameover') openWin('gameover');
  drawMinimap();
}

// ---------------------------------------------------------------- map drawing
const tileCache = new Map();
function tileImg(url) { let t = tileCache.get(url); if (!t) { t = { img: new Image(), ok: false, miss: false }; t.img.onload = () => { t.ok = true; }; t.img.onerror = () => { t.miss = true; }; t.img.src = url; tileCache.set(url, t); if (tileCache.size > 400) tileCache.delete(tileCache.keys().next().value); } return t; }
function drawMap(ctx, w, h, cE, cN, mpp, opt) {
  ctx.fillStyle = '#e4e2da'; ctx.fillRect(0, 0, w, h);
  const hw = w / 2 * mpp, hh = h / 2 * mpp, toX = e => w / 2 + (e - cE) / mpp, toY = n => h / 2 - (n - cN) / mpp;
  const macro = mpp > 7, bs = macro ? 8000 : 1000, x0 = Math.floor((cE - hw) / bs), x1 = Math.floor((cE + hw) / bs), y0 = Math.floor((cN - hh) / bs), y1 = Math.floor((cN + hh) / bs);
  for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++) {
    const t = tileImg(`/map/${macro ? 'macro' : 'tile'}/${macro ? x : x}/${y}.png`); if (!t.ok) continue; const px = toX(x * bs), py = toY((y + 1) * bs), sz = bs / mpp;
    ctx.save(); ctx.translate(px, py + sz); ctx.scale(1, -1); ctx.drawImage(t.img, 0, 0, sz + .6, sz + .6); ctx.restore();
  }
  if (opt.pois) for (const p of pois) { const x = toX(p.e), y = toY(p.n); if (x < -10 || y < -10 || x > w + 10 || y > h + 10) continue; ctx.fillStyle = { Police: '#3385f2', Hospital: '#f24d47', Fuel: '#fac238', FireStation: '#ff8c26', Station: '#a33' }[p.kind] || '#4dcc73'; ctx.beginPath(); ctx.arc(x, y, 5, 0, 7); ctx.fill(); ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.stroke(); if (mpp < 5 && p.kind !== 'Parking') { ctx.fillStyle = '#222'; ctx.font = '11px sans-serif'; ctx.fillText(p.name, x + 8, y + 4); } }
  if (route.length > 3) { ctx.strokeStyle = '#3385f2'; ctx.lineWidth = 5; ctx.lineJoin = 'round'; ctx.beginPath(); for (let i = 0; i < route.length; i += 2) { const x = toX(route[i]), y = toY(route[i + 1]); i ? ctx.lineTo(x, y) : ctx.moveTo(x, y); } ctx.stroke(); }
  if (opt.entities) { ctx.fillStyle = '#555'; for (const c of ent.cars) { const x = toX(c.e), y = toY(c.n); ctx.save(); ctx.translate(x, y); ctx.rotate(c.yaw * Math.PI / 180); ctx.fillStyle = c.v < .3 ? '#c33' : '#444'; ctx.fillRect(-2.2, -4.5, 4.4, 9); ctx.restore(); } ctx.fillStyle = '#e89'; for (const p of ent.peds) { ctx.beginPath(); ctx.arc(toX(p.e), toY(p.n), 1.8, 0, 7); ctx.fill(); } for (const a of ent.actors) { ctx.fillStyle = '#f90'; ctx.beginPath(); ctx.arc(toX(a.e), toY(a.n), 4, 0, 7); ctx.fill(); } }
  const inc = S && (S.active || S.offered); if (inc && inc.e) { const x = toX(inc.e), y = toY(inc.n); ctx.fillStyle = '#f24d47'; ctx.beginPath(); ctx.arc(x, y, 8, 0, 7); ctx.fill(); ctx.strokeStyle = '#fff'; ctx.lineWidth = 2; ctx.stroke(); }
  if (opt.pin) { const x = toX(opt.pin.e), y = toY(opt.pin.n); ctx.fillStyle = '#ff8c26'; ctx.beginPath(); ctx.arc(x, y, 7, 0, 7); ctx.fill(); ctx.strokeStyle = '#fff'; ctx.stroke(); }
  if (S) { const p = S.player; const pe = p.inVehicle ? p.vE : p.e, pn = p.inVehicle ? p.vN : p.n; ctx.save(); ctx.translate(toX(pe), toY(pn)); ctx.rotate(((p.inVehicle ? p.vYaw : p.yaw) || 0) * Math.PI / 180); ctx.fillStyle = '#fff'; ctx.strokeStyle = '#1558b8'; ctx.lineWidth = 2.5; ctx.beginPath(); ctx.moveTo(0, -11); ctx.lineTo(8, 9); ctx.lineTo(0, 4); ctx.lineTo(-8, 9); ctx.closePath(); ctx.fill(); ctx.stroke(); ctx.restore(); }
  return { toX, toY, toE: x => cE + (x - w / 2) * mpp, toN: y => cN - (y - h / 2) * mpp };
}
function drawMinimap() { if (!S) return; const c = $('minimap'), ctx = c.getContext('2d'); const p = S.player; drawMap(ctx, c.width, c.height, p.inVehicle ? p.vE : p.e, p.inVehicle ? p.vN : p.n, document.body.classList.contains('bigmap') ? 1.0 : 0.8, { entities: !embedded }); }

// ---------------------------------------------------------------- windows
function openWin(name) { curWin = name; refreshWin(); send({ t: 'ui', focus: true }); }
function closeWin() { if (!curWin) return; curWin = null; $('win').classList.add('hidden'); $('win').innerHTML = ''; send({ t: 'ui', focus: false }); }
function refreshWin() {
  const w = $('win'); const f = WINDOWS[curWin]; if (!f) { closeWin(); return; }
  const keep = document.activeElement && document.activeElement.id ? [document.activeElement.id, document.activeElement.selectionStart] : null;
  w.classList.remove('hidden'); if (!(curWin === 'encounter' && w.firstChild)) { w.innerHTML = ''; w.appendChild(f()); } if (keep) { const e = $(keep[0]); if (e) { e.focus(); try { e.setSelectionRange(keep[1], keep[1]); } catch (_) { } } }
}
function win(title, bodyHtml, footerHtml, width, onClose) { const d = document.createElement('div'); d.className = 'window'; d.style.width = (width || 760) + 'px'; d.innerHTML = `<header><span>${title}</span>${onClose === false ? '' : '<button id="wClose">✕</button>'}</header><div class="body">${bodyHtml}</div><footer>${footerHtml || ''}</footer>`; setTimeout(() => { const b = $('wClose'); if (b) b.onclick = closeWin; }, 0); return d; }

const WINDOWS = {
  encounter() {
    if (!enc) return win('Kontakt', 'Kein Kontakt.', '', 400);
    const quick = ['Guten Tag. Allgemeine Verkehrskontrolle. Ihre Papiere bitte.', 'Wissen Sie, weshalb ich Sie angehalten habe?', 'Haben Sie heute Alkohol getrunken?', 'Bitte steigen Sie aus dem Fahrzeug.', 'Ich muss Sie wegen der Geschwindigkeit büssen.', 'Danke für Ihre Kooperation. Gute Fahrt.', 'Beruhigen Sie sich bitte, ich erkläre Ihnen alles.', 'Was ist passiert?'];
    const acts = [['reason', 'Grund erklären'], ['docs', 'Papiere verlangen'], ['breath', 'Atemalkoholtest'], ['stepout', 'Aussteigen lassen'], ['searchP', 'Person durchsuchen'], ['searchV', 'Fahrzeug durchsuchen'], ['queryPlate', 'Computer: Kennzeichen'], ['queryPerson', 'Computer: Person'], ['rights', 'Rechte eröffnen'], ['warn', 'Verwarnen'], ['cuff', 'Fesseln'], ['arrest', 'Festnehmen'], ['report', 'Verzeigen (Rapport)'], ['release', 'Gehen lassen']];
    const html = `<div style="display:grid;grid-template-columns:1.25fr 1fr;gap:14px"><div class="col"><div class="transcript" id="tr"></div><div class="row"><input id="say" placeholder="Etwas sagen …" autocomplete="off"><button id="saybtn">Sagen</button><button id="mic" title="Spracheingabe">🎤</button></div><div class="small dim">Schnellsätze</div><div class="grid2">${quick.map((q, i) => `<button data-q="${i}" class="small">${esc(q.length > 46 ? q.slice(0, 44) + '…' : q)}</button>`).join('')}</div></div><div class="col"><div class="card" id="idcard"></div><div class="row small dim">Stimmung<div class="bar" style="flex:1"><i id="anger" style="background:var(--warn)"></i></div></div><div class="grid2">${acts.map(a => `<button data-op="${a[0]}">${a[1]}</button>`).join('')}</div><div class="row"><span class="dim">Busse CHF</span><button id="fm">−</button><b id="fv" style="min-width:48px;text-align:center">0</b><button id="fp">+</button><button class="good" id="fgo" style="flex:1">Ordnungsbusse</button></div></div></div>`;
    const d = win(esc(enc.kind === 'TrafficStop' ? 'Verkehrskontrolle' : 'Personenkontrolle') + ' – ' + esc(enc.name), html, '<button id="hide">Fenster ausblenden</button><button class="warn" id="endenc">Kontakt beenden</button>', 1060, false);
    setTimeout(() => {
      $('saybtn').onclick = sayIt; $('say').onkeydown = e => { if (e.key === 'Enter') sayIt(); e.stopPropagation(); };
      d.querySelectorAll('[data-q]').forEach(b => b.onclick = () => send({ t: 'say', text: quick[+b.dataset.q] })); d.querySelectorAll('[data-op]').forEach(b => b.onclick = () => send({ t: 'enc', op: b.dataset.op }));
      window._fine = enc.expectedFine || 0; $('fv').textContent = window._fine; $('fm').onclick = () => { window._fine = Math.max(0, window._fine - 10); $('fv').textContent = window._fine; }; $('fp').onclick = () => { window._fine += 10; $('fv').textContent = window._fine; }; $('fgo').onclick = () => send({ t: 'enc', op: 'fine', fine: window._fine });
      $('hide').onclick = closeWin; $('endenc').onclick = () => { send({ t: 'enc', op: 'end' }); closeWin(); }; $('mic').onclick = toggleMic; updateEncounter(); $('say').focus();
    }, 0); return d;
  },
  computer() {
    const reasons = ['Verkehrskontrolle / Verdacht auf Verkehrsdelikt', 'Verdacht auf Straftat', 'Fahndung / Vermisstensuche', 'Unfallaufnahme', 'Aus privatem Interesse (kein Dienstzweck)'];
    const html = `<div class="row"><button class="toggle on" id="tabP">Kennzeichen</button><button class="toggle" id="tabN">Person</button><button class="toggle" id="tabL">Protokoll</button></div><div id="cform"></div><div id="cres"></div>`;
    const d = win('Polizeicomputer – Abfragen werden protokolliert (Zweckbindung)', html, '<button id="wc">Schliessen</button>', 860); let tab = 'plate', reason = 0;
    const form = () => { const f = $('cform'); if (tab === 'log') { send({ t: 'computerLog' }); f.innerHTML = ''; return; } f.innerHTML = (tab === 'plate' ? '<div class="row"><label style="width:110px">Kennzeichen</label><input id="cplate" placeholder="BE 123456"></div>' : '<div class="grid3"><input id="cfirst" placeholder="Vorname"><input id="clast" placeholder="Nachname"><input id="cbirth" placeholder="Geburtsdatum (optional)"></div>') + '<div class="small dim" style="margin-top:8px">Abfragegrund (rechtlich relevant)</div><div class="grid2">' + reasons.map((r, i) => `<button data-r="${i}" class="toggle ${i === reason ? 'on' : ''}">${esc(r)}</button>`).join('') + '</div><div class="row"><button class="good" id="crun">Abfrage ausführen</button></div>'; f.querySelectorAll('[data-r]').forEach(b => b.onclick = () => { reason = +b.dataset.r; f.querySelectorAll('[data-r]').forEach(x => x.classList.toggle('on', +x.dataset.r === reason)); }); $('crun').onclick = () => send({ t: 'computer', kind: tab, plate: ($('cplate') || {}).value, first: ($('cfirst') || {}).value, last: ($('clast') || {}).value, birth: ($('cbirth') || {}).value, reason }); const i = f.querySelector('input'); if (i) i.focus(); f.querySelectorAll('input').forEach(x => x.onkeydown = e => e.stopPropagation()); };
    setTimeout(() => { $('wc').onclick = closeWin; const tabs = { tabP: 'plate', tabN: 'person', tabL: 'log' }; for (const k in tabs) $(k).onclick = () => { tab = tabs[k]; for (const j in tabs) $(j).classList.toggle('on', j === k); $('cres').innerHTML = ''; form(); }; form(); }, 0); return d;
  },
  map() {
    const d = win('Karte & Navigation', `<div style="display:grid;grid-template-columns:300px 1fr;gap:12px"><div class="col"><input id="msearch" placeholder="Suche (Ort, Station, Spital …)"><div id="mres" style="max-height:260px;overflow:auto" class="col"></div><div class="card small" id="msel">Rechtsklick/Doppelklick: Ziel setzen</div><button class="good" id="mgo" disabled>Route dorthin starten</button><div class="grid2"><button id="mme">Zu mir</button><button id="mclr">Route löschen</button><button id="mpol">Nächste Polizei</button><button id="mhos">Nächstes Spital</button></div></div><canvas id="bigmap" width="1000" height="640" style="width:100%;border-radius:8px;border:1px solid var(--line)"></canvas></div>`, '<button id="wc">Schliessen</button>', 1400);
    setTimeout(() => initBigMap(), 0); return d;
  },
  report() {
    const a = S && S.active; if (!a || a.state !== 'OnScene') return win('Rapport', '<p>Es gibt keinen Einsatz vor Ort, für den ein Rapport zu verfassen wäre. Kontrollen werden direkt im Kontaktfenster verzeigt.</p>', '<button id="wc">Schliessen</button>', 620, true, setTimeout(() => $('wc').onclick = closeWin, 0));
    let fault = -1; const d = win('Rapport – ' + esc(a.type), `<div class="dim small">${esc(a.address)}, ${esc(a.muni)}</div>${a.roles.length ? '<div class="small dim" style="margin-top:8px">Hauptverantwortliche/r (laut Ihren Erkenntnissen)</div><div class="grid3" id="roles">' + a.roles.map(r => `<button class="toggle" data-i="${r.i}">${esc(r.role)}: ${esc(r.name)}</button>`).join('') + '<button class="toggle on" data-i="-1">Niemand / unklar</button></div>' : ''}<div class="row" style="margin-top:10px"><label><input type="checkbox" id="rinj" style="width:auto"> Verletzte festgehalten</label><label><input type="checkbox" id="ramb" style="width:auto"> Rettungsdienst aufgeboten</label><label><input type="checkbox" id="rtow" style="width:auto"> Abschleppdienst aufgeboten</label></div><div class="small dim">Sachverhalt (Ihre Darstellung der Ereignisse)</div><textarea id="rnarr" rows="7"></textarea><div class="small dim" style="margin-top:6px">Wahrheitsgemäss, sachlich, vollständig – falsche Angaben im Rapport sind strafbar (Art. 317 StGB) und werden im Spiel erkannt.</div><div class="row" style="margin-top:10px"><span class="dim small">Services:</span><button data-s="ems">Rettung 144</button><button data-s="tow">Abschleppdienst</button><button data-s="fire">Feuerwehr 118</button><button data-s="backup">Verstärkung</button></div>`, '<button id="wc">Abbrechen</button><button class="good" id="rsend">Rapport abgeben</button>', 860);
    setTimeout(() => { $('wc').onclick = closeWin; $('rnarr').onkeydown = e => e.stopPropagation(); d.querySelectorAll('[data-i]').forEach(b => b.onclick = () => { fault = +b.dataset.i; d.querySelectorAll('[data-i]').forEach(x => x.classList.toggle('on', x === b)); }); d.querySelectorAll('[data-s]').forEach(b => b.onclick = () => send({ t: 'service', which: b.dataset.s })); $('rsend').onclick = () => { if ($('rnarr').value.trim().length < 25) { toast({ kind: 'warn', text: 'Der Sachverhalt ist zu kurz.' }); return; } send({ t: 'report', atFault: fault, injured: $('rinj').checked, amb: $('ramb').checked, tow: $('rtow').checked, narrative: $('rnarr').value }); closeWin(); }; }, 0); return d;
  },
  career() { send({ t: 'career' }); return win('Laufbahn', '<div id="cbody">…</div>', '<button id="wc">Schliessen</button>', 860, true, setTimeout(() => $('wc').onclick = closeWin, 0)); },
  notebook() { send({ t: 'notebook' }); return win('Notizbuch – Fälle & Funkverlauf', '<div id="nbody">…</div>', '<button id="wc">Schliessen</button>', 900, true, setTimeout(() => $('wc').onclick = closeWin, 0)); },
  pause() {
    const slots = (W ? W.slots : []).map(s => `<div class="row"><span style="flex:1" class="small">${s.slot}: ${esc(s.info)}</span><button data-sv="${s.slot}">Speichern</button><button data-ld="${s.slot}" ${s.exists ? '' : 'disabled'}>Laden</button></div>`).join('');
    const d = win('Pause', `<div class="col"><button class="good" id="resume">Weiterspielen</button><div class="dim small">Spielstände</div>${slots}<div class="row"><span>Zeitraffer ×<b id="tsv">${S ? S.timeScale : 12}</b></span><input type="range" id="ts" min="1" max="60" value="${S ? S.timeScale : 12}"></div><div class="row"><label><input type="checkbox" id="tts" style="width:auto" ${voice.tts ? 'checked' : ''}> Sprachausgabe</label><label><input type="checkbox" id="stt" style="width:auto" ${voice.stt ? 'checked' : ''}> Spracheingabe (Mikro)</label></div><div class="row"><label style="white-space:nowrap"><input type="checkbox" id="llm" style="width:auto" ${S && S.llm && S.llm.enabled ? 'checked' : ''}> KI-Formulierung</label><input id="llmkey" type="password" placeholder="Anthropic-API-Schlüssel (optional, nur im Speicher)" autocomplete="off"></div><div class="small dim" id="llmstat">${S && S.llm ? esc(S.llm.status) : ''}</div></div>`, '<button id="hlp">Hilfe / Steuerung</button><button class="bad" id="menuBtn">Hauptmenü</button>', 520, false);
    setTimeout(() => { $('resume').onclick = () => { send({ t: 'pause', on: false }); closeWin(); }; $('hlp').onclick = () => openWin('help'); $('menuBtn').onclick = () => { send({ t: 'pause', on: false }); started = false; $('hud').classList.add('hidden'); closeWin(); showMenu(); }; $('ts').oninput = e => { $('tsv').textContent = e.target.value; send({ t: 'timescale', v: +e.target.value }); }; $('tts').onchange = e => voice.tts = e.target.checked; $('stt').onchange = e => voice.stt = e.target.checked; const llmSend = () => send({ t: 'llm', enabled: $('llm').checked, key: $('llmkey').value }); $('llm').onchange = llmSend; $('llmkey').onchange = llmSend; $('llmkey').onkeydown = e => e.stopPropagation(); d.querySelectorAll('[data-sv]').forEach(b => b.onclick = () => { send({ t: 'save', slot: +b.dataset.sv }); setTimeout(() => send({ t: 'hello', role: embedded ? 'ui' : 'sandbox' }), 400); }); d.querySelectorAll('[data-ld]').forEach(b => b.onclick = () => { send({ t: 'load', slot: +b.dataset.ld }); }); }, 0); return d;
  },
  help() {
    return win('Steuerung & Hinweise', `<div class="grid2 small"><div><b>ZU FUSS</b><br>W A S D gehen · Shift rennen · Maus umsehen<br>C Kamera · V Ego-Ansicht<br>F Fahrzeug betreten/verlassen<br>E Person/Fahrer ansprechen<br>R Radarpistole · G Fahrzeug anhalten<br><br><b>IM FAHRZEUG</b><br>W/S Gas/Bremse · A/D lenken · Leertaste Handbremse<br>B Blaulicht · Q Sirene · H Hupe · L Licht<br>Z/X Blinker · J Warnblinker</div><div><b>OBERFLÄCHE</b><br>Tab Polizeicomputer · M Karte/Navigation<br>P Rapport · N Notizbuch · K Laufbahn<br>Enter/Rück Funkauftrag annehmen/ablehnen<br>F5/F9 Schnellspeichern/-laden · Esc Pause<br><br><b>GRUNDSÄTZE</b><br>Handeln Sie rechtmässig und verhältnismässig. Jede Handlung wird gegen hinterlegte Normen geprüft; Verstösse haben Konsequenzen von der Ermahnung bis zur Entlassung. Gesetzestexte stammen aus Fedlex; kantonales Recht ist als ‹nicht verifiziert› gekennzeichnet.</div></div>`, '<button id="wc">Zurück</button>', 920, true, setTimeout(() => $('wc').onclick = () => { if (S && S.paused) openWin('pause'); else closeWin(); }, 0));
  },
  suspended() {
    const left = S && S.career ? S.career.suspendedLeft : 0, h = Math.floor(left / 3600), dd = Math.floor(h / 24);
    return win('Vom Dienst suspendiert', `<p style="font-size:17px">Sie wurden wegen eines schweren Verstosses im Dienst eingestellt, bis die Strafuntersuchung geklärt ist. Während der Suspendierung dürfen Sie keine polizeilichen Handlungen vornehmen.</p><p>Verbleibende Dauer: ${dd} Tage ${h % 24} Std. (Spielzeit)</p>`, '<button id="skip" class="good">Zeit überspringen</button>', 720, false, setTimeout(() => $('skip').onclick = () => { send({ t: 'skip' }); closeWin(); }, 0));
  },
  gameover() {
    return win('Dienstenthebung', '<p style="font-size:18px">Ihre Anstellung wurde aufgelöst.</p><p>Grund: wiederholte oder schwere Verstösse gegen Recht und Dienstpflichten.</p>' + (S && S.career ? `<p class="dim">Bilanz: ${S.career.fines} Bussen · ${S.career.arrests} Festnahmen</p>` : ''), '<button id="gmenu" class="good">Hauptmenü</button>', 720, false, setTimeout(() => $('gmenu').onclick = () => { started = false; $('hud').classList.add('hidden'); closeWin(); showMenu(); }, 0));
  }
};

function sayIt() { const i = $('say'); if (i && i.value.trim()) { send({ t: 'say', text: i.value.trim() }); i.value = ''; } }
function updateEncounter() {
  if (!enc || curWin !== 'encounter') return; const tr = $('tr'); if (!tr) return; const atBottom = tr.scrollTop + tr.clientHeight >= tr.scrollHeight - 30;
  tr.innerHTML = enc.transcript.map(l => `<div class="l ${l.warn ? 'w' : l.sys ? 's' : l.officer ? 'o' : ''}">${l.sys ? '' : '<b>' + esc(l.officer ? 'Sie' : l.who) + ':</b> '}${esc(l.text)}</div>`).join(''); if (atBottom) tr.scrollTop = tr.scrollHeight;
  const c = enc.card; $('idcard').innerHTML = '<b style="color:var(--accent)">Ausweis / Papiere</b><br>' + (c ? `${esc(c.name)}<br>${esc(c.birth)} · ${esc(c.nat)}<br>${esc(c.address)}<br>Führerausweis ${esc(c.licence)}<br>${esc(c.vehicle)}<br><span class="dim">Halter: ${esc(c.holder)}</span>` : '<span class="dim">Noch keine Papiere kontrolliert.</span>');
  $('anger').style.width = Math.round(enc.anger * 100) + '%'; $('anger').style.background = enc.anger > .6 ? 'var(--bad)' : 'var(--warn)';
}

function showComputer(m) { const r = $('cres'); if (!r) return; r.innerHTML = `<pre class="mono">${esc(m.title)}\n\n${m.lines.map(esc).join('\n')}${m.problems.length ? '\n\nRECHTLICH PROBLEMATISCH: ' + m.problems.map(esc).join('; ') : ''}</pre>`; }
function showComputerLog(items) { const r = $('cres'); if (r) r.innerHTML = `<pre class="mono">${items.map(q => `${q.time}  ${q.kind}: ${esc(q.subject)} · ${q.reason ? esc(q.reason) : 'OHNE Zweck'}${q.lawful ? '' : '   [UNRECHTMÄSSIG]'}`).join('\n') || 'Keine Abfragen.'}</pre>`; }
function showCareer(m) {
  const b = $('cbody'); if (!b || !S || !S.career) return; const c = S.career, bar = (n, v) => `<div class="row"><span style="width:170px">${n}</span><div class="bar" style="flex:1"><i style="width:${v}%;background:${v < 45 ? 'var(--bad)' : v < 65 ? 'var(--warn)' : 'var(--good)'}"></i></div><span class="dim" style="width:40px;text-align:right">${Math.round(v)}</span></div>`;
  b.innerHTML = `<div style="font-size:22px;font-weight:700">${esc(m.rank)} · ${esc(m.corps)}</div><div class="dim small">Erfahrung ${Math.round(c.xp)} / ${Math.round(c.nextXp)}</div><div class="bar" style="margin:4px 0 10px"><i style="width:${Math.min(100, c.xp / Math.max(1, c.nextXp) * 100)}%"></i></div><div class="row"><button class="good" id="promo" ${c.canPromote ? '' : 'disabled'}>Beförderung beantragen</button></div>${bar('Rechtmässigkeit', c.legality)}${bar('Professionalität', c.prof)}${bar('Deeskalation', c.deesc)}${bar('Berichtswesen', c.reporting)}${bar('Bürgerkontakt', c.community)}<div class="small dim" style="margin:8px 0">Beschwerden offen: ${c.complaints} · Untersuchungen: ${c.investigations} · Disziplinarisch: ${c.disciplinary} · Bussen: ${c.fines} · Festnahmen: ${c.arrests}</div><b>Konsequenzen</b><div class="card small" style="max-height:220px;overflow:auto">${m.history.map(h => `<div style="color:var(--warn)">${esc(h)}</div>`).join('') || '<span class="dim">Keine.</span>'}</div>`;
  const p = $('promo'); if (p) p.onclick = () => send({ t: 'promote' });
}
function showNotebook(m) { const b = $('nbody'); if (b) b.innerHTML = `<div class="grid2"><div><b>Fälle</b><div class="card small" style="max-height:420px;overflow:auto">${m.cases.slice().reverse().map(c => `<div style="margin:4px 0;color:${c.Correct ? 'var(--text)' : 'var(--warn)'}">${esc(c.Kind)}: ${esc(c.Summary)}${c.Correct ? '' : ' (Mängel)'}</div>`).join('') || '<span class="dim">Noch keine Fälle.</span>'}</div></div><div><b>Meldungen</b><div class="card small dim" style="max-height:420px;overflow:auto">${m.log.slice().reverse().map(esc).join('<br>')}</div></div></div>`; }

// ---------------------------------------------------------------- big map window
const mapSearch = { items: [] }; let big = null;
function initBigMap() {
  $('wc').onclick = closeWin; const cv = $('bigmap'); const ctx = cv.getContext('2d'); const p = S ? S.player : { e: W.centerE, n: W.centerN, vE: W.centerE, vN: W.centerN };
  big = { cE: p.inVehicle ? p.vE : p.e, cN: p.inVehicle ? p.vN : p.n, mpp: 3, pin: null, sel: null, drag: null, tr: null }; send({ t: 'poi', e: big.cE, n: big.cN, r: 4000 });
  const draw = () => { if (curWin !== 'map') return; big.tr = drawMap(ctx, cv.width, cv.height, big.cE, big.cN, big.mpp, { pois: true, pin: big.pin || big.sel, entities: !embedded }); requestAnimationFrame(draw); }; draw();
  const pos = e => { const r = cv.getBoundingClientRect(); return [(e.clientX - r.left) * cv.width / r.width, (e.clientY - r.top) * cv.height / r.height]; };
  const setPin = (x, y) => { big.pin = { e: big.tr.toE(x), n: big.tr.toN(y), name: 'Kartenpunkt' }; big.sel = null; selUpdate(); };
  cv.onmousedown = e => { if (e.button === 0) big.drag = pos(e); }; window.onmouseup = () => big && (big.drag = null);
  cv.onmousemove = e => { if (!big.drag) return; const [x, y] = pos(e); big.cE -= (x - big.drag[0]) * big.mpp; big.cN += (y - big.drag[1]) * big.mpp; big.drag = [x, y]; };
  cv.ondblclick = e => { const [x, y] = pos(e); setPin(x, y); }; cv.oncontextmenu = e => { e.preventDefault(); const [x, y] = pos(e); setPin(x, y); };
  cv.onwheel = e => { e.preventDefault(); big.mpp = Math.max(.6, Math.min(900, big.mpp * (e.deltaY > 0 ? 1.18 : 1 / 1.18))); if (big.mpp < 40) send({ t: 'poi', e: big.cE, n: big.cN, r: Math.min(6000, big.mpp * 600) }); };
  $('msearch').onkeydown = e => { e.stopPropagation(); }; $('msearch').oninput = e => send({ t: 'search', text: e.target.value });
  $('mgo').onclick = () => { const t = big.sel || big.pin; if (t) { send({ t: 'route', e: t.e, n: t.n, name: t.name }); closeWin(); } }; $('mclr').onclick = () => { send({ t: 'routeClear' }); route = []; };
  $('mme').onclick = () => { big.cE = p.inVehicle ? p.vE : p.e; big.cN = p.inVehicle ? p.vN : p.n; };
  const nearest = kind => { const q = pois.filter(x => x.kind === kind).sort((a, b) => ((a.e - big.cE) ** 2 + (a.n - big.cN) ** 2) - ((b.e - big.cE) ** 2 + (b.n - big.cN) ** 2))[0]; if (q) { big.sel = q; big.cE = q.e; big.cN = q.n; big.mpp = 3; selUpdate(); } }; $('mpol').onclick = () => { send({ t: 'poi', e: big.cE, n: big.cN, r: 6000 }); setTimeout(() => nearest('Police'), 300); }; $('mhos').onclick = () => { send({ t: 'poi', e: big.cE, n: big.cN, r: 6000 }); setTimeout(() => nearest('Hospital'), 300); };
  window.selUpdate = () => { const t = big.sel || big.pin; $('msel').innerHTML = t ? `<b>${esc(t.name)}</b><br><span class="dim">${esc(t.kind || '')}</span>` : 'Rechtsklick/Doppelklick: Ziel setzen'; $('mgo').disabled = !t; };
}
function renderMapResults() { const r = $('mres'); if (!r) return; r.innerHTML = mapSearch.items.map((p, i) => `<button class="small" data-i="${i}">${esc(p.name)} · ${esc(p.kind)}</button>`).join(''); r.querySelectorAll('button').forEach(b => b.onclick = () => { const p = mapSearch.items[+b.dataset.i]; big.sel = p; big.cE = p.e; big.cN = p.n; big.mpp = 3; selUpdate(); }); }

// ---------------------------------------------------------------- menu
function showMenu() {
  const m = $('menu'); m.classList.remove('hidden'); let sel = 'KAPO_BE', muni = false;
  const draw = () => {
    const list = W.corps.filter(c => (c.level === 'Municipal') === muni);
    m.innerHTML = `<h1>Swiss Police Simulator</h1><div class="dim" style="font-size:20px;margin-bottom:6vh">Realistische Polizeisimulation · Recht nach Fedlex · Pilotgebiet: ${esc(W.world)}</div><div class="stripe"></div><div style="padding-top:3vh"><div class="row"><span class="dim">Korps wählen</span><button id="mtoggle">${muni ? 'Stadtpolizeien' : 'Kantonspolizeien'}</button></div><div class="corps">${list.map(c => `<button class="toggle ${c.id === sel ? 'on' : ''}" data-id="${c.id}">${esc(c.name)}</button>`).join('')}</div><div class="row" style="margin-top:14px"><button class="good" id="mnew" style="font-size:18px;padding:12px 28px">Neues Spiel</button>${W.slots.filter(s => s.exists).map(s => `<button data-ld="${s.slot}">Laden ${s.slot}: ${esc(s.info)}</button>`).join('')}</div></div><div class="dim small" style="margin-top:auto">${esc(W.attribution)} · Gesetze: Fedlex</div>`;
    $('mtoggle').onclick = () => { muni = !muni; draw(); }; m.querySelectorAll('[data-id]').forEach(b => b.onclick = () => { sel = b.dataset.id; draw(); }); $('mnew').onclick = () => send({ t: 'new', corps: sel, seed: 1 + Math.floor(Math.random() * 9000) });
    m.querySelectorAll('[data-ld]').forEach(b => b.onclick = () => send({ t: 'load', slot: +b.dataset.ld }));
  }; draw();
}

// ---------------------------------------------------------------- voice
const voice = { tts: true, stt: false, rec: null };
function speak(m) { if (!voice.tts || !('speechSynthesis' in window)) return; const u = new SpeechSynthesisUtterance(m.text); u.lang = { de: 'de-CH', fr: 'fr-CH', it: 'it-CH' }[m.lang] || 'de-DE'; const vs = speechSynthesis.getVoices().filter(v => v.lang.startsWith(u.lang.slice(0, 2))); const g = vs.find(v => m.female ? /female|frau|katja|hedda|petra|marie/i.test(v.name) : /male|stefan|conrad|hans|claude/i.test(v.name)); if (g) u.voice = g; else if (vs[0]) u.voice = vs[0]; speechSynthesis.cancel(); speechSynthesis.speak(u); }
function toggleMic() { const R = window.SpeechRecognition || window.webkitSpeechRecognition; if (!R) { toast({ kind: 'warn', text: 'Spracherkennung wird von diesem Browser nicht unterstützt.' }); return; } if (voice.rec) { voice.rec.stop(); voice.rec = null; return; } const r = new R(); r.lang = enc ? { de: 'de-CH', fr: 'fr-CH', it: 'it-CH' }[enc.lang] || 'de-CH' : 'de-CH'; r.onresult = e => { const t = e.results[0][0].transcript; if ($('say')) $('say').value = t; sayIt(); }; r.onend = () => voice.rec = null; r.start(); voice.rec = r; }
let actx = null; function beep() { try { actx = actx || new (window.AudioContext || window.webkitAudioContext)(); const o = actx.createOscillator(), g = actx.createGain(); o.frequency.value = 1200; g.gain.value = .06; o.connect(g); g.connect(actx.destination); o.start(); o.stop(actx.currentTime + .12); } catch (_) { } }

// ---------------------------------------------------------------- keyboard + sandbox simulation (standalone mode)
const keys = {};
addEventListener('keydown', e => {
  if (!started) return; const tag = (e.target.tagName || '').toLowerCase(); if (tag === 'input' || tag === 'textarea') { if (e.key === 'Escape') e.target.blur(); return; }
  keys[e.key.toLowerCase()] = true; const k = e.key;
  if (curWin) { if (k === 'Escape') { if (curWin === 'pause') { send({ t: 'pause', on: false }); closeWin(); } else if (curWin === 'help' && S && S.paused) openWin('pause'); else if (['computer', 'map', 'career', 'notebook', 'report', 'help'].includes(curWin)) closeWin(); e.preventDefault(); } else if (k === 'Tab' && curWin === 'computer') { closeWin(); e.preventDefault(); } else if ((k === 'm' || k === 'M') && curWin === 'map') closeWin(); return; }
  if (k === 'Tab') { openWin('computer'); e.preventDefault(); } else if (k === 'm' || k === 'M') openWin('map'); else if (k === 'p' || k === 'P') openWin('report'); else if (k === 'n' || k === 'N') openWin('notebook'); else if (k === 'k' || k === 'K') openWin('career');
  else if (k === 'F1') { openWin('help'); e.preventDefault(); } else if (k === 'Escape') { send({ t: 'pause', on: true }); openWin('pause'); } else if (k === 'Enter' && S && S.offered) send({ t: 'accept' }); else if (k === 'Backspace' && S && S.offered) { send({ t: 'decline' }); e.preventDefault(); }
  else if (k === 'F5') { send({ t: 'save', slot: 1 }); e.preventDefault(); } else if (k === 'F9') { send({ t: 'load', slot: 1 }); e.preventDefault(); } else if (k === 'F8') document.body.classList.toggle('bigmap');
  if (!embedded) sim.key(k);
});
addEventListener('keyup', e => { keys[e.key.toLowerCase()] = false; });

const sim = {
  e: 0, n: 0, yaw: 0, speed: 0, vE: 0, vN: 0, vYaw: 0, inVeh: false, siren: false, blue: false, radar: false, t0: 0, sent: 0, cars: new Map(), propCars: [], actors: [],
  start(m) { this.e = m.e; this.n = m.n; this.vE = m.vE; this.vN = m.vN; this.vYaw = m.vYaw || 0; this.inVeh = !!m.inVehicle; this.yaw = m.pYaw || 0; this.speed = 0; if (!this.running) { this.running = true; this.t0 = performance.now(); requestAnimationFrame(t => this.loop(t)); } },
  onEntityMsg(m) { },
  key(k) { k = k.toLowerCase(); if (k === 'f') this.toggleVeh(); else if (k === 'b') this.blue = !this.blue; else if (k === 'q') this.siren = !this.siren; else if (k === 'r') { this.radar = !this.radar; send({ t: 'radar', on: this.radar }); } else if (k === 'g') { const c = this.aim(); send({ t: 'stop', vehicleId: c ? c.id : 0 }); } else if (k === 'e') this.talk(); else if (k === ' ') { if (this.inVeh) this.speed *= .6; } else if (k === 'x') { const c = this.aim(); if (this.radar && c) send({ t: 'measure', vehicleId: c.id, dist: Math.hypot(c.e - this.px(), c.n - this.py()) }); } },
  px() { return this.inVeh ? this.vE : this.e; }, py() { return this.inVeh ? this.vN : this.n; },
  toggleVeh() { if (this.inVeh) { this.inVeh = false; this.e = this.vE + Math.cos(this.vYaw * Math.PI / 180) * 2.5; this.n = this.vN - Math.sin(this.vYaw * Math.PI / 180) * 2.5; this.speed = 0; } else if (Math.hypot(this.vE - this.e, this.vN - this.n) < 12) { this.inVeh = true; } },
  aim() { const h = (this.inVeh ? this.vYaw : this.yaw) * Math.PI / 180, dx = Math.sin(h), dy = Math.cos(h); let best = null, bd = 1e9; for (const c of ent.cars) { const rx = c.e - this.px(), ry = c.n - this.py(), d = Math.hypot(rx, ry); if (d < 3 || d > 220) continue; const ang = Math.acos((rx * dx + ry * dy) / d) * 180 / Math.PI; if (ang > 9) continue; if (d < bd) { bd = d; best = c; } } return best; },
  talk() { let best = null, bd = 12; for (const c of ent.cars) { if (c.v > .3) continue; const d = Math.hypot(c.e - this.px(), c.n - this.py()); if (d < bd) { bd = d; best = { vehicleId: c.id }; } } for (const a of ent.actors) { const d = Math.hypot(a.e - this.px(), a.n - this.py()); if (d < bd) { bd = d; best = { actorId: a.id }; } } if (best) send(Object.assign({ t: 'approach' }, best)); },
  loop(t) {
    const dt = Math.min(.05, (t - (this.last || t)) / 1000); this.last = t;
    if (S && !S.paused && !curWin && !(S.career && S.career.suspended)) {
      if (this.inVeh) {
        const th = (keys['w'] ? 1 : 0) - (keys['s'] ? 1 : 0), st = (keys['d'] ? 1 : 0) - (keys['a'] ? 1 : 0); this.speed += (th > 0 ? 5 : th < 0 ? -8 : 0) * dt; this.speed -= Math.sign(this.speed) * Math.min(Math.abs(this.speed), (th === 0 ? 1.5 : 0) * dt); this.speed = Math.max(-8, Math.min(48, this.speed));
        if (keys[' ']) this.speed *= (1 - 2.5 * dt); this.vYaw += st * Math.min(1, Math.abs(this.speed) / 6) * Math.sign(this.speed || 1) * 55 * dt; const h = this.vYaw * Math.PI / 180; this.vE += Math.sin(h) * this.speed * dt; this.vN += Math.cos(h) * this.speed * dt; this.e = this.vE; this.n = this.vN; this.yaw = this.vYaw;
      } else {
        const mx = (keys['d'] ? 1 : 0) - (keys['a'] ? 1 : 0), my = (keys['w'] ? 1 : 0) - (keys['s'] ? 1 : 0), sp = keys['shift'] ? 5.5 : 1.6; if (mx || my) { this.yaw = Math.atan2(mx, my) * 180 / Math.PI; const l = Math.hypot(mx, my); this.e += mx / l * sp * dt; this.n += my / l * sp * dt; } this.speed = 0;
      }
    }
    if (t - this.sent > 100 && started) { this.sent = t; send({ t: 'pose', e: this.e, n: this.n, yaw: this.yaw, speed: Math.abs(this.speed) * 3.6, inVehicle: this.inVeh, siren: this.siren, blue: this.blue, vE: this.vE, vN: this.vN, vYaw: this.vYaw, fuel: 1, damage: 0 }); }
    if (!embedded) this.draw(); requestAnimationFrame(t2 => this.loop(t2));
  },
  draw() { const cv = $('sandbox'); if (cv.width !== innerWidth || cv.height !== innerHeight) { cv.width = innerWidth; cv.height = innerHeight; } if (!started || !S) return; const ctx = cv.getContext('2d'); drawMap(ctx, cv.width, cv.height, this.px(), this.py(), 1.0, { pois: false, entities: true }); if (this.radar) { ctx.strokeStyle = 'rgba(255,60,60,.5)'; ctx.beginPath(); const h = (this.inVeh ? this.vYaw : this.yaw) * Math.PI / 180; ctx.moveTo(cv.width / 2, cv.height / 2); ctx.lineTo(cv.width / 2 + Math.sin(h) * 220, cv.height / 2 - Math.cos(h) * 220); ctx.stroke(); } }
};
if (embedded) $('sandbox').classList.add('hidden');
connect();
