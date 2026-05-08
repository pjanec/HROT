# BATCH-14 Review

**Batch:** BATCH-14
**Reviewer:** Development Lead
**Date:** 2026-05-07
**Status:** ✅ APPROVED

---

## Summary

Both tasks (GZ037–GZ038) implemented. Networked GizmoInteraction DDS translators created,
DebugPrimitivesIngressTranslator added, IgApplication cleaned of ECS system registrations.
81 NED tests pass, 2 new Toolkits tests pass. Build clean (0 errors).

---

## Issues Found

### Issue 1: SystemPhase.PreSimulation does not exist

**File:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressSystem.cs`
**Problem:** Developer used `SystemPhase.BeforeSync` instead of `PreSimulation` because the
latter does not exist in the enum. The intent (run before DataDrivenGizmoSystem in PostSimulation)
is preserved by BeforeSync ordering.
**Impact:** Negligible — BeforeSync runs before Simulation/PostSimulation. The system executes
before gizmo processing as intended.
**Severity:** P3 (track for when/if PreSimulation is added to enum)

---

## Test Quality Assessment

Tests are behaviorally sound:
- SC-GZ037-2: Verifies actual field values on `GizmoInteractionBatch` (Kind, SourceNodeId,
  PickEntityIndex, PickSubElementId, WorldX/Y/Z with precision). Not just Write() was called.
- SC-GZ037-3: Ingress-to-event translation verified by reading typed event from bus with actual
  field values (Token.Target, SubElementId, WorldPos.X).
- SC-GZ037-4: Dead-entity DragUpdate correctly yields CancelEvent and zero DragUpdate events.
- SC-GZ037-7/8: Null reader/writer no-op tests prevent NullReferenceException regressions.
- SC-GZ038-5/7: AppendRaw overflow and content verified with actual assertions.

---

## 📝 Commit Message

```
feat: GizmoInteraction DDS translators + IG dumb terminal ingress (BATCH-14)

Completes TASK-GZ037, TASK-GZ038

GZ037: GizmoInteractionEgressSystem drains local bus events (Started,
  DragUpdate, Commit, Cancel) to GizmoInteractionBatch DDS writes.
  GizmoInteractionIngressSystem reads batches and reconstructs typed events,
  converting DragUpdate for dead entities to CancelEvent.

GZ038: DebugPrimitivesIngressTranslator polls IDdsReader<DebugPrimitivesBatch>
  and calls AppendRaw on the local DebugPrimitiveBuffer. AppendRaw public method
  added to DebugPrimitiveBuffer. IgApplication removes DataDrivenGizmoSystem
  and StatelessGizmoSystem (IG is now a dumb terminal).

Tests: 81 NED tests (round-trip, dead entity, field preservation, null safety),
  2 Toolkits tests (AppendRaw overflow, content verification).
```

---

**Next Batch:** BATCH-15 (already completed)
