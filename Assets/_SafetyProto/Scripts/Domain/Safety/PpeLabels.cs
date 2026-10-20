#nullable enable
using SafetyProto.Core;

namespace SafetyProto.Domain.Safety
{
    /// <summary>
    /// Portuguese display name of each <see cref="PPEType"/>, shared by the in-scene feedback,
    /// the session log, the debug HUD and the authoring app's EPIs tab. The dashboard keeps its
    /// own PT/EN copy in <c>Tools/DashboardSrc/js/i18n.js</c>; change both together.
    /// </summary>
    public static class PpeLabels
    {
        public static string DisplayName(PPEType ppeType) => ppeType switch
        {
            PPEType.Helmet => "Capacete",
            PPEType.Goggles => "Óculos de proteção",
            PPEType.Harness => "Cinto paraquedista",
            PPEType.Vest => "Colete de segurança",
            PPEType.Boots => "Botina de segurança",
            PPEType.GloveLeft => "Luva esquerda",
            PPEType.GloveRight => "Luva direita",
            _ => "EPI não identificado"
        };
    }
}
