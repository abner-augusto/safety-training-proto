using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.XR;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Applies GPU-side performance settings that, on a Built-in + Meta OpenXR project,
    /// can only be set at runtime (there is no URP asset render-scale to edit, and the
    /// Meta XR Foveation OpenXR feature exposes no static level field).
    ///
    /// Diagnosed from on-device OVR Metrics (25/06/2026): the app is GPU/fill-rate bound,
    /// ~40 fps on a 72 Hz display, with the eye buffer supersampling up to render_scale 160%
    /// (2688x2816/eye) and foveation reported as level 0 (off). This caps the eye-texture
    /// resolution and turns foveation on.
    ///
    /// Place this on a bootstrap GameObject in the scene. Values are tunable in the Inspector;
    /// re-capture OVR Metrics on hardware after changing them.
    /// </summary>
    public class XrPerformanceBootstrap : MonoBehaviour
    {
        [Header("Render scale")]
        [Tooltip("Multiplier applied to the OpenXR recommended eye-texture resolution. " +
                 "1.0 = native recommended; below 1.0 trades sharpness for GPU headroom. " +
                 "Caps the supersampling overshoot seen in profiling (render_scale 160%).")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float eyeTextureResolutionScale = 1.0f;

        [Tooltip("If > 0, pins the dynamic viewport scale to this value so the runtime can't " +
                 "scale the eye buffer up past the cap. Leave at 0 to let the runtime manage it.")]
        [Range(0f, 1f)]
        [SerializeField] private float fixedRenderViewportScale = 0f;

        [Header("Foveated rendering")]
        [Tooltip("Enable fixed foveated rendering (lowers shading cost at the periphery). " +
                 "Big win for fill-rate-bound scenes.")]
        [SerializeField] private bool enableFoveatedRendering = true;

        [Tooltip("Foveation strength, 0 = off .. 1 = highest. ~0.66 (High) is a good default.")]
        [Range(0f, 1f)]
        [SerializeField] private float foveationLevel = 0.66f;

        [Tooltip("Let the runtime raise/lower the foveation level dynamically with GPU load.")]
        [SerializeField] private bool dynamicFoveation = true;

        private static readonly List<XRDisplaySubsystem> s_Displays = new();

        private void Start()
        {
            ApplyRenderScale();
            // Foveation needs the XR display subsystem running; it may not be ready on the
            // very first frame, so retry for a few frames until at least one display takes it.
            StartCoroutine(ApplyFoveationWhenReady());
        }

        private void ApplyRenderScale()
        {
            XRSettings.eyeTextureResolutionScale = eyeTextureResolutionScale;
            if (fixedRenderViewportScale > 0f)
                XRSettings.renderViewportScale = fixedRenderViewportScale;

            SafetyLog.Info(
                $"[XrPerformanceBootstrap] Render scale aplicado: eyeTextureResolutionScale={eyeTextureResolutionScale:0.00}" +
                (fixedRenderViewportScale > 0f ? $", renderViewportScale fixo={fixedRenderViewportScale:0.00}" : ""),
                this);
        }

        private IEnumerator ApplyFoveationWhenReady()
        {
            if (!enableFoveatedRendering)
            {
                SafetyLog.Info("[XrPerformanceBootstrap] Foveation desabilitada via Inspector.", this);
                yield break;
            }

            for (int attempt = 0; attempt < 30; attempt++)
            {
                if (TryApplyFoveation())
                    yield break;
                yield return null;
            }

            SafetyLog.Warning(
                "[XrPerformanceBootstrap] Nenhum XRDisplaySubsystem aceitou a configuração de foveation " +
                "após 30 frames. Verifique no headset se a feature Meta XR Foveation está ativa.",
                this);
        }

        private bool TryApplyFoveation()
        {
            SubsystemManager.GetSubsystems(s_Displays);
            if (s_Displays.Count == 0)
                return false;

            bool appliedAny = false;
            foreach (var display in s_Displays)
            {
                if (display == null || !display.running)
                    continue;

                display.foveatedRenderingLevel = foveationLevel;
                display.foveatedRenderingFlags = dynamicFoveation
                    ? XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed
                    : XRDisplaySubsystem.FoveatedRenderingFlags.None;
                appliedAny = true;
            }

            if (appliedAny)
            {
                SafetyLog.Info(
                    $"[XrPerformanceBootstrap] Foveation aplicada: nível={foveationLevel:0.00}, dinâmica={dynamicFoveation}.",
                    this);
            }

            return appliedAny;
        }
    }
}
