<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §0 switch now, R2″, D27–D33 (approved)
  - ../../UX/run-indicator-visual.svg · ../../UX/live-session-storyboard.svg — the acceptance pictures (approved)
  - NOTE_Backend_To_Coordinator_Run_Controls_Tasks.md — the 15 items, sized, with their dependencies
  - ../../UX/DESIGN_Scenario_Lifecycle_Review.md — LC-14 … LC-18, which §0 agrees with
-->

# HANDOFF — coordinator → ui and backend: the run controls (AQ108 §0)

**For:** `ui` and `backend`, both free. **From:** coordinator. **Dispatched at `<sha of the commit adding this file>`**. ⭐ Scope FROZEN there.
Each lane: merge `coordinator` and the other lane first (restore after), push `chore: started HANDOFF_Coordinator_To_Ui_And_Backend_Run_Controls at <sha>`,
then the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) — status file after every item, fetch and merge the other lane after every item.
⛔⛔ Anything needing the user is skipped (R-4156). behaviors is on hold.

🔒 User, `2026-11-08`: *"backend updated the design and left you a list of tasks. I would like to continue building, backend and ui lanes are free."*

## The split — ⭐ ONE lane owns the toolbar, so no two lanes edit `RunModeChoice` / the time controls at once

### ui — the run controls (in order)

| # | id | what | needs |
|---|---|---|---|
| **R-1** | CE-3405 | **switch now** — choosing a mode at the indicator switches at once (Save / Discard / Cancel when leaving Edit with unsaved edits; Live = loaded paused at 0:00; AAR = the recording at its start); ▶ in Edit = Preview; ■ = the start of the same mode | — |
| **R-2** | CE-3413 | **Live ends in two steps** — ■ → *End the exercise?* → **Live · ended** (results on screen, ▶ greyed, ■ becomes ⏏) → *Close the live session?* → Idle; the ended menu (Run again · Restart from a checkpoint · DAR · AAR · Close); ⏪ greyed at the briefing. ⭐ **Changes behaviors' L-3 as built** (today the end leaves Live) — fold it into their L-3 as-built note | R-1 |
| **R-3** | CE-3406 | **the indicator** — the current mode; pen (saveable Edit) / eye (variant, Preview) / ● red-green / ⟲ / ▶ blue / the checkered flag (ended); a glyph button with ▾ + hover + hand cursor (D29) | R-1 |
| **R-4** | CE-3409 | ▶'s GLYPH wears the indicator's colour; the presets ▾ only in Edit and the briefing (replaces L-11's colour rule) | R-3 |
| **R-5** | CE-3408 | **the Save slot** — one button whose glyph changes: floppy in Edit, greyed with the reason when not saveable and in Preview, the amber flag (checkpoint) in Live / DAR / AAR | backend's B-1 (CE-3407) |
| **R-6** | CE-3418 | Player seats open their own summary when the exercise ends (trainee debrief / game summary) | R-2 |
| **R-7** | CE-3416 | the Player's **briefing screen** while Live waits at 0:00 (description + my side's / role's objectives; the picture-book area reserved) | — |
| **R-8** | CE-3412 | **fidelity + as-built** — render every state of both pictures from the real toolbar (xvfb), compare, fix (R-4146); fold deviations into AQ108 §0 | all above |

Then ui resumes its queue: P-7 7c / 7d, A4, B-16 (H2–H4), K2 / K3 / K7, A7, U-23 / U-24 rest, L-10 (backend's Preview run-setup note).

### backend — the seams (in order)

| # | id | what | needs |
|---|---|---|---|
| **B-1** | CE-3407 | ONE "can I save" answer — `IScenarioSession.SaveDisabledReason` covers the variant, the cluster state and an error (the pen, the Save slot and File ▸ Save all read it) — ⭐ first: ui's R-5 waits on it | — |
| **B-2** | CE-3410 | a Live closed before it ever ran leaves no recording (D33) | — |
| **B-3** | CE-3415 | the run setting *Start immediately* (a Live load resumes by itself; off by default) | — |
| **B-4** | CE-3414 | the Start screen's **Recent** — Live (→ the briefing) · Edit (author seat only, greyed with its reason elsewhere) · AAR (the latest run, greyed when never run) | — |
| **B-5** | CE-3411 | HTTP `/run-mode` switches (409 + the question unless `confirm:true`), `/status` = the current mode, MCP catalog + skill regenerated | ui's R-1 |
| **B-6** | — | ui's REQUEST [NOTE_Ui_To_Backend_Preview_Run_Setup](NOTE_Ui_To_Backend_Preview_Run_Setup.md) — a dry run that takes the run setup's args (unblocks ui's L-10) | — |

Then backend resumes [HANDOFF_Coordinator_To_Backend_After_Mission_Plan](HANDOFF_Coordinator_To_Backend_After_Mission_Plan.md) (server seat, Wait-task
warning, `EntityMission_MovesEntity`, autosave, the pre-existing reds) and the struct editor follow-ups CE-3399…CE-3404.

⛔ **Not here:** CE-3417 (the briefing picture book) and CE-3419 (the Replay Browser as an editor perspective, with Q-B3 / Q-B6) — ideas, the
coordinator designs them with the user first.

## Gates

The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8: R-1, R-2 and B-2 change the run on every node ⇒ the run-mode suites (`MainToolbarTimeControl`,
`TheOneTimeFacade`, `RunModeChoice` rails) and a cluster rail per move (Edit → Live paused → running → ended → Idle). ⭐ Fidelity (R-4146) on R-3 … R-8.

---
_coordinator lane, 2026-11-08_
