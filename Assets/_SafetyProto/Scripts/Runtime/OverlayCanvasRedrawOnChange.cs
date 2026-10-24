using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Renders an <see cref="OVROverlayCanvas"/> only when its content changes, instead of every
    /// <c>renderInterval</c> frames while it is in view. Each render is a main-thread camera render
    /// of the canvas, so a board whose content never changes was paying for dozens of identical
    /// renders a second.
    ///
    /// The canvas switches to <c>manualRedraw</c> and is marked dirty when any of its graphics
    /// rebuilds (text, sprite, layout, material), turns on or off, or changes its tint (a Button's
    /// hover and press fades). OVROverlayCanvas still redraws on its own when the player walks
    /// close enough for dynamic resolution to want a sharper texture.
    ///
    /// Not for canvases that animate every frame (the checklist's pulsing row): they would redraw
    /// every frame and cost more than their render interval.
    /// </summary>
    [RequireComponent(typeof(OVROverlayCanvas))]
    public class OverlayCanvasRedrawOnChange : MonoBehaviour
    {
        // uGUI and TMP finish their layout late in the frame, and dynamic glyphs land in the font
        // atlas after the first mesh build, so the frame the canvas appears is not final.
        private const int RedrawFramesAfterEnable = 3;
        private const float SettleRedrawSeconds = 0.5f;

        private static readonly List<OverlayCanvasRedrawOnChange> s_Active = new();
        private static bool s_Enabled = true;

        private OVROverlayCanvas _canvas;
        private Graphic[] _graphics = System.Array.Empty<Graphic>();
        private bool[] _wasActive = System.Array.Empty<bool>();
        private Color[] _lastTint = System.Array.Empty<Color>();
        private int _redrawFramesLeft;
        private float _settleRedrawAt;
        private bool _dirty;

        /// <summary>
        /// Turns change-driven redraw on or off for every canvas carrying this component, so a
        /// launch override can compare it with the canvases' render interval.
        /// </summary>
        public static void SetEnabledForAll(bool enabled)
        {
            s_Enabled = enabled;
            foreach (var redraw in s_Active)
                redraw.ApplyMode();
        }

        /// <summary>Re-renders the canvas next frame.</summary>
        public void Redraw() => _dirty = true;

        private void Awake() => _canvas = GetComponent<OVROverlayCanvas>();

        private void OnEnable()
        {
            s_Active.Add(this);

            _graphics = GetComponentsInChildren<Graphic>(true);
            _wasActive = new bool[_graphics.Length];
            _lastTint = new Color[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++)
            {
                _graphics[i].RegisterDirtyVerticesCallback(Redraw);
                _graphics[i].RegisterDirtyLayoutCallback(Redraw);
                _graphics[i].RegisterDirtyMaterialCallback(Redraw);
                _wasActive[i] = _graphics[i].isActiveAndEnabled;
                _lastTint[i] = _graphics[i].canvasRenderer.GetColor();
            }

            _redrawFramesLeft = RedrawFramesAfterEnable;
            _settleRedrawAt = Time.unscaledTime + SettleRedrawSeconds;
            ApplyMode();
        }

        private void OnDisable()
        {
            s_Active.Remove(this);
            foreach (var graphic in _graphics)
            {
                if (graphic == null) continue;
                graphic.UnregisterDirtyVerticesCallback(Redraw);
                graphic.UnregisterDirtyLayoutCallback(Redraw);
                graphic.UnregisterDirtyMaterialCallback(Redraw);
            }
        }

        private void ApplyMode() => _canvas.manualRedraw = s_Enabled;

        // LateUpdate sees this frame's changes; OVROverlayCanvas renders them in next frame's Update.
        private void LateUpdate()
        {
            if (!s_Enabled) return;

            for (int i = 0; i < _graphics.Length; i++)
            {
                var graphic = _graphics[i];
                if (graphic == null) continue;

                // Activation and tint fades change what is drawn without dirtying the graphic.
                bool active = graphic.isActiveAndEnabled;
                var tint = graphic.canvasRenderer.GetColor();
                if (active != _wasActive[i] || tint != _lastTint[i])
                {
                    _wasActive[i] = active;
                    _lastTint[i] = tint;
                    _dirty = true;
                }
            }

            if (_redrawFramesLeft > 0)
            {
                _redrawFramesLeft--;
                _dirty = true;
            }
            if (_settleRedrawAt > 0f && Time.unscaledTime >= _settleRedrawAt)
            {
                _settleRedrawAt = 0f;
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                _canvas.SetFrameDirty();
            }
        }
    }
}
