using System.Linq;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SafetyProto.Core
{
    public class SceneLoader : MonoBehaviour, ISessionResettable
    {
        [System.Serializable]
        public class StringEvent : UnityEvent<string> { }

        [Header("Events")]
        public StringEvent onLoadScene;
        public UnityEvent onReloadScene;

        [SerializeField] 
        private ScoreServiceSO scoreService;

        /// <summary>
        /// Loads a scene by its name.
        /// </summary>
        public void LoadSceneByName(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        /// <summary>
        /// Reloads the current active scene.
        /// </summary>
        public void ReloadCurrentScene()
        {
            var currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
        }

        /// <summary>
        /// Resets all ISessionResettable managers in the current scene, excluding this loader.
        /// </summary>
        public void ResetManagers()
        {
            var resettableObjects = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ISessionResettable>()
                .ToList();

            Debug.Log($"[SceneLoader] Found {resettableObjects.Count} ISessionResettable components.");

            foreach (var resettable in resettableObjects)
            {
                if (!ReferenceEquals(this, resettable))
                {
                    Debug.Log($"[SceneLoader] Resetting: {resettable.GetType().Name}");
                    resettable.ResetSession();
                }
                else
                {
                    Debug.Log("[SceneLoader] Skipping self-reset.");
                }
            }

            if (scoreService != null)
            {
                Debug.Log($"[SceneLoader] Resetting ScriptableObject service: {scoreService.name}");
                scoreService.ResetSession();
            }

            Debug.Log("[SceneLoader] All resettable managers processed.");
        }

        /// <summary>
        /// Full session reset and scene reload. Call this to restart everything.
        /// </summary>
        public void ResetSession()
        {
            ResetManagers();
            ReloadCurrentScene();
        }
        
        private void Reset()
        {
            onLoadScene ??= new StringEvent();
            onReloadScene ??= new UnityEvent();

            if (onLoadScene.GetPersistentEventCount() == 0)
                onLoadScene.AddListener(LoadSceneByName);
            if (onReloadScene.GetPersistentEventCount() == 0)
                onReloadScene.AddListener(ReloadCurrentScene);
        }
    }
}
