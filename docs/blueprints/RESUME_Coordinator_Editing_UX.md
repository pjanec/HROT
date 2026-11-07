<!--STATUS
state: LIVE
updated: 2026-11-06
current-answer: §1 where the programme stands · §2 what to do next · §3 the other lanes at writing
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/UX/DESIGN_Editing_UX.md — OWNS the editing-UX programme (inventory, target, decisions D1–D18, slices E0–E11, the design queue §4.5)
  - docs/CAPABILITIES.md — §14, the product-facing summary of what a user can change per mode
  - docs/blueprints/RESUME_Coordinator_Replay.md — the coordinator's earlier programme (replay harness), still open
  - docs/blueprints/RESUME_Coordinator_Gameplay.md — the coordinator's gameplay programme (Deploy, multiplayer)
-->

# RESUME — coordinator: the editing-UX programme *(2026-11-06)*

RELEARN — then read §1. Everything decided is in [DESIGN_Editing_UX.md](../UX/DESIGN_Editing_UX.md); this file only says where we are.

## 1. Where the programme stands

| step | state | where |
|---|---|---|
| ① inventory | ✅ done — Details tabs, standalone windows, node-gated tabs, units, AI, world, run / review, map interaction, context menus | design §1 (§1.1–§1.7) · CAPABILITIES §14 |
| ② prior art + target | ✅ done — Eden, DCS, VBS4, Unreal / Unity, ArcGIS / QGIS (finish conventions); layout wireframe `docs/UX/editing-ux-layout.svg` | design §2, §3 |
| ③ gap map + decisions | ✅ D2–D9 approved; D11–D16 approved as a FIRST iteration; D1 (undo), D10 (one edit framework), D17 (hats), D18 (mission plan editor) each need their own design | design §4, §4.3a, §4.3c |
| ④ top-level designs, one per slice | ⏳ **in progress — 6 of the queue written** — written HERE with the user (R-4123); lanes detail them while building; nothing built before the user reviews (R-4122) | design §4.5 (the queue) |

Rulings of this programme: R-4118 (Details is the one home) · R-4119 (gated by the entity's components, not the node) · R-4120 (forms
for the common; raw editor = debugging; undo in Edit only; the expected basics) · R-4121 (pickers filtered, condition templates, expert
detail hidden not removed, scarce live confirmations, editing levels per node) · R-4122 (a design per change, reviewed; one edit framework)
· R-4123 (top-level designs with the user; nothing only in the chat).

## 2. Next — in order

1. ~~Point ⑩~~ — dropped by the user.
2. ✍ **Design 1 WRITTEN `2026-11-06`: [DESIGN_Action_Catalogue.md](../UX/DESIGN_Action_Catalogue.md)** — ✅ Q1–Q5 APPROVED (R-4125). (Was: write design 1 of the queue (§4.5): the action catalogue + the tab / action `Requires` + mode model (E9 + E1 core).) Start from
   AQ26, `UX_Feature_Entity_Action_Vocabulary.md`, `UX_Feature_Cross_Surface_Actions.md`, `UX_Feature_Menu_Follows_Focus.md`,
   `designs/gizmos-1/canvas-context-menu-design.md`, `DESIGN_Details_Panel_View_Switching.md` — extend them, ⛔ never a sixth menu mechanism.
3. ✍ **Design 2 WRITTEN `2026-11-06`: [DESIGN_Edit_Framework.md](../UX/DESIGN_Edit_Framework.md)** (E0) — ✅ Q1–Q6 APPROVED as revised (R-4128); stages F1–F7; five defects filed on the way (CE-4274…4278); ✅ the old ack merged into EntityEditOutcome (R-4126, §2.4).
4. ✍ **Design 3 WRITTEN `2026-11-06`: [DESIGN_Mission_Plan_Editor.md](../UX/DESIGN_Mission_Plan_Editor.md)** (E11) — ✅ approved, Q3 (expert conditions, rule editor one click away) and Q4 (phases on events) revised (R-4129).
5. ✍ **Design 3b WRITTEN `2026-11-06`: [DESIGN_Rule_Editor_UX.md](../UX/DESIGN_Rule_Editor_UX.md)** (E13) — ✅ approved, Q3 revised: the condition editor refactored (R-4130).
6. ✍ **Design 4 WRITTEN `2026-11-06`: [DESIGN_Seats_And_Hats.md](../UX/DESIGN_Seats_And_Hats.md)** (E10 + D4) — ✅ APPROVED (R-4131), with §2.5 (HTTP seats, the camera, the director sharing it); stages H1–H7; CE-4279 (a Player can place units and command any side) and CE-4280 filed.
7. ✍ **Design 4b WRITTEN `2026-11-06`: [DESIGN_Input_And_Commands.md](../UX/DESIGN_Input_And_Commands.md)** (E14) — ✅ APPROVED as revised (R-4132: the map is a dumb terminal, map keys resolved on the backend; hard-coded defaults complete); CE-4281, CE-4282 filed.
8. ✍ **Design 5 WRITTEN `2026-11-06`: [DESIGN_Edit_Mode_Undo.md](../UX/DESIGN_Edit_Mode_Undo.md)** (D1) — ✅ Q1–Q7 APPROVED (R-4133); §1.1 what a snapshot misses, §2.3 the restore = the dry run's sequence.
9. ✍ **Design 6 WRITTEN `2026-11-06`: [DESIGN_Map_Interaction.md](../UX/DESIGN_Map_Interaction.md)** (E8 — D11 / D12 / D16) — one drawing core (`ShapeEditSession`), the armed tool and a pending pick visible on every terminal, airborne aircraft by `Mobility` (CE-4272 measured first), 2-D level choice from `SurfacesAt`, group rotate, the gizmo-look review; stages P1–P7; ✅ Q1–Q8 APPROVED (R-4134).
10. ✍ **Design 7 WRITTEN `2026-11-06`: [DESIGN_New_Scenario_And_Recipe.md](../UX/DESIGN_New_Scenario_And_Recipe.md)** (E12 / D19) — New Scenario gains *Blank, on a terrain* (`TerrainCatalog.List`, a header-only file then the ordinary load); recipes stay scenarios saved with Save as Recipe, updating a recipe = a plain Save As (the one Save-As dialog offers Replace for every kind); terrain thumbnails in git; `POST /scenario/new`; stages N1–N6; ✅ APPROVED (R-4135, Q4 revised, Q5 yes).
11. ✍ **Design 8 WRITTEN `2026-11-06`: [DESIGN_Details_Forms.md](../UX/DESIGN_Details_Forms.md)** (E2 / D3, D7) — forms as declarations (`FormDecl` rows naming an attribute) drawn by ONE `FormView`; every field is an attribute (backend adds Health, ammo, StanceIntent, Playable, SpawnArea, weather); revised after the user's review: the raw editor is local BY DESIGN (CE-4283 refuted); every open point measured (§1.2): ammunition = the mount's WeaponState, posture IS a Unit form row (Q7 revised by the user: StanceIntent saved, seeded at load, set at once while paused), weather attributes are the designed path, embark by command, routes lost on a non-owner (CE-4284), a StructEdit session allocates ~24 B per field per frame (CE-4285) so a row edits through a session only while being edited; stages D1–D6; ✅ Q1–Q8 APPROVED (R-4136).
12. ✍ **§1.8 initial state recorded `2026-11-06`** (the user's topics: seats and carrying, carried weapons, weapon posture, named / oriented point, lying weapons and stones, other start states — each measured) ⇒ slice **E15**, queue 7b, after E2.
13. ✍ **Design 9 WRITTEN `2026-11-06`: [DESIGN_Initial_State.md](../UX/DESIGN_Initial_State.md)** (E15) — one start-state rule (saved · start in it · at once while paused · a request at run time · the brain may change it); carry mode + assistant saved; WeaponPosture saved; SeedTargetCommand on every brain node; a Point map graphic via PointLink's entity form; §3a the AI character (skill, bravery, obedience, nerve, resolve, ROE) — the model exists, the writes are local to the brain's node (R-1036) ⇒ Q8 make them attributes; stages S1–S6; ✅ Q1–Q9 APPROVED (R-4137, narrows R-1036).
14. ✍ **Forms visual design WRITTEN `2026-11-06`** — [DESIGN_Details_Forms.md](../UX/DESIGN_Details_Forms.md) §6 + `docs/UX/details-forms-visual.svg` (user: forms first); stage D0; ✅ APPROVED (R-4138) — marks are coloured FontAwesome glyphs (R-4139); §6.6 fidelity (a mockup is an acceptance spec; CLAUDE.md rule); generator `scripts/gen-details-forms-mockup.py`.
15. ✍ **E16 colourful icons WRITTEN `2026-11-06`: [DESIGN_Colour_Icons.md](../UX/DESIGN_Colour_Icons.md)** — REVISED by the user: FontAwesome glyphs stay, IN COLOUR by meaning (R-4139; Silk rejected — a 16 px bitmap at the 0.5–3× UI scale), ONE key → (glyph, colour) map generalising ModeGlyph; 12 files; colours approved per surface from a rendered strip (I1–I3); R-4140: Silk bitmaps and the time controls STAY; five monochrome surfaces (ORBAT relation switch, Details header buttons, AI document tabs, Blueprint attachment pills, mission task state) — ✅ Q-I1, Q-I2 APPROVED (R-4141).
16. ✍ **E5 condition editor WRITTEN `2026-11-06`: [DESIGN_Condition_Editor.md](../UX/DESIGN_Condition_Editor.md)** — rows WHO · IS/HAS · VALUE projected to/from SearchPredicateDto (Custom row for the rest), ONE sentence renderer, four language additions (None, InUnit, InTheFight, E11's PhaseBegins); mockup `condition-editor-visual.svg`; stages C1–C5; ✅ APPROVED (R-4142) — no row icon, icons inside their chips (user).
17. ✍ **Tabs:** CE-4286 (the rule editor's tabs are buttons → a real tab bar, one shared no-close-box tab) · Colour Icons §3.1: every content tab gets a coloured icon via that helper, neutral tab palette (Q-I4 for review).
18. ✍ **Graphical designs (R-4143)**: every UI design now has a house-style mockup generated over `scripts/mockup_kit.py` — map interaction, New Scenario, initial state, rule list + gallery, seats and hats, menus / shortcuts / undo, mission plan (redrawn); the forms and condition editor generators moved onto the kit. List and states: [DESIGN_Editing_UX.md](../UX/DESIGN_Editing_UX.md) §4.6; the seven new ones await the user's review.
19. ✍ **E17 UI conventions WRITTEN `2026-11-06`: [DESIGN_UI_Conventions.md](../UX/DESIGN_UI_Conventions.md)** (R-4144, CE-4287) — trees CHEVRON · ICON · LABEL with ImGui's one arrow (`TreeRow`), label-first forms (`FormGrid`, the layout half of FormView), slate collapsing headers with a coloured icon (`SectionHeader`); all three in `NodeEditor.Core` (the one assembly the pickers and the panels both reach). Mockup `ui-conventions-visual.svg`. Q-C1/2/5 ✅ R-4145, Q-C6 capped width R-4145, Q-C4 withdrawn (node drawers are Details forms), Q-C3 ✅ R-4147 — E17 fully settled, ready for a ui handoff; fidelity checked by the builder, no stop (R-4146 — also in CLAUDE.md).
20. ✍ **Condition editor's Expert view — §8 of [DESIGN_Condition_Editor.md](../UX/DESIGN_Condition_Editor.md)** (CE-4288): measured from a real headless frame of today's rule When tab; fixes go into the ONE struct editor (names, kind-as-row, no empty Bind rows, numbers with units, collapsed sentence, add/trash) + names/glyphs from FactCatalogue + entity-link chip + When tab without the header. Mockup `condition-expert-visual.svg`. ✅ X1–X4 approved (R-4149); names: `[EditDisplayName]` (widened to types) → one humanizer; glyph `[EditIcon(key)]` (§8.4).
21. ✍ **Forms in the replay browser, read-only (R-4148)** — Details forms §2.4: the replay browser already composes the same EntityDetailsBundle (`RunState.Replay`), so by construction; editability one function; *not recorded* for an absent source. Mockup `replay-forms-visual.svg` — grey only, no banner / tooltip (R-4150); ⏳ Q-R1, Q-R3 (seek tint, copy menu); Q-R2 withdrawn (rows always show the current state). R-4151: a mockup specifies only its scope; the replay transport is context, unchanged.
22. ✍ **Replay Browser review — [DESIGN_Replay_Browser_UX.md](../UX/DESIGN_Replay_Browser_UX.md)** (CE-4289, R-4153): measured on a real recording (recipe: `--mode all` + `POST /scenario/load/live`, then `--mode replaybrowser` + `POST /replay {paths}` on Xvfb); main-bar seek bar, Replay Timeline window retired (parts routed), default layout, debug group; CE-4290 lanes drawn empty. Q-B1 (no ticks), Q-B2, Q-B5 ✅ R-4155; Q-B4 ✅ R-4156; Q-B3, Q-B6 open — parked (user: "we might need to return to the design later").
23. ✍ **Show on map — [DESIGN_Show_On_Map.md](../UX/DESIGN_Show_On_Map.md)** (CE-4291, R-4154): ⌖ by type, hover preview / click pin, one MapPreview over a local debug-primitive buffer. ✅ Q-M1…Q-M4 (R-4155).
24. 🚀 **DISPATCHED `2026-11-06` at `9c50cec00` (re-stamped before start; first `d98efb832`) — two self-coordinating lanes:** [ui handoff](batches/HANDOFF_Coordinator_To_Ui_Editing_UX_Build.md) (20 items, U-1…U-25) · [backend handoff](batches/HANDOFF_Coordinator_To_Backend_Editing_UX_Build.md) (18 items, B-1…B-18 — eight UI items moved there, user: roles not honoured, keep both busy) · [the protocol](batches/PROTOCOL_Two_Lanes_Self_Coordinating.md) (merge each other after every item, `STATUS_Editing_UX_Build_<Lane>.md`, `NOTE_<From>_To_<To>_*`). ⭐ Check their STATUS files and any `NOTE_*_To_Coordinator_*` when resuming.
25. ✅ **Checked `2026-11-07` (merged ui `e9c5f752` + backend `56943a90`):** ui U-1…U-16, U-18 (RX1–RX3), U-25 done; U-23 / U-24 parts; backend B-1…B-6, B-8, B-13, B-15 done; B-7, B-9, B-12, B-18 parts; B-10, B-11, B-14, B-16, B-17 not started. Answered backend's *Knows about* QUESTION (R-4157, notes to both lanes). Filed **CE-4292** (the Rules window composed three times; its editing services editor-only) — design first, ⏳ the user.
26. 🚀 **`2026-11-07` — [DESIGN_Rules_Bundle.md](../UX/DESIGN_Rules_Bundle.md) written (CE-4292, READY-TO-BUILD) and dispatched to ui** with B-10, B-11, B-14, B-16, B-17 taken over from backend: [handoff](batches/HANDOFF_Coordinator_To_Ui_Rules_Bundle_And_Takeover.md) · [note to backend](batches/NOTE_Coordinator_To_Backend_Items_Moved_To_Ui.md). Backend keeps B-9, B-12, B-18.
19. ▶ NEXT (queue §4.5): E5 condition templates · E7 review · E2b sim / types. (Was 8: D1 (undo — reads the edit framework's journal), then E8 (map interaction), then the smaller ones. Previously listed after hats:, D1 (undo — reads the edit framework's journal), E8 (map), then the smaller ones (queue §4.5).
4. Before each design: INVENTORY with graph queries, UML (class + sequence + module), and a claim table in the chat.

## 3. The other lanes at writing *(fetched `2026-11-06`)*

| lane | state | for the coordinator |
|---|---|---|
| **backend** | CE-4270 (a host is data; the fused server) **in progress** — head `cc37bffc` (B0–B6 hosts on the one HostSpawning step, S0 snapshot rail first; B7 plan). ✅ Stage A approved by the user in the backend lane (**R-3077**, D1–D5); CE-4269 audio and ③ the player cycle script done | no steer needed; ⏸ not merged (no report yet). ⭐ Overlap to watch: the role table (B7) and our editing levels / hats (D4, D17) — both decide what a node offers by role |
| **ui** | CE-4271 thumbnail atlas **done and MERGED** into coordinator (`b0d5509f`); mounted guns (CE-2317 / 1299 / 1300) reported done (`82aed6ec`), merged with it | reviewed: per-frame lookup allocates nothing after the first upload; open: CE-4273 (Car), CE-1464 |
| **ui** | the November bug batch, the director / thumbnails / deploy map handoff — dispatch shas are in ui | check their reports on the next lane check |
| **behaviors** | performance programme (§11f, 1000 soldiers load) | nothing dispatched |
| **backend** | late-joiner ghosts (CE-4000 / 4007) — dispatch in backend | check its report |

Ids: coordinator next free **CE-4294**, **R-4159** (CE-4293 / R-4158 taken by the gateway session: node extensions).

⚠ All lanes are PAUSED by the user while the editing-UX designs are discussed (`2026-11-06`).
