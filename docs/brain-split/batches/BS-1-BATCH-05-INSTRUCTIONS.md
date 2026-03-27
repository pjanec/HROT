# BS-1-BATCH-05 Instructions

**Workstream:** BS-1 (Brain / Muscle Node Separation)  
**Estimated Effort:** 10–12 hours  

## Onboarding
If you are new to this workstream, please read:
1. **[DEV-LEAD-GUIDE.md](../guides/DEV-LEAD-GUIDE.md)**: Describes the TDD and CQRS workflow standards.
2. **[BS-1-DESIGN.md](../../docs/brain-split/BS-1-DESIGN.md)**: High-level architecture of the Brain/Muscle split.
3. **[BS-1-TASK-DETAIL.md](../../docs/brain-split/BS-1-TASK-DETAIL.md)**: The definitive specification for the tasks below.

## Tech Debt Items (Complete First)

### TD-9: Translator Cache Lifecycle
- **Problem:** `EntityDamageEgressTranslator` caches published health values in a dictionary that only clears via `Dispose(long)` during network cleanup. In topologies where `CycloneNetworkCleanupSystem` is disabled or fails to run, this cache leaks memory as entities are destroyed.
- **Fix:** Since this is a broader architectural issue with how we dispose of state for destroyed entities outside of the network layer, for this batch simply add a `FdpLog.Debug` or `FdpLog.Warn` inside `Dispose(long)` to trace when it happens, and document this risk near the dictionary definition in the code. We will tackle the larger lifecycle cleanup in a dedicated debt burndown if necessary.
- **Validation:** Visual code review.

---

## Core Tasks

### Phase 4: Node Role Reconfiguration

The CQRS pipelines for combat and detonation are implemented, but right now the topology is incorrect because modules are loaded uniformly and translators aren't wired up. We need to lock this down.

### 1. BS1-T016 Update NodeBootstrapper role assignments
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t016--update-nodebootstrapper-role-assignments](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t016--update-nodebootstrapper-role-assignments)
- **Goal:** Update the `NodeBootstrapper` configuration to strictly enforce which modules belong to Brain vs. Muscle vs. IG vs. IOS. Specifically, `CombatToolkitModule` vs `DamageAssessmentModule`.

### 2. BS1-T017 Register new translators in SimHostApp
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t017--register-new-translators-in-simhostapp](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t017--register-new-translators-in-simhostapp)
- **Goal:** Instantiate and register all the newly created egress and ingress translators from Batches 02–04 into the main translation registry within `SimHostApp`. Be mindful of roles (e.g. Brain only runs specific ingress/egress, Muscle runs others).

---

### Phase 5: Navigation CQRS Compliance

We begin separating navigation intents from physical state mutations, similar to what we did for weapons.

### 3. BS1-T018 Refactor FleeExecutor to use NavigationIntent
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t018--refactor-fleeexecutor-to-use-navigationintent](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t018--refactor-fleeexecutor-to-use-navigationintent)
- **Goal:** Stop mutating `NavState` directly in the brain tier. Make `FleeExecutor` publish a `NavigationIntent` event instead.

### 4. BS1-T019 Refactor FollowRoadGraphExecutor to use NavigationIntent
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t019--refactor-followroadgraphexecutor-to-use-navigationintent](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t019--refactor-followroadgraphexecutor-to-use-navigationintent)
- **Goal:** Refactor `FollowRoadGraphExecutor` to publish a `NavigationIntent` event.

### 5. BS1-T020 Refactor FollowRouteExecutor to use NavigationIntent
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t020--refactor-followrouteexecutor-to-use-navigationintent](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t020--refactor-followrouteexecutor-to-use-navigationintent)
- **Goal:** Refactor `FollowRouteExecutor` to publish a `NavigationIntent` event.

---

## Deliverables
1. Source code implementation of the tasks above.
2. Full unit and integration test coverage for the changes.
3. Write a report at `.dev-workstream/reports/BS-1-BATCH-05-REPORT.md` answering:
   - What challenges did you encounter?
   - Any design gaps or edge cases not covered by the spec?
   - Did you have to introduce any temporary hacks or deviations?