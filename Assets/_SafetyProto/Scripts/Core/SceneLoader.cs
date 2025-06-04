using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    [System.Serializable]
    public class StringEvent : UnityEvent<string> { }

    [Header("Events")]
    public StringEvent onLoadScene;   // Drag this in inspector or call from code to load a specific scene
    public UnityEvent onReloadScene;  // Drag this or call from code to reload the current scene

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
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    // Optional: Setup default listeners when you add the component in the Editor
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