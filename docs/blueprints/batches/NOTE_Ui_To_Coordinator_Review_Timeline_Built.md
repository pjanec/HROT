<!--STATUS
state: LIVE — ui → coordinator + backend: the review timeline's ui half (CE-3330 + the addendum), built; what waits on backend's data D
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §2d holds the as-built table, the class / sequence / module diagrams and the rails
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §9 / §10, the spec built against
  - NOTE_Backend_To_Ui_Review_Timeline.md, NOTE_Coordinator_To_Ui_Review_Timeline_Addendum.md — the two notes this answers
-->
# NOTE — ui → coordinator + backend: the review timeline, ui half built (CE-3330)

Built on `ui` against [backend's note](NOTE_Backend_To_Ui_Review_Timeline.md) (A, B where no data is needed) and
[the addendum](NOTE_Coordinator_To_Ui_Review_Timeline_Addendum.md). As-built, diagrams and rails:
[Director Timeline §2d](../../DESIGN_Director_Timeline_Editor.md).

| | state |
|---|---|
| **A** — T1–T12, §10b (follow, fit, presets, range, hover, snapping, keys, history, off-screen playhead, unavailable zones), §10c input map, zoom limits, ruler | ✅ built — pure `TimelineView` / `TimelineRuler` + `TimelinePanel`; rails `TimelineInteractionTests` (red-proved) |
| addendum 2 keymap · 3 no gesture seeks · 4 extensible menu · 5 the one selection | ✅ built (K / J / L wait: no director tracks on the panel yet, no rate in the cursor) |
| addendum 6 — live lanes = recorded lanes · row 28 `LiveTimelineSource` · row 30 `TimelineLaneContext.Live` | ✅ built — ONE per-frame step (`TimelineFrameScanner`) under the builder and the live source; a parity rail asserts a live index equals its recording's, lane by lane |
| R1-e — the editor shows the panel live | ✅ `editor_event_timeline` + `GET /timeline/*` on the editor; measured live (`ua-tank-ammo`): 265 frames / 15 s, the shot, hit and blast indexed; a pause adds nothing |
| ⭐ **found, fixed** | on a LIVE world the recorder clears the destruction log inside the tick, before any observer after the kernel reads it ⇒ the Left lane missed every death. `StructuralScanner` now also reports a tracked entity no longer alive (once). ⚠ **backend:** any other live reader of `GetDestructionLog()` after the kernel tick has the same blind spot |
| ⭐ **T13 — the Sim axis** (on P0/P1) | ✅ built — `TimelineAxis` projects the index, extent, now and seeks through `ReplayTimeMap`; a pause is one instant (◯ / ● on the ruler; a span on Wall); a seek to it lands on its last frame; an axis switch keeps the frames in view; the Replay Browser's S / W toggle |
| ⏳ **waits on D** (backend handoff P1+) | the session layer + live-point marker (row 23 / 29), the live menu's "Review from here …" and "Seek to the live point" (row 27 — the menu takes them as items once `EnterReview` / `Seek` exist on the facade), Loop range (addendum 8), the toolbar's mode indicator + S / W icon on live hosts (C, row 22), row 24 |
| ⏳ not yet (ui) | addendum 1 / 9 director tracks (R2 has none on the panel), live sources on CGF / SimHost / IG (the module diagram's dead edge) |
| ⚠ one frame | the live step runs after the kernel tick, so a live event is one frame (≈ 16 ms) later than the recording files it |

_ui lane, 2026-10-26_
