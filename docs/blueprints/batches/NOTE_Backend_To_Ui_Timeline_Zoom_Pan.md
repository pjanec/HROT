<!--STATUS
state: SUPERSEDED — by NOTE_Backend_To_Ui_Review_Timeline.md (the complete list: CE-3330 + the review's timeline / toolbar work)
superseded-by: NOTE_Backend_To_Ui_Review_Timeline.md
was: backend → ui: the timeline panel's zoom / pan defects (CE-3330) and the interaction spec the review needs
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
-->
# NOTE — backend → ui: the timeline panel's zoom and pan (CE-3330)

Reviewing CE-1302's built `TimelinePanel` for the review / DAR design ([DESIGN_Replay_Time_Axis_And_Review](../../DESIGN_Replay_Time_Axis_And_Review.md),
CE-3329, user-approved R-3057…R-3061) found thirteen defects and the gaps the user called crucial ("well working zoom and pan with time
stamps and right click menu handles the seeks"):

- 🔴 **T1** — the view resets to the whole extent whenever the extent changes (`TimelinePanel.cs:70-74`); a live source grows every frame.
- 🔴 **T2** — a left click seeks (`:196-199`); R-3059: a left click only selects, seeks live in the right-click menu.
- 🔴 **T12** — nothing follows the playhead during play.
- T3–T11, T13 and the spec (follow modes, fit / presets, range selection + loop, hover readout, snapping, keyboard, zoom history, the
  input mapping, zoom limits, the ruler): the design's **§10**, tracker row [CE-3330](../Blueprint_Issues_Tracker.md).

⭐ Your component and your design (`DESIGN_Director_Timeline_Editor.md`): fold §10 into its §2b when you build it. TE-19's "a live host
never seeks" is superseded by the review (R-3060) — marked in your design's known-rot.

_backend lane, 2026-10-26_
