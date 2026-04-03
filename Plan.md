最终实现计划
系统架构
┌─────────────────────────────────────────────────────────────┐
│                      Chainlit (Python)                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐   │
│  │ 聊天界面    │  │ MCP Client  │  │ HTTP Client        │   │
│  │             │  │ (连接多MCP) │  │ → Unity 17997      │   │
│  └─────────────┘  └─────────────┘  └─────────────────────┘   │
│         │                │                     │             │
│         │           获取工具                   │             │
│         │                │                     │             │
│         │          工具列表+消息 ──────────────┼──────────┐   │
│         │                                 │          │   │
│         ▼                ┌────────────────▼──────────┘   ▼   │
│  显示流式消息       实时SSE事件显示              结果显示    │
└─────────────────────────────────────────────────────────────┘
│
┌────────────────────┼────────────────────┐
│                    │                    │
▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ MCP Server      │  │ Agent HTTP API  │  │ 其他MCP Servers │
│ :8080           │  │ :17997          │  │                 │
│ (Unity工具)     │  │ (LLM+Agent)     │  │                 │
└─────────────────┘  └─────────────────┘  └─────────────────┘
Phase 1: Unity端 - Agent HTTP API (端口17997)
新建文件
文件
说明
Scripts/Runtime/Agent/Models/AgentApiModels.cs
请求/响应模型
Scripts/Runtime/Agent/UnaiAgentHttpServer.cs
HTTP服务器
API端点
端点
方法
功能
/agent/chat
POST
简单聊天（单轮，无工具）
/agent/run
POST
Agent执行（多步+工具+流式）
/agent/reset
POST
重置会话
/health
GET
健康检查
请求格式 (POST /agent/run)
{
"sessionId": "可选会话ID",
"message": "用户消息",
"mcpTools": [
{"name": "tool1", "description": "...", "inputSchema": {...}}
],
"config": {
"maxSteps": 10,
"timeoutSeconds": 300,
"systemPrompt": "可选"
},
"apiKey": "认证密钥"
}
流式响应 (SSE)
event: thinking
data: {"step": 1, "messageCount": 3, "estimatedTokens": 500}

event: tool_call
data: {"step": 1, "tool": "CreateGameObject", "args": {...}}

event: tool_result
data: {"step": 1, "tool": "CreateGameObject", "result": "Created!", "isError": false}

event: delta
data: {"content": "正在创建", "step": 1}

event: complete
data: {"content": "完成", "stopReason": "completed"}
错误响应格式
{
"error": "认证失败" | "连接MCP服务器失败" | "Agent执行超时"
}
Phase 2: Chainlit端 - 应用完善
文件结构
Tools~/chainlit-unity/
├── app.py                         # 主应用
├── config/
│   └── mcp_servers.toml          # MCP服务器配置
└── utils/
├── http_client.py            # Unity HTTP客户端
├── mcp_manager.py            # MCP连接管理
└── agent_loop.py             # 流式响应处理
MCP配置 (mcp_servers.toml)
[[mcp_servers]]
name = "unity"
type = "streamable-http"
url = "http://localhost:8080/mcp"

[[mcp_servers]]
name = "filesystem"
type = "stdio"
command = "npx"
args = ["-y", "@modelcontextprotocol/filesystem", "/path"]
Chainlit流程
1. @cl.on_chat_start
   └── 初始化MCP连接（读取配置文件）
   └── 连接成功后获取工具列表

2. @cl.on_message
   └── 获取所有MCP工具列表
   └── 发送HTTP POST到 Unity:17997/agent/run
   └── 解析SSE事件并显示

3. @cl.on_mcp_connect/disconnect
   └── 管理MCP连接状态
   Phase 3: 修改现有Unity代码
   修改文件
   文件
   修改内容
   UnaiAgent.cs
   支持外部工具列表传入、流式回调
   认证机制
   •
   API Key通过请求头或body传递
   •
   错误消息通过Chainlit消息框显示
   实施顺序
1.
Phase 1.1: 创建API模型 (AgentApiModels.cs)
2.
Phase 1.2: 创建HTTP服务器 (UnaiAgentHttpServer.cs)
3.
Phase 1.3: 修改 UnaiAgent.cs 支持外部工具+流式
4.
Phase 2.1: 完善Chainlit MCP管理
5.
Phase 2.2: 完善Chainlit HTTP客户端
6.
Phase 2.3: 完善主应用 app.py
7.
测试: 端到端测试
请确认计划是否可以开始实施？