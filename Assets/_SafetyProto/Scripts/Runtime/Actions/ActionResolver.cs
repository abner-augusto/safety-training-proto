#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Actions;

namespace SafetyProto.Runtime.Actions
{
    /// <summary>
    /// Runtime helper that resolves action ids to their <em>logical</em> definition
    /// (<see cref="ActionDef"/>). Source of truth is the unified action-catalog JSON, loaded
    /// via <see cref="ActionCatalogSource"/> (override → embedded default). Runtime is 100%
    /// JSON — there is no ScriptableObject fallback; the embedded <c>Resources/Actions/actions.json</c>
        /// baked into the build is the guaranteed floor. Presentation (icon/SFX/haptics) is
        /// intentionally not resolved here.
    /// </summary>
    public static class ActionResolver
    {
        private static ActionCatalogDef? _catalog;
        private static bool _catalogLoadAttempted;

        private static readonly HashSet<string> MissingActions = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxMissingWarnings = 10;
        private static int _missingWarningCount;

        /// <summary>Injects a catalog directly (tests / non-Resources hosts), bypassing the load.</summary>
        public static void Configure(ActionCatalogDef catalog)
        {
            _catalog = catalog;
            _catalogLoadAttempted = true;
            MissingActions.Clear();
            _missingWarningCount = 0;
        }

        /// <summary>Resolves an action id to its logical definition, or null when unknown.</summary>
        public static ActionDef? Resolve(string actionId)
            => TryResolve(actionId, out var definition) ? definition : null;

        /// <summary>True when the action id exists in the JSON catalog.</summary>
        public static bool TryResolve(string actionId, out ActionDef? definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(actionId)) return false;

            var normalized = actionId.Trim();

            if (EnsureCatalogLoaded() && _catalog!.TryGet(normalized, out definition))
            {
                return true;
            }

            WarnMissingAction(normalized, $"Ação '{normalized}' não encontrada no catálogo de ações (Resources/Actions/actions.json).");
            return false;
        }

        private static bool EnsureCatalogLoaded()
        {
            if (_catalog != null) return true;
            if (_catalogLoadAttempted) return false;

            _catalog = ActionCatalogSource.Load();
            _catalogLoadAttempted = true;
            return _catalog != null;
        }

        private static void WarnMissingAction(string actionId, string message)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            if (!MissingActions.Add(actionId)) return;

            if (_missingWarningCount >= MaxMissingWarnings)
            {
                if (_missingWarningCount == MaxMissingWarnings)
                {
                    SafetyLog.Warning("[ActionResolver] Avisos adicionais de ações ausentes suprimidos.");
                    _missingWarningCount++;
                }
                return;
            }

            _missingWarningCount++;
            SafetyLog.Warning($"[ActionResolver] {message}");
        }
    }
}
