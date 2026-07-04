using System;
using UnityEngine;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Actions;
using SafetyProto.Domain.Sessions;

namespace SafetyProto.Utils
{
    public class SessionLogger : MonoBehaviour, ISessionResettable
    {
        private SessionLoggerCore _core;

        private void Awake()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            _core = new SessionLoggerCore(
                EventBus.Instance,
                Application.persistentDataPath,
                log => JsonUtility.ToJson(log, true),
                new SafetyLogAdapter(),
                BuildActionNameResolver());
            _core.Subscribe();
        }

        /// <summary>
        /// Builds an actionId → friendly-name resolver from the embedded action catalog
        /// (<c>Resources/Actions/actions</c>). Loaded here via the Domain loader rather than
        /// <c>ActionResolver</c> because that lives in Runtime, which references Utils — pulling it
        /// in would create an assembly cycle. Unknown ids fall back to the raw id.
        /// </summary>
        private static Func<string, string> BuildActionNameResolver()
        {
            var textAsset = Resources.Load<TextAsset>("Actions/actions");
            var catalog = textAsset != null ? ActionCatalogLoader.Parse(textAsset.text).Catalog : null;
            return id => (catalog != null && catalog.TryGet(id, out var def) && def != null)
                ? def.ResolveLogName()
                : id;
        }

        private void OnDestroy()
        {
            _core?.Dispose();
            _core = null;
        }

        public void ResetSession()
        {
            _core?.ResetSession();
        }
    }
}
