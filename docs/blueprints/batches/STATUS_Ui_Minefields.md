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
| MS-U2 `MinefieldGizmo` | ✅ built against terrain's REAL contract (`25a3f2cf6`); the cells light up when MS-T2's table is real | `MinefieldGizmoRail` 4/0 (truth · known-in-full vs knows-nothing vs gate off · cache + 0 B · the rotated cell); red-proved (a cleared mine left drawn ⇒ red); DESIGN_Minefields §11 · ⭐ FRAME EVIDENCE (R-4146, `2026-11-16`): `MinefieldFrameRail` (2, real frames under `xvfb-run -a`, none skipped) — ⓐ the truth ⓑ a Blue seat, gate on, known at start ⓒ a Blue seat that knows only cells (real `MinefieldKnowledge` records through `PlaceKnowledgeTable`, keyed by `ForceId` — the gizmo already passes the right side) ⓓ the 3-D pickets; deviations fixed: outline SOLID in 2-D (dashed only in 3-D), ⊘ edge symbols in the truth view too (2-D only, `FlatOnly`), larger mine marks, 3-D pickets 2.2 m with a thick red sign, the 2-D fill kept out of 3-D. Not matched (ImGui/gizmo limits): the hatch fill (a light tint instead), the label's box + glyph (plain text), the knowledge grid's cell lines, round-ish cell blob (square patch) |
| MS-U3 authoring | ✅ built — `MinefieldOps`, the area tool's minefield arm (no overlay, a default `Minefield`), `MinefieldDetailsView`, Add lane (the area tool in LINE mode), the shape lock via `OutlineEditPolicy` | `MinefieldOpsTests` 7 + `MinefieldDetailsViewTests` 3; as built: [DESIGN_Minefields.md §11](../../designs/minefields/DESIGN_Minefields.md). ⚠ no TKB template for 8830 yet (terrain MS-T1) — placing one in the editor needs it |
| MS-U4 the clearer's kit | ✅ built as a stand-in (family not yet bound to terrain's class; flail-active read is a stand-in returning false) | `MineClearerModelRail` 6 rails (kit box / drum role / motion / 0 B / reader / real frame under `xvfb-run`, side · ¾ · rear · above, drum spinning vs still) + `AudioTests.CE4624_…FlailLoops…` — 15 passed / 0 failed / 0 skipped on the gate filter; red-proof: the drum's `CreateRotationY(motion.Flail)` zeroed ⇒ 2 rails fail (drum role, real frame). As-built: [DESIGN_Map_3D_Mode.md §6al.4](../../DESIGN_Map_3D_Mode.md). **Re-bind (terrain MS-T4 / type 116):** `FamilyOfClass["MineClearer"]`, `FlailActivity.IsOn` body, a flail clip if wanted |

## Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-1054 | the P-3 side-knowledge gate does not exist yet (graph: no `SideKnowledge`/`SideLimited`/`FogOfWar` class; P-3 is PLANNED). `MinefieldGizmo` draws the TRUTH unless the station's seat has a side AND a station setting `Map ▸ Side-limited map` (default OFF, KR-11) is on; the instructor (no side) always sees all | P-3 lands its own gate ⇒ the gizmo reads that one instead |
| R-1055 | the drawing is built over a small data seam (outline, parameters, lanes, mines, the viewer side's cells) that rails fill by hand; the projector that reads terrain's components is written at the re-bind — no stand-in COMPONENT, so no temporary component id (ui's block is exhausted) and no second `MinePattern` | terrain names the seam differently ⇒ the adapter follows it |

## Gates (rule 8) — merged tree `82bb69ece` (terrain MS-T2/T3 + backend merged)

| command | result |
|---|---|
| `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests/... --no-build --filter "Minefield\|MineClearer\|AnimalModelRail\|CE4271\|EntityTypeCatalog\|ShapeKit\|CE2138\|VertexEdit"` | 104 / 0 / 0, twice (after the per-world cache fix) |
| `xvfb-run -a dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/... --no-build --filter "MinefieldDetailsView\|AnimalAreaDetailsView"` | 5 / 0 / 0 |
| pre-existing reds, proven at `411de4f7e` in a clean worktree | `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…`, `TheGalleryOpenedOnClutter…` — filed **CE-1955** |

Findings: `CE4271_TheShippedAtlas` was OUR stale count (the 11 animal thumbnails, `80cc7fdda`) — fixed. `MinefieldOps` cached per entity in statics (raced across worlds) — fixed `82bb69ece`.

Ids allocated: R-1054, R-1055 · CE-1955.

## Waiting on terrain
MS-T1 munition rows 6051–6053 (the burst looks, VE-D) · MS-T4 the clearer's TKB 116 + class `MineClearer` (`FamilyOfClass`, thumbnail, `FlailActivity.IsOn`) · TKB template 8830 (placing a field in the editor).
