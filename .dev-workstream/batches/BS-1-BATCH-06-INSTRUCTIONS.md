# BS-1-BATCH-06 Instructions

**Workstream:** BS-1 (Brain / Muscle Node Separation)  
**Estimated Effort:** 10–12 hours  

## Onboarding
If you are new to this workstream, please read:
1. **[DEV-LEAD-GUIDE.md](../guides/DEV-LEAD-GUIDE.md)**: Describes the TDD and CQRS workflow standards.
2. **[BS-1-DESIGN.md](../../docs/brain-split/BS-1-DESIGN.md)**: High-level architecture of the Brain/Muscle split.
3. **[BS-1-TASK-DETAIL.md](../../docs/brain-split/BS-1-TASK-DETAIL.md)**: The definitive specification for the tasks below.

## Tech Debt Items (Complete First)

Please start by resolving these technical debt items accumulated from the previous batch.

### TD-10: NavigationIntent.IntentId Documentation
- **Problem:** The loop reset mechanism in `FollowRouteExecutor` relies on `NavigationIntent.IntentId` incrementing monotonically to signal a reset. This is undocumented.
- **Fix:** Add an XML doc comment to `NavigationIntent.IntentId` explaining this monotonic-id contract.

### TD-11: NavigationIntentBridgeSystem Reset Logic Comment
- **Problem:** `NavigationIntentBridgeSystem` zeroes out `ProgressS` implicitly upon an intent change, without a comment.
- **Fix:** Add a code comment explaining why `ProgressS` is only reset on intent change (to prevent it resetting every tick while running).

### TD-12: FollowRoute Latency Assumption Documentation
- **Problem:** When `FollowRouteExecutor` loops, it increments `IntentId` but correctly resolving it next tick depends on `NavigationExecutionSystem` quickly echoing `NavigationStatus.IntentId`.
- **Fix:** Document this latency and round-trip assumption in both `FollowRouteExecutor` and `NavigationExecutionSystem`.

---

## Core Tasks (Phase 5: Navigation CQRS Compliance)

The final pieces of the Brain/Muscle split involve decoupling any direct state queries in the Brain tier, forcing them to rely on properly shipped intent requests and status responses.

### 1. BS1-T021 Remove NavState poll from Action_Wander
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t021--remove-navstate-poll-from-action_wander](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t021--remove-navstate-poll-from-action_wander)
- **Goal:** The `WanderMilitary` behavior tree nodes (e.g. `Action_Wander`) should not inspect `NavState` (physics input). Ensure they rely solely on `NavigationStatus` or standard action completion paths.

### 2. BS1-T022 Fix MissionDirectorSystem.ReachedDestination + UI generator
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t022--fix-missiondirectorsystemreacheddestination--ui-generator](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t022--fix-missiondirectorsystemreacheddestination--ui-generator)
- **Goal:** `MissionDirectorSystem` currently relies on reading `NavState` (like `FinalDestination` and position checks) to evaluate the `ReachedDestination` mission trigger. Change this to use `NavigationStatus.Result == Arrived` combined with geographic coordinate distance checks or appropriate Brain-level data. Adjust any mission UI generator components to match.

---

## Deliverables
1. Source code implementation of TD-10..TD-12 and BS1-T021..BS1-T022.
2. Full unit test coverage for refactored systems.
3. Write a report at `.dev-workstream/reports/BS-1-BATCH-06-REPORT.md` answering:
   - What challenges did you encounter?
   - Any design gaps or edge cases not covered by the spec?
   - Did you have to introduce any temporary hacks or deviations?