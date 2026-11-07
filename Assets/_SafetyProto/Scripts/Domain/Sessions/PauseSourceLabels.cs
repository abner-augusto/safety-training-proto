#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Core.Events;

namespace SafetyProto.Domain.Sessions
{
    /// <summary>
    /// Session-log vocabulary for pause sources: the enum name is the stable <c>pauseSource</c>
    /// token, the Portuguese label goes into the human-readable <c>details</c> line.
    /// </summary>
    public static class PauseSourceLabels
    {
        public static string Describe(PauseSource source) => source switch
        {
            PauseSource.ApplicationFocus => "foco do aplicativo perdido",
            PauseSource.ApplicationPause => "aplicativo suspenso (óculos em repouso)",
            PauseSource.XrSessionUnfocused => "sessão XR sem foco (menu do sistema ou óculos retirados)",
            PauseSource.Popup => "popup aberto",
            PauseSource.PauseMenu => "menu de pausa",
            PauseSource.Practice => "área de prática",
            _ => "origem não informada"
        };

        public static string Describe(IReadOnlyList<PauseSource> sources)
        {
            if (sources.Count == 0) return Describe(PauseSource.Unspecified);
            var labels = new string[sources.Count];
            for (int i = 0; i < sources.Count; i++) labels[i] = Describe(sources[i]);
            return string.Join(" + ", labels);
        }

        /// <summary>Token list for <c>data.pauseSources</c>; null when empty so the field is omitted.</summary>
        public static string[]? Tokens(PauseSource[]? sources)
        {
            if (sources == null || sources.Length == 0) return null;
            return Array.ConvertAll(sources, s => s.ToString());
        }
    }
}
