// using System;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using UnityEngine;
//
// namespace UnAI.Resource
// {
//     public delegate Task<McpResourceContent> McpResourceReader(string uri);
//
//     /// <summary>
//     /// Registry for MCP resources. Register URI patterns with read handlers.
//     /// </summary>
//     public class McpResourceRegistry
//     {
//         private readonly List<(McpResource info, McpResourceReader reader)> _resources
//             = new List<(McpResource, McpResourceReader)>();
//
//         private static McpResourceRegistry _instance;
//         public static McpResourceRegistry Instance => _instance ??= new McpResourceRegistry();
//
//         public void Register(McpResource info, McpResourceReader reader)
//         {
//             _resources.Add((info, reader));
//             Debug.Log($"[McpResourceRegistry] Registered resource: {info.uri}");
//         }
//
//         public IEnumerable<McpResource> GetAllResources()
//         {
//             foreach (var r in _resources)
//                 yield return r.info;
//         }
//
//         public async Task<McpResourceContent> ReadAsync(string uri)
//         {
//             foreach (var (info, reader) in _resources)
//             {
//                 if (info.uri == uri || UriMatches(info.uri, uri))
//                     return await reader(uri);
//             }
//             return null;
//         }
//
//         private static bool UriMatches(string pattern, string uri)
//         {
//             // Simple prefix matching for patterns ending with *
//             if (pattern.EndsWith("*"))
//                 return uri.StartsWith(pattern.Substring(0, pattern.Length - 1));
//             return pattern == uri;
//         }
//
//         public void Clear() => _resources.Clear();
//     }
// }