#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScriptScannerWindow : EditorWindow
{
    private string _scriptName = string.Empty;
    private readonly List<Component> _results = new List<Component>();
    private Vector2 _scroll;

    [MenuItem("Tools/Debug/Find Script Instances")]
    public static void ShowWindow()
    {
        GetWindow<ScriptScannerWindow>("Script Scanner").Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Find Components In Scene", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _scriptName = EditorGUILayout.TextField("Component Name", _scriptName);
        if (EditorGUI.EndChangeCheck())
        {
            _results.Clear();
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_scriptName)))
        {
            if (GUILayout.Button("Find Instances"))
            {
                FindInstances();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Results ({_results.Count})", EditorStyles.boldLabel);

        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            foreach (var component in _results)
            {
                if (component == null) continue;
                EditorGUILayout.ObjectField(component.gameObject.name, component.gameObject, typeof(GameObject), true);
            }
        }
    }

    private void FindInstances()
    {
        _results.Clear();

        var trimmedName = _scriptName.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            Debug.LogWarning("Script Scanner: Please enter a component name.");
            return;
        }

        var targetType = ResolveType(trimmedName);
        if (targetType == null)
        {
            Debug.LogError($"Script Scanner: Could not find a type named '{trimmedName}'.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var count = 0;

        foreach (var root in roots)
        {
            var components = root.GetComponentsInChildren(targetType, true);
            foreach (var component in components)
            {
                if (component is Component unityComponent)
                {
                    _results.Add(unityComponent);
                    Debug.Log($"[{targetType.Name}] on: {unityComponent.gameObject.name}", unityComponent.gameObject);
                    count++;
                }
            }
        }

        Debug.Log($"Script Scanner: Found {count} instances of {targetType.FullName} in scene '{scene.name}'.");
        Repaint();
    }

    private static Type ResolveType(string typeName)
    {
        // First try fully qualified names
        var type = Type.GetType(typeName);
        if (type != null)
        {
            return type;
        }

        // Search through loaded assemblies for the first matching type name
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        // Fall back to searching all MonoBehaviours by simple name
        return TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
    }
}
#endif
