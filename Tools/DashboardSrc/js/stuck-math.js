/* Pure stuck-participant logic: no DOM, no storage, no clock of its own, so node --test can import
   it and the router and the multi-headset shell share one definition of "stuck".

   The station page folds every routed event into a `progress` object (trackProgress); the shell
   asks evaluateStuck about it on a timer, because a participant who stops moving sends no event
   to react to. */

export const DEFAULT_STUCK_SECONDS = 60;
export const MIN_STUCK_SECONDS = 10;
export const MAX_STUCK_SECONDS = 600;

/* A hint this deep means the app has already run out of gentler help. */
export const HINT_ALERT_LEVEL = 3;

/* PauseSource wire tokens (PauseSourceLabels.cs). Popup, PauseMenu and Practice are deliberate
   pauses by the app, the participant or the evaluator; the three below are the headset losing the
   participant. A token missing from this set silences the idle rule without raising an alert. */
const FOCUS_PAUSE_SOURCES = new Set(['ApplicationFocus', 'ApplicationPause', 'XrSessionUnfocused']);

/* The attempt and its refusal come from one publish on the headset but reach the dashboard in
   either order, so they are paired by closeness in time. */
const REFUSAL_WINDOW_MS = 1000;

/* ViolationCodes the rule engine raises together with ActionRefused or an equip refusal. */
const REFUSAL_CODES = new Set(['ACTION_ID_MISSING', 'NO_ACTIVE_GROUP', 'WRONG_ACTION', 'PREREQUISITE_PENDING', 'PPE_OUT_OF_ORDER']);

/* Threshold from an input box or localStorage: the default for anything unusable, else clamped. */
export function parseThresholdSeconds(raw) {
  const n = Number(raw);
  if (!Number.isFinite(n) || n <= 0) return DEFAULT_STUCK_SECONDS;
  return Math.min(MAX_STUCK_SECONDS, Math.max(MIN_STUCK_SECONDS, Math.round(n)));
}

function focusSources(pauseSources) {
  return Array.isArray(pauseSources) ? pauseSources.filter(token => FOCUS_PAUSE_SOURCES.has(token)) : [];
}

export function focusLost(pauseSources) {
  return focusSources(pauseSources).length > 0;
}

export function createProgress() {
  return {
    active: false,            // a session is running on the headset
    sessionId: null,
    lastProgressAt: 0,        // ms, the page's own clock
    pauseSources: [],
    pausedAt: 0,              // ms when the current pause began; 0 while running
    hint: null,               // { level, taskId, taskName, secondsWithoutProgress, at }
    attempt: null,            // the last ActionAttempt, kept so a refusal can undo it
    lastRefusalAt: 0,
  };
}

function advance(progress, now) {
  return { ...progress, lastProgressAt: now, hint: null, attempt: null };
}

function stop(progress) {
  return { ...progress, active: false, hint: null, attempt: null };
}

function startSession(progress, sessionId, now) {
  // RequestSync replays SessionStarted after a reconnect: whatever happened during the gap is unknown.
  if (progress.active && progress.sessionId === sessionId) return { ...progress, lastProgressAt: now };
  return { ...createProgress(), active: true, sessionId, lastProgressAt: now,
    pauseSources: progress.pauseSources, pausedAt: progress.pauseSources.length ? now : 0 };
}

/* Time spent paused is not idle time: when the last hold goes, the idle clock skips the paused
   stretch. A pause is not progress either, so a hint alert stays until the participant moves on;
   the headset releases a feedback popup's hold while the participant is still stuck. */
function withPauseSources(progress, sources, now) {
  const pauseSources = sources.filter(Boolean);
  const wasPaused = progress.pauseSources.length > 0;
  const isPaused = pauseSources.length > 0;
  if (!wasPaused && isPaused) return { ...progress, pauseSources, pausedAt: now };
  if (wasPaused && !isPaused) {
    const pausedFor = progress.pausedAt ? Math.max(0, now - progress.pausedAt) : 0;
    return { ...progress, pauseSources, pausedAt: 0, lastProgressAt: progress.lastProgressAt + pausedFor };
  }
  return { ...progress, pauseSources };
}

function acceptAttempt(progress, now) {
  if (now - progress.lastRefusalAt <= REFUSAL_WINDOW_MS) return progress;
  return { ...advance(progress, now), attempt: { at: now, lastProgressAt: progress.lastProgressAt, hint: progress.hint } };
}

function refuse(progress, code, now) {
  if (!REFUSAL_CODES.has(code)) return progress;
  const next = { ...progress, lastRefusalAt: now };
  const attempt = progress.attempt;
  if (!attempt || now - attempt.at > REFUSAL_WINDOW_MS) return next;
  return { ...next, lastProgressAt: attempt.lastProgressAt, hint: attempt.hint, attempt: null };
}

function applyStationStatus(progress, status, now) {
  const next = Array.isArray(status.pauseSources) ? withPauseSources(progress, status.pauseSources, now) : progress;
  if (status.sessionActive === true && !next.active) return { ...next, active: true, lastProgressAt: now };
  if (status.sessionActive === false && next.active) return stop(next);
  return next;
}

function hintFrom(payload, now) {
  const level = Number(payload.level);
  return {
    level: Number.isFinite(level) ? level : 0,
    taskId: payload.taskId ?? '',
    taskName: payload.taskName ?? '',
    secondsWithoutProgress: Number(payload.secondsWithoutProgress) || 0,
    at: now,
  };
}

/* Folds one routed event into the progress. Progress is a task started or completed, a PPE item put
   on, or an action attempt the rule engine did not refuse; every other event leaves the object
   untouched. */
export function trackProgress(progress, eventType, payload, now) {
  const p = payload ?? {};
  switch (eventType) {
    case 'SessionStarted': return startSession(progress, p.sessionId ?? null, now);
    case 'SessionCompleted':
    case 'SessionEnded': return stop(progress);
    case 'SessionReset': return createProgress();
    case 'TaskStarted':
    case 'TaskCompleted': return advance(progress, now);
    case 'ActionAttempt': return acceptAttempt(progress, now);
    case 'PpeChanged': return p.isWearing === true ? advance(progress, now) : progress;
    case 'SafetyViolation': return refuse(progress, p.violationCode, now);
    case 'HintShown': return { ...progress, hint: hintFrom(p, now) };
    case 'SessionPaused': return withPauseSources(progress, p.pauseSources?.length ? p.pauseSources : (p.pauseSource ? [p.pauseSource] : []), now);
    case 'PauseHoldChanged': return withPauseSources(progress, p.pauseSources ?? [], now);
    case 'SessionResumed': return withPauseSources(progress, [], now);
    case 'StationStatus': return applyStationStatus(progress, p, now);
    default: return progress;
  }
}

/* Whether the tile should raise the alert now. A focus loss and a deep hint raise it at once; a
   popup, pause-menu or practice-area hold silences the idle rule. */
export function evaluateStuck(progress, { connected, now, thresholdSeconds }) {
  const secondsWithoutProgress = Math.max(0, Math.floor((now - progress.lastProgressAt) / 1000));
  const verdict = (reason, extra) => ({ stuck: reason !== null, reason, secondsWithoutProgress, ...extra });
  if (!connected || !progress.active) return verdict(null);
  if (focusLost(progress.pauseSources)) return verdict('focus', { pauseSources: focusSources(progress.pauseSources) });
  if (progress.pauseSources.length > 0) return verdict(null);
  if (progress.hint && progress.hint.level >= HINT_ALERT_LEVEL) return verdict('hint', { hint: progress.hint });
  if (secondsWithoutProgress >= thresholdSeconds) return verdict('idle');
  return verdict(null);
}

/* Edge of the alert between two evaluations (their reasons), so the sound plays once per onset. */
export function alertChange(previousReason, reason) {
  if (reason && reason !== previousReason) return 'raised';
  if (previousReason && !reason) return 'cleared';
  return null;
}
