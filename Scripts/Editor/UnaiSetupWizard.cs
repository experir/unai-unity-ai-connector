using UnAI.Config;
using UnAI.Core;
using UnityEditor;
using UnityEngine;

namespace UnAI.Editor
{
    public class UnaiSetupWizard : EditorWindow
    {
        private UnaiGlobalConfig _config;

        [MenuItem("Window/UnAI/Setup Wizard")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnaiSetupWizard>("UNAI Setup");
            window.minSize = new Vector2(400, 300);
        }

        [MenuItem("Window/UnAI/Create Global Config")]
        public static void CreateConfig()
        {
            var config = CreateInstance<UnaiGlobalConfig>();
            string path = "Assets/UnAI/UnaiGlobalConfig.asset";

            if (!AssetDatabase.IsValidFolder("Assets/UnAI"))
                AssetDatabase.CreateFolder("Assets", "UnAI");

            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            EditorUtility.FocusProjectWindow();
            Debug.Log($"[UNAI] Created config at {path}");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UNAI Setup Wizard", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "Welcome to UNAI - Universal AI Connector.\n\n" +
                "Follow these steps to get started:\n" +
                "1. Create or assign a Global Configuration asset\n" +
                "2. Configure at least one AI provider\n" +
                "3. Add UnaiManager to a GameObject in your scene",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Step 1: Config
            EditorGUILayout.LabelField("Step 1: Configuration", EditorStyles.boldLabel);

            _config = (UnaiGlobalConfig)EditorGUILayout.ObjectField(
                "Global Config", _config, typeof(UnaiGlobalConfig), false);

            if (_config == null)
            {
                if (GUILayout.Button("Create New Config Asset"))
                {
                    CreateConfig();
                    string[] guids = AssetDatabase.FindAssets("t:UnaiGlobalConfig");
                    if (guids.Length > 0)
                        _config = AssetDatabase.LoadAssetAtPath<UnaiGlobalConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Config found. Click below to edit it.", MessageType.None);
                if (GUILayout.Button("Select Config in Inspector"))
                {
                    Selection.activeObject = _config;
                }
            }

            EditorGUILayout.Space(8);

            // Step 2: Scene setup
            EditorGUILayout.LabelField("Step 2: Scene Setup", EditorStyles.boldLabel);

            var existingManager = FindAnyObjectByType<UnaiManager>();
            if (existingManager != null)
            {
                EditorGUILayout.HelpBox("UnaiManager found in scene.", MessageType.Info);
                if (GUILayout.Button("Select UnaiManager"))
                    Selection.activeGameObject = existingManager.gameObject;
            }
            else
            {
                if (_config != null && GUILayout.Button("Add UnaiManager to Scene"))
                {
                    var go = new GameObject("UnaiManager");
                    var manager = go.AddComponent<UnaiManager>();

                    var so = new SerializedObject(manager);
                    so.FindProperty("_config").objectReferenceValue = _config;
                    so.ApplyModifiedProperties();

                    Selection.activeGameObject = go;
                    Debug.Log("[UNAI] Added UnaiManager to scene with config assigned.");
                }
                else if (_config == null)
                {
                    EditorGUILayout.HelpBox("Create a config first before adding UnaiManager.", MessageType.Warning);
                }
            }
        }

        private void OnEnable()
        {
            string[] guids = AssetDatabase.FindAssets("t:UnaiGlobalConfig");
            if (guids.Length > 0)
                _config = AssetDatabase.LoadAssetAtPath<UnaiGlobalConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
