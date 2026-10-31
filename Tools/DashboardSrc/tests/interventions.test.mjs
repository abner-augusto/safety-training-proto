import { test } from 'node:test';
import assert from 'node:assert/strict';
import { recordSent, settle, outcomeOf, MAX_INTERVENTIONS } from '../js/interventions.js';
import { COMMAND_ACK_TIMEOUT_MS } from '../js/constants.js';

const T0 = 5_000_000;
const send = (command, requestId) => ({ eventType: 'Command', command, requestId });

test('a sent command is listed as pending with its time and wire name', () => {
  const list = recordSent([], send('recenter_player', 'r1'), T0);
  assert.equal(list.length, 1);
  assert.equal(list[0].command, 'recenter_player');
  assert.equal(list[0].sentAt, T0);
  assert.equal(outcomeOf(list[0], T0 + 100), 'pending');
});

test('entries keep the order they were sent in', () => {
  let list = recordSent([], send('recenter_player', 'r1'), T0);
  list = recordSent(list, send('return_all_ppe', 'r2'), T0 + 10);
  assert.deepEqual(list.map(e => e.command), ['recenter_player', 'return_all_ppe']);
});

test('an accepted ack settles its own entry and keeps the app summary', () => {
  let list = recordSent([], send('return_all_ppe', 'r1'), T0);
  list = recordSent(list, send('recenter_player', 'r2'), T0 + 10);
  list = settle(list, { requestId: 'r1', command: 'return_all_ppe', accepted: true, reason: '3 EPIs devolvidos à mesa' });
  assert.equal(outcomeOf(list[0], T0 + 20), 'accepted');
  assert.equal(list[0].reason, '3 EPIs devolvidos à mesa');
  assert.equal(outcomeOf(list[1], T0 + 20), 'pending');
});

test('a rejected ack keeps the reason the app gave', () => {
  let list = recordSent([], send('reset_session', 'r1'), T0);
  list = settle(list, { requestId: 'r1', accepted: false, reason: 'Nenhuma sessão em andamento.' });
  assert.equal(outcomeOf(list[0], T0 + 20), 'rejected');
  assert.equal(list[0].reason, 'Nenhuma sessão em andamento.');
});

test('a command nobody answered turns into no response after the ack window', () => {
  const list = recordSent([], send('recenter_player', 'r1'), T0);
  assert.equal(outcomeOf(list[0], T0 + COMMAND_ACK_TIMEOUT_MS), 'pending');
  assert.equal(outcomeOf(list[0], T0 + COMMAND_ACK_TIMEOUT_MS + 1), 'noResponse');
});

test('a late ack still settles the entry with what really happened', () => {
  let list = recordSent([], send('recenter_player', 'r1'), T0);
  list = settle(list, { requestId: 'r1', accepted: true, reason: '' });
  assert.equal(outcomeOf(list[0], T0 + 60_000), 'accepted');
});

test('an ack for another page, an unknown id or a repeat changes nothing', () => {
  const list = recordSent([], send('recenter_player', 'r1'), T0);
  assert.equal(settle(list, { requestId: 'other', accepted: true }), list);
  assert.equal(settle(list, null), list);
  assert.equal(settle(list, { accepted: true }), list);
  const settled = settle(list, { requestId: 'r1', accepted: true, reason: 'ok' });
  assert.equal(settle(settled, { requestId: 'r1', accepted: false, reason: 'again' }), settled);
});

test('anything that is not a command with an id is not recorded', () => {
  const list = [];
  assert.equal(recordSent(list, { eventType: 'RequestSync' }, T0), list);
  assert.equal(recordSent(list, { eventType: 'Command', command: 'recenter_player' }, T0), list);
  assert.equal(recordSent(list, { eventType: 'Command', requestId: 'r1' }, T0), list);
  assert.equal(recordSent(list, null, T0), list);
});

test('the list keeps only the most recent entries', () => {
  let list = [];
  for (let i = 0; i < MAX_INTERVENTIONS + 5; i++) list = recordSent(list, send('recenter_player', `r${i}`), T0 + i);
  assert.equal(list.length, MAX_INTERVENTIONS);
  assert.equal(list[list.length - 1].requestId, `r${MAX_INTERVENTIONS + 4}`);
  assert.equal(list[0].requestId, 'r5');
});
