import * as THREE from 'three';
import { getLang } from './i18n.js';
import { toThreeFloor, toThreeMarker, floorAt, markerState, formatMeters } from './layout-math.js';

const LABEL_KEYS = {
  Guardrail: 'markerGuardrail', Toeboard: 'markerToeboard',
  Anchor: 'markerAnchor', Irregularity: 'markerIrregularity',
};

/* Walkable floors and task-spot markers. Owns its meshes, materials and labels;
   EvaluatorViewport only forwards theme, layout, task state and label projection. */
export class SceneLayoutView {
  constructor(scene, overlayEl) {
    this._overlay = overlayEl;
    this._root = new THREE.Group();
    scene.add(this._root);
    this._floors = [];    // { data, group, labels: [{ el, local, value }] }
    this._markers = [];   // { data, group, fill, edges, label, visible, warned }
    this._box = new THREE.Box3();
    this._current = null;   // floor under the participant last frame (sticky lookup)

    this._matFloorFill     = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.08, depthWrite: false, side: THREE.DoubleSide });
    this._matFloorEdge     = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.7 });
    this._matSpotFill      = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.18, depthWrite: false });
    this._matSpotDone      = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.12, depthWrite: false });
    this._matSpotEdge      = new THREE.LineBasicMaterial();
    this._matSpotEdgeDone  = new THREE.LineBasicMaterial();
  }

  setTheme(tok) {
    this._matFloorFill.color    = tok('--accent');
    this._matFloorEdge.color    = tok('--accent');
    this._matSpotFill.color     = tok('--orange');
    this._matSpotEdge.color     = tok('--orange');
    this._matSpotDone.color     = tok('--ppe-worn');
    this._matSpotEdgeDone.color = tok('--ppe-worn');
  }

  setLayout(dto) {
    this._clear();
    for (const raw of dto?.floors ?? []) this._floors.push(this._buildFloor(toThreeFloor(raw)));
    for (const raw of dto?.markers ?? []) this._markers.push(this._buildMarker(toThreeMarker(raw)));
  }

  refreshMarkers(tasks) {
    for (const m of this._markers) {
      if (tasks.size > 0 && !tasks.has(m.data.taskId) && !m.warned) {
        console.warn(`[scene-layout] marker '${m.data.id}' references unknown task '${m.data.taskId}'`);
        m.warned = true;
      }
      const { visible, done } = markerState(m.data.taskId, tasks);
      m.visible = visible;
      m.group.visible = visible;
      m.fill.material  = done ? this._matSpotDone : this._matSpotFill;
      m.edges.material = done ? this._matSpotEdgeDone : this._matSpotEdge;
      m.label.classList.toggle('done', done);
    }
  }

  floorAt(x, z, headY) {
    this._current = floorAt(this._floors.map(f => f.data), x, z, headY, undefined, this._current);
    return this._current;
  }

  /** project(worldVec3) → { x, y, visible } in container pixels. */
  /** project(worldVec3) → { x, y, visible } in container pixels. Anchors are precomputed:
      floors and spots never move, so a frame only projects and skips hidden labels. */
  updateLabels(project) {
    const lang = getLang();
    for (const f of this._floors) {
      for (const l of f.labels) {
        const text = formatMeters(l.value, lang);
        if (l.el.textContent !== text) l.el.textContent = text;
        this._place(l.el, project(l.world));
      }
    }
    for (const m of this._markers) {
      if (m.visible) this._place(m.label, project(m.labelAnchor));
      else this._hide(m.label);
    }
  }

  dispose() {
    this._clear();
    [this._matFloorFill, this._matFloorEdge, this._matSpotFill, this._matSpotDone,
     this._matSpotEdge, this._matSpotEdgeDone].forEach(m => m.dispose());
    this._root.removeFromParent();
  }

  _place(el, sp) {
    if (!sp.visible) { this._hide(el); return; }
    el.style.display = 'block';
    el.style.left = `${sp.x}px`;
    el.style.top  = `${sp.y}px`;
  }

  _hide(el) {
    if (el.style.display !== 'none') el.style.display = 'none';
  }

  _label(className) {
    const el = document.createElement('div');
    el.className = `vp-label layout ${className}`;
    el.style.display = 'none';
    this._overlay.appendChild(el);
    return el;
  }

  _buildFloor(data) {
    const group = new THREE.Group();
    group.position.set(data.cx, data.cy + 0.003, data.cz);   /* above the deck mesh: no z-fighting */
    group.quaternion.set(data.qx, data.qy, data.qz, data.qw);

    const plane = new THREE.PlaneGeometry(data.sx, data.sz);
    plane.rotateX(-Math.PI / 2);
    group.add(new THREE.Mesh(plane, this._matFloorFill));
    group.add(new THREE.LineSegments(new THREE.EdgesGeometry(plane), this._matFloorEdge));
    this._root.add(group);
    group.updateMatrixWorld(true);

    const labels = [
      { el: this._label('dim'), world: group.localToWorld(new THREE.Vector3(0, 0, data.sz / 2)), value: data.sx },
      { el: this._label('dim'), world: group.localToWorld(new THREE.Vector3(data.sx / 2, 0, 0)), value: data.sz },
    ];
    return { data, group, labels };
  }

  _buildMarker(data) {
    const group = new THREE.Group();
    group.position.set(data.cx, data.cy, data.cz);
    group.quaternion.set(data.qx, data.qy, data.qz, data.qw);

    const box = new THREE.BoxGeometry(data.sx, data.sy, data.sz);
    const fill = new THREE.Mesh(box, this._matSpotFill);
    const edges = new THREE.LineSegments(new THREE.EdgesGeometry(box), this._matSpotEdge);
    group.add(fill, edges);
    group.visible = false;
    this._root.add(group);
    group.updateMatrixWorld(true);
    this._box.setFromObject(group);
    const labelAnchor = new THREE.Vector3(data.cx, this._box.max.y + 0.06, data.cz);

    const label = this._label('spot');
    label.dataset.i18n = LABEL_KEYS[data.kind] ?? 'markerIrregularity';
    return { data, group, fill, edges, label, labelAnchor, visible: false, warned: false };
  }

  _clear() {
    for (const f of this._floors) {
      f.group.traverse(o => o.geometry?.dispose());
      f.labels.forEach(l => l.el.remove());
    }
    for (const m of this._markers) {
      m.group.traverse(o => o.geometry?.dispose());
      m.label.remove();
    }
    this._root.clear();
    this._current = null;
    this._floors = [];
    this._markers = [];
  }
}
