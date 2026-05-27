# BATCH-02 Report

**Batch:** BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2025-07-24  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| Corrective T0 | [x] | `NoneIntent_HaltsNavigation_NavStateSetToNone` — renamed + assertions fixed (prior session) |
| NAV-P0-T4a | [x] | `NavigationConstants.cs` — `ActionIdFollowRoadGraph` deprecated; ActionIds 6-9 added |
| NAV-P0-T4b | [x] | `NavigationActions.cs` — `MoveToParams` extended to 32B; 4 new 32B param structs added |
| NAV-P0-T4c | [x] | `NavigationComponents.cs` — `NavigationResult` extended (InProgress..FailedInvalidHandle=7); `NavigationPhase`, `TraversalKind`, `SurfaceType` enums added; `NavigationIntent.RouteHandle` added; `NavigationStatus` extended with Phase/LastFailureReason/ReplanCount/RouteHandle/EstimatedTimeRemaining/NavmeshVersionObserved |
| NAV-P0-T5 | [x] | `NavAgentProfile`, `NavigationCorridorMuscle`, `NavigationCorridorPreview`, `NavigationPathDetailsBuffer`, `CrowdAgent` components with ComponentIds 69-73; `NavWaypoint` 24B struct; `PreviewWaypoint` 16B struct |
| DDS Descriptors | [x] | `SimDescriptors.cs` — `ENavigationResult` extended (4 new values); `NavigationIntent.RouteHandle`; `NavigationStatus` new fields |
| Egress Translators | [x] | `NavigationIntentEgressTranslator.cs` — `RouteHandle` forwarded; `Mode=None` guard added |
| Egress Translators | [x] | `NavigationStatusEgressTranslator.cs` — 4 new fields forwarded; change-detection key adds `Phase`; `MapResult` covers 4 new results |
| Ingress Translators | [x] | `NavigationStatusIngressTranslator.cs` — 4 new fields mapped; `MapResult` covers 4 new results |
| MoveToExecutor | [x] | `RouteHandle` copied from params to intent; 3 new failure result cases added |
| Tests — Actions | [x] | 5 new struct-size tests; `NavigationResult_NewValuesNotColliding`; `AllNavigationActionIds_AreDistinct` updated with IDs 6-9 and `#pragma warning disable CS0618` |
| Tests — Contracts | [x] | 8 new tests: sizes (NavWaypoint/PreviewWaypoint/NavigationCorridorPreview), ComponentId uniqueness/range, distinctness, RouteHandle default, Phase default |
| Tests — Egress | [x] | `RouteHandle_IsIncludedInPublishedSample` added; pre-existing `ModeNone_NeverPublished` and `NewCommand_PublishedExactlyOnce` failures fixed |
| `NavigationContractsComponentIds.cs` | [x] | Constants 69-73 added with reserved range comment |

---

## Testing Results

**Unit Tests — `Fdp.Toolkits.Tests` (Navigation filter):** 79 / 79  
**Unit Tests — `Hrot.Map.Common.Tests` (NavigationIntent filter):** 7 / 7  
**Full solution build:** 0 errors, 0 warnings (navigation code); 9 pre-existing unrelated warnings in Hrot.Blueprints.Tests

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three distinct issues arose during implementation:

1. **CycloneDDS IDL flat-namespace collision** — The code generator collects all enum value names from a module into a single flat namespace. `SurfaceType.None` collided with the pre-existing `NavigationMode.None=0`, producing an IDL parse error. `SurfaceType.Default` hit a different problem: `default` is an IDL reserved keyword. Resolution: renamed the zero value to `SurfaceType.Generic`, which is semantically accurate (unmapped/generic surface) and avoids both collisions.

2. **Missing namespace closing brace** — The `CrowdAgent` struct was added at the very end of `NavigationComponents.cs` in the previous session. A `}` closing the outer `namespace Fdp.Toolkit.Navigation` was truncated. Detected via CS1513 build error, fixed by appending the missing brace.

3. **Pre-existing `ModeNone_NeverPublished` / `NewCommand_PublishedExactlyOnce` failures** — Both tests assert that `NavigationIntentEgressTranslator` must not publish intents when `Mode=None`. The translator lacked this guard — any entity with an unrecorded `IntentId` (including brand-new entities) would be published regardless of mode. Added an early `continue` when `intent.Mode == EcsNavMode.None` before the fine-grained change-detection logic. The fix is minimal and correct: `None` means the entity has no active navigation command.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `NavigationStatusEgressTranslator` uses `ProgressS` in the publish payload but explicitly excludes it from the change-detection key (heartbeat every 300 ticks compensates). This is a reasonable tradeoff but the heartbeat interval is hard-coded and not configurable. A configurable injected value would be better for testing.
- `MoveToExecutor.Execute` now has two separate `case` groups both mapping to `NodeStatus.Failure` (original FailedBlocked/FailedUnreachable, and new NoPath/FailedNoLayer/FailedInvalidHandle). They could be merged into one fall-through group, but the current separation makes the original vs. new results visually distinct — kept as-is per the implementation discipline requirement to minimize changes.

**Q3: What design decisions did you make beyond the instructions? How did you resolve them?**

- The `NavigationStatusEgressTranslator` change-detection dict key was extended from `(IntentId, Result)` to `(IntentId, Result, Phase)` rather than adding `Phase` as a separate condition. This keeps the anti-stutter logic in one place and ensures a `Phase` transition (e.g., Planning→Executing) is faithfully re-published even if the Result hasn't changed yet.
- Added a `using NavigationPhase = Fdp.Toolkit.Navigation.NavigationPhase;` alias to `NavigationStatusIngressTranslator.cs` rather than using the fully-qualified name in the cast, to match the existing alias style in the file.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- A zero-initialised `NavigationStatus.Phase` defaults to `NavigationPhase.Idle = 0`, which is correct. The test `NavigationStatus_Phase_DefaultIsIdle` validates this explicitly.
- `RouteHandle = 0` is a valid "no route" sentinel (same as the default) — the test `NavigationStatus_RouteHandle_DefaultIsZero` confirms the zero-init contract.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `NavigationStatusEgressTranslator` iterates all entities with `EcsNavigationStatus` every tick (full scan, not delta). If the entity count grows large this could become expensive. The change-detection cache mitigates DDS publish cost but not the iteration cost. A `QueryDelta`-based approach (as used in `NavigationIntentEgressTranslator`) would be a natural follow-up.

---

## Outstanding Issues / Next Steps

- None blocking. All tasks in BATCH-02 are complete and all targeted tests pass.
- The hard-coded `ProgressHeartbeatInterval = 300` in `NavigationStatusEgressTranslator` could be made injectable for easier testing (non-blocking tech debt).
- `MoveToExecutor` still has two separate groups of `Failure` cases — could be merged in a later cleanup batch.
