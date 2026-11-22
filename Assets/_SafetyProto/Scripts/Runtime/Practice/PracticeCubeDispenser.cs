using System.Collections;
using System.Collections.Generic;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime.Practice
{
    /// <summary>
    /// Keeps a practice cube ready on the pedestal: once the participant takes the cube away, a
    /// new one materializes in its place, up to <see cref="maxCubes"/> in the room at once. A spare
    /// cube left loose (let go or pushed away from a slot) falls for a moment, then dissolves and frees its place, so two
    /// cubes never head back to the same spot. Each area reset leaves a single cube, on the pedestal.
    /// </summary>
    public sealed class PracticeCubeDispenser : MonoBehaviour, IPracticeResettable
    {
        private enum CubeState { Pooled, Materializing, Live, Falling, Dissolving }

        private sealed class Cube
        {
            public PracticeSnapItem Item;
            public ReturnObjectHome Home;
            public Rigidbody Body;
            public Renderer[] Renderers;
            public Color[] BaseColors;
            public CubeState State;
            public Transform Transform => Item.transform;
        }

        [Tooltip("The scene's practice cube on the pedestal; the spares are copies of it, made when the area first opens.")]
        [SerializeField] private PracticeSnapItem cube;
        [Tooltip("Most cubes in the room at once, the one on the pedestal included.")]
        [SerializeField, Range(1, 10)] private int maxCubes = 5;
        [Tooltip("How far the ready cube must leave the pedestal before the next one appears.")]
        [SerializeField, Min(0.02f)] private float leaveDistance = 0.12f;
        [SerializeField, Min(0f)] private float spawnDelaySeconds = 0.35f;
        [SerializeField, Min(0.05f)] private float materializeSeconds = 0.55f;
        [SerializeField, Min(0.05f)] private float dissolveSeconds = 0.3f;
        [Tooltip("How long a cube let go away from a slot falls under gravity before it dissolves; " +
                 "grabbing it again meanwhile keeps it.")]
        [SerializeField, Min(0f)] private float fallSeconds = 2f;
        [Tooltip("Tint the cube starts from as it materializes and fades to as it dissolves.")]
        [SerializeField] private Color glowColor = new Color(0.8f, 0.97f, 1f, 1f);

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip materializeSound;
        [SerializeField] private AudioClip dissolveSound;
        [SerializeField, Range(0f, 1f)] private float audioVolume = 0.6f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<Cube> _cubes = new List<Cube>();
        private MaterialPropertyBlock _block;
        private Cube _ready;
        private float _emptySince;

        private int LiveCount
        {
            get
            {
                int n = 0;
                foreach (var c in _cubes) if (c.State != CubeState.Pooled) n++;
                return n;
            }
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (cube == null) return;

            // Copies are made now, while the scene's cube is untouched: a copy of a cube that is
            // worn or locked on a slot would inherit its disabled grab and kinematic body.
            _cubes.Add(Wrap(cube));
            for (int i = 1; i < maxCubes; i++)
            {
                var copy = Instantiate(cube.gameObject, transform.position, transform.rotation, cube.transform.parent);
                copy.name = $"{cube.name} ({i})";
                var wrapped = Wrap(copy.GetComponent<PracticeSnapItem>());
                copy.SetActive(false);
                _cubes.Add(wrapped);
            }
            _cubes[0].State = CubeState.Live;
            _ready = _cubes[0];
        }

        private Cube Wrap(PracticeSnapItem item)
        {
            item.Dispenser = this;
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            var colors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                colors[i] = material != null && material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
            }
            return new Cube
            {
                Item = item,
                Home = item.GetComponent<ReturnObjectHome>(),
                Body = item.GetComponent<Rigidbody>(),
                Renderers = renderers,
                BaseColors = colors,
                State = CubeState.Pooled
            };
        }

        public void ResetForPractice()
        {
            if (_cubes.Count == 0) return;
            StopAllCoroutines();
            foreach (var c in _cubes)
            {
                c.Item.ResetForPractice();
                Settle(c);
                c.Item.gameObject.SetActive(false);
                c.State = CubeState.Pooled;
            }

            var first = _cubes[0];
            first.Transform.SetPositionAndRotation(transform.position, transform.rotation);
            first.Item.gameObject.SetActive(true);
            first.State = CubeState.Live;
            if (first.Home != null) first.Home.SetHomeToCurrentPose();
            _ready = first;
        }

        /// <summary>
        /// A return home is due for <paramref name="item"/> (let go, knocked off, recalled). Only
        /// the ready cube nudged on its pedestal travels home; <see cref="Update"/> decides what
        /// happens to every other cube, whichever of the two runs first in a frame.
        /// </summary>
        public bool TakeOverReturn(PracticeSnapItem item)
        {
            var c = Find(item);
            if (c == null) return false;
            return !(c == _ready && !HasLeft(c));
        }

        private Cube Find(PracticeSnapItem item)
        {
            foreach (var c in _cubes) if (c.Item == item) return c;
            return null;
        }

        private void Update()
        {
            if (_cubes.Count == 0) return;

            if (_ready != null && _ready.State == CubeState.Live && HasLeft(_ready))
            {
                _ready = null;
                _emptySince = Time.time;
            }

            // A spare cube nobody holds or wears falls, then dissolves, whether a hand let it go
            // or a hand pushed it; one put back on an empty pedestal becomes the ready cube.
            foreach (var c in _cubes)
            {
                if (c.State != CubeState.Live || c == _ready || IsHeldOrWorn(c)) continue;
                if (_ready == null && !HasLeft(c))
                {
                    _ready = c;
                    if (c.Home != null) c.Home.BeginReturnNow();
                    continue;
                }
                StartCoroutine(FallThenDissolve(c));
            }

            if (_ready != null || Time.time - _emptySince < spawnDelaySeconds || LiveCount >= maxCubes) return;
            foreach (var c in _cubes)
            {
                if (c.State != CubeState.Pooled) continue;
                StartCoroutine(Materialize(c));
                return;
            }
        }

        private static bool IsHeldOrWorn(Cube c) => c.Item.IsSnapped || (c.Home != null && c.Home.IsHeld);

        private IEnumerator FallThenDissolve(Cube c)
        {
            c.State = CubeState.Falling;
            for (float e = 0f; e < fallSeconds; e += Time.deltaTime)
            {
                yield return null;
                if (IsHeldOrWorn(c))
                {
                    c.State = CubeState.Live;
                    yield break;
                }
            }
            yield return Dissolve(c);
        }

        private bool HasLeft(Cube c) =>
            c.Item.IsSnapped || Vector3.Distance(c.Transform.position, transform.position) > leaveDistance;

        private IEnumerator Materialize(Cube c)
        {
            _ready = c;
            c.State = CubeState.Materializing;
            var t = c.Transform;
            t.SetPositionAndRotation(transform.position, transform.rotation);
            t.localScale = Vector3.one * 0.001f;
            c.Item.gameObject.SetActive(true);
            c.Item.SetGrabEnabled(false);
            Freeze(c);
            if (c.Home != null)
            {
                c.Home.SetHomeToCurrentPose();
                c.Home.CancelReturn();
            }
            Play(audioSource, materializeSound);

            for (float e = 0f; e < materializeSeconds; e += Time.deltaTime)
            {
                float k = e / materializeSeconds;
                t.localScale = Vector3.one * EaseOutBack(k);
                Tint(c, 1f - k * k);
                yield return null;
            }

            Settle(c);
            c.State = CubeState.Live;
        }

        private IEnumerator Dissolve(Cube c)
        {
            if (_ready == c) _ready = null;
            c.State = CubeState.Dissolving;
            c.Item.SetGrabEnabled(false);
            Freeze(c);
            var source = c.Item.GetComponent<AudioSource>();
            Play(source, dissolveSound);

            var t = c.Transform;
            for (float e = 0f; e < dissolveSeconds; e += Time.deltaTime)
            {
                float k = e / dissolveSeconds;
                t.localScale = Vector3.one * (1f - k * k);
                Tint(c, Mathf.Sqrt(k));
                yield return null;
            }
            t.localScale = Vector3.one * 0.001f;

            // Switching the cube off stops its AudioSource, so the sound plays out first.
            while (source != null && source.isPlaying) yield return null;

            Settle(c);
            c.Item.gameObject.SetActive(false);
            c.State = CubeState.Pooled;
            if (_ready == null) _emptySince = Mathf.Min(_emptySince, Time.time - spawnDelaySeconds);
        }

        private static void Freeze(Cube c)
        {
            if (!c.Body.isKinematic)
            {
                c.Body.linearVelocity = Vector3.zero;
                c.Body.angularVelocity = Vector3.zero;
            }
            c.Body.isKinematic = true;
            c.Body.useGravity = false;
        }

        /// <summary>Full size, its own colour, grabbable and back under physics.</summary>
        private void Settle(Cube c)
        {
            c.Transform.localScale = Vector3.one;
            foreach (var r in c.Renderers) if (r != null) r.SetPropertyBlock(null);
            c.Item.SetGrabEnabled(true);
            if (!c.Item.IsSnapped)
            {
                c.Body.isKinematic = false;
                c.Body.useGravity = true;
            }
        }

        private void Tint(Cube c, float glow)
        {
            for (int i = 0; i < c.Renderers.Length; i++)
            {
                var r = c.Renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(ColorId, Color.Lerp(c.BaseColors[i], glowColor, glow));
                r.SetPropertyBlock(_block);
            }
        }

        private static float EaseOutBack(float k)
        {
            const float overshoot = 1.70158f;
            float x = k - 1f;
            return 1f + (overshoot + 1f) * x * x * x + overshoot * x * x;
        }

        private void Play(AudioSource source, AudioClip clip)
        {
            if (clip == null) return;
            if (source == null) source = audioSource = AudioSourceDefaults.EnsureSpatial(this, audioSource, 0.3f, 5f);
            source.PlayOneShot(clip, audioVolume);
        }
    }
}
