# BATCH-21 Report — GPU Draw Sink for 3D Gizmos (STR-D16 resolution)

## Implementation Summary

### Step 0 — Research (gizmo pipeline map)

**IDebugDrawSink3D** (`Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs`):
- Two methods: `DrawLine(in DebugDrawLine3D)` and `DrawShape(in DebugDrawShape3D)`.
- `DebugDrawLine3D`: `Start`, `End` (Stride-space Vector3), `StartColor`, `EndColor`.
- `DebugDrawShape3D`: `Kind` (Sphere | Box), `Position`, `Rotation`, `Scale`, `Color`.
- Extended with `BeginFrame()` / `EndFrame()` default interface methods (no-op) so existing sinks are unaffected.

**DebugPrimitiveRenderer3D** (`Hrot.Stride.Core/DebugPrimitiveRenderer3D.cs`):
- Two-pass: Pass 1 caches `SpatialAnchor` primitives by `NetworkId`; Pass 2 resolves each drawable primitive against its anchor (EntityLocal) or emits directly (World-space), then swizzles FDP→Stride and calls the sink.
- Shapes handled: `Line` → `DrawLine`, `Arrow` → `DrawLine`, `Sphere` → `DrawShape(Sphere)`, `SemanticShape` → `DrawShape(Box)`.
- 2D-only shapes (`Box2D`, `Text`, `Icon`, etc.) are skipped.
- `Sink` property exposed for host-side `BeginFrame`/`EndFrame` calls.

**Gizmo pipeline in Tick** (`EditorStrideSubsystem.Tick` step 6):
```csharp
GizmoRenderer3D.Sink.BeginFrame();
GizmoRenderer3D.Render(ProducerBuffer.GetFrame());
GizmoRenderer3D.Sink.EndFrame();
ProducerBuffer.EndFrame(dt);
```

**Prior state** (`LoggingDebugDrawSink3D` only): The renderer resolved and swizzled primitives correctly (all 307 tests green), but emitted only NLog Trace messages — nothing rendered in the Stride window.

### [VERIFY] Stride 4.2.1.2487 rendering options

Confirmed from NuGet package metadata (`Stride.Rendering.dll`, `Stride.Graphics.dll` XMLdocs):

| Option | Available? | Notes |
|--------|-----------|-------|
| `ImmediateDebugRenderSystem` / `DebugShapes` | **NO** | Does not exist in Stride.Rendering at this version |
| `Stride.Profiling.DebugTextSystem` | YES | Text only |
| `Stride.Rendering.Compositing.DebugRenderer` | YES (compositor feature) | Requires custom SDSL shaders — too heavy |
| `GeometricPrimitive<T>` | YES (`Stride.Graphics`) | `VertexBufferBinding` is `protected`, not directly usable for Model assembly |
| Raw GPU buffer construction (`Buffer.Vertex.New`, `Buffer.Index.New`) | YES | Available in `Stride.Graphics`, returns `Buffer` objects |
| `Material.New(GraphicsDevice, MaterialDescriptor)` | YES | `MaterialEmissiveMapFeature(ComputeColor)` with `Intensity=1` = unlit/emissive |
| `VertexPositionNormalTexture` | YES (`Stride.Graphics`) | Layout available |

**Chosen approach: Pooled-entity primitive sink** — pool of Stride `Entity` objects with `ModelComponent`s built from procedurally generated immutable GPU vertex/index buffers (`VertexPositionNormalTexture`), each entity using a per-color emissive material (`MaterialEmissiveMapFeature`). No custom shaders, no `GeometricPrimitive<T>` fields (protected), no compositor changes.

**Why this approach:**
- Zero-custom-shader: works with the existing `DefaultGraphicsCompositorLevel10` compositor already in the project.
- Per-frame allocation-free once the pool reaches steady state.
- `BeginFrame` hides all previous entities in one pass; `DrawLine`/`DrawShape` activate exactly as many as needed; pool grows on demand.
- Emissive material (emissive = debug color, intensity = 1) makes shapes visible independent of scene lighting — ideal for debug overlays.
- No changes to `DebugPrimitiveRenderer3D` (pure CPU swizzle layer stays clean and headless-testable).

### Step 1 — PooledEntityDebugDrawSink3D

**New file:** `Stride/Hrot.Stride.Core/PooledEntityDebugDrawSink3D.cs`

- Three pool sub-lists: `_linePool`, `_boxPool`, `_spherePool`.
- **Line** → unit-X cube (1×1×1) scaled to `(length, LineThicknessMeters, LineThicknessMeters)`, oriented via `RotationFromTo(UnitX, dir)`. Thickness = 0.03 m.
- **Box** → unit cube scaled by `shape.Scale` (extents in Stride space).
- **Sphere** → UV sphere (radius 0.5, 8 lat × 12 lon bands) scaled by `radius × 2`.
- Shared `Model` per kind (one `MeshDraw` with immutable `Buffer.Vertex.New` / `Buffer.Index.New` buffers).
- Per-entity `ModelComponent.Materials[0]` override carries the color (cached by ARGB key).
- `BeginFrame`: hides all pool entries with `Entity.EnableAll(false)`, resets cursors.
- `GetOrGrow`: reuses existing entries by cursor, grows pool (new `Entity` + `ModelComponent`) on overflow.
- `RotationFromTo(Vector3 from, Vector3 to)`: public pure static quaternion helper (testable headlessly).
- `Dispose()`: removes all entities from scene, releases GPU buffers.

### Wiring

**`DebugPrimitiveRenderer3D.Sink`** property exposed (so `EditorStrideSubsystem.Tick` can call `BeginFrame`/`EndFrame`).

**`IDebugDrawSink3D`** interface extended with `BeginFrame()` and `EndFrame()` as default (no-op) methods — backward-compatible with all existing sinks and test fakes.

**`EditorStrideSubsystem.Initialize`** — new optional `IDebugDrawSink3D? debugDrawSink` parameter (default = `null` → `LoggingDebugDrawSink3D`). `_debugDrawSinkDisposable` stored for cleanup.

**`StrideHrotGame.BootEditorSubsystem`** — creates `PooledEntityDebugDrawSink3D(this, scene)` (after `BeginRun`, where `GraphicsDevice` is live) and passes it to `Initialize`. Log: `"[StrideHrotGame] PooledEntityDebugDrawSink3D created (STR-D16 resolved)."`.

**`EditorStrideSubsystem.Dispose`** — disposes `_debugDrawSinkDisposable`.

### Step 2 — D8 DrawTestGizmo upgrade

**File:** `Stride/HrotStrideApp.Game/StrideGizmoReplayHarnessCases.cs`

Upgraded `DrawTestGizmo` from "one line + one sphere" to a rich set of 8 shapes:

| Shape | FDP position | Color | Notes |
|-------|-------------|-------|-------|
| Line (axis +X/East) | origin → origin+2X | Red | Axis triad |
| Line (axis +Y/North) | origin → origin+2Y | Green | Axis triad |
| Line (axis +Z/Up) | origin → origin+2Z | Blue (0,0,255) | Axis triad |
| Sphere | origin + (0,0,2), r=0.75 | Red | Floating above origin |
| Sphere | origin + (1,1,0.5), r=0.4 | White | Secondary landmark |
| Line (diagonal NE) | origin → origin+(1.4,1.4,0) | Cyan (0,255,255) | Cross pattern |
| Line (diagonal NW) | origin → origin+(-1.4,1.4,0) | Magenta (255,0,255) | Cross pattern |
| Line (vertical) | origin+(0,0,1) → origin+(0,0,3) | Yellow | |

Origin is FDP (0, 6, 0) → Stride (0, 0, 6). All shapes persist 8 s.

The case calls `BeginFrame`/`Render`/`EndFrame` immediately on trigger (not just via the tick loop) so the log shows the emitted count on D8 press. Also logs whether the GPU sink is active (`Sink is PooledEntityDebugDrawSink3D`).

**What the user should see when pressing D8:**
At Stride position (0, 0, 6) — the arena center — colored emissive shapes appear: three bright axis sticks (red East, green North, blue Up), a red sphere floating 2 m above the ground, a smaller white sphere offset NE, and yellow/cyan/magenta line segments forming a star/cross pattern. All shapes glow without requiring lighting (emissive material). The log confirms "GPU sink active: True" and "Emitted 8 debug shape(s)". Shapes persist 8 s.

## Design Decisions

1. **Pooled entities over compositor shader:** No custom SDSL shader needed. The pooled entity approach reuses all the material/rendering infrastructure already validated by `StrideVisualFactory`. Risk: entity count grows proportional to max simultaneously-visible primitives; in practice, debug gizmo counts are small (< 50).

2. **Unit-X cube for line segments:** A cube stretched along X (length) with Y=Z=thickness is simpler than a cylinder (which needs Y→Z reorientation). The `RotationFromTo(UnitX, dir)` formula is compact and tested.

3. **UV sphere over GeoSphere:** `GeometricPrimitive<T>.VertexBufferBinding` is `protected` and not accessible outside the class; `GeometricPrimitive.GeoSphere.New()` would require reflection or a subclass to extract the buffers. Hand-rolled UV sphere (8 lat × 12 lon = 156 verts, 192 triangles) avoids this and produces the same visual quality for debug use.

4. **BeginFrame/EndFrame as default interface methods:** Backward-compatible. The `LoggingDebugDrawSink3D` and all test captures (e.g. `CapturingSink` in `DebugPrimitiveRenderer3DTests`) require no changes.

5. **Emissive-only material:** `MaterialEmissiveMapFeature(ComputeColor(color), Intensity=1)` with no diffuse model. Shapes glow with their debug color regardless of lighting — correct for debug overlays. The `MaterialDiffuseLambertModelFeature.UseEnergyConservingSpecular` property that would zero out diffuse is absent in Stride 4.2.1.2487, so the descriptor simply omits any diffuse contribution.

6. **Coordinate correctness:** The sink consumes already-swizzled Stride-space positions/rotations from `DebugPrimitiveRenderer3D`. No second swizzle.

## Deviations

None from the spec.

## Test Results

```
Hrot.Stride.Core.Tests:        Passed  327 / 327   (307 baseline + 20 new)
Hrot.Stride.Animation.Tests:   Passed   48 /  48   (no change)
HrotStrideApp.Game.Tests:      Passed  136 / 136   (no change)
```

**New tests (20) in `PooledEntityDebugDrawSinkTests.cs`:**
- `RotationFromTo_SameDirection_ReturnsIdentity`
- `RotationFromTo_UnitXToUnitY_RotatesCorrectly`
- `RotationFromTo_UnitXToUnitZ_RotatesCorrectly`
- `RotationFromTo_AntiParallel_Rotates180`
- `RotationFromTo_ArbitraryDirection_RotatesCorrectly`
- `LineMidpointAndLength_CorrectFormula` ×3 (Theory: horizontal, vertical, 3-4-5 triangle)
- `SphereScale_EntityScaleIsTwiceRadius` ×4 (Theory: r=0.5,0.75,1,2)
- `DefaultInterface_BeginEndFrame_DoNotThrow`
- `Renderer_Sink_ReturnsSameInstancePassedAtConstruction`
- `TrackingSink_BeginAndEndFrameCalledByHost_DrawsInBetween`
- `RotationFromTo_IsUnitQuaternion` ×5 (Theory: X→Y, X→Z, Y→Z, -X→X, X→diagonal)

**Build:** 0 errors, 9 pre-existing warnings (all `CS0108` / Stride API shadows — pre-existing).

## Developer Insights

- `GeometricPrimitive<T>` in Stride 4.2.1.2487: the `VertexBufferBinding` and `IndexBuffer` fields are `public`/`protected` BUT the generic type parameter's constraint is `struct, IVertex` — this caused C# to reject our helper method when we accidentally used a non-struct constraint. Switching to raw buffer construction sidesteps this entirely.

- `Entity.EnableAll(bool, bool)` — the second parameter is `applyOnChildren` (not `applyToChildren`) — caught by compiler, fixed immediately.

- The `Rgba32` struct has only 5 predefined colors (Red, Green, Yellow, White, Black, Transparent). Blue, Cyan, Magenta must be constructed inline: `new Rgba32(0, 0, 255, 255)` etc.

- `MaterialDiffuseLambertModelFeature.UseEnergyConservingSpecular` does NOT exist in Stride 4.2.1.2487. Removing this property (just use the emissive feature alone) gives a clean emissive material.

- Default interface methods in C# 8 work correctly for `IDebugDrawSink3D.BeginFrame`/`EndFrame` — all existing implementing types (logging sink, capturing fakes) get no-op defaults automatically.

## Known Issues

- Pool entities are always visible to all cameras in the scene (no culling by camera). For the current single-camera setup this is fine.
- Material instances are cached by ARGB key but never released until `Dispose()`. In practice the number of distinct debug colors is bounded (< 10).
- Line segments rendered as thin cubes: when a line segment is very short (< LineThicknessMeters), it looks like a small cube rather than a segment. This is fine for debug use.
- GPU verification required: the actual render must be confirmed by the user pressing D8 in the Stride window.

## Suggested Commit Message

feat(editor_stride): concrete GPU draw sink for 3D gizmos (STR-D16 resolved, BATCH-21)
