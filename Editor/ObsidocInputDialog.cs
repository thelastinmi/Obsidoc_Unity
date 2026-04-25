using System;
using UnityEditor;
using UnityEngine;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Floating single-line input dialog used by ObsidocWindow for create and rename operations.
    /// Call <see cref="Show"/> and provide a callback; the callback receives the trimmed input on OK.
    /// </summary>
    public class ObsidocInputDialog : EditorWindow
    {
        private string         _label;
        private string         _input;
        private Action<string> _onConfirm;
        private bool           _focusSet;

        public static void Show(string title, string label, string defaultValue, Action<string> onConfirm)
        {
            var w = CreateInstance<ObsidocInputDialog>();
            w.titleContent = new GUIContent(title);
            w._label       = label;
            w._input       = defaultValue ?? string.Empty;
            w._onConfirm   = onConfirm;
            w._focusSet    = false;
            w.minSize      = new Vector2(300, 88);
            w.maxSize      = new Vector2(300, 88);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_label);

            GUI.SetNextControlName("InputField");
            _input = EditorGUILayout.TextField(_input);

            if (!_focusSet)
            {
                EditorGUI.FocusTextInControl("InputField");
                _focusSet = true;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(72)))
                Close();

            bool confirm = GUILayout.Button("OK", GUILayout.Width(72));

            if (!confirm && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                confirm = true;
                Event.current.Use();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                Close();

            if (confirm && !string.IsNullOrWhiteSpace(_input))
            {
                _onConfirm?.Invoke(_input.Trim());
                Close();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }
    }
}
