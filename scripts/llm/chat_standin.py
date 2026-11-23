#!/usr/bin/env python3
"""chat_standin.py — an Ollama-style chat server that is not a model (CE-4681, docs/designs/llm-commander/DESIGN_LLM_Commander.md §5a-3).

HROT's chat-model mind (ChatModelMind) sends POST /api/chat exactly as it would to Ollama. This process answers it, two ways:

  script  answers from a JSON file: a list of Ollama `message` objects ({"content": ..., "tool_calls": [...]}), one per request, the last repeated.
          No file: a fixed commander (run PlatoonAttack on the first named place it sees in the system message, then notes). The laptop recipe's
          dry run uses it (scripts/llm/laptop_run.py --endpoint http://localhost:<port>).
  relay   ⭐ the proof without a GPU: each request body is written AS SENT to <queue>/req-<n>.json; the answer is read from <queue>/resp-<n>.json,
          written by an agent (the Haiku worker) that sees ONLY that request. The answer may be a full Ollama response or just its `message`.

Every request and answer is also appended to <log> (JSON lines). Usage:
    python3 scripts/llm/chat_standin.py --port 11500 --mode script [--answers answers.json]
    python3 scripts/llm/chat_standin.py --port 11500 --mode relay --queue /tmp/relay [--wait 600]
"""
import argparse, json, os, re, sys, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LOCK = threading.Lock()
STATE = {"n": 0}


def scripted_message(args, request, n):
    if args.answers:
        answers = json.load(open(args.answers, encoding="utf-8"))
        return answers[min(n, len(answers) - 1)]
    system = next((m.get("content", "") for m in request.get("messages", []) if m.get("role") == "system"), "")
    place = "house"
    m = re.search(r"PLACES?:\s*([^\n,;]+)", system)
    if m:
        place = m.group(1).strip().split()[0]
    if n == 0:
        call = {"function": {"name": "run_play", "arguments": {"play": "PlatoonAttack", "where": place}}}
        return {"role": "assistant", "content": "", "tool_calls": [call]}
    return {"role": "assistant", "content": "", "tool_calls": [{"function": {"name": "note", "arguments": {"text": f"stand-in turn {n}"}}}]}


def relayed_message(args, body, n):
    req = os.path.join(args.queue, f"req-{n:04d}.json")
    resp = os.path.join(args.queue, f"resp-{n:04d}.json")
    tmp = req + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(body)
    os.replace(tmp, req)   # the worker never sees a half-written request
    deadline = time.time() + args.wait
    while time.time() < deadline:
        if os.path.exists(resp):
            time.sleep(0.05)
            try:
                answer = json.load(open(resp, encoding="utf-8"))
            except json.JSONDecodeError:
                time.sleep(0.2)
                continue
            return answer.get("message", answer) if isinstance(answer, dict) else {"role": "assistant", "content": str(answer)}
        time.sleep(0.2)
    return None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def reply(self, code, obj):
        data = json.dumps(obj).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path.startswith("/api/tags"):   # what `ollama list` asks: the laptop script checks the server this way
            return self.reply(200, {"models": [{"name": f"stand-in-{ARGS.mode}"}]})
        self.reply(404, {"error": "not found"})

    def do_POST(self):
        if not self.path.startswith("/api/chat"):
            return self.reply(404, {"error": "only /api/chat"})
        body = self.rfile.read(int(self.headers.get("Content-Length", "0"))).decode("utf-8")
        try:
            request = json.loads(body)
        except json.JSONDecodeError:
            return self.reply(400, {"error": "not JSON"})
        with LOCK:
            n = STATE["n"]
            STATE["n"] += 1
        t0 = time.time()
        message = relayed_message(ARGS, body, n) if ARGS.mode == "relay" else scripted_message(ARGS, request, n)
        if message is None:
            return self.reply(504, {"error": f"no answer in {ARGS.wait} s"})
        message.setdefault("role", "assistant")
        message.setdefault("content", "")
        answer = {"model": request.get("model", "stand-in"), "created_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                  "message": message, "done": True, "done_reason": "stop", "total_duration": int((time.time() - t0) * 1e9)}
        if ARGS.log:
            with LOCK, open(ARGS.log, "a", encoding="utf-8") as f:
                f.write(json.dumps({"n": n, "request": request, "answer": answer, "seconds": round(time.time() - t0, 2)}) + "\n")
        self.reply(200, answer)


def main():
    global ARGS
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--port", type=int, default=11500)
    p.add_argument("--mode", choices=["script", "relay"], default="script")
    p.add_argument("--answers", help="script mode: a JSON list of Ollama message objects")
    p.add_argument("--queue", help="relay mode: the directory the requests and answers go through")
    p.add_argument("--wait", type=float, default=600, help="relay mode: seconds to wait for an answer")
    p.add_argument("--log", help="append every request and answer here (JSON lines)")
    ARGS = p.parse_args()
    if ARGS.mode == "relay":
        if not ARGS.queue:
            sys.exit("relay mode needs --queue")
        os.makedirs(ARGS.queue, exist_ok=True)
    server = ThreadingHTTPServer(("127.0.0.1", ARGS.port), Handler)
    print(f"chat stand-in ({ARGS.mode}) on http://localhost:{ARGS.port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
