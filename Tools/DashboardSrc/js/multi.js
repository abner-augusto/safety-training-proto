    import { t, toggleLang, getLang, applyTranslations, pauseSourceLabel } from './i18n.js';
    import { applyTheme, toggleTheme } from './theme.js';

    /* ================================================================
     * MULTI-HEADSET SHELL (multi.html) — one station page per headset in
     * an iframe, side by side. Each station page keeps its own WebSocket
     * and controls; this shell only lays them out, forwards theme and
     * language, and turns each station's status message into a tile bar
     * an evaluator running two headsets alone can read at a glance.
     * ============================================================== */
    const STORAGE_KEY = 'sp_stations';
    const DEFAULT_HTTP_PORT = 8080;
    const CRITICAL_RECENT_MS = 120_000;
    const SILENT_AFTER_MS = 12_000;

    const grid = document.getElementById('mx-grid');
    const tiles = new Map();   // host -> { el, iframe, status, lastStatusAt }
    let stations = [];
    let focusedHost = null;

    function normalizeHost(raw) {
      let host = String(raw ?? '').trim().replace(/^https?:\/\//i, '').replace(/\/.*$/, '');
      if (!host) return '';
      if (!/:\d+$/.test(host)) host += `:${DEFAULT_HTTP_PORT}`;
      return host;
    }

    function loadStations() {
      const fromUrl = new URLSearchParams(window.location.search).get('hosts');
      if (fromUrl) {
        stations = [...new Set(fromUrl.split(',').map(normalizeHost).filter(Boolean))];
        saveStations();
        return;
      }
      try { stations = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]'); } catch { stations = []; }
      if (!Array.isArray(stations)) stations = [];
      stations = stations.map(normalizeHost).filter(Boolean);
      if (stations.length === 0 && window.location.host) stations = [normalizeHost(window.location.host)];
    }

    function saveStations() {
      try { localStorage.setItem(STORAGE_KEY, JSON.stringify(stations)); } catch { /* private mode */ }
    }

    function stationUrl(host, embed) {
      return `${window.location.protocol === 'https:' ? 'https:' : 'http:'}//${host}/${embed ? '?embed=1' : ''}`;
    }

    function postTo(iframe, message) {
      try { iframe.contentWindow?.postMessage(message, '*'); } catch { /* frame not loaded yet */ }
    }

    function broadcastPreferences() {
      for (const tile of tiles.values()) sendPreferences(tile.iframe);
    }

    function sendPreferences(iframe) {
      postTo(iframe, { type: 'sp-theme', theme: document.documentElement.dataset.theme || 'dark' });
      postTo(iframe, { type: 'sp-lang', lang: getLang() });
    }

    function buildTile(host) {
      const el = document.createElement('section');
      el.className = 'mx-tile';
      el.innerHTML = `
        <div class="mx-tile-bar">
          <span class="status-dot"></span>
          <span class="mx-tile-title"></span>
          <span class="mx-tile-host"></span>
          <span class="mx-tile-sub"></span>
          <span class="mx-badges"></span>
          <span class="mx-tile-actions">
            <button type="button" data-act="max"></button>
            <a target="_blank" rel="noopener" data-act="open"></a>
            <button type="button" data-act="remove"></button>
          </span>
        </div>
        <iframe loading="eager"></iframe>`;
      el.querySelector('.mx-tile-host').textContent = host;
      el.querySelector('[data-act="open"]').href = stationUrl(host, false);
      const iframe = el.querySelector('iframe');
      iframe.title = host;
      iframe.src = stationUrl(host, true);
      iframe.addEventListener('load', () => sendPreferences(iframe));
      el.querySelector('[data-act="max"]').onclick = () => {
        focusedHost = focusedHost === host ? null : host;
        layout();
      };
      el.querySelector('[data-act="remove"]').onclick = () => {
        stations = stations.filter(h => h !== host);
        if (focusedHost === host) focusedHost = null;
        saveStations();
        render();
      };
      return { el, iframe, status: null, lastStatusAt: 0 };
    }

    function render() {
      for (const [host, tile] of tiles) {
        if (!stations.includes(host)) { tile.el.remove(); tiles.delete(host); }
      }
      grid.querySelector('.mx-empty')?.remove();
      for (const host of stations) {
        if (!tiles.has(host)) {
          const tile = buildTile(host);
          tiles.set(host, tile);
          grid.appendChild(tile.el);
        }
      }
      if (stations.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'mx-empty';
        empty.textContent = t('multiEmpty');
        grid.appendChild(empty);
      }
      layout();
    }

    function layout() {
      grid.style.setProperty('--mx-cols', String(Math.min(Math.max(stations.length, 1), 2)));
      grid.classList.toggle('focused', focusedHost !== null && tiles.has(focusedHost));
      for (const [host, tile] of tiles) {
        tile.el.classList.toggle('max', host === focusedHost);
        tile.el.querySelector('[data-act="max"]').textContent = host === focusedHost ? t('multiCollapse') : t('multiExpand');
        tile.el.querySelector('[data-act="open"]').textContent = t('multiOpen');
        tile.el.querySelector('[data-act="remove"]').textContent = t('multiRemove');
        renderTileStatus(tile);
      }
    }

    function badge(text, color) {
      const b = document.createElement('span');
      b.className = `mx-badge ${color}`;
      b.textContent = text;
      return b;
    }

    function renderTileStatus(tile) {
      const s = tile.status;
      const silent = !s || Date.now() - tile.lastStatusAt > SILENT_AFTER_MS;
      const connected = !silent && s.connected;
      const dot = tile.el.querySelector('.status-dot');
      dot.className = 'status-dot' + (connected ? ' connected' : silent ? '' : ' connecting');
      tile.el.querySelector('.mx-tile-title').textContent = (s && s.deviceModel) || '—';

      const modeName = s && s.mode ? t(s.mode === 'avaliacao' ? 'modeEvaluation' : 'modeGuided') : '';
      const pid = s && s.participantId && s.participantId !== '—' ? s.participantId : '';
      tile.el.querySelector('.mx-tile-sub').textContent =
        s && s.sessionActive ? [pid, modeName].filter(Boolean).join(' · ')
        : s && s.sessionCompleted ? `${pid} · ${t('stationCompleted')}`
        : '';

      const badges = tile.el.querySelector('.mx-badges');
      badges.replaceChildren();
      let level = connected && s.sessionActive ? 'ok' : '';
      if (!connected) { badges.append(badge(t('multiDisconnected'), 'red')); level = 'alert'; }
      if (s && connected) {
        if (s.lastCriticalAt && Date.now() - s.lastCriticalAt < CRITICAL_RECENT_MS) { badges.append(badge(t('multiCritical'), 'red')); level = 'alert'; }
        if (s.pauseSources && s.pauseSources.length) {
          badges.append(badge(`${t('paused')} · ${s.pauseSources.map(pauseSourceLabel).join(' + ')}`, 'orange'));
          if (level !== 'alert') level = 'caution';
        }
        if (s.participantKeypadOpen) { badges.append(badge(t('multiKeypad'), 'orange')); if (level !== 'alert') level = 'caution'; }
        else if (s.awaitingStart) badges.append(badge(t('multiWaiting'), 'blue'));
        const charging = s.batteryStatus === 'Charging' || s.batteryStatus === 'Full';
        if (s.batteryLevel >= 0) {
          const pct = `${t('battery')} ${Math.round(s.batteryLevel * 100)}%${charging ? ` · ${t('charging')}` : ''}`;
          if (!charging && s.batteryLevel < 0.2) { badges.append(badge(pct, 'red')); level = 'alert'; }
          else if (!charging && s.batteryLevel < 0.35) badges.append(badge(pct, 'orange'));
          else badges.append(badge(pct, 'blue'));
        }
      }
      tile.el.classList.remove('ok', 'caution', 'alert');
      if (level) tile.el.classList.add(level);
    }

    window.addEventListener('message', e => {
      for (const tile of tiles.values()) {
        if (tile.iframe.contentWindow !== e.source) continue;
        const m = e.data;
        if (m?.type === 'sp-ready') sendPreferences(tile.iframe);
        else if (m?.type === 'sp-status') {
          tile.status = m.status;
          tile.lastStatusAt = Date.now();
          renderTileStatus(tile);
        }
        return;
      }
    });

    /* A station page that stops reporting (headset off, app closed) turns red. */
    setInterval(() => { for (const tile of tiles.values()) renderTileStatus(tile); }, 3000);

    document.getElementById('mx-add').onsubmit = e => {
      e.preventDefault();
      const input = document.getElementById('mx-host');
      const host = normalizeHost(input.value);
      if (!host) return;
      if (!stations.includes(host)) stations.push(host);
      saveStations();
      input.value = '';
      render();
    };

    function translateShell() {
      applyTranslations();
      document.querySelectorAll('[data-i18n-placeholder]').forEach(el => { el.placeholder = t(el.dataset.i18nPlaceholder); });
      document.title = `SafetyProto — ${t('multiTitle')}`;
      layout();
    }

    document.getElementById('btn-lang').onclick = () => {
      toggleLang();
      translateShell();
      broadcastPreferences();
    };
    document.getElementById('btn-theme').onclick = () => {
      toggleTheme();
      broadcastPreferences();
    };

    applyTheme();
    loadStations();
    render();
    translateShell();
