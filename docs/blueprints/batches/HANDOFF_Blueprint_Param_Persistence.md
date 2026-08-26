<!--STATUS
state: LIVE
build-state: FRAME — MCP lane. Coordinator gives the FRAME; the SESSION designs the details (inventory, UML,
  seams) as step 1, then builds (frame-delegation, CLAUDE.md WHO-DESIGNS amendment 2026-08-26).
updated: 2026-08-26
current-answer: this handoff is the FRAME. The decision trail: Architect_Question_61. The measured finding:
  §1 below. ⭐ You author the design doc (with class+sequence UML) before building.
known-conflict: touches blueprint SERIALIZATION (Fdp.Toolkits + Hrot.SimHost) — see §4 lane fences.
-->
# FRAME-HANDOFF — **Persisted instance-blueprint parameters + the MCP wire** *(MCP lane, `MX-`)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Rule 7 re-sync; **rule 1b started-marker before code.** ⛔ No PR. ⭐ `MX-` ids, you allocate them *(rule 5)*.
> ⭐⭐⭐ **This is a FRAME, not a full design.** Per the WHO-DESIGNS amendment, **YOU do the inventory + author the
> class/sequence UML in a `DESIGN_*` doc as step 1**, then build, then fold the as-built. The coordinator verifies
> the design+UML on return.

## 1. ⭐⭐⭐ THE FRAME — the finding that scopes this
🔴 **"Assign an instance blueprint WITH persisted per-entity parameters" is impossible today by ANY path**
*(editor UI, scenario file, or MCP)* — measured `2026-08-26`. The **runtime** params pipeline is built and
tested *(`ParseParams` → params region @16, fed by `attach_blueprint(paramsJson)` / lifecycle events)*, but
**save drops params**: `BlueprintStateTranslator.Extract` writes `AssetId` only; `BlueprintMaterializationSystem`
calls `InitDefault` only; `BlueprintAssignmentDto.Overrides` is **dead** *(never read/written in production)*.
⇒ ⭐⭐ **Assigning a parametric blueprint is hollow until params persist — this batch fixes that, for the editor
AND MCP.** 📄 Decision trail + taxonomy: **[`Architect_Question_61`](../Architect_Question_61_Persisted_Blueprint_Assignment_Over_Mcp.md)**.

## 2. ⭐⭐ WHAT THE FRAME ASKS FOR *(you design the HOW)*
| # | goal | coordinator's lean *(yours to finalize + design)* |
|---|---|---|
| ⭐⭐⭐ **A** | **Params SURVIVE save→reload** — the three engine pieces: **Extract** diffs the live params region vs `InitDefault` and emits them · a **scenario override structure** carries them · **Materialization** applies them on load *(params-aware attach, not `InitDefault`-only)* | ⭐ **reuse ONE param format** across attach + save + load *(the format `ParseParams`/the resolver already speaks)* rather than inventing a second — R-133/ruling 9. Whether that means populating `BlueprintAssignmentDto.Overrides` or the resolver wire form is **your call, decide-and-log** |
| ⭐⭐ **B** | **The MCP wire (AQ61 A+B)** — make `attach_blueprint`/`detach_blueprint` **run-state-aware** *(paused/Edit → direct `BlueprintInstanceService`, running → event, mirroring `EntityBlueprintsPanel.ExecuteCommitPlan`)* + a **`GET /entities/{id}/blueprints`** list route | ⛔ ONE route, matches the panel's branch — NOT a parallel `/assign` *(ruling 9)*. Params now persist *(A)*, so this is finally worth shipping |
| ⭐ **C** | **fold `QA-023`** *(`BlueprintStateTranslator.Inject` mis-handles the mixed-keys case)* — same file as A | it is in your files this batch; fix it while you are there |

⛔ **OUT OF SCOPE — say so, do not build:** the *same blueprint twice on one entity* case *(slot identity is
`blueprintId` alone; "two Patrols, different waypoints" needs `(blueprintId, instanceKey)` — a separate, larger
identity change)*. Single-instance persist only.

## 3. ⭐ DESIGN BASIS TO READ *(set the intent before you draw UML — R-129)*
`Architect_Question_61` *(the reframe + Q61-A/B/C/D leans)* · `DESIGN_Mcp_Authoring.md` *(the attach surface)* ·
`BLUEPRINT-SCENARIO-DESIGN.md` §6 *(the ORIGINAL `Overrides` intent — measure why it was abandoned before reusing it)* ·
`EXPLAINER_Where_Parameters_And_State_Live.md` *(params @16 vs working-state; the slot-identity caveat)*.

## 4. ⭐ LANE FENCES
⭐ **Yours THIS batch** *(MCP lane scope expands into blueprint serialization — declared):* `BlueprintStateTranslator`
*(Extract/Inject)* · `BlueprintMaterializationSystem` · `BlueprintAssignmentDto` · the read side of `BlueprintInstanceService` ·
`DebugApi*`/`tools/ai-debug-mcp`. ⚠⚠ **These are `Fdp.Toolkits` + `Hrot.SimHost` — the BACKEND lane's neighbourhood.**
The backend's concurrent batch is fenced OFF these exact files *(see its handoff)* — but **rule-4 re-pull and STOP-and-report
if you find a collision.** ⛔ Do NOT touch UI/CGF scenario/menu/viewport code.

## 5. ⭐ ACCEPTANCE + PROCESS
- **Round-trip rail:** attach an instance with a non-default param *(edit-time, immediate)* → `save_scenario` → reload → the param **survives** *(inverse-edit red-proof)*. The editor benefits identically. `QA-023`'s mixed-keys case green.
- ⭐⭐ **Process (frame-delegation):** ① rule-7 + started-marker · ② **INVENTORY + author `DESIGN_Blueprint_Param_Persistence.md` with class+sequence UML** *(the Extract→DTO→Materialization flow + the run-state-aware attach)* · ③ build affected projects only · ④ fold as-built into the design *(obligation ⑤)* · ⑤ report with the DECISION LOG *(esp. the override-format choice)* + `MX-` ids + gates *(rule 8: build/test/`gen:catalog`/`rulings`/`design-digest`/`tracker`)*.
