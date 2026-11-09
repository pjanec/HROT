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
| G-1 | goldens | ⏳ | — |
| G-2 | CE-4124 | 📐 ruling IN (R-4179). Measured: the "sees" watch EXISTS — `EscalationLadder.Watch` (`EscalationLadder.cs:110`), run every tick by the police's default task `HoldTheLine` (`CivilianTkbCatalog.cs:116`) — but it warns only within the ENGAGEMENT reach (40 m, `HoldTheLineBehavior.cs:90`); the gunman at 62 m is outside it. Fix: the watch warns every identified-armed man the unit remembers SEEING (the reach cut goes; `Judge` already weighs distance and seeing), the 40 m stays for engaging. Applied after the G-7 worker's build (it touches Fdp.Toolkits) | — |
| G-3 | CE-4041 · CE-4136 · CE-4137 · CE-4139 | ⏳ | — |
| G-4 | CE-4132 · CE-4133 | ✅ closed — `--verify ua-civ-provocation` and `--verify ua-civ-contagion` pass every check in the editor (2026-11-09) | rows ticked |
| G-5 | CE-4035 / CE-2232 | ⏳ | — |
| G-6 | CE-4151 | ⏳ | — |
| G-7 | CE-4354 the better merge | 🔨 design step done — Review perspective §6.12 (`874d1acc0`): the module diagram (both hosts call ONE `BinaryMerger.Merge` where they call the JSON builder today; persistent target; a world boundary resets), classes, S1–S5; S1 with the worker | — |
