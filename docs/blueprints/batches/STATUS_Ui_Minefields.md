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
| MS-U2 `MinefieldGizmo` | ⏳ waits for terrain's contract NOTE (the `Minefield` component, `MinePattern`, `MinefieldKnowledge`) | — |
| MS-U3 authoring | ⏳ same | — |
| MS-U4 the clearer's kit | ✅ built as a stand-in (family not yet bound to terrain's class; flail-active read is a stand-in returning false) | `MineClearerModelRail` 6 rails (kit box / drum role / motion / 0 B / reader / real frame under `xvfb-run`, side · ¾ · rear · above, drum spinning vs still) + `AudioTests.CE4624_…FlailLoops…` — 15 passed / 0 failed / 0 skipped on the gate filter; red-proof: the drum's `CreateRotationY(motion.Flail)` zeroed ⇒ 2 rails fail (drum role, real frame). As-built: [DESIGN_Map_3D_Mode.md §6al.4](../../DESIGN_Map_3D_Mode.md). **Re-bind (terrain MS-T4 / type 116):** `FamilyOfClass["MineClearer"]`, `FlailActivity.IsOn` body, a flail clip if wanted |

## Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-1054 | the P-3 side-knowledge gate does not exist yet (graph: no `SideKnowledge`/`SideLimited`/`FogOfWar` class; P-3 is PLANNED). `MinefieldGizmo` draws the TRUTH unless the station's seat has a side AND a station setting `Map ▸ Side-limited map` (default OFF, KR-11) is on; the instructor (no side) always sees all | P-3 lands its own gate ⇒ the gizmo reads that one instead |
| R-1055 | the drawing is built over a small data seam (outline, parameters, lanes, mines, the viewer side's cells) that rails fill by hand; the projector that reads terrain's components is written at the re-bind — no stand-in COMPONENT, so no temporary component id (ui's block is exhausted) and no second `MinePattern` | terrain names the seam differently ⇒ the adapter follows it |
