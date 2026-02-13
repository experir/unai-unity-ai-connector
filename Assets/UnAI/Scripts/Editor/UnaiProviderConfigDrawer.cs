using UnAI.Config;
using UnityEditor;
using UnityEngine;

namespace UnAI.Editor
{
    [CustomPropertyDrawer(typeof(UnaiProviderConfig))]
    public class UnaiProviderConfigDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
                property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = position.y + EditorGUIUtility.singleLineHeight + 2;
                float lineHeight = EditorGUIUtility.singleLineHeight + 2;

                var enabledProp = property.FindPropertyRelative("Enabled");
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight), enabledProp);
                y += lineHeight;

                if (enabledProp.boolValue)
                {
                    DrawField(ref y, position, property, "BaseUrl", lineHeight);
                    DrawField(ref y, position, property, "ApiKeyEnvironmentVariable", lineHeight);

                    // API key as password field
                    var apiKeyProp = property.FindPropertyRelative("ApiKey");
                    var apiKeyRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
                    apiKeyProp.stringValue = EditorGUI.PasswordField(apiKeyRect, "Api Key (Editor Only)", apiKeyProp.stringValue);
                    y += lineHeight;

                    DrawField(ref y, position, property, "DefaultModel", lineHeight);
                    DrawField(ref y, position, property, "TimeoutSeconds", lineHeight);
                    DrawField(ref y, position, property, "MaxRetries", lineHeight);
                    DrawField(ref y, position, property, "CustomHeaders", lineHeight);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        private void DrawField(ref float y, Rect position, SerializedProperty parent, string fieldName, float lineHeight)
        {
            var prop = parent.FindPropertyRelative(fieldName);
            if (prop != null)
            {
                float height = EditorGUI.GetPropertyHeight(prop, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), prop, true);
                y += height + 2;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            float height = EditorGUIUtility.singleLineHeight + 4; // foldout
            float lineHeight = EditorGUIUtility.singleLineHeight + 2;

            height += lineHeight; // Enabled

            var enabledProp = property.FindPropertyRelative("Enabled");
            if (enabledProp != null && enabledProp.boolValue)
            {
                height += lineHeight; // BaseUrl
                height += lineHeight; // ApiKeyEnvironmentVariable
                height += lineHeight; // ApiKey
                height += lineHeight; // DefaultModel
                height += lineHeight; // TimeoutSeconds
                height += lineHeight; // MaxRetries

                var customHeaders = property.FindPropertyRelative("CustomHeaders");
                if (customHeaders != null)
                    height += EditorGUI.GetPropertyHeight(customHeaders, true) + 2;
            }

            return height;
        }
    }
}
