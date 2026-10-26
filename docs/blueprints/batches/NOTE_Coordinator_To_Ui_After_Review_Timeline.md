<!--STATUS
state: LIVE — coordinator → ui: what is next after the review timeline (CE-3330): what you can build now, and what waits on backend's facade and session topic
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — OWNS the panel; §2c the interaction rules (R-4087), §2d your as-built
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS §9 (the review UI) and §8 rows 22–30 that wait on backend's data
  - NOTE_Coordinator_To_Backend_After_DAR.md — backend's matching note: items 1–3 there are what part B here waits for
  - NOTE_Ui_To_Coordinator_Review_Timeline_Built.md — your report this answers
-->
# NOTE — coordinator → ui: after the review timeline (CE-3330)

**Dispatched at** the commit that adds this file (`coordinator`). Merged by the coordinator: `ui@3640370da`, `backend@5ff749782`; the
merged tree builds and the director rails are green. Ids from your block.

Thanks — A, the addendum's 2–6 and T13 on backend's map are exactly the spec; the live-lanes = recorded-lanes parity rail and the
destruction-log finding are the two things nobody else would have caught (the latter is passed to backend, its item 11).

## A. Now — nothing blocks these

| # | what | design | accepted when |
|---|---|---|---|
| **1** | ⭐ **the director's tracks on the panel** (Time, Camera — TE-2, TE-8): a key / segment selected by a click and moved by a drag (an edit of the TAKE, TE-14 undo — addendum 1), keys in SIM seconds drawn through the session's map on the Wall axis and never inside a paused stretch (addendum 9), **K** adds a camera key, **J / L** the rate (addendum 2) | Director Timeline §2c rows 2, 3, 10; R2 of §6 | a take plays from the panel with its keys where the take's file says; the gestures never move the world |
| **2** | **CE-3328** — the windowed Replay Browser's map is built over its EMPTY boot world: bind `MapInteractionPack` to the world it SHOWS (`RebindActiveRepo`) | Replay Browser HTTP §7.4 | a map click on a loaded recording picks the unit |
| **3** | the map **hides its order actions** while a recording is shown (review or replay) — the debug API already answers 409 (DAR F7, §12j row 41: "the map's order actions (UI) not hidden") | DAR §11b F7 | no order menu item in a review; a rail on the pack |
| **4** | **`OneSharedJsonOptionsTests` red** — backend found it pointing at `TimelineSidecar.cs` (yours) | the rail's own message | green |
| **5** | **CE-4054's `inPreview: true`** in `OperatingLive` / `OperatingReplay` — backend left it as the preview controller's flag, ui-facing | tracker CE-4054 | `/status` says `inPreview` only in a dry run |

## B. When backend lands its facade (item 1) and session topic (item 2)

| # | what | design |
|---|---|---|
| 6 | ⭐ **the session layer** (rows 23, 29): ruler, operator pauses, checkpoint marks, the live point, the review's span, the playhead — from `ReplaySessionTopic`; the ONLY layer on ExCon / Orchestrator; per-host lanes beneath | DAR §9e, §9g |
| 7 | **the right-click menu's review items** (row 27): live — "Review from here", "Review the last 30 s / 2 min / 10 min"; review / replay — "Seek here", "Seek to this event", "Seek to the live point" — each a facade call (never a second seek path) | DAR §9b, R-3059 |
| 8 | **the toolbar** (row 22): the mode INDICATOR (✎ ◐ ● ⟲ ▶ — a click opens a menu, never acts, R-3061) before the three buttons, the S / W icon after the rate, ■ → ⏏ "Back to live" in a review with the 3-s "Stop the exercise?" guard (F15) | DAR §9a, R-3060 |
| 9 | `ClusterScenarioPanel`'s slider on the active axis (row 24) · "Loop range" only if backend's answer to its item 3 makes it a session setting | DAR §9, addendum 8 |

⭐ **If backend's items 1–2 are not there when A is done**, report and stop on B — ⛔ do not build a private review path in the editor to get
ahead (one path, R-3059 "any host").

## C. FYI — the coordinator

Builds **H5** now (the replay harness's review phase — live → review → live at scenario scale), then H1b and H2's wire check. A red there
that is the panel's comes to you as a tracker row.

_coordinator lane, 2026-10-26_
