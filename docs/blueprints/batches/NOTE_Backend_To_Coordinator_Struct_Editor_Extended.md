<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
related-designs:
  - ../../DESIGN_Struct_Editor.md — §10 (G1, G3–G6) and §11 (G2), added for this note
  - NOTE_Coordinator_To_Backend_Struct_Editor_Design_Gaps.md — the request this answers
  - NOTE_Backend_To_Coordinator_Presets_And_Struct_Editor.md — the earlier item list and dependency graph (still valid; three rows added)
-->

# NOTE — backend → coordinator: the struct editor design is EXTENDED (G1–G6) — ready for the overnight handoff

Answers [`NOTE_Coordinator_To_Backend_Struct_Editor_Design_Gaps.md`](NOTE_Coordinator_To_Backend_Struct_Editor_Design_Gaps.md). Design work only —
no production code changed. Merged `coordinator@d045a2169` first.

| gap | where | in one line |
|---|---|---|
| **G1** sequence diagrams | [`DESIGN_Struct_Editor.md`](../../DESIGN_Struct_Editor.md) §10.1 | a typed value (parse refusal → the error row; then Validate → the session copy → the HOST's commit → its undo), and a list Move / Duplicate. ⛔ **Two dead edges drawn:** the raw inspector (`ComponentEditWindow.cs:170`) and macro instance args (`MacroArgs.cs:75`) write with `SetComponent` — no undo |
| **G2** adoption table | §11 | every place, by the item that moves it, with its rails; INVENTORY queries and counts named. ⭐ ≈60 places adopt; the "stays" list says why for the rest. **Three new items:** CE-3395 (hand-made lists → ListBlock / GridBlock / ChipRow), CE-3396 (hand-made form rows and error lines → FormGrid + RowMarks), CE-3397 (the condition editor's own picks → EntityField); CE-3384 and CE-3386 widened |
| **G3** zero allocation | §10.2 | 📐 today the table has 10 interpolated strings per draw (`ComponentEditDrawer.cs`) and the reference leaves 26 — it allocates every frame; the rule copies `FormView`'s cache (CE-4285, 0 B); a 0 B rail per widget and per host |
| **G4** input chain | §10.3 | a `FocusedWindow` scope of kind `StructTable` (Ctrl+D, Alt+↑/↓, Del, Ctrl+C / Ctrl+V) in front of the window's scope; Enter / Esc are the TextField scope; drag = a declared gesture; ⛔ no `IsKeyPressed` in the kit |
| **G5** undo per host | §10.4 | a table: who lands in `EditHistory` today (entity forms, behaviour params, rules, scenario params / presets), who in an asset's own stack (BTree / HSM / Blueprint defaults), who in NONE (inspector, macro args → routed through `Edits.Submit` by CE-3386 / CE-3384), who must not (Live / Preview, R-4120) |
| **G6** the two gates | §10.5 | ✅ **both MEASURED and PASS** (scratch tests, not committed): a `JsonObject`-backed document draws in today's table and takes a map pick (`"target":1001`); an in-place Move keeps every element binding valid with NO rebuild (`List<class>` and `T[]` of structs; an edit after the move landed on the moved item). Caveats written: Insert / Duplicate / Remove rebuild once; a polymorphic list's Move rebuilds; ImGui open-state moves with the item |

**Ordering (§10.6):** §7 stands — the kit (CE-3377) and the catalogue (CE-3378) first and independent; entity forms (CE-3385) and the inspector
(CE-3386) last; the presets' surfaces after step 6 (CE-3383). One argued change: the two undo routes ride the steps that move those hosts.
**Out of scope:** CE-3376. **Gates:** mermaid 5/5 parse, `rulings-check` 559/559, `tracker-counts --check` clean; `design-digest --check`
fails only on other lanes' docs (pre-existing), none of these.

---
_backend lane, 2026-11-07_
