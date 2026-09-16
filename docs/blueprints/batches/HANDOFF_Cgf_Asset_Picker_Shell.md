<!--STATUS
state: LIVE
build-state: DISPATCH — UI/CGF lane. Axis-C E2: compose the asset-picker/new-asset shell on CGF so
  Slice A's greyed Open/New items light up. Lift 3 launchers to shared; factor the duplicated create-core
  into ONE type (ruling 9); wire CGF's picker.
updated: 2026-08-26
current-answer: pointer + autonomy. DESIGN (with UML): DESIGN_Cgf_Asset_Picker_Shell_Slice.md (READY-TO-BUILD).
  Roadmap: gap map §2c Axis-C E2. Precursor: Slice A (CE-046..048, merged).
known-conflict: edits EditorSubsystem.cs + CgfSubsystem.cs + ScenarioMenuCommands.cs (AiShared) + the shared
  toolbar list — the hot files Slice A and the backend batch just touched; rule-4 re-pull. ⛔ Disjoint from
  the MCP lane (DebugApi) and the backend lane (test projects).
-->
# HANDOFF — **CGF asset-picker / new-asset shell** *(Axis-C E2 — UI/CGF lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`CE-`** ids; **you allocate the numbers** *(rule 3; last used `CE-048`)*; state every id *(rule 5)*.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — do NOT design here
📄 **[`DESIGN_Cgf_Asset_Picker_Shell_Slice.md`](../../DESIGN_Cgf_Asset_Picker_Shell_Slice.md)** — READY-TO-BUILD. Class + sequence UML in **§4/§5**; the 5 items in **§3**; the measured inventory in **§2**. Check the UML before building *(obligation ③)*, report match/deviation, and **fold any deviation back into the design §2/§4/§5** before the batch closes *(obligation ⑤)*.
⭐⭐ **The key measured fact:** CGF already has the whole SERVICE layer *(registry/create/recipes/catalog — MA-019..023)*. **E2 is the PICKER UI + wiring**, and the create-core is DUPLICATED inline in both `EditorSubsystem` and `CgfSubsystem` ⇒ factor to ONE shared type *(ruling 9)*.

## 1. ⛔ AUTONOMY + BUILD RULES
Decide-and-log; stop the ITEM not the batch *(R-106; DONE = design §6 rails green)*. Codebase-memory not connected ⇒ the **CLI**, ⛔ not grep-only. Build the AFFECTED PROJECTS *(`Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF`)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`; conformance suite **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §3 — five items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Lift** `AssetPickerLauncher` · `NewAssetLauncher` · `AssetPickActionRouter` *(`Hrot.Editor/Browser`)* → `Hrot.Editor.AiShared/Browser` | ⚠ **HN-037: measure the field captures first** — a lift, ⛔ not `s/old/new/`. §2 confirms they are pure seam glue *(no `IEditorLogic`/`EditorApplication`)* |
| ⭐⭐⭐ **②** | **Extract `AssetCreateController`** from `EditorSubsystem.ShowNewAssetDialog`/`CreateAssetCore` *(:3797/:3831)* **and DELETE CGF's `AssetShellCreate` duplicate** *(`CgfSubsystem.cs:1530`)*; both hosts compose the one shared type | ⛔⛔ **ruling 9** — ONE create-core. ⛔ a third copy is the whole failure this item exists to prevent |
| ⭐⭐ **③** | **CGF composes** the launchers + create-controller and passes REAL `openPicker`/`openSaveAsDialog` to `ScenarioMenuCommands.Register` *(replacing the `null`s at `CgfSubsystem.cs:1770-1782`)* | ⭐ Slice A's greyed items light up with **zero menu code** |
| ⭐ **④** | **Toolbar** — add OpenAsset/NewAsset to CGF's `HostServices` via the shared `CgfEditorShellToolbar` list *(:1744)* | ⛔ no new toolbar model — just enable what the editor exposes; if it collides with the future toolbar-customization AQ, STOP and report |
| ⭐ **⑤** | **Conformance** — extend the `SubsetShape`/equality rail: CGF's Open/New now **enabled**; a no-modal host keeps them greyed-with-cause | reuse Slice A's equality-rail pattern; ⛔ no new verdict type |

## 3. ⚠ THE ONE MEASURED RISK — the modal itself *(design §2)*
`AssetPickerLauncher.openPicker` is backed in the editor by `_shellPickers.OpenPicker` *(a modal over `WindowManager`)*. **Measure whether CGF can compose an equivalent** *(shell-picker infra shareable, or CGF composes its own over its `WindowManager`)*. ⛔ If a host genuinely cannot host a modal, the items **stay greyed-with-cause** *(ruling 49)* — that is the correct end state, a finding, not a failure to force.

## 4. ⭐ DONE — rails *(design §6)*
- editor byte-identical after lift+extract; CGF Open/New **enabled + functional**; the `AssetShellCreate` duplicate **gone**; no-modal host greyed-with-cause; conformance verdict holds; toolbar set matches the shared list.
- affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 5. ⭐ LANE & COLLISION
⭐ **Yours:** `Hrot.Editor.AiShared/Browser/**` *(the lifted launchers + `AssetCreateController`)* · `EditorSubsystem.cs`/`CgfSubsystem.cs` *(compose)* · `ScenarioMenuCommands.cs` *(already takes the seams — Slice A)* · `ClusterConformanceRails.cs`. ⚠ hot files — **rule-4 re-pull** *(the coordinator moved during Slice A + the backend merge)*. ⛔ Do NOT touch DebugApi *(MCP lane)* or test-harness/leak code *(backend lane)*.

## 6. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design if you touch its UML · the `CE-` ids. **When done:** fold any as-built deviation into `DESIGN_Cgf_Asset_Picker_Shell_Slice.md` *(obligation ⑤)*; the report points at the design + carries the DECISION LOG.
