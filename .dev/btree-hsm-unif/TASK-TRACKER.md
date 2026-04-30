# BTree + HSM Unification — Task Tracker

## Phase 1 — Unified Hot Reload Coordinator

- [ ] BHU-001 — Add Fhsm references to `Hrot.AI.Doctrines.csproj`
- [ ] BHU-002 — Add `HsmActionDispatcher.ClearAll()` to `Fhsm.Kernel` (via SourceGen)
- [ ] BHU-003 — Build `AiHotReloadCoordinator`
- [ ] BHU-004 — Wire `AiHotReloadCoordinator` into `EditorSubsystem`

## Phase 2 — HSM Terminal State Routing

- [ ] BHU-005 — Implement `IsFinal` in `Fhsm.Compiler` (`StateNode`, `StateBuilder`, `HsmFlattener`)
- [ ] BHU-006 — Implement `StateFlags.IsFinal` → `InstanceFlags.Terminated` in `HsmKernelCore`
- [ ] BHU-007 — `HsmTickSystem<T>`: detect `Terminated` + publish `DoctrineFinishedEvent`

## Phase 3 — Cognitive Interrupt Decoupling

- [ ] BHU-008 — Create `CognitiveInterruptSystem` (replace `HsmDamageBridgeSystem`, edge-triggered)
- [ ] BHU-009 — `HsmTickSystem<T>`: inject interrupt events (no consume — cleanup handles it)
- [ ] BHU-010 — Update `CognitiveRuntimeModule` system registration order
- [ ] BHU-015 — Create `CognitiveCleanupSystem` (single-frame pulse enforcement)

## Phase 4 — Shared AI Node Attributes

- [ ] BHU-011 — Add `SharedAiConditionAttribute`, `SharedAiActionAttribute`, `WritesChannelAttribute` to `Fbt.Kernel`
- [ ] BHU-012 — Extend `Fbt.SourceGen` for `[SharedAiCondition]` / `[SharedAiAction]`
- [ ] BHU-013 — Extend `Fhsm.SourceGen` for `[SharedAiCondition]` / `[SharedAiAction]`

## Phase 5 — Actuator Channel Safety

- [ ] BHU-014 — Channel safety SourceGen thunks (BTree + HSM) + `HsmGraphValidator` enforcement

## Cross-Cutting

- [ ] BHU-016 — `DoctrineIngressSystem`: reset `BrainHsm64`/`BrainHsm128` on HSM doctrine assignment
