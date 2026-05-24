# BATCH-20 INSTRUCTIONS

**Batch Number:** 20
**Tasks:** GZ055, GZ056
**Assigned To:** Developer
**Date:** 2025-07-20

---

## Overview

This batch creates the `GizmoMap.Presentation` assembly (GZ055) and the unified example
application (GZ056). Together they complete Phase 19 — a fully self-contained GizmoMap stack
with no FDP/HROT dependencies.

Before implementing, read the design references for these tasks in:
- `.dev/gizmos-1/TASK-DETAIL.md` — sections for GZ055 (starts at "TASK-GZ055") and GZ056
  (starts at "TASK-GZ056"). These contain the definitive design, do not duplicate them here.
- `.dev/gizmos-1/DESIGN.md` §7 for architectural goals.

---

## Key Constraints (Non-Negotiable)

1. **Assembly boundary:** `GizmoMap.Presentation` and `GizmoMap.Example` MUST NOT reference
   `Fdp.Core`, `Fdp.ModuleHost`, any `Hrot.*` assembly, or any FDP simulation assembly.
2. **No ECS types:** No `Entity`, `ISimulationView`, `BitMask256`, `IEcsModuleSystem`,
   `DataDrivenGizmoSystem`, `StatelessGizmoSystem`, `GizmoSettingsPublisherSystem` anywhere in
   these new assemblies.
3. **Existing assemblies untouched:** Do NOT modify `Fdp.Presentation`, `Fdp.Toolkits`, or any
   other existing assembly.
4. **COPY strategy:** All types migrated from `Fdp.Presentation` are COPIED, not moved. The
   originals in `Fdp.Presentation` remain intact.
5. **Solution file:** Add all new projects to `IOS-IG-SimHost.sln` under the
   `ExtDeps/GizmoMap` solution folder.

---

## Context: Existing GizmoMap Assemblies

These exist and are referenced by the new assemblies:
- `ExtDeps/GizmoMap/GizmoMap.Contracts/` — BCL-only assembly, namespace
  `Fdp.Toolkit.Diagnostics.Gizmos`. Contains: `DebugPrimitive`, `DebugPrimitiveShape`,
  `CoordinateSpace`, `PipelineTarget`, `SizeMode`, `ScreenAnchor`, `FixedString32`, `Rgba32`,
  `DebugPrimitiveBuffer`, `IDebugDrawBuilder`, `StringInternMap`, `GizmoPickToken`,
  `IGizmoSource`.
- `ExtDeps/GizmoMap/GizmoMap.Network/` — DDS topics + stateless transport adapters.
  Namespace `GizmoMap.Network`. Contains: `DebugPrimitivesBatch`, `GizmoInteractionBatch`,
  `GizmoUiState`, `StringInternBatch`, `EntityAttributeSchema`, `DdsDebugPrimitivePublisher`,
  `DdsDebugPrimitiveSubscriber`, `DdsGizmoInteractionPublisher`, `DdsGizmoInteractionSubscriber`,
  `GizmoInteractionEventKind`.

Refer to the actual files for exact field names and API — do NOT guess.

---

## TASK GZ055 — Create `GizmoMap.Presentation` Assembly

**New project path:** `ExtDeps/GizmoMap/GizmoMap.Presentation/GizmoMap.Presentation.csproj`

### Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GizmoMap.Contracts\GizmoMap.Contracts.csproj" />
    <ProjectReference Include="..\GizmoMap.Network\GizmoMap.Network.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Raylib-cs" Version="7.0.2" />
    <PackageReference Include="rlImgui-cs" Version="3.2.0" />
    <PackageReference Include="ImGui.NET" Version="1.91.6.1" />
  </ItemGroup>
</Project>
```

### Files to create

All types use namespace `GizmoMap.Presentation` (not `Fdp.Toolkit.*`).

#### `Rendering/DebugPrimitiveRenderer2D.cs`

Adapt from `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`.

Key differences from the original:
- No `ISimulationView` dependency. Remove all `_view` usages.
- `EntityLocal` resolution now uses a **two-pass SpatialAnchor cache** instead of ECS lookup:
  - **Pass 1:** Sweep the input span and collect all `DebugPrimitiveShape.SpatialAnchor`
    primitives into a `Dictionary<long, SpatialAnchorEntry>` (keyed by `prim.SpatialNetworkId`).
  - **Pass 2:** Render. When encountering `EntityLocal` primitives, look up the anchor by
    `prim.SpatialNetworkId`. If not found, skip. Use pre-resolved `AnchorWorldX`, `AnchorWorldY`,
    `AnchorWorldZ`, and `AnchorYawRad` fields for transform. No quaternion math needed for 2D —
    just apply 2D rotation using `AnchorYawRad`.
- `SpatialAnchor` shape: render as a world-space crosshair (+) or simply skip rendering (it is a
  meta-primitive used for coordinate resolution only). Do not draw anything for the anchor itself.
- `SemanticShape` shape: delegate to injected `ISemanticShapeProfileRegistry?`. If registry is
  null or profile not found, draw a fallback magenta circle with `SemLengthMeters` radius at
  `(SemAnchorX, SemAnchorY)`.
- `MilStd2525` shape: delegate to `MilStd2525Renderer.Draw(...)`.
- `EntityBadge` shape: no ECS lookup. The badge world position comes from a resolved anchor
  position via the SpatialAnchor cache, keyed by `BadgeNetworkId` field. If no anchor found,
  skip.
- `ComponentInspector` shape: delegate to `ImGuiPropertyTreeAdapter.Schedule(...)` so the ImGui
  tree is drawn in the ImGui pass, not the Raylib 2D pass.
- Retain the existing `Render` signature:
  ```csharp
  public void Render(ReadOnlySpan<DebugPrimitive> primitives, Camera2D camera, float zoom);
  ```
  (No `RenderContext` struct — use simple parameters since we don't depend on Fdp.Toolkit.Vis2D.)

Define a local `struct SpatialAnchorEntry { public float X, Y, Z, YawRad; }`.

#### `Rendering/RichTextRenderer.cs`

Copy verbatim from `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/RichTextRenderer.cs`.
Change namespace to `GizmoMap.Presentation`. Remove `using Fdp.Core;` (the `FixedString32` type
comes from `GizmoMap.Contracts`).

#### `Rendering/MilStd2525Renderer.cs` (NEW)

Stub NATO symbol rendering. Design details are in TASK-DETAIL.md for GZ055.
The renderer must at minimum:
- Accept `string sidcCode`, `float worldX`, `float worldY`, `Camera2D camera`, `float zoom`.
- Draw a filled circle in the symbol's standard affiliation color (blue=friendly, red=hostile,
  yellow=neutral, green=unknown) plus a text label with the first 4 chars of the SIDC code.
- Affiliation is the second character of the SIDC code:
  `'F'|'A'|'D'|'J'` = friendly (blue), `'H'|'S'` = hostile (red),
  `'N'|'L'` = neutral (yellow), else unknown (green).

#### `Rendering/SemanticShapeRenderer.cs` (NEW)

Minimal profile-based renderer. Design details are in TASK-DETAIL.md for GZ055.
The renderer:
- Exposes `interface ISemanticShapeProfileRegistry { bool TryGetProfile(ulong profileId, out SemanticShapeProfile profile); }`.
- `struct SemanticShapeProfile { public float LengthMeters; public float WidthMeters; public string DisplayName; }`.
- `SemanticShapeRenderer(ISemanticShapeProfileRegistry? registry)` — registry is optional.
- `void Draw(ulong profileId, float centerX, float centerY, float lengthMeters, float widthMeters, uint conditionMask, Camera2D camera, float zoom, Rgba32 color)`.
- If registry != null and profile found: draw a rectangle with profile's dimensions. If the
  `conditionMask` bit 0 is set (Damaged), draw a red X overlay.
- If not found: draw a magenta outline circle.

#### `UI/ImGuiPropertyTreeAdapter.cs` (NEW)

Minimal adapter. Design details are in TASK-DETAIL.md for GZ055.
- `void Schedule(long networkId, uint schemaHash, float screenX, float screenY, bool isReadOnly)`.
- `void DrawScheduled()` — call from ImGui pass; draws an ImGui window for each scheduled item.
- For this batch, the ImGui window can simply show: `ImGui.Text($"Entity {networkId} schema 0x{schemaHash:X}")`.
  Full StructEdit integration is out of scope for this batch.

#### `UI/IconAtlasAdapter.cs` (NEW)

Minimal atlas adapter. Design details are in TASK-DETAIL.md for GZ055.
- `interface IIconAtlas { bool TryGetUv(FixedString32 atlasCoord, out System.Numerics.Vector4 uv); }`.
- `class IconAtlasAdapter` with `IIconAtlas? _atlas` field.
- `void Draw(FixedString32 atlasCoord, float worldX, float worldY, Camera2D camera, float zoom, Rgba32 color)`.
- Fallback (no atlas or coord not found): draw a yellow dot at the world position.

#### `Gizmos/GizmoInteractionProxyTool.cs`

Adapt from `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`.

Key differences:
- Replace `PickToken _token` with `GizmoPickToken _token` (from `GizmoMap.Contracts`).
- Replace `FdpEventBus _eventBus` with a simple callback delegate:
  ```csharp
  private readonly Action<GizmoPickToken, GizmoInteractionEventKind, System.Numerics.Vector3>? _onInteraction;
  ```
  This removes the FDP event bus dependency.
- Retain all logic (press → arm, drag → update, click-away cancel, right click cancel, ESC cancel).
- No `MapCanvas` dependency — replace with simple `Action? _onExit` callback (nullable).
- Use `GizmoInteractionEventKind` enum from `GizmoMap.Network` for the callback kind parameter.

#### `Layers/DebugGizmoLayer.cs`

Adapt from `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`.

Key differences:
- No `ISimulationView` or `FdpEventBus` parameters.
- Constructor: `DebugGizmoLayer(DebugPrimitiveBuffer buffer, DebugPrimitiveRenderer2D renderer)`.
- `void Render(Camera2D camera, float zoom)` — calls `_renderer.Render(_buffer.ReadAll(), camera, zoom)`.
- No `IMapLayer` interface (that lives in `Fdp.Toolkit.Vis2D.Abstractions`). This is a
  standalone Raylib rendering component.

#### `GizmoUndoStack.cs`

Copy from `Fdp.Toolkits` if it has no ECS dependencies. If it does, create a minimal stub:
```csharp
public sealed class GizmoUndoStack
{
    private readonly System.Collections.Generic.Stack<IGizmoUndoRecord> _records = new();
    public void Push(IGizmoUndoRecord record) => _records.Push(record);
    public bool TryUndo(out IGizmoUndoRecord? record)
    {
        if (_records.Count == 0) { record = null; return false; }
        record = _records.Pop();
        return true;
    }
}

public interface IGizmoUndoRecord
{
    void Undo();
}
```

### Tests for GZ055

**Test project:** `ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/`

**Test file:** `GizmoMap.Presentation.Tests/GizmoPresentationTests.cs`

Write the following tests (all must pass):

**SC-GZ055-1: No forbidden assembly references**
```csharp
[Fact]
public void SC_GZ055_1_NoForbiddenAssemblyReferences()
{
    var asm = typeof(DebugGizmoLayer).Assembly;
    var refNames = asm.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
    Assert.DoesNotContain(refNames, n => n.StartsWith("Fdp.Core", StringComparison.Ordinal));
    Assert.DoesNotContain(refNames, n => n.StartsWith("Fdp.ModuleHost", StringComparison.Ordinal));
    Assert.DoesNotContain(refNames, n => n.StartsWith("Hrot.", StringComparison.Ordinal));
}
```

**SC-GZ055-2: SpatialAnchor resolution — two-pass**

Create a subclass of `DebugPrimitiveRenderer2D` (or use the public API with a CapturingRenderer)
that records dispatched shapes. Construct two primitives:
1. A `SpatialAnchor` primitive with `SpatialNetworkId = 42`, `AnchorWorldX = 100f`,
   `AnchorWorldY = 200f`, `AnchorYawRad = 0f`.
2. A `Sphere` primitive in `CoordinateSpace.EntityLocal` with `SpatialNetworkId = 42`,
   `SphereCenter = (0, 0, 0)`.

Call `Render(...)`. Assert that the sphere was dispatched at world position `(100, 200)`.

**SC-GZ055-3: SemanticShape with null registry → fallback magenta circle**

Construct a `SemanticShape` primitive with `SemProfileId = 9999`. Call `Render(...)` with no
registry. Assert that a `Sphere` shape (or `Box2D`) was dispatched at the primitive's center.
(Override `DispatchShape` to capture.) Assert color matches the fallback magenta `Rgba32(255,0,255,255)`.

**SC-GZ055-4: No ECS production systems in assembly**
```csharp
[Fact]
public void SC_GZ055_4_NoEcsSystemsInAssembly()
{
    var asm = typeof(DebugGizmoLayer).Assembly;
    var forbidden = new[] { "DataDrivenGizmoSystem", "StatelessGizmoSystem", "GizmoSettingsPublisherSystem" };
    var typeNames = asm.GetTypes().Select(t => t.Name).ToArray();
    foreach (var name in forbidden)
        Assert.DoesNotContain(typeNames, n => n == name);
}
```

**SC-GZ055-5: GizmoInteractionProxyTool callback fires on drag**

Construct a `GizmoInteractionProxyTool` with a callback that records received events.
Call `HandlePress(Vector2.Zero, MouseButton.Left)` then `HandleDrag(new Vector2(5, 5), Vector2.Zero)`.
Assert the callback was called with `GizmoInteractionEventKind` matching drag/update.

**SC-GZ055-6: MilStd2525 affiliation color mapping**

Instantiate `MilStd2525Renderer` (or call the static affiliation-color helper directly).
Assert:
- SIDC starting with "SF..." → blue (`Raylib_cs.Color` with B=255 dominance, or check the
  `Rgba32` returned by the helper).
- SIDC starting with "SH..." → red.
- SIDC starting with "SN..." → yellow.

**Test project file:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GizmoMap.Presentation\GizmoMap.Presentation.csproj" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>
</Project>
```

---

## TASK GZ056 — Unified Example Application

**New project path:** `ExtDeps/GizmoMap/GizmoMap.Example/GizmoMap.Example.csproj`

Read TASK-DETAIL.md for GZ056 for complete design. Summary below.

### Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GizmoMap.Contracts\GizmoMap.Contracts.csproj" />
    <ProjectReference Include="..\GizmoMap.Network\GizmoMap.Network.csproj" />
    <ProjectReference Include="..\GizmoMap.Presentation\GizmoMap.Presentation.csproj" />
  </ItemGroup>
</Project>
```

### `IGizmoTransport` placement

Per SC-GZ056-4, define `IGizmoTransport` in `GizmoMap.Contracts` (not in the example):
```csharp
// File: ExtDeps/GizmoMap/GizmoMap.Contracts/Transport/IGizmoTransport.cs
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IGizmoTransport : System.IDisposable
    {
        void PublishPrimitives(System.ReadOnlySpan<DebugPrimitive> primitives);
        void PollAndApply(DebugPrimitiveBuffer target);
    }
}
```

### Files to create in `GizmoMap.Example/`

#### `Transport/LocalGizmoTransport.cs`

In-process transport: copies primitives directly from the span into the target buffer.
No CycloneDDS involved. `Dispose()` is a no-op.

#### `Transport/DdsGizmoTransport.cs`

CycloneDDS transport using `DdsDebugPrimitivePublisher` and `DdsDebugPrimitiveSubscriber` from
`GizmoMap.Network`. `PublishPrimitives` serialises and publishes. `PollAndApply` polls the
subscriber and deserialises received batches into the target buffer.

#### `Scenarios/DemoSceneGenerator.cs`

Implements `IGizmoSource`. Every frame call to `Emit(float deltaTime, IDebugDrawBuilder draw)`
must emit the following primitives (see TASK-DETAIL.md GZ056 for the full specification of
the mock scenario):

1. **SpatialAnchor** for `NetworkId = 100`: oscillates in a circle (radius=200m, period=10s).
2. **SemanticShape** (`EntityLocal`, `NetworkId = 100`): APC profile, toggles Damaged bit every 2s.
3. **Sphere** (`EntityLocal`, `NetworkId = 100`): sensor ring, `WorldMeters`, radius=50m.
4. **ComponentInspector** for `NetworkId = 100`: mock schema hash `0xDEADBEEF`.
5. **MilStd2525** at static world position (500, 300): `SidcCode = "SHGPE----------"` (hostile infantry).
6. **EntityBadge** (rich text) at the NATO symbol position: `"\x01Hostile\x04 - \x02Target"`.
7. **Interactive Box2D** around the NATO symbol: `GizmoPickToken { AnchorId=200, SubElementId=1 }`.
8. **Gradient Line** from the moving entity position to the static NATO symbol.
9. **Arrow** (`EntityLocal`, `NetworkId = 100`): velocity vector, `ScreenPixels`.
10. **Icon** (`CoordinateSpace.Screen`): at `(50, 50)`, atlas coord `"b12"`.
11. **DrawTextLong** (`Screen`): 200-char diagnostic string.
12. **Z-index test**: two overlapping `Box2D` at same world pos, `ZIndex=0` (gray) and `ZIndex=1` (white).
13. **LOD text**: `Text` with `MinZoomLod=4`, `MaxZoomLod=12`.

Note: For GizmoMap.Contracts' `IDebugDrawBuilder`, you must emit raw `DebugPrimitive` values
(not call high-level helper methods that may not exist). Use a simple `DebugPrimitiveBuffer`-
backed builder or create a minimal `LocalDrawBuilder` adapter in the example project.

#### `Program.cs`

```csharp
// Minimal structure (implement fully):
// 1. Parse args: --mode local | --mode dds (default: local)
// 2. Create IGizmoTransport based on mode
// 3. Create DemoSceneGenerator
// 4. Create DebugPrimitiveBuffer for producer and consumer sides
// 5. Initialize Raylib window (640x480, "GizmoMap Example - [mode]")
// 6. Main loop (30 frames for --mode local CI, infinite for interactive):
//    a. generator.Emit(dt, producerBuffer)
//    b. transport.PublishPrimitives(producerBuffer.ReadAll())
//    c. transport.PollAndApply(consumerBuffer)
//    d. renderer.Render(consumerBuffer.ReadAll(), camera, zoom=1f)
// 7. Exit after 30 frames if --headless flag present (for CI)
```

For `--mode dds`, step 4 creates a real CycloneDDS participant. The publisher and subscriber
share the same domain so the loopback test works.

Add `--headless` flag: when present, run exactly 30 frames without opening a visible window
(use `Raylib.SetWindowState(ConfigFlags.HiddenWindow)` or skip Raylib entirely by not calling
`BeginDrawing/EndDrawing` — just produce and publish).

### Tests for GZ056

**Test project:** `ExtDeps/GizmoMap/GizmoMap.Example.Tests/` (separate from GZ055 tests)

**Test file:** `GizmoMap.Example.Tests/GizmoExampleTests.cs`

Write the following tests:

**SC-GZ056-1: Local mode runs one frame**
```csharp
[Fact]
public void SC_GZ056_1_LocalModeRunsOneFrame()
{
    var producer = new DebugPrimitiveBuffer();
    var consumer = new DebugPrimitiveBuffer();
    using var transport = new LocalGizmoTransport();
    var gen = new DemoSceneGenerator();

    // Emit one frame
    var builder = new LocalDrawBuilder(producer); // or use DebugPrimitiveBuffer directly
    gen.Emit(0.016f, builder);
    transport.PublishPrimitives(producer.ReadAll());
    transport.PollAndApply(consumer);

    // At minimum one primitive must have been produced and transported
    Assert.True(consumer.Count > 0, "Expected at least one primitive in consumer buffer");
}
```

**SC-GZ056-2: Emitted primitives include SpatialAnchor**
```csharp
[Fact]
public void SC_GZ056_2_EmitsSpatialAnchor()
{
    var producer = new DebugPrimitiveBuffer();
    var gen = new DemoSceneGenerator();
    var builder = new LocalDrawBuilder(producer);
    gen.Emit(0f, builder);

    var prims = producer.ReadAll();
    Assert.Contains(prims.ToArray(), p => p.Shape == DebugPrimitiveShape.SpatialAnchor);
}
```

**SC-GZ056-3: No forbidden assembly references in example**
```csharp
[Fact]
public void SC_GZ056_3_NoForbiddenAssemblyReferences()
{
    var asm = typeof(DemoSceneGenerator).Assembly;
    var refNames = asm.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
    Assert.DoesNotContain(refNames, n => n.StartsWith("Fdp.", StringComparison.Ordinal));
    Assert.DoesNotContain(refNames, n => n.StartsWith("Hrot.", StringComparison.Ordinal));
}
```

**SC-GZ056-4: IGizmoTransport is defined in GizmoMap.Contracts**
```csharp
[Fact]
public void SC_GZ056_4_IGizmoTransportInContracts()
{
    var contractsAsm = typeof(IGizmoTransport).Assembly;
    Assert.Equal("GizmoMap.Contracts", contractsAsm.GetName().Name);
}
```

**SC-GZ056-5: Demo emits all required shape types**
```csharp
[Fact]
public void SC_GZ056_5_AllRequiredShapesEmitted()
{
    var producer = new DebugPrimitiveBuffer();
    var gen = new DemoSceneGenerator();
    var builder = new LocalDrawBuilder(producer);
    gen.Emit(1f, builder); // t=1s to ensure toggle state is deterministic

    var shapes = producer.ReadAll().ToArray().Select(p => p.Shape).ToHashSet();
    Assert.Contains(DebugPrimitiveShape.SpatialAnchor, shapes);
    Assert.Contains(DebugPrimitiveShape.SemanticShape, shapes);
    Assert.Contains(DebugPrimitiveShape.MilStd2525, shapes);
    Assert.Contains(DebugPrimitiveShape.Line, shapes);
    Assert.Contains(DebugPrimitiveShape.Sphere, shapes);
    Assert.Contains(DebugPrimitiveShape.Arrow, shapes);
}
```

**SC-GZ056-6: Damaged bit toggles on SemanticShape**
```csharp
[Fact]
public void SC_GZ056_6_DamagedBitToggles()
{
    var buf1 = new DebugPrimitiveBuffer(); var gen = new DemoSceneGenerator();
    gen.Emit(0.5f, new LocalDrawBuilder(buf1)); // t=0.5s, not damaged yet
    var buf2 = new DebugPrimitiveBuffer(); gen.Emit(2.0f, new LocalDrawBuilder(buf2)); // t=2.5s, damaged

    var sem1 = buf1.ReadAll().ToArray().FirstOrDefault(p => p.Shape == DebugPrimitiveShape.SemanticShape);
    var sem2 = buf2.ReadAll().ToArray().FirstOrDefault(p => p.Shape == DebugPrimitiveShape.SemanticShape);

    // One should have bit 0 of ConditionMask set, the other not (they must differ)
    Assert.NotEqual(sem1.SemConditionMask & 1u, sem2.SemConditionMask & 1u);
}
```

**Test project file:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GizmoMap.Example\GizmoMap.Example.csproj" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  </ItemGroup>
</Project>
```

---

## Implementation Notes

### DebugPrimitive field names

When accessing new shape-specific fields on `DebugPrimitive`, read the actual struct definition:
`ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs`

The batch report for BATCH-18 and BATCH-19 details the explicit field layout (offsets 24-63):
- **SpatialAnchor:** `SpatialNetworkId @24`, `AnchorWorldX @32`, `AnchorWorldY @36`,
  `AnchorWorldZ @40`, `AnchorYawRad @44` (Heading), `AnchorPitch @48`, `AnchorRoll @52`.
- **SemanticShape:** `SemProfileId @24` (ulong), `SemLengthMeters @32`, `SemWidthMeters @36`,
  `SemConditionMask @40` (uint), center coords in `SemAnchorX/Y` if available.
- **MilStd2525:** `MilWorldPosX @24`, `MilWorldPosY @28`, `MilSidcCode @32` (aliases `TextContent`).
- **ComponentInspector:** `InspNetworkId @24` (long), `InspSchemaHash @32` (uint),
  `InspIsReadOnly @37` (byte), `InspOffsetX @40`, `InspOffsetY @44`.
- **EntityBadge:** `BadgeNetworkId` (the network-stable id of the anchor entity) + `BadgeRichText`.

Read the actual file to get the correct field names — the above is guidance, not guaranteed exact.

### LocalDrawBuilder

You will need a minimal `IDebugDrawBuilder` implementation that writes into a `DebugPrimitiveBuffer`.
Create `GizmoMap.Example/LocalDrawBuilder.cs` (or put it in `GizmoMap.Contracts` if it has no
dependencies beyond the buffer). It converts the high-level draw calls into `DebugPrimitive`
struct fills, matching the field layout of the actual struct.

### Headless rendering for CI

Tests cannot open Raylib windows. The `GizmoMap.Presentation.Tests` project should NOT call
any Raylib draw functions. Instead:

In `DebugPrimitiveRenderer2D`, use the same override pattern as in `Fdp.Presentation`:
```csharp
protected virtual void DispatchShape(in DebugPrimitive prim, Camera2D camera, float zoom)
{
    // Production: issues real Raylib calls
}
```
Test subclass overrides `DispatchShape` to capture primitives without calling Raylib. See the
pattern in `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`.

### IDebugDrawBuilder → DebugPrimitive mapping

If `DebugPrimitiveBuffer` already has a builder wrapper or `Add(DebugPrimitive)` method, use it.
Check the actual file: `ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitiveBuffer.cs`.

---

## Report Requirements

Write your report to `.dev/gizmos-1/reports/BATCH-20-REPORT.md`.

The report must include for each task:
- List of files created/modified with paths.
- Design decisions and any deviations from the instructions (with justification).
- Which tests pass (`dotnet test` output).

Run the full solution build before submitting the report:
```
dotnet build IOS-IG-SimHost.sln --no-incremental
```
Confirm 0 errors in the report.

If questions arise, write them to `.dev/gizmos-1/questions/BATCH-20-QUESTIONS.md` and
make a reasonable default decision to unblock yourself. Document the decision in the report.

---

## Success Criteria Summary

| ID | Description |
|----|-------------|
| SC-GZ055-1 | GizmoMap.Presentation has no Fdp.Core / Fdp.ModuleHost / Hrot.* references (runtime check) |
| SC-GZ055-2 | SpatialAnchor two-pass: EntityLocal sphere dispatched at correct world position |
| SC-GZ055-3 | SemanticShape with null registry → fallback magenta circle dispatched |
| SC-GZ055-4 | No ECS production systems in assembly (type name check) |
| SC-GZ055-5 | GizmoInteractionProxyTool callback fires on drag |
| SC-GZ055-6 | MilStd2525 affiliation color mapping is correct |
| SC-GZ056-1 | Local mode: produces and transports at least one primitive |
| SC-GZ056-2 | Demo emits SpatialAnchor shape |
| SC-GZ056-3 | GizmoMap.Example has no Fdp.* / Hrot.* references (runtime check) |
| SC-GZ056-4 | IGizmoTransport is defined in GizmoMap.Contracts assembly |
| SC-GZ056-5 | All 6 required shape types emitted in one frame |
| SC-GZ056-6 | Damaged bit on SemanticShape toggles correctly over time |
