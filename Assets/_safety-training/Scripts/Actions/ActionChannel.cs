using UnityEngine;
using UnityEngine.Events;

namespace SafetyTraining.Actions
{
    [System.Serializable]
    public class ActionEvent : UnityEvent<ActionEventData> { }

    [CreateAssetMenu(menuName = "Safety Training/Action Channel", fileName = "NewActionChannel")]
    public class ActionChannel : ScriptableObject
    {
        [Tooltip("Invoked whenever an action is raised through this channel.")]
        public ActionEvent OnActionRaised = new ActionEvent();

        /// <summary>
        /// Raise an action event through this channel.
        /// </summary>
        /// <param name="data">The payload describing the action.</param>
        public void Raise(ActionEventData data)
        {
            OnActionRaised?.Invoke(data);
        }
    }

    /// <summary>
    /// Data container for an action event.
    /// </summary>
    [System.Serializable]
    public class ActionEventData
    {
        public ActionType ActionType;
        public GameObject Source;

        public ActionEventData(ActionType actionType, GameObject source)
        {
            ActionType = actionType;
            Source = source;
        }
    }
}
