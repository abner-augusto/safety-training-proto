#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class TriangleCounterWindow : EditorWindow
{
    // --- Results ---
    private int _meshFilterTotal;
    private int _skinnedTotal;
    private int _grandTotal;

    private List<(string name, int tris, string type)> _topObjects = new();

    private int _topCount = 10;
    private Vector2 _scrollPos;
    private bool _hasCounted = false;

    [MenuItem("Tools/Triangle Counter")]
    public static void OpenWindow()
    {
        var window = GetWindow<TriangleCounterWindow>("Triangle Counter");
        window.minSize = new Vector2(420, 400);
    }

    private void OnGUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Triangle Counter", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Counts tris from MeshFilter and SkinnedMeshRenderer.", EditorStyles.miniLabel);
        GUILayout.Space(6);

        // Top N setting
        _topCount = EditorGUILayout.IntSlider("Top objects to list", _topCount, 5, 50);
        GUILayout.Space(6);

        // Buttons
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Count All", GUILayout.Height(28)))
            CountAll(countMesh: true, countSkinned: true);

        if (GUILayout.Button("MeshFilter Only", GUILayout.Height(28)))
            CountAll(countMesh: true, countSkinned: false);

        if (GUILayout.Button("Skinned Only", GUILayout.Height(28)))
            CountAll(countMesh: false, countSkinned: true);

        EditorGUILayout.EndHorizontal();

        if (!_hasCounted) return;

        GUILayout.Space(10);

        // Summary box
        var boxStyle = new GUIStyle(EditorStyles.helpBox);
        EditorGUILayout.BeginVertical(boxStyle);

        DrawSummaryRow("MeshFilter tris:", _meshFilterTotal, Color.cyan);
        DrawSummaryRow("SkinnedMeshRenderer tris:", _skinnedTotal, new Color(1f, 0.6f, 0.2f));

        EditorGUILayout.Space(2);
        var prevColor = GUI.color;
        GUI.color = Color.green;
        EditorGUILayout.LabelField("Grand Total", _grandTotal.ToString("N0") + " tris", EditorStyles.boldLabel);
        GUI.color = prevColor;

        EditorGUILayout.EndVertical();

        GUILayout.Space(10);
        EditorGUILayout.LabelField($"Top {_topObjects.Count} objects by triangle count:", EditorStyles.boldLabel);
        GUILayout.Space(4);

        // Column headers
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("#", GUILayout.Width(24));
        EditorGUILayout.LabelField("Object Name", GUILayout.MinWidth(160));
        EditorGUILayout.LabelField("Type", GUILayout.Width(60));
        EditorGUILayout.LabelField("Triangles", GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        // Scrollable list
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        for (int i = 0; i < _topObjects.Count; i++)
        {
            var (name, tris, type) = _topObjects[i];

            EditorGUILayout.BeginHorizontal(i % 2 == 0
                ? EditorStyles.helpBox
                : GUIStyle.none);

            EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(24));
            EditorGUILayout.LabelField(name, GUILayout.MinWidth(160));

            var typeColor = type == "Skinned" ? new Color(1f, 0.6f, 0.2f) : Color.cyan;
            var prevC = GUI.color;
            GUI.color = typeColor;
            EditorGUILayout.LabelField(type, GUILayout.Width(60));
            GUI.color = prevC;

            EditorGUILayout.LabelField(tris.ToString("N0"), GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void CountAll(bool countMesh, bool countSkinned)
    {
        _meshFilterTotal = 0;
        _skinnedTotal = 0;
        _topObjects = new List<(string, int, string)>();

        if (countMesh)
        {
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf.sharedMesh == null) continue;
                int tris = mf.sharedMesh.triangles.Length / 3;
                _meshFilterTotal += tris;
                _topObjects.Add((mf.gameObject.name, tris, "Mesh"));
            }
        }

        if (countSkinned)
        {
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (smr.sharedMesh == null) continue;
                int tris = smr.sharedMesh.triangles.Length / 3;
                _skinnedTotal += tris;
                _topObjects.Add((smr.gameObject.name, tris, "Skinned"));
            }
        }

        _grandTotal = _meshFilterTotal + _skinnedTotal;

        _topObjects = _topObjects
            .OrderByDescending(o => o.tris)
            .Take(_topCount)
            .ToList();

        _hasCounted = true;
        Repaint();
    }

    private void DrawSummaryRow(string label, int value, Color color)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(200));
        var prev = GUI.color;
        GUI.color = color;
        EditorGUILayout.LabelField(value.ToString("N0") + " tris", EditorStyles.boldLabel);
        GUI.color = prev;
        EditorGUILayout.EndHorizontal();
    }
}
#endif