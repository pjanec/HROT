<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the item table, then each item's section and the gate table
stale-below: none
related-designs:
  - docs/DESIGN_Parallel_Simulation.md — §18.2c owns the rules under the brain phase (D-1 as-built) and the per-entity rule
  - docs/DESIGN_Semantic_Value_Types.md — §2.4 owns SimInstant / SimDuration, incl. their non-finite JSON (D-2 as-built)
  - docs/DESIGN_Entity_Control.md — §8e owns the operator's fire press (D-3: a press during a reload is dropped, CE-4344)
-->

# STATUS — Determinism residue (CE-3448 · CE-3441 · CE-3447 · CE-3443 · D-5 · CE-6004)

Handoff: [`HANDOFF_Coordinator_To_Backend_Determinism_Residue.md`](HANDOFF_Coordinator_To_Backend_Determinism_Residue.md), dispatched at
`885b083c7`, scope frozen there. Branch `backend`, started at `5d8285c0c` (backend = `coordinator@d01ed2df5`).

| item | tracker | state | result |
|---|---|---|---|
| D-1 | CE-3448 | ✅ `65dda2325` + `34f7e9fc9` | the 26: **0 designed lag · 9 ② lost refusal reason · 17 ① harness defect**; the survey then found 5 verbs writing another entity and 9 undeclared rule reads — fixed. Rule suites 190/190 serial, parallel, parallel + read check |
| D-2 | CE-3441, CE-1471 | ✅ `6b3046960`, `78ce1b04c` | `"-inf"` / `"+inf"` / `"nan"` markers, round-trip rail; none of the 12 combat / squad scenarios stops on ±∞ any more |
| D-3 | CE-3447 | ✅ `cce190bd0` | not a regression: the rail was stale since CE-4344 (a press during a reload is DROPPED, user 2026-11-08); proven by mutation, rail re-pointed |
| D-4 | CE-3443 | ✅ `65dda2325` | `ParamUsage.Rename` sets a new instance; rail red-proved |
| D-5 | CE-3450 | see below | |
| D-6 | CE-6004 | see below | |

Ids allocated: **CE-3449** (five goldens stale at t = 0), **CE-3450** (the D-5 survey findings).

## D-1 — the rules under the parallel brain phase (CE-3448)

| group | n | cause | fix |
|---|---|---|---|
| ② refusal reason | 9 | `ActionFailure.Set` in the phase deferred the add; the trigger read it in the same step ⇒ *"an action failed"* | a per-thread pending reason `TryGet` serves until the add lands — one path for every verb |
| ① harness | 17 | `ObjWorld` advanced the world version only `WithHistory` ⇒ the last-tick view (copied by changed chunk) stayed at tick one | the harness ticks the version and takes the view each tick, as the kernel does |
| survey (`HROT_READ_CHECK=2`) | — | 9 rule types read on other rules undeclared; `AddToCounter`, `SetObjectiveActive`, `BeginPhase`, `EndExercise`, `SetSkillAmplifier` wrote another entity in place | declared in `AiBrainReads`; `RuleAfterPhase.Run` — the one serial path (now, or a follow-up in batch order on the live state); `Message` uses it too; the rules' tree prepared by `AiBrainReads.Contribute` for every host |

⭐ No case was a designed one-tick lag: once the harness was right, every rail holds its serial assertion under the phase. Permanent rail:
`DrillsUnderTheReadCheckTests.TheRules_AsTheParallelPhase_MakeNoUndeclaredCrossReadOrWrite` (a child run with the check on).

## D-3 — the CE-1078 press rail (CE-3447)

Proof instead of a bisect (one build, not ~10): CE-4344's drop disabled at HEAD ⇒ the old rail **green**; CE-4344 is in **both** parents of
the coordinator merge ⇒ the merge was not the cause; red with every threading switch off ⇒ not a parallel defect. The rail now asserts the
user's CE-4344 rule.
