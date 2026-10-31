using System.Collections.Generic;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.Scaffolding;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Tells a participant holding a two-hand piece (the toeboard) with one hand to use both: the
    /// spot where the second hand goes pulses, a short cue sounds, and the first time in the
    /// session a voice line says it. On 28/10 a participant tried for a long time with one hand,
    /// with nothing telling them why the piece would not install. Covers every
    /// <see cref="ScaffoldPieceInstaller"/> whose <see cref="ScaffoldPieceInstaller.RequiresTwoHands"/>
    /// is set; pieces that accept one hand never warn.
    /// </summary>
    public sealed class TwoHandGripCoach : MonoBehaviour
    {
        [SerializeField] private NarrationVoice narrationVoice;
        [Tooltip("Locução da primeira ocorrência na sessão (\"Use as duas mãos para posicionar a peça.\").")]
        [SerializeField] private AudioClip firstTimeNarration;
        [Tooltip("Aviso curto tocado na peça quando o aviso aparece.")]
        [SerializeField] private AudioClip cueClip;
        [SerializeField, Range(0f, 1f)] private float cueVolume = 0.8f;
        [Tooltip("Segundos com uma só mão antes do aviso, para quem já está levando a segunda mão.")]
        [SerializeField, Range(0f, 3f)] private float delaySeconds = 0.8f;
        [Tooltip("Intervalo mínimo entre dois avisos sonoros.")]
        [SerializeField, Range(0f, 30f)] private float cueCooldownSeconds = 8f;

        private sealed class Watch
        {
            public ScaffoldPieceInstaller Piece;
            public TwoHandHintPolicy Policy;
            public GameObject Marker;
        }

        private readonly List<Watch> _watches = new List<Watch>();
        private bool _narrated;

        private void Start()
        {
            if (narrationVoice == null) narrationVoice = FindAnyObjectByType<NarrationVoice>();
            foreach (var piece in FindObjectsByType<ScaffoldPieceInstaller>())
            {
                if (!piece.RequiresTwoHands) continue;
                _watches.Add(new Watch
                {
                    Piece = piece,
                    Policy = new TwoHandHintPolicy(delaySeconds, cueCooldownSeconds),
                    Marker = new GameObject($"SecondHandCue ({piece.name})"),
                });
            }
        }

        private void OnDestroy()
        {
            foreach (var watch in _watches)
                if (watch.Marker != null) Destroy(watch.Marker);
        }

        private void Update()
        {
            foreach (var watch in _watches)
            {
                if (watch.Piece == null) continue;
                int hands = watch.Piece.IsInstalled ? 0 : watch.Piece.SelectingHandCount;
                var step = watch.Policy.Tick(Time.time, hands);

                if (watch.Policy.Showing && watch.Piece.TryGetFreeGripPosition(out var grip))
                    watch.Marker.transform.position = grip;

                if (step.Show) Show(watch, step.PlayCue);
                if (step.Hide) AttentionService.Instance?.Release(watch.Marker);
            }
        }

        private void Show(Watch watch, bool playCue)
        {
            AttentionService.Instance?.Hold(watch.Marker);

            if (playCue && cueClip != null)
                AudioSource.PlayClipAtPoint(cueClip, watch.Piece.transform.position, cueVolume);

            if (_narrated || firstTimeNarration == null || narrationVoice == null) return;
            _narrated = true;
            narrationVoice.PlayAfterCurrent(firstTimeNarration);
        }
    }
}
