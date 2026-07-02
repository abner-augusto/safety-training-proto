#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Domain.Actions;
using UnityEngine;

namespace SafetyProto.Runtime.Actions
{
    /// <summary>
    /// Runtime helper that resolves action ids to their <em>logical</em> definition
    /// (<see cref="ActionDef"/>). Source of truth is the unified action-catalog JSON, loaded
    /// via <see cref="ActionCatalogSource"/> (override → embedded default). If the JSON is
    /// missing, it falls back to the legacy <see cref="ActionRegistry"/> ScriptableObject,
    /// bridging each entry to an <see cref="ActionDef"/> so callers see one shape. Presentation
    /// (icon/SFX/haptics) is intentionally not resolved here — it stays Unity-side on the SO.
    /// </summary>
    public static class ActionResolver
    {
        private const string DefaultRegistryResourcePath = "ActionRegistry";

        private static ActionCatalogDef? _catalog;
        private static bool _catalogLoadAttempted;

        private static ActionRegistry? _registry;
        private static bool _hasLoggedMissingRegistry;

        private static readonly HashSet<string> MissingActions = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxMissingWarnings = 10;
        private static int _missingWarningCount;

        /// <summary>Injects a catalog directly (tests / non-Resources hosts), bypassing the load.</summary>
        public static void Configure(ActionCatalogDef catalog)
        {
            _catalog = catalog;
            _catalogLoadAttempted = true;
            ResetMissingLog();
        }

        /// <summary>Injects the legacy SO registry fallback (tests).</summary>
        public static void Configure(ActionRegistry registry)
        {
            _registry = registry;
            _hasLoggedMissingRegistry = false;
            ResetMissingLog();
        }

        /// <summary>Resolves an action id to its logical definition, or null when unknown.</summary>
        public static ActionDef? Resolve(string actionId)
            => TryResolve(actionId, out var definition) ? definition : null;

        /// <summary>True when the action id exists (JSON catalog first, SO registry fallback).</summary>
        public static bool TryResolve(string actionId, out ActionDef? definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(actionId)) return false;

            var normalized = actionId.Trim();

            // Primary: unified JSON catalog.
            if (EnsureCatalogLoaded() && _catalog!.TryGet(normalized, out definition))
            {
                return true;
            }

            // Fail-safe: legacy ActionRegistry SO, bridged to an ActionDef.
            if (EnsureRegistryLoaded() && _registry!.TryGet(normalized, out var so) && so != null)
            {
                definition = FromScriptableObject(so);
                return true;
            }

            WarnMissingAction(normalized, $"Ação '{normalized}' não encontrada no catálogo nem no ActionRegistry.");
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

        private static bool EnsureRegistryLoaded()
        {
            if (_registry != null) return true;

            _registry = Resources.Load<ActionRegistry>(DefaultRegistryResourcePath);

            if (_registry == null && !_hasLoggedMissingRegistry)
            {
                SafetyLog.Warning($"[ActionResolver] Catálogo JSON ausente e ActionRegistry não encontrado em Resources/{DefaultRegistryResourcePath}.");
                _hasLoggedMissingRegistry = true;
            }

            return _registry != null;
        }

        private static ActionDef FromScriptableObject(ActionTypeSO so) => new()
        {
            ActionId = so.ActionId,
            DisplayName = so.DisplayName,
            Description = so.Description,
            Category = so.Category.ToString(),
            TelemetryName = so.TelemetryName,
            Tags = new List<string>(so.Tags),
            RegulatoryRefs = new List<string>(so.RegulatoryRefs),
            IsSafetyCritical = so.IsSafetyCritical,
            IsHiddenInUI = so.IsHiddenInUI,
            CooldownSeconds = so.CooldownSeconds,
            ExpectedDurationSeconds = so.ExpectedDurationSeconds,
            BaseScore = so.BaseScore,
            Severity = so.Severity,
        };

        private static void ResetMissingLog()
        {
            MissingActions.Clear();
            _missingWarningCount = 0;
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
