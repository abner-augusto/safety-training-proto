#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SafetyProto.Domain.Actions
{
    /// <summary>
    /// Small editing/validation surface for the JSON-backed action catalog.
    /// Used by authoring tools so action ids follow the same rules everywhere.
    /// </summary>
    public static class ActionCatalogEditor
    {
        private static readonly Regex ActionIdPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

        public static ActionDef CreateDefault(string displayName = "Nova Ação")
        {
            var id = GenerateActionId(displayName);
            return new ActionDef
            {
                ActionId = id,
                DisplayName = displayName,
                Description = string.Empty,
                Category = "TaskStep",
                TelemetryName = id,
                Tags = new List<string>(),
                RegulatoryRefs = new List<string>(),
            };
        }

        public static string GenerateActionId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return "nova_acao";

            var asciiName = RemoveDiacritics(displayName.Trim().ToLowerInvariant());
            var chars = asciiName
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray();

            var id = Regex.Replace(new string(chars), "_+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(id)) id = "nova_acao";
            if (!char.IsLetter(id[0])) id = "a_" + id;
            return id;
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static IReadOnlyList<string> Validate(ActionCatalogDef catalog)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < catalog.Actions.Count; i++)
            {
                var action = catalog.Actions[i];
                if (action == null)
                {
                    errors.Add($"Ação nula no índice {i} do catálogo.");
                    continue;
                }

                var actionId = action.ActionId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(actionId))
                {
                    errors.Add($"Ação no índice {i} tem 'actionId' vazio.");
                    continue;
                }

                if (!ActionIdPattern.IsMatch(actionId))
                {
                    errors.Add($"actionId '{actionId}' deve usar snake_case ASCII e começar com letra.");
                }

                if (!seen.Add(actionId))
                {
                    errors.Add($"actionId duplicado '{actionId}' no catálogo de ações.");
                }

                if (string.IsNullOrWhiteSpace(action.DisplayName))
                {
                    errors.Add($"Ação '{actionId}' está sem nome exibido.");
                }

                if (string.IsNullOrWhiteSpace(action.Category))
                {
                    errors.Add($"Ação '{actionId}' está sem categoria.");
                }
            }

            return errors;
        }
    }
}
