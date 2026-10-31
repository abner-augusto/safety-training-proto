import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_STUCK_SECONDS, MIN_STUCK_SECONDS, MAX_STUCK_SECONDS,
  parseThresholdSeconds, focusLost, createProgress, trackProgress, evaluateStuck, alertChange,
} from '../js/stuck-math.js';

const T0 = 1_000_000;
const sec = n => n * 1000;

/** Feeds [eventType, payload, atSeconds] triples through the reducer, like the router does. */
function replay(events) {
  return events.reduce((progress, [type, payload, at]) => trackProgress(progress, type, payload, T0 + sec(at)), createProgress());
}
const check = (progress, atSeconds, thresholdSeconds = 60, connected = true) =>
  evaluateStuck(progress, { connected, now: T0 + sec(atSeconds), thresholdSeconds });

const started = ['SessionStarted', { sessionId: 'S1' }, 0];

test('a running session with no progress is stuck exactly at the threshold', () => {
  const p = replay([started]);
  assert.equal(check(p, 59.9).stuck, false);
  const at = check(p, 60);
  assert.equal(at.stuck, true);
  assert.equal(at.reason, 'idle');
  assert.equal(at.secondsWithoutProgress, 60);
});

test('a one-second tick sees the alert within 2 s of the threshold', () => {
  const p = replay([started]);
  const firstTickStuck = [60, 61, 62].find(s => check(p, s).stuck);
  assert.ok(firstTickStuck <= 62);
});

test('TaskStarted, TaskCompleted and an accepted action restart the idle clock', () => {
  for (const [type, payload] of [['TaskStarted', { taskId: 'a' }], ['TaskCompleted', { taskId: 'a' }], ['ActionAttempt', { actionId: 'x' }]]) {
    const p = replay([started, [type, payload, 50]]);
    assert.equal(check(p, 109).stuck, false, type);
    assert.equal(check(p, 110).stuck, true, type);
  }
});

test('putting a PPE item on restarts the idle clock; taking one off does not', () => {
  const donned = replay([started, ['PpeChanged', { ppeType: 'GloveLeft', isWearing: true }, 50]]);
  assert.equal(check(donned, 109).stuck, false);
  const removed = replay([started, ['PpeChanged', { ppeType: 'GloveLeft', isWearing: false }, 50]]);
  assert.equal(check(removed, 60).stuck, true);
});

test('events that are not progress leave the idle clock alone', () => {
  const p = replay([started, ['ScoreChanged', { delta: 5 }, 30], ['SafetyViolation', { violationCode: 'PPE_MISSING' }, 40],
    ['SceneLayout', {}, 45], ['GroupStarted', { groupName: 'g' }, 50]]);
  assert.equal(check(p, 60).stuck, true);
});

test('an action refused right after the attempt is not progress, in either order', () => {
  const attemptThenRefusal = replay([started, ['ActionAttempt', { actionId: 'x' }, 40], ['SafetyViolation', { violationCode: 'WRONG_ACTION' }, 40.2]]);
  assert.equal(check(attemptThenRefusal, 60).stuck, true);

  const refusalThenAttempt = replay([started, ['SafetyViolation', { violationCode: 'WRONG_ACTION' }, 40], ['ActionAttempt', { actionId: 'x' }, 40.2]]);
  assert.equal(check(refusalThenAttempt, 60).stuck, true);
});

test('a refusal long before an attempt does not cancel it', () => {
  const p = replay([started, ['SafetyViolation', { violationCode: 'WRONG_ACTION' }, 10], ['ActionAttempt', { actionId: 'x' }, 40]]);
  assert.equal(check(p, 60).stuck, false);
});

test('a violation that is not a refusal does not cancel an accepted action', () => {
  const p = replay([started, ['ActionAttempt', { actionId: 'x' }, 40], ['SafetyViolation', { violationCode: 'PPE_MISSING' }, 40.1]]);
  assert.equal(check(p, 60).stuck, false);
});

test('a refusal restores the clock the attempt had moved, not zero', () => {
  const p = replay([started, ['TaskCompleted', { taskId: 'a' }, 20], ['ActionAttempt', { actionId: 'x' }, 70], ['SafetyViolation', { violationCode: 'WRONG_ACTION' }, 70.1]]);
  assert.equal(check(p, 79).stuck, false);
  const atLimit = check(p, 80);
  assert.equal(atLimit.stuck, true);
  assert.equal(atLimit.secondsWithoutProgress, 60);
});

test('every session-ending event stops the alert', () => {
  for (const type of ['SessionCompleted', 'SessionEnded', 'SessionReset']) {
    const p = replay([started, [type, {}, 10]]);
    assert.equal(check(p, 500).stuck, false, type);
  }
});

test('nothing is stuck before a session starts or while the headset is disconnected', () => {
  assert.equal(check(createProgress(), 500).stuck, false);
  assert.equal(check(replay([started]), 500, 60, false).stuck, false);
});

test('a new session forgets the previous one, a replayed start only restarts the clock', () => {
  const stuckFirst = replay([started, ['HintShown', { level: 3, taskId: 'a' }, 20]]);
  assert.equal(check(stuckFirst, 21).reason, 'hint');

  const replayed = trackProgress(stuckFirst, 'SessionStarted', { sessionId: 'S1' }, T0 + sec(100));
  assert.equal(evaluateStuck(replayed, { connected: true, now: T0 + sec(101), thresholdSeconds: 60 }).reason, 'hint');

  const next = trackProgress(stuckFirst, 'SessionStarted', { sessionId: 'S2' }, T0 + sec(100));
  assert.equal(evaluateStuck(next, { connected: true, now: T0 + sec(101), thresholdSeconds: 60 }).stuck, false);
});

test('focus loss raises the alert at once, whichever focus token holds the pause', () => {
  for (const token of ['ApplicationFocus', 'ApplicationPause', 'XrSessionUnfocused']) {
    const p = replay([started, ['SessionPaused', { pauseSource: token, pauseSources: [token] }, 5]]);
    const r = check(p, 5);
    assert.equal(r.stuck, true, token);
    assert.equal(r.reason, 'focus', token);
    assert.deepEqual(r.pauseSources, [token]);
  }
});

test('popup and pause-menu pauses are not stuck, however long they last', () => {
  for (const token of ['Popup', 'PauseMenu']) {
    const p = replay([started, ['SessionPaused', { pauseSource: token, pauseSources: [token] }, 5]]);
    assert.equal(check(p, 5).stuck, false, token);
    assert.equal(check(p, 900).stuck, false, token);
  }
});

test('a focus token among several holds is enough, and only the focus tokens are reported', () => {
  const p = replay([started, ['SessionPaused', { pauseSources: ['Popup', 'XrSessionUnfocused'] }, 5]]);
  const r = check(p, 5);
  assert.equal(r.reason, 'focus');
  assert.deepEqual(r.pauseSources, ['XrSessionUnfocused']);
});

test('the single pauseSource field of an older relay still counts', () => {
  const p = replay([started, ['SessionPaused', { pauseSource: 'ApplicationFocus' }, 5]]);
  assert.equal(check(p, 5).reason, 'focus');
});

test('resuming clears the alert and restarts the idle clock', () => {
  const paused = replay([started, ['SessionPaused', { pauseSources: ['XrSessionUnfocused'] }, 5]]);
  assert.equal(check(paused, 200).stuck, true);
  const resumed = trackProgress(paused, 'SessionResumed', { pauseSource: 'XrSessionUnfocused', pauseSources: [] }, T0 + sec(200));
  assert.equal(check(resumed, 200).stuck, false);
  assert.equal(check(resumed, 259).stuck, false);
  assert.equal(check(resumed, 260).stuck, true);
});

test('the last hold being released through PauseHoldChanged or StationStatus also clears it', () => {
  const held = replay([started, ['PauseHoldChanged', { pauseSource: 'Popup', pauseSources: ['Popup'], held: true }, 5]]);
  assert.equal(check(held, 300).stuck, false);
  const released = trackProgress(held, 'PauseHoldChanged', { pauseSource: 'Popup', pauseSources: [], held: false }, T0 + sec(300));
  assert.equal(check(released, 301).stuck, false);
  assert.equal(check(released, 360).stuck, true);

  const viaStatus = replay([started, ['StationStatus', { sessionActive: true, pauseSources: ['ApplicationFocus'] }, 5],
    ['StationStatus', { sessionActive: true, pauseSources: [] }, 100]]);
  assert.equal(check(viaStatus, 101).stuck, false);
});

test('overlapping holds that change without a resume keep the focus alert until the last focus token goes', () => {
  const p = replay([started, ['PauseHoldChanged', { pauseSources: ['XrSessionUnfocused', 'Popup'] }, 5],
    ['PauseHoldChanged', { pauseSources: ['Popup'] }, 20]]);
  assert.equal(check(p, 21).stuck, false);
});

test('a hint of level 3 raises the alert at once, lower levels do not', () => {
  const low = replay([started, ['HintShown', { level: 1, taskId: 'a', taskName: 'A', secondsWithoutProgress: 20 }, 5],
    ['HintShown', { level: 2, taskId: 'a', taskName: 'A', secondsWithoutProgress: 40 }, 10]]);
  assert.equal(check(low, 11).stuck, false);

  const high = trackProgress(low, 'HintShown', { level: 3, taskId: 'a', taskName: 'Instalar guarda-corpo', secondsWithoutProgress: 60 }, T0 + sec(15));
  const r = check(high, 15);
  assert.equal(r.stuck, true);
  assert.equal(r.reason, 'hint');
  assert.equal(r.hint.level, 3);
  assert.equal(r.hint.taskName, 'Instalar guarda-corpo');
});

test('a hint does not count as progress', () => {
  const p = replay([started, ['HintShown', { level: 1 }, 50]]);
  assert.equal(check(p, 60).stuck, true);
});

test('progress or a resume clears a hint alert', () => {
  const hinted = replay([started, ['HintShown', { level: 3, taskId: 'a' }, 10]]);
  for (const [type, payload] of [['TaskCompleted', { taskId: 'a' }], ['TaskStarted', { taskId: 'b' }], ['ActionAttempt', { actionId: 'x' }]]) {
    const cleared = trackProgress(hinted, type, payload, T0 + sec(20));
    assert.equal(check(cleared, 21).stuck, false, type);
  }
  const paused = trackProgress(hinted, 'SessionPaused', { pauseSources: ['Popup'] }, T0 + sec(20));
  const resumed = trackProgress(paused, 'SessionResumed', { pauseSources: [] }, T0 + sec(30));
  assert.equal(check(resumed, 31).stuck, false);
});

test('a hint with a missing or garbled level never raises the alert', () => {
  for (const level of [undefined, null, 'abc', NaN]) {
    const p = replay([started, ['HintShown', { level }, 5]]);
    assert.equal(check(p, 6).stuck, false, String(level));
  }
  assert.equal(check(replay([started, ['HintShown', null, 5]]), 6).stuck, false);
});

test('StationStatus drives the active flag for a page that connected mid-session', () => {
  const mid = replay([['StationStatus', { sessionActive: true, pauseSources: [] }, 10]]);
  assert.equal(check(mid, 69).stuck, false);
  assert.equal(check(mid, 70).stuck, true);

  const ended = trackProgress(mid, 'StationStatus', { sessionActive: false, sessionCompleted: true }, T0 + sec(80));
  assert.equal(check(ended, 500).stuck, false);
});

test('the threshold is read at evaluation time', () => {
  const p = replay([started]);
  assert.equal(check(p, 45, 30).stuck, true);
  assert.equal(check(p, 45, 90).stuck, false);
});

test('an unknown event leaves the progress untouched', () => {
  const p = replay([started]);
  assert.equal(trackProgress(p, 'PoseFrame', {}, T0 + 5), p);
});

test('parseThresholdSeconds falls back to the default and clamps to the supported range', () => {
  assert.equal(DEFAULT_STUCK_SECONDS, 60);
  assert.equal(parseThresholdSeconds('45'), 45);
  assert.equal(parseThresholdSeconds(90), 90);
  assert.equal(parseThresholdSeconds('45.6'), 46);
  for (const raw of [null, undefined, '', 'abc', NaN, Infinity, '0', -5]) {
    assert.equal(parseThresholdSeconds(raw), DEFAULT_STUCK_SECONDS, String(raw));
  }
  assert.equal(parseThresholdSeconds(1), MIN_STUCK_SECONDS);
  assert.equal(parseThresholdSeconds(99999), MAX_STUCK_SECONDS);
});

test('focusLost accepts only the focus tokens', () => {
  assert.equal(focusLost(['ApplicationFocus']), true);
  assert.equal(focusLost(['Popup', 'ApplicationPause']), true);
  assert.equal(focusLost(['Popup', 'PauseMenu', 'Unspecified']), false);
  assert.equal(focusLost([]), false);
  assert.equal(focusLost(undefined), false);
});

test('alertChange reports only the edges, and an escalation to another reason', () => {
  assert.equal(alertChange(null, 'idle'), 'raised');
  assert.equal(alertChange('idle', 'idle'), null);
  assert.equal(alertChange('idle', null), 'cleared');
  assert.equal(alertChange(null, null), null);
  assert.equal(alertChange('idle', 'focus'), 'raised');
});
