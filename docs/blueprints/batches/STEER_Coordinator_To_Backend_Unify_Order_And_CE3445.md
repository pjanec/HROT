<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Unify_Threaded_Defaults_On.md — the RUNNING batch this steers (started at faf06b8dd from the dispatch 1d459ad0c)
-->

# STEER — coordinator → backend (the running unify batch): the order, and CE-3445 dropped

🔒 User, `2026-11-13`: *"the windows handoff comes AFTER the unify i guess. but the unify contains some rare issues which might take ages to
finish"* · *"maybe we can ignore CE-3445 completely? I need to start with the new showcase scenario (see ui lane) so i need working
parallelized solution soon enough"*.

**Rule 1c:** a steer, not an amendment. It is NOT a correction of the lane — the handoff it runs did not say this.

| | |
|---|---|
| ⭐⭐ **WITHDRAWN: U-6 (`CE-4535`, CE-3445's long series)** | ⛔ do not run it. CE-3445 (a rare stepped divergence in the threaded editor, 1 in 36) stays OPEN in the tracker, deferred — not this batch |
| ⭐⭐ **the order: U-1 → U-2 → U-3 → U-4, then push `READY FOR WINDOWS at <sha>`** | the Windows session starts on that commit; then U-5 and U-7 |
| ⭐⭐ **a rare, non-reproducing failure never holds U-4** | a gate that fails once and passes on a re-run of the same command is RECORDED in U-4's row (with its saved worlds, if the gate saves them) and U-4 is ticked; ⛔ a failure that reproduces is a defect, fixed first |
| ⭐ **why the hurry** | the user starts the city battle showcase scenario (ui lane) on the parallel solution as soon as it has passed the automated gates, Windows and their own manual tests |
| unchanged | U-1 … U-5 and U-7 as written |

---
_coordinator lane, 2026-11-13_
