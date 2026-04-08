// Place this file inside any folder named "Editor" in your project.
// e.g.  Assets/CollectionInstanceArray/Editor/CollectionInstanceArrayEditor.cs

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SafetyProto.Utils;

[CustomEditor(typeof(CollectionInstanceArray))]
public class CollectionInstanceArrayEditor : Editor
{
    // Serialized property handles (wired up in OnEnable)
    SerializedProperty _sourcePrefab;
    SerializedProperty _count;
    SerializedProperty _axis;
    SerializedProperty _offset;
    SerializedProperty _relativeOffset;

    // Cached values to detect changes
    int    _prevCount;
    float  _prevOffset;
    int    _prevAxis;          // enum stored as int
    bool   _prevRelative;
    Object _prevPrefab;

    // Drag state for the custom count control
    bool  _isDragging;
    float _dragStartX;
    int   _dragStartCount;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    void OnEnable()
    {
        _sourcePrefab   = serializedObject.FindProperty("sourcePrefab");
        _count          = serializedObject.FindProperty("count");
        _axis           = serializedObject.FindProperty("axis");
        _offset         = serializedObject.FindProperty("offset");
        _relativeOffset = serializedObject.FindProperty("relativeOffset");

        CacheValues();
    }

    void CacheValues()
    {
        _prevCount    = _count.intValue;
        _prevOffset   = _offset.floatValue;
        _prevAxis     = _axis.enumValueIndex;
        _prevRelative = _relativeOffset.boolValue;
        _prevPrefab   = _sourcePrefab.objectReferenceValue;
    }

    bool ValuesChanged() =>
        _count.intValue          != _prevCount    ||
        _offset.floatValue       != _prevOffset   ||
        _axis.enumValueIndex     != _prevAxis     ||
        _relativeOffset.boolValue!= _prevRelative ||
        _sourcePrefab.objectReferenceValue != _prevPrefab;

    // -----------------------------------------------------------------------
    // Inspector draw
    // -----------------------------------------------------------------------

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var arr = (CollectionInstanceArray)target;

        // ---- Header -------------------------------------------------------
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 13,
            alignment = TextAnchor.MiddleLeft,
        };
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("⬛  Collection Instance Array", headerStyle);
        DrawSeparator();

        // ---- Source -------------------------------------------------------
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_sourcePrefab, new GUIContent("Prefab / Source"));

        EditorGUILayout.Space(6);
        DrawSeparator();

        // ---- Array Parameters --------------------------------------------
        EditorGUILayout.LabelField("Array Parameters", EditorStyles.boldLabel);

        // Axis — toolbar style (matches Blender expand=True)
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Axis", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
        int newAxis = GUILayout.Toolbar(_axis.enumValueIndex, new[] { "X", "Y", "Z" });
        if (newAxis != _axis.enumValueIndex)
            _axis.enumValueIndex = newAxis;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Count — slider + drag button + step buttons
        DrawCountControl();

        EditorGUILayout.Space(4);

        // Offset
        EditorGUILayout.PropertyField(_offset, new GUIContent("Offset"));

        // Relative offset toggle
        EditorGUILayout.PropertyField(_relativeOffset, new GUIContent("Relative Offset",
            "Multiply offset by the prefab's bounds size on the chosen axis."));

        EditorGUILayout.Space(6);
        DrawSeparator();

        // ---- Actions ------------------------------------------------------
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        GUI.enabled = arr.sourcePrefab != null;
        if (GUILayout.Button("↺  Rebuild Array"))
        {
            serializedObject.ApplyModifiedProperties();
            RebuildArray(arr);
            CacheValues();
            return;
        }
        GUI.enabled = true;

        GUI.color = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("✕  Clear Array"))
        {
            Undo.RecordObject(arr, "Clear Array");
            ClearInstances(arr);
        }
        GUI.color = Color.white;

        EditorGUILayout.EndHorizontal();

        // ---- Info ---------------------------------------------------------
        if (arr.instances.Count > 0)
        {
            EditorGUILayout.Space(4);
            DrawSeparator();
            EditorGUILayout.LabelField("Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Instances in scene: {arr.instances.Count}",
                EditorStyles.miniLabel);

            float step = arr.GetEffectiveOffset();
            float total = step * Mathf.Max(0, arr.instances.Count - 1);
            EditorGUILayout.LabelField($"Total span: {total:F2} units", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(4);

        // ---- Apply & auto-rebuild ----------------------------------------
        serializedObject.ApplyModifiedProperties();

        // Ensure the list is never null (e.g. after domain reload)
        if (arr.instances == null)
            arr.instances = new List<GameObject>();

        if (ValuesChanged() && arr.sourcePrefab != null)
        {
            RebuildArray(arr);
            CacheValues();
        }
    }

    // -----------------------------------------------------------------------
    // Custom Count Control
    // -----------------------------------------------------------------------

    void DrawCountControl()
    {
        Rect fullRect = EditorGUILayout.GetControlRect();

        // Label
        float labelW = EditorGUIUtility.labelWidth;
        Rect  labelR = new Rect(fullRect.x, fullRect.y, labelW, fullRect.height);
        EditorGUI.LabelField(labelR, new GUIContent("Count"));

        float btnW    = 22f;
        float dragW   = 24f; // This line can be removed
        float spacing = 2f;
        float controlsW = dragW + spacing + btnW + spacing + btnW; // Update this line

        // Slider occupies the space left of the right-side buttons
        Rect sliderR = new Rect(
            fullRect.x + labelW,
            fullRect.y,
            fullRect.width - labelW - controlsW - spacing,
            fullRect.height
        );

        int newCount = EditorGUI.IntSlider(sliderR, _count.intValue, 1, 50);
        if (newCount != _count.intValue)
            _count.intValue = newCount;

        float btnX = sliderR.xMax + spacing;

/*         // ── Drag button (≡) ─────────────────────────────────────────────
        Rect dragR = new Rect(btnX, fullRect.y, dragW, fullRect.height); // This line can be removed
        GUIContent dragContent = new GUIContent("≡", "Drag horizontally to adjust count"); // This line can be removed
        GUI.Button(dragR, dragContent);   // visual only; input handled below // This line can be removed

        Event e = Event.current;
        if (dragR.Contains(e.mousePosition))
        {
            EditorGUIUtility.AddCursorRect(dragR, MouseCursor.ResizeHorizontal);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _isDragging    = true;
                _dragStartX    = e.mousePosition.x;
                _dragStartCount = _count.intValue;
                e.Use();
            }
        }

        if (_isDragging)
        {
            EditorGUIUtility.AddCursorRect(new Rect(0, 0, 9999, 9999), MouseCursor.ResizeHorizontal);

            if (e.type == EventType.MouseDrag)
            {
                float delta = e.mousePosition.x - _dragStartX;
                int   val   = Mathf.Clamp(Mathf.RoundToInt(_dragStartCount + delta / 8f), 1, 256);
                _count.intValue = val;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp)
            {
                _isDragging = false;
                e.Use();
            }
        }

        btnX += dragW + spacing; // Update this line */

        // ── Minus button ────────────────────────────────────────────────
        Rect minusR = new Rect(btnX, fullRect.y, btnW, fullRect.height);
        if (GUI.Button(minusR, new GUIContent("−", "Decrease count by 1")))
            _count.intValue = Mathf.Max(1, _count.intValue - 1);

        btnX += btnW + spacing;

        // ── Plus button ─────────────────────────────────────────────────
        Rect plusR = new Rect(btnX, fullRect.y, btnW, fullRect.height);
        if (GUI.Button(plusR, new GUIContent("+", "Increase count by 1")))
            _count.intValue = Mathf.Min(256, _count.intValue + 1);
    }

    // -----------------------------------------------------------------------
    // Array build / clear
    // -----------------------------------------------------------------------

    static void RebuildArray(CollectionInstanceArray arr)
    {
        if (arr == null || arr.sourcePrefab == null) return;

        Undo.RecordObject(arr, "Rebuild Array");
        ClearInstances(arr);

        Vector3 axisVec    = arr.GetAxisVector();
        float   stepOffset = arr.GetEffectiveOffset();

        // Determine whether the source is a project asset or a scene object.
        // PrefabUtility.InstantiatePrefab only works for project assets.
        bool isProjectAsset = EditorUtility.IsPersistent(arr.sourcePrefab);

        for (int i = 0; i < arr.count; i++)
        {
            Vector3 localPos = axisVec * stepOffset * i;

            GameObject inst;

            if (isProjectAsset)
            {
                // Keeps the prefab link intact
                inst = (GameObject)PrefabUtility.InstantiatePrefab(
                    arr.sourcePrefab, arr.transform
                );
            }
            else
            {
                // Source is a scene object — plain instantiate under the root
                inst = Instantiate(arr.sourcePrefab, arr.transform);
            }

            if (inst == null)
            {
                Debug.LogError("[CollectionInstanceArray] Failed to instantiate source prefab.");
                continue;
            }

            inst.name                    = $"{arr.sourcePrefab.name}_Array_{i:000}";
            inst.transform.localPosition = localPos;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale    = Vector3.one;

            Undo.RegisterCreatedObjectUndo(inst, "Array Instance");
            arr.instances.Add(inst);
        }

        EditorUtility.SetDirty(arr);
    }

    static void ClearInstances(CollectionInstanceArray arr)
    {
        if (arr == null) return;

        if (arr.instances == null)
        {
            arr.instances = new List<GameObject>();
            return;
        }

        foreach (var inst in arr.instances)
        {
            if (inst != null)
                Undo.DestroyObjectImmediate(inst);
        }
        arr.instances.Clear();
        EditorUtility.SetDirty(arr);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    static void DrawSeparator()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        EditorGUILayout.Space(2);
    }
}
