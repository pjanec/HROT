<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../DESIGN_Struct_Editor.md — the design to extend (S1–S12, K1–K6 approved)
  - ../../UX/DESIGN_Edit_Mode_Undo.md — the undo history the hosts' writes must land in (B-14, built by ui)
  - ../../UX/DESIGN_Input_And_Commands.md — §6b: one input chain for keys AND mouse (R-4163)
-->

# NOTE — coordinator → backend: extend the struct editor design before the overnight build

🔒 User, `2026-11-07`: *"Could you please write a note to the backend to extend the design by all the points you mentioned? Once extended, we
will go back to you."* — and earlier: *"Later I would like to give that to one of the lanes for an overnight autonomous batch, including replacing
all the forms and many places in the current UI where we can benefit from reusing the new library for editing forms."*

⭐ **DESIGN WORK ONLY — no code in this step.** Extend [DESIGN_Struct_Editor.md](../../DESIGN_Struct_Editor.md), commit, push, and write
`NOTE_Backend_To_Coordinator_Struct_Editor_Extended.md` naming the new sections. The coordinator then writes the overnight handoff from it.
⭐ ui and behaviors are ON HOLD (user) — no collision risk; the batch will be yours alone.

## The six gaps — add each to the design

| # | add | what it must say |
|---|---|---|
| **G1** | **a `sequenceDiagram`** (the CLAUDE.md UML obligation — the design has class + module diagrams only) | ① a typed value: keystroke → `TypeEditor.Parse` (refused ⇒ the typed text stays, the old value stays, the error row) → `Validate` → the HOST's write (which host writes how: `Edits.Submit`, a params source, a blackboard) → the undo history; ② a list **Move** / **Duplicate**: `IContainerOps` → the bindings → what is rebuilt. ⚠ Draw the dead edges if a host has no write path |
| **G2** | **an ADOPTION TABLE — every place that should draw through the table or the kit** | 🔒 the user wants ALL forms and every place that benefits. Enumerate with `search_graph` (+ grep): hand-made forms, `BeginTable` two-column property grids, label+input rows outside `FormGrid`, ad-hoc lists with Add / Remove buttons, entity / point pickers outside `ReferenceLeafDrawers`. One row per place: file · today · which step moves it · its existing rails (frame rail / goldens) · ⛔ or *"stays, because …"*. Name the INVENTORY queries and counts (CLAUDE.md INVENTORY rule) |
| **G3** | **zero allocation per steady frame** (R-2084) | the table, every kit widget and every host: 0 B on a steady frame — formatted text CACHED per node (as `FormView` does, CE-4285), recomputed on a value change only; the rail per widget and per host (`GC.GetAllocatedBytesForCurrentThread`) |
| **G4** | **keys and gestures through the ONE input chain** (R-4163, Input §6b) | Ctrl+D, Alt+↑/↓, Ctrl+C / Ctrl+V, Del, Enter / Esc in a cell = declared bindings of a **table scope** (and drag = a declared gesture), ⛔ not raw ImGui key polls; how the scope nests under the map / window scopes |
| **G5** | **undo per host** | in Edit mode every host's commit lands in `EditHistory` (B-14 / U2) as ONE step per committed value or list operation; a rail per host: edit → Ctrl+Z → the old value. Preview / Live hosts: no undo (R-4120), say so |
| **G6** | **the two measure-first points as GATES** (§7 ⚠) | a `JsonObject`-backed `IValueBinding` with no C# owner (step 6) · element bindings valid across a Move without a full rebuild (step 2) — **measure them NOW, in this design pass**, and write the result into §7; if one fails, the design says what the step does instead (or that the step stops with a QUESTION note) — ⛔ never left for the night to guess |

## Also

| | |
|---|---|
| **ordering** | keep §7's order; the entity forms (CE-3385) and the inspector (CE-3386) LAST; presets (CE-3388…CE-3394) after step 6 — confirm or argue |
| **out of scope** | CE-3376 (parameter assets) — the user and coordinator design it first |
| **gates** | the design-digest and mermaid checks clean; every quoted rule cited by doc / ruling |

---
_coordinator lane, 2026-11-07_
