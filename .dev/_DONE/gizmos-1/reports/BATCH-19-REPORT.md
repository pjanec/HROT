# BATCH-19 Report

**Batch:** BATCH-19
**Developer:** Agent
**Date:** 2025-07-25
**Status:** Complete

---

## Task Completion

| Task ID    | Status | Notes |
|------------|--------|-------|
| TASK-GZ053 | [x]    | GizmoMap.Contracts created; 6/6 tests pass |
| TASK-GZ054 | [x]    | GizmoMap.Network created; 5/5 tests pass |

---

## Testing Results

**Unit Tests Passed:** 11 / 11 (6 for GZ053, 5 for GZ054)
**Integration Tests Passed:** N/A

**Key Test Scenarios Verified:**

GizmoMap.Contracts (SC-GZ053-1 through SC-GZ053-6):
- [x] SC-GZ053-1: Assembly references no Fdp.* or Hrot.* assemblies
- [x] SC-GZ053-2: Marshal.SizeOf<DebugPrimitive>() == 64
- [x] SC-GZ053-3: GizmoPickToken.IsValid true when AnchorId != 0
- [x] SC-GZ053-4: GizmoPickToken.IsValid false when AnchorId == 0
- [x] SC-GZ053-5: DebugPrimitiveShape enum contains values 0-10 inclusive
- [x] SC-GZ053-6: IGizmoSource mock is callable with IDebugDrawBuilder

GizmoMap.Network (SC-GZ054-1 through SC-GZ054-5):
- [x] SC-GZ054-1: Assembly references no Fdp.* or Hrot.* assemblies
- [x] SC-GZ054-2: DebugPrimitivesBatch has public fields FrameNumber, NodeId, Primitives
- [x] SC-GZ054-3: EntityAttributeSchema has NodeId (int) and SchemaJson (string)
- [x] SC-GZ054-4: No type in GizmoMap.Network implements IEcsModuleSystem
- [x] SC-GZ054-5: DdsDebugPrimitivePublisher constructor succeeds and Publish works with empty buffer

Both projects added to IOS-IG-SimHost.sln under ExtDeps/GizmoMap solution folder.
Full solution build: 0 errors, 110 warnings (all pre-existing).

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The main challenge was stripping ECS dependencies from copied types without breaking the GizmoMap.Contracts assembly boundary.

- `DebugPrimitive` had `Entity Anchor` and `PickToken Token` properties that depend on Fdp.Core. These were removed entirely; the new `GizmoPickToken` struct in GizmoMap.Contracts carries the equivalent network-stable fields (long AnchorId, uint SubElementId, uint StreamId).
- `IDebugDrawBuilder` had three `DrawEntity*` overloads depending on `Entity`. These were stripped; all remaining draw methods are pure geometry with no ECS types.
- `DebugPrimitiveBuffer` had entity-dependent helpers that were removed identically.
- `FixedString32` was moved into the GizmoMap.Contracts namespace (Fdp.Toolkit.Diagnostics.Gizmos) and the `System.Runtime.CompilerServices.Unsafe` NuGet was conditionally added for the netstandard2.1 target to support pointer arithmetic.

For GizmoMap.Network, the `GizmoInteractionBatch` DDS topic struct replaces the ECS entity index/generation fields with the network-stable fields from `GizmoPickToken` (PickAnchorId, PickSubElementId, PickStreamId).

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The original `DebugPrimitive` struct has a 64-byte fixed layout with a tagged union but no validation that fields remain within their allocated bytes when new shapes are added. The `DebugPrimitiveShape` enum now goes up to value 10 (SpatialAnchor) which implies distinct payload interpretations -- those interpretations are encoded in factory methods but there is no runtime assertion that new factories respect the 64-byte contract.

**Q3: What design decisions did you make beyond the instructions? How did you handle namespaces?**

Namespace decision: all GizmoMap.Contracts types that are copies of FDP originals use namespace `Fdp.Toolkit.Diagnostics.Gizmos` (same as the originals). This provides source-level compatibility for code consuming both assemblies simultaneously and avoids dual-namespace friction when connecting GizmoMap.Network back to the contracts layer.

GizmoMap.Network types use namespace `GizmoMap.Network`, which reflects the assembly boundary cleanly.

The `DdsDebugPrimitivePublisher.Publish()` signature was extended with `frameNumber` and `nodeId` parameters (not specified in the task) because the DDS topic struct requires them as key fields; publishing without providing them would produce semantically incorrect DDS samples.

`DdsGizmoInteractionPublisher.Publish()` uses an internal `uint _sequenceNumber` counter so that successive calls produce monotonically increasing sequence numbers, enabling receivers to detect lost samples.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `CycloneDdsDisableCodeGen` must be set to `true` in GizmoMap.Network even though no `.idl` files are present, because the CycloneDDS.targets file unconditionally registers a code generation build step that fails if that flag is absent.
- `CycloneDDS.Schema` (attribute-only package) has a transitive dependency on `CycloneDDS.Core` through the targets file even when code gen is disabled. This is not a source-level dependency but it does appear in the transitive closure of referenced assemblies. The test SC-GZ054-1 checks only for FDP/Hrot prefixes, so CycloneDDS assemblies are correctly allowed.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

`DdsDebugPrimitivePublisher.Publish()` allocates a new `DebugPrimitive[]` array on every call (copying the buffer frame). For high-frequency telemetry this could be hot. A pooled array or span-based DDS write API would be preferred once the CycloneDDS runtime layer is available for GizmoMap-tier assemblies.

---

## Files Created

**ExtDeps/GizmoMap/GizmoMap.Contracts/**
- GizmoMap.Contracts.csproj (net8.0;netstandard2.1, BCL only)
- Primitives/FixedString32.cs
- Primitives/Rgba32.cs
- Primitives/CoordinateSpace.cs
- Primitives/DebugPrimitiveShape.cs
- Primitives/PipelineTarget.cs
- Primitives/SizeMode.cs
- Primitives/ScreenAnchor.cs
- Primitives/DebugPrimitive.cs
- Primitives/DebugPrimitiveBuffer.cs
- Abstractions/IDebugDrawBuilder.cs
- Sources/GizmoPickToken.cs
- Sources/IGizmoSource.cs
- StringInternMap.cs

**ExtDeps/GizmoMap/GizmoMap.Contracts.Tests/**
- GizmoMap.Contracts.Tests.csproj
- GizmoContractsTests.cs (6 tests)

**ExtDeps/GizmoMap/GizmoMap.Network/**
- GizmoMap.Network.csproj (net8.0, references GizmoMap.Contracts + CycloneDDS.Schema)
- GizmoInteractionEventKind.cs
- Topics/DebugPrimitivesBatch.cs
- Topics/GizmoInteractionBatch.cs
- Topics/GizmoUiState.cs
- Topics/StringInternBatch.cs
- Topics/EntityAttributeSchema.cs
- Transport/IDdsWriter.cs
- Transport/IDdsReader.cs
- Transport/DdsDebugPrimitivePublisher.cs
- Transport/DdsDebugPrimitiveSubscriber.cs
- Transport/DdsGizmoInteractionPublisher.cs
- Transport/DdsGizmoInteractionSubscriber.cs

**ExtDeps/GizmoMap/GizmoMap.Network.Tests/**
- GizmoMap.Network.Tests.csproj
- GizmoNetworkTests.cs (5 tests)

**IOS-IG-SimHost.sln** -- added GizmoMap solution folder under ExtDeps with all 4 projects and their test projects; added ProjectConfigurationPlatforms and NestedProjects entries.

---

## Outstanding Issues / Next Steps

- GZ055 (GizmoMap.Presentation) and GZ056 (unified example) are the next tasks in Phase 19.
- The `DdsDebugPrimitivePublisher` performs a per-publish heap allocation; a span/pooled variant should be considered when the runtime DDS layer is available.
