using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnAI.MCP;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.Tests
{
    public class McpResourceTests
    {
        private static JObject Call(string method, JObject parameters = null)
        {
            var request = new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
            if (parameters != null) request["params"] = parameters;
            string reply = UnaiMcpProtocol.HandleRequest(request.ToString(), new UnaiToolRegistry(), CancellationToken.None)
                .GetAwaiter().GetResult();
            return JObject.Parse(reply);
        }

        [TearDown]
        public void RemoveCustom() => UnaiMcpResources.Unregister("test://notes");

        [Test]
        public void InitializeAdvertisesResources()
        {
            var result = Call("initialize", new JObject { ["protocolVersion"] = "2025-11-25" })["result"];
            Assert.IsNotNull(result["capabilities"]["resources"]);
            Assert.IsNotNull(result["capabilities"]["tools"]);
        }

        [Test]
        public void ListsTheBuiltInResources()
        {
            var uris = Call("resources/list")["result"]["resources"].Select(r => r["uri"].ToString()).ToList();
            CollectionAssert.IsSupersetOf(uris, new[]
            {
                "unity://project/info", "unity://scene/hierarchy", "unity://selection", "unity://console/recent"
            });
        }

        [Test]
        public void ReadsProjectInfoAndRecentConsole()
        {
            var project = Call("resources/read", new JObject { ["uri"] = "unity://project/info" })["result"]["contents"][0];
            Assert.AreEqual("unity://project/info", project["uri"].ToString());
            StringAssert.Contains(Application.unityVersion, project["text"].ToString());

            Debug.Log("mcp resource test marker");
            var console = Call("resources/read", new JObject { ["uri"] = "unity://console/recent" })["result"]["contents"][0];
            StringAssert.Contains("mcp resource test marker", console["text"].ToString());
        }

        [Test]
        public void SceneHierarchyListsObjectsWithComponents()
        {
            var go = new GameObject("McpResourceProbe", typeof(BoxCollider));
            try
            {
                string text = Call("resources/read", new JObject { ["uri"] = "unity://scene/hierarchy" })["result"]["contents"][0]["text"].ToString();
                StringAssert.Contains("McpResourceProbe [BoxCollider]", text);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CustomResourcesCanBeRegistered()
        {
            UnaiMcpResources.Register(new UnaiMcpResource
            {
                Uri = "test://notes", Name = "Notes", MimeType = "text/markdown",
                Read = _ => Task.FromResult("# Level design notes")
            });
            var content = Call("resources/read", new JObject { ["uri"] = "test://notes" })["result"]["contents"][0];
            Assert.AreEqual("text/markdown", content["mimeType"].ToString());
            Assert.AreEqual("# Level design notes", content["text"].ToString());
            Assert.Throws<System.ArgumentException>(() => UnaiMcpResources.Register(new UnaiMcpResource
            {
                Uri = "unity://project/info", Read = _ => Task.FromResult("")
            }), "Built-ins can't be replaced");
        }

        [Test]
        public void UnknownOrMissingUrisAreErrors()
        {
            Assert.AreEqual(-32002, (int)Call("resources/read", new JObject { ["uri"] = "unity://nope" })["error"]["code"]);
            Assert.AreEqual(-32602, (int)Call("resources/read", new JObject())["error"]["code"]);
        }
    }
}
