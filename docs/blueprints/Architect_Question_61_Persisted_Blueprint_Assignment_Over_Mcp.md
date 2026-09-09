<!--STATUS
state: LIVE
build-state: DESIGN — architect question (decision-shaped, resolved WITH the user). Not buildable until an
  option is approved; on approval it graduates to a buildable design with class+sequence UML + a handoff.
updated: 2026-08-26
current-answer: §3 the decision sub-questions with Claude's recommended lean per each. §2 = the measured
  INVENTORY that reframes the item (runtime attach + save ALREADY persists; the real gap is narrower).
design-basis: DESIGN_Mcp_Authoring.md (attach_blueprint = Group Q, runtime hot-attach) · MCP_Integration.md
  §"AS-BUILT — MX4b" (the mission-editing precedent this was thought to mirror) · PROGRAMME_Mcp_Agent_Surface.md
  §2 (the DEFERRED "entity blueprint-assignment authoring" item this resolves) · ruling 9 (one implementation) ·
  ruling 49 (unavailable = greyed-with-cause, run-state honesty).
known-conflict: none. The write seam is already reachable from DebugApi (no new wiring); the param-persistence
  half (Q61-C) is ENGINE work in Fdp.Toolkits + Hrot.SimHost, not an MCP-route wire.
-->
# Architect Question 61 — **Persisted instance-blueprint assignment over MCP** *(the deferred parity item)*

> 🎯 Give the MCP an **authoring** way to assign an instance blueprint to an entity *(the counterpart to the
> runtime `attach_blueprint`)*. ⚠ **The investigation reframed the item** — read §1 before the options.

## 1. ⭐⭐⭐ THE REFRAME — the gap is NARROWER than "add a persisted route" *(measured `2026-08-26`)*
🔴 **Runtime attach + `save_scenario` ALREADY persists.** `attach_blueprint` → `AttachInstanceBlueprintEvent`
→ next tick → `BlueprintInstanceService.AttachToEntity` leaves a `BlueprintBlackboard*` **slot on the entity**;
`save_scenario` → `BlueprintStateTranslator.Extract` snapshots that slot into the `BlueprintAssignments`
array. ⇒ ⭐ **persistence is a snapshot of live components, not a separate authoring table** — so the earlier
framing *("attach_blueprint is runtime-only, not persisted")* was WRONG.

⇒ ⭐⭐ **The true gap is TWO small things, not a persisted-authoring subsystem:**
| # | the real gap | why it matters |
|---|---|---|
| **G1** | ⭐ **edit-time IMMEDIATE attach** — `attach_blueprint` always publishes the next-**tick** event, so in **Edit** state *(time frozen)* it never lands without a step. The editor's own panel branches: **paused → `BlueprintInstanceService.AttachToEntity` directly (same frame)**; running → event *(`EntityBlueprintsPanel.ExecuteCommitPlan:284-295`)*. MCP does not branch. | authoring is done paused/frozen; today the agent must step to make an attach land |
| **G2** | ⭐ **no LIST route** — there is `attach_blueprint`/`detach_blueprint` but no *"what instance blueprints are on this entity"* read *(the `list_entity_variables` read is per-variable, not the assignment set)* | an agent can't see what it has assigned before editing |

⚠ **And one thing that is NOT a small gap — params do NOT persist** *(the biggest finding — Q61-C)*.

## 2. ⭐⭐ INVENTORY — measured `2026-08-26` *(the write path, end to end)*
| symbol | home | role |
|---|---|---|
| `BlueprintInstanceService.AttachToEntity/DetachFromEntity` *(static)* | **Fdp.Toolkits** | ⭐ the write seam — mutates the entity's blackboard slot directly. **Already reachable from DebugApi** *(`_world` + `_blueprintRegistry` injected; `Fdp.Toolkits` referenced)* — ⛔ no new wiring, no facade extraction |
| `EntityBlueprintsPanel.ExecuteCommitPlan` | Hrot.Blueprints.Editor | the editor UI — **branches paused (direct) vs running (event)**; the pattern MCP should mirror |
| `EntityBlueprintsEditModel` | Hrot.Blueprints.Editor | the panel's headless view-model *(staging by asset GUID)* — ⛔ NOT needed for the route; DebugApi calls the static service directly |
| `attach_blueprint`/`detach_blueprint` → `AttachInstanceBlueprintEvent` *(9100)* → `BlueprintEventIngressSystem` | Hrot.Editor / Fdp.Toolkits | the runtime path *(next tick)* |
| `BlueprintStateTranslator.Extract` | **Hrot.SimHost** | save-time snapshot: reads the slot table → emits `BlueprintAssignments` = `List<BlueprintAssignmentDto>` |
| `BlueprintAssignmentDto{ AssetId, Overrides }` | **Fdp.Toolkits** | ⚠ `Overrides` is *"Null/empty in MVP"*; `Extract` writes **only `AssetId`** |
| `InitialBlueprintsIntent` → `BlueprintMaterializationSystem` | Hrot.Common | load-time materialization of the array |

🔴🔴 **`BlueprintInstanceService.AttachToEntity` is the ONLY source of params** *(its own remark)* — there is
**no read of `BlueprintAssignmentDto.Overrides`**, and `Extract` never writes params back into `Overrides`.
⇒ ⭐⭐ **per-entity param overrides do NOT round-trip through save/load** — attach params live only in the
in-memory slot until the process ends.

## 3. ⭐⭐⭐ THE DECISION — sub-questions with Claude's recommended lean
| | question | ⭐ recommended lean | blast radius |
|---|---|---|---|
| **Q61-A** | Close G1 *(edit-time immediate attach)*? | ✅ **YES.** Make `attach_blueprint`/`detach_blueprint` **run-state-aware**, mirroring the panel: **paused/Edit → `BlueprintInstanceService.AttachToEntity` directly (same frame)**; running → the existing event. ⭐ Ruling 9 — **ONE route that matches the panel's own branch**, ⛔ NOT a parallel `/entities/{id}/blueprints/assign`. | ⭐ small — DebugApi-local, seam already reachable, no new wiring |
| **Q61-B** | Close G2 *(list)*? | ✅ **YES.** Add **`GET /entities/{networkId}/blueprints`** → the instance blueprints currently on the entity *(read the slot table, same source `Extract` uses)*. | ⭐ small — a read |
| **Q61-C** | 🔴 Make per-assignment **params persist** through save/load? | ⚠ **NOT in this slice — file it as separate ENGINE work.** Ship the route with params applied at attach *(as today)* and **document that overrides do not survive save** *(ruling-49-style honesty in the RouteDoc)*. Round-tripping needs `Extract` to read slot params into `Overrides` **and** `BlueprintMaterializationSystem` to apply them — ⛔ engine work in `Fdp.Toolkits` + `Hrot.SimHost` that touches serialization, not an MCP wire. | 🔴 **large — engine/serialization**; ⛔ do not block the route on it |
| **Q61-D** | Build an `IEntityBlueprintEditorService` facade with **OCC/version** to structurally mirror MX4b? | ⛔ **NO.** Blueprints have **no version/OCC concept** *(the write is a direct component mutation)* — a facade would invent ergonomics that do not exist. ⭐ Thin wire over the static service, exactly as the panel does. | ⭐ avoids needless abstraction |

⇒ ⭐⭐ **Net recommended slice (if approved): a SMALL MCP batch** — run-state-aware attach/detach *(A)* + a list route *(B)*, thin wire over `BlueprintInstanceService`, **params-don't-persist documented** *(C deferred as engine work)*, **no facade** *(D)*. ⛔ **Not the "persisted-authoring subsystem" the deferred item implied** — the persistence already exists.

## 4. ⭐ ON APPROVAL
Graduate this to a buildable design *(class + sequence UML: the run-state branch + the list read over
`BlueprintInstanceService`/the slot table)* and an MCP-lane handoff *(`MX-` ids)*. ⭐ File Q61-C's param
round-trip as its own tracked engine item *(it is the honest non-deliverable, like AX/QA handbacks)*.
