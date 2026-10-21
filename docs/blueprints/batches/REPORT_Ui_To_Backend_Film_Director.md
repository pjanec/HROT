<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole report
related-designs:
  - ../../DESIGN_Film_Director.md — THE DESIGN; §8 is the as-built this report summarises
  - HANDOFF_Backend_To_Ui_Film_Director.md — the dispatch this answers (scope frozen at 687208f56)
-->

# REPORT — ui → backend: the film director, slice 1 (CE-4038)

**Built on `ui`** from `36c225111` (merged `backend@36c225111`, which carries the dispatch `687208f56`); started marker `a3ee50773`.
⭐ Design obligation ③: the design had **19 classes and 4 sequences**; what was built matches §1 / §1a and deviates from §2 in eight
places, each folded into [DESIGN_Film_Director.md §8.3](../../DESIGN_Film_Director.md) and marked SUPERSEDED in its STATUS block.

## The items

| # | built | where |
|---|---|---|
| B0′ | ✅ the two-run identity rail — REPORT mode (a gate behind `HROT_DIRECTOR_IDENTITY_GATE=1` after CE-3293 S5, `CE-1240`) | `Hrot.SystemTests/DirectorIdentityRails.cs`; every cut's world is in each take's report (`cuts[]`) |
| B1 | ✅ `Take` / `Reel` / `TakeStore` / `TakeJson`; unknown names refused WITH the name (and near names); Save As copies `takes/` | `Hrot.Presentation/Map3D/Director/`, `EditorScenarioSession.SaveAs` |
| B2 | ✅ `ShotEvaluator` — film.py's `_shot_for` ported; film.py keeps no copy (`/film/shot {spec, t}`, `--take`) | `ShotEvaluator.cs`, `scripts/film.py` |
| B3 | ✅ `PacedClock` + `DirectorRunner`: one step per rendered frame (asserted), held to real time | `Hrot.Editor/Director/DirectorRunner.cs` |
| B4 | ✅ `ScenarioReadyProbe` extracted; the load route uses it | `Director/ScenarioReadyProbe.cs`, `DebugApiHost.HandleScenarioLoad` |
| B5 | ✅ `/director/takes · reels · take/{s}/{t} (GET, PUT) · play · scout · stop · next · pause · resume · status`, each documented | `DebugApiHost.Director.cs`, `DebugApiRouteDocs.cs` |
| B6 | ✅ View › Director | `Director/DirectorWindow.cs` |
| B7 | ✅ `AnchorBook` (component state + the combat logs, ⛔ no `Bus.Read`) + the scout mode that bakes (rate, scenario hash) | `AnchorBook.cs`, `DirectorRunner.Bake` |
| B8 | ✅ capture: a frame per film frame, cards, captions, one movie per reel with chapters and sound; no retakes | `DirectorRunner.Capture/StartEncoding`, `FilmCaptureRequest.Caption/Card` |
| B9 | ✅ cinema mode (`WindowManager.Cinema`) + the card/caption overlay; Esc stops | `EditorCinema` |
| B10 | ✅ a take's layers: a transient override, never written; `/map/layers` = the user's click | `LayerControlGizmo.SetOverride/SetRows` |
| B11 | ✅ the 2-D ear, `/audio`, a take's `sound`, the film's sound track (logged, rendered offline) | `AudioLayer.Ear2D`, `SoundTrack.cs` |
| B12 | ✅ 55 takes seeded from `clips.json`; `films/features.reel.json` reads (it was already there) | `scenarios/*/takes/main.take.json` |

## Gates

### Gate 5 — the measurements (cloud VM: Xvfb + llvmpipe, a 2200×1200 window; `scripts/director-check.py live director-check --tick-hz 60 20 15`)

| reel `director-check` (3 scenarios, 64 s of sim) | 60 Hz | 20 Hz | 15 Hz |
|---|---|---|---|
| status | ok · ok · ok | ok · ok · ok | ok · ok · ok |
| realtime factor (bt-doors · ua-civ-gathering · ua-fast-rope-roof) | ×0.41 · ×0.27 · ×0.28 | ×1.00 · ×0.75 · ×0.72 | ×1.00 · ×0.97 · ×0.86 |
| ⭐ steps asked in one frame (max) | 1 | 1 | 1 |
| ⭐ steps the KERNEL took in one frame (max, from the clock) | 1 | 1 | 1 |
| frames waiting on the master | 0 | 0 | 0 |
| frame time, panels on → cinema | (boot-skewed: 454 → 40 ms) | 65 → 46 ms (×1.42) | 67 → 50 ms (×1.34) |

⇒ ⭐ **two steps per frame: NO** — the master takes a step ONE FRAME after it is asked (measured on the first run: the first guard
stepped every other frame; fixed in `0adef0cb6`, design §8.2). ⭐ **`tickHz` on this VM: 15** (20 where a scene is light); a desktop at
60 fps keeps 60 Hz — ⚠ not measured here (no desktop). B9's gain: **~1.4× with the panels off** on the VM, where the renderer, not the
panels, is the cost — ⚠ D-7's "~0.8 s per frame, mostly the panels" was a capture-mode figure. R-106: B3's "within 5 % on a desktop"
is not measurable from the cloud — reported, not claimed.

GATES-PLACEHOLDER

## Ids allocated (ui block)

`CE-1239` (an OGG clip is missing from a film's sound track) · `CE-1240` (re-scout the takes and turn B0′ into a gate after CE-3293
S5). No ruling ids. Design sections updated to the as-built: Film Director §8 (new) + its STATUS · Map 3D Mode §6ab-i · FPS Audio §8.2 ·
Terrain Combat Tuning §5e.2 · RUNBOOK_Filming §0a · PLAN_Feature_Films §3.
