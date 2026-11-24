#!/usr/bin/env python3
"""laptop_run.py — L-3b, the LLM commander on a real local model (CE-4681, docs/designs/llm-commander/DESIGN_LLM_Commander.md §6).

For the user's laptop (RTX 3070, 8 GB): checks Ollama, pulls the models, runs `llm-platoon-attack` once per model with the commander's seat on the
chat-model mind, and prints a per-model summary — orders, refusals, malformed calls, answer time, checks passed. Ready on the cloud VM, where it runs
end to end against the stand-in chat server instead of Ollama:

    # the laptop (Ollama on its default port; models pulled once)
    python scripts/llm/laptop_run.py
    python scripts/llm/laptop_run.py --models qwen2.5:7b-instruct --num-ctx 16384

    # the VM dry run (no Ollama, no GPU): the stand-in answers in Ollama's shape
    python3 scripts/llm/chat_standin.py --port 11500 --mode script &
    python3 scripts/llm/laptop_run.py --endpoint http://localhost:11500 --models stand-in --no-pull

Every model's run leaves, in --out/<model>/: the verify report (<scenario>.json, .log) and the model log (commander-<id>.jsonl — every request,
every raw answer, every action's verdict). The summary is --out/summary.json.
"""
import argparse, glob, json, os, platform, shutil, subprocess, sys, time, urllib.request

DEFAULT_MODELS = ["qwen2.5:7b-instruct", "llama3.1:8b", "qwen3:8b"]   # 4-bit by default in Ollama's library (§6)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RUNNER = os.path.join(ROOT, "Hrot", "Runner", "Hrot.ClusterRunner", "bin", "Debug", "net8.0", "Hrot.ClusterRunner.dll")


def server_up(endpoint):
    try:
        with urllib.request.urlopen(endpoint + "/api/tags", timeout=5) as r:
            return json.load(r)
    except Exception as e:   # noqa: BLE001 — any failure means "not up"
        return None


def ensure_ollama(args):
    if server_up(args.endpoint) is not None:
        return
    if shutil.which("ollama") is None:
        how = ("winget install Ollama.Ollama" if platform.system() == "Windows"
               else "curl -fsSL https://ollama.com/install.sh | sh" if platform.system() == "Linux" else "https://ollama.com/download")
        sys.exit(f"no chat server at {args.endpoint} and no `ollama` on PATH — install it: {how}  (then: ollama serve)")
    sys.exit(f"`ollama` is installed but nothing answers at {args.endpoint} — start it: ollama serve")


def pull(model):
    print(f"== pull {model}", flush=True)
    subprocess.run(["ollama", "pull", model], check=True)


def run_model(args, model, out):
    os.makedirs(out, exist_ok=True)
    env = dict(os.environ,
               HROT_COMMANDER_MIND="ChatModel", HROT_COMMANDER_ENDPOINT=args.endpoint, HROT_COMMANDER_MODEL=model,
               HROT_COMMANDER_NUM_CTX=str(args.num_ctx), HROT_COMMANDER_KEEP_ALIVE=args.keep_alive,
               HROT_COMMANDER_TIMEOUT_S=str(args.timeout), HROT_COMMANDER_LOG_DIR=os.path.abspath(out))
    cmd = ["dotnet", RUNNER, "--mode", "editor", "--headless", "--verify", args.scenario, "--verify-out", os.path.abspath(out)]
    print(f"== {model}: {' '.join(cmd)}", flush=True)
    t0 = time.time()
    p = subprocess.run(cmd, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    with open(os.path.join(out, "runner.log"), "w", encoding="utf-8") as f:
        f.write(p.stdout)
    return summarize(model, out, p.returncode, time.time() - t0)


def summarize(model, out, code, wall):
    s = {"model": model, "exit": code, "wallSeconds": round(wall, 1), "calls": 0, "toolCalls": 0, "accepted": 0, "refused": 0,
         "malformed": 0, "retried": 0, "decisions": 0, "stale": 0, "repaired": 0, "fallback": 0, "tokens": [], "answerSecondsMean": None, "answerSecondsMax": None, "checksPassed": None, "checksTotal": None,
         "checksSkipped": None, "promptChars": None}
    times = []
    for log in glob.glob(os.path.join(out, "commander-*.jsonl")):
        for line in open(log, encoding="utf-8"):
            e = json.loads(line)
            if e.get("kind") == "call":
                s["calls"] += 1
                s["toolCalls"] += len(e.get("actions", []))
                s["malformed"] += e.get("malformed", 0)
                s["retried"] += 1 if e.get("retried") else 0
                s["repaired"] += 1 if e.get("repaired") else 0      # CE-4684 — a decision repaired once
                s["fallback"] += 1 if e.get("fallback") else 0      # CE-4684 — fell back to CONTINUE
                if "tokens" in e:
                    s["tokens"].append(e["tokens"])
                if "ms" in e:
                    times.append(e["ms"] / 1000.0)
                if s["promptChars"] is None and "request" in e:
                    s["promptChars"] = len(e["request"])
            elif e.get("kind") == "verdict":
                state = e.get("state")
                if state == "decision":
                    s["decisions"] += 1
                elif state == "stale":
                    s["stale"] += 1
                elif state in ("accepted", "refused"):
                    s[state] += 1
    t = s.pop("tokens")
    if t:
        s["tokensFirstMedianLast"] = [t[0], sorted(t)[len(t) // 2], t[-1]]
    if times:
        s["answerSecondsMean"] = round(sum(times) / len(times), 2)
        s["answerSecondsMax"] = round(max(times), 2)
    for report in glob.glob(os.path.join(out, "*.json")):
        try:
            r = json.load(open(report, encoding="utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        scenarios = r.get("scenarios") if isinstance(r, dict) else None
        verdicts = scenarios[0].get("verdicts") if isinstance(scenarios, list) and scenarios else None
        if isinstance(verdicts, list):
            s["checksTotal"] = len(verdicts)
            s["checksSkipped"] = sum(1 for v in verdicts if v.get("kind") == "skipped")
            s["checksPassed"] = sum(1 for v in verdicts if v.get("ok") and v.get("kind") != "skipped")
    return s


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--endpoint", default="http://localhost:11434", help="the chat server (Ollama's default port)")
    p.add_argument("--models", nargs="+", default=DEFAULT_MODELS)
    p.add_argument("--scenario", default="llm-platoon-attack")
    p.add_argument("--num-ctx", type=int, default=12288, help="the context (tokens): 8-16 k fits an 8 B model on 8 GB")
    p.add_argument("--keep-alive", default="30m")
    p.add_argument("--timeout", type=float, default=120, help="seconds one call may take")
    p.add_argument("--no-pull", action="store_true", help="do not `ollama pull` (the models are there, or the endpoint is a stand-in)")
    p.add_argument("--out", default=os.path.join(ROOT, "artifacts", "llm-laptop", time.strftime("%Y%m%d-%H%M%S")))
    args = p.parse_args()

    if not os.path.exists(RUNNER):
        sys.exit(f"build the runner first: dotnet build Hrot/Runner/Hrot.ClusterRunner  (no {RUNNER})")
    ensure_ollama(args)
    if not args.no_pull:
        for m in args.models:
            pull(m)
    summaries = [run_model(args, m, os.path.join(args.out, m.replace(":", "_").replace("/", "_"))) for m in args.models]
    os.makedirs(args.out, exist_ok=True)
    json.dump(summaries, open(os.path.join(args.out, "summary.json"), "w", encoding="utf-8"), indent=2)
    print("\nmodel                     calls  tools  acc  ref  bad  retry  mean s  max s  checks")
    for s in summaries:
        checks = f"{s['checksPassed']}/{s['checksTotal'] - (s['checksSkipped'] or 0)}" if s["checksTotal"] else "-"
        print(f"{s['model'][:24]:24}  {s['calls']:5}  {s['toolCalls']:5}  {s['accepted']:3}  {s['refused']:3}  {s['malformed']:3}  {s['retried']:5}  "
              f"{s['answerSecondsMean'] or '-':>6}  {s['answerSecondsMax'] or '-':>5}  {checks}")
    print(f"\nsummary: {os.path.join(args.out, 'summary.json')}")


if __name__ == "__main__":
    main()
