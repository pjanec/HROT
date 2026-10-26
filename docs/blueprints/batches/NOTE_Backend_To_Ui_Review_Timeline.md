<!--STATUS
state: LIVE — backend → ui: EVERYTHING the timeline (and the time-control toolbar) must do for replay and During Action Review — CE-3330 + the design's P4 rows
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS the spec (§9 the UI, §10 the timeline review, §8 rows 21–30, 33); READY-TO-BUILD
  - ../../DESIGN_Director_Timeline_Editor.md — ui's; OWNS the panel; fold §9 / §10 into its §2b as you build
  - NOTE_Backend_To_Ui_Timeline_Zoom_Pan.md — the first note (the defects only); this one SUPERSEDES it as the complete list
  - HANDOFF_Backend_Replay_Review_Build.md — backend's build of the same design (the data this UI reads)
-->
# NOTE — backend → ui: the review timeline, complete (CE-3330 + P4)

The first note ([NOTE_Backend_To_Ui_Timeline_Zoom_Pan](NOTE_Backend_To_Ui_Timeline_Zoom_Pan.md)) named the 13 defects only. This one is
the COMPLETE list of what the timeline and the toolbar need for replay and During Action Review (DAR). Spec:
[DESIGN_Replay_Time_Axis_And_Review](../../DESIGN_Replay_Time_Axis_And_Review.md), user-approved R-3057…R-3063.

⏳ **Who builds what is the user's call.** Lean: **ui builds A–C** (your panel and toolbar); **backend builds the data D** (its handoff, P1)
and the facade contract (row 21). Until D lands, A runs against today's wall-tick sources.

## A. The panel's zoom and pan — CE-3330 *(design §10; T1, T2, T12 first)*

| | |
|---|---|
| **defects** (§10a) | T1 view reset on every extent change · T2 a left click seeks · T3 left drag pans · T4 zoom only Ctrl+wheel over a row · T5 wheel magnitude ignored · T6 no horizontal wheel · T7 closest zoom 0.1 s · T8 ruler loses hours, labels overlap, no minor ticks · T9 overview edges not draggable · T10 `_dragging` stuck · T11 ruler not sticky · T12 playhead leaves the view · T13 wall ticks only |
| **gaps** (§10b) | follow modes (live tail + `⇥ Live` chip; page during play) · fit / presets (F, 0–3, double-click ruler) · range selection (Shift+drag) → zoom / loop / export / review from · hover readout · snapping to events and frames · keyboard (panel focused only) · zoom history · ⭐ **off-screen playhead arrow** · ⭐ **unavailable zones shaded** (before a node's first frame, after the live point) · ⭐ **break marks at collapsed pauses on the Sim axis** · ⭐ **an axis switch keeps the same FRAMES in view** · ⭐ **view state is per viewer** (only the axis is the session's) |
| **spec** (§10c) | the input mapping table · zoom limits (≈10 frames closest; `1.0015^(−delta×120)`) · the ruler (1·2·5 steps, measured labels, `h:mm:ss` past an hour, `+frame` at the closest zoom, "−5:27" in a review) |

## B. What the panel SHOWS and OFFERS in replay and review *(design §9b, §9e, §9g, R10)*

| § / row | what |
|---|---|
| §9b, row 27 | `TimelineContextMenu` — the RIGHT-CLICK seek menu, items per mode (live: "Review from here", "Review the last 30 s / 2 min"; review / replay: "Seek here", "Seek to this event", "Loop range"…). ⛔ a left click only selects (R-3059) |
| R10, row 23 | positions on the ACTIVE axis (S / W); paused stretches shaded (W) or ◯ / ● ticks (S); checkpoint marks; the live-point marker in a review; the playhead handle drags (scrub, seek on release) in review / replay only |
| §9e, row 29 | the SESSION layer (ruler, stretches, checkpoints, live point, playhead) from the cluster status + `ReplaySessionTopic` — identical on every host, the ONLY layer on ExCon / Orchestrator; per-host lanes beneath it |
| row 28 | `LiveTimelineSource` (TE-17) — the lane catalogue evaluated on the live world each frame, appended; frozen at the live point during a review |
| row 30 | `TimelineLaneContext` `+ Live` beside `Recording` |
| §9g | after a replay opens, the session layer shows at once; the per-host lanes FILL IN as the background scan proceeds (a progress hint, not an empty panel) |
| row 24 | `ClusterScenarioPanel` slider + `ClusterUiCache` on the active axis, reading `ReplaySessionTopic` |
| row 26 | amend your `DESIGN_Director_Timeline_Editor.md`: TL-4 gains the sim axis; TE-19 "a live host never seeks" → "the right-click menu enters a review" (R-3059) |

## C. The time-control toolbar *(design §9a, R-3060, R-3061, row 22)*

The EXISTING section, two small glyphs only: the **mode indicator** before the three buttons — ✎ Edit · ◐ Preview · ● Live · ⟲ Review · ▶
Replay, each its own colour; ⛔ a click never acts, it opens a menu only where there is something to offer · the **S / W axis icon** after the
rate · ■ shows **⏏ "Back to live"** in a review (and asks "Stop the exercise?" for 3 s after a return — F15) · the readout on the active axis.
The Replay Browser keeps its own controls + the S / W icon only (row 34 — backend's, P1).

## D. The data this UI reads — backend builds it *(its handoff, P1)*

`ReplaySessionTopic` (mode, review id, axis, time map, live point, checkpoints) · `ReplayTimeMap` · `SetReplayAxisIntent` and
`SeekReplayIntent.SimSeconds` from any host · `ITimeTransportFacade` `+ Mode`, `Axis`, `SetAxis`, `EnterReview(t?)`, `ExitReview()`,
`LivePoint`, `Seek(t)` (row 21) · the `.timeline` sidecar written at `FinalizeLive` (row 33).

_backend lane, 2026-10-26_
