    import { t, tf, actionLabel, ppeLabel, violationLabel, violationMessage, applyTranslations, pauseSourceLabel } from './i18n.js';
    import { tasks, wornPpe, state, sessionTimer } from './state.js';
    import { addLog, setScoreUI, renderCluster, renderTasks, resetTaskUI, emptyState, renderReportSection, resolveCommandAck } from './ui.js';
    import { saveSessionToHistory } from './history.js';
    import { setStationStatus, setPauseSources, renderStation, clearStartCode, settleIntervention, clearInterventions, setHighlightTargets } from './station.js';
    import { notifyShell, relayAck } from './embed.js';
    import { trackProgress } from './stuck-math.js';

    /* ================================================================
     * EVENT ROUTER — declarative handler map
     * ============================================================== */
    const handlers = {};
    const on = (type, fn) => { handlers[type] = fn; };
    const refreshMarkers = () => window.__spViewport?.refreshMarkers(tasks);

    export function route(envelope) {
      const { eventType, payload } = envelope;
      if (!eventType) return;
      state.progress = trackProgress(state.progress, eventType, payload, Date.now());
      const fn = handlers[eventType];
      if (fn) { try { fn(payload); } catch (e) { console.error(`[handler:${eventType}]`, e); } }
      // PoseFrame handled inline (high frequency — skip addLog)
      notifyShell();
    }

    on('StationStatus', setStationStatus);
    on('HighlightTargets', p => setHighlightTargets(p?.targets ?? []));

    on('SceneLayout', p => {
      window.__spViewport?.setLayout(p);
      applyTranslations();   /* marker labels take their text from data-i18n */
      refreshMarkers();
    });

    /* ── Session lifecycle ── */
    on('SessionStarted', p => {
      const sameSession = state.currentSessionId === (p.sessionId ?? null);
      state.currentSessionId = p.sessionId ?? null;
      state.currentParticipantId = p.participantId ?? '—';
      state.currentMode = p.mode ?? null;
      state.currentAttempt = p.attempt ?? 0;
      document.getElementById('session-id-label').textContent =
        `${t('session')}: ${p.sessionId?.substring(0, 8) ?? '?'}`;
      document.getElementById('participant-label').textContent =
        `${t('participant')}: ${state.currentParticipantId}`;
      const badge = document.getElementById('mode-badge');
      if (badge) {
        badge.hidden = !state.currentMode;
        if (state.currentMode) {
          const evaluation = state.currentMode === 'avaliacao';
          badge.textContent = evaluation ? t('modeEvaluation') : t('modeGuided');
          badge.dataset.i18n = evaluation ? 'modeEvaluation' : 'modeGuided';
          badge.classList.toggle('mode-avaliacao', evaluation);
        } else {
          badge.textContent = '—';
          badge.removeAttribute('data-i18n');
          badge.classList.remove('mode-avaliacao');
        }
      }
      // RequestSync replays SessionStarted after a reconnect; keep the clock and tallies then.
      if (!sameSession) {
        sessionTimer.start();
        state.violationCount = 0;
        wornPpe.clear();
        clearStartCode();
        clearInterventions();
        const attempt = state.currentAttempt > 1 ? ` · ${t('attempt')} ${state.currentAttempt}` : '';
        addLog(`${t('logSessionStarted')} · ${state.currentParticipantId}${attempt}`, 'success');
      }
      renderCluster();
      renderStation();
    });

    on('SessionManifest', p => {
      const snapshot = p.tasks ?? [];
      const seen = new Set();

      snapshot.forEach((task, idx) => {
        const id = task.taskId ?? task.taskName;
        if (!id) return;

        seen.add(id);
        const existing = tasks.get(id) ?? {};
        tasks.set(id, {
          id,
          name:          task.taskName     ?? existing.name          ?? id,
          description:   task.description  ?? existing.description   ?? '',
          groupName:     task.groupName    ?? existing.groupName     ?? '',
          order:         task.order        ?? existing.order         ?? (idx + 1),
          executionMode: existing.executionMode ?? '',
          expectedAction:existing.expectedAction?? '',
          requiredPpe:   existing.requiredPpe   ?? [],
          successPoints: existing.successPoints ?? 0,
          ppePenalty:    existing.ppePenalty    ?? 0,
          status:        task.status       ?? existing.status        ?? 'pending',
        });
      });

      Array.from(tasks.keys()).forEach(id => {
        if (!seen.has(id)) tasks.delete(id);
      });

      renderTasks();
      refreshMarkers();
    });

    const pauseLabels = list => (list ?? []).map(pauseSourceLabel).join(' + ');
    on('SessionPaused', p => {
      const sources = p?.pauseSources?.length ? p.pauseSources : (p?.pauseSource ? [p.pauseSource] : []);
      setPauseSources(sources);
      sessionTimer.pause();
      addLog(`${t('logSessionPaused')}${sources.length ? ` · ${pauseLabels(sources)}` : ''}`, 'caution');
    });
    on('SessionResumed', p => {
      setPauseSources([]);
      sessionTimer.resume();
      addLog(`${t('logSessionResumed')}${p?.pauseSource ? ` · ${pauseLabels([p.pauseSource])}` : ''}`, 'success');
    });
    /* Overlapping holds change the chip without a new pause/resume pair. */
    on('PauseHoldChanged', p => setPauseSources(p?.pauseSources ?? []));
    on('SessionEnded',    () => { sessionTimer.stop();   addLog(t('logSessionEnded')); });

    on('SessionCompleted', p => {
      sessionTimer.stop();
      addLog(`${t('logSessionComplete')}: ${p.totalScore} | ${t('tasks')}: ${p.tasksCompleted}/${p.totalTasks}`, 'success');
    });

    on('SessionReset', () => {
      tasks.clear();
      refreshMarkers();
      state.sessionReport = null;
      state.currentScore  = 0;
      state.currentMode = null;
      state.currentAttempt = 0;
      setPauseSources([]);
      sessionTimer.reset();
      setScoreUI(0);
      resetTaskUI();
      state.violationCount = 0;
      wornPpe.clear();
      clearInterventions();
      renderCluster();
      window.__spViewport?.clearSession();   // sem malhas de EPI órfãs entre participantes
      document.getElementById('report-section').innerHTML = emptyState('', t('reportAfterSession'));
      document.getElementById('session-id-label').textContent = t('noSession');
      document.getElementById('participant-label').textContent = `${t('participant')}: —`;
      const badge = document.getElementById('mode-badge');
      if (badge) {
        badge.hidden = true;
        badge.textContent = '—';
        badge.removeAttribute('data-i18n');
        badge.classList.remove('mode-avaliacao');
      }
      addLog(t('logSessionReset'));
      renderStation();
    });

    /* ── Task events ── */
    function upsertTask(p, status) {
      const existing = tasks.get(p.taskId) ?? {};
      tasks.set(p.taskId, {
        id:            p.taskId,
        name:          p.taskName          ?? existing.name          ?? p.taskId,
        description:   p.taskDescription   ?? existing.description   ?? '',
        groupName:     p.groupName         ?? existing.groupName     ?? '',
        order:         p.order             ?? existing.order         ?? 0,
        executionMode: p.executionMode     ?? existing.executionMode ?? '',
        expectedAction:p.expectedAction    ?? existing.expectedAction?? '',
        requiredPpe:   p.requiredPpe       ?? existing.requiredPpe   ?? [],
        successPoints: p.successPoints     ?? existing.successPoints ?? 0,
        ppePenalty:    p.ppePenalty        ?? existing.ppePenalty    ?? 0,
        status,
      });
      renderTasks();
      refreshMarkers();
    }

    on('TaskStarted',   p => { upsertTask(p, 'active');    addLog(`${t('logStarted')}: ${p.taskName}`); });
    on('TaskCompleted', p => { upsertTask(p, 'completed'); addLog(`${t('logCompleted')}: ${p.taskName}`, 'success'); });
    // Não há evento para "não realizada": a tarefa fica pendente no manifesto até o portão
    // fechar o grupo, e é a violação TASK_NOT_PERFORMED que registra o desfecho no log.

    /* ── Group events ── */
    on('GroupStarted',   p => addLog(`${t('logGroupStarted')}: ${p.groupName}`));
    on('GroupCompleted', p => addLog(`${t('logGroupCompleted')}: ${p.groupName}`, 'success'));

    /* ── Score ── */
    on('ScoreChanged', p => {
      setScoreUI(p.totalScore);
      const d = p.delta;
      if (d > 0)      addLog(`${t('logScorePlus')} +${d} (${t('logTotal')}: ${p.totalScore})`, 'success');
      else if (d < 0) addLog(`${t('logScoreMinus')} ${d} (${t('logTotal')}: ${p.totalScore})`, 'violation');
    });

    /* ── PPE ── */
    on('PpeChanged', p => {
      const label = ppeLabel(p.ppeType);
      if (p.isWearing) { wornPpe.add(String(p.ppeType)); addLog(`${t('logPpeEquipped')}: ${label}`, 'success'); }
      else             { wornPpe.delete(String(p.ppeType)); addLog(`${t('logPpeRemoved')}: ${label}`, 'violation'); }
      renderCluster();
    });

    /* ── Safety ── */
    on('ActionAttempt', p => {
      const pos = p.hasPosition ? ` ${t('at')} (${p.px?.toFixed(1)},${p.py?.toFixed(1)},${p.pz?.toFixed(1)})` : '';
      const label = actionLabel(p.actionId);
      const source = p.sourceId ? ` · ${t('origin')}: ${p.sourceId}` : '';
      addLog(`${t('logActionAttempt')}: ${label}${source}${pos}`);
      /* interaction instrument: ring + callout at the point of the action */
      if (p.hasPosition) window.__spViewport?.showInteraction(label, p.px, p.py, p.pz);
    });

    on('SafetyViolation',      p => {
      state.violationCount++;
      renderCluster();
      const detail = violationMessage(p.violationCode, p.message);
      addLog(`${t('logViolation')}: ${violationLabel(p.violationCode)}${detail ? ` · ${detail}` : ''}`, 'violation');
    });
    on('CriticalSafetyFailure',p => { state.violationCount++; state.lastCriticalAt = Date.now(); renderCluster(); addLog(`${t('logCritical')}: ${p.violationCount} ${t('violations').toLowerCase()} ${t('detailIn')} ${p.windowSeconds}s`, 'violation'); });
    on('SafetyError',          p => addLog(`${t('logSafetyError')}: ${t('internalError')}${p.source ? ` · ${t('origin')}: ${p.source}` : ''}`, 'violation'));

    /* XR ergonomics, never scored: the room and the virtual floor disagree. */
    on('PlayspaceNotice', p => {
      const area = p.area ? ` (${p.area})` : '';
      if (p.kind === 'LeftWalkableArea') addLog(`${t('logLeftWalkable')}${area}`, 'caution');
      else if (p.kind === 'ReturnedToWalkableArea') addLog(`${t('logReturnedWalkable')}${area} · ${Number(p.durationSeconds ?? 0).toFixed(1)} s`, 'caution');
      else if (p.kind === 'RoomCenterDiscarded') addLog(t('logRoomCenterDiscarded'), 'caution');
    });

    /* The app's graded help: the shell raises the stuck alert from the progress state; this only
       puts the line in the log. */
    on('HintShown', p => {
      const seconds = Math.round(Number(p.secondsWithoutProgress) || 0);
      addLog(tf('logHintShown', { level: p.level, task: p.taskName || p.taskId || '—', seconds }), 'caution');
    });

    /* ── Evaluator commands ── */
    on('CommandAck', p => {
      resolveCommandAck(p);
      settleIntervention(p);
      relayAck(p);
    });

    /* ── Session Log File ── */
    on('SessionLogFile', p => {
      if (!p.content) return;
      try {
        const report = JSON.parse(p.content);
        renderReportSection(report);
        saveSessionToHistory(report, p.sessionId, p.participantId);
        addLog(t('logReportAvailable'), 'success');
      } catch (e) {
        console.error('Failed to parse session log:', e);
      }
    });
