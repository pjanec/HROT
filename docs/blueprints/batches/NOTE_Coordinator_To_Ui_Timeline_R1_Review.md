<!--STATUS
state: LIVE — coordinator → ui: review of Timeline R1-a…c (CE-1302)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — §9b holds the review; this note only points at it
-->

# NOTE — coordinator → ui: Timeline R1-a…c reviewed ([NOTE_Ui_To_Coordinator_Timeline_R1abc](NOTE_Ui_To_Coordinator_Timeline_R1abc.md))

⭐ **Accepted.** It is the timeline the design asked for, measured under every budget; the deviations of §9 / §9a are accepted as
argued. Merged into `coordinator` at `ui@42f1ff7b5`. ⛔ Not a re-dispatch: your scope is unchanged — these are inputs for the slices named.

| | for | what — [`DESIGN_Director_Timeline_Editor.md`](../../DESIGN_Director_Timeline_Editor.md) §9b |
|---|---|---|
| ① | R2 | store **sim time per frame** beside `FrameTicks` (takes and `checks.json` are in sim seconds) |
| ② | R2 | `ReplayDirectorHost` seeks through the panel's **`ITimelineCursor`** (your D1); §1/§3/§4b are marked known-rot |
| ③ | R1-d | **the TKB in the Replay Browser (R-4069)** is in no slice of §9 — the swimlane needs it |
| ④ | — | the unloaded-assembly playback stop is filed as **CE-4050** (coordinator's replay harness) |
| Q-a, Q-b | the user | TL-3 vs R-4075 (lean: your seam, no adapter, retire `ITraceLaneProvider` at R1-e) · the extractors vs R-4077 (lean: fixed lanes are a catalogue; every condition is a predicate). ⚠ Hold the R1-e seam choice until the user answers Q-a |

---
_coordinator lane, 2026-10-26_
