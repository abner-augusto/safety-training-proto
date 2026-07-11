    /* ================================================================
     * SidebarResizeManager — drag-to-resize with localStorage persist
     * ============================================================== */
    export class SidebarResizeManager {
      /**
       * @param {HTMLElement} sidebar   — the element whose width changes
       * @param {HTMLElement} handle    — the drag target on the right edge
       * @param {EvaluatorViewport} viewport — notified after each resize
       * @param {{ min: number, max: number, default: number }} opts
       */
      constructor(sidebar, handle, viewport, opts = {}) {
        this._sidebar  = sidebar;
        this._handle   = handle;
        this._viewport = viewport;
        this._min      = opts.min     ?? 260;
        this._max      = opts.max     ?? 700;
        this._default  = opts.default ?? 380;
        this._storageKey = 'sp_sidebar_w';

        this._dragging  = false;
        this._startX    = 0;
        this._startW    = 0;

        this._restore();
        this._bind();
      }

      /* ── Private ── */

      _restore() {
        const saved = parseInt(localStorage.getItem(this._storageKey), 10);
        const w = (!isNaN(saved) && saved >= this._min && saved <= this._max)
          ? saved : this._default;
        this._applyWidth(w);
      }

      _applyWidth(w) {
        const clamped = Math.max(this._min, Math.min(this._max, w));
        this._sidebar.style.setProperty('--sidebar-w', `${clamped}px`);
        // Override the CSS var directly on the element for immediate effect
        this._sidebar.style.width = `${clamped}px`;
      }

      _bind() {
        this._handle.addEventListener('mousedown', e => this._onMouseDown(e));
        /* Touch support */
        this._handle.addEventListener('touchstart', e => this._onTouchStart(e), { passive: false });
      }

      _onMouseDown(e) {
        e.preventDefault();
        this._dragging = true;
        this._startX   = e.clientX;
        this._startW   = this._sidebar.offsetWidth;
        this._handle.classList.add('dragging');
        document.body.classList.add('resizing');

        const onMove = e => this._onMouseMove(e);
        const onUp   = () => {
          this._dragging = false;
          this._handle.classList.remove('dragging');
          document.body.classList.remove('resizing');
          this._save();
          document.removeEventListener('mousemove', onMove);
          document.removeEventListener('mouseup', onUp);
          /* Tell the viewport its container changed size */
          this._viewport?.resize();
        };

        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
      }

      _onMouseMove(e) {
        if (!this._dragging) return;
        const delta = e.clientX - this._startX;
        this._applyWidth(this._startW + delta);
        /* mantém o aspecto do canvas correto DURANTE o arrasto (1×/frame) */
        if (!this._resizeQueued) {
          this._resizeQueued = true;
          requestAnimationFrame(() => { this._resizeQueued = false; this._viewport?.resize(); });
        }
      }

      _onTouchStart(e) {
        e.preventDefault();
        const touch = e.touches[0];
        this._dragging = true;
        this._startX   = touch.clientX;
        this._startW   = this._sidebar.offsetWidth;
        this._handle.classList.add('dragging');

        const onMove = e => {
          const t = e.touches[0];
          const delta = t.clientX - this._startX;
          this._applyWidth(this._startW + delta);
        };
        const onEnd = () => {
          this._dragging = false;
          this._handle.classList.remove('dragging');
          this._save();
          this._viewport?.resize();
          document.removeEventListener('touchmove', onMove);
          document.removeEventListener('touchend', onEnd);
        };

        document.addEventListener('touchmove', onMove, { passive: false });
        document.addEventListener('touchend', onEnd);
      }

      _save() {
        localStorage.setItem(this._storageKey, this._sidebar.offsetWidth);
      }
    }
