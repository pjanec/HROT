<!--STATUS
state: LIVE
build-state: DISPATCH — UI/CGF lane. UXI-05 slice: the follows-focus registry model + CGF File-menu
  adoption via the shared CgfEditorShellToolbar helper. The four gizmo-block cleanup (UXI-13) is OUT.
updated: 2026-08-26
current-answer: pointer + autonomy. DESIGN (with UML): DESIGN_Cgf_Menu_Follows_Focus_Slice.md
  (READY-TO-BUILD; §6 decision settled — CGF File items are GLOBAL).
known-conflict: extends CgfEditorShellToolbar.cs + CgfSubsystem.cs + EditorSubsystem.cs + the shared
  engine GlobalMenuRegistry/MenuCommandAdapter/WindowManager ⇒ UI/CGF lane; rule-4 re-pull (files are hot).
-->
# HANDOFF — **CGF menu adoption + the follows-focus registry model** *(UXI-05 — UI/CGF lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`CE-`** ids; **you allocate the numbers** *(rule 3; last used `CE-040`)*; state every id *(rule 5)*.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — **do NOT design here**
📄 **[`DESIGN_Cgf_Menu_Follows_Focus_Slice.md`](../../DESIGN_Cgf_Menu_Follows_Focus_Slice.md)** — READY-TO-BUILD.
⭐⭐ **Class + sequence diagrams live in the design §4/§5** — check them before building *(obligation ③)* and report match/deviation. Intent basis: `UX/UX_Feature_Menu_Follows_Focus.md` *(UXI-05)* + `UX/UX_Feature_Shell_Parity.md` *(UXI-35)*. §6 decision **settled: CGF File items are GLOBAL.**

## 1. ⛔ AUTONOMY + BUILD RULES
§0-style autonomy *(decide-and-log; stop the ITEM not the batch — R-106; DONE = design §7 rails green)*. Codebase-memory not connected ⇒ the **CLI** *(`codebase-memory-mcp cli <tool> '<json>'`)*, ⛔ not grep-only. Build the AFFECTED PROJECTS *(`Fdp.Presentation` · `Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.SystemTests` + `Fdp.Presentation.Tests`)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`; conformance/system suite is **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §3 — five items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Registry per-perspective model** — `MenuItemNode` gains `List<MenuBinding>` *(record `(string? Perspective, Action? OnClick, Func<bool>? GetChecked, Action<bool>? OnCheckedChanged)`)*; the three `GlobalMenuRegistry.Register*` methods gain `string? perspective = null` | ⛔⛔ **backward-compatible** — a call with no perspective = ONE global binding ⇒ **editor menu byte-identical** *(the gate)* |
| ⭐⭐ **②** | **Draw-time resolution** in `WindowManager.RenderGlobalMenu` — per leaf: perspective binding → global → **skip**; **skip an intermediate node with no visible descendant** | ⛔ empty-parent skip is required *(UXI-05 risk: dead headers)*; `Shortcut`/`Icon`/`GetEnabled`/`DynamicLabel` stay node-level |
| ⭐⭐ **③** | **Extend the helper** — `MenuCommandAdapter.Register` gains `string? perspective = null` *(passthrough)*; `CgfEditorShellToolbar.RegisterCommonCore` gains a `GlobalMenuRegistry? menu` param + a **menu-emit pass** over the SAME `Layout`/`shell` *(File/* paths)* | ⛔⛔ **ONE list** — ⛔ no CGF-private menu list; the editor's call passes its `GlobalMenu`, keeping its menu byte-identical |
| ⭐ **④** | **Adopt on CGF** — pass `windowManager.GlobalMenu` to the helper ⇒ CGF gains `File/Save`, `File/Open Asset…`, `File/New Asset…`, `File/Reload`, registered **GLOBAL** *(§6)* | subset only *(what CGF services)*; OMIT Save-All/Scenario *(ruling 49)* |
| ⭐ **⑤** | **Conformance** — a `SUBSET-BY-DESIGN` **menu** verdict mirroring CE-040 *(CGF paths ⊆ editor's; empty CGF `File` = violation)* + a **unit rail** for resolution *(two bindings on one path, flip `CurrentPerspective`, assert switch + not-drawn + empty-parent-skip)* | ⛔ NOT full-array identity — the editor has Save-As/Save-All/Scenario |

## 3. ⭐ DONE — rails *(design §7)*
- **editor menu byte-identical** *(a `RenderGlobalMenu`/registry-dump diff before/after — migration step-1 gate)*.
- the resolution unit rail green; CGF's `File` menu dumps the four items; the `SUBSET-BY-DESIGN` menu verdict asserts ⊆ editor & anti-vacuity.
- affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `FDP/Engine/Fdp.Presentation/…/GlobalMenuRegistry.cs` · `MenuCommandAdapter.cs` · `WindowManager.cs` *(the model + draw + additive, backward-compatible)* · `Hrot.Editor.AiShared/Windows/CgfEditorShellToolbar.cs` · `EditorSubsystem.cs` *(pass its GlobalMenu)* · `CgfSubsystem.cs` *(adopt)* · `ClusterConformanceRails.cs` · `Fdp.Presentation.Tests`. ⚠ These files are **hot** *(toolbar slice CE-037..040 + AX-009)* — ⭐ **rule-4 re-pull** before the final commit. ⛔ Do NOT touch the four gizmo-block bars *(SimHost/IG/ReplayBrowser — that's UXI-13, §9 of the design)* or the diagnostics lane's files.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design · the `CE-` ids. **When done:** fold any as-built deviation back into `DESIGN_Cgf_Menu_Follows_Focus_Slice.md` *(obligation ⑤)*; the report points at the design and carries the DECISION LOG.
