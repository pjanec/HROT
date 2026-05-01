# BTree + HSM Unification — Task Tracker

**Status: WORKSTREAM COMPLETE** — All BHU-001 through BHU-017 delivered and committed.

| Batch | Commits | Tests |
|-------|---------|-------|
| BATCH-01 | FastHSM `0c3a0ec`, FDP `2e3ec8a` | Fhsm.Tests 251/251, Fdp.Toolkits.Tests 776/776 |
| BATCH-02 | FastBTree `bb6e62f`, FastHSM `e56de20`, FDP `2e3ec8a`, root `0dda564` | Fhsm.Tests 257/257, Fbt.Tests 171/171 |
| BATCH-03 | FastHSM `a611020`, FDP `4312d68`, root `4c94b47` | +15 integration tests, E1+E2 pass |

---

## Phase 1 — Unified Hot Reload Coordinator

- [x] BHU-001 — Add Fhsm references to `Hrot.AI.Doctrines.csproj`
- [x] BHU-002 — Add `HsmActionDispatcher.ClearAll()` to `Fhsm.Kernel` (via SourceGen)
- [x] BHU-003 — Build `AiHotReloadCoordinator`
- [x] BHU-004 — Wire `AiHotReloadCoordinator` into `EditorSubsystem`

## Phase 2 — HSM Terminal State Routing

- [x] BHU-005 — Implement `IsFinal` in `Fhsm.Compiler` (`StateNode`, `StateBuilder`, `HsmFlattener`)
- [x] BHU-006 — Implement `StateFlags.IsFinal` → `InstanceFlags.Terminated` in `HsmKernelCore`
- [x] BHU-007 — `HsmTickSystem<T>`: detect `Terminated` + publish `DoctrineFinishedEvent`

## Phase 3 — Cognitive Interrupt Decoupling

- [x] BHU-008 — Create `CognitiveInterruptSystem` (replace `HsmDamageBridgeSystem`, edge-triggered)
- [x] BHU-009 — `HsmTickSystem<T>`: inject interrupt events (no consume — cleanup handles it)
- [x] BHU-010 — Update `CognitiveRuntimeModule` system registration order
- [x] BHU-015 — Create `CognitiveCleanupSystem` (single-frame pulse enforcement)

## Phase 4 — Shared AI Node Attributes

- [x] BHU-011 — Add `SharedAiConditionAttribute`, `SharedAiActionAttribute`, `WritesChannelAttribute` to `Fbt.Kernel`
- [x] BHU-012 — Extend `Fbt.SourceGen` for `[SharedAiCondition]` / `[SharedAiAction]`
- [x] BHU-013 — Extend `Fhsm.SourceGen` for `[SharedAiCondition]` / `[SharedAiAction]`

## Phase 5 — Actuator Channel Safety

- [x] BHU-014 — Channel safety SourceGen thunks (BTree + HSM) + `HsmGraphValidator` enforcement

## Cross-Cutting

- [x] BHU-016 — `DoctrineIngressSystem`: reset `BrainHsm64`/`BrainHsm128` on HSM doctrine assignment

## Integration Tests

- [x] BHU-017 — End-to-end integration tests proving all unified features work together
  - Groups A+B: `BhuIntegrationTests` (7 tests, Fdp.Toolkits.Tests)
  - Groups C+D: `HsmSourceGenIntegrationTests` + `HsmTerminalStateIntegrationTests` (6 tests, Fhsm.Tests)
  - Group E: `HsmDoctrineIntegrationTests` (2 tests, Hrot.ClusterRunner.Integration.Tests)
