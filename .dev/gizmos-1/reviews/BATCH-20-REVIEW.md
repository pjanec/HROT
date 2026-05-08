# BATCH-20 REVIEW

**Batch:** BATCH-20
**Tasks:** GZ055, GZ056
**Reviewer:** Dev Lead
**Status:** APPROVED (with one P2 observation)

---

## Build

`dotnet build IOS-IG-SimHost.sln --no-incremental` -> **0 errors** (verified locally).
Warnings are pre-existing xUnit2017 issues in `Hrot.ClusterRunner.Tests`, unrelated to this batch.

---

## Test Results (verified)

| Task  | Tests Passed | Test Project |
|-------|-------------|--------------|
| GZ055 | 6/6 | `GizmoMap.Presentation.Tests` SC_GZ055_* |
| GZ056 | 6/6 | `GizmoMap.Example.Tests` SC_GZ056_* |
| **Total** | **12/12** | |

---

## Per-Task Review

### GZ055 — Create GizmoMap.Presentation Assembly

APPROVED. The assembly boundary constraint is correctly enforced: no `Fdp.Core`,
`Fdp.ModuleHost`, or `Hrot.*` references. SC-GZ055-1 verifies this at runtime via
`Assembly.GetReferencedAssemblies()`. SC-GZ055-4 verifies that no ECS production
systems (`DataDrivenGizmoSystem` etc.) are present by name-checking all assembly types.

**Two-pass SpatialAnchor resolution (SC-GZ055-2):** The renderer performs a correct
two-pass sweep:
- Pass 1 builds `Dictionary<long, SpatialAnchorEntry>` keyed by `NetworkId`.
- Pass 2 resolves `EntityLocal` primitives using 2D rotation from `AnchorYawRad = prim.Heading * DegToRad`.

The test constructs a SpatialAnchor at `(100, 200)` and a sphere at local `(0, 0, 0)` in
`EntityLocal` space, then asserts the sphere is dispatched at `(100, 200)`. This is a
functional correctness test, not just a smoke test.

**SemanticShape rendering (SC-GZ055-3):** The test verifies that a SemanticShape primitive
reaches `DispatchShape` (via `CapturingRenderer`) when the registry is null. However, since
`DispatchShape` is overridden in the test double, the actual magenta fallback draw call
(`Raylib.DrawCircleLines(..., Color.Magenta)`) is never exercised. The test validates the
`Color.Magenta` constant rather than the execution path.

**P2 observation:** SC-GZ055-3 is structurally weak — it proves the shape reaches dispatch
but does not prove the fallback color is wired correctly. A stronger test would use a
`FallbackCapturingRenderer` that records whether the magenta path was taken (e.g., by
overriding `DispatchShape` and inspecting `_semanticRegistry == null`). This is not a
blocking issue since the code review confirms the correct `Color.Magenta` branch at lines
270-273 in `DebugPrimitiveRenderer2D.cs`, but should be strengthened in a follow-up.

**GizmoInteractionProxyTool (SC-GZ055-5):** The callback delegate replaces `FdpEventBus`
cleanly. The test verifies the `Started` event fires in the constructor (not on `OnEnter`),
`Press` arms the drag, and `Drag` triggers a `DragUpdate` callback. The click-away cancel
logic from GZ046 is retained.

**MilStd2525 affiliation color mapping (SC-GZ055-6):** The static helper
`MilStd2525Renderer.GetAffiliationColor` correctly maps SIDC character[1]:
`'F'` -> Blue, `'H'` -> Red, `'N'` -> Yellow, else Green. Tests verify all four paths.

**Field name deviations noted in report:**
- `SpatialNetworkId` -> `NetworkId` (correct per actual struct)
- `AnchorYawRad` -> `Heading` in degrees (correctly converted to radians in renderer)
- `SemProfileId` -> `ProfileId`
- `SemConditionMask` -> `ConditionMask`

These were documentation gaps in the batch instructions, not implementation errors. The
developer correctly read the actual `DebugPrimitive.cs` struct definition.

**SemanticShape world-position encoding in EntityLocal:** The resolved world position for
EntityLocal SemanticShape primitives is stored in spare fields (`Pitch` for X,
`InspOffsetY` for Y). This is a pragmatic workaround for the fixed 64-byte struct layout
with no spare float fields in the SemanticShape offset region. The developer documented
this clearly. This is acceptable for Phase 19; a cleaner solution (e.g. a dedicated `ResolvedX/Y`
union alias) can be pursued in Phase 21.

### GZ056 — Unified Example Application

APPROVED. `GizmoMap.Example` correctly references only `GizmoMap.Contracts`,
`GizmoMap.Network`, and `GizmoMap.Presentation` — SC-GZ056-3 verifies this at runtime.
SC-GZ056-4 verifies `IGizmoTransport` is defined in `GizmoMap.Contracts` (not in Example).

**DemoSceneGenerator** emits all 13 required primitive types across both WorldMeters and
ScreenPixels size modes. The Damaged bit toggles at 2-second intervals via
`((int)(t / 2f) & 1) != 0`. SC-GZ056-6 exercises this by emitting at t=0.5s (undamaged)
and t=2.5s (accumulated from two Emit calls with deltaTime=0.5 + 2.0 = 2.5s).

**LocalGizmoTransport**: correct in-process copy with no CycloneDDS. `Dispose()` is a no-op.

**`LocalDrawBuilder` / `IDebugDrawBuilder`:** The generator casts the `IDebugDrawBuilder`
to `LocalDrawBuilder` internally, which couples the generator to the concrete builder type.
This is a minor design smell for a demo — the interface was intended to be the abstraction
boundary. However, the design note in TASK-DETAIL.md acknowledges that
`IDebugDrawBuilder`'s high-level methods must be implemented as `EmitRaw` calls, and the
cast is limited to the example project only. Acceptable for this phase.

**Headless mode** (`--headless` flag): documented as running 30 frames. Tests exercise the
headless path through `LocalGizmoTransport` without opening Raylib windows, which is the
correct CI approach.

---

## Design Alignment

Both assemblies respect the COPY strategy (originals in `Fdp.Presentation` untouched) and
the hard dependency boundary. The solution builds with 0 errors. The GizmoMap stack is
now self-contained: `GizmoMap.Contracts` -> `GizmoMap.Network` -> `GizmoMap.Presentation`
-> `GizmoMap.Example`, with no FDP/HROT leakage at any level.

---

## Decision

**APPROVED — BATCH-20 is accepted. Proceed to BATCH-21.**

Tasks marked completed: GZ055, GZ056.

**Carry-forward for BATCH-21+:**
- Strengthen SC-GZ055-3: verify the magenta execution path (not just the constant).
- Consider a `ResolvedX/Y` union alias in `DebugPrimitive` to eliminate the `Pitch`/
  `InspOffsetY` encoding hack for SemanticShape EntityLocal resolution.
