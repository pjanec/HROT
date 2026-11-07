<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../DESIGN_Struct_Editor.md — THE design (S1–S12, K1–K6 approved; §10 sequences / 0 B / input / undo / gates; §11 the adoption table)
  - ../../DESIGN_Field_Declaration_Editor.md · ../../UX/DESIGN_Scenario_Presets.md — Define mode and the presets surfaces
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the protocol (ui and behaviors ON HOLD tonight)
  - HANDOFF_Coordinator_To_Backend_After_Mission_Plan.md — your earlier list; resume it AFTER this one
-->

# HANDOFF — coordinator → backend: the struct editor, overnight

**For:** backend, autonomous overnight. **From:** coordinator. **Dispatched at `<sha of the commit adding this file>`**. ⭐ Scope FROZEN there.
Merge `coordinator` first (restore after), push `chore: started HANDOFF_Coordinator_To_Backend_Struct_Editor_Overnight at <sha>`, then the
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): status file after every item. ⭐ **ui and behaviors are ON HOLD (user) — no collisions;
every file in §11 is yours tonight.**

🔒 User, `2026-11-07`: *"Later I would like to give that to one of the lanes for an overnight autonomous batch, including replacing all the forms
and many places in the current UI where we can benefit from reusing the new library for editing forms."* · design extended at the coordinator's
request ([note](NOTE_Coordinator_To_Backend_Struct_Editor_Design_Gaps.md) → [yours](NOTE_Backend_To_Coordinator_Struct_Editor_Extended.md)).

⛔⛔ **Never stop to ask; anything needing the user is SKIPPED** with a QUESTION note and your lean (R-4156). ⛔ **A step that breaks a host's rail
is not done** — fix it, or revert that host's move and file it; never skip / quarantine a test.

## The order — by value; if the night ends, what is done must be whole

| # | ids | what | done when |
|---|---|---|---|
| **N-0** | CE-3377 | **the widget kit** in `NodeEditor.Core.Widgets`: Chip (moved from `ReferenceLeafDrawers.cs:641`), ChipRow, ListBlock, GridBlock, RowMarks; `EntityField` in `Fdp.Presentation` (§8, K1–K6) | a frame rail per widget, rendered next to the mockup's panel F (R-4146), **0 B on a steady frame** (§10.2) |
| **N-1** | CE-3378 | **one registration per type** — `TypeEditor<T>` + the catalogue; the 16 drawers + 13 field editors moved in; hosts stop passing dictionaries (§3, §11 row 1) | Quaternion / Guid / Type draw on every host (one rail over all hosts); §11's listed rails green |
| **N-2** | CE-3379 | **list operations + the list forms** B1–B6 + the ⋮ item menu (§4, §10.1 second sequence); keys as bindings of the `StructTable` scope (§10.3) | a rail per form; insert / move / duplicate round-trip; ⚠ drag: see *Input* below |
| **N-3** | CE-3380 | **Parse / Validate per field**, the error row; nullable, flags, `long` (S6, S9) | typed garbage → the row says why, the typed text and the old value stay |
| **N-4** | CE-3381 | **default / changed + read-only** in the table (S7, S8) | the mockup's grey / amber + ↺, measured (R-4138); a recording's table all grey, ⌖ still works |
| **N-5** | CE-3382 | **one entity chip** for EntityRef and EntityLink, the *gone* mark, the picker's type-ahead (§5) | a gone entity reads red; side · kind · distance in the picker |
| **N-6** | CE-3383 · CE-3387 | **Define mode** — `DeclaredEditDocumentBuilder` (gate measured ✅, §10.5) + the Define cells + the host's own words; scenario parameters move onto it | `field-editor-visual.svg` side by side; §11 row 3's rails green |
| **N-7** | CE-3388 … CE-3394 (+ CE-3374 first if the presets design needs it) | **scenario presets** on the Define+Fill table — load with a preset, `SaveGate`, HTTP, the preset editor, the load dialog, the split ▶, the glyphs | [Scenario presets](../../UX/DESIGN_Scenario_Presets.md)' own acceptance + mockup |
| **N-8** | CE-3384 | **the struct-table hosts** (§11 row 2: `BehaviorParamsForm` + its 7 hosts, `ParamsDefaultsForm`, `ParamArgsTable`, node properties, `VariableEditModal`, replay search criteria, Game master) + blackboards ×3; **macro args → `Edits.Submit`** (undo, §10.4) | each host's listed rails green; one undo rail per host (edit → Ctrl+Z → the old value) |
| **N-9** | CE-3395 · CE-3397 | hand-made **lists** → ListBlock / GridBlock / ChipRow (§11 row 6); the condition editor's own picks → `EntityField` | §11's rails green; 0 B per host |
| **N-10** | CE-3385 · CE-3386 | **entity forms** (`FormView` onto the kit, K2 — its declared rows stay, R-4120) and **the inspector** onto the catalogue + **`Edits.Submit` in Edit mode** (undo) | `FormViewTests`, `TheUnitFormDrawsTests` unchanged in content; the inspector's rails; undo rail |
| **N-11** | CE-3396 | hand-made **form rows and error lines** → FormGrid + RowMarks (§11 row 7 — modals, node drawers, panels) | §11's rails; `LabelFirstFormsFrameRail` |

⭐ **Promote from §11's "stays" list:** nothing tonight.

## Rules that apply to every item

| | |
|---|---|
| **0 B** | every widget AND every host moved: `GC.GetAllocatedBytesForCurrentThread()` around the 3rd steady frame `== 0` (§10.2); ⛔ no `$"…"` / LINQ in a draw path |
| **input** | keys = bindings of the `StructTable` scope on K1's `Keymap` (built) — ⛔ no `ImGui.IsKeyPressed` in the kit. ⚠ **Drag:** K7 (gestures as bindings) is NOT built (ui, on hold) ⇒ use ImGui's drag-drop API inside `ListBlock` / `ChipRow` ONLY, behind one function, and mark it *"K7 adopter"* in the design; the ⋮ menu and Alt+↑/↓ always work (K4) |
| **undo** | Edit-mode commits land in `EditHistory` as ONE step per value / list op (§10.4); Preview / Live: none |
| **fidelity** | render the real frame under `xvfb-run -a` for each mockup state (`struct-editor-visual.svg` A–F, `field-editor-visual.svg`, the presets mockup), compare, fix, render again — the side-by-side into your report as an FYI (R-4146); ⛔ no screenshots committed |
| **as-built** | fold every deviation into `DESIGN_Struct_Editor.md` (obligation ⑤) before the item closes |
| **tests** | build the TEST project you run (the second stale-binary trap); display rails under `xvfb-run -a` (they skip silently without a display) |

## Gates (report per item — protocol §6)

The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8: N-1 / N-8 / N-10 change every host's drawing ⇒ each §11 rail set, named, run, with counts.
⭐ At the end: a full `Fdp.Presentation.Tests` + `Hrot.Presentation.Tests` + `Hrot.Editor.AiShared.Tests` + `Hrot.Blueprints.Tests` run in the
background, xvfb, counts vs the base sha.

---
_coordinator lane, 2026-11-07_
