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

        /// <summary>
        /// Get a parameter value, trying the primary key first, then fallback aliases.
        /// Handles JTokenType.Null gracefully.
        /// </summary>
        protected static string GetString(JObject args, params string[] keys)
        {
            foreach (var key in keys)
            {
                var token = args[key];
                if (token != null && token.Type != JTokenType.Null)
                    return token.ToString();
            }
            return null;
        }

        protected static JToken GetToken(JObject args, params string[] keys)
        {
            foreach (var key in keys)
            {
                var token = args[key];
                if (token != null && token.Type != JTokenType.Null)
                    return token;
            }
            return null;
        }
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
            Description = "Create a new GameObject in the scene. Use 'primitive' for visible objects (Cube, Sphere, Capsule, Cylinder, Plane, Quad). Supports Undo.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name for the new GameObject"" },
                    ""primitive"": { ""type"": ""string"", ""enum"": [""Cube"", ""Sphere"", ""Capsule"", ""Cylinder"", ""Plane"", ""Quad""], ""description"": ""Create as a primitive with mesh and collider. Use this for any visible object."" },
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
                    ""scale"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""x"": { ""type"": ""number"" },
                            ""y"": { ""type"": ""number"" },
                            ""z"": { ""type"": ""number"" }
                        },
                        ""description"": ""Local scale""
                    },
                    ""components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Additional component type names to add (e.g. ['Rigidbody'])""
                    }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name", "gameObject", "gameobject", "object") ?? "New GameObject";
            string primitive = GetString(args, "primitive", "type", "shape");

            GameObject go;
            if (!string.IsNullOrEmpty(primitive) && Enum.TryParse<PrimitiveType>(primitive, true, out var primType))
            {
                go = GameObject.CreatePrimitive(primType);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            // Set parent
            var parentToken = args["parent"];
            if (parentToken != null && parentToken.Type != JTokenType.Null)
            {
                string parentName = parentToken.ToString();
                if (!string.IsNullOrEmpty(parentName))
                {
                    var parent = GameObject.Find(parentName);
                    if (parent != null)
                        Undo.SetTransformParent(go.transform, parent.transform, $"Set parent of {name}");
                }
            }

            // Set position
            var pos = args["position"];
            if (pos != null && pos.Type == JTokenType.Object)
            {
                float x = pos["x"]?.Value<float>() ?? 0;
                float y = pos["y"]?.Value<float>() ?? 0;
                float z = pos["z"]?.Value<float>() ?? 0;
                go.transform.position = new Vector3(x, y, z);
            }

            // Set scale
            var scale = args["scale"];
            if (scale != null && scale.Type == JTokenType.Object)
            {
                float sx = scale["x"]?.Value<float>() ?? 1;
                float sy = scale["y"]?.Value<float>() ?? 1;
                float sz = scale["z"]?.Value<float>() ?? 1;
                go.transform.localScale = new Vector3(sx, sy, sz);
            }

            // Add components
            var components = args["components"] as JArray;
            if (components != null && components.Type != JTokenType.Null)
            {
                foreach (var comp in components)
                {
                    string typeName = comp.ToString();
                    var type = FindComponentType(typeName);
                    if (type != null && go.GetComponent(type) == null)
                        Undo.AddComponent(go, type);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Created GameObject '{go.name}'");
            if (!string.IsNullOrEmpty(primitive)) sb.AppendLine($"  Primitive: {primitive}");
            sb.AppendLine($"  Position: {go.transform.position}");
            sb.AppendLine($"  Scale: {go.transform.localScale}");
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

    public class ModifyGameObjectTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "modify_gameobject",
            Description = "Modify an existing GameObject in the scene: move, rotate, scale, rename, " +
                          "add/remove components, set parent, set tag, set active state, or delete it. Supports Undo.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name or path of the GameObject to modify"" },
                    ""rename"": { ""type"": ""string"", ""description"": ""New name for the GameObject"" },
                    ""position"": {
                        ""type"": ""object"",
                        ""properties"": { ""x"": { ""type"": ""number"" }, ""y"": { ""type"": ""number"" }, ""z"": { ""type"": ""number"" } },
                        ""description"": ""Set world position""
                    },
                    ""rotation"": {
                        ""type"": ""object"",
                        ""properties"": { ""x"": { ""type"": ""number"" }, ""y"": { ""type"": ""number"" }, ""z"": { ""type"": ""number"" } },
                        ""description"": ""Set euler rotation""
                    },
                    ""scale"": {
                        ""type"": ""object"",
                        ""properties"": { ""x"": { ""type"": ""number"" }, ""y"": { ""type"": ""number"" }, ""z"": { ""type"": ""number"" } },
                        ""description"": ""Set local scale""
                    },
                    ""parent"": { ""type"": ""string"", ""description"": ""Name of new parent (empty string to unparent)"" },
                    ""tag"": { ""type"": ""string"", ""description"": ""Set tag (e.g. 'Player', 'MainCamera')"" },
                    ""active"": { ""type"": ""boolean"", ""description"": ""Set active/inactive"" },
                    ""add_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Component types to add (e.g. ['Rigidbody', 'BoxCollider', 'Camera'])""
                    },
                    ""remove_components"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""Component types to remove""
                    },
                    ""delete"": { ""type"": ""boolean"", ""description"": ""If true, delete this GameObject entirely"" }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name", "gameObject", "gameobject", "object", "target", "parent_name", "parentName");
            if (string.IsNullOrEmpty(name)) return "Error: 'name' is required. Specify the name of the GameObject to modify. " +
                "Example: {\"name\": \"MyObject\", \"add_components\": [\"Camera\"]}";

            var go = GameObject.Find(name);
            if (go == null)
            {
                go = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g.scene.isLoaded && g.name == name);
            }
            if (go == null) return $"Error: GameObject '{name}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Modified '{go.name}':");

            Undo.RecordObject(go.transform, $"Modify {go.name}");
            Undo.RecordObject(go, $"Modify {go.name}");

            // Delete
            if (args["delete"]?.Value<bool>() == true)
            {
                Undo.DestroyObjectImmediate(go);
                return $"Deleted GameObject '{name}'.";
            }

            // Rename
            var renameToken = args["rename"];
            if (renameToken != null && renameToken.Type != JTokenType.Null)
            {
                string newName = renameToken.ToString();
                go.name = newName;
                sb.AppendLine($"  Renamed to: {newName}");
            }

            // Position
            var pos = args["position"];
            if (pos != null && pos.Type == JTokenType.Object)
            {
                float x = pos["x"]?.Value<float>() ?? go.transform.position.x;
                float y = pos["y"]?.Value<float>() ?? go.transform.position.y;
                float z = pos["z"]?.Value<float>() ?? go.transform.position.z;
                go.transform.position = new Vector3(x, y, z);
                sb.AppendLine($"  Position: {go.transform.position}");
            }

            // Rotation
            var rot = args["rotation"];
            if (rot != null && rot.Type == JTokenType.Object)
            {
                float rx = rot["x"]?.Value<float>() ?? 0;
                float ry = rot["y"]?.Value<float>() ?? 0;
                float rz = rot["z"]?.Value<float>() ?? 0;
                go.transform.eulerAngles = new Vector3(rx, ry, rz);
                sb.AppendLine($"  Rotation: {go.transform.eulerAngles}");
            }

            // Scale
            var scale = args["scale"];
            if (scale != null && scale.Type == JTokenType.Object)
            {
                float sx = scale["x"]?.Value<float>() ?? go.transform.localScale.x;
                float sy = scale["y"]?.Value<float>() ?? go.transform.localScale.y;
                float sz = scale["z"]?.Value<float>() ?? go.transform.localScale.z;
                go.transform.localScale = new Vector3(sx, sy, sz);
                sb.AppendLine($"  Scale: {go.transform.localScale}");
            }

            // Parent
            var parentToken = args["parent"];
            if (parentToken != null && parentToken.Type != JTokenType.Null)
            {
                string parentName = parentToken.ToString();
                if (string.IsNullOrEmpty(parentName))
                {
                    Undo.SetTransformParent(go.transform, null, $"Unparent {go.name}");
                    sb.AppendLine("  Unparented (moved to root)");
                }
                else
                {
                    var parent = GameObject.Find(parentName);
                    if (parent != null)
                    {
                        Undo.SetTransformParent(go.transform, parent.transform, $"Set parent of {go.name}");
                        sb.AppendLine($"  Parent: {parentName}");
                    }
                    else
                    {
                        sb.AppendLine($"  Warning: Parent '{parentName}' not found");
                    }
                }
            }

            // Tag
            var tagToken = args["tag"];
            if (tagToken != null && tagToken.Type != JTokenType.Null)
            {
                try
                {
                    go.tag = tagToken.ToString();
                    sb.AppendLine($"  Tag: {go.tag}");
                }
                catch { sb.AppendLine($"  Warning: Tag '{tagToken}' is not defined"); }
            }

            // Active
            var activeToken = args["active"];
            if (activeToken != null && activeToken.Type != JTokenType.Null)
            {
                go.SetActive(activeToken.Value<bool>());
                sb.AppendLine($"  Active: {go.activeSelf}");
            }

            // Add components (accept multiple parameter names)
            var addComps = (GetToken(args, "add_components", "addComponents", "components", "component", "component_additions", "componentAdditions") as JArray)
                ?? (args["add_components"] as JArray) ?? (args["components"] as JArray) ?? (args["component_additions"] as JArray);
            // If a single string component was passed, wrap it in an array
            if (addComps == null)
            {
                var singleComp = GetString(args, "add_components", "addComponents", "components", "component", "component_type", "component_additions");
                if (!string.IsNullOrEmpty(singleComp))
                    addComps = new JArray(singleComp);
            }
            if (addComps != null && addComps.Count > 0)
            {
                foreach (var comp in addComps)
                {
                    string typeName = comp.ToString();
                    var type = FindComponentType(typeName);
                    if (type != null)
                    {
                        if (go.GetComponent(type) == null)
                        {
                            Undo.AddComponent(go, type);
                            sb.AppendLine($"  Added component: {type.Name}");
                        }
                        else
                        {
                            sb.AppendLine($"  Component already exists: {type.Name}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"  Warning: Component type '{typeName}' not found");
                    }
                }
            }

            // Remove components
            var removeComps = args["remove_components"] as JArray;
            if (removeComps != null && removeComps.Count > 0)
            {
                foreach (var comp in removeComps)
                {
                    string typeName = comp.ToString();
                    var type = FindComponentType(typeName);
                    if (type != null)
                    {
                        var existing = go.GetComponent(type);
                        if (existing != null)
                        {
                            Undo.DestroyObjectImmediate(existing);
                            sb.AppendLine($"  Removed component: {type.Name}");
                        }
                    }
                }
            }

            sb.AppendLine($"  Components: {string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))}");
            return sb.ToString();
        }

        private Type FindComponentType(string name)
        {
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

    public class CreateScriptTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "create_script",
            Description = "Create a new C# script file in the project Assets folder. " +
                          "The script is written to disk and imported by Unity automatically. " +
                          "Use this to create MonoBehaviours, ScriptableObjects, or any C# class. " +
                          "The path should end in .cs and be relative to the project root (e.g. 'Assets/Scripts/PlayerController.cs').",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""path"": { ""type"": ""string"", ""description"": ""Asset path for the new script (e.g. 'Assets/Scripts/PlayerController.cs')"" },
                    ""content"": { ""type"": ""string"", ""description"": ""Full C# source code for the script"" },
                    ""overwrite"": { ""type"": ""boolean"", ""description"": ""If true, overwrite an existing file. Default: false"" }
                },
                ""required"": [""path"", ""content""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string path = GetString(args, "path", "filePath", "filepath", "file", "scriptPath");
            string content = GetString(args, "content", "code", "source", "script");
            bool overwrite = args["overwrite"]?.Value<bool>() ?? false;

            if (string.IsNullOrEmpty(path))
                return "Error: 'path' is required.";
            if (string.IsNullOrEmpty(content))
                return "Error: 'content' is required.";
            if (!path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
                return "Error: path must end with '.cs'.";

            // Fix JSON-escaped content from small models that emit literal \n instead of newlines.
            // If the file content has no actual newline characters but has literal \n sequences,
            // the entire file is on one line — clearly broken. Unescape it.
            if (!content.Contains("\n") && content.Contains("\\n"))
            {
                content = content.Replace("\\n", "\n");
                content = content.Replace("\\t", "\t");
                content = content.Replace("\\r", "");
            }

            // Resolve to full path
            string fullPath;
            if (path.StartsWith("Assets"))
                fullPath = System.IO.Path.Combine(Application.dataPath, "..", path);
            else
                fullPath = System.IO.Path.Combine(Application.dataPath, path);

            fullPath = System.IO.Path.GetFullPath(fullPath);

            // Security: must be within Assets folder
            if (!fullPath.StartsWith(System.IO.Path.GetFullPath(Application.dataPath)))
                return "Error: Path must be within the Assets folder.";

            if (System.IO.File.Exists(fullPath) && !overwrite)
                return $"Error: File already exists at '{path}'. Set overwrite=true to replace it.";

            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(fullPath);
            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            System.IO.File.WriteAllText(fullPath, content);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            int lineCount = content.Split('\n').Length;
            return $"Created script '{path}' ({lineCount} lines, {content.Length} chars).\n" +
                   "Unity will compile the script automatically.";
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

    // ─────────────────────────────────────────────────────────────────────────
    //  CREATE MATERIAL
    // ─────────────────────────────────────────────────────────────────────────

    public class CreateMaterialTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "create_material",
            Description = "Create a new Material asset or apply a material to a GameObject. " +
                          "Can set color, shader, and common properties. Supports Undo.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name for the material"" },
                    ""path"": { ""type"": ""string"", ""description"": ""Asset path to save the material (e.g. 'Assets/Materials/Red.mat'). If omitted, material is created in memory only."" },
                    ""shader"": { ""type"": ""string"", ""description"": ""Shader name (e.g. 'Standard', 'Universal Render Pipeline/Lit', 'Universal Render Pipeline/Unlit'). Default: auto-detect URP or Standard."" },
                    ""color"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""r"": { ""type"": ""number"", ""description"": ""Red (0-1)"" },
                            ""g"": { ""type"": ""number"", ""description"": ""Green (0-1)"" },
                            ""b"": { ""type"": ""number"", ""description"": ""Blue (0-1)"" },
                            ""a"": { ""type"": ""number"", ""description"": ""Alpha (0-1, default 1)"" }
                        },
                        ""description"": ""Base color""
                    },
                    ""emission_color"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""r"": { ""type"": ""number"" },
                            ""g"": { ""type"": ""number"" },
                            ""b"": { ""type"": ""number"" }
                        },
                        ""description"": ""Emission color (enables emission)""
                    },
                    ""metallic"": { ""type"": ""number"", ""description"": ""Metallic value (0-1)"" },
                    ""smoothness"": { ""type"": ""number"", ""description"": ""Smoothness value (0-1)"" },
                    ""apply_to"": { ""type"": ""string"", ""description"": ""Name of a GameObject to apply this material to (optional)"" },
                    ""rendering_mode"": { ""type"": ""string"", ""enum"": [""opaque"", ""transparent""], ""description"": ""Rendering mode (default: opaque)"" }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name", "material_name", "materialName");
            if (string.IsNullOrEmpty(name)) return "Error: 'name' is required.";

            string shaderName = GetString(args, "shader", "shaderName", "shader_name");
            string path = GetString(args, "path", "filePath", "asset_path");
            string applyTo = GetString(args, "apply_to", "applyTo", "gameobject", "target");
            string renderMode = GetString(args, "rendering_mode", "renderingMode", "render_mode") ?? "opaque";

            // Auto-detect shader
            if (string.IsNullOrEmpty(shaderName))
            {
                var urpLit = Shader.Find("Universal Render Pipeline/Lit");
                shaderName = urpLit != null ? "Universal Render Pipeline/Lit" : "Standard";
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
                return $"Error: Shader '{shaderName}' not found. Try 'Standard' or 'Universal Render Pipeline/Lit'.";

            var material = new Material(shader) { name = name };

            // Color
            var colorToken = GetToken(args, "color");
            if (colorToken is JObject colorObj)
            {
                float r = colorObj["r"]?.Value<float>() ?? 1f;
                float g = colorObj["g"]?.Value<float>() ?? 1f;
                float b = colorObj["b"]?.Value<float>() ?? 1f;
                float a = colorObj["a"]?.Value<float>() ?? 1f;
                material.color = new Color(r, g, b, a);
            }

            // Metallic & Smoothness
            var metallicToken = GetToken(args, "metallic");
            if (metallicToken != null)
                material.SetFloat("_Metallic", metallicToken.Value<float>());

            var smoothnessToken = GetToken(args, "smoothness");
            if (smoothnessToken != null)
                material.SetFloat("_Smoothness", smoothnessToken.Value<float>());

            // Emission
            var emissionToken = GetToken(args, "emission_color", "emissionColor", "emission");
            if (emissionToken is JObject emObj)
            {
                material.EnableKeyword("_EMISSION");
                float er = emObj["r"]?.Value<float>() ?? 0f;
                float eg = emObj["g"]?.Value<float>() ?? 0f;
                float eb = emObj["b"]?.Value<float>() ?? 0f;
                material.SetColor("_EmissionColor", new Color(er, eg, eb));
            }

            // Transparent mode
            if (renderMode.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                material.SetFloat("_Surface", 1); // URP transparent
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = 3000;
                material.SetFloat("_ZWrite", 0);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            var sb = new StringBuilder();

            // Save as asset if path provided
            if (!string.IsNullOrEmpty(path))
            {
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    path += ".mat";

                string fullPath = path.StartsWith("Assets")
                    ? System.IO.Path.Combine(Application.dataPath, "..", path)
                    : System.IO.Path.Combine(Application.dataPath, path);
                fullPath = System.IO.Path.GetFullPath(fullPath);

                string dir = System.IO.Path.GetDirectoryName(fullPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                // Ensure path starts with Assets/ for AssetDatabase
                if (!path.StartsWith("Assets"))
                    path = "Assets/" + path;

                AssetDatabase.CreateAsset(material, path);
                AssetDatabase.SaveAssets();
                sb.AppendLine($"Created material asset: {path}");
            }
            else
            {
                sb.AppendLine($"Created material '{name}' (in memory)");
            }

            sb.AppendLine($"  Shader: {shaderName}");
            sb.AppendLine($"  Color: {material.color}");

            // Apply to GameObject
            if (!string.IsNullOrEmpty(applyTo))
            {
                var go = GameObject.Find(applyTo);
                if (go == null)
                {
                    go = Resources.FindObjectsOfTypeAll<GameObject>()
                        .FirstOrDefault(g => g.scene.isLoaded && g.name == applyTo);
                }

                if (go != null)
                {
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        Undo.RecordObject(renderer, "UNAI Apply Material");
                        renderer.sharedMaterial = material;
                        sb.AppendLine($"  Applied to: {go.name}");
                    }
                    else
                    {
                        sb.AppendLine($"  Warning: '{applyTo}' has no Renderer component.");
                    }
                }
                else
                {
                    sb.AppendLine($"  Warning: GameObject '{applyTo}' not found.");
                }
            }

            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EXECUTE MENU ITEM
    // ─────────────────────────────────────────────────────────────────────────

    public class ExecuteMenuItemTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "execute_menu_item",
            Description = "Execute a Unity Editor menu command by its path. " +
                          "This can run any menu command available in the Unity Editor. " +
                          "Examples: 'File/Save', 'Edit/Undo', 'Window/General/Console', " +
                          "'GameObject/Light/Directional Light', 'Component/Physics/Rigidbody'.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""menu_path"": { ""type"": ""string"", ""description"": ""Full menu path (e.g. 'GameObject/Light/Directional Light')"" }
                },
                ""required"": [""menu_path""]
            }")
        };

        // Allowed menu roots for safety
        private static readonly string[] _blockedPrefixes =
        {
            "File/Build",     // Don't let AI trigger builds
            "File/Quit",
            "File/Exit"
        };

        protected override string Execute(JObject args)
        {
            string menuPath = GetString(args, "menu_path", "menuPath", "path", "menu", "command");
            if (string.IsNullOrEmpty(menuPath))
                return "Error: 'menu_path' is required.";

            // Safety check
            foreach (var blocked in _blockedPrefixes)
            {
                if (menuPath.StartsWith(blocked, StringComparison.OrdinalIgnoreCase))
                    return $"Error: Menu command '{menuPath}' is blocked for safety.";
            }

            bool result = EditorApplication.ExecuteMenuItem(menuPath);

            if (result)
                return $"Executed menu command: '{menuPath}'";
            else
                return $"Error: Menu command '{menuPath}' not found or could not be executed. " +
                       "Make sure the exact menu path is correct (case-sensitive, using '/' separator). " +
                       "Common valid paths: 'GameObject/Create Empty', 'GameObject/3D Object/Cube', " +
                       "'GameObject/3D Object/Sphere', 'GameObject/3D Object/Plane', " +
                       "'GameObject/Light/Directional Light', 'GameObject/Light/Point Light', " +
                       "'GameObject/Camera', 'GameObject/UI/Canvas', 'GameObject/UI/Text - TextMeshPro', " +
                       "'Component/Physics/Rigidbody', 'Component/Physics/Box Collider', " +
                       "'File/Save', 'Edit/Undo', 'Edit/Redo'. " +
                       "NOTE: There is NO menu to assign scripts ' use modify_gameobject with add_component instead.";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MODIFY SCRIPT
    // ─────────────────────────────────────────────────────────────────────────

    public class ModifyScriptTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "modify_script",
            Description = "Modify an existing C# script file. Supports: " +
                          "replacing text (find and replace), inserting text at a line number, " +
                          "or overwriting the entire file content.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""path"": { ""type"": ""string"", ""description"": ""Asset path of the script (e.g. 'Assets/Scripts/Player.cs')"" },
                    ""mode"": { ""type"": ""string"", ""enum"": [""replace"", ""insert"", ""overwrite""], ""description"": ""Edit mode: 'replace' (find & replace), 'insert' (at line), or 'overwrite' (full file). Default: replace"" },
                    ""find"": { ""type"": ""string"", ""description"": ""Text to find (for 'replace' mode)"" },
                    ""replace_with"": { ""type"": ""string"", ""description"": ""Text to replace with (for 'replace' mode)"" },
                    ""line"": { ""type"": ""integer"", ""description"": ""Line number to insert at (1-based, for 'insert' mode)"" },
                    ""text"": { ""type"": ""string"", ""description"": ""Text to insert (for 'insert' mode)"" },
                    ""content"": { ""type"": ""string"", ""description"": ""Full new content (for 'overwrite' mode)"" },
                    ""replace_all"": { ""type"": ""boolean"", ""description"": ""Replace all occurrences (for 'replace' mode). Default: false"" }
                },
                ""required"": [""path""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string path = GetString(args, "path", "filePath", "filepath", "file", "scriptPath");
            if (string.IsNullOrEmpty(path))
                return "Error: 'path' is required.";

            string fullPath;
            if (path.StartsWith("Assets"))
                fullPath = System.IO.Path.Combine(Application.dataPath, "..", path);
            else
                fullPath = System.IO.Path.Combine(Application.dataPath, path);
            fullPath = System.IO.Path.GetFullPath(fullPath);

            if (!fullPath.StartsWith(System.IO.Path.GetFullPath(Application.dataPath)))
                return "Error: Path must be within the Assets folder.";

            if (!System.IO.File.Exists(fullPath))
                return $"Error: File not found at '{path}'.";

            string mode = GetString(args, "mode") ?? "replace";
            string currentContent = System.IO.File.ReadAllText(fullPath);

            string newContent;
            string description;

            switch (mode.ToLower())
            {
                case "replace":
                {
                    string find = GetString(args, "find", "search", "old", "old_text");
                    string replaceWith = GetString(args, "replace_with", "replaceWith", "replacement", "new", "new_text") ?? "";
                    bool replaceAll = args["replace_all"]?.Value<bool>() ?? false;

                    if (string.IsNullOrEmpty(find))
                        return "Error: 'find' is required for replace mode.";

                    if (!currentContent.Contains(find))
                        return $"Error: Text not found in '{path}'. Make sure the 'find' text matches exactly (including whitespace/indentation).";

                    if (replaceAll)
                    {
                        int count = 0;
                        string temp = currentContent;
                        while (temp.Contains(find))
                        {
                            temp = temp.Remove(temp.IndexOf(find, StringComparison.Ordinal), find.Length)
                                       .Insert(temp.IndexOf(find, StringComparison.Ordinal), replaceWith);
                            count++;
                            if (count > 1000) break; // Safety
                        }
                        newContent = currentContent.Replace(find, replaceWith);
                        description = $"Replaced {count} occurrence(s)";
                    }
                    else
                    {
                        int idx = currentContent.IndexOf(find, StringComparison.Ordinal);
                        newContent = currentContent.Remove(idx, find.Length).Insert(idx, replaceWith);
                        description = "Replaced 1 occurrence";
                    }
                    break;
                }
                case "insert":
                {
                    int line = args["line"]?.Value<int>() ?? 0;
                    string text = GetString(args, "text", "insert_text", "code") ?? "";

                    if (line <= 0)
                        return "Error: 'line' must be a positive integer (1-based).";

                    var lines = currentContent.Split('\n').ToList();
                    int insertIdx = Math.Min(line - 1, lines.Count);
                    lines.Insert(insertIdx, text);
                    newContent = string.Join("\n", lines);
                    description = $"Inserted text at line {line}";
                    break;
                }
                case "overwrite":
                {
                    string content = GetString(args, "content", "code", "source", "new_content");
                    if (string.IsNullOrEmpty(content))
                        return "Error: 'content' is required for overwrite mode.";
                    newContent = content;
                    description = "Overwrote entire file";
                    break;
                }
                default:
                    return $"Error: Unknown mode '{mode}'. Use 'replace', 'insert', or 'overwrite'.";
            }

            System.IO.File.WriteAllText(fullPath, newContent);

            // Normalize asset path for Unity
            if (!path.StartsWith("Assets"))
                path = "Assets/" + path;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            int lineCount = newContent.Split('\n').Length;
            return $"Modified '{path}': {description} ({lineCount} lines, {newContent.Length} chars).";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CREATE PREFAB
    // ─────────────────────────────────────────────────────────────────────────

    public class CreatePrefabTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "create_prefab",
            Description = "Save a scene GameObject as a Prefab asset. " +
                          "The original GameObject remains in the scene and becomes a prefab instance.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""gameobject"": { ""type"": ""string"", ""description"": ""Name of the GameObject in the scene to save as prefab"" },
                    ""path"": { ""type"": ""string"", ""description"": ""Asset path for the prefab (e.g. 'Assets/Prefabs/Player.prefab'). If omitted, saves to 'Assets/Prefabs/{name}.prefab'."" }
                },
                ""required"": [""gameobject""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string goName = GetString(args, "gameobject", "name", "object", "gameObject", "source");
            if (string.IsNullOrEmpty(goName))
                return "Error: 'gameobject' is required.";

            var go = GameObject.Find(goName);
            if (go == null)
            {
                go = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g.scene.isLoaded && g.name == goName);
            }
            if (go == null)
                return $"Error: GameObject '{goName}' not found in scene.";

            string path = GetString(args, "path", "filePath", "prefab_path", "prefabPath");
            if (string.IsNullOrEmpty(path))
            {
                path = $"Assets/Prefabs/{go.name}.prefab";
            }

            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                path += ".prefab";

            if (!path.StartsWith("Assets"))
                path = "Assets/" + path;

            // Ensure directory exists
            string fullDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", System.IO.Path.GetDirectoryName(path)));
            if (!System.IO.Directory.Exists(fullDir))
                System.IO.Directory.CreateDirectory(fullDir);

            bool success;
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction, out success);

            if (!success || prefab == null)
                return $"Error: Failed to create prefab at '{path}'.";

            var components = prefab.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name);

            var sb = new StringBuilder();
            sb.AppendLine($"Created prefab: {path}");
            sb.AppendLine($"  Source: {go.name}");
            sb.AppendLine($"  Components: {string.Join(", ", components)}");
            sb.AppendLine($"  Children: {go.transform.childCount}");
            sb.AppendLine("  The scene object is now a prefab instance.");
            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UNDO
    // ─────────────────────────────────────────────────────────────────────────

    public class UndoTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "undo",
            Description = "Undo the last action in the Unity Editor. Can undo multiple steps.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""steps"": { ""type"": ""integer"", ""description"": ""Number of undo steps (default: 1)"" }
                }
            }")
        };

        protected override string Execute(JObject args)
        {
            int steps = args["steps"]?.Value<int>() ?? 1;
            steps = Mathf.Clamp(steps, 1, 50);

            var sb = new StringBuilder();
            for (int i = 0; i < steps; i++)
            {
                Undo.PerformUndo();
            }

            sb.AppendLine($"Performed {steps} undo step(s).");
            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GET CONSOLE LOGS
    // ─────────────────────────────────────────────────────────────────────────

    public class GetConsoleLogsTool : UnaiEditorTool
    {
        // Static ring buffer to capture log messages
        private static readonly System.Collections.Generic.List<LogEntry> _logBuffer = new();
        private static bool _isListening;
        private const int MaxLogEntries = 200;

        public override UnaiToolDefinition Definition => new()
        {
            Name = "get_console_logs",
            Description = "Read recent messages from the Unity Console (errors, warnings, logs). " +
                          "Useful for debugging issues, checking for compile errors, or verifying actions.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""integer"", ""description"": ""Number of recent entries to return (default: 20, max: 100)"" },
                    ""filter"": { ""type"": ""string"", ""enum"": [""all"", ""error"", ""warning"", ""log""], ""description"": ""Filter by log type (default: all)"" },
                    ""search"": { ""type"": ""string"", ""description"": ""Filter entries containing this text"" },
                    ""clear"": { ""type"": ""boolean"", ""description"": ""Clear the log buffer after reading (default: false)"" }
                }
            }")
        };

        public GetConsoleLogsTool()
        {
            EnsureListening();
        }

        private static void EnsureListening()
        {
            if (_isListening) return;
            Application.logMessageReceived += OnLogMessage;
            _isListening = true;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            // Skip UNAI internal debug messages to avoid noise
            if (message.StartsWith("[UNAI]")) return;

            lock (_logBuffer)
            {
                _logBuffer.Add(new LogEntry
                {
                    Message = message,
                    StackTrace = stackTrace,
                    Type = type,
                    Timestamp = DateTime.Now
                });

                // Trim old entries
                while (_logBuffer.Count > MaxLogEntries)
                    _logBuffer.RemoveAt(0);
            }
        }

        protected override string Execute(JObject args)
        {
            int count = args["count"]?.Value<int>() ?? 20;
            count = Mathf.Clamp(count, 1, 100);
            string filter = GetString(args, "filter", "type", "level") ?? "all";
            string search = GetString(args, "search", "query", "text");
            bool clear = args["clear"]?.Value<bool>() ?? false;

            System.Collections.Generic.List<LogEntry> entries;
            lock (_logBuffer)
            {
                entries = new System.Collections.Generic.List<LogEntry>(_logBuffer);
                if (clear) _logBuffer.Clear();
            }

            // Apply filter
            if (!filter.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                LogType targetType = filter.ToLower() switch
                {
                    "error" => LogType.Error,
                    "warning" => LogType.Warning,
                    "log" => LogType.Log,
                    _ => LogType.Log
                };
                entries = entries.Where(e =>
                    e.Type == targetType ||
                    (filter.ToLower() == "error" && e.Type == LogType.Exception))
                    .ToList();
            }

            // Apply search
            if (!string.IsNullOrEmpty(search))
            {
                entries = entries.Where(e =>
                    e.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            // Take most recent
            entries = entries.Skip(Math.Max(0, entries.Count - count)).ToList();

            if (entries.Count == 0)
                return "No console log entries found" +
                       (filter != "all" ? $" (filter: {filter})" : "") +
                       (search != null ? $" (search: '{search}')" : "") + ".";

            var sb = new StringBuilder();
            sb.AppendLine($"Console Logs ({entries.Count} entries):");

            foreach (var entry in entries)
            {
                string typeLabel = entry.Type switch
                {
                    LogType.Error => "ERROR",
                    LogType.Exception => "EXCEPTION",
                    LogType.Warning => "WARNING",
                    LogType.Assert => "ASSERT",
                    _ => "LOG"
                };
                string time = entry.Timestamp.ToString("HH:mm:ss");
                string msg = entry.Message.Length > 300
                    ? entry.Message.Substring(0, 300) + "..."
                    : entry.Message;

                sb.AppendLine($"  [{time}] [{typeLabel}] {msg}");

                // Include first line of stack trace for errors
                if ((entry.Type == LogType.Error || entry.Type == LogType.Exception)
                    && !string.IsNullOrEmpty(entry.StackTrace))
                {
                    var firstLine = entry.StackTrace.Split('\n').FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(firstLine))
                        sb.AppendLine($"    at {firstLine}");
                }
            }

            return sb.ToString();
        }

        private struct LogEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public DateTime Timestamp;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ADD COMPONENT CONFIGURED
    // ─────────────────────────────────────────────────────────────────────────

    public class AddComponentConfiguredTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "add_component_configured",
            Description = "Add a component to a GameObject and configure its serialized properties in one call. " +
                          "Properties are set via SerializedObject so any inspector-visible field can be changed. " +
                          "Supports Undo.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""gameobject"": { ""type"": ""string"", ""description"": ""Name or path of the target GameObject"" },
                    ""component"": { ""type"": ""string"", ""description"": ""Component type name (e.g. 'Rigidbody', 'BoxCollider', 'AudioSource')"" },
                    ""properties"": {
                        ""type"": ""object"",
                        ""description"": ""Key-value pairs of property names and values to set (e.g. { 'mass': 5, 'useGravity': false, 'drag': 0.5 }). Property names match the inspector field names (camelCase or the serialized field name).""
                    }
                },
                ""required"": [""gameobject"", ""component""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string goName = GetString(args, "gameobject", "gameObject", "name", "target", "object");
            string compName = GetString(args, "component", "componentType", "component_type", "type");

            if (string.IsNullOrEmpty(goName)) return "Error: 'gameobject' is required.";
            if (string.IsNullOrEmpty(compName)) return "Error: 'component' is required.";

            var go = GameObject.Find(goName);
            if (go == null)
                go = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g.scene.isLoaded && g.name == goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var type = FindComponentType(compName);
            if (type == null) return $"Error: Component type '{compName}' not found.";

            var existing = go.GetComponent(type);
            Component comp;
            if (existing != null)
            {
                comp = existing;
            }
            else
            {
                comp = Undo.AddComponent(go, type);
            }

            var sb = new StringBuilder();
            sb.AppendLine(existing != null
                ? $"Configured existing '{type.Name}' on '{go.name}':"
                : $"Added and configured '{type.Name}' on '{go.name}':");

            var properties = args["properties"] as JObject;
            if (properties != null && properties.Count > 0)
            {
                var so = new SerializedObject(comp);

                foreach (var kvp in properties)
                {
                    string propName = kvp.Key;
                    var value = kvp.Value;

                    var prop = so.FindProperty(propName);
                    if (prop == null)
                    {
                        // Try common Unity serialized field name patterns
                        prop = so.FindProperty("m_" + char.ToUpper(propName[0]) + propName.Substring(1));
                    }

                    if (prop == null)
                    {
                        sb.AppendLine($"  Warning: Property '{propName}' not found on {type.Name}");
                        continue;
                    }

                    if (SetPropertyValue(prop, value))
                        sb.AppendLine($"  {propName} = {value}");
                    else
                        sb.AppendLine($"  Warning: Could not set '{propName}' (type: {prop.propertyType})");
                }

                so.ApplyModifiedProperties();
            }

            // List current property values
            var soRead = new SerializedObject(comp);
            var iter = soRead.GetIterator();
            int shown = 0;
            if (iter.NextVisible(true))
            {
                do
                {
                    if (iter.name == "m_Script") continue;
                    sb.AppendLine($"  [{iter.displayName}: {GetPropertyDisplay(iter)}]");
                    shown++;
                } while (iter.NextVisible(false) && shown < 15);
            }

            return sb.ToString();
        }

        private bool SetPropertyValue(SerializedProperty prop, JToken value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = value.Value<int>();
                    return true;
                case SerializedPropertyType.Float:
                    prop.floatValue = value.Value<float>();
                    return true;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value.Value<bool>();
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = value.ToString();
                    return true;
                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.Integer)
                        prop.enumValueIndex = value.Value<int>();
                    else
                    {
                        string enumStr = value.ToString();
                        int idx = Array.IndexOf(prop.enumDisplayNames, enumStr);
                        if (idx < 0) idx = Array.FindIndex(prop.enumNames, n =>
                            n.Equals(enumStr, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) prop.enumValueIndex = idx;
                        else return false;
                    }
                    return true;
                case SerializedPropertyType.Vector2:
                    if (value is JObject v2)
                    {
                        prop.vector2Value = new Vector2(
                            v2["x"]?.Value<float>() ?? 0,
                            v2["y"]?.Value<float>() ?? 0);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Vector3:
                    if (value is JObject v3)
                    {
                        prop.vector3Value = new Vector3(
                            v3["x"]?.Value<float>() ?? 0,
                            v3["y"]?.Value<float>() ?? 0,
                            v3["z"]?.Value<float>() ?? 0);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Color:
                    if (value is JObject col)
                    {
                        prop.colorValue = new Color(
                            col["r"]?.Value<float>() ?? 1,
                            col["g"]?.Value<float>() ?? 1,
                            col["b"]?.Value<float>() ?? 1,
                            col["a"]?.Value<float>() ?? 1);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private string GetPropertyDisplay(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Float => prop.floatValue.ToString("F3"),
                SerializedPropertyType.Boolean => prop.boolValue.ToString(),
                SerializedPropertyType.String => $"\"{prop.stringValue}\"",
                SerializedPropertyType.Enum => prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? prop.enumDisplayNames[prop.enumValueIndex] : prop.enumValueIndex.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                SerializedPropertyType.Color => prop.colorValue.ToString(),
                _ => $"({prop.propertyType})"
            };
        }

        private Type FindComponentType(string name)
        {
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

    // ─────────────────────────────────────────────────────────────────────────
    //  CREATE LIGHT
    // ─────────────────────────────────────────────────────────────────────────

    public class CreateLightTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "create_light",
            Description = "Create a Light GameObject in the scene with full configuration. " +
                          "Supports Directional, Point, Spot, and Area light types. Supports Undo.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name for the light (default: 'New Light')"" },
                    ""type"": { ""type"": ""string"", ""enum"": [""Directional"", ""Point"", ""Spot"", ""Area""], ""description"": ""Light type (default: Point)"" },
                    ""color"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""r"": { ""type"": ""number"" },
                            ""g"": { ""type"": ""number"" },
                            ""b"": { ""type"": ""number"" }
                        },
                        ""description"": ""Light color (RGB 0-1, default: white)""
                    },
                    ""intensity"": { ""type"": ""number"", ""description"": ""Light intensity (default: 1)"" },
                    ""range"": { ""type"": ""number"", ""description"": ""Range for Point/Spot lights (default: 10)"" },
                    ""spot_angle"": { ""type"": ""number"", ""description"": ""Spot angle in degrees for Spot lights (default: 30)"" },
                    ""shadows"": { ""type"": ""string"", ""enum"": [""None"", ""Hard"", ""Soft""], ""description"": ""Shadow type (default: Soft)"" },
                    ""position"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""x"": { ""type"": ""number"" },
                            ""y"": { ""type"": ""number"" },
                            ""z"": { ""type"": ""number"" }
                        },
                        ""description"": ""World position""
                    },
                    ""rotation"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""x"": { ""type"": ""number"" },
                            ""y"": { ""type"": ""number"" },
                            ""z"": { ""type"": ""number"" }
                        },
                        ""description"": ""Euler rotation""
                    },
                    ""parent"": { ""type"": ""string"", ""description"": ""Parent GameObject name (optional)"" }
                }
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name") ?? "New Light";
            string typeStr = GetString(args, "type", "lightType", "light_type") ?? "Point";

            if (!Enum.TryParse<LightType>(typeStr, true, out var lightType))
                return $"Error: Unknown light type '{typeStr}'. Use Directional, Point, Spot, or Area.";

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create Light {name}");

            var light = go.AddComponent<Light>();
            light.type = lightType;

            // Color
            var colorToken = GetToken(args, "color");
            if (colorToken is JObject col)
            {
                light.color = new Color(
                    col["r"]?.Value<float>() ?? 1f,
                    col["g"]?.Value<float>() ?? 1f,
                    col["b"]?.Value<float>() ?? 1f);
            }

            // Intensity
            var intensityToken = GetToken(args, "intensity");
            if (intensityToken != null)
                light.intensity = intensityToken.Value<float>();

            // Range (Point/Spot)
            var rangeToken = GetToken(args, "range");
            if (rangeToken != null)
                light.range = rangeToken.Value<float>();

            // Spot angle
            var spotToken = GetToken(args, "spot_angle", "spotAngle");
            if (spotToken != null)
                light.spotAngle = spotToken.Value<float>();

            // Shadows
            string shadowStr = GetString(args, "shadows", "shadow_type") ?? "Soft";
            if (Enum.TryParse<LightShadows>(shadowStr, true, out var shadowType))
                light.shadows = shadowType;

            // Position
            var pos = GetToken(args, "position");
            if (pos is JObject posObj)
            {
                go.transform.position = new Vector3(
                    posObj["x"]?.Value<float>() ?? 0,
                    posObj["y"]?.Value<float>() ?? 0,
                    posObj["z"]?.Value<float>() ?? 0);
            }
            else if (lightType == LightType.Directional)
            {
                go.transform.position = new Vector3(0, 3, 0);
                go.transform.eulerAngles = new Vector3(50, -30, 0);
            }
            else
            {
                go.transform.position = new Vector3(0, 3, 0);
            }

            // Rotation
            var rot = GetToken(args, "rotation");
            if (rot is JObject rotObj)
            {
                go.transform.eulerAngles = new Vector3(
                    rotObj["x"]?.Value<float>() ?? 0,
                    rotObj["y"]?.Value<float>() ?? 0,
                    rotObj["z"]?.Value<float>() ?? 0);
            }

            // Parent
            string parentName = GetString(args, "parent");
            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    Undo.SetTransformParent(go.transform, parent.transform, $"Set parent of {name}");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Created {lightType} light '{name}':");
            sb.AppendLine($"  Color: {light.color}");
            sb.AppendLine($"  Intensity: {light.intensity}");
            if (lightType is LightType.Point or LightType.Spot)
                sb.AppendLine($"  Range: {light.range}");
            if (lightType == LightType.Spot)
                sb.AppendLine($"  Spot Angle: {light.spotAngle}");
            sb.AppendLine($"  Shadows: {light.shadows}");
            sb.AppendLine($"  Position: {go.transform.position}");
            sb.AppendLine($"  Rotation: {go.transform.eulerAngles}");
            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SEARCH PROJECT
    // ─────────────────────────────────────────────────────────────────────────

    public class SearchProjectTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "search_project",
            Description = "Full-text search across project scripts and text assets. " +
                          "Finds files containing the search text and shows matching lines with context.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""query"": { ""type"": ""string"", ""description"": ""Text to search for (case-insensitive)"" },
                    ""folder"": { ""type"": ""string"", ""description"": ""Folder to search in (default: 'Assets')"" },
                    ""extension"": { ""type"": ""string"", ""description"": ""File extension filter (e.g. '.cs', '.shader', '.json'). Default: '.cs'"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of file matches to return (default: 20)"" }
                },
                ""required"": [""query""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string query = GetString(args, "query", "search", "text", "pattern");
            if (string.IsNullOrEmpty(query)) return "Error: 'query' is required.";

            string folder = GetString(args, "folder", "directory", "path") ?? "Assets";
            string extension = GetString(args, "extension", "ext", "file_type") ?? ".cs";
            int maxResults = args["max_results"]?.Value<int>() ?? 20;
            maxResults = Mathf.Clamp(maxResults, 1, 50);

            string fullFolder = folder.StartsWith("Assets")
                ? System.IO.Path.Combine(Application.dataPath, "..", folder)
                : System.IO.Path.Combine(Application.dataPath, folder);
            fullFolder = System.IO.Path.GetFullPath(fullFolder);

            if (!System.IO.Directory.Exists(fullFolder))
                return $"Error: Folder '{folder}' not found.";

            var files = System.IO.Directory.GetFiles(fullFolder, $"*{extension}", System.IO.SearchOption.AllDirectories);
            var sb = new StringBuilder();
            int matchCount = 0;

            foreach (var file in files)
            {
                if (matchCount >= maxResults) break;

                try
                {
                    string content = System.IO.File.ReadAllText(file);
                    if (content.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    matchCount++;
                    string relativePath = file.Replace(
                        System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")),
                        "").TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                    sb.AppendLine($"--- {relativePath} ---");

                    var lines = content.Split('\n');
                    int linesShown = 0;
                    for (int i = 0; i < lines.Length && linesShown < 5; i++)
                    {
                        if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string trimmed = lines[i].TrimEnd('\r');
                            if (trimmed.Length > 120)
                                trimmed = trimmed.Substring(0, 120) + "...";
                            sb.AppendLine($"  L{i + 1}: {trimmed}");
                            linesShown++;
                        }
                    }
                    sb.AppendLine();
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            if (matchCount == 0)
                return $"No matches found for '{query}' in {folder} ({extension} files).";

            sb.Insert(0, $"Found '{query}' in {matchCount} file(s):\n\n");
            if (matchCount >= maxResults)
                sb.AppendLine($"... (limited to {maxResults} results)");

            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DUPLICATION & ORGANIZATION
    // ─────────────────────────────────────────────────────────────────────────

    public class DuplicateGameObjectTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "duplicate_gameobject",
            Description = "Duplicate (clone) a GameObject including all components and children. " +
                          "Optionally rename the clone and set a new parent.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name of the GameObject to duplicate"" },
                    ""new_name"": { ""type"": ""string"", ""description"": ""Name for the clone (default: original name with (1) suffix)"" },
                    ""parent"": { ""type"": ""string"", ""description"": ""Name of the parent to place the clone under"" },
                    ""offset"": { ""type"": ""object"", ""description"": ""Position offset from original"",
                        ""properties"": {
                            ""x"": { ""type"": ""number"" },
                            ""y"": { ""type"": ""number"" },
                            ""z"": { ""type"": ""number"" }
                        }
                    }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name");
            string newName = GetString(args, "new_name");
            string parentName = GetString(args, "parent");
            var offset = GetToken(args, "offset") as JObject;

            var original = GameObject.Find(name);
            if (original == null)
                return $"Error: GameObject '{name}' not found.";

            Undo.IncrementCurrentGroup();
            var clone = UnityEngine.Object.Instantiate(original);
            Undo.RegisterCreatedObjectUndo(clone, $"Duplicate {name}");

            if (!string.IsNullOrEmpty(newName))
                clone.name = newName;
            else
                clone.name = original.name; // Remove "(Clone)" suffix

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    clone.transform.SetParent(parent.transform, true);
            }

            if (offset != null)
            {
                float ox = offset["x"]?.Value<float>() ?? 0f;
                float oy = offset["y"]?.Value<float>() ?? 0f;
                float oz = offset["z"]?.Value<float>() ?? 0f;
                clone.transform.position = original.transform.position + new Vector3(ox, oy, oz);
            }

            int componentCount = clone.GetComponents<Component>().Length;
            int childCount = clone.GetComponentsInChildren<Transform>(true).Length - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"Duplicated '{original.name}' as '{clone.name}'");
            sb.AppendLine($"  Components: {componentCount}");
            sb.AppendLine($"  Children: {childCount}");
            sb.AppendLine($"  Position: {clone.transform.position}");
            if (clone.transform.parent != null)
                sb.AppendLine($"  Parent: {clone.transform.parent.name}");

            return sb.ToString();
        }
    }

    public class SetLayerTagTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "set_layer_tag",
            Description = "Set the layer and/or tag on a GameObject. Optionally apply to all children recursively.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name of the GameObject"" },
                    ""layer"": { ""type"": ""string"", ""description"": ""Layer name to set (e.g. 'Water', 'UI', 'Ignore Raycast')"" },
                    ""tag"": { ""type"": ""string"", ""description"": ""Tag to set (e.g. 'Player', 'Enemy', 'Respawn')"" },
                    ""include_children"": { ""type"": ""boolean"", ""description"": ""Apply to all children recursively (default: false)"" }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name");
            string layer = GetString(args, "layer");
            string tag = GetString(args, "tag");
            bool includeChildren = args["include_children"]?.Value<bool>() ?? false;

            if (string.IsNullOrEmpty(layer) && string.IsNullOrEmpty(tag))
                return "Error: At least one of 'layer' or 'tag' must be specified.";

            var go = GameObject.Find(name);
            if (go == null)
                return $"Error: GameObject '{name}' not found.";

            int layerIndex = -1;
            if (!string.IsNullOrEmpty(layer))
            {
                layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex < 0)
                    return $"Error: Layer '{layer}' does not exist. Use Unity's Tags and Layers settings to add it.";
            }

            Undo.IncrementCurrentGroup();
            var targets = includeChildren
                ? go.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray()
                : new[] { go };

            int count = 0;
            foreach (var target in targets)
            {
                Undo.RecordObject(target, "Set Layer/Tag");
                if (layerIndex >= 0)
                    target.layer = layerIndex;
                if (!string.IsNullOrEmpty(tag))
                    target.tag = tag;
                count++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Updated {count} GameObject(s):");
            if (!string.IsNullOrEmpty(layer))
                sb.AppendLine($"  Layer: {layer} (index {layerIndex})");
            if (!string.IsNullOrEmpty(tag))
                sb.AppendLine($"  Tag: {tag}");
            if (includeChildren)
                sb.AppendLine($"  (Applied recursively to {count - 1} children)");

            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PROJECT SETTINGS
    // ─────────────────────────────────────────────────────────────────────────

    public class GetProjectSettingsTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "get_project_settings",
            Description = "Read Unity project settings: Physics, Quality, Time, or Player settings.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""category"": { ""type"": ""string"", ""description"": ""Settings category: 'physics', 'quality', 'time', or 'player'"" }
                },
                ""required"": [""category""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string category = GetString(args, "category")?.ToLowerInvariant();

            return category switch
            {
                "physics" => GetPhysicsSettings(),
                "quality" => GetQualitySettings(),
                "time" => GetTimeSettings(),
                "player" => GetPlayerSettings(),
                _ => $"Error: Unknown category '{category}'. Use 'physics', 'quality', 'time', or 'player'."
            };
        }

        private string GetPhysicsSettings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Physics Settings ===");
            sb.AppendLine($"  Gravity: {Physics.gravity}");
            sb.AppendLine($"  Default solver iterations: {Physics.defaultSolverIterations}");
            sb.AppendLine($"  Default solver velocity iterations: {Physics.defaultSolverVelocityIterations}");
            sb.AppendLine($"  Bounce threshold: {Physics.bounceThreshold}");
            sb.AppendLine($"  Default contact offset: {Physics.defaultContactOffset}");
            sb.AppendLine($"  Sleep threshold: {Physics.sleepThreshold}");
            sb.AppendLine($"  Auto-sync transforms: {Physics.autoSyncTransforms}");

            // Layer collision matrix — show which layers collide
            sb.AppendLine("  Layer collision matrix (non-default):");
            int layerCount = 0;
            for (int i = 0; i < 32; i++)
            {
                string ln = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(ln)) continue;
                for (int j = i; j < 32; j++)
                {
                    string ln2 = LayerMask.LayerToName(j);
                    if (string.IsNullOrEmpty(ln2)) continue;
                    if (!Physics.GetIgnoreLayerCollision(i, j)) continue;
                    sb.AppendLine($"    {ln} <-> {ln2}: IGNORED");
                    layerCount++;
                }
            }
            if (layerCount == 0) sb.AppendLine("    (all layers collide with each other)");

            return sb.ToString();
        }

        private string GetQualitySettings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Quality Settings ===");
            var names = QualitySettings.names;
            sb.AppendLine($"  Current level: {names[QualitySettings.GetQualityLevel()]} (index {QualitySettings.GetQualityLevel()})");
            sb.AppendLine($"  All levels: {string.Join(", ", names)}");
            sb.AppendLine($"  VSync count: {QualitySettings.vSyncCount}");
            sb.AppendLine($"  Anti-aliasing: {QualitySettings.antiAliasing}x");
            sb.AppendLine($"  Shadow distance: {QualitySettings.shadowDistance}");
            sb.AppendLine($"  Shadow resolution: {QualitySettings.shadowResolution}");
            sb.AppendLine($"  Texture quality: {QualitySettings.globalTextureMipmapLimit}");
            sb.AppendLine($"  Anisotropic filtering: {QualitySettings.anisotropicFiltering}");
            sb.AppendLine($"  LOD bias: {QualitySettings.lodBias}");
            sb.AppendLine($"  Particle raycast budget: {QualitySettings.particleRaycastBudget}");
            return sb.ToString();
        }

        private string GetTimeSettings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Time Settings ===");
            sb.AppendLine($"  Fixed timestep: {Time.fixedDeltaTime}");
            sb.AppendLine($"  Maximum allowed timestep: {Time.maximumDeltaTime}");
            sb.AppendLine($"  Time scale: {Time.timeScale}");
            sb.AppendLine($"  Maximum particle timestep: {Time.maximumParticleDeltaTime}");
            return sb.ToString();
        }

        private string GetPlayerSettings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Player Settings ===");
            sb.AppendLine($"  Product name: {PlayerSettings.productName}");
            sb.AppendLine($"  Company name: {PlayerSettings.companyName}");
            sb.AppendLine($"  Bundle version: {PlayerSettings.bundleVersion}");
            sb.AppendLine($"  Default screen width: {PlayerSettings.defaultScreenWidth}");
            sb.AppendLine($"  Default screen height: {PlayerSettings.defaultScreenHeight}");
            sb.AppendLine($"  Fullscreen mode: {PlayerSettings.fullScreenMode}");
            sb.AppendLine($"  Run in background: {PlayerSettings.runInBackground}");
            sb.AppendLine($"  Color space: {PlayerSettings.colorSpace}");
            sb.AppendLine($"  Scripting backend: {PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine($"  API compatibility: {PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine($"  Target platform: {EditorUserBuildSettings.activeBuildTarget}");
            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SCENE VIEW
    // ─────────────────────────────────────────────────────────────────────────

    public class FocusSceneViewTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "focus_scene_view",
            Description = "Focus the Scene View camera on a specific GameObject (equivalent to pressing F in the editor).",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name of the GameObject to focus on"" }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name");

            var go = GameObject.Find(name);
            if (go == null)
                return $"Error: GameObject '{name}' not found.";

            // Select the object and frame it in the Scene View
            Selection.activeGameObject = go;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.FrameSelected();
                sceneView.Repaint();
                return $"Focused Scene View on '{name}' at position {go.transform.position}.";
            }

            return $"Selected '{name}' but no Scene View is currently open. Open Window > General > Scene to see it.";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PHYSICS SETUP
    // ─────────────────────────────────────────────────────────────────────────

    public class CreatePhysicsSetupTool : UnaiEditorTool
    {
        public override UnaiToolDefinition Definition => new()
        {
            Name = "create_physics_setup",
            Description = "Add a complete physics setup to a GameObject: Rigidbody + Collider + optional PhysicMaterial, in one call.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Name of the target GameObject"" },
                    ""collider"": { ""type"": ""string"", ""description"": ""Collider type: 'box', 'sphere', 'capsule', 'mesh' (default: 'box')"" },
                    ""is_trigger"": { ""type"": ""boolean"", ""description"": ""Set collider as trigger (default: false)"" },
                    ""mass"": { ""type"": ""number"", ""description"": ""Rigidbody mass (default: 1)"" },
                    ""drag"": { ""type"": ""number"", ""description"": ""Rigidbody drag (default: 0)"" },
                    ""angular_drag"": { ""type"": ""number"", ""description"": ""Rigidbody angular drag (default: 0.05)"" },
                    ""use_gravity"": { ""type"": ""boolean"", ""description"": ""Enable gravity (default: true)"" },
                    ""is_kinematic"": { ""type"": ""boolean"", ""description"": ""Set as kinematic (default: false)"" },
                    ""freeze_position"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Axes to freeze position: ['x','y','z']"" },
                    ""freeze_rotation"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Axes to freeze rotation: ['x','y','z']"" },
                    ""bounciness"": { ""type"": ""number"", ""description"": ""PhysicMaterial bounciness (0-1). Only created if specified."" },
                    ""friction"": { ""type"": ""number"", ""description"": ""PhysicMaterial dynamic friction (0-1)"" }
                },
                ""required"": [""name""]
            }")
        };

        protected override string Execute(JObject args)
        {
            string name = GetString(args, "name");
            string colliderType = GetString(args, "collider", "collider_type") ?? "box";
            bool isTrigger = args["is_trigger"]?.Value<bool>() ?? false;
            float mass = args["mass"]?.Value<float>() ?? 1f;
            float drag = args["drag"]?.Value<float>() ?? 0f;
            float angularDrag = args["angular_drag"]?.Value<float>() ?? 0.05f;
            bool useGravity = args["use_gravity"]?.Value<bool>() ?? true;
            bool isKinematic = args["is_kinematic"]?.Value<bool>() ?? false;

            var go = GameObject.Find(name);
            if (go == null)
                return $"Error: GameObject '{name}' not found.";

            Undo.IncrementCurrentGroup();
            var sb = new StringBuilder();
            sb.AppendLine($"Physics setup for '{name}':");

            // Add Collider
            Collider collider;
            switch (colliderType.ToLowerInvariant())
            {
                case "sphere":
                    collider = Undo.AddComponent<SphereCollider>(go);
                    sb.AppendLine($"  + SphereCollider");
                    break;
                case "capsule":
                    collider = Undo.AddComponent<CapsuleCollider>(go);
                    sb.AppendLine($"  + CapsuleCollider");
                    break;
                case "mesh":
                    collider = Undo.AddComponent<MeshCollider>(go);
                    if (collider is MeshCollider mc)
                        mc.convex = true; // Required for Rigidbody
                    sb.AppendLine($"  + MeshCollider (convex)");
                    break;
                default:
                    collider = Undo.AddComponent<BoxCollider>(go);
                    sb.AppendLine($"  + BoxCollider");
                    break;
            }

            if (isTrigger && collider != null)
            {
                collider.isTrigger = true;
                sb.AppendLine($"    isTrigger: true");
            }

            // PhysicMaterial (only if bounciness or friction specified)
            var bouncinessToken = GetToken(args, "bounciness");
            var frictionToken = GetToken(args, "friction");
            if (bouncinessToken != null || frictionToken != null)
            {
                var mat = new PhysicsMaterial($"{name}_PhysMat");
                if (bouncinessToken != null)
                    mat.bounciness = bouncinessToken.Value<float>();
                if (frictionToken != null)
                    mat.dynamicFriction = frictionToken.Value<float>();
                if (collider != null)
                    collider.material = mat;
                sb.AppendLine($"  + PhysicMaterial (bounce: {mat.bounciness}, friction: {mat.dynamicFriction})");
            }

            // Add Rigidbody
            var rb = Undo.AddComponent<Rigidbody>(go);
            rb.mass = mass;
            rb.linearDamping = drag;
            rb.angularDamping = angularDrag;
            rb.useGravity = useGravity;
            rb.isKinematic = isKinematic;

            // Freeze constraints
            RigidbodyConstraints constraints = RigidbodyConstraints.None;
            var freezePos = args["freeze_position"] as JArray;
            var freezeRot = args["freeze_rotation"] as JArray;
            if (freezePos != null)
            {
                foreach (var axis in freezePos)
                {
                    switch (axis.ToString().ToLowerInvariant())
                    {
                        case "x": constraints |= RigidbodyConstraints.FreezePositionX; break;
                        case "y": constraints |= RigidbodyConstraints.FreezePositionY; break;
                        case "z": constraints |= RigidbodyConstraints.FreezePositionZ; break;
                    }
                }
            }
            if (freezeRot != null)
            {
                foreach (var axis in freezeRot)
                {
                    switch (axis.ToString().ToLowerInvariant())
                    {
                        case "x": constraints |= RigidbodyConstraints.FreezeRotationX; break;
                        case "y": constraints |= RigidbodyConstraints.FreezeRotationY; break;
                        case "z": constraints |= RigidbodyConstraints.FreezeRotationZ; break;
                    }
                }
            }
            rb.constraints = constraints;

            sb.AppendLine($"  + Rigidbody (mass: {mass}, drag: {drag}, angularDrag: {angularDrag})");
            sb.AppendLine($"    useGravity: {useGravity}, isKinematic: {isKinematic}");
            if (constraints != RigidbodyConstraints.None)
                sb.AppendLine($"    constraints: {constraints}");

            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TOOL REGISTRY
    // ─────────────────────────────────────────────────────────────────────────

    public static class UnaiAssistantToolsFactory
    {
        public static UnaiToolRegistry CreateEditorToolRegistry()
        {
            var registry = new UnaiToolRegistry();

            // Scene inspection
            registry.Register(new InspectSceneTool());
            registry.Register(new FindGameObjectTool());
            registry.Register(new GetSelectionTool());

            // GameObject manipulation
            registry.Register(new CreateGameObjectTool());
            registry.Register(new ModifyGameObjectTool());
            registry.Register(new InspectGameObjectTool());
            registry.Register(new CreatePrefabTool());

            // Materials & Lighting
            registry.Register(new CreateMaterialTool());
            registry.Register(new CreateLightTool());

            // Components
            registry.Register(new AddComponentConfiguredTool());

            // Scripts & Assets
            registry.Register(new ReadScriptTool());
            registry.Register(new CreateScriptTool());
            registry.Register(new ModifyScriptTool());
            registry.Register(new ListAssetsTool());
            registry.Register(new SearchProjectTool());

            // Duplication & organization
            registry.Register(new DuplicateGameObjectTool());
            registry.Register(new SetLayerTagTool());

            // Project settings
            registry.Register(new GetProjectSettingsTool());

            // Scene view
            registry.Register(new FocusSceneViewTool());

            // Physics
            registry.Register(new CreatePhysicsSetupTool());

            // Editor control
            registry.Register(new ExecuteMenuItemTool());
            registry.Register(new UndoTool());
            registry.Register(new GetConsoleLogsTool());
            registry.Register(new LogMessageTool());

            return registry;
        }
    }
}
