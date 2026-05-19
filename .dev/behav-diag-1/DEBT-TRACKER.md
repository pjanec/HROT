# Behavior Diagnostics — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-NOTE-1 | Design review | User preferred `DebugState` in `Hrot.Common`, but `Fdp.Toolkits` does not reference `Hrot.Common` (verified in csproj). Design relocated `DebugState` + patch infrastructure to `Fdp.Toolkit.Behavior.Diagnostics` to honor the existing layer direction. If a future requirement demands keeping `DebugState` in `Hrot.Common`, the FDP tick systems would need a non-trivial abstraction (interface in FDP layer, impl in Hrot layer). | P3 | (none — informational) | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
