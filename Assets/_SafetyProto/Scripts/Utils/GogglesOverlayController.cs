using SafetyProto.Utils;
using UnityEngine;

public sealed class GogglesOverlayController : MonoBehaviour
{
    [SerializeField] private Material mat;
    [SerializeField] private float fadeInSeconds = 0.25f;
    [SerializeField] private float fadeOutSeconds = 0.10f;

    private float _alpha;
    private float _target;

    private void Reset()
    {
        var img = GetComponent<UnityEngine.UI.Graphic>();
        if (img != null) mat = img.material;
    }

    private void Update()
    {
        if (mat == null) return;

        float t = (_target > _alpha) ? fadeInSeconds : fadeOutSeconds;
        float k = (t <= 1e-4f) ? 1f : Time.unscaledDeltaTime / t;

        _alpha = Mathf.MoveTowards(_alpha, _target, k);
        mat.SetFloat("_GlobalAlpha", _alpha);
    }

    public void SetWorn(bool worn) => _target = worn ? 1f : 0f;
}
