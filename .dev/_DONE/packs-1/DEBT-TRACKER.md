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
| DEBT-006 | P2 | PACK-P001 / BATCH-03 | `MissionControlRequestSystem` still exists in codebase but is no longer wired. Must be deleted to avoid confusion. | BATCH-04 | ✅ Resolved |
| DEBT-007 | P3 | PACK-P001 / BATCH-03 | `view as EntityRepository` cast in `MissionControlIngressTranslator` (and `EntityMissionIngressTranslator`) — silently no-op if view is wrapped. `ISimulationView` should expose `Bus`/`PublishManagedEvent`. | TBD | Open |
| DEBT-008 | P3 | PACK-P001 / BATCH-03 | `[EventId]` collision has no compile-time guard — only fails at runtime. A test enumerating all registered event type IDs and asserting uniqueness would catch it. | TBD | Open |
| DEBT-009 | P3 | PACK-P001 / BATCH-03 | `IDescriptorTranslator.Dispose(long)` contract undocumented. New bus-bridge translators implement as no-op (correct) but no guidance exists. | TBD | Open |
| DEBT-010 | P3 | PACK-C002 / BATCH-04 | `OrchestratorSubsystem.Update()` bridges `SwitchTimeModeEvent` between two buses per-frame. Could be eliminated by unifying buses. Low priority. | TBD | Open |
| DEBT-011 | P3 | PACK-C002 / BATCH-04 | `OrchestrationObserverTranslator.Tick()` parses JSON (asset inventory) every frame even if unchanged. Version/hash check could short-circuit. Not on hot path. | TBD | Open |
| DEBT-007 | P3 | PACK-P001 / BATCH-03 | `view as EntityRepository` cast in `MissionControlIngressTranslator` (and `EntityMissionIngressTranslator`) — silently no-op if view is wrapped. `ISimulationView` should expose `Bus`/`PublishManagedEvent`. | TBD | Open |
| DEBT-008 | P3 | PACK-P001 / BATCH-03 | `[EventId]` collision has no compile-time guard — only fails at runtime. A test enumerating all registered event type IDs and asserting uniqueness would catch it. | TBD | Open |
| DEBT-009 | P3 | PACK-P001 / BATCH-03 | `IDescriptorTranslator.Dispose(long)` contract undocumented. New bus-bridge translators implement as no-op (correct) but no guidance exists. | TBD | Open |
