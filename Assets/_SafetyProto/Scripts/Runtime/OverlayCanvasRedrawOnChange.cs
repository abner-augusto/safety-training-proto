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
    /// hover and press fades).
    ///
    /// It also redraws when the canvas grows on screen, so one first drawn from across the room
    /// sharpens after the player teleports up to it. Leave the OVROverlayCanvas's own Dynamic
    /// Resolution Redraw off: that path resizes the layer's source rect and then skips the render,
    /// which shrinks the old image inside the panel.
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

        // Growth in on-screen size since the last render that is worth a sharper texture.
        // OVROverlayCanvas sizes the texture from the canvas's projected size at render time.
        private const float RedrawScreenGrowth = 1.1f;

        private static readonly List<OverlayCanvasRedrawOnChange> s_Active = new();
        private static readonly Vector3[] s_Corners = new Vector3[4];
        private static readonly Plane[] s_FrustumPlanes = new Plane[6];
        private static bool s_Enabled = true;

        private OVROverlayCanvas _canvas;
        private Graphic[] _graphics = System.Array.Empty<Graphic>();
        private bool[] _wasActive = System.Array.Empty<bool>();
        private Color[] _lastTint = System.Array.Empty<Color>();
        private int _redrawFramesLeft;
        private float _settleRedrawAt;
        private bool _dirty;
        private RectTransform _rect;
        private float _renderedScreenSize;

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

        private void Awake()
        {
            _canvas = GetComponent<OVROverlayCanvas>();
            _rect = GetComponent<RectTransform>();
        }

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

            var eye = OVRManager.FindMainCamera();
            if (eye == null) return;

            float screenSize = ScreenSize(eye);
            if (screenSize > _renderedScreenSize * RedrawScreenGrowth)
                _dirty = true;

            // OVROverlayCanvas renders a dirty frame only once the canvas is in view, and sizes the
            // texture from where the player is then. Hold the request until then, or the size
            // recorded here would describe a render that happens later from somewhere else.
            if (_dirty && IsInView(eye))
            {
                _dirty = false;
                _renderedScreenSize = screenSize;
                _canvas.SetFrameDirty();
            }
        }

        // The canvas's world diagonal over its distance from the eye: proportional to its size on
        // screen, ignoring the viewing angle.
        private float ScreenSize(Camera eye)
        {
            var size = Vector2.Scale(_rect.rect.size, _rect.lossyScale);
            float distance = Vector3.Distance(eye.transform.position, _rect.position);
            return size.magnitude / Mathf.Max(distance, 0.01f);
        }

        private bool IsInView(Camera eye)
        {
            _rect.GetWorldCorners(s_Corners);
            var bounds = new Bounds(s_Corners[0], Vector3.zero);
            for (int i = 1; i < s_Corners.Length; i++)
                bounds.Encapsulate(s_Corners[i]);

            GeometryUtility.CalculateFrustumPlanes(eye, s_FrustumPlanes);
            return GeometryUtility.TestPlanesAABB(s_FrustumPlanes, bounds);
        }
    }
}
