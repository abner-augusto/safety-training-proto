using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Turns baked lights on at runtime by swapping between two bakes of the scene: the scene's
    /// own lightmaps and probes (lights on) and a copy baked with those lights disabled (lights
    /// off). Baked lights cost nothing per frame, but enabling one at runtime shows nothing, so
    /// the "lights come on" moment has to be a lightmap swap.
    ///
    /// The off set is produced by <c>SafetyProto -> Bake Lighting States</c>; rebake through that
    /// menu so both sets keep the same lightmap layout.
    /// </summary>
    public class BakedLightingSwitch : MonoBehaviour
    {
        [Tooltip("The baked lights this switch turns on. The bake menu disables them for the lights-off bake; " +
                 "at runtime their enabled state changes nothing.")]
        [SerializeField] private Light[] switchedLights = new Light[0];

        [Tooltip("Lightmap colour textures baked with the lights off, in the scene's lightmap order.")]
        [SerializeField] private Texture2D[] offLightmapColors = new Texture2D[0];

        [Tooltip("Directional lightmaps of the lights-off bake, same order as the colours.")]
        [SerializeField] private Texture2D[] offLightmapDirs = new Texture2D[0];

        [Tooltip("Light probes of the lights-off bake, so dynamic objects stay dark with the room.")]
        [SerializeField] private LightProbes offLightProbes;

        [Tooltip("Start the scene with the lights off.")]
        [SerializeField] private bool startOff = true;

        private LightmapData[] _onLightmaps;
        private LightmapData[] _offLightmaps;
        private LightProbes _onLightProbes;

        public bool IsOn { get; private set; } = true;

        public Light[] SwitchedLights => switchedLights;

        private void Awake()
        {
            _onLightmaps = LightmapSettings.lightmaps;
            _onLightProbes = LightmapSettings.lightProbes;

            if (offLightmapColors.Length != _onLightmaps.Length)
            {
                SafetyLog.Warning(
                    $"[BakedLightingSwitch] O conjunto de luzes apagadas tem {offLightmapColors.Length} lightmaps " +
                    $"e a cena tem {_onLightmaps.Length}; as luzes ficam acesas. Rebake em SafetyProto -> Bake Lighting States.",
                    this);
                return;
            }

            _offLightmaps = new LightmapData[_onLightmaps.Length];
            for (int i = 0; i < _onLightmaps.Length; i++)
            {
                _offLightmaps[i] = new LightmapData
                {
                    lightmapColor = offLightmapColors[i],
                    lightmapDir = i < offLightmapDirs.Length ? offLightmapDirs[i] : null,
                    shadowMask = _onLightmaps[i].shadowMask,
                };
            }

            if (startOff)
                TurnOff();
        }

        public void TurnOn()
        {
            // An auto-skipped onboarding finishes in its OnEnable, which can run before this Awake;
            // clearing startOff keeps that early call from being undone.
            startOff = false;
            Apply(true);
        }

        public void TurnOff() => Apply(false);

        private void Apply(bool on)
        {
            if (_offLightmaps == null || IsOn == on)
                return;

            LightmapSettings.lightmaps = on ? _onLightmaps : _offLightmaps;
            if (offLightProbes != null)
                LightmapSettings.lightProbes = on ? _onLightProbes : offLightProbes;
            IsOn = on;
        }
    }
}
