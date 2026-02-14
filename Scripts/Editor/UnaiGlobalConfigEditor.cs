using UnAI.Config;
using UnityEditor;
using UnityEngine;

namespace UnAI.Editor
{
    [CustomEditor(typeof(UnaiGlobalConfig))]
    public class UnaiGlobalConfigEditor : UnityEditor.Editor
    {
        private static readonly string[] _providerFields = new[]
        {
            "OpenAI", "Anthropic", "Gemini", "Mistral", "Cohere",
            "Ollama", "LMStudio", "LlamaCpp", "OpenAICompatible"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("UNAI - Universal AI Connector", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // General settings
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DefaultProviderId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DebugLogging"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Cloud Providers", EditorStyles.boldLabel);

            DrawProviderSection("OpenAI");
            DrawProviderSection("Anthropic");
            DrawProviderSection("Gemini");
            DrawProviderSection("Mistral");
            DrawProviderSection("Cohere");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Local Providers", EditorStyles.boldLabel);

            DrawProviderSection("Ollama");
            DrawProviderSection("LMStudio");
            DrawProviderSection("LlamaCpp");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Custom", EditorStyles.boldLabel);

            DrawProviderSection("OpenAICompatible");

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Resolve All API Keys (Debug)"))
            {
                var config = (UnaiGlobalConfig)target;
                foreach (var provider in config.AllProviders())
                {
                    string resolved = provider.ResolvedApiKey;
                    string masked = string.IsNullOrEmpty(resolved) ? "(empty)" : resolved[..System.Math.Min(8, resolved.Length)] + "...";
                    Debug.Log($"[UNAI] {provider.ProviderId}: {masked}");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProviderSection(string fieldName)
        {
            var prop = serializedObject.FindProperty(fieldName);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop, new GUIContent(fieldName), true);
            }
        }
    }
}
