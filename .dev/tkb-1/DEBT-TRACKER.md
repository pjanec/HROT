# TKB — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-001 | BATCH-02 | `TryGetDescriptor<T>` struct overload has no direct test — all current DTOs are reference types. | P3 | BATCH-06 | OPEN |
| D-002 | BATCH-02 | `NedTkbBuilder.WithHeavyMemory` is now a no-op (Blackboard1024 dropped until Phase 6). SC-HA014 tests deleted; behavior to be restored via translator in TKB-014. | P2 | BATCH-05 | OPEN |
| D-003 | BATCH-02 | `UrbanAmbushIntegrationTests` and `ScenarioDirector` integration tests fail because entity ECS component application was removed. Will be restored by TKB-014 (Phase 6 translators). | P2 | BATCH-05 | OPEN |

| D-004 | BATCH-03 | `TkbDescriptorRegistry.TryGetParser` allocates one `string` per property name on the deserializer hot path (net8.0 limitation). Upgrade to `GetAlternateLookup<ReadOnlySpan<char>>()` when targeting .NET 9+. | P3 | Future | OPEN |
| D-005 | BATCH-03 | `ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap` LOH test is a heuristic (GC measurement can vary); 85,000-byte threshold is very conservative. | P3 | N/A | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
