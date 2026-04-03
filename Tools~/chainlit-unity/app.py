"""
Chainlit App - UnAI Agent 聊天界面
支持 MCP 工具和 Unity Agent API
"""

import os
import asyncio
import chainlit as cl
from typing import Dict, List, Optional, Any
import toml

from utils.mcp_manager import McpManager, get_mcp_manager
from utils.http_client import UnityHttpClient, AgentConfig, get_http_client


# 全局状态
mcp_manager: Optional[McpManager] = None
http_client: Optional[UnityHttpClient] = None
current_session_id: Optional[str] = None


def load_app_config():
    """加载应用配置"""
    base_dir = os.path.dirname(os.path.abspath(__file__))
    config_path = os.path.join(base_dir, "config", "mcp_servers.toml")

    if os.path.exists(config_path):
        try:
            config = toml.load(config_path)
            openai_config = config.get("openai", {})
            return {
                "model": openai_config.get("model", "gpt-4o"),
                "api_key": os.environ.get(
                    "OPENAI_API_KEY", openai_config.get("api_key", "")
                ),
            }
        except Exception as e:
            print(f"[App] Failed to load config: {e}")

    return {"model": "gpt-4o", "api_key": ""}


@cl.on_chat_start
async def start():
    """聊天开始时初始化"""
    global mcp_manager, http_client, current_session_id

    # 初始化 MCP 管理器
    mcp_manager = get_mcp_manager()

    # 加载 MCP 服务器配置
    mcp_manager.load_config()

    # 连接所有 MCP 服务器
    try:
        await mcp_manager.connect_all()
        connected_servers = mcp_manager.get_server_names()

        if connected_servers:
            tools_count = len(mcp_manager.get_all_tools())
            await cl.Message(
                content=f"已连接到 {len(connected_servers)} 个 MCP 服务器，共 {tools_count} 个工具可用"
            ).send()
        else:
            await cl.Message(
                content="未连接到任何 MCP 服务器。请在配置文件中添加 MCP 服务器。"
            ).send()
    except Exception as e:
        await cl.Message(content=f"MCP 连接失败：{e}").send()

    # 初始化 HTTP 客户端
    http_client = get_http_client()

    # 健康检查
    health = await http_client.health_check()
    if health.get("error"):
        await cl.Message(
            content=f"⚠️ Unity Agent 服务器未启动\n\n"
            f"请在 Unity 中运行场景，并确保已添加 UnaiAgentHttpServer 组件。\n\n"
            f"错误详情：{health.get('error')}"
        ).send()
    else:
        await cl.Message(
            content=f"✅ Unity Agent 服务器已连接 (版本：{health.get('version', 'unknown')})"
        ).send()


@cl.on_message
async def main(message: cl.Message):
    """处理用户消息"""
    global mcp_manager, http_client, current_session_id

    user_message = message.content

    # 显示处理中状态
    msg = cl.Message(content="")
    await msg.send()

    try:
        # 优先使用 on_mcp_connect 中保存的工具列表
        mcp_tools_dict = cl.user_session.get("mcp_tools", {})

        # 合并所有 MCP 服务器的工具
        mcp_tools = []
        for tools in mcp_tools_dict.values():
            mcp_tools.extend(tools)

        # 如果没有找到工具，回退到 mcp_manager
        if not mcp_tools and mcp_manager:
            mcp_tools = mcp_manager.get_all_tools()

        if mcp_tools:
            # 有 MCP 工具，使用 Agent 模式
            result = await http_client.run_agent_stream(
                message=user_message,
                mcp_tools=mcp_tools,
                session_id=current_session_id,
                config=AgentConfig(max_steps=10),
                on_thinking=lambda data: update_thinking(msg, data),
                on_tool_call=lambda data: update_tool_call(msg, data),
                on_tool_result=lambda data: update_tool_result(msg, data),
                on_delta=lambda data: update_delta(msg, data),
                on_complete=lambda data: update_complete(msg, data),
                on_error=lambda err: show_error(msg, err),
            )

            if result.get("error"):
                await cl.Message(content=f"错误：{result.get('error')}").send()
                return

            current_session_id = result.get("sessionId") or current_session_id
            final_content = result.get("content", "")

            if final_content:
                msg.content = final_content
                await msg.update()
            else:
                await msg.remove()

        else:
            # 无 MCP 工具，使用简单聊天
            result = await http_client.chat(
                message=user_message, session_id=current_session_id
            )

            if result.get("error"):
                await cl.Message(content=f"错误：{result.get('error')}").send()
                return

            current_session_id = result.get("sessionId") or current_session_id
            await cl.Message(content=result.get("content", "")).send()
            await msg.remove()

    except Exception as e:
        await cl.Message(content=f"处理消息时出错：{e}").send()


@cl.on_mcp_connect
async def on_mcp_connect(connection, session):
    """MCP 连接时处理"""
    try:
        # 获取可用工具列表
        result = await session.list_tools()

        # 处理工具元数据
        tools = [
            {
                "name": t.name,
                "description": t.description,
                "input_schema": t.inputSchema,
            }
            for t in result.tools
        ]

        # 存储到用户会话中
        mcp_tools = cl.user_session.get("mcp_tools", {})
        mcp_tools[connection.name] = tools
        cl.user_session.set("mcp_tools", mcp_tools)

        await cl.Message(
            content=f"已连接到 MCP 服务器：{connection.name} ({len(tools)} 个工具)"
        ).send()
    except Exception as e:
        await cl.Message(content=f"MCP 连接处理失败：{e}").send()


@cl.on_mcp_disconnect
async def on_mcp_disconnect(name: str, session):
    """MCP 断开时处理"""
    # 从用户会话中移除该服务器的工具
    mcp_tools = cl.user_session.get("mcp_tools", {})
    if name in mcp_tools:
        del mcp_tools[name]
        cl.user_session.set("mcp_tools", mcp_tools)

    await cl.Message(content=f"已断开 MCP 服务器：{name}").send()


# ===== SSE 事件处理函数 =====


def update_thinking(msg: cl.Message, data: Dict):
    """更新思考状态"""
    step = data.get("step", "?")
    msg.content = f"🤔 Step {step} - 思考中..."
    asyncio.create_task(msg.update())


def update_tool_call(msg: cl.Message, data: Dict):
    """更新工具调用"""
    step = data.get("step", "?")
    tool = data.get("tool", "?")
    msg.content = f"🔧 Step {step} - 调用工具：{tool}"
    asyncio.create_task(msg.update())


def update_tool_result(msg: cl.Message, data: Dict):
    """更新工具结果"""
    step = data.get("step", "?")
    tool = data.get("tool", "?")
    is_error = data.get("isError", False)
    prefix = "❌" if is_error else "✅"
    msg.content = f"{prefix} Step {step} - {tool} 完成"
    asyncio.create_task(msg.update())


def update_delta(msg: cl.Message, data: Dict):
    """更新流式内容"""
    content = data.get("content", "")
    if content:
        msg.content += content
        asyncio.create_task(msg.update())


def update_complete(msg: cl.Message, data: Dict):
    """更新完成状态"""
    # 内容已在 delta 中累积，这里可以做一些清理
    pass


def show_error(msg: cl.Message, error: str):
    """显示错误"""
    msg.content = f"❌ 错误：{error}"
    asyncio.create_task(msg.update())


# ===== 入口点 =====

if __name__ == "__main__":
    # 开发模式运行
    # chainlit run app.py
    pass
