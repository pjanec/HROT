# Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-001 | BATCH-01 | Generators cannot reference Hrot.Blueprints.Core (netstandard2.0 cannot ref net8.0). Generator only checks `{` start. Full schema validation needs a shared netstandard2.0 contracts assembly. | P3 | BATCH-07+ | OPEN |
| DEBT-002 | BATCH-01 | WorldResetTests.WorldResetEvent_IsPlainClass calls Assert.NotNull on a struct (xUnit2002). Suppressed with NoWarn. Test is logically pointless; fix in housekeeping pass. | P3 | BATCH-07+ | OPEN |
| DEBT-003 | BATCH-03 | `BreakpointKey(string NodeId)` record specified in TASK-TH-008 but not implemented. Breakpoints use raw strings. No functional impact in Slice 1. | P3 | BATCH-07+ | OPEN |
| DEBT-004 | BATCH-03 | `IBlueprintDebugSession` event named `OnPinValueChangedEvent` instead of `OnPinValueChanged` (design name) due to C# conflict with generic method of same base name. Deliberate deviation; add comment in source. | P3 | BATCH-07+ | OPEN |

| DEBT-005 | BATCH-04 | `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` only asserts ALCs are live, never tests reclaim-after-unload. SC3 of TASK-TH-005 is not covered. Fix: load 3 ALCs, manually unload first 2, ForceGcReclaim, assert first two are dead. | P2 | BATCH-05 | RESOLVED (BATCH-05 CT0) |
| DEBT-006 | BATCH-04 | `SnapshotAllBlackboards()` absent from BlueprintTestFixture (TASK-TH-003 spec). Defer until TASK-RT-004 provides real partitions. | P3 | BATCH-07+ | OPEN |
| DEBT-007 | BATCH-04 | `SetChannelStatus<TChannel>()` absent from BlueprintTestFixture (TASK-TH-003 spec). Defer until channel types exist in Phase 5. | P3 | BATCH-07+ | OPEN |
| DEBT-008 | BATCH-04 | `GetSlotEntry(BlueprintAsset, Entity)` absent from BlueprintTestFixture (TASK-TH-003 spec). Defer until TASK-RT-004. | P3 | BATCH-07+ | OPEN |
| DEBT-009 | BATCH-04 | Dev insight: Debug JIT keeps all locals alive for method scope -- any ALC-touching temp in a test method pins the ALC. Always isolate ALC ops in [NoInlining] helpers. | P3 | ongoing | OPEN |
| DEBT-010 | BATCH-04 | Dev insight: `WeakReference<T>.TryGetTarget(out _)` creates a strong ref via the out slot. Use non-generic `WeakReference.IsAlive` in GC reclaim loops. | P3 | ongoing | OPEN |

| DEBT-011 | BATCH-05 | Assembly objects from `LoadTestAssemblyFromBytes` (even discarded) are kept alive by Debug JIT as implicit stack locals, preventing ALC GC. Fix: isolate ALL ALC loading calls in `[NoInlining]` helpers. Extends DEBT-009. | P3 | ongoing | OPEN |
| DEBT-012 | BATCH-05 | `HsmActionDispatcher` is a `static class` (not singleton instance). TASK-TH-010 design anticipated `HsmDispatcher { get; }` property on fixture -- not implementable in C#. ClearAll() called statically in Dispose(). Future design docs should document this. | P3 | BATCH-07+ | OPEN |

| DEBT-013 | BATCH-06 | `BlueprintDispatchKind` enum exists in both `Hrot.Blueprints.Core.Assets` and `Fdp.Toolkit.Blueprints`. Required because cross-assembly reference is impossible. Both should carry a comment: "Mirror of [other namespace] -- kept in sync manually." | P3 | BATCH-07+ | RESOLVED (BATCH-07 CT0-C) |
| DEBT-014 | BATCH-06 | `BlueprintSlotEntry.StructureHash` is `uint` (not `ulong` per spec) due to 16-byte struct budget. When used in `BlueprintTickSystem` reload detection, compare via `slot.StructureHash != (uint)def.StructureHash` with an explicit comment. Add XML doc note to the field. CT0 in BATCH-07. | P2 | BATCH-07 CT0 | RESOLVED (BATCH-07 CT0-A) |
| DEBT-015 | BATCH-06 | `BlueprintDefinition.StateFields` is `IReadOnlyList<BlueprintFieldDescriptor>` instead of `IReadOnlyDictionary<string, BlueprintFieldDescriptor>` per spec. Must be corrected before `BlueprintStateView.GetField` is implemented (Phase 2 systems). CT0 in BATCH-07. | P2 | BATCH-07 CT0 | RESOLVED (BATCH-07 CT0-B) |

| DEBT-016 | BATCH-15 review | `CompileAndLoadMany`, `SimulateReload`, `SimulateQuickReload`, `SimulateReloadWithThrowingRegistrar`, `SimulateReloadFromAlc` in `BlueprintTestFixture` not marked `[NoInlining]` — Debug JIT can inline them into the test body and pin their ALC locals for the body's lifetime. Extends DEBT-011. | P1 | BATCH-16 CT0-A | OPEN |
| DEBT-017 | BATCH-15 review | `FailedReload_DoesNotLeakNewAlc` body checks `liveAlcs == 1` while `ex` (exception with `InnerException.TargetSite` pointing into failed ALC) is alive. Isolate `Record.Exception` + assertion into a `[NoInlining]` helper so `ex` goes out of scope before the GC check. | P1 | BATCH-16 CT0-B | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
