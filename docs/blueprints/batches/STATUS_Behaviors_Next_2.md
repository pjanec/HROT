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
| G-2 | CE-4124 | 📐 ruling IN (R-4179). Measured: the "sees" watch EXISTS — `EscalationLadder.Watch` (`EscalationLadder.cs:110`), run every tick by the police's default task `HoldTheLine` (`CivilianTkbCatalog.cs:116`) — but it warns only within the ENGAGEMENT reach (40 m, `HoldTheLineBehavior.cs:90`); the gunman at 62 m is outside it. Fix: the watch warns every identified-armed man the unit remembers SEEING (the reach cut goes; `Judge` already weighs distance and seeing), the 40 m stays for engaging. Applied after the G-7 worker's build (it touches Fdp.Toolkits) | — |
| G-3 | CE-4041 · CE-4136 · CE-4137 · CE-4139 | 🔎 ✅ CE-4139 closed — not a defect: the 360° control's description asks exactly for the early turn it shows (R-4182) · others in progress | — |
| G-4 | CE-4132 · CE-4133 | ✅ closed — `--verify ua-civ-provocation` and `--verify ua-civ-contagion` pass every check in the editor (2026-11-09) | rows ticked |
| G-5 | CE-4035 / CE-2232 | 🔨 root-caused (editor Preview journal): both technicals ended `LandPlatformAttack` with SUCCESS at 0.03 s — the attack is a Parallel that ends when either branch ends, and the fight branch was resolved WITHOUT the order's target, so it answered "nothing left to fight" before anything was sensed. ✅ Fix: `ResolveAttack` gives the cycle the named target (`VehicleFightFromCoverBehavior.cs:40`); the cycle designates it at its top, so it fights from the first frame. Rail `CE2232_AnAttackOrder_GivesTheCycleItsNamedTarget_SoItFightsBeforeAnythingIsSensed` (red-proved by dropping the line). Design row folded (`DESIGN_Vehicle_Exposure_Cycle.md`, "nothing sensed yet"). Live verify: see below | — |
| G-6 | CE-4151 | ⏳ | — |
| G-7 | CE-4354 the better merge | 🔨 design step done — Review perspective §6.12 (`874d1acc0`): the module diagram (both hosts call ONE `BinaryMerger.Merge` where they call the JSON builder today; persistent target; a world boundary resets), classes, S1–S5; S1 with the worker | — |
