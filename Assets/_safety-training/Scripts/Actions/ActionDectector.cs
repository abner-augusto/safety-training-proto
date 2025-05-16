using UnityEngine;

namespace SafetyTraining.Actions
{
    /// <summary>
    /// Base class for detecting player actions in the scene and relaying them through an ActionChannel.
    /// Attach to a GameObject with a collider (isTrigger) or wire up to other events.
    /// </summary>
    public abstract class ActionDetector : MonoBehaviour
    {
        [Tooltip("Channel through which to raise action events.")]
        public ActionChannel channel;

        [Tooltip("Type of action this detector will raise.")]
        public ActionType actionType;

        /// <summary>
        /// Call this to raise the action event.
        /// </summary>
        /// <param name="source">The GameObject that triggered the action.</param>
        protected void RaiseAction(GameObject source)
        {
            if (channel == null)
            {
                Debug.LogWarning($"[{nameof(ActionDetector)}] No channel assigned on {gameObject.name}.");
                return;
            }
            var data = new ActionEventData(actionType, source);
            channel.Raise(data);
        }
    }

    /// <summary>
    /// Example of a trigger-based detector: when the player enters the zone with the correct tool equipped.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ProximityActionDetector : ActionDetector
    {
        [Tooltip("Tag that the player GameObject should have to be considered valid.")]
        public string playerTag = "Player";

        [Tooltip("Optional: required tool GameObject that must be a child of player to allow action.")]
        public GameObject requiredTool;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;

            // If a tool is required, ensure it's currently held/snapped under the player hierarchy
            if (requiredTool != null && !requiredTool.activeInHierarchy)
            {
                return;
            }

            // Raise the action when entering the zone
            RaiseAction(other.gameObject);
        }
    }
}