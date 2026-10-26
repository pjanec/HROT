<!--STATUS
state: LIVE — coordinator → ui: ten additions to backend's review-timeline note, approved by the user (R-4087)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Backend_To_Ui_Review_Timeline.md — backend's note this ADDS TO (A the zoom / pan, B what the panel shows in replay and review, C the toolbar, D backend's data); read it first
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS §9 (the review UI) and §10 (the panel's defects, gaps and input map) that both notes build on
  - ../../DESIGN_Director_Timeline_Editor.md — OWNS the panel, the director's tracks, the lanes and the sidecar; amended the same day for items 1–3 (R-4087, its §2c)
-->
# NOTE — coordinator → ui: the review timeline, ten additions (R-4087)

🔒 **User, `2026-10-26`:** *"yes write the addendum note, and approved"* — on the ten points below.

**Read [backend's note](NOTE_Backend_To_Ui_Review_Timeline.md) first** — it is the work list (CE-3330 + the review's P4 rows). This note adds
what it misses when the SAME panel also carries the director's tracks, the trace lanes and the Replay Browser
([Director Timeline](../../DESIGN_Director_Timeline_Editor.md) — TE-2, TE-14, TE-21, TL-6, §9c/§9d). ⭐ One panel, ONE input map: where
DAR §10c and our design disagree, the row below says which wins. Our design's §2c carries these as amendments.

## Conflicts — one rule set, not two

| # | what | rule |
|---|---|---|
| **1** | ⭐ **director tracks take a left drag** | DAR §10c: a left drag over the lanes does nothing *"on empty space"*. On a DIRECTOR track (Time, Camera — TE-2, TE-8) a left click selects a key / segment and a left drag moves it — an edit of the TAKE (undoable, TE-14), never of the world, so R-3059 is not touched. Empty space on that track: nothing, as §10c |
| **2** | ⭐ **one keyboard map** (panel focused only) | Space play / pause · ←/→ one frame · Shift+←/→ previous / next event · Home / End · +/− zoom (§10b) · ⭐ **K adds a camera key** (TE-2, §4a — kept) · J / L slower / faster (the shuttle without K) · I / O range in / out. ⭐ **On a LIVE host the arrow keys move the SELECTION between events and never seek** — a seek there would enter a review (R-3059: no state change without the menu). In a review / replay / the Replay Browser they move the playhead |
| **3** | ⛔ **the old click-to-seek gestures go** | our design's *"double-click a marker jumps there"* (§2 Markers row), *"drag on the ruler to zoom to a range"* (§2b) and *"a click in the past seeks"* in the Replay Browser (§2a table) are SUPERSEDED by §10c: double-click = zoom to the event's cluster, ruler drag = pan, Shift+drag = range, seeks in the right-click menu. ⭐ **The Replay Browser obeys the same input map** — only its TOOLBAR is its own (R-3060) |

## Gaps

| # | what | rule |
|---|---|---|
| **4** | ⭐ **the right-click menu is EXTENSIBLE** | the mode's seek items (DAR §9b) **+ the items the row under the mouse contributes** — *Set breakpoint here* (trace lanes, TE-21 / R-4075), *Add key here* (director tracks), *Add bookmark* — through the lane / track provider, ⛔ never a switch on the row's kind in the panel (R-4078, R-4079: extensible, not hard-coded) |
| **5** | ⭐ **a left click on an event selects its unit — the ONE selection** | through `SelectionRequestSystem` (as `POST /selection` does, R-4083), so the map and the inspector follow; ⛔ no private selection in the panel |
| **6** | ⭐ **live lanes = recorded lanes** | `LiveTimelineSource` (row 28) runs the index builder's own per-frame step (`TimelineIndexBuilder` — the catalogue's `IndexedLaneDefinition`s, the same extractor semantics) on the live world and its bus — ⛔ not a second evaluator. Acceptance: a live run's live index equals the index rebuilt from its recording, lane by lane (the replay harness can assert it — Scenario Regression §10-2) |
| **7** | ⭐ **the sidecar at the end of a live run (row 33) must survive its header check** | the sidecar's header holds the recording's BYTE SIZE and its last frame's ticks (TL-6) ⇒ write it only AFTER the recorder has closed the file (`FinalizeLive` → the module uninstalled), with the existing writer (`TimelineSidecar`, per-lane definition hashes — §9d ④). ⛔ Otherwise the header mismatches and the replay silently rescans everything |
| **8** | ⚠ **"Loop range" needs the master** | a loop in a review / replay drives the CLUSTER clock — two hosts looping on their own fight over it. ⛔ Do not build it in the panel alone: it is a session setting of backend's `ReplayClockProcessManager` (asked of backend in [NOTE_Coordinator_To_Backend_Review_Harness](NOTE_Coordinator_To_Backend_Review_Harness.md)). Until then the menu item is absent (the Replay Browser may loop locally — its clock is its own) |
| **9** | **director tracks on the Wall axis** | a take's keys are in SIM seconds (`AtExpr`, `TakeTimeline`); on the Wall axis they are drawn through the session's `ReplayTimeMap`; a key never sits inside a paused stretch (a drag there snaps to the unpause instant) |
| **10** | **events inside one collapsed pause** | on the Sim axis every event of a collapsed operator pause lands on ONE instant: draw them as one cluster with a count at the pause's ◯ / ● tick; hover lists them; double-click zooms to it on the Wall axis |

_coordinator lane, 2026-10-26_
