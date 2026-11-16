<!--STATUS
state: LIVE — a note to a RUNNING batch (behaviors, minefields), not an amendment of the handoff
updated: 2026-11-16
current-answer: the whole note
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — the design (§4 slices, MR-19 the demo)
  - ../../DESIGN_Scenario_Regression.md — §3 the checks grammar, §4 the golden, the verifier (`scripts/scenario-regression.sh`)
  - HANDOFF_Coordinator_To_Behaviors_Minefields.md — your batch; its "Acceptance demo" line is what this note sharpens
-->

# NOTE — coordinator → `behaviors`: the minefield FEATURE-TEST SCENARIOS (R-4277)

🔒 User, `2026-11-16`, verbatim: *"Without the scenario passing, i don't believe that the feature works because in the past every such scenario
development revealed a lot of issues. Those scenarios should work like a regression test - their description should clearly describe what they
are all about and what is expected. Maybe event better if this is expressed as objectives so it is testable by machine (without the need of smart
ai agent interpretting the results) as part of regression test suite"*

⭐ Your handoff already asks for ONE acceptance demo. R-4277 makes it a SET, each one a regression test: a `scenario.json` whose `description`
says **What** happens and how to **Judge** it, and a `checks.json` whose `expect` rows say the same thing to the machine (Scenario Regression §3:
`field` / `near` / `task` / `health` / `blast` cues, `after`, `never`, `atEnd`) + a `golden`. ⭐ A slice is DONE only when its scenario passes
`scripts/scenario-regression.sh <name>` in the deterministic editor run *(amended `2026-11-16`: the 1 / 4 / 8-thread hash is NOT part of a scenario's verdict — it stays the parallel rule's own gate)*. A cue the grammar cannot say ⇒ extend the grammar
(§9's process) — ⛔ never a check judged by reading a log.

| scenario (`scenarios/demos/minefields/…`) | what it proves — every line an `expect` row |
|---|---|
| `mf-strike-and-halt` | a patrol walks into an unknown AP field: a mine goes off ONCE (`blast`), a man is hurt, the squad HALTS, backs out on its own track, cares for him in place, and reports; the side's cells round the blast become SUSPECTED; nobody re-enters them |
| `mf-recon-and-mark` | an engineer team traces the suspected area at detector pace, never steps on an unswept cell, and MARKS the field; the cells end known-clear / known-mined as the truth says |
| `mf-route-around` | a squad ordered across a KNOWN field routes round it; an own field (known at start) is avoided the same way; an enemy squad that does not know the field walks in |
| `mf-no-way-round` | the only way is through the field: the march reports a Minefield area, halts and calls the engineers |
| `mf-at-vs-vehicle` | an AT field: men walk over it unharmed; a tank sets one off and is mobility-killed from BELOW; the crew stays, followers reverse in their tracks |
| `mf-clear-lane-and-cross` | the mine clearer cuts a lane with plough / flail (and a line charge in a second run) under the breach play's fire and smoke; the squads cross single file; nobody is hurt in the lane |
| `mf-side-knowledge-replay` | save mid-run → load → the knowledge is the same; a replay seek shows each side's knowledge as it was |

⭐ Add each to `docs/CAPABILITIES.md` §9b with its ✅ / ⚠ when it passes. ⚠ They need terrain's MS-T1…MS-T4; build the ones you can as soon as their
engine half has merged, keep the rest listed as ⏳ in your status with what they wait on.
