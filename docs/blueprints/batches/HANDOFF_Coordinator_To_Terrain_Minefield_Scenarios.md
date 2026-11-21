<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — the programme (CE-4624), every slice built; §3 decisions, §4 slices, §6.1 the contract
  - STATUS_Terrain_Minefields.md · STATUS_Behaviors_Minefields.md · STATUS_Ui_Minefields.md — what each lane built
-->
# HANDOFF — coordinator → `terrain`: THE LAST TWO MINEFIELD SCENARIOS, then close the programme (CE-2443, CE-4624)

🔒 User, `2026-11-19`: *"yes give those two to terrain"*. All twelve slices (MS-T1…T4, MS-B1…B4, MS-U1…U4) are built and six scenarios pass
(`mf-strike-and-halt`, `mf-at-vs-vehicle`, `mf-recon-and-mark`, `mf-way-round`, `mf-no-way-round`, `mf-engineers-answer`). Two situations the
programme promised have no scenario yet (R-4277: a feature works when its scenarios pass).

## Priority
Next.

| order | scenario | what it proves (the checks) |
|---|---|---|
| 1 | **`mf-clear-lane-and-cross`** | the END-TO-END chain (MR-19): a patrol strikes a mine and halts → the engineers trace and mark the field → the clearer (TKB 116) cuts a lane along the patrol's route → the squad crosses THROUGH the lane and reaches its objective. Checks: a strike, a mark, `MinesCleared` on the lane, the lane on the side's knowledge, every squad member's path inside the lane while crossing, the objective reached, no second strike |
| 2 | **`mf-knowledge-survives`** | a side's minefield KNOWLEDGE (strikes, marks, cleared lanes) survives a SAVE + LOAD and a REPLAY seek: after the load / the seek the side still routes round the field, through the lane, and the other side still does not know it. Checks on the knowledge table before and after |

A scenario that goes red is a FINDING: root-cause it (the owning design first); fix it if it is yours, else a NOTE to the owning lane (behaviors for the
squad, ui for the drawing) and mark the row blocked by it. §6a per row.

## Then close
When both pass: tick **CE-2443** (rewrite its stale text — `mf-at-vs-vehicle` passes, the pieces landed) and **CE-4624** (the umbrella), each with the
eight scenarios and their verdicts; mark `DESIGN_Minefields.md`'s build-state BUILT (obligation ⑤ — fold any deviation in).
