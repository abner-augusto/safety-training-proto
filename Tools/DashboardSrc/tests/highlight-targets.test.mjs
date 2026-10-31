import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  HIGHLIGHT_DURATIONS, DEFAULT_HIGHLIGHT_SECONDS, groupTargets, targetLabel, highlightArgs, targetsSignature,
} from '../js/highlight-targets.js';

const targets = [
  { id: 'piece:GuardRailPiece', label: 'Peça: Instalar Guarda-corpo', kind: 'piece' },
  { id: 'ppe:Safety_Helmet', label: 'Capacete', kind: 'ppe' },
  { id: 'distractor:Distractor_FlipFlops', label: 'Distrator: Chinelo', kind: 'distractor' },
  { id: 'ppe:Safety_Boots', label: 'Botina de segurança', kind: 'ppe' },
  { id: 'anchor:LanyardAnchor', label: 'Ponto de ancoragem', kind: 'anchor' },
  { id: 'mystery:x', label: 'X', kind: 'mystery' },
];

test('targets are grouped by kind in training order, keeping the app order inside a group', () => {
  const groups = groupTargets(targets);
  assert.deepEqual(groups.map(g => g.kind), ['ppe', 'distractor', 'anchor', 'piece', 'tool']);
  assert.deepEqual(groups[0].targets.map(t => t.id), ['ppe:Safety_Helmet', 'ppe:Safety_Boots']);
  assert.equal(groups[0].labelKey, 'highlightKind_ppe');
});

test('an unknown kind lands under tools instead of being dropped', () => {
  const tools = groupTargets(targets).find(g => g.kind === 'tool');
  assert.deepEqual(tools.targets.map(t => t.id), ['mystery:x']);
});

test('an empty or malformed list gives no groups', () => {
  assert.deepEqual(groupTargets(undefined), []);
  assert.deepEqual(groupTargets([null, { label: 'no id' }]), []);
});

test('the confirm label uses the app label of the target', () => {
  assert.equal(targetLabel(targets, 'ppe:Safety_Helmet'), 'Capacete');
  assert.equal(targetLabel(targets, 'gone:id'), 'gone:id');
});

test('a command needs a target and sends one of the offered durations', () => {
  assert.deepEqual(highlightArgs('', 8), { errorKey: 'highlightPickTarget' });
  assert.deepEqual(highlightArgs('ppe:Safety_Helmet', 15), { args: { targetId: 'ppe:Safety_Helmet', durationSeconds: 15 } });
  assert.equal(highlightArgs('ppe:Safety_Helmet', '5').args.durationSeconds, 5);
  assert.equal(highlightArgs('ppe:Safety_Helmet', 99).args.durationSeconds, DEFAULT_HIGHLIGHT_SECONDS);
  assert.ok(HIGHLIGHT_DURATIONS.includes(DEFAULT_HIGHLIGHT_SECONDS));
});

test('the signature changes only when the list of ids does', () => {
  assert.equal(targetsSignature(targets), targetsSignature(targets.map(t => ({ ...t, label: 'other' }))));
  assert.notEqual(targetsSignature(targets), targetsSignature(targets.slice(1)));
});
