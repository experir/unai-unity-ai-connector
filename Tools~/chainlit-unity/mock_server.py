"""
Unity Agent HTTP Server Mock - 用于测试Chainlit
模拟Unity Agent API的响应
"""

import asyncio
import json
import random
from aiohttp import web

PORT = 17997
API_KEY = "unai-default-key"


async def health_check(request):
    return web.json_response(
        {
            "status": "ok",
            "server": "UnAI Agent HTTP (Mock)",
            "version": "1.0.0",
            "sessions": 0,
        }
    )


async def agent_chat(request):
    body = await request.json()
    api_key = body.get("apiKey", "")

    if api_key != API_KEY:
        return web.json_response({"error": "Unauthorized"}, status=401)

    message = body.get("message", "")
    session_id = body.get("sessionId", "mock-session-001")

    # 模拟AI回复
    responses = [
        f"我收到了你的消息：{message}",
        f"这是一个模拟回复。你说的是：{message}",
        f"Unity Agent (Mock) 回复：{message}",
    ]

    return web.json_response(
        {
            "sessionId": session_id,
            "content": random.choice(responses),
            "stopReason": "completed",
        }
    )


async def agent_run(request):
    try:
        body = await request.json()
    except:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    api_key = body.get("apiKey", "")

    if api_key != API_KEY:
        return web.json_response({"error": "Unauthorized"}, status=401)

    message = body.get("message", "")
    session_id = body.get("sessionId", "mock-session-001")
    mcp_tools = body.get("mcpTools", [])

    # SSE流式响应
    response = web.StreamResponse(
        status=200,
        reason="OK",
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        },
    )
    await response.prepare(request)

    # 模拟SSE事件
    async def send_event(event_type, data):
        json_data = json.dumps(data, ensure_ascii=False)
        event = f"event: {event_type}\ndata: {json_data}\n\n"
        await response.write(event.encode("utf-8"))
        await asyncio.sleep(0.3)

    try:
        # 思考
        await send_event(
            "thinking", {"step": 1, "messageCount": 2, "estimatedTokens": 50}
        )

        # 如果有工具，模拟工具调用
        if mcp_tools:
            first_tool = mcp_tools[0]
            await send_event(
                "tool_call",
                {
                    "step": 1,
                    "tool": first_tool.get("name", "mock_tool"),
                    "args": {"message": message},
                },
            )

            await send_event(
                "tool_result",
                {
                    "step": 1,
                    "tool": first_tool.get("name", "mock_tool"),
                    "result": f"工具执行成功：{message}",
                    "isError": False,
                },
            )

        # 流式输出
        reply = f"这是Unity Agent的模拟回复。你发送了：{message}"
        for char in reply:
            await send_event("delta", {"content": char, "step": 1})

        # 完成
        await send_event("complete", {"content": reply, "stopReason": "completed"})
    except Exception as e:
        error_event = f'event: error\ndata: {{"message": "{str(e)}"}}\n\n'
        await response.write(error_event.encode("utf-8"))

    await response.write_eof()
    return response


async def agent_reset(request):
    body = await request.json()
    session_id = body.get("sessionId", "")
    return web.json_response({"status": "ok"})


async def handle_404(request):
    return web.json_response({"error": "Not found"}, status=404)


def create_app():
    app = web.Application()
    app.router.add_get("/health", health_check)
    app.router.add_post("/agent/chat", agent_chat)
    app.router.add_post("/agent/run", agent_run)
    app.router.add_post("/agent/reset", agent_reset)
    app.router.add_route("*", "/{tail:.*}", handle_404)
    return app


if __name__ == "__main__":
    print(f"[Mock Server] Starting on http://0.0.0.0:{PORT}")
    print(f"[Mock Server] API Key: {API_KEY}")
    print(f"[Mock Server] Endpoints:")
    print(f"  GET  /health")
    print(f"  POST /agent/chat")
    print(f"  POST /agent/run")
    print(f"  POST /agent/reset")
    print()

    app = create_app()
    web.run_app(app, host="0.0.0.0", port=PORT)
