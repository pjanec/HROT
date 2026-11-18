<!--STATUS
state: LIVE — the ui lane's open questions for the coordinator / user (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-18
current-answer: the whole file
-->

# DECISIONS — ui

## CE-1286 (taken while the queue ran dry) — how does a C# behaviour's param / working-state field get into the Watch? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): A — a new Watch row section `Occurrence` addressed by (behaviour hash, slot kind, field path), read through `OccurrenceSlotCatalog`, written through R-1041's one live-write path; the dump's row menu gains Add to Watch.**

**Where it stands:** the display half is DONE (CE-1315: the Brain tab and the inspector dump every running behaviour's params and variables, C#
ones included, through `OccurrenceSlotCatalog`). Left: the row menu *Add to Watch*. ⚠ The Watch's pinned row is an asset-variable identity
`(AssetId, Entity, Section, VariablePath)` (`AiWatchWindow`, `PinnedVariableRowSource`) — a C# behaviour has no asset id and its fields no section.

| option | what it means | cost |
|---|---|---|
| **A (lean)** — a new row SECTION `Occurrence` addressed by `(behaviour hash, slot kind task/SOP/hosted, field path)`, read through `OccurrenceSlotCatalog` (the one reader, R-1037) and written through R-1041's one live-write path | the Watch shows and edits a C# behaviour's fields like an asset variable; the dump's row menu gains *Add to Watch* | a new `VariableRow` kind + its persistence entry (DebugSessionPersistence) + rails; touches the Watch's identity, so it should be agreed first |
| B — *Add to Watch* only for asset-defined behaviours (the existing identity) | no new identity | the user's ask was precisely the C# ones — leaves them out |

**Claim table (lean A):** the display exists — ✅ `CE-1315` (`OccurrenceSlotView.Boxed`); the Watch identity is asset-only — ✅ `AiWatchWindow.cs` header
(`(AssetId, Entity, Section, VariablePath)`); a design rules the C# watch identity — ⛔ searched `docs/DESIGN_Entity_Brain_View.md` §12b (BV-42…45: *"edits through
Add to Watch / the Watch window"*, no identity named) and `docs/DESIGN_Behaviour_Debugging.md` DBG-5, none found.

**State:** SKIPPED — needs decision. Nothing built.

## CE-1067 — should a REMOTE terminal's panel reach another node's GLOBAL gizmo? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): A — not a defect today; close the row (reopen when a feature needs a terminal to drive another node's global gizmo).**

**The question:** `GizmoInteractionIngressTranslator` drops a remote StructUpdate / InspectorClosed whose anchor is not an entity in its world. A
global gizmo (the layer control, picker tools — `GlobalGizmoManager.NewId()`, 8 call sites: `MapInteractionPack.cs:337/382/394`, `PickerToolHost.cs:240/354`,
`AreaAuthoringArm.cs:116`, `ScenarioToolRegistrations.cs:139`) has a per-run, per-node id, so a remote terminal cannot name it at all — fixing the drop
alone fixes nothing.

| option | what it means | cost |
|---|---|---|
| **A (lean)** — NOT A DEFECT TODAY: a global gizmo is a host's OWN UI (each map host draws and edits its own layer control; §5e made the layers per view, remembered locally) — close the row; reopen when a feature needs a terminal to drive ANOTHER node's global gizmo | nothing changes; the row stops sitting in the bug queue | none; the drop stays correct for picks and drags (behaviour ②) |
| B — a stable global-gizmo ADDRESS (a registered name per kind, e.g. `layer-control`, resolved per node) and the ingress admits StructUpdate / InspectorClosed for a registered name | a new addressing scheme in the gizmo wire + 8 registrations named | a design (DESIGN_Uniform_Gizmo_Membership) + the wire change; no current flow uses it |

**Claim table (lean A):** a local panel is unaffected — ✅ the row's own measurement (`:120` drops only remote batches); a shipped flow edits a REMOTE global gizmo —
⛔ searched `docs/DESIGN_Terrain_Combat_Tuning.md` §5d/§5e and `docs/DESIGN_Uniform_Gizmo_Membership.md`, none found; ⚠ graph not queried for this one (grep only).

**State:** SKIPPED — needs decision. Nothing built.

## CE-1954 — does a PRESS-AND-DRAG on an UNSELECTED unit move it? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): A — a press on an unselected unit selects it and the same drag moves it, in BOTH the threaded and the serial editor.**

**The question:** the threaded editor moves it, the serial one does not (it only selects); the two must agree (R-4224). Which rule?

| option | what it means | cost |
|---|---|---|
| **A (lean)** — press on an unselected unit SELECTS it and the same drag MOVES it, in both modes | the common editor convention (a click-drag grabs what is under the cursor); `GroupTransformTests.P5_ADragOnAnUnselectedUnit_MovesOnlyThatUnit` already asserts the gizmo-level half | the serial host must emit a drag box for EVERY unit under the cursor (not only selected ones — `DataDrivenGizmoSystem.IsSelectedPredicate`), or select-then-drag on the press; a frame rail on both hosts |
| B — the press only SELECTS; a drag needs the unit selected first | what serial does today | the threaded host must stop answering a press with the previous frame's pick boxes; P5's unselected-drag rail is then wrong and changes |

**Claim table (lean A):** threaded drags it — ✅ measured live (the row, `tests/hill-attack-close`); serial selects only — ✅ the drag box is emitted for selected units only (`DataDrivenGizmoSystem` `IsSelectedPredicate`); the design rules it — ⛔ searched `docs/UX/DESIGN_Map_Interaction.md` (§2.5 table, P5 as-built), no rule for an unselected unit's drag.

**State:** SKIPPED — needs decision. Nothing built.
