<!--STATUS
state: LIVE — coordinator → ui: three editor fixes from the user's testing (CE-4084, CE-4081, CE-4085), done FIRST while the run-modes tasks wait on backend's CE-4089
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Ui_Run_Modes.md — the run-modes batch already started (fea2a00f8); this is a SEPARATE batch (rule 1a: a started handoff is not amended)
  - ../../DESIGN_Map_3D_Mode.md — CE-4084: the map cameras and `OpenMapArea`
  - ../../DESIGN_Film_Director.md, ../../DESIGN_Director_Timeline_Editor.md — CE-4081: the take / reel and the cinema overlay's Esc
  - ../../UX/UX_Feature_Menu_Follows_Focus.md — CE-4085: the ONE main menu tree (CE-1243)
-->
# HANDOFF — coordinator → ui: the map's visible centre, Esc stops a take, main-menu order

**Dispatched at** the commit that adds this line (`coordinator`). ⭐ **Your scope is FROZEN at that sha** — documents that change after it are
FYI only; if one invalidates a task, STOP that task and report it. Merge `coordinator`, push an empty `chore: started editor-fixes at <sha>`.

⭐⭐ **Why now:** your run-modes batch ([`HANDOFF_Coordinator_To_Ui_Run_Modes.md`](HANDOFF_Coordinator_To_Ui_Run_Modes.md), started at
`fea2a00f8`) waits on backend's CE-4089 (the content move) before CE-4096 / CE-4098. These three depend on nothing — do them in that wait. When
CE-4089 lands, return to the run-modes batch (finish the task in hand first).

⭐⭐⭐ **Sonnet wherever possible** (user, `2026-10-28`, token thrift). The column below says which part needs your own model. For a Sonnet part:
brief it with the task's *done when* as numbered success conditions plus the files named here; Sonnet writes its own rails until they pass; you
review the tests and the diff (do they assert what matters? is the code OK?) and do NOT re-run them; verify any *"cannot / does not exist"* claim.

## The tasks, in this order

| # | task | ⭐ who | start from | done when |
|---|---|---|---|---|
| 1 | **[CE-4085](../Blueprint_Issues_Tracker.md)** main-menu order: Help right-aligned and last; Settings and Perspective move under View | **Sonnet** (all of it) | the ONE main-menu tree of CE-1243 (per-frame contributors) — find where Help / Settings / Perspective register; ⛔ no per-host reordering | on every host Help is the last item, drawn at the right edge; View holds Settings and Perspective; no other menu lost an item (a rail on the tree, not on one host) |
| 2 | **[CE-4081](../Blueprint_Issues_Tracker.md)** Esc does not stop a take played in the editor | **Sonnet** — first a measurement, then the fix | the cinema overlay already raises `EscapePressed` (`Hrot/Engine/Hrot.Presentation/Map3D/Director/DirectorAdapters.cs:87-88`); `RUNBOOK_Filming.md:65` says Esc stops a reel. Measure which of three it is: the take path does not draw that overlay · nothing subscribes to `EscapePressed` · the key never reaches it while the map holds input | Esc stops a take AND a reel and returns the editor to where it was before Play; a rail in the director's own suite (T-1), red-proved |
| 3 | **[CE-4084](../Blueprint_Issues_Tracker.md)** the 2-D/3-D map zooms and turns around the screen centre, not the centre of the area left visible between the docked panels | **your own model for the design step** (where the visible centre enters each camera — see below), then **Sonnet** for adopting it at each site and the rails | ⭐⭐ **the shared rectangle ALREADY EXISTS:** `OpenMapArea` (`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/OpenMapArea.cs`, CE-1140) — the dockspace's central node, reported each frame, today used only for HUD corners (`PossessionStation.cs:734`, `MapStatsOverlay.cs:85,99`). ⛔ Do not build a second "layout evaluator" — extend this one | wheel zoom and orbit centre on the middle of the visible area on 2-D and 3-D; the crosshair / aim pick uses the same point; with no dockspace (`Current == null`) nothing changes; rails per camera; the design updated (below) |

### ⭐ CE-4084 — what is measured, and what the design step must settle

| fact | where |
|---|---|
| `OpenMapArea.Current` is reported ONLY by the cluster runner's frame loop | `Hrot/Runner/Hrot.ClusterRunner/Program.cs:749` — ⚠ measure whether the editor host goes through that loop; `ExConMock.cs:203` and `Stride/…/StrideInspectorWindow.cs:556` lay a dockspace and do NOT report it |
| the 3-D camera's pivot is `LookAt`, which the projection puts at the SCREEN centre | `FDP/Engine/Fdp.Presentation/Vis3D/MapCamera3D.cs` (`LookAt`, `Distance`, `WorldToScreen` at :514 uses the full viewport) |
| 2-D → 3-D switch takes the 2-D view's centre as `vp / 2f` | `MapCamera3D.cs:169` |
| the aim / crosshair pick is at `camera.Viewport / 2f` | `Hrot/Engine/Hrot.Presentation/Map3D/PossessionStation.cs:1821` (via `FpsCameraDriver.Aim`) |
| the 2-D map's zoom anchor | `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs` — not yet measured |

**The design call (yours, before Sonnet):** for 3-D, either shift the projection (an off-centre frustum, so `LookAt` is drawn at the visible
centre and every existing pivot / orbit / fly maths stays untouched) or keep the projection and move the pivot. ⭐ Lean: **the off-centre
projection** — one place (the projection) instead of every input path, and picking / `WorldToScreen` follow from it. For 2-D, the
equivalent: the canvas's screen centre = the visible centre. Draw it as a `classDiagram` + `sequenceDiagram` in
[`DESIGN_Map_3D_Mode.md`](../../DESIGN_Map_3D_Mode.md) (where `OpenMapArea` already lives in the design), then brief Sonnet.

## Rules that bind every task

- **One implementation per concept** — `OpenMapArea` is THE visible-area answer; the menu has ONE tree; Esc stops through the director's one
  path, not a second key handler.
- **Fold the as-built back into the owning design** before the task closes (obligation ⑤).
- **Tests:** the feature's own suite first (T-1); each new rail red-proved by a compiling mutation; build the TEST project before `--no-build`.
- **Report per task** with the gate report contract (CLAUDE.md, Rule 8) and the tracker ids you closed. Push after each task.

_coordinator lane, 2026-10-28_
