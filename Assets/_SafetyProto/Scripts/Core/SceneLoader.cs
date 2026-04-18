using System.Linq;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
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

        public void LoadSceneByName(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        public void ReloadCurrentScene()
        {
            var currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
        }

        public void ResetManagers()
        {
            var resettableObjects = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ISessionResettable>()
                .ToList();

            SafetyLog.Info($"[SceneLoader] Found {resettableObjects.Count} ISessionResettable components.", this);

            foreach (var resettable in resettableObjects)
            {
                if (!ReferenceEquals(this, resettable))
                {
                    SafetyLog.Info($"[SceneLoader] Resetting: {resettable.GetType().Name}", this);
                    resettable.ResetSession();
                }
                else
                {
                    SafetyLog.Info("[SceneLoader] Skipping self-reset.", this);
                }
            }

            SafetyLog.Info("[SceneLoader] Resetting ScoreService singleton.", this);
            ScoreService.Instance.ResetSession();

            SafetyLog.Info("[SceneLoader] All resettable managers processed.", this);
        }

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
