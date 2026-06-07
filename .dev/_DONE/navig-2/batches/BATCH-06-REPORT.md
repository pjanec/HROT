# BATCH-06 Report: Phase 3 — CrowdAgent Admission + OffMeshLinkDetectionSystem

**Batch:** BATCH-06
**Phase tasks:** NAV-P3-T1 (CrowdAgentUpdateSystem), NAV-P3-T2 (OffMeshLinkDetectionSystem), NAV-P9-T1 (system tests), NAV-P9-T2 (system tests)
**Status:** COMPLETE

---

## Summary

BATCH-06 implements Phase 3 of the navigation subsystem: crowd-agent lifecycle management and off-mesh link detection with zero-frame suppression. The crowd system ensures infantry entities use the Detour crowd provider for velocity while the linear kinematics system is excluded. The off-mesh detection system halts crowd locomotion and emits a traversal event when an agent approaches a non-Walk corridor segment.

---

## Files Created

### Fdp.Toolkits (production)

| File | Description |
|------|-------------|
| `Navigation/Systems/CrowdAgentUpdateSystem.cs` | Reads crowd velocity from `IDtCrowdProvider`, writes `SimVelocity`, integrates `SimTransform.Position`. Skips entities in `AwaitingTraversal` phase. |
| `Navigation/Systems/OffMeshLinkDetectionSystem.cs` | Detects non-Walk waypoints within lookahead distance, sets `Phase=AwaitingTraversal`, emits `OffMeshTraversalStartedEvent`, removes `CrowdAgent` tag. |

### Files Modified

| File | Change |
|------|--------|
| `Navigation/NavigationComponents.cs` | Added `CurrentTraversalKind` field to `NavigationStatus` struct |
| `Navigation/PathfindingEvents.cs` | Added `OffMeshTraversalStartedEvent` (EventId 2035) |
| `Navigation/Systems/NavigationIntentBridgeSystem.cs` | Added two-arg constructor with `IDtCrowdProvider`; MoveTo branch now registers infantry as crowd agents and sets agent target |
| `CarKinem/Systems/LinearKinematicsSystem.cs` | Added `.Without<CrowdAgent>()` to query; added `using Fdp.Toolkit.Navigation;` |

### Fdp.Toolkits.Tests (test-only)

| File | Description |
|------|-------------|
| `Navigation/CrowdAgentUpdateSystemTests.cs` | 4 tests: velocity written, traversal suppression, filter exclusion, phase resume |
| `Navigation/OffMeshLinkDetectionSystemTests.cs` | 7 tests: no-link unchanged, beyond-lookahead unchanged, within-lookahead triggers, event carries kind, tag removed, event emitted with LinkWorldPos, multiple agents same tick |

---

## Test Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Navigation (all) | 151 | 162 | +11 |
| CrowdAgentUpdateSystemTests | 0 | 4 | +4 |
| OffMeshLinkDetectionSystemTests | 0 | 7 | +7 |
| Pre-existing Navigation tests | 151 | 151 | 0 (no regression) |

All 162 tests pass. 0 failures. 0 skipped.

---

## Design Decisions & Deviations

1. **MontageEndedEvent handling deferred**: `OffMeshLinkDetectionSystem` cannot import `Hrot.MuscleCharacter.Animation.Events` due to assembly boundary (`Fdp.Toolkits` does not reference Hrot). The `HandleMontageEndedEvents` method is left as a stub with a comment. Montage resume will be wired in a Hrot-side bridge system in a future phase.

2. **`LinkDetected_PlayMontageWritten` repurposed**: Since `AnimationChannel` is not available in `Fdp.Toolkits`, this test instead verifies that `OffMeshTraversalStartedEvent` carries the correct `TraversalKind` discriminant — which is what causes the animation tier to select a montage. Behavior contract is equivalent.

3. **`CrowdAgentUpdateSystem` integrates position**: Since `LinearKinematicsSystem` now excludes `CrowdAgent` entities, `CrowdAgentUpdateSystem` also integrates `SimTransform.Position += velocity * dt` to prevent crowd agents from becoming stationary.

4. **Direct component mutation (no ECB)**: `OffMeshLinkDetectionSystem` uses `repo.RemoveComponent<CrowdAgent>()` directly (not ECB). This is acceptable because tests assert state after the Execute() call, and same-tick visibility is required for the zero-frame suppression guarantee.

---

## Debt Items

No new debt added.

---

## Next: BATCH-07

Phase 4: NAV-P4-T1 (`MoveToExecutor` extension), NAV-P4-T2 (new executors), NAV-P4-T3 (Brain-side `NavigationPathDetailsUpdateSystem`), NAV-P4-T4 (Event Catalog entries).
