/* PerfTicker Linux · 行情终端前端
 * 数据来自 SSE（/api/stream），K 线由 tick 序列在前端聚合（对应原版 RebuildCandles）。
 */
'use strict';

// ─────────────────────────── 状态 ───────────────────────────
const S = {
  meta: null,
  metrics: [],          // [{code,name,group,unit,decimals,title}]
  byCode: {},
  series: {},           // code -> {ms:[], v:[]}
  latest: null,         // 最近一条 payload
  values: {},           // code -> 最新值
  shown: {},            // code -> 缓动值
  open: {},             // 今开
  high: {}, low: {},
  prev: {},             // 上一 tick
  sel: '000001',
  period: 2000,
  upRed: true,
  dark: true,
  paused: false,
  interval: 1000,
  ecoOn: true,
  ecoTotal: +(localStorage.getItem('pt_eco_total') || 0),
  ecoPeak: 0,
  ecoSec: 0,
  procs: [],
  procHist: new Map(),  // pid -> [cpu...]
  lastPids: new Set(),
  tape: [],
  procSort: { key: 'cpu', desc: true },
  search: '',
  marqueeX: 0,
};

const $ = (id) => document.getElementById(id);
const MAX_TICKS = 3600;
const PERIODS = [2000, 5000, 10000, 30000, 60000];
const INDEXES = ['000001', '000002', '600519', '600521', '000003', '600523', '600036', '000301'];
const INDEX_LABEL = {
  '000001': 'CPU综合', '000002': 'CPU负载', '600519': 'GPU频率',
  '600521': 'GPU负载', '000003': '内存占用', '600523': 'GPU温度',
  '600036': '网络下行', '000301': '进程数',
};

// ─────────────────────────── 格式化 ───────────────────────────
function fmt(v, dec = 2) {
  if (v == null || Number.isNaN(v)) return '—';
  const a = Math.abs(v);
  if (a >= 10000) return Math.round(v).toLocaleString('en-US');
  return v.toFixed(dec);
}
function deltaText(d, m) {
  if (d == null || Number.isNaN(d)) return '—';
  const sign = d >= 0 ? '+' : '-';
  const a = Math.abs(d);
  if (m.unit === '%') return sign + a.toFixed(2) + 'pt';
  if (m.unit === '') return sign + Math.round(a);
  return sign + a.toFixed(m.decimals <= 0 ? 0 : 2) + m.unit;
}
function chgClass(d) {
  if (d == null || Math.abs(d) < 1e-9) return 'flat';
  const up = d > 0;
  return (up === S.upRed) ? 'up' : 'down';
}
function colorOf(d) {
  if (d == null || Math.abs(d) < 1e-9) return '#9aa6b8';
  const up = d > 0;
  return (up === S.upRed) ? '#ff4d4f' : '#00c08b';
}
const cssVar = (n) => getComputedStyle(document.body).getPropertyValue(n).trim();
const MONO = '"LXGW WenKai Mono", ui-monospace, monospace';
const UI = '"LXGW WenKai", system-ui, sans-serif';

// ─────────────────────────── 启动 ───────────────────────────
async function boot() {
  const meta = await fetch('/api/meta').then(r => r.json());
  S.meta = meta;
  S.metrics = meta.metrics;
  S.metrics.forEach(m => { S.byCode[m.code] = m; });
  $('hostName').textContent = `${meta.host.node} · ${meta.host.cores} 核 ${meta.host.cpu_name.replace(/\(R\)|\(TM\)|CPU|@.*/g, '').trim()}`;

  const hist = await fetch('/api/history').then(r => r.json());
  for (const [code, ser] of Object.entries(hist.series)) {
    S.series[code] = ser;
    const last = ser.v.length ? ser.v[ser.v.length - 1] : null;
    if (last != null) { S.values[code] = last; S.shown[code] = last; }
    initOHLC(code, ser.v);
  }
  $('watchCount').textContent = `${S.metrics.length} 项`;
  buildWatch();
  buildIndices();
  buildEcoBars();
  bindUI();
  connect();
  requestAnimationFrame(frame);
  setInterval(tickClock, 1000);
  tickClock();
}

function initOHLC(code, arr) {
  if (!arr.length) return;
  const head = arr.slice(0, Math.min(60, arr.length));
  S.open[code] = head.reduce((a, b) => a + b, 0) / head.length;
  S.high[code] = Math.max(...arr);
  S.low[code] = Math.min(...arr);
}

// ─────────────────────────── SSE ───────────────────────────
let es = null, retry = 0;
function connect() {
  es = new EventSource('/api/stream');
  es.onopen = () => { retry = 0; setLive(true); };
  es.onmessage = (ev) => {
    let d;
    try { d = JSON.parse(ev.data); } catch { return; }
    onTick(d);
  };
  es.onerror = () => {
    setLive(false);
    es.close();
    retry = Math.min(retry + 1, 6);
    setTimeout(connect, 500 * retry);
  };
}
function setLive(on) {
  $('liveDot').classList.toggle('off', !on);
  $('liveText').textContent = on ? '实时采样' : '连接中断·重试中';
}

function onTick(d) {
  S.latest = d;
  S.interval = d.cfg.interval_ms;
  S.paused = d.cfg.paused;
  $('sampleCost').textContent = `${d.cfg.sample_ms.toFixed(1)}ms/帧`;
  $('btnPause').classList.toggle('on', S.paused);
  $('btnPause').textContent = S.paused ? '继续' : '暂停';
  $('liveText').textContent = S.paused ? '已暂停' : '实时采样';

  // 指标 tick 入列
  for (const m of S.metrics) {
    const v = d.values[m.code];
    if (v == null) continue;
    let ser = S.series[m.code];
    if (!ser) ser = S.series[m.code] = { ms: [], v: [] };
    ser.ms.push(d.ts);
    ser.v.push(v);
    if (ser.ms.length > MAX_TICKS) { ser.ms.shift(); ser.v.shift(); }
    S.prev[m.code] = S.values[m.code];
    S.values[m.code] = v;
    if (S.shown[m.code] == null) S.shown[m.code] = v;
    // 今开：前 60 个采样均值（与原版一致）
    const n = ser.v.length;
    if (n <= 60) {
      S.open[m.code] = ser.v.slice(-60).reduce((a, b) => a + b, 0) / Math.min(n, 60);
    }
    S.high[m.code] = Math.max(S.high[m.code] ?? v, v);
    S.low[m.code] = Math.min(S.low[m.code] ?? v, v);
  }

  S.procs = d.procs || [];
  updateProcHist();
  pushEvents(d);
  renderTick();
  if (S.ecoOn && !S.paused) stepEco(d);
}

function updateProcHist() {
  const alive = new Set();
  const top = S.procs.slice(0, 90);
  for (const p of top) {
    alive.add(p.pid);
    let a = S.procHist.get(p.pid);
    if (!a) { a = []; S.procHist.set(p.pid, a); }
    a.push(p.cpu);
    if (a.length > 40) a.shift();
  }
  if (S.procHist.size > 800) {
    for (const k of S.procHist.keys()) if (!alive.has(k)) S.procHist.delete(k);
  }
}

// ─────────────────────────── 逐笔事件 ───────────────────────────
function pushEvents(d) {
  const t = new Date(d.ts);
  const hh = String(t.getHours()).padStart(2, '0');
  const mm = String(t.getMinutes()).padStart(2, '0');
  const ss = String(t.getSeconds()).padStart(2, '0');
  const now = `${hh}:${mm}:${ss}`;
  const add = (txt, dir = 0) => {
    S.tape.unshift({ t: now, txt, dir, k: Math.random() });
    if (S.tape.length > 80) S.tape.pop();
  };

  const pids = new Set(d.procs.map(p => p.pid));
  if (S.lastPids.size) {
    for (const p of d.procs) if (!S.lastPids.has(p.pid) && p.cpu > 0.5) add(`${p.name} 上市 ${p.pid}`, 1);
    let gone = 0;
    for (const pid of S.lastPids) if (!pids.has(pid)) gone++;
    if (gone) add(`${gone} 只标的退市`, -1);
  }
  S.lastPids = pids;

  if (d.cpu_pct >= 88) add(`CPU 负载 ${d.cpu_pct.toFixed(0)}% 触及涨停`, 1);
  if (d.cpu_temp >= 82) add(`CPU 温度 ${d.cpu_temp.toFixed(0)}°C 高温预警`, 1);
  if (d.mem_pct >= 90) add(`内存占用 ${d.mem_pct.toFixed(0)}% 告急`, 1);
  if (d.gpu.ok && d.gpu.util >= 92) add(`GPU 引擎 ${d.gpu.util.toFixed(0)}% 满载`, 1);
  if (d.disk_read + d.disk_write >= 200) add(`磁盘突发 ${(d.disk_read + d.disk_write).toFixed(0)} MB/s`, 1);
  if (d.net_rx >= 100) add(`网络下行 ${d.net_rx.toFixed(0)} MB/s 放量`, 1);

  const top = d.procs[0];
  if (top && top.cpu > 25) add(`${top.name} 主力拉升 ${top.cpu.toFixed(0)}%`, 1);
}

// ─────────────────────────── 渲染（每 tick）───────────────────────────
function renderTick() {
  renderIndices();
  renderWatch();
  renderQuote();
  renderFlow();
  renderTape();
  renderProcs();
  renderStatus();
}

function renderIndices() {
  for (const code of INDEXES) {
    const el = document.querySelector(`.idx[data-code="${code}"]`);
    if (!el) continue;
    const m = S.byCode[code];
    const v = S.values[code];
    const d = (v ?? 0) - (S.open[code] ?? v ?? 0);
    el.querySelector('.v').textContent = fmt(v, m.decimals);
    const de = el.querySelector('.d');
    de.textContent = deltaText(d, m);
    de.className = 'd ' + chgClass(d);
    sparkline(el.querySelector('canvas'), S.series[code]?.v.slice(-90), colorOf(d));
  }
}

function renderWatch() {
  for (const m of S.metrics) {
    const row = document.querySelector(`.wrow[data-code="${m.code}"]`);
    if (!row) continue;
    const v = S.values[m.code];
    const d = (v ?? 0) - (S.open[m.code] ?? v ?? 0);
    const p = row.querySelector('.p');
    if (p) {
      p.textContent = fmt(v, m.decimals);
      p.className = 'p ' + chgClass(d);
    }
    const c = row.querySelector('.c');
    if (c) { c.textContent = deltaText(d, m); c.className = 'c ' + chgClass(d); }
    sparkline(row.querySelector('.sp'), S.series[m.code]?.v.slice(-90), colorOf(d));
  }
}

function renderQuote() {
  const m = S.byCode[S.sel];
  if (!m) return;
  const v = S.values[m.code];
  const o = S.open[m.code] ?? v;
  const d = (v ?? 0) - (o ?? 0);
  $('qCode').textContent = m.code;
  $('qName').textContent = m.name;
  $('qPrice').textContent = fmt(v, m.decimals);
  $('qPrice').className = 'q-price ' + chgClass(d);
  const pct = o ? ((v - o) / Math.abs(o) * 100) : 0;
  $('qChg').textContent = `${deltaText(d, m)}   ${pct >= 0 ? '+' : ''}${pct.toFixed(2)}%`;
  $('qChg').className = 'q-chg ' + chgClass(d);
  const hi = S.high[m.code], lo = S.low[m.code];
  const prev = S.prev[m.code] ?? v;
  $('qFields').innerHTML = `
    <div class="qf"><div class="k">今开</div><div class="v">${fmt(o, m.decimals)}</div></div>
    <div class="qf"><div class="k">最高</div><div class="v up">${fmt(hi, m.decimals)}</div></div>
    <div class="qf"><div class="k">最低</div><div class="v down">${fmt(lo, m.decimals)}</div></div>
    <div class="qf"><div class="k">昨收</div><div class="v">${fmt(prev, m.decimals)}</div></div>
    <div class="qf"><div class="k">成交量</div><div class="v">${(S.latest?.ctx ?? 0).toLocaleString()}</div></div>`;
  document.querySelectorAll('#periods button').forEach(b => {
    b.classList.toggle('on', +b.dataset.p === S.period);
  });
}

function renderFlow() {
  const m = S.byCode[S.sel];
  const key = m?.group === '图形' ? 'gpu' : 'cpu';
  const list = [...S.procs].sort((a, b) => (key === 'gpu' ? b.gpu - a.gpu : b.cpu - a.cpu)).slice(0, 10);
  const max = Math.max(...list.map(p => key === 'gpu' ? p.gpu : p.cpu), 1);
  $('flow').innerHTML = list.map((p, i) => {
    const val = key === 'gpu' ? p.gpu : p.cpu;
    const w = Math.max(2, Math.min(100, val / max * 100));
    const cls = i === 0 ? 'g1' : i === 1 ? 'g2' : i === 2 ? 'g3' : '';
    return `<div class="frow fwrap">
      <div class="bar" style="width:${w}%"></div>
      <span class="rk ${cls}">${i + 1}</span>
      <span class="nm">${esc(p.name)}</span>
      <span class="pc">${val.toFixed(1)}%</span>
    </div>`;
  }).join('');
  $('flowSub').textContent = `Top 10 · ${key === 'gpu' ? 'GPU' : 'CPU'}`;
}

function renderTape() {
  $('tape').innerHTML = S.tape.slice(0, 26).map(e => `
    <div class="trow">
      <span class="tm">${e.t}</span>
      <span class="ic ${e.dir > 0 ? 'up' : e.dir < 0 ? 'down' : ''}">${e.dir > 0 ? '▲' : e.dir < 0 ? '▼' : '·'}</span>
      <span class="tx">${esc(e.txt)}</span>
    </div>`).join('');
}

const PROC_COLS = [
  { k: 'pid', t: 'PID' }, { k: 'name', t: '名称' }, { k: 'cpu', t: 'CPU%' },
  { k: 'd', t: '涨跌' }, { k: 'mem', t: '内存' }, { k: 'thr', t: '线程' },
  { k: 'fd', t: 'FD' },
];
function renderProcs() {
  let list = S.procs;
  if (S.search) {
    const q = S.search.toLowerCase();
    list = list.filter(p => p.name.toLowerCase().includes(q) || String(p.pid).includes(q));
  }
  const { key, desc } = S.procSort;
  list = [...list].sort((a, b) => {
    const x = a[key], y = b[key];
    const c = typeof x === 'string' ? String(x).localeCompare(String(y)) : (x - y);
    return desc ? -c : c;
  }).slice(0, 120);

  $('procCount').textContent = `${S.procs.length} 进程`;
  const rows = list.map(p => {
    const d = p.d ?? 0;
    const hist = S.procHist.get(p.pid);
    return `<div class="prow">
      <span class="pid">${p.pid}</span>
      <span class="pn" title="${esc(p.name)}">${esc(p.name)}</span>
      <span class="${p.cpu > 20 ? 'up' : ''}">${p.cpu.toFixed(1)}</span>
      <span class="${chgClass(d)}">${d >= 0 ? '+' : ''}${d.toFixed(1)}</span>
      <span>${(p.mem / 1024).toFixed(2)}G</span>
      <span>${p.thr}</span>
      <span>${p.fd}</span>
      <canvas data-pid="${p.pid}" width="112" height="28"></canvas>
    </div>`;
  }).join('');
  $('prows').innerHTML = rows;
  // 迷你走势
  $('prows').querySelectorAll('canvas').forEach(cv => {
    const pid = +cv.dataset.pid;
    const h = S.procHist.get(pid) || [];
    sparkline(cv, h, h.length > 1 && h[h.length - 1] > h[0] ? '#ff4d4f' : '#00c08b');
  });
}

function renderStatus() {
  const d = S.latest;
  if (!d) return;
  $('fstat').textContent = `${S.paused ? '暂停' : '采样'} ${(S.interval / 1000).toFixed(1)}s · #${d.tick}`;
  $('fcProc').textContent = d.proc_count;
  $('fcThr').textContent = d.threads;
  $('fcFd').textContent = d.handles;
  $('fcUp').textContent = dur(d.uptime);
  const parts = [
    `CPU ${d.cpu_pct.toFixed(1)}% · ${d.cpu_avg_mhz}MHz`,
    `内存 ${d.mem_used_gb.toFixed(1)}/${d.mem_total_gb.toFixed(1)}GB (${d.mem_pct.toFixed(0)}%)`,
    `温度 ${d.cpu_temp.toFixed(0)}°C`,
    d.gpu.ok ? `GPU ${d.gpu.clock}MHz · ${d.gpu.util.toFixed(0)}%` : 'GPU —',
    `磁盘 ↓${d.disk_read.toFixed(1)} ↑${d.disk_write.toFixed(1)} MB/s`,
    `网络 ↓${d.net_rx.toFixed(2)} ↑${d.net_tx.toFixed(2)} MB/s`,
    `上下文 ${d.ctx.toLocaleString()}/s`,
    `进程 ${d.proc_count} · 线程 ${d.threads}`,
  ];
  $('marquee').innerHTML = parts.map(p => `<span>${p}</span><span class="sep">/</span>`).join('');
}

function dur(s) {
  s = Math.floor(s);
  const d = Math.floor(s / 86400), h = Math.floor(s % 86400 / 3600), m = Math.floor(s % 3600 / 60);
  if (d) return `${d}天${h}小时`;
  if (h) return `${h}小时${m}分`;
  return `${m}分`;
}
function esc(s) {
  return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
}

// ─────────────────────────── 迷你走势 ───────────────────────────
function sparkline(cv, data, color) {
  if (!cv) return;
  const dpr = window.devicePixelRatio || 1;
  const w = cv.clientWidth || cv.width, h = cv.clientHeight || cv.height;
  if (cv.width !== Math.round(w * dpr)) { cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr); }
  const g = cv.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  if (!data || data.length < 2) return;
  let mn = Infinity, mx = -Infinity;
  for (const v of data) { if (v < mn) mn = v; if (v > mx) mx = v; }
  if (mx - mn < 1e-9) { mx = mn + 1; }
  const pad = 1.5;
  g.beginPath();
  data.forEach((v, i) => {
    const x = i / (data.length - 1) * (w - 2) + 1;
    const y = h - pad - (v - mn) / (mx - mn) * (h - pad * 2);
    i ? g.lineTo(x, y) : g.moveTo(x, y);
  });
  g.strokeStyle = color;
  g.lineWidth = 1;
  g.globalAlpha = .95;
  g.stroke();
  g.globalAlpha = .12;
  g.lineTo(w - 1, h); g.lineTo(1, h); g.closePath();
  g.fillStyle = color;
  g.fill();
  g.globalAlpha = 1;
}

// ─────────────────────────── K 线聚合 ───────────────────────────
function buildCandles(ms, vs, periodMs) {
  const out = [];
  if (!ms || !ms.length) return out;
  let bucket = -1, c = null;
  for (let i = 0; i < ms.length; i++) {
    const b = Math.floor(ms[i] / periodMs);
    if (!c || b !== bucket) {
      if (c) out.push(c);
      c = { b, o: vs[i], h: vs[i], l: vs[i], c: vs[i], i0: i, i1: i };
      bucket = b;
    } else {
      if (vs[i] > c.h) c.h = vs[i];
      if (vs[i] < c.l) c.l = vs[i];
      c.c = vs[i];
      c.i1 = i;
    }
  }
  if (c) out.push(c);
  return out.slice(-260);
}
function ma(candles, n) {
  const out = [];
  let sum = 0;
  for (let i = 0; i < candles.length; i++) {
    sum += candles[i].c;
    if (i >= n) sum -= candles[i - n].c;
    out.push(i >= n - 1 ? sum / n : null);
  }
  return out;
}

// ─────────────────────────── 主图 ───────────────────────────
const mainCv = () => $('mainChart');
let hoverX = null;

function drawMain() {
  const cv = mainCv();
  const box = cv.parentElement.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  const W = Math.max(120, box.width), H = Math.max(120, box.height);
  if (cv.width !== Math.round(W * dpr) || cv.height !== Math.round(H * dpr)) {
    cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
  }
  const g = cv.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, W, H);

  const m = S.byCode[S.sel];
  const ser = S.series[S.sel];
  const period = Math.max(S.period, S.interval * 2);
  const candles = ser ? buildCandles(ser.ms, ser.v, period) : [];
  const dark = S.dark;
  const cBg = dark ? '#070a0f' : '#fbfcfe';
  const cGrid = dark ? '#141a25' : '#edf1f7';
  const cTxt = dark ? '#8b97a8' : '#7e8b9e';
  const cLine = dark ? '#232c3b' : '#d2dae6';
  const cHi = dark ? '#e9eff8' : '#101724';

  g.fillStyle = cBg;
  g.fillRect(0, 0, W, H);

  const padR = 64, padL = 8, padT = 10, padB = 20;
  const volH = Math.max(34, H * 0.2);
  const priceH = H - padT - padB - volH - 8;
  const plotW = W - padL - padR;
  const priceTop = padT, priceBot = padT + priceH;
  const volTop = priceBot + 8, volBot = H - padB;

  if (!candles.length) {
    g.fillStyle = cTxt; g.font = `13px ${MONO}`;
    g.fillText('等待行情数据…', padL + 14, H / 2);
    return;
  }

  // 价格范围（含 MA）
  let mn = Infinity, mx = -Infinity;
  for (const c of candles) { mn = Math.min(mn, c.l); mx = Math.max(mx, c.h); }
  const span = Math.max(mx - mn, Math.abs(mx) * 0.002, 1e-6);
  const padP = span * 0.08;
  mn -= padP; mx += padP;
  const Y = (v) => priceBot - (v - mn) / (mx - mn) * priceH;
  const B = (b) => padL + (candles.indexOf(b) + 0.5) * (plotW / candles.length);
  const cw = Math.max(1, Math.min(14, plotW / candles.length * 0.68));

  // 网格 + 右侧价格轴
  g.strokeStyle = cGrid; g.lineWidth = 1; g.font = `10px ${MONO}`; g.fillStyle = cTxt;
  for (let i = 0; i <= 4; i++) {
    const y = priceTop + priceH * i / 4;
    g.beginPath(); g.moveTo(padL, y + .5); g.lineTo(padL + plotW, y + .5); g.stroke();
    const val = mx - (mx - mn) * i / 4;
    g.fillText(fmt(val, m.decimals), padL + plotW + 6, y + 3.5);
  }
  // 量能区网格
  g.strokeStyle = cLine;
  g.beginPath(); g.moveTo(padL, volTop - .5); g.lineTo(padL + plotW, volTop - .5); g.stroke();

  // 时间轴
  const tN = Math.min(6, candles.length);
  g.fillStyle = cTxt;
  for (let i = 0; i < tN; i++) {
    const idx = Math.floor(i * (candles.length - 1) / Math.max(tN - 1, 1));
    const c = candles[idx];
    const t = new Date(c.b * period);
    const lbl = period >= 60000
      ? `${t.getHours()}:${String(t.getMinutes()).padStart(2, '0')}`
      : `${t.getHours()}:${String(t.getMinutes()).padStart(2, '0')}:${String(t.getSeconds()).padStart(2, '0')}`;
    const x = B(c);
    const align = i === 0 ? 'left' : i === tN - 1 ? 'right' : 'center';
    g.textAlign = align;
    g.fillText(lbl, x, H - 6);
  }
  g.textAlign = 'left';

  // 成交量柱
  const volMax = Math.max(...candles.map(c => c.i1 - c.i0 + 1), 1);
  candles.forEach((c, i) => {
    const v = c.i1 - c.i0 + 1;
    const hgt = Math.max(1, v / volMax * (volBot - volTop - 2));
    const up = c.c >= c.o;
    g.fillStyle = (up === S.upRed) ? 'rgba(255,77,79,.55)' : 'rgba(0,192,139,.55)';
    const x = padL + (i + .5) * (plotW / candles.length);
    g.fillRect(x - cw / 2, volBot - hgt, cw, hgt);
  });

  // MA 线
  const mas = [[5, '#f2c14e'], [10, '#4d8dff'], [20, '#a074ff']];
  for (const [n, col] of mas) {
    const arr = ma(candles, n);
    g.beginPath();
    let started = false;
    arr.forEach((v, i) => {
      if (v == null) return;
      const x = padL + (i + .5) * (plotW / candles.length);
      const y = Y(v);
      started ? g.lineTo(x, y) : (g.moveTo(x, y), started = true);
    });
    g.strokeStyle = col; g.lineWidth = 1; g.globalAlpha = .85; g.stroke(); g.globalAlpha = 1;
  }

  // 蜡烛
  candles.forEach((c, i) => {
    const x = padL + (i + .5) * (plotW / candles.length);
    const up = c.c >= c.o;
    const col = (up === S.upRed) ? '#ff4d4f' : '#00c08b';
    g.strokeStyle = col;
    g.fillStyle = col;
    g.lineWidth = 1;
    // 影线
    g.beginPath();
    g.moveTo(Math.round(x) + .5, Math.round(Y(c.h)));
    g.lineTo(Math.round(x) + .5, Math.round(Y(c.l)));
    g.stroke();
    // 实体（红涨绿跌实心，与原版一致）
    const y1 = Y(Math.max(c.o, c.c)), y2 = Y(Math.min(c.o, c.c));
    const hh = Math.max(1, y2 - y1);
    g.fillRect(Math.round(x - cw / 2), Math.round(y1), Math.max(1, Math.round(cw)), Math.round(hh));
  });

  // 最新价线
  const last = candles[candles.length - 1];
  const lastCol = (last.c >= last.o === S.upRed) ? '#ff4d4f' : '#00c08b';
  const yLast = Y(last.c);
  g.setLineDash([3, 3]); g.strokeStyle = lastCol; g.globalAlpha = .7;
  g.beginPath(); g.moveTo(padL, yLast); g.lineTo(padL + plotW, yLast); g.stroke();
  g.setLineDash([]); g.globalAlpha = 1;
  // 价格标签
  g.fillStyle = lastCol;
  g.fillRect(padL + plotW + 2, yLast - 8, padR - 4, 16);
  g.fillStyle = '#0a0d13';
  g.font = `600 10.5px ${MONO}`;
  g.fillText(fmt(last.c, m.decimals), padL + plotW + 5, yLast + 3.5);

  // 十字光标
  if (hoverX != null && hoverX >= padL && hoverX <= padL + plotW) {
    const idx = Math.max(0, Math.min(candles.length - 1,
      Math.round((hoverX - padL) / (plotW / candles.length) - .5)));
    const c = candles[idx];
    const x = padL + (idx + .5) * (plotW / candles.length);
    g.strokeStyle = dark ? 'rgba(242,193,78,.55)' : 'rgba(180,140,20,.6)';
    g.setLineDash([2, 3]); g.lineWidth = 1;
    g.beginPath(); g.moveTo(x, priceTop); g.lineTo(x, volBot); g.stroke();
    const yy = (hoverY != null ? hoverY : Y(c.c));
    g.beginPath(); g.moveTo(padL, yy); g.lineTo(padL + plotW, yy); g.stroke();
    g.setLineDash([]);

    const d = c.c - c.o;
    const box = $('tip');
    const t = new Date(c.b * period);
    box.innerHTML =
      `<span style="color:${cTxt}">${String(t.getHours()).padStart(2, '0')}:${String(t.getMinutes()).padStart(2, '0')}:${String(t.getSeconds()).padStart(2, '0')}</span><br>` +
      `开 <b style="color:${cHi}">${fmt(c.o, m.decimals)}</b>　高 <b class="up">${fmt(c.h, m.decimals)}</b><br>` +
      `低 <b class="down">${fmt(c.l, m.decimals)}</b>　收 <b style="color:${cHi}">${fmt(c.c, m.decimals)}</b><br>` +
      `涨跌 <b class="${chgClass(d)}">${deltaText(d, m)}</b>`;
    box.style.opacity = 1;
    const tw = box.offsetWidth || 130;
    let left = x + 14;
    if (left + tw > W - 6) left = x - tw - 14;
    box.style.left = left + 'px';
    box.style.top = Math.max(6, Math.min(H - box.offsetHeight - 6, (hoverY ?? Y(c.c)) - 10)) + 'px';
  } else {
    $('tip').style.opacity = 0;
  }
}

let hoverY = null;

// ─────────────────────────── 营收系统 ───────────────────────────
const ECO = [
  { key: 'cpu', name: 'CPU', rate: 22, be: 30, off: 'cpu_pct' },
  { key: 'gpu', name: 'GPU', rate: 30, be: 28, off: 'gpu_util' },
  { key: 'mem', name: '内存', rate: 12, be: 35, off: 'mem_pct' },
  { key: 'disk', name: '磁盘', rate: 7.2, be: 25, off: 'disk' },
  { key: 'net', name: '网络', rate: 7.2, be: 25, off: 'net' },
];

function buildEcoBars() {
  $('ecoBars').innerHTML = ECO.map(e => `
    <div class="ebar" data-k="${e.key}">
      <div class="lb"><span>${e.name}</span><span class="pv">0%</span></div>
      <div class="tr"><div class="fl" style="width:0%"></div><div class="be" style="left:${e.be}%"></div></div>
    </div>`).join('');
}

function ecoWeights() {
  // 硬件自适应折算（原版：16 线程 / 8G 显存 / 32G 内存 / 2 块盘 = 1.0）
  const d = S.latest;
  if (!d) return { cpu: 1, gpu: 1, mem: 1 };
  const cores = d.core_pct.length || 1;
  return {
    cpu: clamp(cores / 16, .3, 4),
    gpu: clamp(0.3, .3, 4),
    mem: clamp(d.mem_total_gb / 32, .3, 4),
  };
}
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

function stepEco(d) {
  const weights = ecoWeights();
  const dt = S.interval / 1000;
  // 核显（无独立显存）不参与 GPU 产能结算，否则必然恒为负
  const gpuEco = d.gpu.ok && d.gpu.backend === 'nvml' && d.gpu.mem_total > 0;
  let net = 0;
  const loads = {
    cpu: d.cpu_pct,
    gpu: gpuEco ? d.gpu.util : 0,
    mem: d.mem_pct,
    disk: d.disk_busy_pct ?? Math.min((d.disk_read + d.disk_write) / 5, 100),
    net: Math.min((d.net_rx + d.net_tx) * 40, 100),
  };
  for (const e of ECO) {
    if (e.key === 'gpu' && !gpuEco) continue;
    const load = clamp(loads[e.key] ?? 0, 0, 100) / 100;
    const wt = e.key === 'cpu' ? weights.cpu : e.key === 'gpu' ? weights.gpu : e.key === 'mem' ? weights.mem : 1;
    // 过热降效（GPU 温度越高折算越低，原版口径）
    const heat = e.key === 'gpu' && d.gpu.temp >= 72 ? (d.gpu.temp >= 84 ? .78 : d.gpu.temp >= 78 ? .88 : .95) : 1;
    const gross = e.rate * wt * Math.pow(load, 1.7) * heat;
    const idle = e.rate * wt * Math.pow(e.be / 100, 1.7);
    net += gross - idle;
  }
  S.ecoTotal += net * dt;
  S.ecoSec += dt;
  if (S.ecoTotal > S.ecoPeak) S.ecoPeak = S.ecoTotal;
  if (Math.random() < dt) localStorage.setItem('pt_eco_total', S.ecoTotal.toFixed(2));

  $('ecoTot').textContent = `¥ ${S.ecoTotal >= 0 ? '' : '-'}${Math.abs(S.ecoTotal).toFixed(2)}`;
  $('ecoTot').className = 'eco-total ' + (S.ecoTotal >= 0 ? 'up' : 'down');
  $('ecoNet').textContent = `${net >= 0 ? '+' : ''}${net.toFixed(2)} 元/s`;
  $('ecoNet').className = 'eco-total ' + (net >= 0 ? 'up' : 'down');
  $('ecoRate').textContent = `基准 ×${weights.cpu.toFixed(2)} · 本时段 ${Math.floor(S.ecoSec / 60)} 分 · 峰值 ¥${S.ecoPeak.toFixed(0)}`;

  document.querySelectorAll('.ebar').forEach(el => {
    const k = el.dataset.k;
    const v = clamp(loads[k] ?? 0, 0, 100);
    el.querySelector('.fl').style.width = v + '%';
    el.querySelector('.pv').textContent = v.toFixed(0) + '%';
  });
}

// ─────────────────────────── UI 构建 ───────────────────────────
function buildIndices() {
  $('indices').innerHTML = INDEXES.map(code => {
    const m = S.byCode[code];
    if (!m) return '';
    return `<div class="idx" data-code="${code}">
      <div class="k">${INDEX_LABEL[code] || m.name}</div>
      <div class="v">—</div>
      <div class="d flat">—</div>
      <canvas></canvas>
    </div>`;
  }).join('');
  $('indices').querySelectorAll('.idx').forEach(el => {
    el.onclick = () => select(el.dataset.code);
  });
}

function buildWatch() {
  let html = '';
  for (const grp of (S.meta.groups || [])) {
    const items = S.metrics.filter(m => m.group === grp);
    if (!items.length) continue;
    html += `<div class="grp">${grp}</div>`;
    html += items.map(m => `
      <div class="wrow" data-code="${m.code}">
        <span class="n"><small>${m.code}</small>${m.name}</span>
        <canvas class="sp"></canvas>
        <span class="p flat">—</span>
        <span class="c flat">—</span>
      </div>`).join('');
  }
  $('watch').innerHTML = html;
  $('watch').querySelectorAll('.wrow').forEach(el => {
    el.onclick = () => select(el.dataset.code);
  });
  markSel();
}

function select(code) {
  S.sel = code;
  markSel();
  S.high[code] = S.high[code] ?? S.values[code];
  S.low[code] = S.low[code] ?? S.values[code];
  S.tape = [];
  renderTick();
}
function markSel() {
  document.querySelectorAll('.wrow').forEach(el => el.classList.toggle('sel', el.dataset.code === S.sel));
  document.querySelectorAll('.idx').forEach(el => el.classList.toggle('sel', el.dataset.code === S.sel));
}

function bindUI() {
  document.querySelectorAll('#intervalSeg button').forEach(b => {
    b.onclick = async () => {
      const ms = +b.dataset.ms;
      await fetch('/api/interval', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ms }),
      });
      document.querySelectorAll('#intervalSeg button').forEach(x => x.classList.toggle('on', x === b));
      S.interval = ms;
    };
  });
  document.querySelectorAll('#periods button').forEach(b => {
    b.onclick = () => { S.period = +b.dataset.p; renderQuote(); };
  });
  $('btnPause').onclick = async () => {
    const next = !S.paused;
    await fetch('/api/pause', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ paused: next }),
    });
    S.paused = next;
    $('btnPause').classList.toggle('on', next);
    $('btnPause').textContent = next ? '继续' : '暂停';
    // 暂停只是停止采样，SSE 连接仍在，不能点亮「连接中断」
    $('liveDot').classList.toggle('off', next);
    $('liveText').textContent = next ? '已暂停' : '实时采样';
  };
  $('btnColor').onclick = () => {
    S.upRed = !S.upRed;
    $('btnColor').textContent = S.upRed ? '红涨' : '绿涨';
    renderTick();
  };
  $('btnTheme').onclick = () => {
    S.dark = !S.dark;
    document.body.classList.toggle('light', !S.dark);
    $('btnTheme').textContent = S.dark ? '深色' : '浅色';
    renderTick();
  };
  $('btnEco').onclick = () => {
    S.ecoOn = !S.ecoOn;
    $('btnEco').classList.toggle('on', S.ecoOn);
    $('ecoPanel').style.display = S.ecoOn ? '' : 'none';
  };
  $('search').oninput = (e) => { S.search = e.target.value.trim(); renderProcs(); };
  document.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.key.toLowerCase() === 'f') { e.preventDefault(); $('search').focus(); }
    if (e.key === 'Escape' && document.activeElement === $('search')) {
      $('search').value = ''; S.search = ''; $('search').blur(); renderProcs();
    }
    if (e.key === ' ' && !['INPUT'].includes(document.activeElement.tagName)) {
      e.preventDefault(); $('btnPause').click();
    }
    if (['1', '2', '3', '4', '5'].includes(e.key) && !['INPUT'].includes(document.activeElement.tagName)) {
      S.period = PERIODS[+e.key - 1]; renderQuote();
    }
    if (e.key.toLowerCase() === 'c') $('btnColor').click();
    if (e.key.toLowerCase() === 'e') $('btnEco').click();
  });

  // 行头排序
  $('phead').querySelectorAll('span[data-sort]').forEach(sp => {
    sp.onclick = () => {
      const k = sp.dataset.sort;
      if (S.procSort.key === k) S.procSort.desc = !S.procSort.desc;
      else S.procSort = { key: k, desc: k !== 'name' };
      $('phead').querySelectorAll('span').forEach(x => x.textContent = x.textContent.replace(/[↓↑]/g, ''));
      sp.textContent = sp.textContent + (S.procSort.desc ? '↓' : '↑');
      renderProcs();
    };
  });

  // 主图交互
  const cv = mainCv();
  cv.addEventListener('mousemove', (e) => {
    const r = cv.getBoundingClientRect();
    hoverX = e.clientX - r.left;
    hoverY = e.clientY - r.top;
  });
  cv.addEventListener('mouseleave', () => { hoverX = hoverY = null; });
  cv.addEventListener('touchstart', (e) => {
    const t = e.touches[0], r = cv.getBoundingClientRect();
    hoverX = t.clientX - r.left; hoverY = t.clientY - r.top;
  }, { passive: true });
  cv.addEventListener('touchmove', (e) => {
    const t = e.touches[0], r = cv.getBoundingClientRect();
    hoverX = t.clientX - r.left; hoverY = t.clientY - r.top;
  }, { passive: true });
}

// ─────────────────────────── 帧循环 ───────────────────────────
let marqueePos = 0;
function frame() {
  drawMain();
  // 跑马灯
  const mq = $('marquee');
  marqueePos -= 0.55;
  if (mq.firstElementChild && -marqueePos > mq.scrollWidth / 2) marqueePos = 0;
  mq.style.transform = `translateX(${marqueePos}px)`;
  requestAnimationFrame(frame);
}

function tickClock() {
  const d = new Date();
  $('clock').textContent = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`;
}

boot().catch(e => {
  document.body.insertAdjacentHTML('beforeend',
    `<div style="position:fixed;inset:0;display:grid;place-items:center;background:#0a0d13;color:#ff4d4f;font:14px monospace">加载失败: ${e.message}</div>`);
});
