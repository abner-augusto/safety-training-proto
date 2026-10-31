using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// The attention glow on one target. Each mesh renderer gets a hidden child copy drawn with the
    /// <c>SafetyProto/AttentionPulse</c> overlay, so the cue works whatever the object's own shader
    /// is; a skinned renderer gets the overlay appended to its materials while it shows; a target
    /// with no renderer (an anchor point) gets a small marker sphere. Added at runtime by
    /// <see cref="AttentionService"/>.
    /// </summary>
    public sealed class AttentionPulse : MonoBehaviour
    {
        private static readonly int PulseAmountId = Shader.PropertyToID("_PulseAmount");
        private const float FadeSeconds = 0.25f;
        private const float MarkerDiameter = 0.12f;

        private readonly List<Renderer> _shells = new List<Renderer>();
        private readonly List<SkinnedMeshRenderer> _skinned = new List<SkinnedMeshRenderer>();
        private readonly List<Material[]> _skinnedOriginals = new List<Material[]>();
        private MaterialPropertyBlock _mpb;
        private Material _overlay;
        private AudioSource _pingSource;
        private AudioClip _pingClip;
        private float _pingVolume;
        private float _pingInterval;
        private float _nextPingAt;
        private bool _built;
        private bool _skinnedApplied;
        private int _holds;
        private float _flashUntil;
        private float _pingUntil;
        private float _amount;

        /// <summary>Whether the glow is showing or fading out.</summary>
        public bool IsShowing => _amount > 0f || IsWanted;

        private bool IsWanted => _holds > 0 || Time.time < _flashUntil;

        /// <summary>The pulse on <paramref name="target"/>, created on first use.</summary>
        public static AttentionPulse On(GameObject target)
        {
            if (target == null) return null;
            return target.TryGetComponent(out AttentionPulse pulse) ? pulse : target.AddComponent<AttentionPulse>();
        }

        /// <summary>Sets what the glow draws with; the first call builds the overlay copies.</summary>
        public void Setup(Material overlay, IReadOnlyList<Renderer> renderers, Vector3? markerPosition,
                          AudioClip pingClip, float pingVolume, float pingInterval)
        {
            _overlay = overlay;
            _pingClip = pingClip;
            _pingVolume = pingVolume;
            _pingInterval = Mathf.Max(0.5f, pingInterval);
            if (_built || overlay == null) return;
            _built = true;
            _mpb = new MaterialPropertyBlock();

            if (renderers != null)
                foreach (var source in renderers)
                    AddShell(source);

            if (_shells.Count == 0 && _skinned.Count == 0 && markerPosition.HasValue)
                AddMarker(markerPosition.Value);

            Apply(0f);
        }

        /// <summary>Shows the glow for <paramref name="seconds"/>, extending a shorter one; with
        /// <paramref name="withSound"/> a positional ping repeats for the same time.</summary>
        public void Flash(float seconds, bool withSound)
        {
            float until = Time.time + Mathf.Max(0f, seconds);
            if (until > _flashUntil) _flashUntil = until;
            if (withSound && _pingClip != null)
            {
                if (Time.time >= _pingUntil) _nextPingAt = Time.time;
                if (until > _pingUntil) _pingUntil = until;
            }
            enabled = true;
        }

        /// <summary>Keeps the glow on until a matching <see cref="Release"/>.</summary>
        public void Hold()
        {
            _holds++;
            enabled = true;
        }

        public void Release()
        {
            if (_holds > 0) _holds--;
        }

        /// <summary>Ends every flash and hold at once.</summary>
        public void StopAll()
        {
            _holds = 0;
            _flashUntil = 0f;
            _pingUntil = 0f;
        }

        private void Update()
        {
            float target = IsWanted ? 1f : 0f;
            float next = Mathf.MoveTowards(_amount, target, Time.deltaTime / FadeSeconds);
            if (!Mathf.Approximately(next, _amount)) Apply(next);

            if (_pingClip != null && Time.time < _pingUntil && Time.time >= _nextPingAt)
            {
                _nextPingAt = Time.time + _pingInterval;
                PlayPing();
            }

            if (_amount <= 0f && !IsWanted) enabled = false;
        }

        private void OnDisable()
        {
            if (_amount > 0f) Apply(0f);
        }

        private void OnDestroy()
        {
            RestoreSkinned();
        }

        private void AddShell(Renderer source)
        {
            if (source == null) return;

            if (source is SkinnedMeshRenderer skinned)
            {
                _skinned.Add(skinned);
                _skinnedOriginals.Add(skinned.sharedMaterials);
                return;
            }

            var filter = source.GetComponent<MeshFilter>();
            if (!(source is MeshRenderer) || filter == null || filter.sharedMesh == null) return;

            var shell = new GameObject("AttentionShell") { layer = source.gameObject.layer };
            shell.transform.SetParent(source.transform, false);
            shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = shell.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = _overlay;
            renderer.sharedMaterials = materials;
            ConfigureShell(renderer);
            _shells.Add(renderer);
        }

        private void AddMarker(Vector3 position)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "AttentionMarker";
            Destroy(marker.GetComponent<Collider>());
            marker.layer = gameObject.layer;
            marker.transform.SetParent(transform, true);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * MarkerDiameter / Mathf.Max(1e-4f, transform.lossyScale.x);
            var renderer = marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _overlay;
            ConfigureShell(renderer);
            _shells.Add(renderer);
        }

        private static void ConfigureShell(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
        }

        private void Apply(float amount)
        {
            _amount = amount;
            bool visible = amount > 0f;

            foreach (var shell in _shells)
            {
                if (shell == null) continue;
                shell.enabled = visible;
                if (!visible) continue;
                shell.GetPropertyBlock(_mpb);
                _mpb.SetFloat(PulseAmountId, amount);
                shell.SetPropertyBlock(_mpb);
            }

            if (visible != _skinnedApplied)
            {
                if (visible) AppendSkinned();
                else RestoreSkinned();
            }

            foreach (var skinned in _skinned)
            {
                if (skinned == null || !visible) continue;
                skinned.GetPropertyBlock(_mpb, skinned.sharedMaterials.Length - 1);
                _mpb.SetFloat(PulseAmountId, amount);
                skinned.SetPropertyBlock(_mpb, skinned.sharedMaterials.Length - 1);
            }
        }

        private void AppendSkinned()
        {
            _skinnedApplied = true;
            for (int i = 0; i < _skinned.Count; i++)
            {
                if (_skinned[i] == null) continue;
                var original = _skinnedOriginals[i];
                var withOverlay = new Material[original.Length + 1];
                original.CopyTo(withOverlay, 0);
                withOverlay[original.Length] = _overlay;
                _skinned[i].sharedMaterials = withOverlay;
            }
        }

        private void RestoreSkinned()
        {
            if (!_skinnedApplied) return;
            _skinnedApplied = false;
            for (int i = 0; i < _skinned.Count; i++)
                if (_skinned[i] != null) _skinned[i].sharedMaterials = _skinnedOriginals[i];
        }

        private void PlayPing()
        {
            if (_pingSource == null)
            {
                var host = new GameObject("AttentionPing");
                host.transform.SetParent(transform, false);
                _pingSource = AudioSourceDefaults.EnsureSpatial(host.transform, null, 0.5f, 10f);
            }
            _pingSource.PlayOneShot(_pingClip, _pingVolume);
        }
    }
}
