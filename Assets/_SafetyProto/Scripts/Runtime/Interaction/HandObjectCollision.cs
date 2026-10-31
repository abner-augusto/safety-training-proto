using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Whether the physical hands (HandPhysicsCapsules on the HandPhysics layer) push interactive
    /// objects. The collision matrix lets HandPhysics touch only the interactive layers (Object,
    /// Lanyard, ScaffoldInteractive), so "pass through" turns those three pairs off and the hands no
    /// longer knock PPE off the table. The evaluator switches it live from the dashboard, so both
    /// can be compared on the headset in one build.
    /// </summary>
    public sealed class HandObjectCollision : MonoBehaviour
    {
        public enum Mode { Push, PassThrough }

        private static readonly string[] InteractiveLayers = { "Object", "Lanyard", "ScaffoldInteractive" };

        [SerializeField] private Mode startMode = Mode.Push;

        private int _handLayer = -1;
        private int[] _interactive;
        private bool[] _ignoredAtStart;

        public static HandObjectCollision Instance { get; private set; }

        public Mode Current { get; private set; }

        /// <summary>Wire token: "push" or "pass_through".</summary>
        public static string Token(Mode mode) => mode == Mode.PassThrough ? "pass_through" : "push";

        public static bool TryParse(string token, out Mode mode)
        {
            mode = Mode.Push;
            if (token == "push") return true;
            if (token != "pass_through") return false;
            mode = Mode.PassThrough;
            return true;
        }

        private void Awake()
        {
            Instance = this;
            _handLayer = LayerMask.NameToLayer("HandPhysics");
            _interactive = new int[InteractiveLayers.Length];
            _ignoredAtStart = new bool[InteractiveLayers.Length];
            for (int i = 0; i < InteractiveLayers.Length; i++)
            {
                _interactive[i] = LayerMask.NameToLayer(InteractiveLayers[i]);
                _ignoredAtStart[i] = _handLayer >= 0 && _interactive[i] >= 0
                    && Physics.GetIgnoreLayerCollision(_handLayer, _interactive[i]);
            }
            if (_handLayer < 0)
                SafetyLog.Warning("[HandObjectCollision] Layer HandPhysics ausente; a colisão das mãos não pode ser alternada.", this);
            Apply(startMode);
        }

        // The layer matrix is global and outlives the scene (and Play Mode in the Editor), so put
        // back what was there.
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_handLayer < 0) return;
            for (int i = 0; i < _interactive.Length; i++)
                if (_interactive[i] >= 0) Physics.IgnoreLayerCollision(_handLayer, _interactive[i], _ignoredAtStart[i]);
        }

        public void Apply(Mode mode)
        {
            Current = mode;
            if (_handLayer < 0) return;
            for (int i = 0; i < _interactive.Length; i++)
                if (_interactive[i] >= 0)
                    Physics.IgnoreLayerCollision(_handLayer, _interactive[i], mode == Mode.PassThrough || _ignoredAtStart[i]);
        }
    }
}
