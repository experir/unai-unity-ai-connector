using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnAI.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnAI.Editor.Assistant
{
    public abstract class UnaiEditorTool : IUnaiTool
    {
        public abstract UnaiToolDefinition Definition { get; }

        public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
        {
            try
            {
                var args = call.GetArguments();
                string result = Execute(args);
                return Task.FromResult(new UnaiToolResult { Content = result });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new UnaiToolResult
                {
                    Content = $"Error: {ex.Message}",
                    IsError = true
                });
            }
        }

        protected abstract string Execute(JObject args);
    }

    public class InspectSceneTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "inspect_scene",
            Description = "List all root GameObjects in the active scene with their child counts and active state.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {},
                ""required"": []
            }")
        };

        protected override string Execute(JObject args)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var sb = new StringBuilder();
            sb.AppendLine($"Scene: {scene.name} ({roots.Length} root objects)");

            foreach (var root in roots)
                AppendHierarchy(sb, root, 0, 2);

            return sb.ToString();
        }

        private void AppendHierarchy(StringBuilder sb, GameObject go, int depth, int maxDepth)
        {
            string indent = new string(' ', depth * 2);
            string active = go.activeSelf ? "" : " [inactive]";
            int childCount = go.transform.childCount;
            string children = childCount > 0 ? $" ({childCount} children)" : "";
            sb.AppendLine($"{indent}- {go.name}{active}{children}");

            if (depth < maxDepth)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                    AppendHierarchy(sb, go.transform.GetChild(i).gameObject, depth + 1, maxDepth);
            }
        }
    }

    public class FindGameObjectTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "find_gameobject",
            Description = "Find GameObjects by name, tag, or component type. Returns matching objects with their paths.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name or partial name to search for"" },
                    ""tag"": { ""type"": ""string"", ""description"": ""Tag to filter by"" },
                    ""component"": { ""type"": ""string"", ""description"": ""Component type name to filter by"" }
                }
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = args["name"]?.ToString();
            string tag = args["tag"]?.ToString();
            string component = args["component"]?.ToString();

            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(go => go.scene.isLoaded);

            if (!string.IsNullOrEmpty(name))
                allObjects = allObjects.Where(go =>
                    go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!string.IsNullOrEmpty(tag))
                allObjects = allObjects.Where(go => go.CompareTag(tag));

            if (!string.IsNullOrEmpty(component))
                allObjects = allObjects.Where(go =>
                    go.GetComponents<Component>().Any(c =>
                        c != null && c.GetType().Name.Equals(component, StringComparison.OrdinalIgnoreCase)));

            var results = allObjects.Take(50).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} GameObjects:");

            foreach (var go in results)
            {
                string path = GetGameObjectPath(go);
                sb.AppendLine($"  - {path} [active={go.activeSelf}, tag={go.tag}]");
            }

            return sb.ToString();
        }

        private string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }

    public class CreateGameObjectTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "create_gameobject",
            Description = "Create a new GameObject in the scene with optional position and components. Supports Undo.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name for the new GameObject"" },
                    ""parent"": { ""type"": ""string"", ""description"": ""Name of parent GameObject (optional)"" },
                    ""position"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""x"": { ""type"": ""number"" },
                            ""y"": { ""type"": ""number"" },
                            ""z"": { ""type"": ""number"" }
                        },
                        ""description"": ""World position""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Component type names to add (e.g. ['BoxCollider', 'Rigidbody'])""
                    }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = args["name"]?.ToString() ?? "New GameObject";
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            // Set parent
            string parentName = args["parent"]?.ToString();
            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    Undo.SetTransformParent(go.transform, parent.transform, $"Set parent of {name}");
            }

            // Set position
            var pos = args["position"];
            if (pos != null)
            {
                float x = pos["x"]?.Value<float>() ?? 0;
                float y = pos["y"]?.Value<float>() ?? 0;
                float z = pos["z"]?.Value<float>() ?? 0;
                go.transform.position = new Vector3(x, y, z);
            }

            // Add components
            var components = args["components"] as JArray;
            if (components != null)
            {
                foreach (var comp in components)
                {
                    string typeName = comp.ToString();
                    var type = FindComponentType(typeName);
                    if (type != null)
                        Undo.AddComponent(go, type);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Created GameObject '{go.name}'");
            sb.AppendLine($"  Position: {go.transform.position}");
            sb.AppendLine($"  Components: {string.Join(", ", go.GetComponents<Component>().Select(c => c.GetType().Name))}");
            return sb.ToString();
        }

        private Type FindComponentType(string name)
        {
            // Search common Unity component types
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                var type = asm.GetTypes().FirstOrDefault(t =>
                    t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    typeof(Component).IsAssignableFrom(t));
                if (type != null) return type;
            }
            return null;
        }
    }

    public class InspectGameObjectTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "inspect_gameobject",
            Description = "Get detailed info about a GameObject: transform, all components, and their serialized properties.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name or path of the GameObject to inspect"" }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = args["name"]?.ToString();
            var go = GameObject.Find(name);
            if (go == null)
            {
                // Try searching all loaded objects
                go = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g.scene.isLoaded && g.name == name);
            }
            if (go == null) return $"GameObject '{name}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {go.name}");
            sb.AppendLine($"  Active: {go.activeSelf} (in hierarchy: {go.activeInHierarchy})");
            sb.AppendLine($"  Tag: {go.tag}  Layer: {LayerMask.LayerToName(go.layer)}");
            sb.AppendLine($"  Position: {go.transform.position}");
            sb.AppendLine($"  Rotation: {go.transform.eulerAngles}");
            sb.AppendLine($"  Scale: {go.transform.localScale}");
            sb.AppendLine($"  Children: {go.transform.childCount}");
            sb.AppendLine();

            var components = go.GetComponents<Component>();
            sb.AppendLine($"Components ({components.Length}):");
            foreach (var comp in components)
            {
                if (comp == null) continue;
                sb.AppendLine($"  [{comp.GetType().Name}]");

                var so = new SerializedObject(comp);
                var prop = so.GetIterator();
                if (prop.NextVisible(true))
                {
                    int count = 0;
                    do
                    {
                        if (prop.name == "m_Script") continue;
                        sb.AppendLine($"    {prop.displayName}: {GetPropertyValue(prop)}");
                        count++;
                    } while (prop.NextVisible(false) && count < 20);
                }
            }

            return sb.ToString();
        }

        private string GetPropertyValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Float => prop.floatValue.ToString("F3"),
                SerializedPropertyType.Boolean => prop.boolValue.ToString(),
                SerializedPropertyType.String => $"\"{prop.stringValue}\"",
                SerializedPropertyType.Enum => prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString(),
                SerializedPropertyType.Vector2 => prop.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                SerializedPropertyType.Color => prop.colorValue.ToString(),
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? prop.objectReferenceValue.name
                    : "None",
                _ => $"({prop.propertyType})"
            };
        }
    }

    public class ReadScriptTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "read_script",
            Description = "Read the contents of a C# script file from the project Assets folder.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""path"": { ""type"": ""string"", ""description"": ""Asset path (e.g. 'Assets/Scripts/Player.cs')"" }
                },
                ""required"": [""path""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string path = args["path"]?.ToString();
            if (string.IsNullOrEmpty(path)) return "Error: 'path' is required.";

            string fullPath = path.StartsWith("Assets")
                ? System.IO.Path.Combine(Application.dataPath, "..", path)
                : System.IO.Path.Combine(Application.dataPath, path);

            fullPath = System.IO.Path.GetFullPath(fullPath);

            if (!fullPath.StartsWith(System.IO.Path.GetFullPath(Application.dataPath)))
                return "Error: Path must be within the Assets folder.";

            if (!System.IO.File.Exists(fullPath))
                return $"Error: File not found at '{path}'.";

            string content = System.IO.File.ReadAllText(fullPath);
            if (content.Length > 8000)
                content = content.Substring(0, 8000) + "\n... (truncated)";

            return $"File: {path}\n```csharp\n{content}\n```";
        }
    }

    public class ListAssetsTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "list_assets",
            Description = "List asset files in a project folder. Returns file names and types.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""folder"": { ""type"": ""string"", ""description"": ""Asset folder path (e.g. 'Assets/Prefabs'). Defaults to 'Assets'."" },
                    ""filter"": { ""type"": ""string"", ""description"": ""Type filter: 'scripts', 'prefabs', 'materials', 'textures', 'scenes', or file extension like '.cs'"" }
                }
            }")
        };

        protected override string Execute(JObject args)
        {
            string folder = args["folder"]?.ToString() ?? "Assets";
            string filter = args["filter"]?.ToString();

            string[] guids = string.IsNullOrEmpty(filter)
                ? AssetDatabase.FindAssets("", new[] { folder })
                : AssetDatabase.FindAssets($"t:{MapFilter(filter)}", new[] { folder });

            var sb = new StringBuilder();
            sb.AppendLine($"Assets in '{folder}'" + (filter != null ? $" (filter: {filter})" : "") + ":");

            int count = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Only show direct children and assets, not sub-folders' contents beyond limit
                if (count >= 100)
                {
                    sb.AppendLine($"  ... and {guids.Length - count} more");
                    break;
                }
                sb.AppendLine($"  {path}");
                count++;
            }

            if (count == 0)
                sb.AppendLine("  (empty)");

            return sb.ToString();
        }

        private string MapFilter(string filter)
        {
            return filter?.ToLower() switch
            {
                "scripts" => "Script",
                "prefabs" => "Prefab",
                "materials" => "Material",
                "textures" => "Texture",
                "scenes" => "Scene",
                "meshes" => "Mesh",
                "audio" => "AudioClip",
                _ => filter
            };
        }
    }

    public class GetSelectionTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "get_selection",
            Description = "Get the currently selected objects in the Unity Editor.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {},
                ""required"": []
            }")
        };

        protected override string Execute(JObject args)
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
                return "No objects selected.";

            var sb = new StringBuilder();
            sb.AppendLine($"Selected ({objects.Length} objects):");

            foreach (var obj in objects)
            {
                if (obj is GameObject go)
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                    {
                        // Scene object
                        var comps = go.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => c.GetType().Name);
                        sb.AppendLine($"  [Scene] {go.name} - Components: {string.Join(", ", comps)}");
                    }
                    else
                    {
                        sb.AppendLine($"  [Asset] {path}");
                    }
                }
                else
                {
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    sb.AppendLine($"  [{obj.GetType().Name}] {(string.IsNullOrEmpty(assetPath) ? obj.name : assetPath)}");
                }
            }

            return sb.ToString();
        }
    }

    public class LogMessageTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "log_message",
            Description = "Write a message to the Unity Console. Useful for confirming actions or providing output.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""message"": { ""type"": ""string"", ""description"": ""Message to log"" },
                    ""level"": { ""type"": ""string"", ""enum"": [""info"", ""warning"", ""error""], ""description"": ""Log level (default: info)"" }
                },
                ""required"": [""message""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string message = args["message"]?.ToString() ?? "";
            string level = args["level"]?.ToString() ?? "info";

            string prefix = "[UNAI Assistant] ";
            switch (level.ToLower())
            {
                case "warning":
                    Debug.LogWarning(prefix + message);
                    break;
                case "error":
                    Debug.LogError(prefix + message);
                    break;
                default:
                    Debug.Log(prefix + message);
                    break;
            }

            return $"Logged ({level}): {message}";
        }
    }

    public static class UnaiAssistantToolsFactory
    {
        public static UnaiToolRegistry CreateEditorToolRegistry()
        {
            var registry = new UnaiToolRegistry();
            registry.Register(new InspectSceneTool());
            registry.Register(new FindGameObjectTool());
            registry.Register(new CreateGameObjectTool());
            registry.Register(new InspectGameObjectTool());
            registry.Register(new ReadScriptTool());
            registry.Register(new ListAssetsTool());
            registry.Register(new GetSelectionTool());
            registry.Register(new LogMessageTool());
            return registry;
        }
    }
}
