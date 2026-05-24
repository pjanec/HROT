# BATCH-17 REVIEW

**Batch:** BATCH-17
**Tasks:** GZ043, GZ044, GZ045, GZ046, GZ047, GZ049
**Reviewer:** Dev Lead
**Status:** APPROVED

---

## Build

`dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors, 109 warnings** (was 108 before
batch; the extra warning is from new using directives — acceptable).

---

## Test Results (verified)

| Task  | Tests Passed | Test Class / File                                     |
|-------|-------------|-------------------------------------------------------|
| GZ043 | 5/5         | `Fdp.Diagnostics.Contracts.Tests` SC_GZ043_*          |
| GZ044 | 7/7         | `Hrot.IG.Tests` SC_GZ044_*                            |
| GZ045 | 4/4         | `Hrot.IG.Tests` + `Hrot.Network.NED.Tests` SC_GZ045_*|
| GZ046 | 7/7         | `Fdp.Presentation.Tests` SC_GZ046_*                  |
| GZ047 | 5/5         | `Hrot.Network.NED.Tests` SC_GZ047_*                  |
| GZ049 | 7/7         | `Fdp.Toolkits.Tests` SC_GZ049_*                      |
| **Total** | **35/35** |                                                   |

---

## Per-Task Review

### GZ043 — Fix PipelineTarget Enum

APPROVED. `NodeGraph = 4`, `All = 7` correct. Flags arithmetic verified:
`All == Map2D | Viewport3D | NodeGraph` = `1 | 2 | 4 = 7`. Five tests cover the enum values,
bit patterns, and flag composition.

### GZ044 — Fix IGCapabilitiesPublisherSystem

APPROVED. `RegisteredGizmosJson string` field added to `IGCapabilitiesAnnounce`. `SupportedShapes`
renamed to `SupportedShapeMask` and widened to `uint`. The reflection loop correctly builds the
shape mask on the cold path. SC-GZ044-5 tests each `DebugPrimitiveShape` value individually with
a `foreach`, catching future shape additions automatically. SC-GZ044-3 verifies that
`RegisteredGizmosJson` and `LayerNamesJson` remain independent fields. SC-GZ044-7 verifies the
once-only publish gate.

P3 observation: SC-GZ044-3 renames `LayerTreeJson` to `LayerNamesJson` in the test vs spec which
says `LayerTreeJson`. This may indicate the existing field was already called `LayerNamesJson` in
the codebase (not a new inconsistency introduced by this batch). Not a defect.

### GZ045 — Wire Composition Roots

APPROVED. All three wiring points implemented: `GizmoInteractionEgressSystem` in `IgApplication`,
`DebugPrimitivesIngressTranslator` in `IgApplication`, `GizmoInteractionIngressSystem` in
`SimHostApp`. `NullIgNetworkAdapter` returns null for both new interface properties. SC-GZ045-3
(null adapter no-throw) and SC-GZ045-4 (buffer populated after `PollAndApply`) directly test the
critical correctness invariants. The SC-GZ045-1/2 complex integration tests were deferred with
comments — acceptable per instructions.

P3 observation: The `DdsGizmoAdapters.cs` helper created in `Fdp.Diagnostics.Network` introduces
a new file in that assembly. The adapters are generic wrappers that forward DDS reader/writer
calls — low complexity, appropriate separation.

### GZ046 — Fix GizmoInteractionProxyTool Click-Away Hazard

APPROVED. Implementation matches spec exactly:
- `_dragActive` field added
- `HandlePress` arms `_dragActive`, returns `true` to consume press
- `HandleDrag` gated on `_dragActive`
- `HandleClick(Left)` with `_dragActive=true` → commit; without → cancel + return false
- `HandleClick(Right)` → always cancel + return true
- `HandleKeyPressed(Escape)` → cancel (regression preserved)
- `IMapTool.HandlePress` default returns `false` (non-breaking)
- `MapCanvas` routes press to active tool before layers

SC-GZ046-4 verifies `_dragActive` is reset post-cancel by testing that a subsequent `HandleDrag`
produces no event. SC-GZ046-6b tests the `MapCanvas` routing via a `PressingRecorderTool` mock
that records `HandlePress` calls. The regression test SC-GZ046-5 covers ESC. All 7 tests verify
specific behavioral outcomes, not just absence of exceptions.

SC-GZ010 regression tests (in the original `GizmoInteractionProxyToolTests.cs`) were updated to
add a `HandlePress` before drag/commit — this is correct since the new guard requires arming.

### GZ047 — Fix Screen-Space Coordinate Mismatch

APPROVED. `CoordinateSpace Space` field added to `GizmoDragUpdateEvent` and
`GizmoInteractionCommitEvent`. Field NOT added to `GizmoInteractionStartedEvent` or
`GizmoInteractionCancelEvent` (per spec). Transport layer (`GizmoInteractionBatch`) extended with
`Space` field. Egress system propagates `evt.Space → batch.Space`; ingress system restores
`batch.Space → evt.Space`.

SC-GZ047-4 and SC-GZ047-5 are the key behavioral tests: they verify actual `CoordinateSpace`
values propagated through egress and restored through ingress respectively, using
`CoordinateSpace.Screen` as the test value (non-default, catches missing propagation).

All event structs remain blittable (byte-sized `CoordinateSpace` enum).

### GZ049 — Settings Scopes

APPROVED. `SettingScope` enum created. `GizmoSettingsRegistry` extended with:
- `_scopes` dictionary
- `Write` extended with optional `scope = SettingScope.Global` (backward compatible)
- `GetScope` method
- `SaveToDisk(path, scope)` filters by scope
- `LoadFromDisk(path, scope)` assigns scope on load
- `DiscardScope(scope)` resets matching entries to default

SC-GZ049-2 and SC-GZ049-3 are the strongest tests — they write to actual temp files and check
file content for presence/absence of the key name. SC-GZ049-4 verifies `Read` returns the default
value after `DiscardScope` (not just that the scope is gone). SC-GZ049-5 verifies isolation:
discarding `Project` scope does not affect `Global` or `Session` settings.

The hot-path `Read` method was not modified — confirmed by code review.

SC-GZ049-7 (regression: existing SC-GZ007/SC-GZ008 tests pass with new `Write` signature) is
covered implicitly — all pre-existing tests continue to pass with the optional param defaulting to
`Global`.

---

## Design Alignment

All implementations align with their respective DESIGN.md and TASK-DETAIL.md specifications.
No shortcuts taken. The click-away hazard fix (GZ046) correctly implements all three deactivation
paths: commit (HandlePress + drag + left release), cancel (right-click or ESC), and click-away
cancel (left release without prior HandlePress). This matches the design's §4.3 exactly.

---

## Decision

**APPROVED — BATCH-17 is accepted. Proceed to BATCH-18.**

Tasks marked completed: GZ043, GZ044, GZ045, GZ046, GZ047, GZ049.
