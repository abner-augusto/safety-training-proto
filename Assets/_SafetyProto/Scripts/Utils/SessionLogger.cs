using UnityEngine;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
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
                new SafetyLogAdapter());
            _core.Subscribe();
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
