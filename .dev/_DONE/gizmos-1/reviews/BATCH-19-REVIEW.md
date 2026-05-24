# BATCH-19 REVIEW

**Batch:** BATCH-19
**Tasks:** GZ053, GZ054
**Reviewer:** Dev Lead
**Status:** APPROVED

---

## Build

`dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**.
Standalone builds also verified:
- `ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj` — 0 errors (BCL-only)
- `ExtDeps/GizmoMap/GizmoMap.Network/GizmoMap.Network.csproj` — 0 errors

---

## Test Results (verified)

| Task  | Tests Passed | Test Project |
|-------|-------------|--------------|
| GZ053 | 6/6 | `GizmoMap.Contracts.Tests` SC_GZ053_* |
| GZ054 | 5/5 | `GizmoMap.Network.Tests` SC_GZ054_* |
| **Total** | **11/11** | |

---

## Per-Task Review

### GZ053 — Create GizmoMap.Contracts Assembly

APPROVED. The `GizmoMap.Contracts.csproj` correctly targets `net8.0;netstandard2.1` with no
FDP/HROT project references (only a conditional `System.Runtime.CompilerServices.Unsafe` package
for `netstandard2.1`). The assembly boundary is verified at RUNTIME in SC-GZ053-1: the test
loads the assembly, enumerates referenced assembly names, and asserts that none start with
`Fdp.` or `Hrot.`. This is a strong boundary check.

`GizmoPickToken` correctly uses `long AnchorId` (network-stable) instead of ECS `Entity`
(process-local). `IsValid` returns false for zero AnchorId. SC-GZ053-3/4 cover both cases.

SC-GZ053-5 enumerates all 11 shape values (0-10) individually — this directly catches any
omitted enum value. SC-GZ053-6 creates a mock `IGizmoSource` and exercises the builder, verifying
the interface is usable end-to-end.

The report notes that `Entity Anchor` and `PickToken Token` properties from the original
`DebugPrimitive` were removed — correct, as these are ECS types. The new `GizmoPickToken` fills
this role for the network-stable path.

### GZ054 — Create GizmoMap.Network Assembly

APPROVED. `GizmoMap.Network.csproj` references only `GizmoMap.Contracts` and `CycloneDDS.Schema`.
The `CycloneDdsDisableCodeGen = true` property correctly suppresses the IDL code generation that
would otherwise fail (no `.idl` files in this assembly).

All 5 DDS topic structs are present. The `GizmoInteractionBatch` in `GizmoMap.Network` uses
network-stable fields (`PickAnchorId`, `PickSubElementId`, `PickStreamId`) instead of the
ECS-index fields from the original in `Fdp.Diagnostics.Network` — this is correct architectural
evolution. The `Space` field (added in GZ047) is also present.

SC-GZ054-4 verifies at runtime that no type in `GizmoMap.Network` implements `IEcsModuleSystem` —
the test enumerates all assembly types and checks their interface list. This is a strong
boundary check that prevents ECS types from creeping in through future changes.

The report notes an important design decision: `DdsGizmoInteractionPublisher.Publish()` uses an
internal sequence number counter for DDS sample ordering. This is a reasonable addition beyond
the spec for semantic correctness.

P3 observation: The heap allocation in `DdsDebugPrimitivePublisher.Publish()` per frame is noted
as a known limitation. For the Phase 19 goal (assembly boundary establishment) this is acceptable.
The optimization should be tracked for Phase 21+.

---

## Design Alignment

Both assemblies respect the hard constraint: zero dependencies on `Fdp.Core`, `Fdp.ModuleHost`,
or any `Hrot.*` assembly. The solution still builds with 0 errors — existing assemblies
(`Fdp.Diagnostics.Contracts`, `Fdp.Diagnostics.Network`) are unchanged, maintaining full
backward compatibility. The COPY strategy (not MOVE) was the correct choice.

---

## Decision

**APPROVED — BATCH-19 is accepted. Proceed to BATCH-20.**

Tasks marked completed: GZ053, GZ054.
