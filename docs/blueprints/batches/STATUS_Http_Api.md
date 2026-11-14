<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the item table
stale-below: none
related-designs:
  - docs/DESIGN_Debug_Api_Http_Only.md — THE design of part A (§2–§5) and B-6 (§6); as-built folded there
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — how a process is started; B-5 adds the performance switches
-->

# STATUS — the HTTP API (HANDOFF_Coordinator_To_Backend_Http_Api.md)

Dispatched at `eb43032c3`, scope frozen there. Branch `backend`, started at `247525ec3` (merged `coordinator@28b430a8f`).

| item | row | state | result |
|---|---|---|---|
| H-1 | CE-4523 | ✅ | the source-scan rail over 224 routes; 1 wrong place fixed (invoke `scope` → body), 12 undocumented keys documented, 5 param docs moved from the GET to the POST variable route; red-proved |
| H-2 | CE-4524 | ✅ | prose embedded in `Hrot.Editor/DebugApi/Guide/`, HTTP-worded (rail) |
| H-3 | CE-4525 | ✅ | `GET /help` (markdown, availability marks) + `--help-api`; rails red-proved; live 2 637 / 2 636 lines |
| H-4 | CE-4526 | ✅ | server + `.mcp.json` entry + catalog rail removed; `NotATool` dropped, `Tool` kept as the id |
| H-5 | CE-4527 | ✅ | `.claude/skills/ai-debug-http` — a pointer; followed live |
| H-6 | CE-4528 | ✅ | HISTORY notes in 31 docs; no new design-digest failures |
| H-7 | CE-4529 | ✅ | design §7 as-built |
| B-1 … B-6 | CE-1474 · CE-1950 · CE-1472 · CE-1464 · CE-4585 · CE-4586 | | |

## Gates (part A)

| gate | result |
|---|---|
| `Hrot.Editor.Tests` `AgentGuide\|EveryRoute\|DebugApi\|Capabilit` | 225 / 225 |
| red-proofs | invoke `scope` back on the query ⇒ the params rail red · `/help` dropping `GET /status` ⇒ `Help_NamesEveryDocumentedRoute` red |
| live | `--help-api` exit 0, 2 636 lines; editor `GET /help` `text/markdown`, 2 637 lines, 1 route marked not-here; `GET /status` ok |
| `design-digest.py --check` | identical to the base (pre-existing failures only) |
| grep `ai-debug-mcp\|tool-catalog` outside `.dev/`, batches and HISTORY notes | 2 HISTORY mentions in code comments (the rail's and the csproj's "moved from") |
