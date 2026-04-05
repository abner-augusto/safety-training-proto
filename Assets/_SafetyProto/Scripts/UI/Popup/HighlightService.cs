using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.UI
{
    public static class HighlightService
    {
        private static readonly int OutlineEnabledId =
            Shader.PropertyToID("_OutlineEnabled");
        private static readonly MaterialPropertyBlock _mpb =
            new MaterialPropertyBlock();

        public static void Enable(GameObject target)  => SetOutline(target, 1f);
        public static void Disable(GameObject target) => SetOutline(target, 0f);

        private static void SetOutline(GameObject target, float value)
        {
            if (target == null) return;

            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0)
            {
                SafetyLog.Warning(
                    $"HighlightService: nenhum Renderer encontrado em '{target.name}'.");
                return;
            }

            foreach (var r in renderers)
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(OutlineEnabledId, value);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
