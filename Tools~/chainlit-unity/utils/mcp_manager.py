"""
MCP Manager - 管理多个 MCP 服务器连接
"""
import os
import asyncio
from typing import Dict, List, Optional, Any
from dataclasses import dataclass
import toml
from mcp import ClientSession, StdioServerParameters, stdio_server
from mcp.client.stdio import stdio_client
from mcp.client.sse import sse_client


@dataclass
class McpServerConfig:
    name: str
    server_type: str  # "stdio" | "streamable-http" | "sse"
    url: Optional[str] = None
    command: Optional[str] = None
    args: Optional[List[str]] = None
    enabled: bool = True


@dataclass
class McpTool:
    name: str
    description: str
    input_schema: Dict[str, Any]


def _get_default_config_path() -> str:
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base_dir, "config", "mcp_servers.toml")


class McpManager:
    def __init__(self, config_path: str = None):
        self.config_path = config_path or _get_default_config_path()
        self.servers: Dict[str, McpServerConfig] = {}
        self.sessions: Dict[str, ClientSession] = {}
        self.tools: Dict[str, List[McpTool]] = {}
        self._config_loaded = False

    def load_config(self) -> bool:
        """从配置文件加载 MCP 服务器配置"""
        if not os.path.exists(self.config_path):
            print(f"[MCP Manager] Config file not found: {self.config_path}")
            return False

        try:
            config = toml.load(self.config_path)

            # 加载 MCP 服务器配置
            mcp_servers = config.get("mcp_servers", [])
            for server in mcp_servers:
                name = server.get("name", "")
                server_type = server.get("type", "streamable-http")

                if not server.get("enabled", True):
                    continue

                self.servers[name] = McpServerConfig(
                    name=name,
                    server_type=server_type,
                    url=server.get("url"),
                    command=server.get("command"),
                    args=server.get("args", []),
                    enabled=True
                )

            self._config_loaded = True
            print(f"[MCP Manager] Loaded {len(self.servers)} MCP server configs")
            return True

        except Exception as e:
            print(f"[MCP Manager] Failed to load config: {e}")
            return False

    async def connect_all(self) -> Dict[str, bool]:
        """连接所有已启用的 MCP 服务器"""
        if not self._config_loaded:
            self.load_config()

        results = {}

        for name, server in self.servers.items():
            if not server.enabled:
                continue

            success = await self.connect(name, server)
            results[name] = success

            if success:
                await self.list_tools(name)

        return results

    async def connect(self, name: str, server: McpServerConfig = None) -> bool:
        """连接到单个 MCP 服务器"""
        if server is None:
            server = self.servers.get(name)
            if server is None:
                print(f"[MCP Manager] Server '{name}' not found")
                return False

        try:
            if server.server_type == "streamable-http":
                client = sse_client(url=server.url)
            elif server.server_type == "stdio":
                client = stdio_server(
                    StdioServerParameters(
                        command=server.command,
                        args=server.args
                    )
                )
            else:
                print(f"[MCP Manager] Unsupported server type: {server.server_type}")
                return False

            read, write = await client.__aenter__()
            session = await ClientSession(read, write).__aenter__()

            self.sessions[name] = session
            print(f"[MCP Manager] Connected to MCP server: {name}")
            return True

        except Exception as e:
            print(f"[MCP Manager] Failed to connect to '{name}': {e}")
            return False

    async def disconnect(self, name: str):
        """断开 MCP 服务器连接"""
        if name in self.sessions:
            try:
                await self.sessions[name].__aexit__(None, None, None)
            except:
                pass
            del self.sessions[name]

        if name in self.tools:
            del self.tools[name]

        print(f"[MCP Manager] Disconnected from MCP server: {name}")

    async def list_tools(self, name: str) -> List[McpTool]:
        """列出 MCP 服务器的工具"""
        if name not in self.sessions:
            return []

        try:
            result = await self.sessions[name].list_tools()
            tools = []

            for tool in result.tools:
                tools.append(McpTool(
                    name=tool.name,
                    description=tool.description,
                    input_schema=tool.inputSchema
                ))

            self.tools[name] = tools
            print(f"[MCP Manager] Server '{name}' has {len(tools)} tools")
            return tools

        except Exception as e:
            print(f"[MCP Manager] Failed to list tools from '{name}': {e}")
            return []

    def get_all_tools(self) -> List[Dict[str, Any]]:
        """获取所有 MCP 服务器的工具（合并）"""
        all_tools = []

        for name, tools in self.tools.items():
            for tool in tools:
                all_tools.append({
                    "name": tool.name,
                    "description": tool.description,
                    "input_schema": tool.input_schema
                })

        return all_tools

    async def call_tool(self, name: str, tool_name: str, arguments: Dict[str, Any]) -> Any:
        """调用 MCP 工具"""
        if name not in self.sessions:
            raise ValueError(f"Server '{name}' not connected")

        try:
            result = await self.sessions[name].call_tool(tool_name, arguments)
            return result
        except Exception as e:
            print(f"[MCP Manager] Tool call failed: {e}")
            raise

    def get_server_names(self) -> List[str]:
        """获取已连接的服务器名称"""
        return list(self.sessions.keys())

    async def close_all(self):
        """关闭所有 MCP 连接"""
        for name in list(self.sessions.keys()):
            await self.disconnect(name)


# 全局 MCP 管理器实例
_mcp_manager: Optional[McpManager] = None


def get_mcp_manager(config_path: str = None) -> McpManager:
    """获取全局 MCP 管理器实例"""
    global _mcp_manager
    if _mcp_manager is None:
        _mcp_manager = McpManager(config_path)
    return _mcp_manager
