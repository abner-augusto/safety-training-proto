#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Actions
{
    /// <summary>
    /// Outcome of an <see cref="ActionCatalogLoader.Parse"/> call. Never throws into the
    /// runtime: callers inspect <see cref="Success"/> and fall back as needed. Mirrors
    /// <c>ScenarioLoadResult</c> so every JSON contract in the project fails identically.
    /// </summary>
    public sealed class ActionCatalogLoadResult
    {
        public bool Success { get; }
        public ActionCatalogDef? Catalog { get; }
        public IReadOnlyList<string> Errors { get; }

        private ActionCatalogLoadResult(bool success, ActionCatalogDef? catalog, IReadOnlyList<string> errors)
        {
            Success = success;
            Catalog = catalog;
            Errors = errors;
        }

        internal static ActionCatalogLoadResult Ok(ActionCatalogDef catalog) =>
            new(true, catalog, System.Array.Empty<string>());

        internal static ActionCatalogLoadResult Fail(IReadOnlyList<string> errors) =>
            new(false, null, errors);

        internal static ActionCatalogLoadResult Fail(string error) =>
            new(false, null, new[] { error });

        public string ErrorSummary => string.Join(" | ", Errors);
    }

    /// <summary>
    /// Single source of truth for parsing and validating an action-catalog JSON document.
    /// Shared by every host so the same input is accepted or rejected identically everywhere.
    /// </summary>
    public static class ActionCatalogLoader
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static ActionCatalogLoadResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return ActionCatalogLoadResult.Fail("JSON de catálogo de ações vazio.");
            }

            ActionCatalogDef? catalog;
            try
            {
                catalog = JsonConvert.DeserializeObject<ActionCatalogDef>(json, Settings);
            }
            catch (JsonException ex)
            {
                return ActionCatalogLoadResult.Fail($"Falha ao interpretar o JSON do catálogo de ações: {ex.Message}");
            }

            if (catalog == null)
            {
                return ActionCatalogLoadResult.Fail("JSON de catálogo de ações resultou em documento nulo.");
            }

            var errors = new List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < catalog.Actions.Count; i++)
            {
                var action = catalog.Actions[i];
                if (action == null)
                {
                    errors.Add($"Ação nula no índice {i} do catálogo.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(action.ActionId))
                {
                    errors.Add($"Ação no índice {i} tem 'actionId' vazio.");
                    continue;
                }

                if (!seen.Add(action.ActionId.Trim()))
                {
                    errors.Add($"actionId duplicado '{action.ActionId.Trim()}' no catálogo de ações.");
                }
            }

            return errors.Count > 0
                ? ActionCatalogLoadResult.Fail(errors)
                : ActionCatalogLoadResult.Ok(catalog);
        }
    }
}
