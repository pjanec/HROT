# DEBT-TRACKER.md — Logic Packs & Translator Packs Refactoring

> **P1** issues become Corrective Task 0 in the next batch (never stacked here).
> **P2/P3** issues are tracked here with source and target batch.
> When resolved, mark ✅ (do not delete rows).

| ID | Priority | Source | Description | Target Batch | Status |
|----|----------|--------|-------------|--------------|--------|
| DEBT-001 | P3 | PACK-M002 / BATCH-01 | AllInOne mode (`DamageSystem`) does not strip `CanMove` on non-lethal hits. Existing test contract (`Damage_StripsCapabilities_OnLethalHit` Part A) prohibits it. Design gap: AllInOne and Brain/CQRS paths have different non-lethal damage behavior. Future AllInOne parity pass needed if this matters. | TBD | Open |
| DEBT-002 | P3 | PACK-M001 / BATCH-01 | `IReadOnlyList<T>` lacks `FindIndex` — workaround `.ToList().FindIndex(...)` in `CognitiveRuntimeModuleTests`. Minor test ergonomics issue. | TBD | Open |
| DEBT-003 | P3 | PACK-P002 / BATCH-02 | `SimHostModule` constructor now has 9 optional parameters. A builder or options-object pattern would improve readability. Will worsen as more systems are added. | TBD | Open |
| DEBT-004 | P3 | PACK-P002 / BATCH-02 | `SstRequestFinalizationSystem.cs` file contains class `NedRequestFinalizationSystem` — file name mismatch is a maintenance hazard. | TBD | Open |
| DEBT-005 | P3 | General / BATCH-02 | 328 xUnit2013 style warnings (`Assert.Equal` on collection size vs `Assert.Empty/Single`). Adds noise. Could be fixed in a cleanup batch. | TBD | Open |
