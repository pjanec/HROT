<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md (on ui) — the showcase this note is about
  - ../../DESIGN_Parallel_Simulation.md — §18.5–§18.8 the per-entity rule, as built
  - HANDOFF_Coordinator_To_Backend_Unify_Threaded_Defaults_On.md — the unify batch that produces the branch to build on (READY FOR WINDOWS)
-->

# NOTE — coordinator → ui: build the city battle showcase's behaviour code on the PARALLEL branch

🔒 User, `2026-11-13`: *"for sure i would like to start developing the showcase in parallel, but how to prevent breaking the parallelism rules?
there will be lots of new behavior work involved"* · *"yes, write both"* (R-4225).

| | |
|---|---|
| ⭐ **now, on `ui`** | the showcase's design, scenario layout and behaviour DESIGNS — content that needs no parallel code |
| ⭐⭐ **behaviour CODE: on a `showcase` branch made from `exp/threaded-build` once it carries `READY FOR WINDOWS at <sha>`** | there every behaviour runs as a per-entity parallel phase and the checks exist; ⛔ on plain `ui` they do not, so a violation would surface only at merge |
| ⭐⭐⭐ **the rule and the checks** | `.claude/CLAUDE.md` § *"BEHAVIOUR CODE RUNS IN PARALLEL"* — nine measured "never / instead" rows, and the three machine checks every new behaviour's own rails run with (`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1`, the strict guard, `scripts/stepped-hash.py --threads 1,4,8` on the showcase scenario) |
| ⭐ **when the programme merges into the lanes** (after the user's manual tests) | the `showcase` branch merges with it — already compliant |
| ⚠ **the showcase scenario itself** | design outcomes ("the squad clears the block"), never exact same-tick ordering: an effect on another unit lands one tick later (R-4212) |

---
_coordinator lane, 2026-11-13_
