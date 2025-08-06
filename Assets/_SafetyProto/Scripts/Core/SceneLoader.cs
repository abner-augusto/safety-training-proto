using SafetyProto.Core.Interfaces;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour, ISessionResettable
{
    [System.Serializable]
    public class StringEvent : UnityEvent<string> { }

    [Header("Events")]
    public StringEvent onLoadScene;
    public UnityEvent onReloadScene;

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
    private void ResetManagers()
    {
        foreach (var resettable in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                     .OfType<ISessionResettable>())
        {
            // Avoid calling ResetSession on self
            if ((SceneLoader)resettable != this)
            {
                resettable.ResetSession();
            }
        }
    }
    
    /// <summary>
    /// Full session reset + scene reload. Call this to restart everything.
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
