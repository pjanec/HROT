<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-26
current-answer: the whole file — the report for Batch HN-123 (MX4b, Group P mission editing over MCP),
  branched from the coordinator at dabd3571. ⛔ EPHEMERAL: the durable truth was folded into
  MCP_Integration.md §"AS-BUILT — MX4b" (+ §"Group P"/§"UML" corrections), the HANDOFF STATUS, and
  PROGRAMME_Mcp_Agent_Surface.md §1/§2. Quote those, not this.
known-conflict: none.
-->
# BATCH HN-123 — **mission editing over MCP (MX4b / Group P)**

> 📄 Design: [`MCP_Integration.md`](../../MCP_Integration.md) §"Group P" + §"UML" + §"AS-BUILT — MX4b".
> Roadmap + MX-002 resolution: [`PROGRAMME_Mcp_Agent_Surface.md`](../PROGRAMME_Mcp_Agent_Surface.md) §2/§3.
> Handoff: [`HANDOFF_Mcp_Mission_Editing.md`](./HANDOFF_Mcp_Mission_Editing.md).

Missions are the **proper** way a behaviour attaches to an entity — as a task. Edit-time and runtime are the
**same** `CommitMissionAsync` path. MX4b exposes that path over MCP: read the plan, add a task, clear tasks,
run/restart — all against the already-wired `Hrot.UI.Common.Facades.IMissionEditorService` (MX-002).

## What shipped — 4 routes, 4 tools (93 → 97), one seam

| id | route | tool | facade call |
|---|---|---|---|
| **MX-025** | `GET /missions/{networkId}` | `get_mission` | `GetMissionSnapshot(id)` → `{plan, version}` |
| **MX-026** | `POST /missions/{networkId}/task` | `add_mission_task` | snapshot → append `MissionTask` → `CommitMissionAsync(id, plan, version)` |
| **MX-027** | `DELETE /missions/{networkId}/tasks` | `clear_mission_tasks` | snapshot → empty plan → `CommitMissionAsync` |
| **MX-028** | `POST /missions/{networkId}/run` | `run_mission` | `SendControlCommandAsync(id, CMD_JUMP_TO_TASK, Guid.Empty)` |
| **MX-029** | — | (all four) | Node wrappers in `src/index.mjs`; regenerated `tool-catalog.mjs` / `SKILL.md`; `test-catalog.mjs` allow-list |

**Files:** `DebugApiService.Missions.cs` *(new)* · `DebugApiHost.cs` *(routes + `AwaitMissionCommitAsync` + `MissionError`)* ·
`DebugApiRouteDocs.cs` *(4 RouteDocs, group "P — Mission editing")* · `tools/ai-debug-mcp/src/index.mjs` ·
regenerated `tool-catalog.mjs` / `SKILL.md` / `test-catalog.mjs`.

## DECISION LOG *(rule 8)*

| # | decision | why |
|---|---|---|
| D1 | **Params stored verbatim, NOT via `ScenarioSerializer`** | `MissionTask.BehaviorParams` is a plain JSON string; the engine reads it with `System.Text.Json` and the Mission panel stores it verbatim. §UML said "decode via ScenarioSerializer" — corrected. ScenarioSerializer is the MX4a discovery side. |
| D2 | **`run` == `restart` → `CMD_JUMP_TO_TASK` to index 0** | The `eMissionCommandType` vocabulary has no "resume without reset"; both flags send the same jump-to-first-task. `restart` echoed for honesty. |
| D3 | **Commit awaited OFF the main thread, bounded 15 s** | `CommitMissionAsync` resolves only when the editor loop's `PollAcks()` reads the ack across frames; awaiting on the main thread deadlocks. `Begin*` publishes on-thread and returns the `Task`; the host awaits it → 200 / **409** (conflict) / **504** (timeout, points at play/step). |
| D4 | **OCC version passed through even though the editor reports 0** | `EditorMissionService.GetMissionSnapshot` returns version 0 and the engine bypasses OCC when `baseVersion==0`, so no conflict arises in the editor today — but the route passes the read version so the 409 guard engages unchanged the moment an adapter tracks one. |
| D5 | **Optional `triggers[]`, default `BehaviorFinished`** | Round-out: mirrors `MissionPanel.HandleAddTask` (a new task gets a default trigger so it can transition); an explicit array lets an agent author transitions without a separate route. |
| D6 | **Behaviour pass-through, no name mapper** | Per handoff — `BehaviorId = behavior` verbatim; unknown names are not rejected (editor-authored BTrees are valid). `behavior` required (400 if absent, hint → `GET /behaviors`). |

## Gates *(rule 8)*

| gate | command | result |
|---|---|---|
| affected build | `dotnet build Hrot.Editor.csproj` | ✅ 0 errors (10 pre-existing warns) |
| runner build (for dump-api) | `dotnet build Hrot.ClusterRunner.csproj` | ✅ 0 errors |
| route docs | `dotnet test --filter EveryRouteIsDocumented` | ✅ 4/4 |
| catalog | `npm run gen:catalog:check` | ✅ 97 tools, 97 endpoints |
| skill | `npm run gen:skill:check` | ✅ up to date |
| node tests | `npm run test:catalog` | ✅ 777 passed, 0 failed |
| STATUS/UML/inventory | `design-digest.py --check` | ✅ all pass |
| rulings | `rulings-check.py` | ✅ 25/25 (pre-existing WARN on `.claude/CLAUDE.md`, unrelated) |
| tracker | `tracker-counts.py` | ✅ 449 rows scanned |
| mermaid (UML touched) | `mermaid-check.mjs MCP_Integration.md` | ✅ 4/4 blocks parse |

⛔ Per lane rules, **test projects were not touched** (backend lane). Gated at route/unit level; the E2E rail
*(discover → add-task → play/step → the entity acts)* is **named** for `DESIGN_MCP_System_Test_Harness.md`
H4/H5, sequenced behind Test-Suite-Reliability *(rule 8 row 8)*.

## NOT done here (out of scope)
Phase 2/3 (battle-test cases, cross-host conformance) — Handoff B, dispatched after this merges.
