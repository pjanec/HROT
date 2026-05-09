# BATCH-24 Review -- Phase 1: Context Menu Decoupling & Marker Components

**Status:** APPROVED with P2/P3 debt items noted
**Build:** 0 errors confirmed (independent verification)
**Tests:** 6/6 new tests pass; 144 pre-existing gizmo regression tests pass

---

## Verification Results

Build was independently re-run and confirmed clean:
```
Build succeeded.
    0 Error(s)
```

---

## What Was Delivered

The developer correctly implemented the complete Phase 1 architecture:

### The Core Architectural Win

The proxy hack (`ExclusiveCaptureProxyTool`) is gone. Both "Rotate entity" context menu
sites in `SimHostVisualization.cs` now follow the data-driven pattern:

**Before (WRONG -- UI owns gizmo lifecycle):**
```csharp
var gizmo = new EntityRotatorGizmo(_repo!, entity, onRemove: () => _gizmoSystem.DeactivateGizmo(entity));
_gizmoSystem.ActivateGizmo(entity, gizmo);
_map.PushTool(new ExclusiveCaptureProxyTool(gizmo));  // UI wraps gizmo as a canvas tool
```

**After (CORRECT -- UI only mutates ECS state):**
```csharp
_repo!.AddComponent<ActiveRotationToolRequest>(entity, default);
_repo!.Bus.Publish(new GizmoComponentActivatedEvent { Entity = entity });
_map.PushTool(new GizmoFocusInputBridge(_repo!.Bus, entity));  // generic bridge, not gizmo-specific
```

The UI no longer knows what gizmo will be created. It adds a marker component and publishes
an event. `DataDrivenGizmoSystem` creates the right gizmo based on registered rules. The
`GizmoFocusInputBridge` is generic -- it translates canvas events to ECS events for ANY
focused gizmo, without knowing which gizmo is active.

### Files Audit

All required files delivered correctly. No unauthorized scope expansion detected.

---

## Issues Found

### P1 (blocking -- NONE for this batch)

No P1 issues. All required work is complete and correct.

### P2 (should fix in next available batch)

**P2-24-001: `_gizmoSystem` field is now dead in `SimHostVisualization`**

The field `_gizmoSystem` (type: `DataDrivenGizmoSystem?`) is still declared and stored
in `SimHostVisualization.cs` (lines 91, 155) but is never read after this batch. The
proxy hack was the only thing using it in this class. The field should be removed along
with the matching `gizmoSystem` parameter from the `Initialize` method signature.

Deferring because `SimHostVisualization.Initialize` is called from `SimHostApp.cs` and
changing the signature requires a coordinated update. No functional impact.

**P2-24-002: Per-frame `List` allocation in DataDrivenGizmoSystem step 1b**

In `Execute()`, step 1b allocates `new List<(Entity entity, int ruleIndex)>()` on every
frame, even when `_activeGizmos` is empty. At 60 Hz this is 60 heap allocations/second.
Should be replaced with a pre-allocated field or, better, only allocate when at least one
mask-violation is found.

**P2-24-003: Step 2 (ConstructionOrder path) does not grant exclusive focus**

When a gizmo matching rule fires from `ConstructionOrder` (step 2), exclusive focus is NOT
granted even if the gizmo requests it. Only step 2b (late-activation path) grants focus.
This is a latent bug: if `ActiveRotationToolRequest` were ever present at entity
construction time (e.g., from a serialized scenario), the gizmo would be created but
frozen (no input routing). Not critical for Phase 1 since the marker is always added
post-construction, but should be harmonized.

### P3 (cosmetic / informational)

**P3-24-001: Misleading comment in step 1b about injected gizmos and RuleIndex**

The comment `// Injected (on-demand) gizmos have RuleIndex == -1; skip them.` is
misleading: injected gizmos live in `_injectedGizmos`, not in `_activeGizmos`. The
`if (gi.RuleIndex < 0) continue;` guard inside step 1b's `_activeGizmos` loop is
therefore defensive dead code. The comment should clarify that all entries in
`_activeGizmos` have a valid RuleIndex >= 0 by design. No functional impact.

**P3-24-002: `using System.Linq` added to DataDrivenGizmoSystem**

The `.Any(gi => gi.RuleIndex == rule.RuleIndex)` call in step 2b introduces a LINQ
dependency and a lambda closure. This is only executed when `GizmoComponentActivatedEvent`
events are present in the bus (rare -- one per user gesture), so GC pressure is negligible.
A future cleanup could use a manual loop to avoid the dependency, but this is not urgent.

**P3-24-003: SC_ER001 assertion verifies fields not Unsafe.SizeOf**

The batch spec asked for `Assert.Equal(0, Unsafe.SizeOf<ActiveRotationToolRequest>())`.
The developer instead checks that the struct has no public fields. Both verify the
zero-payload intent, but a future struct with private fields (e.g., a padding byte) would
fool the fields check while the `Unsafe.SizeOf` check would catch it. Low risk since the
type is unlikely to grow, but the assertion method is slightly weaker than specified.

---

## Phase 1 Compliance Check

Against `old-stuff-erradication.md` Phase 1 requirements:

| Requirement                                            | Status |
|--------------------------------------------------------|--------|
| Delete ExclusiveCaptureProxyTool                       | DONE   |
| Context menus add marker component instead of tool     | DONE   |
| ActiveRotationToolRequest marker component defined     | DONE   |
| EntityRotatorGizmoDefinition registered in registry    | DONE   |
| DataDrivenGizmoSystem activates gizmo on event         | DONE   |
| Gizmo teardown when marker component removed           | DONE   |
| GizmoFocusInputBridge replaces specific proxy          | DONE   |
| Tests covering activation, teardown, bridge events     | DONE   |

Phase 1 is substantially complete. The `_map.PushTool(new GizmoFocusInputBridge(...))` in
the context menu is acknowledged as a deliberate temporary bridge (documented in code)
pending Phase 5 input routing migration.

---

## Debt Tracker Updates

The following items should be added to `DEBT-TRACKER.md`:

| ID          | Priority | Description                                          | Source    | Target |
|-------------|----------|------------------------------------------------------|-----------|--------|
| P2-24-001   | P2       | Remove dead `_gizmoSystem` field from SimHostVis     | BATCH-24  | TBD    |
| P2-24-002   | P2       | Pre-allocate teardown list in DataDrivenGizmoSystem  | BATCH-24  | TBD    |
| P2-24-003   | P2       | Fix missing exclusive-focus grant in step 2 (ConstructionOrder) | BATCH-24 | TBD |
| P3-24-001   | P3       | Clarify/remove misleading RuleIndex < 0 comment      | BATCH-24  | TBD    |
| P3-24-002   | P3       | Replace LINQ `.Any()` with manual loop in step 2b    | BATCH-24  | TBD    |

---

## Next Batch

**BATCH-25** should target **Phase 2** of `old-stuff-erradication.md`:
Purging geometry manipulation tools (`EditTool`, `RouteEditTool`).

The P2-24-001 cleanup (`_gizmoSystem` field removal from `SimHostVisualization`) can be
folded into BATCH-25 as a corrective task since it requires touching `SimHostApp.cs` and
the `Initialize` signature.
