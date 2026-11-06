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
| ④ top-level designs, one per slice | ⏳ **next** — written HERE with the user (R-4123); lanes detail them while building; nothing built before the user reviews (R-4122) | design §4.5 (the queue) |

Rulings of this programme: R-4118 (Details is the one home) · R-4119 (gated by the entity's components, not the node) · R-4120 (forms
for the common; raw editor = debugging; undo in Edit only; the expected basics) · R-4121 (pickers filtered, condition templates, expert
detail hidden not removed, scarce live confirmations, editing levels per node) · R-4122 (a design per change, reviewed; one edit framework)
· R-4123 (top-level designs with the user; nothing only in the chat).

## 2. Next — in order

1. ~~Point ⑩~~ — dropped by the user.
2. ✍ **Design 1 WRITTEN `2026-11-06`: [DESIGN_Action_Catalogue.md](../UX/DESIGN_Action_Catalogue.md)** — ✅ Q1–Q5 APPROVED (R-4125). (Was: write design 1 of the queue (§4.5): the action catalogue + the tab / action `Requires` + mode model (E9 + E1 core).) Start from
   AQ26, `UX_Feature_Entity_Action_Vocabulary.md`, `UX_Feature_Cross_Surface_Actions.md`, `UX_Feature_Menu_Follows_Focus.md`,
   `designs/gizmos-1/canvas-context-menu-design.md`, `DESIGN_Details_Panel_View_Switching.md` — extend them, ⛔ never a sixth menu mechanism.
3. ✍ **Design 2 WRITTEN `2026-11-06`: [DESIGN_Edit_Framework.md](../UX/DESIGN_Edit_Framework.md)** (E0) — awaiting the user's review of §5 Q1–Q6 and stages F1–F7; five defects filed on the way (CE-4274…4278); ✅ the old ack merged into EntityEditOutcome (R-4126, §2.4).
4. ▶ NEXT: E11 (mission plan editor), then E10 (hats), D1 (undo — reads the edit framework's journal), E8 (map), then the smaller ones (queue §4.5).
4. Before each design: INVENTORY with graph queries, UML (class + sequence + module), and a claim table in the chat.

## 3. The other lanes at writing *(fetched `2026-11-06`)*

| lane | state | for the coordinator |
|---|---|---|
| **backend** | CE-4270 (a host is data; the fused server) **in progress** — head `cc37bffc` (B0–B6 hosts on the one HostSpawning step, S0 snapshot rail first; B7 plan). ✅ Stage A approved by the user in the backend lane (**R-3077**, D1–D5); CE-4269 audio and ③ the player cycle script done | no steer needed; ⏸ not merged (no report yet). ⭐ Overlap to watch: the role table (B7) and our editing levels / hats (D4, D17) — both decide what a node offers by role |
| **ui** | CE-4271 thumbnail atlas **done and MERGED** into coordinator (`b0d5509f`); mounted guns (CE-2317 / 1299 / 1300) reported done (`82aed6ec`), merged with it | reviewed: per-frame lookup allocates nothing after the first upload; open: CE-4273 (Car), CE-1464 |
| **ui** | the November bug batch, the director / thumbnails / deploy map handoff — dispatch shas are in ui | check their reports on the next lane check |
| **behaviors** | performance programme (§11f, 1000 soldiers load) | nothing dispatched |
| **backend** | late-joiner ghosts (CE-4000 / 4007) — dispatch in backend | check its report |

Ids: coordinator next free **CE-4279**, **R-4128**.

⚠ All lanes are PAUSED by the user while the editing-UX designs are discussed (`2026-11-06`).
