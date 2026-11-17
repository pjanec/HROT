<!--STATUS
state: LIVE — ui minefields (CE-4624 MS-U1…MS-U4), overnight
updated: 2026-11-16
current-answer: the slice table
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — THE design (§3 MF, §3a ids, §6.1 contract, §8 / §10 reviews)
  - HANDOFF_Coordinator_To_Ui_Minefields.md — the dispatch (8b02d1a2a)
-->

# STATUS — ui: minefields visuals + authoring (CE-4624)

Started `411de4f7e` (merged `coordinator@66d47feba`).

| slice | state | evidence |
|---|---|---|
| MS-U1 mockup | ✅ built | `scripts/gen-minefields-mockup.py` → `docs/designs/minefields/minefields-visuals.svg` — the mines are MF-2's derived pattern, not hand-placed |
| MS-U2 `MinefieldGizmo` | ✅ built against terrain's REAL contract (`25a3f2cf6`); the cells light up when MS-T2's table is real | `MinefieldGizmoRail` 4/0 (truth · known-in-full vs knows-nothing vs gate off · cache + 0 B · the rotated cell); red-proved (a cleared mine left drawn ⇒ red); DESIGN_Minefields §12 · ⭐ FRAME EVIDENCE (R-4146, `2026-11-16`): `MinefieldFrameRail` (2, real frames under `xvfb-run -a`, none skipped) — ⓐ the truth ⓑ a Blue seat, gate on, known at start ⓒ a Blue seat that knows only cells (real `MinefieldKnowledge` records through `PlaceKnowledgeTable`, keyed by `ForceId` — the gizmo already passes the right side) ⓓ the 3-D pickets; deviations fixed: outline SOLID in 2-D (dashed only in 3-D), ⊘ edge symbols in the truth view too (2-D only, `FlatOnly`), larger mine marks, 3-D pickets 2.2 m with a thick red sign, the 2-D fill kept out of 3-D. Not matched (ImGui/gizmo limits): the hatch fill (a light tint instead), the label's box + glyph (plain text), the knowledge grid's cell lines, round-ish cell blob (square patch) |
| MS-U3 authoring | ✅ built — `MinefieldOps`, the area tool's minefield arm (no overlay, a default `Minefield`), `MinefieldDetailsView`, Add lane (the area tool in LINE mode), the shape lock via `OutlineEditPolicy` | `MinefieldOpsTests` 7 + `MinefieldDetailsViewTests` 3; as built: [DESIGN_Minefields.md §12](../../designs/minefields/DESIGN_Minefields.md). ⚠ no TKB template for 8830 yet (terrain MS-T1) — placing one in the editor needs it |
| MS-U4 the clearer's kit | ✅ BOUND (`2026-11-16`) to terrain's TKB 116 / class `MineClearer`: `FamilyOfClass["MineClearer"]`, the real type through the real catalogue (9.0 × 4.4 × 3.2 m box, the TKB's dull olive), the picker thumbnail (atlas 91), `FlailActivity` on `MineClearing` | `MineClearerModelRail` 7 (incl. `TheRealType116_…`, the frame rail draws type 116) + `AudioTests.CE4624_…`; `EntityTypeCatalogTests` goldens (116 line, 8830's folder) + atlas 91. As built: [DESIGN_Map_3D_Mode.md §6al.4](../../DESIGN_Map_3D_Mode.md) |

## Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-1054 ✅ APPROVED (R-4278, `2026-11-16`) | the P-3 side-knowledge gate does not exist yet (graph: no `SideKnowledge`/`SideLimited`/`FogOfWar` class; P-3 is PLANNED). `MinefieldGizmo` draws the TRUTH unless the station's seat has a side AND a station setting `Map ▸ Side-limited map` (default OFF, KR-11) is on; the instructor (no side) always sees all | P-3 lands its own gate ⇒ the gizmo reads that one instead |
| R-1055 ✅ APPROVED (R-4278, `2026-11-16`) | the drawing is built over a small data seam (outline, parameters, lanes, mines, the viewer side's cells) that rails fill by hand; the projector that reads terrain's components is written at the re-bind — no stand-in COMPONENT, so no temporary component id (ui's block is exhausted) and no second `MinePattern` | terrain names the seam differently ⇒ the adapter follows it |

## Gates (rule 8) — merged tree `82bb69ece` (terrain MS-T2/T3 + backend merged)

| command | result |
|---|---|
| `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests/... --no-build --filter "Minefield\|MineClearer\|AnimalModelRail\|CE4271\|EntityTypeCatalog\|ShapeKit\|CE2138\|VertexEdit"` | 104 / 0 / 0, twice (after the per-world cache fix) |
| `xvfb-run -a dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/... --no-build --filter "MinefieldDetailsView\|AnimalAreaDetailsView"` | 5 / 0 / 0 |
| pre-existing reds, proven at `411de4f7e` in a clean worktree | `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…`, `TheGalleryOpenedOnClutter…` — filed **CE-1955** |

Findings: `CE4271_TheShippedAtlas` was OUR stale count (the 11 animal thumbnails, `80cc7fdda`) — fixed. `MinefieldOps` cached per entity in statics (raced across worlds) — fixed `82bb69ece`.

Ids allocated: R-1054, R-1055 · CE-1955.

## Waiting on terrain
~~MS-T1 munitions, TKB 8830~~ landed and bound (bursts: `EffectTkbCatalog`; the field placeable) · ~~MS-T4 `MineClearing`~~ bound (`FlailActivity`) · ~~runtime lanes~~ drawn · ~~MS-T4's TKB 116 template + class `MineClearer`~~ landed and bound (`FamilyOfClass`, the thumbnail).

## Morning summary — `2026-11-16`

| slice | state |
|---|---|
| MS-U1 mockup | ✅ |
| MS-U2 gizmo | ✅ bound to terrain's real contract (knowledge table, strikes, runtime lanes); frame rails a–d; LIVE in the editor (placed, edited, struck — `NOTE_Ui_To_Terrain_Minefield_Live.md`) |
| MS-U3 authoring | ✅ picker entry, area-tool birth (no overlay style), the form, Add / Delete lane (line mode), the struck-field shape lock |
| MS-U4 clearer | ✅ kit, flail drum + loop bound to `MineClearing`, mine bursts; ✅ bound to terrain's TKB 116 / class (`FamilyOfClass`, thumbnail, goldens) |
| CE-4623 animals | ✅ the area tool re-bound to backend's `AnimalArea` (stand-in id 511 deleted) |

Follow-ups: the picker files the field under "Cultural Feature/Category 91" (the DIS name table has no name for 5.0.*.91 — golden accepted as is); the HTTP API cannot drive the area tool (the live run used the spawn route).
