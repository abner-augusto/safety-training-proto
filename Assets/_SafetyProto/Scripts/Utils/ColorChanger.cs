using Oculus.Interaction;
using UnityEngine;
using SafetyProto.Core.Logging;

namespace SafetyProto.Utils
{
    [RequireComponent(typeof(Renderer))]
    public class ColorChangerFullControl : MonoBehaviour
    {
        [SerializeField]
        private RayInteractable rayInteractable;

        private Renderer _renderer;
        private Color _lastSelectedColor;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();

            if (_renderer == null)
            {
                SafetyLog.Error("ColorChanger: Renderer missing.", this);
                return;
            }

            _lastSelectedColor = _renderer.material.color;

            if (rayInteractable == null)
            {
                SafetyLog.Error("ColorChanger: RayInteractable is not assigned.", this);
                return;
            }

            rayInteractable.WhenStateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            if (rayInteractable != null)
            {
                rayInteractable.WhenStateChanged -= OnStateChanged;
            }
        }

        private void OnStateChanged(InteractableStateChangeArgs args)
        {
            switch (args.NewState)
            {
                case InteractableState.Select:
                    _lastSelectedColor = Random.ColorHSV();
                    _renderer.material.color = _lastSelectedColor;
                    break;

                case InteractableState.Normal:
                    _renderer.material.color = _lastSelectedColor;
                    break;
            }
        }
    }
}
