<!--STATUS
state: LIVE — the manual test guide for objectives, triggers, scores, macros and their screens: one row per feature, how to try it by hand, what you should see
updated: 2026-11-03 (skeleton by coordinator; each owning lane writes and LIVE-VERIFIES its rows' steps — HANDOFF_Coordinator_To_*_Rules_Runnable.md)
current-answer: §2 the feature rows
stale-below: nothing
known-rot: rows marked "steps: owner to write" carry no verified steps yet
related-designs:
  - docs/EXPLAINER_Scenario_Evaluation_And_Direction.md — HOW each feature works (this file is only HOW TO TRY IT)
  - docs/SHOWCASE_Scenarios.md — the two scenarios almost every row runs on (Bridgehead = training, Crossfire = game)
  - docs/DESIGN_Scenario_Rules.md · docs/DESIGN_Rules_View_UI.md · docs/DESIGN_Player_HUD_And_Debrief.md · docs/DESIGN_Scenario_Macros.md · docs/DESIGN_Macro_Authoring_Editor.md · docs/DESIGN_Node_Features.md — the owning designs
-->

# MANUAL TEST — **objectives, triggers, scores, macros and their screens**

⭐ For a person at a desktop: **start this, open that, do this, you should see that.** Each row's steps are written and **run live** by the lane that
owns the feature; a row with no verified steps says so. ⛔ No screenshots in git — a row may name the rail that renders one.

## 1. Starting

| what | how |
|---|---|
| the whole cluster in one process (editor, server, player, AI) | `./run_All.sh` (Windows `run_All.bat`) — ⭐ owner: backend to confirm the exact command and how to load a scenario Live |
| the editor alone | `./run_Editor.sh` |
| a player station | `./run_Player.sh` |
| a recording afterwards | `./run_ReplayBrowser.sh` |
| the two scenarios | `scenarios/showcase-bridgehead` (training: objectives, standards, evaluation) · `scenarios/showcase-crossfire` (game: capture points, tickets, score) |

## 2. The feature rows

| # | feature | owner | steps → what you should see | verified |
|---|---|---|---|---|
| **A1** | objectives tick and report (`GET /objectives`, the Rules window) | behaviors + ui | *owner to write* | ⏳ |
| **A2** | a trigger fires its Do list, step by step (*step 2 / 3*) | behaviors + ui | *owner to write* | ⏳ |
| **A3** | fire / disarm / re-arm / reset a rule by hand | ui | *owner to write* | ⏳ |
| **A4** | author a trigger in the editor (When / Do / Rules tabs, + Add, drag to connect), save, reload | ui | *owner to write* | ⏳ |
| **A5** | change a rule LIVE (cooldown while armed) — the fire count stays; a bad edit is refused with its full reason | ui + behaviors | *owner to write* | ⏳ |
| **A6** | the *Coming up* strip and the round clock count down to timed triggers | behaviors + ui | *owner to write* | ⏳ |
| **A7** | the instructor's **End** button ends the exercise through the scenario's own end | ui | *owner to write* | ⏳ |
| **A8** | messages: a briefing reaches the right players; the instructor sees **what went to whom** | ui | *owner to write* | ⏳ |
| **B1** | per-member standards (Bridgehead): every man's evaluation, role / standard means | behaviors | *owner to write* | ⏳ |
| **B2** | the mission summary — score per side with trend and pass mark, scores by chain / objective / role, *why* | ui | *owner to write* | ⏳ |
| **B3** | the trainee HUD (my objectives, my standards) and the trainee debrief (GO / NO-GO, went well / work on), live and on a recording | ui | *owner to write* | ⏳ |
| **B4** | the coaching hint (*"you are in the open"*), off by default | ui | *owner to write* | ⏳ |
| **C1** | Crossfire: capture points, tickets, respawn waves, the end naming the winner | backend | *owner to write* | ⏳ |
| **C2** | the per-player score, +100 pop-ups, hold-K scoreboard, kill feed | backend + ui | *owner to write* | ⏳ |
| **C3** | streaks, the bounty (announced, marked on the map, claimed for 300), awards | backend + ui | *owner to write* | ⏳ |
| **C4** | the round summary (winner, scoreboard, awards, best moment), live and on a recording | ui | *owner to write* | ⏳ |
| **C5** | a supply crate: inserted on time, held for tickets | backend | *owner to write* | ⏳ |
| **D1** | place a macro in the editor; its marker, member lines, dimmed transient members, the Scenario view | backend + ui | *owner to write* | ⏳ |
| **D2** | insert a macro at runtime (*Add ▸ Macros*, and an `InsertMacro` step), with a **preview** first (MA-10) | ui | *owner to write* | ⏳ |
| **D3** | the director: a counter-attack macro triggered by the trainees' progress; the skill amplifier | backend | *owner to write* | ⏳ |
| **E1** | game-master palette: run any shared action by hand; a per-side skill slider | ui | *owner to write* | ⏳ |
| **E2** | features per node: training vs game HUD by the scenario's preset; the operator's feature grid toggles one live | ui + backend | *owner to write* | ⏳ |
| **E3** | the score / evaluation entities never show on the map or in the ORBAT; they do show in the Scenario view and the rules view | ui | *owner to write* | ⏳ |

⭐ **A row is VERIFIED** when its owner ran exactly those steps on the current branch and saw the result; the cell names the branch@sha and date.
⚠ A known bug that does not stop the row is noted in the row (*"score may miss ~1 in 10 downings — CE-…"*), not a reason to leave it unverified.
