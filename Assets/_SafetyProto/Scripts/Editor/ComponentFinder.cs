#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ComponentFinderWindow : EditorWindow
{
    private MonoScript _monoScript;
    private string     _typeName   = "";
    private string     _lastSearch = "";

    private List<GameObject> _results    = new();
    private string           _statusMsg  = "";
    private Vector2          _scrollPos;

    [MenuItem("Tools/Component Finder")]
    public static void OpenWindow()
    {
        var window = GetWindow<ComponentFinderWindow>("Component Finder");
        window.minSize = new Vector2(360, 300);
    }

    private void OnGUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Component Finder", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Find all scene GameObjects with a given component.", EditorStyles.miniLabel);
        GUILayout.Space(6);

        DrawSeparator();
        GUILayout.Space(4);

        // --- Script object field (drag & drop) ---
        EditorGUI.BeginChangeCheck();
        _monoScript = (MonoScript)EditorGUILayout.ObjectField(
            new GUIContent("Script", "Drag a MonoScript asset here"),
            _monoScript, typeof(MonoScript), allowSceneObjects: false);
        if (EditorGUI.EndChangeCheck() && _monoScript != null)
            _typeName = _monoScript.GetClass()?.Name ?? "";

        // --- Manual type-name field ---
        EditorGUI.BeginChangeCheck();
        _typeName = EditorGUILayout.TextField(
            new GUIContent("Component Name", "Type the exact class name, e.g. Rigidbody"),
            _typeName);
        if (EditorGUI.EndChangeCheck())
            _monoScript = null; // user typed manually — clear the drag slot

        GUILayout.Space(6);

        // --- Search button ---
        GUI.enabled = !string.IsNullOrWhiteSpace(_typeName);
        if (GUILayout.Button("Search in Scene", GUILayout.Height(28)))
            RunSearch();
        GUI.enabled = true;

        // --- Status / error ---
        if (!string.IsNullOrEmpty(_statusMsg))
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(_statusMsg, MessageType.Info);
        }

        if (_results.Count == 0) return;

        GUILayout.Space(6);
        DrawSeparator();
        GUILayout.Space(4);

        // --- Results header ---
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Results for  \"{_lastSearch}\"  —  {_results.Count} object(s)",
            EditorStyles.boldLabel);
        if (GUILayout.Button("Select All", GUILayout.Width(80)))
            SelectAll();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);

        // --- Column headers ---
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("#",    GUILayout.Width(28));
        EditorGUILayout.LabelField("GameObject Name");
        EditorGUILayout.LabelField("Ping", GUILayout.Width(44));
        EditorGUILayout.EndHorizontal();

        // --- Scrollable list ---
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        for (int i = 0; i < _results.Count; i++)
        {
            var go = _results[i];
            if (go == null) continue;

            EditorGUILayout.BeginHorizontal(i % 2 == 0 ? EditorStyles.helpBox : GUIStyle.none);

            EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(28));

            // Clicking the label selects the object in the Hierarchy
            if (GUILayout.Button(go.name, EditorStyles.label))
                Selection.activeGameObject = go;

            if (GUILayout.Button("⊙", GUILayout.Width(44)))
                EditorGUIUtility.PingObject(go);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    // -----------------------------------------------------------------------

    private void RunSearch()
    {
        _results.Clear();
        _statusMsg = "";

        string trimmed = _typeName.Trim();
        Type type = ResolveType(trimmed);

        if (type == null)
        {
            _statusMsg = $"Type \"{trimmed}\" not found. Make sure the class name is correct and the script is compiled.";
            _lastSearch = trimmed;
            Repaint();
            return;
        }

        if (!typeof(Component).IsAssignableFrom(type))
        {
            _statusMsg = $"\"{trimmed}\" is not a Component type.";
            _lastSearch = trimmed;
            Repaint();
            return;
        }

        var components = (Component[])FindObjectsByType(type, FindObjectsSortMode.None);
        foreach (var c in components)
            _results.Add(c.gameObject);

        _lastSearch = trimmed;
        _statusMsg  = _results.Count == 0
            ? $"No GameObjects with \"{trimmed}\" found in the active scene."
            : "";

        Repaint();
    }

    private static Type ResolveType(string name)
    {
        // Check all loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = assembly.GetType(name, throwOnError: false);
            if (t != null) return t;

            // Also try without namespace (match by simple name)
            foreach (var exported in assembly.GetExportedTypes())
                if (exported.Name == name) return exported;
        }
        return null;
    }

    private void SelectAll()
    {
        Selection.objects = _results.ToArray();
    }

    private static void DrawSeparator()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        EditorGUILayout.Space(2);
    }
}
#endif
