using UnityEngine;
using UnityEngine.U2D.Aseprite;

namespace UnityEditor.U2D.Aseprite
{
    [CustomEditor(typeof(ImageSpriteLayout)), CanEditMultipleObjects]
    internal class ImageSpriteLayoutEditor : Editor
    {
        private static readonly GUIContent PivotLabel = new("Sprite Pivot");
        private static readonly GUIContent NativeSizeLabel = new("Native Size");
        private static readonly GUIContent MultiplierLabel = new("Size Multiplier");
        private static readonly GUIContent XLabel = new("X");
        private static readonly GUIContent YLabel = new("Y");

        private SerializedProperty _pivotX;
        private SerializedProperty _pivotY;
        private SerializedProperty _nativeSizeX;
        private SerializedProperty _nativeSizeY;
        private SerializedProperty _nativeSizeMultiplier;

        private void OnEnable()
        {
            _pivotX = serializedObject.FindProperty("useSpritePivotX");
            _pivotY = serializedObject.FindProperty("useSpritePivotY");
            _nativeSizeX = serializedObject.FindProperty("useNativeSizeX");
            _nativeSizeY = serializedObject.FindProperty("useNativeSizeY");
            _nativeSizeMultiplier = serializedObject.FindProperty("nativeSizeMultiplier");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            AxisToggles(PivotLabel, _pivotX, _pivotY);
            AxisToggles(NativeSizeLabel, _nativeSizeX, _nativeSizeY);
            using (new EditorGUI.DisabledScope(IsOff(_nativeSizeX) && IsOff(_nativeSizeY)))
                EditorGUILayout.PropertyField(_nativeSizeMultiplier, MultiplierLabel);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool IsOff(SerializedProperty toggle) => !toggle.boolValue && !toggle.hasMultipleDifferentValues;

        private static void AxisToggles(GUIContent label, SerializedProperty x, SerializedProperty y)
        {
            Rect fields = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float half = fields.width / 2f;
            AxisToggle(new Rect(fields.x, fields.y, half, fields.height), XLabel, x);
            AxisToggle(new Rect(fields.x + half, fields.y, half, fields.height), YLabel, y);

            EditorGUI.indentLevel = indent;
        }

        private static void AxisToggle(Rect rect, GUIContent label, SerializedProperty property)
        {
            using var scope = new EditorGUI.PropertyScope(rect, label, property);
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUI.ToggleLeft(rect, scope.content, property.boolValue);
            if (EditorGUI.EndChangeCheck()) property.boolValue = value;
        }
    }
}
