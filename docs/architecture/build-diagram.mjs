// Generates docs/images/architecture-{light,dark}.svg, the animated README
// diagram in both GitHub themes. Run: node docs/architecture/build-diagram.mjs
//
// The output is a self-contained SVG with CSS-only animation: GitHub serves
// README images through a proxy that strips scripts, so every motion must be an
// infinite CSS loop. Negative animation-delays put each pulse mid-cycle at t=0,
// so the first frame a reader sees already shows traffic.
//
// Projection kernel and look adapted from iso-glow / iso-figure (MIT,
// github.com/MrBongoC/ai-iso-skill).

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', 'images');
// The evaluator dashboard's two themes (Tools/DashboardSrc/style.css), with
// translucent borders flattened onto the panel. Only the dark theme keeps
// iso-glow's bloom; in light mode lit parts read as flat accent.
const THEMES = {
  light: {
    bg: '#e9edf3', deck: '#ffffff', body: '#f2f5fa', recess: '#dee5ee',
    line: '#7d8a9b', detail: '#c9d1dc', wire: '#9aa6b5',
    text: '#17222e', muted: '#46566a', dim: '#6b7a8d',
    accent: '#2264c4', accentInk: '#174a94', accentWeak: '#d5e2f4', glass: '#eef3fb',
    pulse: '#2264c4', glow: false,
  },
  dark: {
    bg: '#0e1217', deck: '#1b232c', body: '#151b22', recess: '#0a0d11',
    line: '#4a5563', detail: '#2a323c', wire: '#3f4955',
    text: '#e8ecf2', muted: '#aeb8c6', dim: '#7f8b9b',
    accent: '#4d8df0', accentInk: '#9cc2ff', accentWeak: '#1a2a44', glass: '#101b2b',
    pulse: '#9cc2ff', glow: true,
  },
};
const T = 4.8;
const TRAVEL = 0.12;

const C = Math.cos(Math.PI / 6), S = Math.sin(Math.PI / 6);
const f = n => +n.toFixed(2);

function kernel(OX, OY) {
  const P = (x, y, z) => [(x - y) * C + OX, (x + y) * S - z + OY];
  const D = (x, y, z) => [(x - y) * C, (x + y) * S - z];
  const plane = (O, U, V) => {
    const o = P(...O), u = D(...U), v = D(...V);
    return `matrix(${f(u[0])} ${f(u[1])} ${f(v[0])} ${f(v[1])} ${f(o[0])} ${f(o[1])})`;
  };
  const TOP = (x, y, z) => plane([x, y, z], [1, 0, 0], [0, 1, 0]);
  const FRONT = (x, y, z) => plane([x, y, z], [1, 0, 0], [0, 0, -1]);
  const SIDE = (x, y, z) => plane([x, y, z], [0, -1, 0], [0, 0, -1]);
  const rect = (t, w, h, r = 0, cls = 'face') => `<g transform="${t}"><rect class="${cls}" width="${w}" height="${h}" rx="${r}"/></g>`;
  const rounded = (x, y, z, w, d, h, r, cls) => {
    r = Math.min(r, w / 2, d / 2);
    const n = 10, z1 = z + h, q = Math.PI / 4, H = Math.PI / 2, c = cls ? ' ' + cls : '';
    const arc = (cx, cy, a0, a1) => {
      const o = [];
      for (let i = 0; i <= n; i++) { const a = a0 + (a1 - a0) * i / n; o.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]); }
      return o;
    };
    const ring = [...arc(x + w - r, y + r, -H, 0), ...arc(x + w - r, y + d - r, 0, H), ...arc(x + r, y + d - r, H, 2 * H), ...arc(x + r, y + r, 2 * H, 3 * H)];
    const vis = [...arc(x + w - r, y + r, -q, 0), ...arc(x + w - r, y + d - r, 0, H), ...arc(x + r, y + d - r, H, 3 * q)];
    const d3 = pts => 'M' + pts.map(p => P(...p).map(f).join(' ')).join('L') + 'Z';
    const wall = [...vis.map(([a, b]) => [a, b, z]), ...vis.slice().reverse().map(([a, b]) => [a, b, z1])];
    return `<path class="face${c}" d="${d3(wall)}"/><path class="face top${c}" d="${d3(ring.map(([a, b]) => [a, b, z1]))}"/>`;
  };
  const box = (x, y, z, w, d, h, r = 0, cls = '') => r > 0 ? rounded(x, y, z, w, d, h, r, cls) :
    rect(SIDE(x + w, y + d, z + h), d, h, 0, `face ${cls}`) + rect(FRONT(x, y + d, z + h), w, h, 0, `face ${cls}`) +
    rect(TOP(x, y, z + h), w, d, 0, `face top ${cls}`);
  const path = pts => 'M' + pts.map(p => P(...p).map(f).join(' ')).join('L');
  const len = pts => pts.slice(1).reduce((s, p, i) => {
    const a = P(...pts[i]), b = P(...p);
    return s + Math.hypot(b[0] - a[0], b[1] - a[1]);
  }, 0);
  return { P, TOP, FRONT, SIDE, rect, box, path, len };
}

function frame(points, margin, aspect) {
  const d = points.map(([x, y, z]) => [(x - y) * C, (x + y) * S - z]);
  const x0 = Math.min(...d.map(p => p[0])), x1 = Math.max(...d.map(p => p[0]));
  const y0 = Math.min(...d.map(p => p[1])), y1 = Math.max(...d.map(p => p[1]));
  const m = margin * Math.max(x1 - x0, y1 - y0);
  let w = x1 - x0 + 2 * m, h = y1 - y0 + 2 * m, ox = m - x0, oy = m - y0;
  if (w / h < aspect) { const nw = h * aspect; ox += (nw - w) / 2; w = nw; }
  else { const nh = w / aspect; oy += (nh - h) / 2; h = nh; }
  return { ...kernel(ox, oy), W: f(w), H: f(h) };
}

const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const delay = phase => `animation-delay:${f((phase - 1) * T)}s`;

const K = frame([
  [0, 0, 120], [600, 0, 0], [0, 440, 0], [960, 440, 0], [960, 190, 0], [830, 40, 0], [660, 56, 175],
  [10, 692, 0], [30, 618, 90], [105, 548, 140], [1140, 236, 0], [1140, 142, 104],
], 0.06, 1.5);
const { P, TOP, FRONT, SIDE, rect, box, path, len } = K;

const on = (t, inner) => `<g transform="${t}">${inner}</g>`;

function wire(pts, phases) {
  const L = len(pts), dash = f(Math.min(600, 1000 * 34 / L));
  let s = `<path class="wire" d="${path(pts)}"/>`;
  for (const ph of [].concat(phases)) {
    s += `<path class="pulse" pathLength="1000" d="${path(pts)}" style="stroke-dasharray:${dash} 3000;${delay(ph)}"/>`;
  }
  return s;
}

const led = (cx, cy, r, phase) =>
  `<circle class="led" cx="${cx}" cy="${cy}" r="${r}"/><circle class="led-on ping" filter="url(#glow)" cx="${cx}" cy="${cy}" r="${r}" style="${delay(phase)}"/>`;

const plate = (x, y, w, d, screws) => {
  let s = box(x, y, 0, w, d, 10, 14);
  s += on(TOP(x, y, 10), `<rect class="detail" x="10" y="10" width="${w - 20}" height="${d - 20}" rx="8"/>` +
    screws.map(([a, b]) => `<circle class="detail" cx="${a}" cy="${b}" r="4"/><line class="detail" x1="${a - 3}" y1="${b}" x2="${a + 3}" y2="${b}"/>`).join(''));
  return s;
};

function chip(x, y, w, d, h, lines, phase) {
  let s = box(x, y, 10, w, d, h);
  const z = 10 + h;
  s += on(TOP(x, y, z), `<rect class="face recess" x="12" y="12" width="${w - 24}" height="${d - 24}" rx="4"/>` +
    `<rect class="halo" filter="url(#bloom)" x="16" y="16" width="${w - 32}" height="${d - 32}"/>` +
    `<rect class="die ping-soft" x="16" y="16" width="${w - 32}" height="${d - 32}" rx="3" style="${delay(phase)}"/>` +
    lines.map(([t, sz, cls], i) => `<text class="${cls}" x="26" y="${36 + lines.slice(0, i).reduce((a, l) => a + l[1] + 6, 0) + sz * 0.3}" font-size="${sz}">${esc(t)}</text>`).join(''));
  for (let u = 14; u < w - 8; u += 16) s += box(x + u, y + d, 10, 6, 6, 4);
  for (let v = 14; v < d - 8; v += 16) s += box(x + w, y + v, 10, 6, 6, 4);
  return s;
}

const tags = [];
function tag(pt, name, sub, { lift = 26, side = 'up', anchor = 'middle', dx = 0 } = {}) {
  const [px, py] = P(...pt);
  const ty = side === 'up' ? py - lift : py + lift;
  const tx = px + dx;
  let s = `<line class="lead" x1="${f(px)}" y1="${f(py)}" x2="${f(tx)}" y2="${f(side === 'up' ? ty + 6 : ty - 18)}"/><circle class="lead-dot" cx="${f(px)}" cy="${f(py)}" r="2"/>`;
  const nameY = side === 'up' ? (sub ? ty - 18 : ty) : ty;
  s += `<text class="tag" x="${f(tx)}" y="${f(nameY)}" text-anchor="${anchor}">${esc(name)}</text>`;
  if (sub) s += `<text class="sub" x="${f(tx)}" y="${f(nameY + 17)}" text-anchor="${anchor}">${esc(sub)}</text>`;
  tags.push(s);
}

const Z = 10;
let svg = '';

svg += plate(0, 0, 600, 440, [[22, 22], [578, 22], [22, 418], [578, 418]]);
svg += plate(660, 190, 300, 250, [[22, 22], [278, 22], [22, 228], [278, 228]]);

svg += `<path class="link" d="${path([[290, 432, Z], [290, 436, Z], [810, 436, Z], [810, 431, Z]])}"/>`;
svg += `<path class="link-flow" d="${path([[290, 432, Z], [290, 436, Z], [810, 436, Z], [810, 431, Z]])}"/>`;

// Producers publish down into the bus; subscribers draw from it.
// Front-row boxes hide the plate behind them for a depth equal to twice their
// height, so the bus sits far enough back for those wires to stay visible.
svg += wire([[90, 110, Z], [90, 160, Z]], 0.0);
svg += wire([[230, 110, Z], [230, 160, Z]], 0.0);
svg += wire([[370, 110, Z], [370, 160, Z]], 0.80);
svg += wire([[510, 160, Z], [510, 110, Z]], 0.48);
svg += wire([[80, 206, Z], [80, 300, Z]], 0.48);
svg += wire([[230, 206, Z], [230, 290, Z]], 0.22);
svg += wire([[350, 290, Z], [350, 206, Z]], 0.36);
svg += wire([[510, 206, Z], [510, 300, Z]], 0.48);
svg += wire([[560, 75, Z], [600, 75, Z], [700, 75, 4]], 0.60);

svg += on(TOP(30, 160, Z), `<rect class="halo" filter="url(#bloom)" x="-14" y="-14" width="568" height="74"/>`);

// Back row: PPE manager, scene emitters, session manager, dashboard bootstrap.
svg += box(40, 40, Z, 100, 70, 50);
svg += on(FRONT(40, 110, 60), `<g>${[14, 22, 30, 38, 46, 54].map(u => `<line class="detail" x1="${u}" y1="12" x2="${u}" y2="38"/>`).join('')}</g>` +
  `<text class="label" x="66" y="28" font-size="11" text-anchor="middle">PPE</text>` + led(86, 40, 3.2, 0.0));
svg += on(TOP(40, 40, 60), `<rect class="detail" x="10" y="10" width="80" height="50" rx="4"/>`);

svg += box(180, 40, Z, 100, 70, 40);
for (const [i, u] of [200, 228, 256].entries()) {
  svg += `<g class="${i === 0 ? 'cap ping-cap' : 'cap'}" style="${i === 0 ? delay(0.0) : ''}">${box(u, 62, 50, 18, 26, 7)}</g>`;
}
svg += on(FRONT(180, 110, 50), `<text class="label" x="50" y="24" font-size="10" text-anchor="middle">ACTIONS</text>` + led(86, 30, 3.2, 0.0));

svg += box(320, 40, Z, 100, 70, 50);
svg += on(FRONT(320, 110, 60), `<rect class="glass" x="12" y="10" width="62" height="24" rx="3"/>` +
  `<text class="scr" x="43" y="27" font-size="13" text-anchor="middle">00:42</text>` + led(86, 22, 3.2, 0.80));

svg += box(460, 40, Z, 100, 70, 50);
svg += on(FRONT(460, 110, 60), `${[12, 18, 24, 30].map(v => `<line class="detail" x1="10" y1="${v}" x2="56" y2="${v}"/>`).join('')}` +
  `<text class="label" x="34" y="44" font-size="10" text-anchor="middle">WS :8080</text>` + led(84, 22, 3.2, 0.48));
svg += on(SIDE(560, 110, 60), `<rect class="face recess" x="30" y="24" width="14" height="10" rx="2"/>`);

// Evaluator browser: base, stand, then the display slab facing the viewer.
svg += box(700, 45, 0, 100, 60, 6, 6);
svg += box(735, 60, 6, 30, 14, 36);
svg += box(660, 74, 42, 180, 14, 128);
svg += on(FRONT(660, 88, 170), (() => {
  const W = 180, H = 128;
  let s = `<rect class="halo" filter="url(#bloom)" x="8" y="8" width="${W - 16}" height="${H - 16}"/>`;
  s += `<rect class="glass" x="8" y="8" width="${W - 16}" height="${H - 16}" rx="4"/>`;
  s += `<text class="scr" x="18" y="26" font-size="10">LIVE · P-07</text>`;
  s += `<circle class="led-on blink" cx="${W - 22}" cy="22" r="3.4" filter="url(#glow)"/>`;
  s += `<text class="scr big ping-num" x="18" y="66" font-size="30" style="${delay(0.72)}">92</text>`;
  s += `<text class="scr dim" x="66" y="66" font-size="10">score</text>`;
  [0.85, 0.6, 0.3].forEach((k, i) => {
    s += `<rect class="bar-bg" x="18" y="${80 + i * 12}" width="${W - 50}" height="6" rx="2"/>`;
    s += `<rect class="bar ${i === 2 ? 'grow' : ''}" x="18" y="${80 + i * 12}" width="${f((W - 50) * k)}" height="6" rx="2" style="${i === 2 ? delay(0.72) : ''}"/>`;
  });
  return s;
})());

svg += box(30, 160, Z, 540, 46, 16);
svg += on(TOP(30, 160, 26), (() => {
  let s = `<rect class="lane" x="10" y="7" width="520" height="32" rx="4"/>`;
  for (let i = 0; i < 16; i++) {
    s += `<rect class="cell ping-cell" x="${18 + i * 32}" y="14" width="22" height="18" rx="2" style="${delay(i / 16 || 1)}"/>`;
  }
  return s;
})());
svg += on(FRONT(30, 206, 26), `<text class="label" x="14" y="12" font-size="9">EVENTBUS · TYPED PAYLOADS · QUEUED · CAUSAL ORDER · ~2 MS/FRAME DRAIN</text>`);
svg += wire([[60, 183, 26], [560, 183, 26]], 0.12).replace('class="wire"', 'class="wire faint"');

// Front row: UI consumer, the shared domain assembly, the session logger.
svg += box(30, 300, Z, 100, 90, 40);
svg += on(FRONT(30, 390, 50), `<rect class="halo" filter="url(#bloom)" x="12" y="6" width="76" height="22"/>` +
  `<rect class="glass" x="12" y="6" width="76" height="22" rx="3"/>` +
  `<text class="scr ping-num" x="20" y="17" font-size="8" style="${delay(0.56)}">⚠ EPI</text>` +
  `<line class="scr-line" x1="20" y1="23" x2="74" y2="23"/>` + led(84, 34, 3, 0.56));

svg += chip(170, 290, 240, 130, 18, [
  ['SafetyProto.Shared.dll', 15, 'scr'],
  ['TaskManagerCore', 10, 'scr dim'],
  ['SafetyRuleEngineCore', 10, 'scr dim'],
  ['ScoreService · SessionLoggerCore', 10, 'scr dim'],
], 0.30);

svg += box(460, 300, Z, 100, 90, 36);
svg += on(FRONT(460, 390, 46), `<rect class="face recess" x="12" y="5" width="54" height="26" rx="2"/>` +
  [10, 16, 22, 28].map((v, i) => `<line class="doc ping-line" x1="18" y1="${v}" x2="${54 - (i % 2) * 12}" y2="${v}" style="${delay(0.56 + i * 0.03)}"/>`).join('') +
  `<text class="label" x="72" y="18" font-size="9">.json</text>` + led(84, 28, 3, 0.56));

// The headset that hosts the Unity runtime: a stylized standalone HMD, no logo.
{
  // The headset is the one shape that is not an extruded plan, so it gets a
  // local sheet-based renderer: two 3D polylines bound a strip of quads, and
  // every contiguous run of same-facing quads becomes one filled path (so
  // no tessellation hairlines). Facing uses the view vector (1,1,1): the
  // kernel's +x, +y and +z faces are the visible ones.
  const vsub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
  const vdot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const vcross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
  const vadd = (a, b, k = 1) => [a[0] + k * b[0], a[1] + k * b[1], a[2] + k * b[2]];
  const sheet = (A, B, { closed = false, sign = 0, outer = 'face', top = 'face top', inner = 'face lining' } = {}) => {
    const n = A.length, m = closed ? n : n - 1;
    const cen = [...A, ...B].reduce((s, p) => vadd(s, p, 1 / (2 * n)), [0, 0, 0]);
    const keys = [];
    for (let i = 0; i < m; i++) {
      const j = (i + 1) % n, nrm = vcross(vsub(A[j], A[i]), vsub(B[i], A[i]));
      const mid = vadd(vadd(A[i], A[j]), vadd(B[i], B[j]));
      const s = sign || (vdot(nrm, vsub(mid, cen)) < 0 ? -1 : 1);
      const o = nrm.map(c => c * s);
      keys.push(vdot(o, [1, 1, 1]) <= 0 ? inner : (o[2] > Math.max(o[0], o[1]) ? top : outer));
    }
    let start = 0;
    if (closed) { while (start < m && keys[(start + m - 1) % m] === keys[start]) start++; if (start === m) start = 0; }
    const out = { back: '', front: '' };
    let i = 0;
    while (i < m) {
      const k = keys[(start + i) % m];
      let r = 0;
      while (i + r < m && keys[(start + i + r) % m] === k) r++;
      const idx = Array.from({ length: r + 1 }, (_, q) => (start + i + q) % n);
      const d = path([...idx.map(q => A[q]), ...idx.slice().reverse().map(q => B[q])]) + 'Z';
      out[k === inner ? 'back' : 'front'] += `<path class="${k}" d="${d}"/>`;
      i += r;
    }
    return out;
  };
  // Rounded-rectangle profile lying in the plane spanned by U (width) and V (height).
  const rrect = (O, U, V, w, h, r, n = 6) => {
    const at = (u, v) => vadd(vadd(O, U, u), V, v);
    const corners = [[w - r, r, -Math.PI / 2], [w - r, h - r, 0], [r, h - r, Math.PI / 2], [r, r, Math.PI]];
    const pts = [];
    for (const [cu, cv, a0] of corners) for (let i = 0; i <= n; i++) {
      const a = a0 + (Math.PI / 2) * i / n;
      pts.push(at(cu + r * Math.cos(a), cv + r * Math.sin(a)));
    }
    return pts;
  };
  const solid = (prof, vec, opt) => {
    const s = sheet(prof, prof.map(p => vadd(p, vec)), { closed: true, ...opt });
    const cap = vdot(vec, [1, 1, 1]) > 0 ? prof.map(p => vadd(p, vec)) : prof;
    return s.back + s.front + (opt.cap === false ? '' : `<path class="${opt.capCls || 'face'}" d="${path(cap)}Z"/>`);
  };
  const X = [1, 0, 0], Y = [0, 1, 0], Zu = [0, 0, 1];

  // Rear strap: an open band in a plane that tilts down toward the back, so it
  // reads as a head loop rather than a ring on the floor. It widens into the
  // rear cradle. `sign:-1` makes the lining the side facing the ring centre.
  const up = [0, -22 / 52.8, 48 / 52.8];
  const rA = [], rB = [];
  for (let i = 0; i <= 28; i++) {
    const t = Math.PI * i / 28, s = Math.sin(t);
    const c = [105 + 74.5 * Math.cos(t), 512 - 48 * s, 58 - 22 * s], w = 14 + 12 * s * s;
    rA.push(vadd(c, up, -w / 2)); rB.push(vadd(c, up, w / 2));
  }
  const rear = sheet(rA, rB, { sign: -1 });
  svg += rear.back + rear.front;
  svg += `<path class="detail" d="${path(rA.map((p, i) => vadd(p, vsub(rB[i], p), 0.3)))}"/>`;

  // Side arms: flat panels rounded in the y-z plane, reaching back past the
  // visor. The left one is mostly hidden; only its tail shows above the visor.
  const arm = (x, detail) => {
    let s = solid(rrect([x, 512, 40], Y, Zu, 96, 36, 12), [-7, 0, 0], {});
    if (detail) s += on(SIDE(x, 608, 76), `<rect class="detail" x="7" y="6" width="82" height="24" rx="12"/>` +
      `<rect class="face recess" x="60" y="14" width="22" height="8" rx="4"/>`);
    return s;
  };
  svg += arm(34, false);

  // Facial interface (foam gasket) behind the visor; only its top strip shows.
  svg += solid(rrect([40, 600, 18], X, Zu, 130, 58, 18), [0, -16, 0], { outer: 'face recess', top: 'face recess', capCls: 'face recess' });

  // Visor: rounded in elevation (a pillow), extruded back along y. A convex
  // extrusion's visible body is the hull of its two projected profiles, which
  // keeps it closed without the per-quad facing pass.
  const hull2d = pts => {
    const s = pts.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    const cross = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    const half = list => list.reduce((h, p) => {
      while (h.length > 1 && cross(h[h.length - 2], h[h.length - 1], p) <= 0) h.pop();
      return [...h, p];
    }, []);
    const lo = half(s), hi = half(s.slice().reverse());
    return [...lo.slice(0, -1), ...hi.slice(0, -1)];
  };
  const visor = rrect([30, 650, 14], X, Zu, 150, 64, 20);
  const body = hull2d([...visor, ...visor.map(p => vadd(p, [0, -50, 0]))].map(p => P(...p)));
  svg += `<path class="face top" d="M${body.map(p => p.map(f).join(' ')).join('L')}Z"/>`;
  svg += `<path class="detail" d="${path([[180, 650, 34], [180, 600, 34]])}"/>`;
  svg += `<path class="detail" d="${path([[160, 650, 78], [160, 600, 78]])}"/>`;
  svg += rect(FRONT(30, 650, 78), 150, 64, 20);
  svg += on(FRONT(30, 650, 78), `<rect class="detail" x="8" y="6" width="134" height="52" rx="18"/>` +
    `<rect class="detail" x="31" y="17" width="10" height="30" rx="5"/><rect class="detail" x="109" y="17" width="10" height="30" rx="5"/>` +
    `<circle class="detail" cx="75" cy="32" r="3.2"/>` +
    `<rect class="pod" x="56" y="49" width="38" height="7" rx="3.5"/><rect class="pod-on ping-pod" x="58.5" y="50.5" width="33" height="4" rx="2" style="${delay(0.92)}"/>`);

  // Top strap: a flat band from the visor's top face arching back and down
  // onto the rear cradle.
  const tA = [], tB = [];
  for (let i = 0; i <= 20; i++) {
    const s = i / 20, q = [105, 640 - 181 * s, 78 - 30 * s + 46 * Math.sin(Math.PI * s)];
    tA.push(vadd(q, X, -10)); tB.push(vadd(q, X, 10));
  }
  const topStrap = sheet(tA, tB, { sign: 1 });
  svg += topStrap.back + topStrap.front;
  svg += `<path class="detail" d="${path(tA.map(p => vadd(p, X, 10)))}"/>`;

  svg += arm(183, true);
}

// CLI harness.
svg += wire([[730, 260, Z], [730, 300, Z]], 0.05);
svg += wire([[890, 300, Z], [890, 262, Z]], 0.50);
svg += wire([[770, 326, Z], [770, 360, Z]], 0.20);
svg += wire([[850, 360, Z], [850, 326, Z]], 0.36);

svg += box(680, 210, Z, 100, 50, 38);
svg += on(FRONT(680, 260, 48), `<rect class="face recess" x="10" y="8" width="58" height="22" rx="2"/>` +
  `<text class="label" x="16" y="23" font-size="9">steps.json</text>` + led(84, 18, 3, 0.05));
svg += box(840, 205, Z, 100, 57, 62);
svg += on(FRONT(840, 262, 72), (() => {
  let s = `<rect class="halo" filter="url(#bloom)" x="8" y="7" width="84" height="46"/>`;
  s += `<rect class="glass" x="8" y="7" width="84" height="46" rx="3"/>`;
  ['> PPEStateChanged', '> ActionAttempted', '> TaskCompleted', '> ScoreChanged'].forEach((t, i) => {
    s += `<text class="scr term ping-line" x="13" y="${18 + i * 10}" font-size="7" style="${delay(0.58 + i * 0.02)}">${esc(t)}</text>`;
  });
  return s;
})());

svg += box(680, 300, Z, 260, 26, 12);
svg += on(TOP(680, 300, 22), (() => {
  let s = `<rect class="lane" x="8" y="5" width="244" height="16" rx="3"/>`;
  for (let i = 0; i < 8; i++) s += `<rect class="cell ping-cell" x="${14 + i * 30}" y="9" width="20" height="8" rx="1.5" style="${delay(((i / 8) + 0.5) % 1 || 1)}"/>`;
  return s;
})());

svg += chip(730, 360, 160, 65, 14, [['Shared.dll', 13, 'scr']], 0.28);

// Any machine with the .NET SDK runs the harness headless: a laptop at a shell.
svg += wire([[1000, 222, 3], [960, 222, 10], [944, 222, 10]], 0.97);
svg += box(1004, 142, 6, 132, 8, 98);
svg += on(FRONT(1004, 150, 104), (() => {
  let s = `<rect class="halo" filter="url(#bloom)" x="8" y="8" width="116" height="78"/>`;
  s += `<rect class="glass" x="8" y="8" width="116" height="78" rx="3"/>`;
  [['$ dotnet run', 0.97], ['  scenario.json', 0.97], ['ActionAttempted', 0.05],
    ['TaskCompleted', 0.38], ['score 92 · exit 0', 0.62]].forEach(([t, ph], i) => {
    s += `<text class="scr term ping-line" x="14" y="${22 + i * 13}" font-size="9" style="${delay(ph)}">${esc(t)}</text>`;
  });
  return s;
})());
svg += box(1000, 150, 0, 140, 86, 6, 6);
svg += on(TOP(1000, 150, 6), `<rect class="face recess" x="12" y="8" width="116" height="40" rx="3"/>` +
  [14, 22, 30, 38].map(v => `<line class="detail" x1="16" y1="${v}" x2="124" y2="${v}"/>`).join('') +
  `<rect class="detail" x="48" y="56" width="44" height="24" rx="3"/>`);

// Labels sit above every shape so no face can hide them.
tag([90, 75, 60], 'PPEManager', 'physics → PPE events', { lift: 36 });
tag([230, 75, 57], 'Scene emitters', 'ActionAttempted', { lift: 38 });
tag([370, 75, 60], 'SessionManager', 'start · pause · end', { lift: 36 });
tag([510, 75, 60], 'Dashboard bootstrap', 'HTTP + WebSocket', { lift: 36 });
tag([750, 81, 170], 'Evaluator browser', 'read-only telemetry', { lift: 24 });
tag([40, 170, 26], 'EventBus', 'every system talks only here', { lift: 92, dx: -40, anchor: 'end' });
tag([80, 345, 50], 'UI · HUD & popups', null, { lift: 30, dx: -60 });
tag([290, 420, 10], 'SafetyProto.Shared.dll', 'domain core · zero UnityEngine', { side: 'down', lift: 56 });
tag([470, 390, 10], 'SessionLogger', 'session log → JSON', { side: 'down', lift: 62, dx: -30, anchor: 'end' });
tag([730, 235, 48], 'ScriptedActor', 'producer', { lift: 30, dx: -20 });
tag([890, 233, 72], 'TranscriptRecorder', 'observer', { lift: 30, dx: 20 });
tag([940, 313, 22], 'HarnessEventBus', null, { side: 'down', lift: 30, dx: 46, anchor: 'start' });
tag([630, 436, Z], 'same compiled assembly', null, { side: 'down', lift: 92, dx: -20, anchor: 'end' });
tag([105, 650, 14], 'Meta Quest 3', 'runs the Unity build', { side: 'down', lift: 64 });
tag([1070, 236, 0], '.NET 10 machine', 'runs the harness headless', { side: 'down', lift: 70 });


const titles = `
<text class="title" x="40" y="58">FIG 1 · EVENT-DRIVEN ARCHITECTURE</text>
<text class="sub" x="40" y="86">one typed event bus · one shared domain assembly · two runtimes</text>
`;

const legend = (() => {
  const x = 34, y = K.H - 84;
  return `<g transform="translate(${x} ${y})">
  <line class="legend-pulse" x1="0" y1="-4" x2="26" y2="-4" filter="url(#glow)"/><text class="sub" x="38" y="0">typed event in flight</text>
  <rect class="legend-chip" x="0" y="16" width="26" height="12" rx="2"/><text class="sub" x="38" y="27">domain logic, no UnityEngine reference</text>
  <line class="link" x1="0" y1="50" x2="26" y2="50"/><text class="sub" x="38" y="54">one source, compiled into both runtimes</text>
</g>`;
})();

const READS = [
  [0.00, 0.22, 'ActionAttempted → bus'],
  [0.22, 0.36, 'SafetyRuleEngineCore · validate'],
  [0.36, 0.48, 'TaskCompleted → bus'],
  [0.48, 0.72, 'ScoreChanged → UI · log · dashboard'],
  [0.72, 1.00, 'dashboard · score 92'],
];
let readCss = '';
const readout = READS.map(([a, b, t], i) => {
  const w = f((b - a) * 100 - 0.3);
  readCss += `@keyframes rd${i}{0%,${w}%{opacity:1}${f(w + 0.01)}%,100%{opacity:0}}.rd${i}{animation:rd${i} ${T}s steps(1,end) infinite;${delay(a || 1)}}`;
  return `<text class="readout rd${i}" x="${f(K.W - 34)}" y="${f(K.H - 34)}" text-anchor="end">${esc(t)}</text>`;
}).join('');

const cssFor = C_ => `
.panel{fill:${C_.bg}}
.face{fill:${C_.body};stroke:${C_.line};stroke-width:1;vector-effect:non-scaling-stroke;stroke-linejoin:round}
.face.top{fill:${C_.deck}}
.face.recess{fill:${C_.recess}}
.face.lining{fill:${C_.recess}}
.detail{fill:none;stroke:${C_.detail};stroke-width:1;vector-effect:non-scaling-stroke}
text{font-family:'IBM Plex Mono',ui-monospace,SFMono-Regular,Consolas,Menlo,monospace}
.label{fill:${C_.dim};letter-spacing:.04em}
.glass{fill:${C_.glass};stroke:${C_.accent};stroke-width:1;vector-effect:non-scaling-stroke}
.halo{${C_.glow ? `fill:${C_.accent};opacity:.22` : 'display:none'}}
.scr{fill:${C_.accentInk}}
.scr.dim{fill:${C_.accent}}
.scr-line,.doc{stroke:${C_.accent};stroke-width:1.4;vector-effect:non-scaling-stroke}
.die{fill:${C_.accent}}
.lane{fill:${C_.recess};stroke:${C_.detail};stroke-width:1;vector-effect:non-scaling-stroke}
.cell{fill:${C_.accent}}
.led{fill:${C_.detail}}
.pod{fill:${C_.recess};stroke:${C_.line};stroke-width:1;vector-effect:non-scaling-stroke}
.pod-on{fill:${C_.accent}}
.ping-pod{opacity:.45;animation:pingpod ${T}s linear infinite}
@keyframes pingpod{0%,12%{opacity:1}40%,100%{opacity:.45}}
.led-on{fill:${C_.accent}}
.wire{fill:none;stroke:${C_.wire};stroke-width:1.4;vector-effect:non-scaling-stroke;stroke-linecap:round;stroke-linejoin:round}
.wire.faint{stroke:none}
.pulse{fill:none;stroke:${C_.pulse};stroke-width:3;stroke-linecap:round;vector-effect:non-scaling-stroke;opacity:0;${C_.glow ? 'filter:url(#glow);' : ''}animation:flow ${T}s linear infinite}
@keyframes flow{0%{stroke-dashoffset:0;opacity:1}${TRAVEL * 100}%{stroke-dashoffset:-1000;opacity:1}${TRAVEL * 100 + 0.01}%,100%{stroke-dashoffset:-1000;opacity:0}}
.ping{opacity:.12;animation:ping ${T}s linear infinite}
.ping-line,.ping-num{animation:pingtxt ${T}s linear infinite}
.ping-cell{opacity:.25;animation:pingcell ${T}s linear infinite}
.ping-soft{opacity:.14;animation:pingsoft ${T}s linear infinite}
.ping-cap .face{animation:capfill ${T}s linear infinite;animation-delay:inherit}
@keyframes ping{0%,10%{opacity:1}30%,100%{opacity:.12}}
@keyframes pingtxt{0%,14%{opacity:1}40%{opacity:.55}100%{opacity:.55}}
@keyframes pingcell{0%,6%{opacity:1}24%,100%{opacity:.22}}
@keyframes pingsoft{0%,18%{opacity:.34}50%,100%{opacity:.12}}
@keyframes capfill{0%,8%{fill:${C_.accent};stroke:${C_.accentInk}}22%,100%{fill:${C_.body};stroke:${C_.line}}}
.blink{animation:blink 1.6s steps(1,end) infinite}
@keyframes blink{0%{opacity:1}50%{opacity:.2}}
.bar-bg{fill:${C_.accentWeak}}
.bar{fill:${C_.accent}}
.grow{transform-box:fill-box;transform-origin:left center;animation:grow ${T}s ease-out infinite}
@keyframes grow{0%{transform:scaleX(.6)}20%,100%{transform:scaleX(1)}}
.link{fill:none;stroke:${C_.accent};stroke-width:1.4;stroke-dasharray:6 6;vector-effect:non-scaling-stroke;opacity:.45}
.link-flow{fill:none;stroke:${C_.accent};stroke-width:1.4;stroke-dasharray:6 6;vector-effect:non-scaling-stroke;animation:march 1.2s linear infinite}
@keyframes march{to{stroke-dashoffset:-24}}
.lead{stroke:${C_.wire};stroke-width:1;vector-effect:non-scaling-stroke}
.lead-dot{fill:${C_.dim}}
.tag,.sub,.title,.zone,.readout{paint-order:stroke;stroke:${C_.bg};stroke-width:5px;stroke-linejoin:round}
.tag{fill:${C_.text};font-size:20px;font-weight:600}
.sub{fill:${C_.muted};font-size:15px}
.title{fill:${C_.text};font-size:24px;font-weight:600;letter-spacing:.06em}
.zone{fill:${C_.dim};font-size:15px;letter-spacing:.14em}
.readout{fill:${C_.accent};font-size:18px;font-weight:600}
.legend-pulse{stroke:${C_.pulse};stroke-width:3;stroke-linecap:round}
.legend-chip{fill:${C_.accent};opacity:.6}
@media (prefers-reduced-motion:reduce){*{animation:none!important}.pulse{opacity:0}.readout:not(.rd0){display:none}}
${readCss}`;

const filters = glow => glow
  ? `<filter id="glow" filterUnits="userSpaceOnUse" x="-2000" y="-2000" width="6000" height="6000"><feGaussianBlur stdDeviation="3" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
<filter id="bloom" filterUnits="userSpaceOnUse" x="-2000" y="-2000" width="6000" height="6000"><feGaussianBlur stdDeviation="9"/></filter>`
  : `<filter id="glow" filterUnits="userSpaceOnUse" x="-2000" y="-2000" width="6000" height="6000"><feMerge><feMergeNode in="SourceGraphic"/></feMerge></filter>
<filter id="bloom" filterUnits="userSpaceOnUse" x="-2000" y="-2000" width="6000" height="6000"><feFlood flood-opacity="0"/></filter>`;

mkdirSync(OUT_DIR, { recursive: true });
for (const [name, C_] of Object.entries(THEMES)) {
const out = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${K.W} ${K.H}" width="${Math.round(K.W)}" height="${Math.round(K.H)}" role="img" aria-label="Isometric diagram of the event-driven architecture: Unity producers and consumers communicate only through a typed EventBus, the domain logic is one shared assembly, and a .NET CLI harness runs the same assembly.">
<title>safety-training-proto architecture</title>
<defs>
<style>${cssFor(C_)}</style>
${filters(C_.glow)}
</defs>
<rect class="panel" width="${K.W}" height="${K.H}" rx="18"/>
${svg}
${tags.join('\n')}
${titles}
${legend}
${readout}
</svg>
`;

const file = join(OUT_DIR, `architecture-${name}.svg`);
writeFileSync(file, out);
console.log(`wrote ${file} (${(out.length / 1024).toFixed(1)} KiB, ${K.W}×${K.H})`);
}
