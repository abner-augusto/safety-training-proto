using UnityEngine;

namespace SafetyProto.Gameplay.Actions
{
    /// <summary>
    /// Runtime helper that resolves action IDs to their associated ActionDefinition assets.
    /// </summary>
    public static class ActionResolver
    {
        private const string DefaultRegistryResourcePath = "ActionRegistry";

        private static ActionRegistry _registry;
        private static bool _hasLoggedMissingRegistry;

        /// <summary>
        /// Overrides the registry used for lookups. Useful for tests or bootstrap code.
        /// </summary>
        public static void Configure(ActionRegistry registry)
        {
            _registry = registry;
            _hasLoggedMissingRegistry = false;
        }

        /// <summary>
        /// Resolves an action ID to its definition. Returns null when not found.
        /// </summary>
        public static ActionDefinition Resolve(string actionId)
        {
            return TryResolve(actionId, out var definition) ? definition : null;
        }

        /// <summary>
        /// Attempts to resolve an action ID to its definition.
        /// </summary>
        public static bool TryResolve(string actionId, out ActionDefinition definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            if (!EnsureRegistryLoaded())
            {
                return false;
            }

            return _registry.TryGet(actionId, out definition);
        }

        private static bool EnsureRegistryLoaded()
        {
            if (_registry != null)
            {
                return true;
            }

            _registry = Resources.Load<ActionRegistry>(DefaultRegistryResourcePath);

            if (_registry == null && !_hasLoggedMissingRegistry)
            {
                Debug.LogWarning($"[ActionResolver] Could not locate ActionRegistry at Resources/{DefaultRegistryResourcePath}. Action lookups will fail until it is created or configured.");
                _hasLoggedMissingRegistry = true;
            }

            return _registry != null;
        }
    }
}
