import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toThreeFloor, toThreeMarker, floorAt, markerState, formatMeters } from '../js/layout-math.js';

const deck = toThreeFloor({ id: 'andaime', px: -2.246, py: 21.728, pz: 12.458, sx: 5.925, sz: 0.902, qx: 0, qy: 0, qz: 0, qw: 1 });
const yard = toThreeFloor({ id: 'canteiro', px: 0.336, py: 0.05, pz: 0.661, sx: 7.328, sz: 5.178, qx: 0, qy: 0, qz: 0, qw: 1 });

test('toThreeFloor mirrors Z on position and rotation', () => {
  const f = toThreeFloor({ id: 'f', px: 1, py: 2, pz: 3, sx: 4, sz: 5, qx: 0.1, qy: 0.2, qz: 0.3, qw: 0.9 });
  assert.deepEqual([f.cx, f.cy, f.cz, f.qx, f.qy, f.qz, f.qw], [1, 2, -3, -0.1, -0.2, 0.3, 0.9]);
});

test('toThreeMarker keeps kind, task and size', () => {
  const m = toThreeMarker({ id: 'g', kind: 'Guardrail', taskId: 'install_guardrail', px: 0, py: 1, pz: 2, sx: 0.1, sy: 0.2, sz: 1.1, qx: 0, qy: 0, qz: 0, qw: 1 });
  assert.equal(m.kind, 'Guardrail');
  assert.equal(m.taskId, 'install_guardrail');
  assert.equal(m.cz, -2);
  assert.equal(m.sz, 1.1);
});

test('floorAt finds the deck under a participant standing on it', () => {
  assert.equal(floorAt([yard, deck], -1.0, -12.5, 23.3)?.id, 'andaime');
});

test('floorAt margin keeps the deck while leaning past its 0.90 m edge', () => {
  assert.equal(floorAt([deck], -1.0, -12.458 - 0.6, 23.3)?.id, 'andaime');
  assert.equal(floorAt([deck], -1.0, -12.458 - 0.8, 23.3), null);
});

test('floorAt ignores floors at or above the head', () => {
  assert.equal(floorAt([deck], -1.0, -12.5, 21.0), null);
});

test('floorAt picks the highest floor below the head', () => {
  const low = { ...deck, id: 'low', cy: 10 };
  assert.equal(floorAt([low, deck], -1.0, -12.5, 23.3)?.id, 'andaime');
});

test('floorAt honours a yawed floor', () => {
  const s = Math.SQRT1_2;
  const rotated = toThreeFloor({ id: 'r', px: 0, py: 0, pz: 0, sx: 4, sz: 1, qx: 0, qy: s, qz: 0, qw: s });
  assert.equal(floorAt([rotated], 0, 1.5, 1.7, 0)?.id, 'r');
  assert.equal(floorAt([rotated], 1.5, 0, 1.7, 0), null);
});

test('floorAt on the canteiro returns its 0.05 m height', () => {
  assert.equal(floorAt([yard, deck], 0, 0.75, 1.7)?.cy, 0.05);
});

const tasks = (entries) => new Map(entries.map(t => [t.id, t]));

test('markerState hides an unknown task and an empty session', () => {
  assert.deepEqual(markerState('install_guardrail', new Map()), { visible: false, done: false });
});

test('markerState hides markers until their group starts', () => {
  const m = tasks([
    { id: 'equip_helmet', groupName: 'EPI', status: 'active' },
    { id: 'install_guardrail', groupName: 'Andaime', status: 'pending' },
  ]);
  assert.deepEqual(markerState('install_guardrail', m), { visible: false, done: false });
});

test('markerState shows a pending marker once any task in its group is active', () => {
  const m = tasks([
    { id: 'connect_lanyard', groupName: 'Andaime', status: 'active' },
    { id: 'install_guardrail', groupName: 'Andaime', status: 'pending' },
  ]);
  assert.deepEqual(markerState('install_guardrail', m), { visible: true, done: false });
});

test('markerState marks a completed task done', () => {
  const m = tasks([{ id: 'install_toeboard', groupName: 'Andaime', status: 'completed' }]);
  assert.deepEqual(markerState('install_toeboard', m), { visible: true, done: true });
});

test('markerState keeps a not-performed task visible and pending', () => {
  const m = tasks([{ id: 'report_damaged_safety_mesh', groupName: 'Andaime', status: 'not_performed' }]);
  assert.deepEqual(markerState('report_damaged_safety_mesh', m), { visible: true, done: false });
});

test('formatMeters uses the locale decimal separator', () => {
  assert.equal(formatMeters(5.925, 'pt'), '5,93 m');
  assert.equal(formatMeters(0.902, 'en'), '0.90 m');
});
