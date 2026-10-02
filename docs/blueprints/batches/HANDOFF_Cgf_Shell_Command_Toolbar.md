<!--STATUS
state: LIVE
build-state: DISPATCH — UI/CGF lane. CE-016 §7 (slice A2): route CGF's main toolbar through the shared
  ShellEditorCommands -> ToolbarCommandAdapter (+SilkIconProvider) pipeline. Menu (UXI-05) is a LATER slice.
updated: 2026-08-26
current-answer: pointer + autonomy. The DESIGN (with UML + the approved A2 decision):
  DESIGN_Cgf_Shell_Command_Toolbar_Slice.md. Decision trail: Architect_Question_58 (A2 approved).
known-conflict: shares CgfSubsystem.cs + EditorSubsystem.cs with this lane's own in-flight work ⇒
  sequence AFTER it; rule-4 re-pull before the final commit. ⛔ does NOT touch the diagnostics lane's files.
-->
# HANDOFF — **CGF shell-command + main-toolbar adoption** *(CE-016 §7, slice A2 — UI/CGF lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`CE-`** ids *(cgf==editor)*; **you allocate the numbers** *(rule 3)*; state every id *(rule 5)*.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — **do NOT design here**
📄 **[`DESIGN_Cgf_Shell_Command_Toolbar_Slice.md`](../../DESIGN_Cgf_Shell_Command_Toolbar_Slice.md)** — READY-TO-BUILD.
⭐⭐ **The class + sequence diagrams live in the design §4/§5** — ⛔ this handoff does NOT redraw them; **check them before building** *(obligation ③)* and report match/deviation. Decision trail + options + INVENTORY: [`Architect_Question_58`](Architect_Question_58_Cgf_Shell_Command_Toolbar_Adoption.md) *(**A2 approved**, user `2026-08-26`)*.

## 1. ⛔ AUTONOMY + BUILD RULES
§0-style autonomy *(decide-and-log; stop the ITEM not the batch — R-106; DONE = the design §6 rails green)*. Codebase-memory not connected ⇒ use the **CLI** *(`codebase-memory-mcp cli <tool> '<json>'`)*, ⛔ not grep-only. Build the AFFECTED PROJECTS *(`Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.SystemTests`)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`; the conformance/system suite is **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §3 — the four items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **A shared toolbar common-core helper** `CgfEditorShellToolbar.RegisterCommonCore(shell, toolbar, icons, hostServices)` in **`Hrot.Editor.AiShared`** | ⛔⛔ **ONE registration list** *(ruling 58)* — ⛔ NOT a CGF-private copy of the editor's list |
| ⭐⭐ **②** | **Extract** the editor's inline toolbar wiring *(`EditorSubsystem.cs:4464-4562`)* to call the helper | ⛔⛔ **behaviour-preserving** — the editor's rendered toolbar entry list is **byte-identical** after *(the design §6 gate: a `MainToolbarManager.BuildViewModel` diff before/after)* |
| ⭐⭐ **③** | **Adopt on CGF** — `new SilkIconProvider(windowManager.Atlas)`, call the helper on `windowManager.ShellCommands`/`.MainToolbar`, **delete** the two ad-hoc `ImGui.Button` entries *(`CgfSubsystem.cs:1657-1667`)* + the dangling `ToolbarSep_TimeToPersp` *(`:1087`)* | ⭐ debug-step handlers route through **CGF's cluster debug controller** *(CE-025..028)*, ⛔ not the editor's `AiDebugCommands`. Subset: Save/SaveAll/Open/New/QuickReload + step; **OMIT** fullRebuild + scenario-menu *(ruling 49: absent, not greyed)* |
| ⭐ **④** | **Flip the conformance rail** — delete the `main-toolbar` known-divergence entry *(`ClusterConformanceRails.cs:256-259`)* | ⛔ assert the **shared subset** SAME by id+sortOrder+visibility — NOT full-array identity; the editor legitimately has more |

## 3. ⭐ DONE — rails *(design §6)*
- CGF's `main-toolbar` dumps the shared subset **with icons** by id+sortOrder; the divergence entry is deleted ⇒ the three-way rail asserts SAME on the subset.
- the editor's toolbar entry list is **unchanged** *(the byte-identical extraction gate)*.
- each command invokes through `IEditorCommands.Invoke` *(headless `ToolbarCommandAdapter.GetState` rail)*; debug-step reaches CGF's controller.
- omitted commands declared absent *(ruling 49)*; affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `Hrot.Editor.AiShared/**` *(the new helper)* · `EditorSubsystem.cs` *(the extraction)* · `CgfSubsystem.cs` *(the adoption — toolbar region ~`:1077`/`:1657`)* · `ClusterConformanceRails.cs`. ⚠ **These files also carry THIS lane's other in-flight work** — sequence this AFTER it; ⭐ **rule-4 re-pull** before the final commit. ⛔ Do NOT touch the diagnostics lane's DebugApi / `Program.cs` / provider files.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design · the `CE-` ids. **When done:** fold any as-built deviation back into `DESIGN_Cgf_Shell_Command_Toolbar_Slice.md` *(obligation ⑤)*; flip the gap-map CE-016 §7 row; the report points at the design and carries the DECISION LOG.
