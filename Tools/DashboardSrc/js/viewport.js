    import * as THREE from 'three';
    import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
    import { t } from './i18n.js';

    /* Automatic reframing */
    const FRAME_EDGE = 0.92;   /* NDC: fires just BEFORE the participant clips the edge */
    const FRAME_DWELL_MS = 800;  /* time off-frame before the camera moves */
    const FRAME_GLIDE_MS = 600;  /* glide duration */

    /* ================================================================
     * EvaluatorViewport — Three.js scene for the field instrument.
     * Colours come from the CSS tokens (setTheme). The avatar is a skeleton
     * DERIVED from tracking (real head + hands; shoulders/spine/hips are
     * inferred; a plumb line + floor disc instead of invented legs).
     * Render on demand: it only draws when the pose, the camera or the
     * theme changes, or while an interaction ring is alive.
     * ============================================================== */
    export class EvaluatorViewport {
      constructor(containerEl) {
        this._container = containerEl;
        this._dirty = true;

        /* Scene (cores aplicadas em setTheme) */
        this._scene = new THREE.Scene();

        /* Camera — instrument FOV (55), not an FPS one */
        this._camera = new THREE.PerspectiveCamera(
          55,
          containerEl.clientWidth / containerEl.clientHeight,
          0.1, 100
        );
        this._camera.position.set(0, 1.8, 3.2);

        /* Renderer — capped pixel ratio (4K screens must not cost 9×) */
        this._renderer = new THREE.WebGLRenderer({ antialias: true });
        this._renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        this._renderer.setSize(containerEl.clientWidth, containerEl.clientHeight);
        containerEl.appendChild(this._renderer.domElement);

        /* Controls — damping para leitura suave; 'change' marca sujo */
        this._controls = new OrbitControls(this._camera, this._renderer.domElement);
        this._controls.target.set(0, 1.0, 0);
        this._controls.enableDamping = true;
        this._controls.dampingFactor = 0.08;
        this._controls.addEventListener('change', () => { this._dirty = true; });
        this._controls.update();

        /* Lighting */
        this._scene.add(new THREE.AmbientLight(0xffffff, 1.6));
        const dir = new THREE.DirectionalLight(0xffffff, 1.2);
        dir.position.set(2, 5, 2);
        this._scene.add(dir);

        /* Label overlay */
        this._labelOverlay = document.createElement('div');
        Object.assign(this._labelOverlay.style, {
          position: 'absolute', top: '0', left: '0',
          width: '100%', height: '100%', pointerEvents: 'none',
        });
        containerEl.appendChild(this._labelOverlay);

        /* Object + label pools */
        this._objects = { hmd: null, leftHand: null, rightHand: null, ppe: {} };
        this._labels  = new Map();   // mesh → { el, yOffset }

        /* Shared geometries */
        this._geoHead = new THREE.BoxGeometry(0.2, 0.15, 0.25);
        this._geoHand = new THREE.SphereGeometry(0.05, 16, 16);
        this._geoPPE  = new THREE.BoxGeometry(0.1, 0.1, 0.1);
        this._geoRing = new THREE.RingGeometry(0.10, 0.125, 32);

        /* Shared materials — colours assigned in setTheme */
        this._matHead        = new THREE.MeshLambertMaterial();
        this._matHand        = new THREE.MeshLambertMaterial();
        this._matPPELoose    = new THREE.MeshLambertMaterial();   // pending  = amber
        this._matPPEAttached = new THREE.MeshLambertMaterial();   // fulfilled = neutral
        this._matRing        = new THREE.MeshBasicMaterial({ transparent: true, side: THREE.DoubleSide });

        /* Derived skeleton: spine, shoulders, hips, 2 arms (5 segments) */
        this._skelPos = new Float32Array(5 * 2 * 3);
        const skelGeo = new THREE.BufferGeometry();
        skelGeo.setAttribute('position', new THREE.BufferAttribute(this._skelPos, 3));
        this._matSkel = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.85 });
        this._skel = new THREE.LineSegments(skelGeo, this._matSkel);
        this._skel.visible = false;
        this._skel.frustumCulled = false;
        this._scene.add(this._skel);

        /* Plumb line (hips → floor), dashed — honest about being derived */
        this._plumbPos = new Float32Array(2 * 3);
        const plumbGeo = new THREE.BufferGeometry();
        plumbGeo.setAttribute('position', new THREE.BufferAttribute(this._plumbPos, 3));
        this._matPlumb = new THREE.LineDashedMaterial({ dashSize: 0.05, gapSize: 0.06, transparent: true, opacity: 0.6 });
        this._plumb = new THREE.Line(plumbGeo, this._matPlumb);
        this._plumb.visible = false;
        this._plumb.frustumCulled = false;
        this._scene.add(this._plumb);

        /* Gaze fan (2 segments out of the HMD) */
        this._gazePos = new Float32Array(2 * 2 * 3);
        const gazeGeo = new THREE.BufferGeometry();
        gazeGeo.setAttribute('position', new THREE.BufferAttribute(this._gazePos, 3));
        this._matGaze = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.35 });
        this._gaze = new THREE.LineSegments(gazeGeo, this._matGaze);
        this._gaze.visible = false;
        this._gaze.frustumCulled = false;
        this._scene.add(this._gaze);

        /* Floor disc under the participant */
        this._matDisc = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.12, depthWrite: false });
        this._disc = new THREE.Mesh(new THREE.CircleGeometry(0.3, 28), this._matDisc);
        this._disc.rotation.x = -Math.PI / 2;
        this._disc.position.y = 0.005;
        this._disc.visible = false;
        this._scene.add(this._disc);

        /* Pose smoothing (data arrives at 10 Hz) */
        this._smooth = { head: null, yaw: 0 };

        /* Automatic reframing: if the participant leaves the field of view and
           STAYS out for DWELL ms, the camera glides after them. The dwell keeps
           someone who merely grazed the edge and came back from yanking it. */
        this._follow = { offSince: 0, anim: null };

        /* DOM instruments */
        this._calloutEl    = document.getElementById('vp-callout');
        this._postureChip  = document.getElementById('posture-chip');
        this._postureValue = document.getElementById('posture-value');

        /* Live interaction rings (ActionAttempt carrying a position) */
        this._interactions = [];

        /* The grid is rebuilt by setTheme (GridHelper bakes its colours) */
        this._grid = null;
        this.setTheme();

        /* Resize listener */
        this._onResize = () => this.resize();
        window.addEventListener('resize', this._onResize);

        /* Start loop */
        this._animate();
      }

      /* ── Public API ───────────────────────────────────────────── */

      /** Re-reads the active CSS tokens and applies them to the scene (light/dark). */
      setTheme() {
        const css = getComputedStyle(document.documentElement);
        const tok = name => new THREE.Color(css.getPropertyValue(name).trim() || '#ff00ff');
        const bg  = tok('--bg');
        const isLight = document.documentElement.dataset.theme === 'light';

        this._scene.background = bg;
        this._scene.fog = new THREE.Fog(bg, 8, 30);
        this._matHead.color        = tok('--accent');
        this._matHand.color        = tok('--text-muted');
        this._matPPELoose.color    = tok('--orange');
        this._matPPEAttached.color = tok('--ppe-worn');
        this._matSkel.color        = tok('--text-muted');
        this._matPlumb.color       = tok('--text-dim');
        this._matGaze.color        = tok('--accent');
        this._matDisc.color        = tok('--accent');
        this._matRing.color        = tok('--accent');

        if (this._grid) {
          this._scene.remove(this._grid);
          this._grid.geometry.dispose();
          this._grid.material.dispose();
        }
        const gridColor = tok('--text-dim');
        this._grid = new THREE.GridHelper(10, 10, gridColor, gridColor);
        this._grid.material.transparent = true;
        this._grid.material.opacity = isLight ? 0.35 : 0.25;
        this._scene.add(this._grid);

        this._dirty = true;
      }

      /** Call after the container element changes size (e.g. sidebar resize). */
      resize() {
        const w = this._container.clientWidth;
        const h = this._container.clientHeight;
        if (!w || !h) return;
        this._camera.aspect = w / h;
        this._camera.updateProjectionMatrix();
        this._renderer.setSize(w, h);
        this._dirty = true;
      }

      /** Full pose frame (10 Hz): HMD, hands, PPE and the derived skeleton. */
      updateFrame(p) {
        if (p.hmd)       this._updateObject('hmd',       p.hmd,       'hmd');
        if (p.leftHand)  this._updateObject('leftHand',  p.leftHand,  'hand');
        if (p.rightHand) this._updateObject('rightHand', p.rightHand, 'hand');
        if (Array.isArray(p.ppe)) {
          const present = new Set();
          p.ppe.forEach(item => {
            present.add(String(item.id));
            this._updateObject(item.id, item.pose, 'ppe', item.attachedTo);
          });
          /* PPE that left the frame left the scene (hideWhenEquipped vanishes on equip):
             without this reaping the mesh would stay frozen at the slot forever. It comes
             back on its own if the item reappears — _updateObject recreates what is missing. */
          Object.keys(this._objects.ppe).forEach(id => {
            if (!present.has(id)) this._removePpe(id);
          });
        }
        this._updateBody();
        this._dirty = true;
      }

      /** ActionAttempt with a position: pulsing ring + callout naming the action. */
      showInteraction(actionId, px, py, pz) {
        const pos = new THREE.Vector3(px, py, -pz);
        const mesh = new THREE.Mesh(this._geoRing, this._matRing.clone());
        mesh.position.copy(pos);
        this._scene.add(mesh);
        this._interactions.push({ mesh, label: String(actionId ?? ''), pos, t0: performance.now() });
        while (this._interactions.length > 3) this._removeInteraction(this._interactions.shift());
        this._dirty = true;
      }

      /** Clears PPE, skeleton and interactions — called on SessionReset. */
      clearSession() {
        Object.keys(this._objects.ppe).forEach(id => this._removePpe(id));
        this._objects.ppe = {};
        this._interactions.forEach(it => this._removeInteraction(it));
        this._interactions = [];
        this._skel.visible = this._plumb.visible = this._gaze.visible = this._disc.visible = false;
        this._smooth.head = null;
        /* A reset mid-glide must not leave the orbit controls stranded. */
        this._follow.anim = null;
        this._follow.offSince = 0;
        this._controls.enabled = true;
        if (this._postureChip) this._postureChip.style.display = 'none';
        this._calloutEl?.classList.remove('on');
        this._dirty = true;
      }

      /** Release GPU resources (call if viewport is ever torn down). */
      dispose() {
        window.removeEventListener('resize', this._onResize);
        this.clearSession();
        [this._geoHead, this._geoHand, this._geoPPE, this._geoRing].forEach(g => g.dispose());
        [this._matHead, this._matHand, this._matPPELoose, this._matPPEAttached,
         this._matSkel, this._matPlumb, this._matGaze, this._matDisc, this._matRing].forEach(m => m.dispose());
        this._labels.forEach(ld => ld.el.remove());
        this._labels.clear();
        this._renderer.dispose();
      }

      /* ── Private helpers ──────────────────────────────────────── */

      _updateObject(id, pose, type, attachedTo = '') {
        let obj = type === 'ppe' ? this._objects.ppe[id] : this._objects[id];
        if (!obj) obj = this._createObject(id, type);

        /* Unity (left-handed) → Three (right-handed): we mirror Z on the position, so the
           rotation needs the SAME conjugation by diag(1,1,-1) — that is (-qx,-qy, qz, qw).
           Mirroring any other axis here inverts pitch and roll while leaving yaw correct
           (the gaze pointed up whenever the participant looked down). */
        obj.position.set(pose.px, pose.py, -pose.pz);
        obj.quaternion.set(-pose.qx, -pose.qy, pose.qz, pose.qw);

        if (type === 'ppe') {
          /* semantics, not identity: loose = pending obligation (amber);
             worn = fulfilled obligation (neutral, shrunk) */
          if (attachedTo) { obj.material = this._matPPEAttached; obj.scale.setScalar(0.8); }
          else            { obj.material = this._matPPELoose;    obj.scale.setScalar(1); }
          obj.userData.worn = !!attachedTo;
        }
      }

      /* Derived skeleton + plumb line + gaze + disc + posture.
         Nothing here is animated — it is all inferred from the tracked pose,
         so locomotion never produces a broken "walk". */
      _updateBody() {
        const hmd = this._objects.hmd;
        if (!hmd) return;

        if (!this._smooth.head) this._smooth.head = hmd.position.clone();
        this._smooth.head.lerp(hmd.position, 0.35);
        const H = this._smooth.head;

        /* the damped HMD yaw orients the shoulders */
        const fwd = new THREE.Vector3(0, 0, -1).applyQuaternion(hmd.quaternion);
        const targetYaw = Math.atan2(fwd.x, fwd.z);
        let dy = targetYaw - this._smooth.yaw;
        while (dy >  Math.PI) dy -= 2 * Math.PI;
        while (dy < -Math.PI) dy += 2 * Math.PI;
        this._smooth.yaw += dy * 0.25;
        const rx = Math.cos(this._smooth.yaw), rz = -Math.sin(this._smooth.yaw);

        const headY = H.y;
        const shY   = headY - 0.24;
        const hipY  = Math.max(headY - 0.72, 0.25);
        const SH = 0.19, HIP = 0.11;

        const set = (i, ax, ay, az, bx, by, bz) => {
          const o = i * 6;
          this._skelPos[o]   = ax; this._skelPos[o+1] = ay; this._skelPos[o+2] = az;
          this._skelPos[o+3] = bx; this._skelPos[o+4] = by; this._skelPos[o+5] = bz;
        };
        set(0, H.x, headY - 0.10, H.z, H.x, hipY, H.z);                              /* spine     */
        set(1, H.x - rx*SH, shY, H.z - rz*SH, H.x + rx*SH, shY, H.z + rz*SH);        /* shoulders */
        set(2, H.x - rx*HIP, hipY, H.z - rz*HIP, H.x + rx*HIP, hipY, H.z + rz*HIP);  /* hips      */
        const lh = this._objects.leftHand, rh = this._objects.rightHand;
        /* Each arm starts at the shoulder on the SAME side as the hand: the shoulder
           axis comes from the HMD yaw, where +(rx,rz) is the person's left side and
           −(rx,rz) their right; wiring the left hand to the −rx shoulder (or vice
           versa) would cross the forearms over the torso. */
        set(3, H.x + rx*SH, shY, H.z + rz*SH,
               lh ? lh.position.x : H.x + rx*SH, lh ? lh.position.y : shY, lh ? lh.position.z : H.z + rz*SH);
        set(4, H.x - rx*SH, shY, H.z - rz*SH,
               rh ? rh.position.x : H.x - rx*SH, rh ? rh.position.y : shY, rh ? rh.position.z : H.z - rz*SH);
        this._skel.geometry.attributes.position.needsUpdate = true;
        this._skel.visible = true;

        this._plumbPos[0] = H.x; this._plumbPos[1] = hipY; this._plumbPos[2] = H.z;
        this._plumbPos[3] = H.x; this._plumbPos[4] = 0.02; this._plumbPos[5] = H.z;
        this._plumb.geometry.attributes.position.needsUpdate = true;
        this._plumb.computeLineDistances();
        this._plumb.visible = true;

        /* gaze fan */
        const gLen = 1.1;
        const up = new THREE.Vector3(0, 1, 0);
        for (let s = 0; s < 2; s++) {
          const dir = fwd.clone().applyAxisAngle(up, s === 0 ? -0.22 : 0.22);
          const o = s * 6;
          this._gazePos[o]   = H.x;                 this._gazePos[o+1] = headY;                 this._gazePos[o+2] = H.z;
          this._gazePos[o+3] = H.x + dir.x * gLen;  this._gazePos[o+4] = headY + dir.y * gLen;  this._gazePos[o+5] = H.z + dir.z * gLen;
        }
        this._gaze.geometry.attributes.position.needsUpdate = true;
        this._gaze.visible = true;

        this._disc.position.set(H.x, 0.005, H.z);
        this._disc.visible = true;

        this._updatePosture(headY);
        this._trackFraming(H);
      }

      /* Only counts time spent off-frame; re-entering resets the dwell, so someone who
         merely grazes the edge never drags the camera after them. */
      _trackFraming(H) {
        if (this._follow.anim) return;                 /* already reframing */
        /* Pose frames keep arriving while the tab is hidden, but rAF stops: arming here
           would disable the orbit with nobody left to re-enable it. Re-arms on return. */
        if (document.hidden) { this._follow.offSince = 0; return; }

        const p = H.clone().project(this._camera);
        const off = p.z > 1 ||                          /* atrás da câmera */
                    Math.abs(p.x) > FRAME_EDGE ||
                    Math.abs(p.y) > FRAME_EDGE;

        if (!off) { this._follow.offSince = 0; return; }

        const now = performance.now();
        if (!this._follow.offSince) { this._follow.offSince = now; return; }
        if (now - this._follow.offSince >= FRAME_DWELL_MS) this._startFollow(H);
      }

      /* Target and camera move by the SAME delta: the evaluator's orbit angle and zoom
         survive the glide — the view slides sideways instead of swinging around the scene. */
      _startFollow(H) {
        const toTarget = new THREE.Vector3(H.x, Math.max(H.y - 0.4, 0.2), H.z);
        this._follow.anim = {
          elapsed: 0,
          last: performance.now(),
          fromTarget: this._controls.target.clone(),
          fromCam: this._camera.position.clone(),
          delta: toTarget.sub(this._controls.target),
        };
        this._controls.enabled = false;   /* input and damping must not fight the glide */
      }

      /** @returns {boolean} true while a glide is in progress. */
      _stepFollow() {
        const a = this._follow.anim;
        if (!a) return false;

        /* Time measured in steps, not on the wall clock: if the tab is hidden mid-glide
           rAF freezes, and a raw dt would snap the camera straight to the end. */
        const now = performance.now();
        a.elapsed += Math.min(now - a.last, 50);
        a.last = now;

        const k = Math.min(a.elapsed / FRAME_GLIDE_MS, 1);
        const e = k < 0.5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;   /* easeInOutQuad */
        this._controls.target.copy(a.fromTarget).addScaledVector(a.delta, e);
        this._camera.position.copy(a.fromCam).addScaledVector(a.delta, e);
        this._dirty = true;

        if (k >= 1) {
          this._follow.anim = null;
          this._follow.offSince = 0;
          this._controls.enabled = true;
        }
        return true;
      }

      _updatePosture(headY) {
        if (!this._postureChip) return;
        this._postureChip.style.display = 'flex';
        let key = 'postureStanding', low = false;
        if (headY <= 1.15)      { key = 'postureCrouched'; low = true; }
        else if (headY <= 1.42) { key = 'postureLowering'; low = true; }
        this._postureValue.textContent = `${t(key)} · ${headY.toFixed(2)} m`;
        this._postureValue.classList.toggle('low', low);
      }

      /* PPE geometry and material are shared across the meshes — remove from the scene
         and drop the label, never dispose of them. */
      _removePpe(id) {
        const mesh = this._objects.ppe[id];
        if (!mesh) return;
        this._scene.remove(mesh);
        const ld = this._labels.get(mesh);
        if (ld) { ld.el.remove(); this._labels.delete(mesh); }
        delete this._objects.ppe[id];
        this._dirty = true;
      }

      _removeInteraction(it) {
        this._scene.remove(it.mesh);
        it.mesh.material.dispose();
      }

      _updateInteractions() {
        const now = performance.now();
        const LIFE = 2600, FADE = 600;
        for (let i = this._interactions.length - 1; i >= 0; i--) {
          const it = this._interactions[i];
          const age = now - it.t0;
          if (age > LIFE) { this._removeInteraction(it); this._interactions.splice(i, 1); continue; }
          it.mesh.scale.setScalar(1 + 0.15 * Math.sin(age / 1000 * 8));
          it.mesh.lookAt(this._camera.position);
          it.mesh.material.opacity = age > LIFE - FADE ? (LIFE - age) / FADE : 1;
        }
        /* the callout follows the most recent interaction */
        const latest = this._interactions[this._interactions.length - 1];
        if (latest && this._calloutEl) {
          const v = latest.pos.clone().project(this._camera);
          if (v.z <= 1) {
            this._calloutEl.textContent = latest.label;
            this._calloutEl.style.left = `${(v.x * 0.5 + 0.5) * this._container.clientWidth}px`;
            this._calloutEl.style.top  = `${(v.y * -0.5 + 0.5) * this._container.clientHeight}px`;
            this._calloutEl.classList.add('on');
          }
        } else if (this._calloutEl) {
          this._calloutEl.classList.remove('on');
        }
      }

      _createObject(id, type) {
        let mesh;
        if      (type === 'hmd')  mesh = new THREE.Mesh(this._geoHead, this._matHead);
        else if (type === 'hand') mesh = new THREE.Mesh(this._geoHand, this._matHand);
        else                      mesh = new THREE.Mesh(this._geoPPE, this._matPPELoose);

        this._scene.add(mesh);
        if (type === 'ppe') this._objects.ppe[id] = mesh;
        else                this._objects[id] = mesh;

        /* Labels only for PPE — the head and hands are already legible from their
           shape/legend; the label stays dark until a hand comes near */
        if (type === 'ppe') this._labels.set(mesh, this._createLabel(id.toUpperCase(), 0.14));

        return mesh;
      }

      _createLabel(text, yOffset) {
        const el = document.createElement('div');
        el.className = 'vp-label';
        el.textContent = text;
        this._labelOverlay.appendChild(el);
        return { el, yOffset };
      }

      _projectLabel(ld, worldPos) {
        const v = worldPos.clone().project(this._camera);
        const x = (v.x * 0.5 + 0.5) * this._container.clientWidth;
        const y = (v.y * -0.5 + 0.5) * this._container.clientHeight;
        const visible = v.z <= 1;
        ld.el.style.display = visible ? 'block' : 'none';
        ld.el.style.left = `${x}px`;
        ld.el.style.top  = `${y}px`;
        return { x, y, visible };
      }

      _animate() {
        requestAnimationFrame(() => this._animate());
        if (document.hidden) return;                 /* hidden tab: nothing to draw */
        const following = this._stepFollow();        /* before update: repositions camera+target */
        this._controls.update();                     /* damping fires 'change' → _dirty */
        const hasFx = this._interactions.length > 0;
        if (hasFx) this._updateInteractions();
        if (!this._dirty && !hasFx && !following) return;   /* render on demand */
        this._dirty = false;
        this._updateLabels();
        this._renderer.render(this._scene, this._camera);
      }

      _updateLabels() {
        /* A label only "lights up" for the loose PPE NEAREST to a hand (within range) —
           with clustered objects, lighting every one inside a radius would clutter the
           scene; worn PPE stays dark even with a hand right next to it */
        const NEAR = 0.35; /* m */
        const nearMeshes = new Set();
        for (const hand of [this._objects.leftHand, this._objects.rightHand]) {
          if (!hand) continue;
          let best = null, bestD = NEAR;
          this._labels.forEach((ld, obj) => {
            if (obj.userData.worn) return;
            const d = hand.position.distanceTo(obj.position);
            if (d < bestD) { bestD = d; best = obj; }
          });
          if (best) nearMeshes.add(best);
        }
        const visible = [];
        this._labels.forEach((ld, obj) => {
          if (!obj?.position) return;
          ld.el.classList.toggle('near', nearMeshes.has(obj));
          const wp = obj.position.clone();
          wp.y += ld.yOffset;
          const sp = this._projectLabel(ld, wp);
          if (sp.visible) visible.push({ el: ld.el, x: sp.x, y: sp.y });
        });

        /* Iterative screen-space push-apart (4 passes, 96×22 px label estimate) */
        const LW = 96, LH = 22, PAD = 6;
        for (let iter = 0; iter < 4; iter++) {
          for (let i = 0; i < visible.length; i++) {
            for (let j = i + 1; j < visible.length; j++) {
              const a = visible[i], b = visible[j];
              const ox = (LW + PAD) - Math.abs(b.x - a.x);
              const oy = (LH + PAD) - Math.abs(b.y - a.y);
              if (ox > 0 && oy > 0) {
                if (ox < oy) {
                  const push = ox / 2 * Math.sign(b.x - a.x || 1);
                  a.x -= push; b.x += push;
                } else {
                  const push = oy / 2 * Math.sign(b.y - a.y || -1);
                  a.y -= push; b.y += push;
                }
              }
            }
          }
        }
        visible.forEach(({ el, x, y }) => {
          el.style.left = `${x}px`;
          el.style.top  = `${y}px`;
        });
      }
    }
