using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Applies GPU-side performance settings that, on a Built-in + Meta OpenXR project,
    /// can only be set at runtime (there is no URP asset render-scale to edit, and the
    /// Meta XR Foveation OpenXR feature exposes no static level field).
    ///
    /// Baseline from plan 036's on-device F3 profiling (2026-09-07): the app holds
    /// ~68 fps at ~83% dynamic render scale, GPU/fill-rate bound, with fixed foveated
    /// rendering reported as level 3 (foveation_mode: 0). This caps the eye-texture
    /// resolution and turns foveation on.
    ///
    /// Place this on a bootstrap GameObject in the scene. Values are tunable in the Inspector;
    /// re-capture OVR Metrics on hardware after changing them.
    ///
    /// The values above are tuned on a Quest 3. A Quest 2 gets its own render scale and foveation
    /// from the "Quest 2" block: its GPU has roughly half the headroom, and fixed foveation there
    /// distorts and mirrors the image at the edges of the view while saving no measurable GPU time.
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

        [Tooltip("Foveation strength, 0 = off .. 1 = highest. ~0.66 (High) is a good default. " +
                 "On-device this maps to the OVR Metrics integer levels; 0.66 reported as level 3 " +
                 "in every plan 036 run.")]
        [Range(0f, 1f)]
        [SerializeField] private float foveationLevel = 0.66f;

        [Tooltip("Permits eye-tracked (gaze-contingent) foveated rendering on devices that support " +
                 "it, by setting XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed. This is not " +
                 "a load-adaptive control and does not change foveation strength (see " +
                 "foveationLevel). No effect on Quest 3, which has no eye tracking; all plan 036 " +
                 "runs report foveation_mode: 0 (fixed) regardless of this flag.")]
        [FormerlySerializedAs("dynamicFoveation")]
        [SerializeField] private bool allowGazeFoveation = true;

        [Header("Quest 2")]
        [Tooltip("Replace the render scale and foveation above with the values below when the app " +
                 "runs on a Quest 2.")]
        [SerializeField] private bool applyQuest2Profile = true;

        [Tooltip("eyeTextureResolutionScale on a Quest 2. It is the hard ceiling for OVRManager's dynamic " +
                 "resolution, which otherwise spends any GPU saving on more pixels instead of frame rate. " +
                 "At 0.8 the scaffold view measured ~9 ms of app GPU time, inside the 13.9 ms budget.")]
        [Range(0.5f, 1.5f)]
        [SerializeField] private float quest2EyeTextureResolutionScale = 0.8f;

        [Tooltip("Fixed foveated rendering on a Quest 2. Off: on-device it produced a mirrored, " +
                 "blocky periphery, and at the 0.8 render scale it raised app GPU time (12.3 vs 9.2 ms).")]
        [SerializeField] private bool quest2FoveatedRendering = false;

        [Tooltip("OVROverlayCanvas.superSample on a Quest 2. Dynamic resolution already renders each " +
                 "canvas at twice the eye-buffer density, so super-sampling on top quadruples it; off, " +
                 "the canvas textures hold a quarter of the pixels.")]
        [SerializeField] private bool quest2CanvasSuperSample = false;

        [Tooltip("Keep the Standard (PBR) shader on the PPE materials below on a Quest 2. Off, they " +
                 "fall back to SafetyProto/SimpleLit there; other headsets always keep Standard.")]
        [SerializeField] private bool quest2PpePbr = true;

        [Tooltip("PPE materials authored with the Standard shader. Held close to the eyes, they are the " +
                 "scene's hero assets.")]
        [SerializeField] private Material[] ppePbrMaterials = System.Array.Empty<Material>();

        private bool? _canvasSuperSample;
        private bool? _ppePbr;

        // Launch overrides only: there is no device profile for them.
        private bool? _metalReflections;
        private bool? _canvasRedrawOnChange;

        [Header("Hand debug visuals")]
        [Tooltip("Disable the Meta Interaction SDK HandSphereMap debug spheres (one Standard-shader " +
                 "MeshRenderer per finger joint on both hands, ~144 total). They are pure grab-debug " +
                 "visuals — hiding them removes ~144 draw calls, ~110k tris and their Standard-shader " +
                 "fill-rate cost every frame with no gameplay effect. Confirmed on-device: the scene " +
                 "is fill-rate bound, not triangle bound.")]
        [SerializeField] private bool hideHandDebugSpheres = true;

        private static readonly List<XRDisplaySubsystem> s_Displays = new();

        private void Start()
        {
            if (applyQuest2Profile && IsQuest2(ReadAndroidBuildDevice(), SystemInfo.deviceModel))
            {
                eyeTextureResolutionScale = quest2EyeTextureResolutionScale;
                enableFoveatedRendering = quest2FoveatedRendering;
                _canvasSuperSample = quest2CanvasSuperSample;
                _ppePbr = quest2PpePbr;
                SafetyLog.Info("[XrPerformanceBootstrap] Quest 2 detectado: aplicando o perfil Quest 2.", this);
            }

            ApplyLaunchOverrides();
            ApplyRenderScale();
            ApplyCanvasSuperSample();
            ApplyMetalReflections();
            ApplyPpePbr();
            ApplyCanvasRedrawOnChange();
            HideHandDebugSpheres();
            // The XR display subsystem may not be running on the first frame; retry.
            StartCoroutine(ApplyFoveationWhenReady());
        }

        /// <summary>True for a Quest 2, identified by its Android device codename or model name.</summary>
        public static bool IsQuest2(string androidDevice, string deviceModel)
        {
            if (string.Equals(androidDevice, "hollywood", System.StringComparison.OrdinalIgnoreCase))
                return true;
            return deviceModel != null &&
                   deviceModel.IndexOf("Quest 2", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadAndroidBuildDevice()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var build = new AndroidJavaClass("android.os.Build");
            return build.GetStatic<string>("DEVICE");
#else
            return null;
#endif
        }

        /// <summary>
        /// Development builds read render-scale and foveation overrides from the launch intent, so
        /// a setting can be compared on the headset without a rebuild:
        /// <c>am start -n &lt;pkg&gt;/&lt;activity&gt; --es safety_perf_scale 0.9 --es safety_perf_viewport 1
        /// --es safety_perf_foveation 0.66 --es safety_perf_canvas_ss 0 --es safety_perf_metal_reflections 0
        /// --es safety_perf_ppe_pbr 0 --es safety_perf_canvas_redraw 0</c>.
        /// A foveation of 0 turns it off; a viewport of 0 or 1 leaves the runtime's dynamic viewport
        /// alone; canvas_ss 0 or 1 sets OVROverlayCanvas super-sampling on every canvas;
        /// metal_reflections 0 turns off SimpleLit's reflection-probe path on the materials using it;
        /// ppe_pbr 0 shades the PPE with SimpleLit instead of Standard; canvas_redraw 0 puts the
        /// change-driven canvases back on their render interval.
        /// </summary>
        private void ApplyLaunchOverrides()
        {
#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (TryReadFloatExtra("safety_perf_scale", out float scale))
                eyeTextureResolutionScale = scale;
            if (TryReadFloatExtra("safety_perf_viewport", out float viewport))
                fixedRenderViewportScale = viewport >= 1f ? 0f : viewport;
            if (TryReadFloatExtra("safety_perf_foveation", out float foveation))
            {
                enableFoveatedRendering = foveation > 0f;
                foveationLevel = foveation;
            }
            if (TryReadFloatExtra("safety_perf_canvas_ss", out float canvasSuperSample))
                _canvasSuperSample = canvasSuperSample > 0f;
            if (TryReadFloatExtra("safety_perf_metal_reflections", out float metalReflections))
                _metalReflections = metalReflections > 0f;
            if (TryReadFloatExtra("safety_perf_ppe_pbr", out float ppePbr))
                _ppePbr = ppePbr > 0f;
            if (TryReadFloatExtra("safety_perf_canvas_redraw", out float canvasRedraw))
                _canvasRedrawOnChange = canvasRedraw > 0f;
#endif
        }

        /// <summary>Overrides super-sampling on every overlay canvas, inactive ones included, when a
        /// device profile or launch override asks for it. OVROverlayCanvas reads the flag each time
        /// it sizes its texture, so setting it after the canvas started still takes effect.</summary>
        private void ApplyCanvasSuperSample()
        {
            if (_canvasSuperSample is not bool superSample)
                return;

            var canvases = Object.FindObjectsByType<OVROverlayCanvas>(FindObjectsInactive.Include);
            foreach (var canvas in canvases)
                canvas.superSample = superSample;

            SafetyLog.Info(
                $"[XrPerformanceBootstrap] Super-sampling dos canvases de overlay: {superSample} ({canvases.Length} canvases).",
                this);
        }

        private void ApplyMetalReflections()
        {
            if (_metalReflections is not bool on)
                return;

            const string keyword = "_SIMPLELIT_REFLECTIONS";
            var materials = new HashSet<Material>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            foreach (var material in renderer.sharedMaterials)
            {
                bool usesReflections = material != null && material.HasProperty("_Reflections") &&
                                       material.GetFloat("_Reflections") > 0f;
                if (usesReflections && materials.Add(material))
                {
                    if (on) material.EnableKeyword(keyword);
                    else material.DisableKeyword(keyword);
                }
            }

            SafetyLog.Info(
                $"[XrPerformanceBootstrap] Reflexos metálicos do SimpleLit: {on} ({materials.Count} materiais).",
                this);
        }

        private void ApplyPpePbr()
        {
            if (_ppePbr is not false)
                return;

            // Changes the shared material, so every PPE instance follows. Not reachable in the
            // Editor, where it would rewrite the material asset.
            var simpleLit = Shader.Find("SafetyProto/SimpleLit");
            if (simpleLit == null)
            {
                SafetyLog.Error("[XrPerformanceBootstrap] Shader SafetyProto/SimpleLit não encontrado; EPIs mantêm o Standard.", this);
                return;
            }

            int swapped = 0;
            foreach (var material in ppePbrMaterials)
            {
                if (material == null) continue;
                material.shader = simpleLit;
                swapped++;
            }

            SafetyLog.Info($"[XrPerformanceBootstrap] EPIs com SimpleLit em vez de Standard ({swapped} materiais).", this);
        }

        private void ApplyCanvasRedrawOnChange()
        {
            if (_canvasRedrawOnChange is not bool on)
                return;

            OverlayCanvasRedrawOnChange.SetEnabledForAll(on);
            SafetyLog.Info($"[XrPerformanceBootstrap] Redesenho dos canvases só quando mudam: {on}.", this);
        }

#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
        private bool TryReadFloatExtra(string key, out float value)
        {
            value = 0f;
            string raw;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity?.Call<AndroidJavaObject>("getIntent");
                raw = intent?.Call<string>("getStringExtra", key);
            }
            catch (System.Exception ex)
            {
                SafetyLog.Error($"[XrPerformanceBootstrap] Não foi possível ler o extra '{key}': {ex.Message}", this);
                return false;
            }

            if (string.IsNullOrEmpty(raw))
                return false;
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                SafetyLog.Warning($"[XrPerformanceBootstrap] Extra '{key}' ignorado: '{raw}' não é um número.", this);
                return false;
            }

            SafetyLog.Info($"[XrPerformanceBootstrap] Override de lançamento: {key}={value:0.00}", this);
            return true;
        }
#endif

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

        private void HideHandDebugSpheres()
        {
            if (!hideHandDebugSpheres)
                return;

            // Meta's hand branches stay inactive until the hand is tracked, so include inactive objects.
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            int disabled = 0;
            foreach (var mr in renderers)
            {
                if (!mr.enabled)
                    continue;
                if (!mr.gameObject.name.StartsWith("sphere", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!HasAncestorNamed(mr.transform, "HandSphereMap"))
                    continue;

                mr.enabled = false;
                disabled++;
            }

            SafetyLog.Info(
                $"[XrPerformanceBootstrap] {disabled} esferas de debug da mão (HandSphereMap) desativadas.",
                this);
        }

        private static bool HasAncestorNamed(Transform t, string namePart)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (p.name.Contains(namePart))
                    return true;
            }
            return false;
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
                display.foveatedRenderingFlags = allowGazeFoveation
                    ? XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed
                    : XRDisplaySubsystem.FoveatedRenderingFlags.None;
                appliedAny = true;
            }

            if (appliedAny)
            {
                SafetyLog.Info(
                    $"[XrPerformanceBootstrap] Foveation aplicada: nível={foveationLevel:0.00}, gazeAllowed={allowGazeFoveation}.",
                    this);
            }

            return appliedAny;
        }
    }
}
