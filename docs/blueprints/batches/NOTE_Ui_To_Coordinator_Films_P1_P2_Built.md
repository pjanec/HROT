<!--STATUS
state: LIVE — ui → coordinator: films from recordings — P1 (one director) and P2 (adjust in the RB) built; P3 position track built; the live proof pending
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11j P1 · §11k CR-6 · §11l P3 position track · §11m P2a · §11o P2b (each with its as-built)
  - NOTE_Coordinator_To_Ui_Films_From_Recordings.md — the frame this answers
  - NOTE_Ui_To_Backend_Films_P2a_And_The_T3_Flake.md — sent to backend the same day
-->
# NOTE — ui → coordinator: films P1 and P2 are built

**Dispatched at** the commit that adds this file (`ui`). Answers [NOTE_Coordinator_To_Ui_Films_From_Recordings](NOTE_Coordinator_To_Ui_Films_From_Recordings.md).

| phase / item | where | as built (one line) |
|---|---|---|
| **P1** one director (CE-1303) | §11j | runner, take session, Director window, the four adapters (now `WindowCinema`, `MapLayerOverride`, `AudioDirectorSound`, `FilmFrameSink`) → `Hrot.Presentation`; `DirectorPack.Attach(DirectorPackHost)` is the one registrar; the editor contributes its host, your `ScenarioVerifyRun` and a `VerifyTab` (`IDirectorWindowTab`). Your `CE4061_*` rail green across the move |
| **CR-6** keys tied to events | §11k | K stores `shot:Unit#n` / `blast#n` / `hit:Unit` + offset as an ordinary `AnchorSpec` + bake — no new `AtExpr` form, `AnchorBook` unchanged |
| **P2a** the director in the RB (CR-3, CR-4) | §11m | `ReplayDirectorHost` (seek = `SetBaseWallTicks`, no history) · the in-memory take plays (no save) · `PlayFrom(t)` · Loop shot · takes beside the recording |
| **P2b** CR-7, CR-11, update key | §11o | ONE `FilmTimeMap` (segments end to end: rate, gap = seek/cut/dip on the RB, fast-forward live, a back segment = instant replay on the RB, skipped live) · a Subtitles track (timed in SOURCE time, captions read as subtitles) · *update key from view* (U) · pilot ⇄ program · takes in a kept exercise |
| **P3** position track (CE-4059, step 1) | §11l | 2 Hz per networked unit in the ONE per-frame step; `PositionAt` / `PositionsOf` / `UnitsWithin`; sidecar v3. 📐 1000 units: +0.03 ms / frame, 144 MB / h. ⭐ The Drafter can read it |

**Gates (rule 8), final P2b tree:** `Hrot.ReplayBrowser.Tests` 53 / 0 / 0 · `Hrot.Presentation.Tests` 1049 / 2 / 2 (the two reds,
`Map3DFrameRail.CE1227_V_DemoCity_*` and `CE3174_V_TheFpsStation_*`, are red at base) · `Hrot.Editor.Tests` (serial) 603 / 0 / 2 ·
`DirectorRunnerTests` incl. `CE4061_*`, `CE1364_T4_*` green.

⏭ **Not yet:** the P2 live proof (a before / after screenshot pair from the RB over a real kept recording) — ui runs it next; P3's routes
(`/director/*` with `recording=`, stills, MP4, MCP) and the Drafter's home (CR-8, your shot rules). New ui rows: CE-1369 (a replayed stretch is
silent in a rendered film), CE-1370 (a burned-in subtitle ignores position / fade), CE-1371 (takes saved before a keep stay in temp).

_ui lane, 2026-10-27_
