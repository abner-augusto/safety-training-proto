#nullable enable
using SafetyProto.Runtime.Actions;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    [CustomPropertyDrawer(typeof(ActionIdAttribute))]
    public sealed class ActionIdPropertyDrawer : PropertyDrawer
    {
        private const float PopupWidth = 190f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            position = EditorGUI.PrefixLabel(position, label);
            var popupRect = new Rect(position.x, position.y, Mathf.Min(PopupWidth, position.width), position.height);
            var textRect = new Rect(popupRect.xMax + 4f, position.y, Mathf.Max(0f, position.width - popupRect.width - 4f), position.height);

            var ids = ActionCatalogEditorUtility.LoadActionIds();
            var options = new string[ids.Count + 2];
            options[0] = "— vazio —";
            for (var i = 0; i < ids.Count; i++) options[i + 1] = ids[i];
            options[options.Length - 1] = "Texto livre";

            var current = property.stringValue?.Trim() ?? string.Empty;
            var selected = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], current, System.StringComparison.OrdinalIgnoreCase))
                {
                    selected = i + 1;
                    break;
                }
            }

            if (selected == 0 && !string.IsNullOrWhiteSpace(current)) selected = options.Length - 1;

            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Popup(popupRect, selected, options);
            if (EditorGUI.EndChangeCheck() && next < options.Length - 1)
            {
                property.stringValue = next == 0 ? string.Empty : options[next];
            }

            EditorGUI.BeginChangeCheck();
            var typed = EditorGUI.TextField(textRect, property.stringValue ?? string.Empty);
            if (EditorGUI.EndChangeCheck()) property.stringValue = typed.Trim();
        }
    }
}
