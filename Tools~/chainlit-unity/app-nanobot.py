"""
Chainlit app for nanobot
This script enables web-based chat interface for nanobot.
Supports custom port via command line arguments.
"""

import chainlit as cl
from chainlit.input_widget import Select, Slider, Switch
from typing import Dict, Any
import asyncio
from mcp import ClientSession


@cl.on_chat_start
async def start():
    """
    This function is called when a new chat starts
    """
    await cl.Message(content="你好！我是 nanobot，你的本地 AI 助手。有什么我可以帮你的吗？").send()

@cl.on_message  # this function will be called every time a user inputs a message in the UI
async def main(message: cl.Message):
    """
    Main logic for handling messages from the user
    """
    try:
        # 显示处理中状态
        await cl.Message(content="发送中...").send()

        try:
            # 正确调用 nanobot agent
            process = await asyncio.create_subprocess_exec(
                'nanobot', 'agent', '--message', message.content,
                '--session', 'chainlit:' + str(message.id),
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE
            )
            stdout, stderr = await process.communicate()

            if process.returncode == 0:
                response_text = stdout.decode('utf-8', errors='ignore').strip()
            else:
                error_msg = stderr.decode('utf-8', errors='ignore')
                response_text = f"Error calling nanobot agent:\n\n{error_msg}"
        except Exception as e:
            response_text = f"Exception occurred: {str(e)}"

        # 发送响应
        await cl.Message(content=response_text).send()

    except Exception as e:
        await cl.Message(content=f"发生错误: {str(e)}").send()


@cl.on_mcp_connect
async def on_mcp_connect(connection, session: ClientSession):
    """Called when an MCP connection is established"""
    # Your connection initialization code here
    # This handler is required for MCP to work
    # 获取MCP服务器提供的所有工具列表
    result = await session.list_tools()

    # 将工具信息存储在用户会话中，供后续使用
    all_tools = [{"name": t.name, "description": t.description} for t in result.tools]
    cl.user_session.set("mcp_tools", all_tools)

    await cl.Message(content=f"已连接到 {connection.name}，发现 {len(all_tools)} 个工具！").send()

@cl.on_mcp_disconnect
async def on_mcp_disconnect(name: str, session: ClientSession):
    """Called when an MCP connection is terminated"""
    # Your cleanup code here
    # This handler is optional
    await cl.Message(content=f"已断开 MCP 服务器：{name}").send()