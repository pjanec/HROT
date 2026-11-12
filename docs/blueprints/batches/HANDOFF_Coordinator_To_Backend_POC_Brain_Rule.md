<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — ⭐ §18–§18.2 the per-entity rule, the redirect and the declared mask; §11.2 the brain's ~36 ms serial walk (the measured ceiling)
  - ../../DESIGN_Threaded_Work_Inventory.md — §7.2 the review's fixes F-1…F-16, §8 the decision list
  - STATUS_Read_Check.md (on exp/read-check) — the per-read check, affordable as designed
-->

# HANDOFF — coordinator → backend (VM): DAYTIME POC — the per-entity rule on the BRAIN's serial walk

**For:** a backend VM session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen.
🔒 User, `2026-11-12`: *"we need to measure first, and once we are sure, then we will start the overnight batch"*. ⛔⛔ **Autonomous** (R-4156).

## Why
Windows (CE-4451): the brain pre-score scales, but ~36 ms of BrainTick is a SERIAL walk (behaviour trees, HSMs, blueprints, actions) — the
measured ceiling (whole CGF 1.50×). The per-entity rule (R-4212 / R-4214) is meant to make that walk parallel. Prove it on the brain BEFORE the
night converts every system.

## Where to work
A NEW branch **`exp/poc-brain-rule`** from **`exp/integration`**. Status `docs/blueprints/batches/STATUS_POC_Brain_Rule.md` on it. Switch default OFF.

| # | prove | done when |
|---|---|---|
| **B-1** | ⭐ the pool carries the tick context and the current entity into every batch (F-1); an access with no current entity inside a parallel phase throws in Debug | a rail: a batch on a pool thread reads as on-tick with the right entity; red-proved |
| **B-2** | ⭐ the redirect in the repository's own read / write path (`exp/read-check`'s check made real): a read of ANOTHER entity's in-phase-written type served from the masked LAST-TICK copy taken serially at the phase start (R-4214 ④⑤); a write to another entity captured as a field-level / append COMMAND (F-5); a structural change deferred (F-6); `Bus.Publish` inside the phase to the batch's event list (F-2) | a rail per path; 0 B per steady tick |
| **B-3** | ⭐ BrainTick's serial walk run per entity on the pool under the rule — the census's conversion list (child sensors deferred + idempotent while pending, F-8; callouts / order journal as commands; per-thread scratch; the shared statics) | ⭐ the same result at 1 / 2 / 4 / 8 threads (a world hash over N steps, stepped mode); the guard silent; red-proved by removing one deferral |
| **B-4** | the behaviour changes the reviews predicted, MEASURED on a contact-onset run: callout chorus (implement F-10's de-dup at playback), cover ties (F-9), squads per squad (F-11) — counts before / after | the counts; the de-dup leaves exactly one "Contact" per burst within 10 m / 2 s |
| **B-5** | BrainTick at 1 / 2 / 4 threads on the VM (a trend) + a one-command Windows script for 1…12 threads | the VM trend; the script left for the Windows lane |
| **B-6** | ⭐ the verdict — does the serial walk parallelise; what the overnight conversion of the other systems must do differently | top of the status file; folded into Parallel Simulation §18 and inventory §8 |

Rules: the touched features' OWN rails only, no broad suites; slow things in the background, never polled; ⛔ no screenshots in git.

---
_coordinator lane, 2026-11-12_
