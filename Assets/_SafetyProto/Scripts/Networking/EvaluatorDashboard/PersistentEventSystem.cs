using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Garante que só existe um EventSystem na cena, mesmo após DontDestroyOnLoad.
/// Adicione este componente no mesmo GameObject do EventSystem.
/// </summary>
[RequireComponent(typeof(EventSystem))]
public class PersistentEventSystem : MonoBehaviour
{
    private static PersistentEventSystem _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // Já existe um — destrói este duplicado
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
