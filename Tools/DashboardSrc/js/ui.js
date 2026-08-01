    import { t, getLang, locale, ppeLabel, violationLabel, violationMessage } from './i18n.js';
    import { MAX_LOG_ITEMS } from './constants.js';
    import { tasks, wornPpe, state } from './state.js';

    /* ================================================================
     * UI HELPERS
     * ============================================================== */
    /** Escape text before interpolating it into innerHTML (session data is untrusted). */
    export function esc(s) {
      return String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    export function setStatusUI(state) {
      const dot   = document.getElementById('status-dot');
      const label = document.getElementById('conn-status-label');
      dot.className = 'status-dot' + (state === 'connected' ? ' connected' : state === 'connecting' ? ' connecting' : '');
      label.textContent = t(state === 'connected' ? 'connected' : state === 'connecting' ? 'connecting' : 'disconnected');
    }

    export function setScoreUI(score) {
      state.currentScore = score;
      const el = document.getElementById('score-value');
      el.textContent = score;
      el.classList.remove('bump');
      void el.offsetWidth; // reflow to retrigger animation
      el.classList.add('bump');
      setTimeout(() => el.classList.remove('bump'), 180);
    }

    // Neutral institutional placeholder mark for empty states (replaces emoji).
    const EMPTY_ICON = '<svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v6"/><circle cx="12" cy="16.5" r="0.6" fill="currentColor" stroke="none"/></svg>';

    export function emptyState(_icon, text) {
      return `<div class="empty-state"><div class="empty-state-icon">${EMPTY_ICON}</div><div>${text}</div></div>`;
    }

    /** Typed glyph (gutter column) for a report event — same vocabulary
        as the live log, no emoji. */
    function eventGlyph(name) {
      const n = (name ?? '').toLowerCase();
      if (n.includes('critical'))         return '▲';
      if (n.includes('violation'))        return '▲';
      if (n.includes('safetyerror'))      return '✕';
      if (n.includes('tasktimeout'))      return '✕';
      if (n.includes('taskcompleted'))    return '✔';
      if (n.includes('groupcompleted'))   return '✔';
      if (n.includes('sessioncompleted')) return '⚑';
      if (n.includes('taskstarted'))      return '▶';
      if (n.includes('groupstarted'))     return '▶';
      return '●';
    }

    /* ── Cluster: violations + PPE worn/required ── */
    export function renderCluster() {
      const violEl = document.getElementById('viol-value');
      if (violEl) violEl.textContent = state.violationCount;

      const required = new Set();
      tasks.forEach(tk => (tk.requiredPpe ?? []).forEach(p => required.add(String(p))));

      const ppeEl = document.getElementById('ppe-value');
      if (!ppeEl) return;
      if (required.size > 0) {
        let worn = 0;
        required.forEach(p => { if (wornPpe.has(p)) worn++; });
        ppeEl.textContent = `${worn}/${required.size}`;
      } else {
        ppeEl.textContent = wornPpe.size > 0 ? String(wornPpe.size) : '—';
      }
    }

    /* Localized display label for a logged event name (JSON keeps English). */
    function eventLabel(name) {
      const labels = {
        pt: {
          SessionStarted: 'Sessão iniciada',
          SessionPaused: 'Sessão pausada',
          SessionResumed: 'Sessão retomada',
          SessionEnded: 'Sessão encerrada',
          SessionCompleted: 'Sessão concluída',
          SessionReset: 'Sessão reiniciada',
          ActionAttempt: 'Tentativa de ação',
          PpeStateChanged: 'EPI alterado',
          TaskStarted: 'Tarefa iniciada',
          TaskCompleted: 'Tarefa concluída',
          ScoreChanged: 'Pontuação alterada',
          GroupStarted: 'Grupo iniciado',
          GroupCompleted: 'Grupo concluído',
          SafetyViolation: 'Violação de segurança',
          SafetyError: 'Erro de segurança',
          CriticalSafetyFailure: 'Falha crítica de segurança',
        },
        en: {
          SessionStarted: 'Session started',
          SessionPaused: 'Session paused',
          SessionResumed: 'Session resumed',
          SessionEnded: 'Session ended',
          SessionCompleted: 'Session completed',
          SessionReset: 'Session reset',
          ActionAttempt: 'Action attempt',
          PpeStateChanged: 'PPE changed',
          TaskStarted: 'Task started',
          TaskCompleted: 'Task completed',
          ScoreChanged: 'Score changed',
          GroupStarted: 'Group started',
          GroupCompleted: 'Group completed',
          SafetyViolation: 'Safety violation',
          SafetyError: 'Safety error',
          CriticalSafetyFailure: 'Critical safety failure',
        },
      };
      const table = labels[getLang()] || labels.en;
      return table[name] ?? `${t('unknownEvent')} (${name || '—'})`;
    }

    function formatLegacyDetails(entry) {
      const details = entry.details || '';
      switch (entry.eventName) {
        case 'SessionReset':
          return t('manualReset');
        case 'PpeStateChanged': {
          const match = details.match(/(?:PPE|EPI)=([^,]+),\s*(?:Wearing|Equipado)=(True|False|Sim|Não)/i);
          if (!match) return '';
          const wearing = /^(True|Sim)$/i.test(match[2]);
          return `${t('detailPpe')}: ${ppeLabel(match[1])} · ${wearing ? t('detailWorn') : t('detailRemoved')}`;
        }
        case 'ScoreChanged': {
          const match = details.match(/(?:Delta|Variação)=(-?\d+),\s*Total=(-?\d+)/i);
          return match ? `${t('detailDelta')}: ${Number(match[1]) >= 0 ? '+' : ''}${match[1]} · ${t('detailTotal')}: ${match[2]}` : '';
        }
        case 'SafetyViolation': {
          const code = details.split('|')[0].trim();
          return violationLabel(code);
        }
        case 'SafetyError': {
          const source = details.split(':')[0].trim();
          return `${t('internalError')}${source ? ` · ${t('origin')}: ${source}` : ''}`;
        }
        case 'CriticalSafetyFailure': {
          const match = details.match(/\[(\d+)\s+(?:in|em)\s+([\d.,]+)s\]/i);
          return match ? `${match[1]} ${t('violations').toLowerCase()} ${t('detailIn')} ${match[2]}s` : t('logCritical');
        }
        case 'SessionCompleted':
          return '';
        default:
          return '';
      }
    }

    /* Localized detail line from structured entry.data (falls back to entry.details
       for older saved reports that predate the structured schema). */
    function formatDetails(entry) {
      const d = entry.data;
      if (!d) return formatLegacyDetails(entry);
      switch (entry.eventName) {
        case 'PpeStateChanged':
          return `${t('detailPpe')}: ${ppeLabel(d.ppeType)} · ${d.wearing ? t('detailWorn') : t('detailRemoved')}`;
        case 'ScoreChanged':
          return `${t('detailDelta')}: ${d.delta >= 0 ? '+' : ''}${d.delta} · ${t('detailTotal')}: ${d.totalScore}`;
        case 'SafetyViolation':
          return `${violationLabel(d.violationCode)}${d.message ? ` · ${violationMessage(d.violationCode, d.message)}` : ''}`;
        case 'SafetyError':
          return `${t('internalError')}${d.source ? ` · ${t('origin')}: ${d.source}` : ''}`;
        case 'CriticalSafetyFailure':
          return `${d.violationCount} ${t('violations').toLowerCase()} ${t('detailIn')} ${d.windowSeconds}s`;
        case 'SessionReset':
          return t('manualReset');
        case 'SessionCompleted': {
          const m = Math.floor((d.totalElapsedTime ?? 0) / 60);
          const s = Math.floor((d.totalElapsedTime ?? 0) % 60);
          return `${t('detailTime')}: ${m}:${String(s).padStart(2, '0')} · ${t('detailScore')}: ${d.totalScore} · ${t('detailCompleted')}: ${d.tasksCompleted}/${d.totalTasks}`;
        }
        default:
          return '';
      }
    }

    export function resetTaskUI() {
      document.getElementById('task-list').innerHTML = emptyState('', t('waitingSession'));
    }

    /* ── Task renderer — manifesto de procedimento: grupos com
       cabeçalho (modo de execução + progresso) e linhas numeradas
       num trilho de glifos. A tarefa ativa é a única expandida. ── */
    const STATUS_GLYPH = { completed: '✔', active: '▶', pending: '·', not_performed: '✕' };

    function modeLabel(mode) {
      const m = String(mode ?? '').toLowerCase();
      if (m.startsWith('seq'))  return t('modeSequential');
      if (m.startsWith('free')) return t('modeFreeOrder');
      return '';
    }

    export function renderTasks() {
      const container = document.getElementById('task-list');
      if (tasks.size === 0) { resetTaskUI(); renderCluster(); return; }

      /* agrupa por groupName preservando a ordem das tarefas */
      const groups = new Map();
      [...tasks.values()]
        .sort((a, b) => (a.order ?? 999) - (b.order ?? 999))
        .forEach(tk => {
          const g = tk.groupName || '—';
          if (!groups.has(g)) groups.set(g, []);
          groups.get(g).push(tk);
        });

      container.innerHTML = '';
      let gi = 0;
      groups.forEach((list, groupName) => {
        gi++;
        const done = list.filter(tk => tk.status === 'completed').length;
        const mode = modeLabel(list.find(tk => tk.executionMode)?.executionMode);

        const header = document.createElement('div');
        header.className = 'mg-h';
        header.innerHTML = `
          <b>${t('group')} ${String(gi).padStart(2, '0')} · ${esc(groupName)}</b>
          <span>${mode ? `<span class="mg-mode">${mode}</span> · ` : ''}<span class="mg-count">${done}/${list.length}</span></span>`;
        container.appendChild(header);

        list.forEach((task, idx) => {
          if (state.hideCompleted && (task.status === 'completed' || task.status === 'not_performed')) return;

          // Uma tarefa não realizada não subtrai pontos — perde os que valia. O '✕' marca
          // o desfecho sem anunciar uma penalidade que não existe.
          let pt = '';
          if (task.status === 'active')             pt = t('active').toUpperCase();
          else if (task.status === 'not_performed') pt = '✕';
          else if (task.successPoints)              pt = `+${task.successPoints}`;

          let expand = '';
          if (task.status === 'active') {
            const chips = [];
            (task.requiredPpe ?? []).forEach(p => chips.push(`<span class="chip">${t('detailPpe')}: ${esc(ppeLabel(p))}</span>`));
            if (task.ppePenalty)     chips.push(`<span class="chip pen">−${task.ppePenalty} ${t('detailPpe')}</span>`);
            expand = `
              <div class="tr-x">
                ${task.description ? `<p>${esc(task.description)}</p>` : ''}
                ${chips.length ? `<div class="chips">${chips.join('')}</div>` : ''}
              </div>`;
          }

          const row = document.createElement('div');
          row.className = `tr ${task.status}`;
          row.innerHTML = `
            <span class="g">${STATUS_GLYPH[task.status] ?? '·'}</span>
            <span class="ix">${String(idx + 1).padStart(2, '0')}</span>
            <span class="nm">${esc(task.name)}</span>
            <span class="pt">${pt}</span>
            ${expand}`;
          container.appendChild(row);
        });
      });
      renderCluster();
    }

    /* ── Log: hora · glifo tipado · mensagem ── */
    const LOG_GLYPH = { success: '✔', violation: '▲', info: '●' };

    export function addLog(message, type = 'info') {
      const container = document.getElementById('log-container');
      const empty = container.querySelector('.empty-state');
      if (empty) container.innerHTML = '';

      const item = document.createElement('div');
      item.className = `log-item ${type}`;
      const time = new Date().toLocaleTimeString(locale(), { hour12: false });
      item.innerHTML = `<span class="log-time">${time}</span><span class="g">${LOG_GLYPH[type] ?? '●'}</span><span>${esc(message)}</span>`;
      container.insertBefore(item, container.firstChild);

      while (container.children.length > MAX_LOG_ITEMS) {
        container.removeChild(container.lastChild);
      }
    }

    /* ── Report Section — documento carimbado (régua dupla) ── */
    export function renderReportSection(report) {
      state.sessionReport = report;
      const summary = report.summary ?? {};
      const mins    = Math.floor((summary.totalElapsedTime ?? 0) / 60);
      const secs    = Math.floor((summary.totalElapsedTime ?? 0) % 60);
      const when    = new Date().toLocaleString(locale(),
        { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false });

      document.getElementById('report-section').innerHTML = `
        <div class="report-card"><div class="report-card-in">
          <div class="report-head">
            <b>${esc(state.currentParticipantId)}</b>
            <span>${esc(when)}</span>
          </div>
          <div class="report-summary">
            <div>
              <div class="report-stat-value">${mins}:${String(secs).padStart(2,'0')}</div>
              <div class="report-stat-label">${t('duration')}</div>
            </div>
            <div>
              <div class="report-stat-value">${summary.totalScore ?? 0}</div>
              <div class="report-stat-label">${t('finalScore')}</div>
            </div>
            <div>
              <div class="report-stat-value">${summary.tasksCompleted ?? 0}/${summary.totalTasks ?? 0}</div>
              <div class="report-stat-label">${t('tasks')}</div>
            </div>
          </div>
          ${summary.completed ? `<div class="report-stamp">${t('sessionRecorded')}</div>` : ''}
          <div class="report-actions">
            <button class="report-btn primary" id="btn-view-report">${t('viewFullReport')}</button>
            <button class="report-btn secondary" id="btn-dl-report">${t('downloadJson')}</button>
          </div>
        </div></div>
      `;

      document.getElementById('btn-view-report').onclick = () => openReportModal();
      document.getElementById('btn-dl-report').onclick   = downloadReport;
    }

    export function openReportModal(report) {
      // Defaults to the live report; history rows pass their own stored report.
      report = (report && report.summary) ? report : state.sessionReport;
      if (!report) return;
      const summary = report.summary ?? {};
      const entries = report.entries ?? [];
      const mins = Math.floor((summary.totalElapsedTime ?? 0) / 60);
      const secs = Math.floor((summary.totalElapsedTime ?? 0) % 60);

      const timelineHtml = entries.map(entry => {
        const time  = new Date(entry.timestamp).toLocaleTimeString(locale(), { hour12: false });
        const en    = entry.eventName ?? '';
        const glyph = eventGlyph(en);
        const isViol = /violation|critical|safetyerror/i.test(en);
        const detail = formatDetails(entry);
        return `
          <div class="timeline-item ${isViol ? 'violation' : ''}">
            <div class="timeline-time">${time}</div>
            <div class="g">${glyph}</div>
            <div>
              <div class="timeline-event">${esc(eventLabel(en))}</div>
              ${detail ? `<div class="timeline-detail">${esc(detail)}</div>` : ''}
            </div>
          </div>`;
      }).join('');

      document.getElementById('modal-body').innerHTML = `
        <div class="session-summary-grid">
          <div class="summary-card">
            <div class="summary-card-value">${mins}:${String(secs).padStart(2,'0')}</div>
            <div class="summary-card-label">${t('totalDuration')}</div>
          </div>
          <div class="summary-card">
            <div class="summary-card-value">${summary.totalScore ?? 0}</div>
            <div class="summary-card-label">${t('finalScore')}</div>
          </div>
          <div class="summary-card">
            <div class="summary-card-value">${summary.tasksCompleted ?? 0}</div>
            <div class="summary-card-label">${t('tasksCompleted')}</div>
          </div>
          <div class="summary-card">
            <div class="summary-card-value">${summary.totalTasks ?? 0}</div>
            <div class="summary-card-label">${t('totalTasks')}</div>
          </div>
        </div>
        <h3 style="margin-bottom:14px;color:var(--accent)">${t('eventTimeline')}</h3>
        <div class="timeline">${timelineHtml}</div>
      `;
      document.getElementById('report-modal').classList.add('open');
      _lastFocused = document.activeElement;
      document.addEventListener('keydown', _onModalKey);
      document.getElementById('btn-close-modal').focus();
    }

    /* ── Modal close + keyboard accessibility ── */
    let _lastFocused = null;
    export function closeReportModal() {
      document.getElementById('report-modal').classList.remove('open');
      document.removeEventListener('keydown', _onModalKey);
      if (_lastFocused && typeof _lastFocused.focus === 'function') _lastFocused.focus();
      _lastFocused = null;
    }
    function _onModalKey(e) {
      if (e.key === 'Escape') closeReportModal();
    }

    /* ── Evaluator recenter control ───────────────────────────────
       Two-stage confirm (click -> "Confirmar?" -> click) so a stray click cannot teleport a
       participant mid-assessment, then a Command/CommandAck round-trip with a local timeout so
       the evaluator is never left staring at a button that silently did nothing — the headset is
       on the participant's head and the dashboard is the evaluator's only window into it. ── */
    const RECENTER_CONFIRM_WINDOW_MS = 4000;
    const RECENTER_ACK_TIMEOUT_MS = 5000;
    let _recenterConfirmArmed = false;
    let _recenterConfirmTimer = null;
    let _recenterAckTimer = null;

    function _recenterEls() {
      return {
        btn: document.getElementById('btn-recenter'),
        status: document.getElementById('recenter-status'),
      };
    }

    function _setRecenterStatus(text, kind) {
      const { status } = _recenterEls();
      if (!status) return;
      status.textContent = text ?? '';
      status.className = 'recenter-status' + (kind ? ` ${kind}` : '');
    }

    function _resetRecenterButton() {
      _recenterConfirmArmed = false;
      clearTimeout(_recenterConfirmTimer);
      const { btn } = _recenterEls();
      if (!btn) return;
      btn.disabled = false;
      btn.classList.remove('confirm');
      btn.textContent = t('recenterButton');
    }

    /** Click handler for the "Recentralizar jogador" button. First click arms a short-lived
        confirm state; the second click (within the window) actually sends the command and waits
        for the CommandAck (or a local timeout if the app never answers). */
    export function handleRecenterClick(ws) {
      const { btn } = _recenterEls();
      if (!btn) return;

      if (!_recenterConfirmArmed) {
        _recenterConfirmArmed = true;
        btn.textContent = t('recenterConfirmButton');
        btn.classList.add('confirm');
        _setRecenterStatus('');
        clearTimeout(_recenterConfirmTimer);
        _recenterConfirmTimer = setTimeout(_resetRecenterButton, RECENTER_CONFIRM_WINDOW_MS);
        return;
      }

      clearTimeout(_recenterConfirmTimer);
      _recenterConfirmArmed = false;

      const requestId = `recenter-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
      btn.disabled = true;
      btn.classList.remove('confirm');
      btn.textContent = t('recenterButton');
      _setRecenterStatus(t('recenterPending'), 'pending');

      const sent = ws.send({ eventType: 'Command', command: 'recenter_player', requestId });
      if (!sent) {
        btn.disabled = false;
        _setRecenterStatus(t('recenterNoConnection'), 'error');
        return;
      }

      state.recenterPendingRequestId = requestId;
      clearTimeout(_recenterAckTimer);
      _recenterAckTimer = setTimeout(() => {
        if (state.recenterPendingRequestId !== requestId) return; // superseded by a newer ack
        state.recenterPendingRequestId = null;
        btn.disabled = false;
        _setRecenterStatus(t('recenterNoResponse'), 'error');
      }, RECENTER_ACK_TIMEOUT_MS);
    }

    /** Routed from the CommandAck event — resolves the pending recenter request if this ack
        matches it (ignored otherwise: a stale ack after the local timeout, or an ack for some
        other command). */
    export function resolveRecenterAck(payload) {
      if (!payload || payload.requestId == null || payload.requestId !== state.recenterPendingRequestId) return;
      clearTimeout(_recenterAckTimer);
      state.recenterPendingRequestId = null;
      const { btn } = _recenterEls();
      if (btn) btn.disabled = false;
      _setRecenterStatus(
        payload.accepted ? t('recenterSuccess') : (payload.reason || t('recenterFailed')),
        payload.accepted ? 'success' : 'error'
      );
    }

    export function downloadReport() {
      if (!state.sessionReport) return;
      const blob = new Blob([JSON.stringify(state.sessionReport, null, 2)], { type: 'application/json' });
      const url  = URL.createObjectURL(blob);
      const a    = Object.assign(document.createElement('a'), {
        href: url,
        download: `${getLang() === 'pt' ? 'relatorio_sessao' : 'session_report'}_${new Date().toISOString().replace(/[:.]/g, '-')}.json`
      });
      a.click();
      URL.revokeObjectURL(url);
    }
