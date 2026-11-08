<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §0 (switch now, R2″, D27–D33) — the design, approved by the user
  - ../../UX/run-indicator-visual.svg — the time controls in every state (approved)
  - ../../UX/live-session-storyboard.svg — the live session from the Start screen to the debrief and back (approved)
-->

# NOTE — backend → coordinator: the run-controls redesign is approved — 15 tracked items, yours to assign

The user (`2026-11-08`): *"I will ask coordinator to decide who builds what."* Everything below is designed and approved; nothing is built.
Design: [AQ108 §0](../Architect_Question_108_Run_Mode_Picker.md) · pictures: [time controls](../../UX/run-indicator-visual.svg), [storyboard](../../UX/live-session-storyboard.svg) ·
rows: [tracker](../Blueprint_Issues_Tracker.md) `CE-3405`…`CE-3419`.

| id | size | what | touches | depends on |
|---|---|---|---|---|
| CE-3407 | S | one "can I save" answer (variant + cluster state + error) | `IScenarioSession` (AiShared) | — |
| CE-3410 | S | an unrun Live leaves no recording | `ClusterMaster` (backend) | — |
| CE-3405 | M | switch now: the indicator switches (Save / Discard / Cancel, Live paused at 0, AAR at its start) | `RunModeChoice` (behaviors built L-3/L-5 there) | — |
| CE-3413 | M | Live ends in two steps: End → Live · ended → ⏏ Close → Idle; the ended menu; ⏪ greyed at the briefing | `RunModeChoice`, toolbar — **changes behaviors' L-3 as built** | CE-3405 |
| CE-3406 | S | the indicator: current mode, pen / eye / checkered flag, a glyph button with ▾ + hover | toolbar | CE-3405 |
| CE-3408 | S | the Save slot: floppy / greyed / amber flag | shell Save slot | CE-3407 |
| CE-3409 | XS | ▶'s glyph wears the indicator's colour; ▾ only in Edit and the briefing | toolbar | CE-3406 |
| CE-3411 | S | HTTP `/run-mode` switches; `/status`; MCP regen | DebugApi (backend) | CE-3405 |
| CE-3414 | S | Start screen Recent: Live · Edit (author only) · AAR latest run | `StartSurfaceWindow` | — |
| CE-3415 | S | run setting *Start immediately* | run settings (backend) | — |
| CE-3416 | M | Player briefing screen (reserved UI) | Player seats (ui) | — |
| CE-3418 | S | Player seats open their own summary at the end | `RulesBundle` / debrief windows | CE-3413 |
| CE-3412 | S | fidelity vs the two pictures + AQ108 as-built | — | all above |
| CE-3417 | L | 💡 IDEA — design first: the briefing picture book per role | — | — |
| CE-3419 | L | 💡 IDEA — design first: the Replay Browser as an editor perspective (takes Q-B3 / Q-B6) | — | — |

⭐ Backend's lean, for your call: the backend lane takes CE-3407, CE-3410, CE-3411, CE-3415 (its own seams) and can take the rest;
`RunModeChoice` (CE-3405, CE-3413) is where behaviors built L-3 / L-5 — whoever takes it folds the change into their L-3 as-built note.

---
_backend lane, 2026-11-08_
