<!--STATUS state: LIVE · updated: 2026-11-15 · current-answer: §1 the matrix, §2 the causes · scope frozen at 40cb8d4df (handoff HANDOFF_Coordinator_To_Ui_Multi_Select.md) -->
# STATUS — ui: dragging a multi-selection (CE-4342 reopened)

Handoff: [HANDOFF_Coordinator_To_Ui_Multi_Select.md](HANDOFF_Coordinator_To_Ui_Multi_Select.md) (dispatched `40cb8d4df`, started `6e3d76114`). Design folded:
[DESIGN_Map_Interaction.md "AS-BUILT — CE-4342 reopened"](../../UX/DESIGN_Map_Interaction.md) (the P5 CE-4342 row marked SUPERSEDED) ·
[UX_Feature_Multi_Select.md](../../UX/UX_Feature_Multi_Select.md) STATUS (BUILT) · [DESIGN_Threaded_Simulation_Presentation.md](../../DESIGN_Threaded_Simulation_Presentation.md) (the A-side
bracket row) · [DESIGN_Paused_And_Stepped_Execution.md](../DESIGN_Paused_And_Stepped_Execution.md) §7f note · [DESIGN_System_State_Inventory.md](../../DESIGN_System_State_Inventory.md) (one row).

## 0. How the real product was driven (step ①)
A real `--mode editor` (the ClusterRunner build of this tree) on its own Xvfb display, driven with **real X input** (`xdotool` mouse / keys — GLFW reads them
exactly as from a person; nothing synthetic inside the process), scenario `tests/hill-attack-close` (8 units: a 5-tank platoon `1000`–`1004`, 3 M1s). Positions,
selection and Undo over the debug API (`GET /entities/{id}`, `GET /selection`, `POST /editor/commands/edit.undo/invoke {scope:shell}` — the menu's command). Two
processes per round: **threaded** (`threadedKernelMode 3`, the product default, confirmed by `GET /performance`) and **serial** (`HROT_PARALLEL_SIM=0
HROT_THREADED_KERNEL=0 HROT_SIM_THREADS=1`). Per case: clear, select `1004 · 1003 · 1002` by the gesture (band = a left drag from ground round them; Shift = click,
Shift-click, Shift-click; Ctrl likewise), grab `1003`, drag +160 / −50 px, read every unit, Undo once, read again; `1001` is the unselected control. The view is
toggled by View ▸ 2-D / 3-D Map. Live = `POST /scenario/load/live` (paused briefing), then `POST /sim/timescale 0.1` + `/sim/play` (the AI moving).
Scripts (scratchpad, not committed): `drive.py`, `matrix.py`.

## 1. The matrix — before → after (`all` = the three selected moved by the same delta, the control did not; `1 undo` = one Undo restored all three)

| view · mode | gesture | threaded BEFORE | serial BEFORE | threaded AFTER | serial AFTER |
|---|---|---|---|---|---|
| 2-D · edit | band | all · ⛔ **no undo step** | all · 1 undo | all · 1 undo | all · 1 undo |
| 2-D · edit | Shift-click | ⛔ selection = last clicked · **only the grabbed one moves** | ⛔ selection = last clicked · **none** | all · 1 undo | all · 1 undo |
| 2-D · edit | Ctrl-click | ⛔ same — **only the grabbed one** | ⛔ **none** | all · 1 undo | all · 1 undo |
| 3-D · edit | band | all · ⛔ no undo step | ⛔ **none** (after the 2-D case's Undo) | all · 1 undo | all · 1 undo |
| 3-D · edit | Shift / Ctrl | ⛔ only the grabbed one | ⛔ none | all · 1 undo | all · 1 undo |
| 2-D · live (paused) | band | all | all | all | all |
| 2-D · live (paused) | Shift | ⛔ only the grabbed one | ⛔ only the grabbed one | all | all |
| 3-D · live (paused) | band | all | all | all | all |
| 3-D · live (paused) | Shift | ⛔ only the grabbed one | ⛔ only the grabbed one | all | all |
| 2-D · live (playing, 0.1×) | Shift | (not run before: at 1× the units drove out of view and the script lost them) | — | all (33.4, 8.8) — the AI's own motion on top | all (33.5, 9.7) |
| 3-D · live (playing, 0.1×) | band / Shift | — | — | all · all | all · all |
| 2-D · live (playing, 0.1×) | band | — | — | ⚠ the band's start press landed on a drawn route's waypoint handle (the moving units' routes) ⇒ no band; the drag of `1003` moved it alone — the drive's geometry, not a selection defect (Shift on the same frame: all) | same |

Undo is Edit-mode only by design (`EditUndoCommands`, "only in Edit mode") — the live rows have none. The deltas are identical per unit in every "all" row
(e.g. 2-D `(33.3, 10.4)` m, 3-D edit `(29.1, 20.9)` m): offsets kept. Raw lines: `matrix-{T,S}-{before,after,after2}.txt` in the scratchpad.

Frames (⛔ not in git), `/tmp/claude-0/-home-user-HROT/1bf325a1-da7f-51ac-b2db-4ed0c6f53d2b/scratchpad/group-drag/`:
`BEFORE-threaded-2D-edit-shift-{before,after}.png` (only `1003` moved) · `AFTER2-threaded-2D-edit-shift-{before,after}.png` (all three) ·
`BEFORE-serial-3D-edit-band-{before,after}.png` (none) · `AFTER2-serial-3D-edit-band-{before,after}.png` · every other case under the same naming.

## 2. The causes (each measured, then fixed in the ONE shared hop)

| # | cause | where | fix |
|---|---|---|---|
| ① | the left press's `Started` carried **no modifiers** (only the raw route polled them) ⇒ Shift / Ctrl / Alt-click and Shift-band never reached `SelectionInteractionSystem`'s P5 rows: a modified click REPLACED the selection — every view, mode and threading mode. 📐 logged at the terminal: every `Started` `action=0` with Shift / Ctrl held | `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/Layers/DebugGizmoLayer.cs` (left press), `Gizmos/GizmoInteractionProxyTool.cs:44` | `HeldModifiers` polled once per frame → `startedActionId = Left \| mods` (the contract `GizmoInteractionStartedEvent.Button` documents) |
| ② | **threaded: no undo step for a drag** (group or single): the gizmo brackets on A, the recorder is S's (`EditorSubsystem.cs:353-361`) | `FDP/Toolkits/Fdp.Toolkits/Replication/Edits/EditOp.cs` `EditSteps` | a world with no recorder but a `SimLink` posts Begin / End / Cancel to S's job queue, in order with the writes |
| ③ | **after any Undo, nothing could be dragged** (serial; threaded once ② made Undo real): the restore moves `WorldEpoch`, `DataDrivenGizmoSystem` tore down every gizmo and only a `ConstructionOrder` re-creates one. 📐 the frame after the Undo: the unit's select box only, no drag box (`type 1880133284` gone) | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | on a RESTORE (`RestoresOf` moved) re-adopt the living entities the rules match; never two instances per rule |
| ④ | **threaded: after an Undo the map went blank** (frame 78 → 1 primitive; also after a threaded Undo of an HTTP delete — a path that recorded before this batch): A's pull carries the restore's epoch but not the structural clock the match-pass skip reads ⇒ a stale match drew a unit that had lost `BehaviorState`, `HillAttackGizmo.Draw` threw, the gizmo group aborted every frame | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs` `Execute` | re-derive the match lists on `WorldEpoch.Moved` (§7f's rule) |

Not reproduced: an early serial 3-D drag that moved nothing was the same cause ③ (it followed a 2-D case's Undo); a first 3-D band that selected nothing started
on the dock splitter (the drive's fault, fixed in the script).

**Claim table (the lean: fix each in the shared hop)**

| the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| Started was meant to carry modifiers | ✅ only raw packs them (`DebugGizmoLayer.cs` raw block); proxy tool passes 0 | ✅ `GizmoInteractionEvents.cs:33` "It also carries ShiftMask / CtrlMask / AltMask"; P5 rows in `MapGestureBindings` |
| one terminal serves every host and both views | ✅ `Fdp…Vis2D.Layers.DebugGizmoLayer.Update` → `_innerTerminal.HandleInput` (2-D and 3-D) | ✅ gizmo-input-focus design: "the map is a dumb terminal" |
| the threaded recorder is on S, the gizmos on A | ✅ `EditorSubsystem.cs:353`, `MapPresentationStep.RunPerPulledTick` | ✅ Threaded §14.8 U-1, R-4211 |
| a bracket forward keeps ONE step | ✅ `SimJobQueue` drains in post order; `WorldUndoRecorder` nests by depth | ✅ Edit-mode undo §2.2 "one user action = one step" |
| a restore publishes no ConstructionOrder | ✅ measured (gizmo frame after Undo) | ✅ §7f / restore design D4: `Moved` fires on a restore, entities kept |
| the proven solution: input modifiers ride with the press event; an undo bracket lives where the writes land; per-entity caches re-derive on a world replacement | adopted for all three — each is the engine's existing seam (the event's field, the SimLink retargeting the writer already does, `WorldEpoch.Moved`) | — |

Rejected: a per-view selection lookup — the selection was never lost (the live matrix: band always selected) · registering the selection under every world — the
gizmo's world IS the registered one in both modes (A in mode 3, the one world otherwise) · a forward in `GroupTransform` only — 14 files bracket on A · the
modifiers read from ImGui inside `SelectionInteractionSystem` — the backend side has no keyboard; the terminal owns input.

**Lane decisions** (what would change each): a unit the router refuses is put back, the others move (`GroupTransform.Commit`, the edit path's AX-007 rule) —
change if the user wants a refused member to cancel the whole group · the right-click `Started` keeps no modifiers (a Shift right-click would resolve an unbound
row) — change if a Shift right-click row is designed · a throwing stateless projector still aborts its whole group — not changed (a separate robustness item).

## 3. Gates (rule 8)

Base for "pre-existing": `6e3d76114` (this batch's changes stashed, the same four test projects rebuilt, the red tests re-run by name). Each test project BUILT
before its `--no-build` run (`dotnet build <tests.csproj> --no-restore`). Frame rails under `xvfb-run -a` (counts below are real: 0 of the CE-4342 rails skip).

| gate | verbatim | result | vs base |
|---|---|---|---|
| T-1 the feature's suites | `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build --filter "FullyQualifiedName~GroupTransformTests\|FullyQualifiedName~RtsCommandToolTests\|FullyQualifiedName~SelectionInteractionSystemTests"` | **62 / 0 / 0 skip** (incl. the frame rail `P5_AGroupRotate_OnARealFrame…`) | +5 new rails |
| new rails | `… --filter FullyQualifiedName~CE4342` (Presentation.Tests) · `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter FullyQualifiedName~CE4342` | 5 / 0 · 1 / 0 | new |
| red-proofs (each a compiling mutation, build + run, reverted) | M1 `GizmoInteractionProxyTool` passes `0` for `startedActionId` · M2 `EditSteps.Begin` without the SimLink forward · M3 no `AdoptLivingEntities` · M4 no `WorldEpoch.Moved` in `StatelessGizmoSystem` | M1 red: the terminal rail · M2 red: both threaded rails · M3 red: the terminal rail (the drag after the Undo) · M4 red: the stateless rail (`Entity(1, v1) missing GizmoTestCompA` — the live fault) | — |
| 0 B per steady drag frame (R-2084) | `CE4342_ASteadyGroupDragFrame_Allocates0B_OneWorldAndThreaded` | green: 0 B one-world and threaded; a steady frame posts no sim job | new |
| Hrot.Presentation.Tests whole | `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build` | 2164 / **10** / 11 skip | 7 of the 10 red at base by name (`CE2138`, `CE1309`, `CE1086`, `CE1380`, `CE4388`, `CE1227_V`, `E2b_V`); 3 (`CE1039` OneWindowPerProcess, `CE4582`, `P7B_In3D`) are ORDER-dependent window rails: green alone and green in the base's 10-test set at head exactly as at base. The 11 skips are env-gated (scale-one terrain, live OSM, audio, hidden-window) — none touched |
| Fdp.Presentation.Tests whole | `xvfb-run -a dotnet test FDP/Engine/Fdp.Presentation.Tests --no-build` | 841 / 1 / 0 | `CE4351_AFixedBufferOfStructs…` red at base |
| Fdp.Toolkits.Tests whole | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 5068 / 7 / 1 skip (`AllReaders_ZeroAlloc` flaky-marked) | the 7 `S0*` type rails red at base by name; gizmos + edits namespaces 227 / 0 |
| integration of the invariant (rule 8 row 8) | Hrot.ClusterRunner.Tests `EverySystemStateFieldIsClassifiedTests`, `EverySystemForgetsTheLastWorldTests` (the §7f state rails this touches) | 3 red: 6 systems / 319 fields / 45 class-B | red at base; neither touched type appears in any list (`DataDrivenGizmoSystem._restoresSeen` classified A; `StatelessGizmoSystem._epoch` under `*._epoch`) |
| the live re-run (④) | `matrix.py` on two real editors (§0) | every edit and live case all-move; one Undo restores (Edit) | §1 |
| design gates | `python3 scripts/design-digest.py --check` · `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/UX/DESIGN_Map_Interaction.md` · `python3 scripts/rulings-check.py` | no new finding (4 pre-existing designs without INVENTORY) · 15 / 15 parse · 685 / 685 | — |
| tracker | `python3 scripts/tracker-counts.py --check` | OK — 3259 rows, open 712 / done 2531 | +2 rows |

Working tree clean after every run (only this batch's paths modified). Golden files: none moved. Quarantine: no new skip, no new flaky mark.
Not run: `run-system-tests.sh` (T3) — no route or scenario behaviour changed.

## 4. Ids
CE-4342 closed with this evidence. New (ui block, appended at the end of the ui area): **CE-1953** `GET /view`'s screen point is not the window's pixel (2-D y +215 px; 3-D offset) · **CE-1954** a press on an unselected unit drags it on the threaded editor only. No ruling ids.
