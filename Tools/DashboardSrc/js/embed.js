    import { state } from './state.js';
    import { t } from './i18n.js';
    import { isTileCommand } from './tile-actions.js';

    /* ================================================================
     * EMBED — the station page inside the multi-headset shell
     * (multi.html loads it as http://<headset>:8080/?embed=1). The shell
     * owns theme and language and pushes them down; the page reports a
     * short status up so the shell's tile bar covers every headset at a
     * glance. The shell also sends the tile's quick-action commands down,
     * and the page sends their acks back up. Only the parent window is
     * listened to.
     * ============================================================== */
    export const EMBEDDED = new URLSearchParams(window.location.search).has('embed') && window.parent !== window;

    let _queued = false;

    function snapshot() {
      const s = state.station ?? {};
      return {
        connected: state.connected,
        participantId: state.currentParticipantId,
        mode: state.currentMode,
        sessionId: state.currentSessionId,
        pauseSources: state.pauseSources,
        practice: state.practice,
        violations: state.violationCount,
        score: state.currentScore,
        lastCriticalAt: state.lastCriticalAt,
        progress: state.progress,
        interventionCount: state.interventions.length,
        highlightTargets: state.highlightTargets,
        deviceModel: s.deviceModel ?? '',
        appVersion: s.appVersion ?? '',
        batteryLevel: typeof s.batteryLevel === 'number' ? s.batteryLevel : -1,
        batteryStatus: s.batteryStatus ?? '',
        awaitingStart: !!s.awaitingStart,
        participantKeypadOpen: !!s.participantKeypadOpen,
        sessionActive: !!s.sessionActive,
        sessionCompleted: !!s.sessionCompleted,
        hasStationStatus: !!state.station,
      };
    }

    /** Coalesces a burst of updates into one message per tick. */
    export function notifyShell() {
      if (!EMBEDDED || _queued) return;
      _queued = true;
      setTimeout(() => {
        _queued = false;
        window.parent.postMessage({ type: 'sp-status', status: snapshot() }, '*');
      }, 150);
    }

    /** Sends a CommandAck up so the shell can settle the quick-action button that sent it. */
    export function relayAck(ack) {
      if (EMBEDDED) window.parent.postMessage({ type: 'sp-command-ack', ack }, '*');
    }

    /** `sendCommand(envelope)` hands a Command to this page's own WebSocket and returns whether it went out. */
    export function initEmbed({ onTheme, onLang, sendCommand }) {
      if (!EMBEDDED) return;
      document.documentElement.classList.add('embed');
      window.addEventListener('message', e => {
        if (e.source !== window.parent) return;
        const m = e.data;
        if (m?.type === 'sp-theme') onTheme(m.theme);
        else if (m?.type === 'sp-lang') onLang(m.lang);
        else if (m?.type === 'sp-command' && isTileCommand(m.envelope) && !sendCommand(m.envelope)) {
          relayAck({ requestId: m.envelope.requestId, command: m.envelope.command, accepted: false, reason: t('commandNoConnection') });
        }
      });
      window.parent.postMessage({ type: 'sp-ready' }, '*');
      notifyShell();
    }
