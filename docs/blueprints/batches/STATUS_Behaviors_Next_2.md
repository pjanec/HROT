<!--STATUS
state: LIVE — behaviors' status for HANDOFF_Coordinator_To_Behaviors_Next_2 (dispatched at bacf4119a)
updated: 2026-11-09
current-answer: the table
related-designs:
  - ../../UX/DESIGN_Review_Perspective.md — §6 the better merge (G-7)
-->
# STATUS — behaviors, Next_2

| # | id | state | evidence |
|---|---|---|---|
| G-1 | goldens | ✅ 3 re-captured · ⛔ 7 NOT (they moved) — `--verify <10> --verify-capture` on the merged build: **all 10 pass every check**. Diff shape per scenario (values that differ · max position change): **representation-only, committed:** `ua-night-sensing` (870 values, positions EQUAL), `ua-weapon-systems` (432, EQUAL), `ua-weather-sensing` (892, EQUAL) — the differences are EntityGuid / params-as-objects / passenger-list shape and the hashes of components holding them. **NOT committed — the run itself moved:** `ua-m2-engage` (608 · 0.08 m), `ld-sentry-surprise-360` (344 · 1.5 m, from snapshot 4), `ua-javelin-lock` (591 · 1.71 m), `ua-car-bomb` (802 · 2.15 m), `ua-weather-regions` (330 · 2.15 m), `ua-shootdown-eject` (856 · 2.02 m — already at t = 0: Patrol 1's spawn HEIGHT), `ua-casualty-care` (3185 · 14.88 m — a behaviour change). ⏳ For the golden owner: these 7 pass their checks; the drift is small motion (≤ 2.2 m) for six and a real change for `ua-casualty-care` — re-capture on purpose or chase | `/tmp`-free: re-run `scripts/scenario-regression.sh <list>` |
| G-2 | CE-4124 | 🔨 R-4179 BUILT (`7e18dd6fc`: the watch warns every armed man it SEES; the 40 m reach is for engaging only) + scenario: gunman on open ground (`775ae6f94`), rifleman `HoldTheLine {Reach: 75}` ⇒ `--verify` 11/12 (×2, deterministic): warned 4.25 s, police rifle fires at and hits the gunman 12.38 s. 🔴 1 red: the Instigator is beaten to death by NON-LETHAL blows (25 each) once the rifleman is busy with the gunman — ⏳ design question written into CE-4124 (lean: a non-lethal hit never takes a man below incapacitated). Row stays open | — |
| G-3 | CE-4041 · CE-4136 · CE-4137 · CE-4139 | ✅ CE-4139 closed — not a defect (R-4182). 🔨 CE-4137 ROOT-CAUSED + FIXED (`60af8e687`): reproduced twice on the editor's Live path (backend's `/run-mode` fix made it runnable) — Alpha walks to Bravo's support point; a commander's roster was filled in QUERY order, and a Live load after Edit reuses freed entity slots in reverse; now network-id (file) order; rail `CE4137_TheRosterIsInNetworkIdOrder_…` red-proved. CE-4041 / CE-4136 in progress | — |
| G-4 | CE-4132 · CE-4133 | ✅ closed — `--verify ua-civ-provocation` and `--verify ua-civ-contagion` pass every check in the editor (2026-11-09) | rows ticked |
| G-5 | CE-4035 / CE-2232 | 🔨 two fixes built + red-proved (`1f8adf4b2`, `31c8c4d05`): `ResolveAttack` gives the cycle the named target, and the cycle WAITS while it is in the fight (📐 instrumented live: the seeded target is gone from memory by tick 2, so the attack ended Success at 0.03 s). Rail `CE2232_AnAttackOrder_GivesTheCycleItsNamedTarget_AndItWaitsWhileNothingIsRemembered`; design row folded. 🔴 `--verify` still: neither technical fires — next layer being probed | — |
| G-6 | CE-4151 | ⏳ | — |
| G-7 | CE-4354 the better merge | 🔨 design step done — Review perspective §6.12 (`874d1acc0`): the module diagram (both hosts call ONE `BinaryMerger.Merge` where they call the JSON builder today; persistent target; a world boundary resets), classes, S1–S5; S1 with the worker | — |
