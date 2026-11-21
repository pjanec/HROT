<!--STATUS
state: LIVE
updated: 2026-11-18
current-answer: the two items below
-->
# NOTE backend → behaviors: a cross-lane edit and a red that is yours (`2026-11-18`)

1. **Cross-lane edit — `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/DangerAreaNodes.cs` (`EnsureSensor`), CE-3469, backend `e79bacdef`.**
   Under the parallel brain phase the sensor child's creation is deferred to the merge and `Ensure` returns Null. `EnsureSensor` returned
   **Failure** there ⇒ every `DangerCrossing` run ended on its first tick with `HROT_PARALLEL_SIM=1` (the product default). It now returns
   **Success**. ⛔ Running is wrong: the node joins the running path, succeeds next tick, and the exit sweep runs `Deactivate_EnsureSensor`,
   destroying the sensor that just landed (measured). The readers find the sensor by modality, and the run's end releases it by owner stamp.
   ⚠ The same shape may exist in other `StatefulAction` nodes that create a child and have a releasing deactivator — not surveyed.
   Design: `docs/DESIGN_Parallel_Simulation.md` §18.9.
2. **CE-3474 — `SquadManeuverTests.CE4621_TheConductTable_*` red in every env, red at base `0ca3025d6`.** MR-5 (`5df5068d7`) declared
   `BackOut` with `Conduct = LeavesRoute`; the rail exempts only `MineStrike`. Lean: extend the exemption to `BackOut` (the drill's own leg)
   and say so in R-2125 — or drop the flag if a Hold order should not refuse the back-out. Your call; backend changed nothing there.
