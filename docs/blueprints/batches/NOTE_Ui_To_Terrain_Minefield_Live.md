<!--STATUS
state: LIVE — findings for terrain, from ui's live run
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — §12 ui as built
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract this run exercised
-->

# NOTE — ui → terrain: the minefield in a LIVE editor (CE-4624)

The editor driven over HTTP (`tests/test-move`, flat Berlin, a Bradley driving east): a field placed with the area tool's components (`POST /entities/spawn`,
TKB 8830, `Minefield` + `EditablePolyline`), edited (`/component` patch), drawn in 2-D and 3-D, and the Bradley sent through it. ⭐ **Your trigger fires**:
at ~12.5 s a strike — a `MinefieldKnowledge` record (Blue, source Strike) and a strikes record (Count 2) — and the gizmo stars both struck mines.

| # | finding (yours to judge) | evidence |
|---|---|---|
| T-1 | ⚠ the Bradley's Health stayed **100 / 100** after two strikes and it finished its move — the AT mine's damage does not seem to reach the hull (the BOTTOM facing, MR-10)? | `GET /entities/{bradley}` after the strike |
| T-2 | the strike's burst effect entity (TKB 7201) sat at **z ≈ 1500** on this flat "no terrain" scenario — invisible; the burst point's ground lookup looks like a sentinel height | the effect entity's `SimTransform` |
| T-3 | a bare `POST /entities/spawn` of 8830 with no components makes a husk (no `Minefield`, no outline). Only HTTP can do that (the picker goes through the area tool, which adds both) — a birth default on your side would close it | — |

ui fixed on its side in the same run: the field drew the generic unit box in 2-D and the unknown purple body in 3-D (`NotAUnit`, `EntityBodyLayer3D.LookOf`, `abded9ea9`).
