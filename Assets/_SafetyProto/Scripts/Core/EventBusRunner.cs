using UnityEngine;

namespace SafetyProto.Core
{
    public class EventBusRunner : MonoBehaviour
    {
        [SerializeField]
        private EventBus eventBus;

        private void Awake()
        {
            if (eventBus == null)
            {
                eventBus = EventBus.Instance;
            }
        }

        private void Update()
        {
            if (eventBus != null)
            {
                eventBus.ProcessEvents(2);
            }
        }
    }
}
