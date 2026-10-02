<!--STATUS
state: LIVE
build-state: DISPATCH — UI/CGF lane. Axis-C E3: extract the tool/selection/camera/rename orchestration from
  the editor monolith into shared systems (finish PACK2-E002), and DELETE CGF's hand-rolled parallels.
updated: 2026-08-26
current-answer: pointer + autonomy. DESIGN (with UML): DESIGN_Cgf_Tool_Selection_Camera_Slice.md (READY-TO-BUILD).
  Roadmap: gap map §2c line 171. Precursor: E2 (CE-049/050, merged).
known-conflict: extracts from EditorSubsystem.cs + edits CgfSubsystem.cs + populates ScenarioEditorModule
  (Hrot.Presentation). Hot files — rule-4 re-pull. ⛔ Disjoint from the MCP lane (DebugApi) and backend (tests).
-->
# HANDOFF — **CGF tool / selection / camera / rename** *(Axis-C E3 — UI/CGF lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`CE-`** ids; **you allocate the numbers** *(rule 3; last used `CE-050`)*; state every id *(rule 5)*.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — do NOT design here
📄 **[`DESIGN_Cgf_Tool_Selection_Camera_Slice.md`](../../DESIGN_Cgf_Tool_Selection_Camera_Slice.md)** — READY-TO-BUILD. Class + sequence UML in **§4/§5**; the 5 items in **§3**; the inventory in **§2**; ⛔⛔ **the two-way reconciliation risk in §6 — read it first.** Check the UML before building *(obligation ③)*, report match/deviation, and fold any deviation back into the design *(obligation ⑤)*.
⭐⭐ **The intended home already exists and is EMPTY:** `ScenarioEditorModule.RegisterSystems` was reserved for *"PACK2-E002 tool migration"* and never populated — E3 finishes it.

## 1. ⛔ AUTONOMY + BUILD RULES
Decide-and-log; stop the ITEM not the batch *(R-106; DONE = design §7 rails)*. Codebase-memory not connected ⇒ the **CLI**, ⛔ not grep-only. Build the AFFECTED PROJECTS *(`Hrot.Presentation` · `Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF`)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`; conformance suite **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §3 — five items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Lift** `EditorTool` · `ActivateEditorToolEvent` · `EditorSpawnAdapter` to shared; **move `CenterOnEntityCommand` to Core** *(beside `SelectEntityCommand`/`OpenRenameDialogCommand`)* | ⚠ HN-037: measure captures — a lift, ⛔ not `s/old/new/` |
| ⭐⭐⭐ **②** | **Populate `ScenarioEditorModule.RegisterSystems`** with `ToolActivationDrainSystem` · `SelectEntitySystem` · `CenterOnEntitySystem`, extracted from `EditorSubsystem`'s drain *(`:4907/:5000/:5013`)* | ⛔ thread `_spawnAdapter`/`_mapPickAdapter`/`EntityInfo`/`EntityWriteRouter` as module deps; ⛔ do NOT drag `IEditorLogic` in |
| ⭐⭐⭐ **③** | **Extract `EntityRenameModal`** *(ImGui)* → `Hrot.Editor.AiShared.Browser` *(beside `AssetRenameModal`)*, driven by `OpenRenameDialogCommand` | ⛔ windowed-host only; a headless node never registers it *(ruling 49, like the E2 modal)* |
| ⭐⭐⭐ **④** | **Editor delegates** — delete the drain + center/rename handlers + inline modal from `EditorSubsystem`; register the module + modal | ⛔⛔ **editor byte-identical** *(the gate)* |
| ⭐⭐ **⑤** | **CGF composes + DE-DUPS** — register the module + modal over CGF's existing `MapCanvas`/`MapCamera`/`DefaultSelectionState`/gizmo stack, and **DELETE `CgfSubsystem.CenterCameraOnEntity` *(:2201)* + the ad-hoc rotate/context-menu parallels** | ⛔⛔ **§6 — CGF's parallels must DIE, not sit beside the shared path** |

⚠ **Selection is NOT map-pick** *(design §2)* — E3 = `DefaultSelectionState` *(viewport)*; ⛔ `IMapPickService` is Axis-B, untouched.

## 3. ⭐ DONE — rails *(design §7)*
- editor byte-identical; CGF activates the **same tool set** + center/rotate route through the shared systems; **a source-scan rail asserting CGF's deleted parallels are gone** *(like E2's create-core rail)*; `SelectEntity` writes `DefaultSelectionState` on both; the rename modal works on both windowed hosts, absent (not broken) headless.
- affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `Hrot.Presentation/ScenarioEditorModule.cs` + the new systems · `Hrot.Editor.AiShared/Browser/EntityRenameModal.cs` + the lifted types · `EditorSubsystem.cs` *(delete the drain)* · `CgfSubsystem.cs` *(compose + delete parallels)* · `ClusterConformanceRails.cs`. ⚠ hot files — **rule-4 re-pull**. ⛔ Do NOT touch DebugApi *(MCP lane)*, test-harness code *(backend lane)*, or `IMapPickService` *(Axis-B)*.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design if you touch its UML · the `CE-` ids. **When done:** fold any as-built deviation into `DESIGN_Cgf_Tool_Selection_Camera_Slice.md` *(obligation ⑤)* — **especially the §6 two-way reconciliation: state per host what was deleted and what it now routes through**; the report points at the design + carries the DECISION LOG.
