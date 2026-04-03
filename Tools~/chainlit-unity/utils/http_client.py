"""
Unity HTTP Client - 连接Unity Agent API
"""

import os
import asyncio
import json
import aiohttp
from typing import Dict, List, Optional, Any, Callable, AsyncIterator
from dataclasses import dataclass
import toml


@dataclass
class UnityAgentConfig:
    url: str = "http://localhost:17997"
    api_key: str = "unai-default-key"


@dataclass
class AgentConfig:
    max_steps: int = 10
    timeout_seconds: int = 300
    system_prompt: Optional[str] = None
    model: Optional[str] = None


class UnityHttpClient:
    def __init__(self, config_path: str = None):
        self.config = self._load_config(config_path)
        self.session: Optional[aiohttp.ClientSession] = None
        self._session_id: Optional[str] = None

    def _load_config(self, config_path: str = None) -> UnityAgentConfig:
        """从配置文件加载配置"""
        if config_path is None:
            base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            config_path = os.path.join(base_dir, "config", "mcp_servers.toml")

        if os.path.exists(config_path):
            try:
                config = toml.load(config_path)
                unity_config = config.get("unity", {})
                return UnityAgentConfig(
                    url=unity_config.get("url", "http://localhost:17997"),
                    api_key=unity_config.get("api_key", "unai-default-key"),
                )
            except Exception as e:
                print(f"[HTTP Client] Failed to load config: {e}")

        return UnityAgentConfig()

    async def _get_session(self) -> aiohttp.ClientSession:
        """获取或创建HTTP会话"""
        if self.session is None or self.session.closed:
            from urllib.parse import urlparse

            parsed = urlparse(self.config.url)
            host_header = parsed.netloc if parsed.netloc else parsed.hostname
            self.session = aiohttp.ClientSession(headers={"Host": host_header})
        return self.session

    async def close(self):
        """关闭HTTP会话"""
        if self.session and not self.session.closed:
            await self.session.close()

    async def health_check(self) -> Dict[str, Any]:
        """健康检查"""
        session = await self._get_session()
        url = f"{self.config.url}/health"

        try:
            async with session.get(url) as response:
                text = await response.text()
                if response.status == 200:
                    try:
                        return await response.json()
                    except:
                        return {"raw": text}
                return {"error": f"HTTP {response.status}", "detail": text[:500]}
        except Exception as e:
            return {"error": str(e)}

    async def reset_session(self, session_id: str) -> bool:
        """重置会话"""
        session = await self._get_session()
        url = f"{self.config.url}/agent/reset"

        try:
            async with session.post(url, json={"sessionId": session_id}) as response:
                return response.status == 200
        except Exception as e:
            print(f"[HTTP Client] Reset session failed: {e}")
            return False

    async def chat(
        self,
        message: str,
        session_id: Optional[str] = None,
        config: Optional[AgentConfig] = None,
    ) -> Dict[str, Any]:
        """
        简单聊天（单轮，无Agent循环）
        """
        session = await self._get_session()
        url = f"{self.config.url}/agent/chat"

        payload = {
            "message": message,
            "sessionId": session_id or self._session_id,
            "apiKey": self.config.api_key,
        }

        if config:
            payload["config"] = {
                "maxSteps": config.max_steps,
                "timeoutSeconds": config.timeout_seconds,
                "systemPrompt": config.system_prompt,
                "model": config.model,
            }

        try:
            async with session.post(url, json=payload) as response:
                if response.status == 200:
                    result = await response.json()
                    self._session_id = result.get("sessionId")
                    return result
                elif response.status == 401:
                    return {"error": "Unauthorized - Invalid API key"}
                else:
                    text = await response.text()
                    return {"error": f"HTTP {response.status}", "detail": text[:500]}
        except Exception as e:
            return {"error": str(e)}

    async def run_agent_stream(
        self,
        message: str,
        mcp_tools: Optional[List[Dict[str, Any]]] = None,
        session_id: Optional[str] = None,
        config: Optional[AgentConfig] = None,
        on_thinking: Optional[Callable[[Dict], None]] = None,
        on_tool_call: Optional[Callable[[Dict], None]] = None,
        on_tool_result: Optional[Callable[[Dict], None]] = None,
        on_delta: Optional[Callable[[Dict], None]] = None,
        on_complete: Optional[Callable[[Dict], None]] = None,
        on_error: Optional[Callable[[str], None]] = None,
    ) -> Dict[str, Any]:
        """
        Agent执行（多步+工具+流式）
        """
        session = await self._get_session()
        url = f"{self.config.url}/agent/run"

        payload = {
            "message": message,
            "sessionId": session_id or self._session_id,
            "apiKey": self.config.api_key,
        }

        if mcp_tools:
            payload["mcpTools"] = mcp_tools

        if config:
            payload["config"] = {
                "maxSteps": config.max_steps,
                "timeoutSeconds": config.timeout_seconds,
                "systemPrompt": config.system_prompt,
                "model": config.model,
            }

        try:
            async with session.post(url, json=payload) as response:
                text = await response.text()
                if response.status != 200:
                    if response.status == 401:
                        if on_error:
                            on_error("Unauthorized - Invalid API key")
                        return {"error": "Unauthorized", "detail": text[:500]}
                    if on_error:
                        on_error(f"HTTP {response.status}: {text[:200]}")
                    return {"error": f"HTTP {response.status}", "detail": text[:500]}

                # 解析SSE流
                result = await self._parse_sse_stream(
                    response,
                    on_thinking,
                    on_tool_call,
                    on_tool_result,
                    on_delta,
                    on_complete,
                    on_error,
                )

                if result.get("sessionId"):
                    self._session_id = result["sessionId"]

                return result

        except Exception as e:
            error_msg = str(e)
            if on_error:
                on_error(error_msg)
            return {"error": error_msg}

    async def _parse_sse_stream(
        self,
        response: aiohttp.ClientResponse,
        on_thinking: Optional[Callable],
        on_tool_call: Optional[Callable],
        on_tool_result: Optional[Callable],
        on_delta: Optional[Callable],
        on_complete: Optional[Callable],
        on_error: Optional[Callable],
    ) -> Dict[str, Any]:
        """解析SSE流响应"""
        accumulated_content = ""
        session_id = None
        final_result = None
        stop_reason = None

        async for line in response.content:
            try:
                line = line.decode("utf-8").strip()
                if not line:
                    continue

                # 解析SSE格式: event: type\ndata: {...}\n\n
                if line.startswith("event:"):
                    event_type = line[6:].strip()
                elif line.startswith("data:"):
                    data_str = line[5:].strip()
                    try:
                        data = json.loads(data_str)
                    except:
                        continue

                    # 处理不同事件类型
                    if event_type == "thinking":
                        if on_thinking:
                            on_thinking(data)
                    elif event_type == "tool_call":
                        if on_tool_call:
                            on_tool_call(data)
                    elif event_type == "tool_result":
                        if on_tool_result:
                            on_tool_result(data)
                    elif event_type == "delta":
                        content = data.get("content", "")
                        accumulated_content += content
                        if on_delta:
                            on_delta(data)
                    elif event_type == "complete":
                        accumulated_content = data.get("content", accumulated_content)
                        stop_reason = data.get("stopReason")
                        final_result = data
                        if on_complete:
                            on_complete(data)
                    elif event_type == "error":
                        error_msg = data.get("message", "Unknown error")
                        if on_error:
                            on_error(error_msg)

            except Exception as e:
                if on_error:
                    on_error(f"Stream parse error: {e}")
                continue

        return {
            "sessionId": session_id,
            "content": accumulated_content,
            "stopReason": stop_reason,
            "final": final_result,
        }

    @property
    def session_id(self) -> Optional[str]:
        """获取当前会话ID"""
        return self._session_id


# 全局HTTP客户端实例
_http_client: Optional[UnityHttpClient] = None


def get_http_client(config_path: str = None) -> UnityHttpClient:
    """获取全局HTTP客户端实例"""
    global _http_client
    if _http_client is None:
        _http_client = UnityHttpClient(config_path)
    return _http_client
