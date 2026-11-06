<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — HOW to run this list beside the backend lane (merge as you go, status file, notes)
  - HANDOFF_Coordinator_To_Backend_Editing_UX_Build.md — the backend lane's list, running at the same time
  - ../../UX/DESIGN_Editing_UX.md — the programme; §4.6 the mockups (each one the acceptance spec of its item)
-->

# HANDOFF — coordinator → ui: build today's approved editing-UX designs (long list, runs until done)

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `9c50cec00`** *(re-dispatched before any start, rule 1a: + R-4156 skip-on-user-decision, + Q-B4, + eight items moved to backend; first stamp `d98efb832`)*. ⭐ Scope FROZEN there; later documents are FYI.
Merge `coordinator` first, push `chore: started HANDOFF_Coordinator_To_Ui_Editing_UX_Build at <sha>`, then follow
[the protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) — ⭐ **after every item: push, status file, fetch, merge `backend`, answer its notes.**
Work top to bottom; skip a blocked item and come back. ⛔⛔ **Nobody to ask — anything that needs the user's decision is SKIPPED, never blocking (R-4156):** a QUESTION note to Coordinator with your lean, the item (or just that part) marked *SKIPPED — needs user*, and on to the next item.

## Why

🔒 User, `2026-11-06`: *"find out what from our today's designs can be implemented … in autonomous mode. It must be something which do not raise
questions"* · *"put there much more … to keep the overnight worker busy. All what is pretty much clear."* Every item below is APPROVED by
the user (ruling cited); its design carries the diagrams and, for UI, the mockup that is its acceptance spec (R-4138, checked by you, R-4146).

## The items — in order

| # | what | design basis (build against it) | done when | ⏳ needs |
|---|---|---|---|---|
| **U-1** | `RowIcon`, `TreeRow`, `SectionHeader`, `FormGrid` in **`NodeEditor.Core`**; move Colour Icons' planned `IconGlyph` / `IconPalette` there too (Q-C1) | [UI conventions](../../UX/DESIGN_UI_Conventions.md) §2–§5 (R-4144…R-4147) | frame rails per helper (chevron < icon < label; a leaf at its depth's x; slate header; label column = min(widest + 14, max(25 %, 90 px)), cut with … + tooltip); 0 B steady frame | — |
| **U-2** | colour icons: `Icons` key → (glyph, colour), `GlyphButton` / `GlyphToggle`, the shared no-close-box **icon tab** (neutral palette); **CE-4286** the rule editor's tabs become a real tab bar | [Colour icons](../../UX/DESIGN_Colour_Icons.md) §2, §3.1, I1 (R-4139…R-4141, R-4143) | the mode indicator unchanged on screen; tab ids stable when a label changes | U-1 |
| **U-3** | the five monochrome surfaces in colour (ORBAT mode switch, Details mode switch, AI graph tabs, Blueprint attachment pills, Mission task icons) | Colour icons §3, I3 | a frame rail per surface asserts glyph + colour; compared with the approved strip | U-2 |
| **U-4** | the trees onto `TreeRow`: `TreeLayout`, `SaveAsBrowserDialog`, `SharedOrbatPanel` (the `ArrowButton` goes), then `AssetBrowserPanel`, `MyBlueprintPanel`, `FolderTreePicker`, `BehaviorPickerTree` | UI conventions §2, C2 | each feature's own suite (T-1) + the C1 rail on the real drawer; side-by-side with `ui-conventions-visual.svg` §① | U-1 |
| **U-5** | the 45 `CollapsingHeader` calls onto `SectionHeader`, each with its icon; the raw editor's authority green kept as an override | UI conventions §4, C3 (Q-C5) | slate everywhere except authority green | U-1, U-2 |
| **U-6** | display names: `DisplayNames.Of(member)` in `StructEdit.Core` — `[EditDisplayName]` widened to classes / structs, ONE humanizer otherwise (fold `EntityRoleLine.Words`, `AiPrimitiveNaming.DisplayNameFromTickCoreFqn`); `[EditIcon(key)]` | [Condition editor](../../UX/DESIGN_Condition_Editor.md) §8.4 (R-4149) | a rail: attribute wins, humanized fallback (`DeadlineSeconds` → *Deadline* · s, `EntryOrExit` → *Entry or exit*); cached, 0 B | — |
| **U-7** | **CE-4288** the struct editor readable: names (U-6), the kind as the node's own row, no empty `Bind` rows, numbers with units / *no limit*, a collapsed-node summary HOOK, *Add …* button + trash glyph; names + glyphs on the 19 condition kinds | Condition editor §8.1–§8.2, X1–X2 (R-4149) | the §8.1 frame again, measured: no class names, no `Bind` rows, every control at one x; side-by-side with `condition-expert-visual.svg` | U-1, U-6 |
| **U-8** | the entity-link chip (a thing by name + kind glyph + map pick) | Condition editor §8.2 ④, X3 | `Objective: Hold the bridge`, never `Key #10` | U-7 |
| **U-9** | the rule's When tab holds the condition + timing only; title / scoring / visibility move to Settings | Condition editor §8.2 ⑧, X4 | `RuleEditorR5bTests` + a probe of the tab's fields | U-2 |
| **U-10** | **show on map**: `MapPreviews.For(type)` (point, bearing, entity, area, route), one `MapPreview` per host over a LOCAL `DebugPrimitiveBuffer`; the ⌖ in the struct editor and in `FormGrid` rows; hover = preview, click = pin, row menu *Show on map · Centre the map here · Copy value* | [Show on map](../../UX/DESIGN_Show_On_Map.md) M1–M2 (R-4154, R-4155) | a frame rail: hover draws, click pins, a selection change clears; nothing on the wire; side-by-side with `show-on-map-visual.svg` | U-1, U-7 |
| **U-11** | the forms foundation **D0**: the ONE status palette in `Fdp.Presentation` (Blueprints' `EditorColors` folded in), the `FormRow` helper on `FormGrid` (status slot, marks, tooltips, invalid frame) | [Details forms](../../UX/DESIGN_Details_Forms.md) §6 (R-4138, R-4139) | the §6 row states as frame rails | U-1 |
| **U-12** | **D1**: `FormDecl` / `FormRow` / `FormView` / `FormCatalogue` — cached formatted value, a one-field `ScalarEditBox<T>` session only while editing (CE-4285); read-only = grey only (R-4150), copy menu (R-4152) | Details forms §2, §6, D1 | 0 B per steady frame (the CE-4285 probe) | U-11 |
| **U-13** | the label-first sweep: the panels (`DirectorWindow`, `ClusterScenarioPanel`, `ConfigPanel`, `WaypointEditorPanel`, `SpawnerPanel`, `ZoneEditorPanel` …), the 20 Blueprint node editors, the *promote to variable* popup, then `ComponentReflector` / `ImGuiPropertyTree` / **`ComponentDiffPanel`** (the Replay Browser's seek diff) onto `FormGrid`; search boxes, filter-checkbox rows and button rows stay (Q-C3) | UI conventions §3, C4 (R-4145, R-4147, R-4155) | grep for visible-label input widgets outside the §3.1 exceptions → 0; one rail per migrated panel | U-1 |
| **U-14** | the condition editor **C2**: `FactCatalogue` (reads U-6's attributes — no second copy of names), `RowProjection` (`ToDto` / `FromDto`), `ConditionSentence` (`BreakpointConditionSummarizer` folded in); the U-7 summary hook filled | Condition editor §2–§3, C2 | round trip: every catalogue row `FromDto(ToDto(x)) = x`; an unknown DTO ⇒ one Custom row, byte-identical | the 3 new leaves: ⏳ backend B-1 (do the existing kinds first) |
| **U-15** | **C3** the friendly view + add menu + presets, on the rule's When tab; **C4** the timing line + the Live column | Condition editor §4 (R-4142) | the mockup's states as frame rails; side-by-side with `condition-editor-visual.svg` | U-14 |
| **U-16** | **C5** the same editor in the replay search, breakpoints (the **Replay Browser's data breakpoints** included) and the mission task's *Advanced condition* | Condition editor C5 (R-4155) | each host shows only its kinds | U-15 |
| **U-18** | the rule editor **RX1–RX3**: CE-4245 and the Rules window on CGF; the rule detail as Details tabs; the List view of sentences | [Rule editor UX](../../UX/DESIGN_Rule_Editor_UX.md) RX1–RX3 (R-4130) | side-by-side with `rule-list-visual.svg` | U-14 for the sentences |
| **U-23** | the forms **D3–D5**: Unit, Scenario, Shape and Crew & cargo forms, every write through `Edits.Submit` | Details forms §3, D3–D5 (R-4136) | each form from Edit and Live, survives save and reload; read-only on a recording (§2.4) | ⏳ backend B-2 (`Write`), B-4 (attributes) |
| **U-24** | initial state **S5**: the Unit form rows (Aboard, Carried by, Carries, Weapon, In hand, Knows about, Damage) and the seat roles; the Brain tab's Character section (**S6** ui side) | [Initial state](../../UX/DESIGN_Initial_State.md) §3, §3a (R-4137) | side-by-side with `initial-state-visual.svg` | ⏳ backend B-5 |
| **U-25** | the **Point** map graphic (S4 ui side): the point tool, pickers listing it | Initial state S4 | a rule's point picked from *LZ Alpha* resolves | ⏳ backend B-5 (type id) |

⭐ **Moved to the backend lane** *(user: roles are not honoured — keep both lanes busy)*: U-17 → B-7 (Replay Browser seek bar), U-19 → B-9
(New Scenario picker), U-20 → B-10 (keymap), U-21 → B-11 (action catalogue), U-26 → B-12 (notifications), U-27 → B-14 (undo UI), U-28 → B-16
(seats UI), U-22 → B-17 (map interaction). ⭐ They use your U-1 / U-2 / U-11 — push those early; the backend lane waits on them.

### When blocked — always available

The other lane's REQUESTs · fold-backs into designs · the E17 label-first sweep (U-13) panel by panel · **CE-4290** (the Event Timeline's
lanes drawn empty while the index holds events — Replay Browser UX §1 B3).

⛔ **Not in this list** (open questions or another lane): the Replay Browser's default layout (Q-B3), the Federation window (Q-B6),
map interaction P3 / P7, the mission plan editor (behaviors), action catalogue A6 / A8.

## Gates (report per item — protocol §6)

---
_coordinator lane, 2026-11-06_
