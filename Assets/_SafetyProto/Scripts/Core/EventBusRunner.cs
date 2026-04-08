using UnityEngine;

namespace SafetyProto.Core
{
    public class EventBusRunner : MonoBehaviour
    {
        [SerializeField]
        private EventBus eventBus;

        private void Awake() => eventBus ??= EventBus.Instance;

        private void Update() => eventBus?.ProcessEvents(0.5f);
    }
}
