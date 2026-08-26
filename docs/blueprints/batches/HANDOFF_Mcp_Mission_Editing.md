<!--STATUS
state: LIVE
build-state: DISPATCH — BACKEND lane. Phase 1 of PROGRAMME_Mcp_Agent_Surface.md: build MX4b (mission
  editing over MCP) — the proper behavior-attach path. MX-002 is RESOLVED; build against the wired facade.
updated: 2026-08-26
current-answer: this handoff. DESIGN + UML: MCP_Integration.md §Group P + §"UML" (READY-TO-BUILD).
  Roadmap + the MX-002 resolution: PROGRAMME_Mcp_Agent_Surface.md §2/§3.
known-conflict: edits DebugApiService/DebugApiHost/DebugApiRouteDocs + the generated tool-catalog/SKILL
  (shared MCP files) ⇒ branch from a base that already contains the merged MCP work; rule-4 re-pull.
  ⛔ Disjoint from the UI/CGF lane's Slice A. Sequences BEHIND Test-Suite-Reliability for the E2E half.
-->
# HANDOFF — **Mission editing over MCP (MX4b / Group P)** *(BACKEND lane — Phase 1)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`MX-`** ids *(last used `MX4b` is THIS item; number the doc/route sub-items yourself — rule 3)*; state every id *(rule 5)*.
> 🎯 Missions are the **proper** way a behavior attaches to an entity (as a task). Edit-time and runtime are the **same** `CommitMissionAsync` path — no difference.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — do NOT design here
📄 **[`MCP_Integration.md`](../../MCP_Integration.md) §Group P** *(P.1 read/edit/run routes)* **+ §"UML"** *(the `DebugApiService`↔`IMissionEditorService` classDiagram and the "behaviour discovery then mission add-task" sequence)*. Check the UML before building *(obligation ③)*; report match/deviation, and **fold any deviation back into §Group P/§UML** before the batch closes *(obligation ⑤)*.
📄 Roadmap + **MX-002 resolution**: **[`PROGRAMME_Mcp_Agent_Surface.md`](../PROGRAMME_Mcp_Agent_Surface.md) §3.**

## 1. ⛔ AUTONOMY + BUILD RULES
Decide-and-log; stop the ITEM not the batch *(R-106)*. Codebase-memory not connected ⇒ the **CLI**, ⛔ not grep-only. Build the AFFECTED project *(`Hrot.Editor` + the MCP node server’s catalog/tests)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`.

## 2. ⭐⭐⭐ WHAT TO BUILD *(MX4b — four routes, one seam)*
⭐⭐ **The seam is already injected** — `DebugApiService._missionService` (the `MissionService` property, type **`Hrot.UI.Common.Facades.IMissionEditorService`**, used today by `GET /behaviors?entityId=`). ⛔ **No new injection; NOT the `Hrot.ExCon` interface** *(MX-002, PROGRAMME §3)*.

| # | route | build | the one thing not to get wrong |
|---|---|---|---|
| ⭐ **①** | `GET /missions/{networkId}` | `GetMissionSnapshot(id)` → `{ plan(tasks+specs), version }` | ⚠ return the **OCC `version`** — the edits below need it |
| ⭐⭐⭐ **②** | `POST /missions/{networkId}/task {behavior, params}` | **read-modify-commit**: snapshot → append a `MissionTask` *(carrying `BehaviorName`=behavior; params per P.0 schema, decoded by `ScenarioSerializer`)* → `CommitMissionAsync(id, newPlan, version)` | ⛔ pass the `version` you read; a `ERR_VERSION_CONFLICT` is a legitimate 409, surface it. ⛔ do NOT invent a mapper — pass-through runs an arbitrary behavior by name |
| ⭐ **③** | `DELETE /missions/{networkId}/tasks` | snapshot → trimmed/empty plan → `CommitMissionAsync` | same OCC rule |
| ⭐ **④** | `POST /missions/{networkId}/run {restart?}` | **`SendControlCommandAsync(id, eMissionCommandType, taskId)`** | ⚠ the wired facade has the **async** variant only — use it |
| ⭐ **⑤** | RouteDoc + `src/index.mjs` handler per route; `gen:catalog` + `gen:skill` + `test-catalog` green | mirror the existing authoring routes | 📌 CE-009 §4c — advertised-but-unreachable tools; every route carries a `RouteDoc` |

⚠ **Behavior discovery already exists** *(MX4a, `GET /behaviors?entityId=`)* — the add-task route’s `behavior` + `params` shape is what that returns; cite it in the RouteDoc `Hint` *(the `missionTask` category already points there — `DebugApiHints.cs`)*.

## 3. ⭐ DONE — rails
- add-task then `GET /missions/{id}` shows the task; a bad `version` yields a 409, not a silent overwrite; run/restart advances the mission; the four routes appear in `gen:catalog` (tool count Δ stated) and `test-catalog` is green.
- an **integration rail** (or a filtered conformance case): add a task naming a known behavior → play/step → the entity acts *(the `ScenarioBehaviorTests` outcome-with-tolerance pattern)*. ⚠ If the E2E harness is not yet trustworthy *(Test-Suite-Reliability in flight)*, gate at the route/unit level and **name** the integration suite that will cover it *(rule 8 row 8)*.
- affected-project builds; reds proven pre-existing by `git diff`.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `DebugApiService*.cs` *(the mission routes)* · `DebugApiHost.cs` *(registration)* · `DebugApiRouteDocs.cs` · `tools/ai-debug-mcp/{src/index.mjs, RouteDoc harvest}` + regenerated `tool-catalog.mjs`/`SKILL.md`. ⚠ shared MCP files — **rule-4 re-pull**. ⛔ Do NOT touch scenario-session/toolbar/menu code (UI/CGF lane), and ⛔ do NOT start Phase 2/3 here — Handoff B branches from this as-built.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on `MCP_Integration.md` if you touch its UML · `gen:catalog`/`gen:skill`/`test-catalog` · the `MX-` ids. **When done:** fold the as-built into `MCP_Integration.md` §Group P/§UML *(obligation ⑤)*, flip MX4b BUILT, and the report points at the design + carries the DECISION LOG.
