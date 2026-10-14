"""Loopback-only WebGL harness host plus controlled cross-origin NDJSON fixtures."""
import argparse
import json
import threading
import time
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("build", type=Path)
parser.add_argument("reports", type=Path)
args = parser.parse_args()
build, reports = args.build.resolve(), args.reports.resolve()
reports.mkdir(parents=True, exist_ok=True)
origin = "http://localhost:8765"

class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, ".wasm": "application/wasm", ".data": "application/octet-stream"}

    def log_message(self, format, *arguments):
        pass

    def cors(self):
        if not self.path.startswith("/no-cors") and self.headers.get("Origin") == origin:
            self.send_header("Access-Control-Allow-Origin", origin)
            self.send_header("Vary", "Origin")
            self.send_header("Access-Control-Allow-Headers", "Content-Type, Accept")
            self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")

    def do_OPTIONS(self):
        self.record("OPTIONS")
        self.send_response(204)
        self.cors()
        self.end_headers()

    def do_GET(self):
        if self.path == "/":
            loaders = sorted((build / "Build").glob("*.loader.js"))
            if not loaders:
                self.send_error(503, "Unity build not ready")
                return
            name = loaders[0].name.removesuffix(".loader.js")
            page = f'''<!doctype html><html><head><meta charset="utf-8"><title>UNAI WebGL browser validation</title></head>
<body style="background:#111827;color:#e5e7eb;font:16px system-ui;padding:24px">
<h1>UNAI WebGL browser validation</h1><p id="status" role="status">Loading Unity player…</p>
<button id="run" disabled onclick="window.unaiPlayer.SendMessage('UNAI Browser Validation','Run','')">Run browser checks</button>
<canvas id="unity" width="480" height="100" style="display:block;margin-top:16px"></canvas>
<table style="margin:20px 0;border-collapse:collapse"><thead><tr><th align="left">Check</th><th align="left">Result</th><th align="left">Details</th></tr></thead><tbody id="checks"></tbody></table>
<details><summary>Full report</summary><pre id="report" style="white-space:pre-wrap"></pre></details><script src="Build/{name}.loader.js"></script><script>
new MutationObserver(function(){{
  var data;try{{data=JSON.parse(document.getElementById('report').textContent);}}catch(e){{return;}}
  var table=document.getElementById('checks');table.replaceChildren();
  data.checks.forEach(function(check){{var row=document.createElement('tr');
    var detail=check.delivery ? check.delivery+'; first chunk '+check.firstDeltaSeconds.toFixed(2)+'s' : check.settledSeconds ? check.settledSeconds.toFixed(2)+'s' : '';
    [check.name,check.result||'running',detail].forEach(function(value){{var cell=document.createElement('td');cell.textContent=value;cell.style.padding='8px 24px 8px 0';row.appendChild(cell);}});table.appendChild(row);
  }});
}}).observe(document.getElementById('report'),{{childList:true,subtree:true,characterData:true}});
createUnityInstance(document.getElementById('unity'), {{dataUrl:'Build/{name}.data',frameworkUrl:'Build/{name}.framework.js',codeUrl:'Build/{name}.wasm',companyName:'UNAI',productName:'Browser validation',productVersion:'test',showBanner:function(message){{document.getElementById('status').textContent=message;}}}})
.then(function(player){{window.unaiPlayer=player;document.getElementById('run').disabled=false;}})
.catch(function(error){{document.getElementById('status').textContent='Player failed: '+error;}});
</script></body></html>'''.encode()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(page)))
            self.end_headers()
            self.wfile.write(page)
            return
        super().do_GET()

    def do_POST(self):
        self.record("POST")
        size = int(self.headers.get("Content-Length", "0"))
        if size < 0 or size > 200000:
            self.send_error(413)
            return
        body = json.loads(self.rfile.read(size))
        if self.path == "/report":
            if self.server.server_port != 8765 or self.headers.get("Origin") != origin:
                self.send_error(403)
                return
            (reports / "webgl-browser.json").write_text(json.dumps(body, indent=2) + "\n", encoding="utf-8")
            self.send_response(204)
            self.end_headers()
            return
        prompt = body.get("messages", [{}])[0].get("content", "")
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson")
        self.cors()
        self.end_headers()
        try:
            def send(value):
                self.wfile.write((json.dumps(value, ensure_ascii=False) + "\n").encode())
                self.wfile.flush()
            if prompt == "fixture:malformed":
                self.wfile.write(b"not-json\n")
            elif prompt == "fixture:stall":
                time.sleep(5)
                send({"message": {"content": "late"}, "done": True})
            elif prompt == "fixture:slow":
                # Each newline is flushed. The Unicode payload is split across byte writes too.
                line = (json.dumps({"message": {"content": "café 😀"}, "done": False}, ensure_ascii=False) + "\n").encode()
                emoji = line.index("😀".encode())
                for part in [line[:emoji + 1], line[emoji + 1:emoji + 3], line[emoji + 3:]]:
                    self.wfile.write(part)
                    self.wfile.flush()
                    time.sleep(0.075)
                time.sleep(2)
                send({"message": {"content": " end"}, "done": False})
                time.sleep(2)
                send({"done": True, "done_reason": "stop", "prompt_eval_count": 2, "eval_count": 3})
            else:
                send({"message": {"content": "fixture reply"}, "done": True})
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass

    def record(self, method):
        if self.path == "/report": return
        with (reports / "fixture-requests.jsonl").open("a", encoding="utf-8") as output:
            output.write(json.dumps({"method": method, "path": self.path, "origin": self.headers.get("Origin"),
                "requestedHeaders": self.headers.get("Access-Control-Request-Headers")}) + "\n")

servers = [ThreadingHTTPServer(("127.0.0.1", port), partial(Handler, directory=str(build))) for port in (8765, 8766)]
for server in servers:
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
print("UNAI browser harness: http://localhost:8765; fixtures: http://localhost:8766", flush=True)
try:
    threading.Event().wait()
except KeyboardInterrupt:
    pass
finally:
    for server in servers:
        server.shutdown()
        server.server_close()
