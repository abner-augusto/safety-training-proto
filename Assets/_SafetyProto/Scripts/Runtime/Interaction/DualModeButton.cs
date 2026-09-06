using System;
using Oculus.Interaction;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Unifies poke and ray interaction behind a single "on click" surface.
    ///
    /// The button is expected to host both an Oculus <see cref="PokeInteractable"/> (finger
    /// press) and a <see cref="RayInteractable"/> (controller / far ray), each forwarding its
    /// pointer events into one shared <see cref="PointableElement"/>. This component listens to
    /// that element and re-exposes the interaction as simple <see cref="UnityEvent"/>s, so a
    /// designer wires "on click -> run something" exactly once and it works regardless of which
    /// input mode the user employed.
    /// </summary>
    public class DualModeButton : MonoBehaviour
    {
        [Tooltip("Shared PointableElement that both the Poke and Ray interactables forward their events to. " +
                 "All interaction is observed through this single element.")]
        [SerializeField] private PointableElement _pointable;

        [Tooltip("Raised when the button is pressed (pointer Select). This is the most responsive moment " +
                 "and matches the feel of a physical poke press.")]
        [SerializeField] private UnityEvent _onClick = new UnityEvent();

        [Tooltip("Raised when the press is released (pointer Unselect).")]
        [SerializeField] private UnityEvent _onReleased = new UnityEvent();

        [Header("Audio Feedback")]
        [Tooltip("Optional AudioSource on the button. Auto-found or added if null.")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Audio clip played when the button is clicked.")]
        [SerializeField] private AudioClip _clickSound;
        [SerializeField, Range(0f, 1f)] private float _clickVolume = 0.7f;

        /// <summary>Code-side equivalent of <see cref="OnClick"/>, raised on pointer Select.</summary>
        public event Action Clicked;

        /// <summary>Code-side equivalent of <see cref="OnReleased"/>, raised on pointer Unselect.</summary>
        public event Action ReleasedEvent;

        /// <summary>Inspector-wired event fired when the button is pressed.</summary>
        public UnityEvent OnClick => _onClick;

        /// <summary>Inspector-wired event fired when the press is released.</summary>
        public UnityEvent OnReleased => _onReleased;

        private bool _subscribed;

        private void Awake()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            // With a clip but no source, PlayClickAudio builds one at play time from the
            // hard-coded values below, so what an author tuned in the scene is not what plays.
            if (_audioSource == null && _clickSound != null)
            {
                SafetyLog.Warning(
                    $"DualModeButton [{name}]: tem clipe de clique mas nenhum AudioSource. Um " +
                    "source será criado em runtime com valores padrão do código (spatialBlend 1, " +
                    "min 0.2 m, max 4 m), ignorando qualquer ajuste feito na cena.", this);
            }
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            if (_pointable == null)
            {
                SafetyLog.Warning("DualModeButton sem PointableElement atribuído; o botão não responderá a poke nem ray.", this);
                return;
            }

            _pointable.WhenPointerEventRaised += HandlePointerEvent;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            _pointable.WhenPointerEventRaised -= HandlePointerEvent;
            _subscribed = false;
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    PlayClickAudio();
                    _onClick.Invoke();
                    Clicked?.Invoke();
                    break;
                case PointerEventType.Unselect:
                    _onReleased.Invoke();
                    ReleasedEvent?.Invoke();
                    break;
            }
        }

        private void PlayClickAudio()
        {
            if (_clickSound == null) return;
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                    _audioSource.spatialBlend = 1f;
                    _audioSource.spatialize = true;
                    _audioSource.playOnAwake = false;
                    _audioSource.minDistance = 0.2f;
                    _audioSource.maxDistance = 4f;
                }
            }
            _audioSource.PlayOneShot(_clickSound, _clickVolume);
        }
    }
}
