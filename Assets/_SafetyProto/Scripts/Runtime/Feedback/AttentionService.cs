using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Scaffolding;
using SafetyProto.Runtime.Task;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// Finds every interactive thing in the scene that can be made to pulse (PPE, decoys, body
    /// slots, scaffold pieces and their sockets, the correct anchor, the lanyard, action objects,
    /// tools) and drives its <see cref="AttentionPulse"/>. The evaluator's "Chamar atenção"
    /// command, the onboarding highlight and the guided-mode hints all go through it. Purely
    /// visual and sonic: nothing it does reaches tasks or the score.
    /// </summary>
    public sealed class AttentionService : MonoBehaviour
    {
        [Tooltip("The SafetyProto/AttentionPulse shader. Referenced here so builds include it.")]
        [SerializeField] private Shader pulseShader;

        [Tooltip("Short positional ping played at the target while an on-demand cue lasts.")]
        [SerializeField] private AudioClip pingClip;
        [SerializeField, Range(0f, 1f)] private float pingVolume = 0.8f;
        [SerializeField, Range(0.5f, 5f)] private float pingIntervalSeconds = 1.5f;

        private sealed class Entry
        {
            public AttentionTargetInfo Info;
            public GameObject Host;
            public Renderer[] Renderers;
            public Vector3? Marker;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<AttentionTargetInfo> _infos = new List<AttentionTargetInfo>();
        private Material _overlay;
        private bool _scanned;

        public static AttentionService Instance { get; private set; }

        /// <summary>Every target found in the loaded scene, in discovery order.</summary>
        public IReadOnlyList<AttentionTargetInfo> Targets
        {
            get
            {
                EnsureScanned();
                return _infos;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                SafetyLog.Warning("[AttentionService] Mais de um AttentionService na cena; o segundo foi ignorado.", this);
                return;
            }
            Instance = this;
            if (pulseShader == null) pulseShader = Shader.Find("SafetyProto/AttentionPulse");
            if (pulseShader == null)
                SafetyLog.Error("[AttentionService] Shader SafetyProto/AttentionPulse ausente; os destaques não aparecem.", this);
            else
                _overlay = new Material(pulseShader) { name = "AttentionPulse (runtime)" };
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_overlay != null) Destroy(_overlay);
        }

        /// <summary>Makes the target with <paramref name="id"/> pulse for <paramref name="seconds"/>.</summary>
        public bool TryFlash(string id, float seconds, bool withSound, out AttentionTargetInfo info)
        {
            info = null;
            var entry = Find(id);
            if (entry == null || entry.Host == null) return false;
            info = entry.Info;
            PulseOf(entry).Flash(seconds, withSound);
            return true;
        }

        /// <summary>Targets of <paramref name="kind"/> tied to <paramref name="key"/> (a PPE type name, an action id).</summary>
        public IEnumerable<AttentionTargetInfo> FindByKey(AttentionTargetKind kind, string key)
        {
            EnsureScanned();
            foreach (var entry in _entries)
                if (entry.Info.Kind == kind && string.Equals(entry.Info.Key, key, System.StringComparison.Ordinal))
                    yield return entry.Info;
        }

        /// <summary>The scene object behind a target id, or null.</summary>
        public GameObject HostOf(string id) => Find(id)?.Host;

        /// <summary>Keeps <paramref name="target"/> pulsing until <see cref="Release"/>; used by the onboarding.</summary>
        public void Hold(GameObject target) => PulseOn(target)?.Hold();

        public void Release(GameObject target)
        {
            if (target != null && target.TryGetComponent(out AttentionPulse pulse)) pulse.Release();
        }

        /// <summary>Makes any scene object pulse, whether or not it is a listed target.</summary>
        public void Flash(GameObject target, float seconds, bool withSound) => PulseOn(target)?.Flash(seconds, withSound);

        private AttentionPulse PulseOn(GameObject target)
        {
            if (target == null) return null;
            var entry = FindByHost(target) ?? new Entry
            {
                Host = target,
                Renderers = target.GetComponentsInChildren<Renderer>(),
                Marker = target.transform.position
            };
            return PulseOf(entry);
        }

        private AttentionPulse PulseOf(Entry entry)
        {
            var pulse = AttentionPulse.On(entry.Host);
            pulse.Setup(_overlay, entry.Renderers, entry.Marker, pingClip, pingVolume, pingIntervalSeconds);
            return pulse;
        }

        private Entry Find(string id)
        {
            EnsureScanned();
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var entry in _entries)
                if (string.Equals(entry.Info.Id, id.Trim(), System.StringComparison.Ordinal)) return entry;
            return null;
        }

        private Entry FindByHost(GameObject host)
        {
            EnsureScanned();
            foreach (var entry in _entries)
                if (entry.Host == host) return entry;
            return null;
        }

        private void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;

            var groups = TaskManager.Instance != null ? TaskManager.Instance.RuntimeGroups : null;
            var claimed = new HashSet<GameObject>();

            foreach (var item in FindObjectsByType<PPEItem>())
            {
                if (!claimed.Add(item.gameObject)) continue;
                var info = item.isDistractor
                    ? AttentionTargets.ForDistractor(item.name, DistractorName(groups, item.distractorId), item.distractorId)
                    : AttentionTargets.ForPpe(item.name, item.ppeType);
                Add(info, item.gameObject);
            }

            foreach (var slot in FindObjectsByType<PPESnapSlot>())
                if (claimed.Add(slot.gameObject))
                    Add(AttentionTargets.ForBodySlot(slot.name, slot.AcceptedTypes), slot.gameObject);

            foreach (var piece in FindObjectsByType<ScaffoldPieceInstaller>())
            {
                if (!claimed.Add(piece.gameObject)) continue;
                string task = TaskNameFor(groups, piece.ActionId);
                Add(AttentionTargets.ForScaffoldPiece(piece.name, piece.ActionId, task), piece.gameObject);
                var preview = piece.TargetPreviewRenderer;
                if (preview != null && claimed.Add(preview.gameObject))
                    Add(AttentionTargets.ForScaffoldSocket(piece.name, piece.ActionId, task), preview.gameObject,
                        new[] { preview });
            }

            foreach (var anchor in FindObjectsByType<AnchorPoint>())
                if (anchor.isCorrectAnchor && claimed.Add(anchor.gameObject))
                    Add(AttentionTargets.ForAnchor(anchor.name), anchor.gameObject, null, anchor.AttachPosition);

            foreach (var lanyard in FindObjectsByType<RetractableLanyardController>())
                if (claimed.Add(lanyard.gameObject))
                    Add(AttentionTargets.ForLanyard(lanyard.name), lanyard.gameObject);

            foreach (var trigger in FindObjectsByType<SimpleActionTrigger>())
                if (claimed.Add(trigger.gameObject))
                    Add(AttentionTargets.ForActionObject(trigger.name, trigger.ActionId, TaskNameFor(groups, trigger.ActionId)),
                        trigger.gameObject);

            foreach (var home in FindObjectsByType<ReturnObjectHome>())
                if (claimed.Add(home.gameObject))
                    Add(AttentionTargets.ForTool(home.name), home.gameObject);
        }

        private void Add(AttentionTargetInfo info, GameObject host, Renderer[] renderers = null, Vector3? marker = null)
        {
            _entries.Add(new Entry
            {
                Info = info,
                Host = host,
                Renderers = renderers ?? host.GetComponentsInChildren<Renderer>(),
                Marker = marker ?? host.transform.position
            });
            _infos.Add(info);
        }

        private static string DistractorName(IReadOnlyList<ITaskGroup> groups, string distractorId)
            => SafetyProto.Domain.Safety.DistractorResolver.Resolve(groups, distractorId)?.Distractor.Name;

        private static string TaskNameFor(IReadOnlyList<ITaskGroup> groups, string actionId)
        {
            if (groups == null || string.IsNullOrWhiteSpace(actionId)) return null;
            foreach (var group in groups)
                foreach (var task in group.tasks)
                    if (string.Equals(task.ResolveExpectedActionId(), actionId, System.StringComparison.Ordinal))
                        return task.taskName;
            return null;
        }
    }
}
