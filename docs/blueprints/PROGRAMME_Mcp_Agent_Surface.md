<!--STATUS
state: LIVE
build-state: ROADMAP (a programme/backlog, not a single buildable design). The buildable designs it
  sequences are MCP_Integration.md (§Group P / §UML = MX4b) and DESIGN_Mcp_Authoring.md; each carries its
  own UML. Handoffs reference those, not this file.
updated: 2026-08-26
current-answer: §2 is the ordered backlog. §1 is WHY. §3 records the MX-002 resolution (the one design call
  that blocked MX4b). Dispatch order and lane in §4.
design-basis: MCP_Integration.md §Group P + §UML (mission editing, MX4b) · DESIGN_Mcp_Authoring.md
  (the authoring surface, MA-001..018, §11.4 the auto-test loop) · PROGRAMME_Cgf_Equals_Editor_Gap_Map.md
  (cgf==editor: edit and runtime are the same operation, differing only in bootstrap/network).
known-conflict: none. Sequences BEHIND HANDOFF_Test_Suite_Reliability.md (the E2E harness must be
  trustworthy before the battle-test is codified).
-->
# PROGRAMME — **MCP as a first-class agent surface** *(BACKEND lane)*

> 🎯 Make the ai-debug MCP server a surface an AI agent can drive **by heart**: the missing authoring
> commands built, the SKILL sufficient to know **what it can AND cannot do without reading engine
> source**, and both proven by an agent actually using MCP for a real full-cycle task.

## 1. ⭐ WHY — the three things this fixes *(measured `2026-08-26`)*
| finding | evidence |
|---|---|
| ⭐⭐ **Mission editing over MCP was SPECIFIED but never built.** Missions are the *proper* way a behavior attaches to an entity (as a task); edit-time and runtime are the **same** `CommitMissionAsync` path *(no difference — the cgf==editor thesis)* | `MCP_Integration.md` §Group P = **MX4b**, build-state *READY-TO-BUILD, gated on MX-002*; no `/missions/*` route is registered in `DebugApiHost.cs` |
| ⭐⭐⭐ **The SKILL documents what EXISTS and is silent on what does NOT** — so an agent cannot tell *"can't be done"* from *"didn't find the tool"*. Negative space is invisible; the quick-vs-authoring distinction is buried in one tool note; there is no "attach a behavior / blueprint instance" workflow | `tools/ai-debug-mcp/SKILL.md` (read `2026-08-26`): strong mental-model/workflows/gotchas, but no boundaries section and no mission/attach workflow |
| ⭐ **The features were never battle-tested by an agent** using only the SKILL. `BlueprintScenarioIntegrationTests` proves the engine path but its full-pipeline case is `[Fact(Skip)]` and it never drives MCP | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintScenarioIntegrationTests.cs:39` |

## 2. ⭐⭐⭐ THE BACKLOG *(ordered; the impl session allocates final ids — rule 3)*
> Suggested prefix **`MX-`** (continues the MCP series; `MX4b` already exists) for commands+docs; the battle-test
> cases follow the conformance-rail naming. State every id in the report *(rule 5)*.

| phase | item | scope | design basis | dep |
|---|---|---|---|---|
| **1 — commands** | **MX4b · mission editing** ⭐⭐⭐ | `GET /missions/{id}` · `POST /missions/{id}/task {behavior,params}` · `DELETE /missions/{id}/tasks` · `POST /missions/{id}/run {restart?}` — each **read-snapshot → modify → `CommitMissionAsync(id, plan, baseVersion)`** (OCC); run = `SendControlCommandAsync`. Params encode via `ScenarioSerializer`; the shape is discovered via the already-built `GET /behaviors` (MX4a) | `MCP_Integration.md` §Group P + §UML | §3 (MX-002 — RESOLVED below) |
| | **(DEFERRED · DECIDE later)** entity blueprint-assignment authoring | a **persisted** `BlueprintAssignments` edit route (edit-time parity for instances), vs today's runtime-only `attach_blueprint`. ⛔ **Do NOT build yet** — build only if Phase 3 shows a runtime attach does not survive `save_scenario` | new; parallels MX4b | Phase 3 evidence |
| **2 — agent-usability** | **skill trigger** ✅ *(coordinator wrote it `2026-08-26`)* | `.claude/skills/ai-debug-mcp/SKILL.md` — fires on MCP-editor intents → "read `tools/ai-debug-mcp/SKILL.md` first; don't derive from source; an unanswerable question is a SKILL gap to file" | this programme | — |
| | **SKILL mental-model + workflows** | elevate into `skill-parts/10-mental-model.md`: *behaviors attach via mission tasks (edit==runtime); blueprint instances are scenario content; `attach_blueprint` is a runtime shortcut vs the authoring path*. Add **"attach a behavior (mission task)"** + **"attach a blueprint instance"** workflows to `skill-parts/30-workflows.md` | this programme | MX4b (so the workflow is real, not "not yet") |
| | **RouteDoc lifecycle fields** | add `Lifecycle: runtime-hot \| authoring-persisted` + a `SeeAlso`/`properPathFor` cross-ref to `RouteDoc`, harvested into SKILL — so quick-vs-proper and persistence are uniform machine facts, not prose | R-133 (harvest, never hand-author) | — |
| | **Generated "Capabilities & boundaries"** ⭐⭐ | in `generate-skill.mjs`: reflect the engine capability inventory (`GraphCommand` union · `eMissionCommandType` · attach/assign events · entity ops) **vs the MCP route surface** and emit *exposed / engine-only-not-over-MCP*. Gate it with a coverage rail like `TheCommandRouteCoversTheWholeUnion` so the negative space **cannot rot** | fix; mirrors `DESIGN_Mcp_Authoring.md` §11.5 | benefits from MX4b |
| **3 — real-world use** | **Cycle 2 — blueprint instance** | author a `Count4`-like **Instance** blueprint over MCP → save/reload (registers) → `attach_blueprint` → play → watch `Count` increment | `DESIGN_Mcp_Authoring.md` §11.4 | P0 (harness) |
| | **Cycle 1 — BTree behavior via mission** | author a BTree over MCP → register (name in `BehaviorRegistry`) → **add a mission task** naming it (MX4b) → play → assert motion. Pass-through means **no new mapper/task-type** is needed for an arbitrary behavior | MX4b · `MissionDirectorSystem` / `TacticalIntentResolutionSystem` pass-through | MX4b, P0 |
| | **doc-sufficiency gate** ⭐ | an agent given **only** `tools/ai-debug-mcp/SKILL.md` completes both cycles with **zero engine-source reads**; each guess/spelunk = a filed SKILL gap feeding Phase 2 | this programme | Phase 2 |

⚠ **Precondition both cycles hinge on (gate the FIRST build step on it):** a *freshly MCP-authored* BTree/Blueprint
must appear in `list_behaviors` / `list_blueprints` **by name** after `save`+`reload`. If it does not, authoring
one's own asset then running it is impossible — surface it before proceeding.

## 3. ⭐⭐⭐ MX-002 RESOLVED — **which `IMissionEditorService`** *(the design call that blocked MX4b)*
📐 **Measured `2026-08-26`:** there are **two** interfaces named `IMissionEditorService`
*(`MCP_Integration.md` said three; two exist in source)*:

| interface | members | wired into DebugApi? |
|---|---|---|
| `Hrot.ExCon.Services.IMissionEditorService` | `GetAvailableBehaviors` · `GetMissionSnapshot` · `CommitMissionAsync` · `SendControlCommandAsync` · `SendControlCommand`(void) · `IDisposable` | no |
| ⭐⭐⭐ **`Hrot.UI.Common.Facades.IMissionEditorService`** *(`Hrot/Engine/Hrot.Presentation/Facades/IMissionEditorService.cs`)* | `GetAvailableBehaviors` · `GetMissionSnapshot` · `CommitMissionAsync` · `SendControlCommandAsync` | ✅ **YES — `DebugApiService._missionService` / the `MissionService` property, already used by `GET /behaviors?entityId=`** |

⇒ ⭐⭐ **MX4b builds against `Hrot.UI.Common.Facades.IMissionEditorService`** — it is **already injected**, and it
carries `GetMissionSnapshot` + `CommitMissionAsync` + `SendControlCommandAsync`. ⛔ No new injection; ⛔ do NOT
use the ExCon interface. Run/restart uses **`SendControlCommandAsync`** *(the facade has the async variant, not the
void one)*. 📌 `MCP_Integration.md` §UML's `IMissionEditorService <<exists · Hrot.ExCon>>` note is CORRECTED to
`Hrot.UI.Common.Facades` in this batch *(known-rot cleared)*.

## 4. ⭐ DISPATCH & LANE
⭐ **BACKEND lane.** ⭐ Order: **P0 (Test-Suite Reliability — in flight) → Phase 1 → Phase 2 → Phase 3.** The
**skill trigger** is already done; **RouteDoc lifecycle fields** are independent and may land any time.
- **Handoff A = Phase 1** *(commands: MX4b, with MX-002 resolved here)* — `HANDOFF_Mcp_Mission_Editing.md`.
- **Handoff B = Phase 2 + Phase 3** — branches from A's as-built so the boundaries generator + new workflows
  describe **real** routes. ⛔ Not dispatched until A merges.

⚠⚠ **Who runs the doc-sufficiency gate (Phase 3 ⭐) — user ruling `2026-08-26`:** a session **without
build-context**. ⭐ Either a **FRESH session**, OR an **existing session AFTER a compaction** *(it has lost the
build details, so it is effectively naive — "not exactly fresh, but not the one who implemented")*. ⛔ **NEVER the
session that built Phase 1/2** — testing your own docs with insider knowledge proves nothing. The backend session
**codifies** both cycles as durable harness cases *(`ScenarioBehaviorTests` / `ClusterConformanceRails`)*; the
naive-agent doc-sufficiency run is a SEPARATE session driving the live MCP reading only `tools/ai-debug-mcp/SKILL.md`.
