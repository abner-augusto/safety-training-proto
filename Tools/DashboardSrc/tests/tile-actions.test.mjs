import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TILE_ACTIONS, isTileCommand, isActionBlocked } from '../js/tile-actions.js';

const action = command => TILE_ACTIONS.find(a => a.command === command);
const running = { connected: true, hasStationStatus: true, sessionActive: true, sessionCompleted: false };

test('the tile offers collect objects, recenter and restart, wired to the app commands', () => {
  assert.deepEqual(TILE_ACTIONS.map(a => a.command), ['return_all_ppe', 'recenter_player', 'reset_session']);
});

test('every action names the strings its button needs', () => {
  for (const a of TILE_ACTIONS) {
    for (const key of ['labelKey', 'confirmKey', 'successKey', 'failedKey']) assert.ok(a[key], `${a.command}.${key}`);
  }
});

test('the embedded page forwards only listed commands that carry a request id', () => {
  assert.equal(isTileCommand({ eventType: 'Command', command: 'recenter_player', requestId: 'r1' }), true);
  assert.equal(isTileCommand({ eventType: 'Command', command: 'reset_session', requestId: 'r1' }), true);
  assert.equal(isTileCommand({ eventType: 'Command', command: 'start_session', requestId: 'r1', args: { participantCode: 'P-01' } }), false);
  assert.equal(isTileCommand({ eventType: 'Command', command: 'recenter_player' }), false);
  assert.equal(isTileCommand({ eventType: 'RequestSync', command: 'recenter_player', requestId: 'r1' }), false);
  assert.equal(isTileCommand(null), false);
  assert.equal(isTileCommand('recenter_player'), false);
});

test('every action is blocked while the headset is offline or silent', () => {
  for (const a of TILE_ACTIONS) {
    assert.equal(isActionBlocked(a, null), true, a.command);
    assert.equal(isActionBlocked(a, { ...running, connected: false }), true, a.command);
    assert.equal(isActionBlocked(a, running), false, a.command);
  }
});

test('restart needs a session to restart, the others work on the waiting screen too', () => {
  const waiting = { connected: true, hasStationStatus: true, sessionActive: false, sessionCompleted: false };
  assert.equal(isActionBlocked(action('reset_session'), waiting), true);
  assert.equal(isActionBlocked(action('recenter_player'), waiting), false);
  assert.equal(isActionBlocked(action('return_all_ppe'), waiting), false);
  assert.equal(isActionBlocked(action('reset_session'), { ...waiting, sessionCompleted: true }), false);
});

test('an app that predates StationStatus keeps restart available', () => {
  assert.equal(isActionBlocked(action('reset_session'), { connected: true, hasStationStatus: false }), false);
});
