using UnityEngine;
using Oculus.Interaction;

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
            Debug.LogError("ColorChanger: Renderer missing.");
            return;
        }

        // grab current material color at start
        _lastSelectedColor = _renderer.material.color;

        if (rayInteractable == null)
        {
            Debug.LogError("ColorChanger: RayInteractable is not assigned.");
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

                // ignore Hover
        }
    }
}
