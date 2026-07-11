    import { t, locale } from './i18n.js';
    import { state } from './state.js';
    import { esc, emptyState, openReportModal } from './ui.js';

    /* ================================================================
     * SESSION HISTORY — persisted in localStorage, survives resets/reloads
     * ============================================================== */
    const HISTORY_KEY = 'sp_session_history';
    const MAX_HISTORY = 50;
    let sessionHistory = [];               // [{ sessionId, participantId, savedAt, report }]

    export function loadHistory() {
      try {
        const raw = localStorage.getItem(HISTORY_KEY);
        sessionHistory = raw ? JSON.parse(raw) : [];
        if (!Array.isArray(sessionHistory)) sessionHistory = [];
      } catch (e) {
        console.error('Failed to load session history:', e);
        sessionHistory = [];
      }
    }

    function persistHistory() {
      try {
        localStorage.setItem(HISTORY_KEY, JSON.stringify(sessionHistory));
      } catch (e) {
        // Most likely quota exceeded — drop the oldest and retry once.
        console.error('Failed to persist session history:', e);
        if (sessionHistory.length > 1) {
          sessionHistory = sessionHistory.slice(0, Math.floor(sessionHistory.length / 2));
          try { localStorage.setItem(HISTORY_KEY, JSON.stringify(sessionHistory)); } catch (_) {}
        }
      }
    }

    /** Capture the just-finished session report into history (newest first). */
    export function saveSessionToHistory(report) {
      if (!report) return;
      sessionHistory.unshift({
        sessionId:     state.currentSessionId,
        participantId: state.currentParticipantId,
        savedAt:       Date.now(),
        report,
      });
      if (sessionHistory.length > MAX_HISTORY) sessionHistory.length = MAX_HISTORY;
      persistHistory();
      renderHistory();
    }

    export function renderHistory() {
      const list    = document.getElementById('history-list');
      const actions = document.getElementById('history-actions');
      const count   = document.getElementById('history-count');

      count.textContent = sessionHistory.length ? `(${sessionHistory.length})` : '';

      if (sessionHistory.length === 0) {
        list.innerHTML = emptyState('', t('noHistory'));
        actions.style.display = 'none';
        return;
      }
      actions.style.display = 'flex';

      list.innerHTML = sessionHistory.map((h, i) => {
        const s     = h.report?.summary ?? {};
        const score = s.totalScore ?? 0;
        const done  = s.tasksCompleted ?? 0;
        const total = s.totalTasks ?? 0;
        const when  = new Date(h.savedAt ?? Date.now())
          .toLocaleString(locale(), { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false });
        return `
          <div class="history-item" data-idx="${i}" role="button" tabindex="0"
               aria-label="${esc(h.participantId ?? '—')} — ${score} pts">
            <div class="history-item-top">
              <span class="history-pid">${esc(h.participantId ?? '—')}</span>
              <span class="history-score">${score} pts</span>
            </div>
            <div class="history-item-sub">${done}/${total} · ${esc(when)}</div>
          </div>`;
      }).join('');

      list.querySelectorAll('.history-item').forEach(el => {
        const open = () => {
          const idx = Number(el.dataset.idx);
          const entry = sessionHistory[idx];
          if (entry) openReportModal(entry.report);
        };
        el.onclick = open;
        el.onkeydown = e => {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); }
        };
      });
    }

    export function clearHistory() {
      if (sessionHistory.length === 0) return;
      if (!window.confirm(t('confirmClearHistory'))) return;
      sessionHistory = [];
      persistHistory();
      renderHistory();
    }

    export function exportAllHistory() {
      if (sessionHistory.length === 0) return;
      const blob = new Blob([JSON.stringify(sessionHistory, null, 2)], { type: 'application/json' });
      const url  = URL.createObjectURL(blob);
      const a    = Object.assign(document.createElement('a'), {
        href: url,
        download: `session_history_${new Date().toISOString().replace(/[:.]/g, '-')}.json`
      });
      a.click();
      URL.revokeObjectURL(url);
    }
