using System;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;

namespace SafetyProto.Gameplay.Actions
{
    /// <summary>
    /// Runtime helper that resolves action IDs to their associated ActionTypeSO assets.
    /// </summary>
    public static class ActionResolver
    {
        private const string DefaultRegistryResourcePath = "ActionRegistry";

        private static ActionRegistry _registry;
        private static bool _hasLoggedMissingRegistry;
        private static readonly HashSet<string> MissingActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int MaxMissingWarnings = 10;
        private static int _missingWarningCount;

        public static void Configure(ActionRegistry registry)
        {
            _registry = registry;
            _hasLoggedMissingRegistry = false;
            MissingActions.Clear();
            _missingWarningCount = 0;
}

        /// <summary>
        /// Resolves an action ID to its definition. Returns null when not found.
        /// </summary>
        public static ActionTypeSO Resolve(string actionId)
            => TryResolve(actionId, out var definition) ? definition : null;

        /// <summary>
        /// Attempts to resolve an action ID to its definition.
        /// </summary>
        public static bool TryResolve(string actionId, out ActionTypeSO definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(actionId)) return false;

            var normalized = actionId.Trim();

            if (!EnsureRegistryLoaded())
            {
                WarnMissingAction(normalized, $"Action '{normalized}' requested but ActionRegistry is not loaded.");
                return false;
            }

            if (_registry.TryGet(normalized, out definition)) return true;

            WarnMissingAction(normalized, $"Action '{normalized}' not found in registry.");
            return false;
        }

        private static bool EnsureRegistryLoaded()
        {
            if (_registry != null) return true;

            _registry = Resources.Load<ActionRegistry>(DefaultRegistryResourcePath);

            if (_registry == null && !_hasLoggedMissingRegistry)
            {
                SafetyLog.Warning($"[ActionResolver] Could not locate ActionRegistry at Resources/{DefaultRegistryResourcePath}.");
                _hasLoggedMissingRegistry = true;
            }

            return _registry != null;
        }

        private static void WarnMissingAction(string actionId, string message)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            if (!MissingActions.Add(actionId)) return;

            if (_missingWarningCount >= MaxMissingWarnings)
            {
                if (_missingWarningCount == MaxMissingWarnings)
                {
                    SafetyLog.Warning("[ActionResolver] Additional missing action warnings suppressed.");
                    _missingWarningCount++;
                }
                return;
            }

            _missingWarningCount++;
            SafetyLog.Warning($"[ActionResolver] {message}");
        }
    }
}
