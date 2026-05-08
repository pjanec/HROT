# BATCH-20 Implementation Report

**Batch:** BATCH-20  
**Tasks:** GZ055, GZ056  
**Status:** COMPLETE — 0 build errors, 12/12 tests passing

---

## Summary

BATCH-20 implemented two tasks:

- **GZ055** — `GizmoMap.Presentation` assembly: standalone 2D rendering layer for debug primitives using Raylib, with no dependency on `Fdp.Core`, `Fdp.ModuleHost`, or `Hrot.*`.
- **GZ056** — `GizmoMap.Example` unified application: entry-point executable demonstrating local and DDS transport modes, with `IGizmoTransport` interface defined in `GizmoMap.Contracts`.

All four new projects are registered in `IOS-IG-SimHost.sln` with correct GUIDs, solution folder nesting, and platform configuration entries.

---

## Files Created

### GizmoMap.Contracts (additions)

| File | Description |
|------|-------------|
| `ExtDeps/GizmoMap/GizmoMap.Contracts/Transport/IGizmoTransport.cs` | `IGizmoTransport` interface; `PublishPrimitives` + `PollAndApply` + `IDisposable` |

### GizmoMap.Presentation (GZ055) — new assembly

| File | Description |
|------|-------------|
| `ExtDeps/GizmoMap/GizmoMap.Presentation/GizmoMap.Presentation.csproj` | Project file; references Contracts + Network; Raylib-cs 7.0.2, rlImgui-cs 3.2.0, ImGui.NET 1.91.6.1 |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs` | Core renderer; two-pass SpatialAnchor resolution; `Render(ReadOnlySpan<DebugPrimitive>, Camera2D, float)` |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/RichTextRenderer.cs` | Rich-text badge renderer (namespace changed from Fdp.Presentation) |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/MilStd2525Renderer.cs` | NATO symbol stub; affiliation colour from SIDC[1] |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/Rendering/SemanticShapeRenderer.cs` | Profile-based shape renderer; `ISemanticShapeProfileRegistry` + `SemanticShapeProfile` |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs` | ImGui property tree stub; `Schedule` + `DrawScheduled` |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/UI/IconAtlasAdapter.cs` | Icon atlas resolver; `IIconAtlas` + `IconAtlasAdapter.Draw` |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/Gizmos/GizmoInteractionProxyTool.cs` | Interaction proxy; callback delegate, no FdpEventBus |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/Layers/DebugGizmoLayer.cs` | Standalone Raylib layer; wraps buffer + renderer; no IMapLayer |
| `ExtDeps/GizmoMap/GizmoMap.Presentation/GizmoUndoStack.cs` | Minimal undo stack stub |

### GizmoMap.Presentation.Tests

| File | Description |
|------|-------------|
| `ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/GizmoMap.Presentation.Tests.csproj` | xunit 2.9.3 test project |
| `ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/GizmoPresentationTests.cs` | 6 tests SC-GZ055-1 through SC-GZ055-6 |

### GizmoMap.Example (GZ056) — new executable

| File | Description |
|------|-------------|
| `ExtDeps/GizmoMap/GizmoMap.Example/GizmoMap.Example.csproj` | OutputType=Exe; references Contracts + Network + Presentation |
| `ExtDeps/GizmoMap/GizmoMap.Example/LocalDrawBuilder.cs` | `IDebugDrawBuilder` implementation wrapping `DebugPrimitiveBuffer` |
| `ExtDeps/GizmoMap/GizmoMap.Example/Transport/LocalGizmoTransport.cs` | In-process transport; PublishPrimitives copies span, PollAndApply drains |
| `ExtDeps/GizmoMap/GizmoMap.Example/Transport/DdsGizmoTransport.cs` | DDS transport; wraps `DdsDebugPrimitiveSubscriber` |
| `ExtDeps/GizmoMap/GizmoMap.Example/Scenarios/DemoSceneGenerator.cs` | `IGizmoSource` mock; 13 primitives/frame; animated SpatialAnchor orbit |
| `ExtDeps/GizmoMap/GizmoMap.Example/Program.cs` | Entry point; `--mode local|dds`, `--headless` flag; 30-frame headless loop |

### GizmoMap.Example.Tests

| File | Description |
|------|-------------|
| `ExtDeps/GizmoMap/GizmoMap.Example.Tests/GizmoMap.Example.Tests.csproj` | xunit 2.9.3 test project |
| `ExtDeps/GizmoMap/GizmoMap.Example.Tests/GizmoExampleTests.cs` | 6 tests SC-GZ056-1 through SC-GZ056-6 |

### IOS-IG-SimHost.sln (modified)

- Added 4 `Project` entries: GUIDs A1000006, A1000007, A1000008, A1000009
- Updated `GlobalSection(NestedProjects)`: all 4 nested under GizmoMap folder `{A1000001-B2C3-D4E5-F6A7-B8C9D0E1F2A3}`
- Added 48 lines of `ProjectConfigurationPlatforms` entries (12 per project x 4 projects)

---

## Design Decisions and Deviations

### Field names — actual struct layout used

The batch instructions used provisional field names that did not match the actual `DebugPrimitive` struct. All code uses the real names:

| Instruction name | Actual field name | Notes |
|---|---|---|
| `SpatialNetworkId` | `NetworkId` (long @24) | SpatialAnchor shape |
| `AnchorYawRad` | `Heading` (float @44, in degrees) | SpatialAnchor shape |
| `SemProfileId` | `ProfileId` (ulong @24) | SemanticShape shape |
| `SemConditionMask` | `ConditionMask` (uint @40) | SemanticShape shape |

### `GetFrame()` instead of `ReadAll()`

`DebugPrimitiveBuffer` exposes `GetFrame()` returning `ReadOnlySpan<DebugPrimitive>`. The buffer has no `ReadAll()` method. All code and tests use `GetFrame()` and `GetFrame().Length` (no `Count` property either).

### SemanticShape resolved position encoding

`DebugPrimitive` has no dedicated world-position fields for the SemanticShape layout. After resolving a SemanticShape's EntityLocal anchor, the resolved world coordinates are stored in spare, layout-compatible fields:

- World X → `resolved.Pitch` (float @48 — unused by SemanticShape)
- World Y → `resolved.InspOffsetY` (float @44 — unused by SemanticShape)

These fields do not overlap with ProfileId (@24), LengthMeters (@32), WidthMeters (@36), or ConditionMask (@40).

### DemoSceneGenerator — dual Emit overload

`DemoSceneGenerator.Emit(float, IDebugDrawBuilder)` casts the builder to `LocalDrawBuilder` to call `EmitRaw`. A typed `Emit(float, LocalDrawBuilder)` overload is provided for test use without a cast.

### DdsGizmoTransport — direct batch enqueue

`DdsDebugPrimitivePublisher` takes a `DebugPrimitiveBuffer`, not a `ReadOnlySpan`. To implement `PublishPrimitives(ReadOnlySpan<DebugPrimitive>)`, the DDS transport creates a `DebugPrimitivesBatch` from the span and enqueues it via in-memory adapters. PollAndApply delegates to `DdsDebugPrimitiveSubscriber.PollAndApply`.

### Fix applied during build — missing `using System;`

Two files were missing `using System;`:
- `GizmoMap.Presentation/Rendering/RichTextRenderer.cs` — `ReadOnlySpan<>` requires System
- `GizmoMap.Example.Tests/GizmoExampleTests.cs` — `StringComparison` requires System
- `GizmoMap.Presentation/Rendering/MilStd2525Renderer.cs` — `Rgba32` not in scope; added `using Fdp.Toolkit.Diagnostics.Gizmos`

---

## Build Output

```
dotnet build IOS-IG-SimHost.sln --no-incremental
Build succeeded.
    0 Error(s)
```

---

## Test Results

### GizmoMap.Presentation.Tests (SC-GZ055)

```
Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 0.7920 Seconds
```

| Test | Result |
|------|--------|
| SC_GZ055_1_NoForbiddenAssemblyReferences | PASS |
| SC_GZ055_2_SpatialAnchorResolution_TwoPass | PASS |
| SC_GZ055_3_SemanticShapeFallback_MagentaSphere | PASS |
| SC_GZ055_4_NoEcsSystemsInAssembly | PASS |
| SC_GZ055_5_GizmoInteractionProxyTool_DragCallbackFires | PASS |
| SC_GZ055_6_MilStd2525AffiliationColors | PASS |

### GizmoMap.Example.Tests (SC-GZ056)

```
Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 0.8058 Seconds
```

| Test | Result |
|------|--------|
| SC_GZ056_1_LocalModeRunsOneFrame | PASS |
| SC_GZ056_2_EmitsSpatialAnchor | PASS |
| SC_GZ056_3_NoForbiddenAssemblyReferences | PASS |
| SC_GZ056_4_IGizmoTransportInContracts | PASS |
| SC_GZ056_5_AllRequiredShapesEmitted | PASS |
| SC_GZ056_6_DamagedBitToggles | PASS |

---

## Conclusion

BATCH-20 is complete.

- 21 new files created across 4 new projects + 1 contract extension
- `IOS-IG-SimHost.sln` updated with full project registration
- Full solution builds with 0 errors
- 12/12 tests pass across GZ055 and GZ056 success criteria
