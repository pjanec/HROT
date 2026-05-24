# BATCH-05 Instructions — 2D Presentation Adapter

**Tasks:** GZ011, GZ012, GZ013, GZ014
**Phase:** Phase 5 — 2D Presentation Adapter
**Design references:** TASK-DETAIL.md §TASK-GZ011, §TASK-GZ012, §TASK-GZ013, §TASK-GZ014; DESIGN.md §5.1–5.4

---

## Context

This batch wires the gizmo primitive layer into the 2D Raylib renderer and the existing
`DebugGizmoLayer`. GZ011 and GZ012 go into `Fdp.Presentation`. GZ013 modifies the existing
`DebugGizmoLayer.cs`. GZ014 adds entity-badge rich-text rendering.

Test project: `FDP/Engine/Fdp.Presentation.Tests`
Test namespace: `Fdp.Toolkit.Vis2D.Tests.Gizmos`

---

## Task GZ011 — DebugPrimitiveRenderer2D (base renderer + culling)

**Full spec:** TASK-DETAIL.md §TASK-GZ011

### File: `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`

**Testable design requirement:** The class must have a `protected virtual void DispatchShape(in DebugPrimitive prim, RenderContext ctx)` method. The public `Render` method performs filtering and sorting; `DispatchShape` performs actual Raylib calls. This allows test subclasses to override `DispatchShape` and count/record calls without invoking Raylib.

```csharp
public class DebugPrimitiveRenderer2D
{
    private ushort _activeLayerMask = 0xFFFF;
    protected readonly ISimulationView? _view;

    public DebugPrimitiveRenderer2D(ISimulationView? view = null) { _view = view; }

    public void SetLayerMask(ushort mask) => _activeLayerMask = mask;

    public void Render(ReadOnlySpan<DebugPrimitive> primitives, RenderContext ctx)
    {
        // 1. Filter: TargetView, layer mask, LOD culling.
        // 2. Collect filtered primitives into a temporary sorted list.
        // 3. Sort by (DebugLayer ascending, ZIndex ascending).
        // 4. Call DispatchShape for each.
    }

    protected virtual void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
    {
        // Real Raylib calls here.
        // Shape dispatch for Line, Sphere, Arrow, Text.
        // Unknown shapes: silently skip.
    }
}
```

**Filtering rules in `Render`:**
- Skip if `(prim.TargetView & PipelineTarget.Map2D) == 0`.
- Skip if `prim.DebugLayer >= 16 || (_activeLayerMask & (1u << prim.DebugLayer)) == 0`.
- LOD zoom culling: `float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f`.
  - Skip if `prim.MinZoomLod != 0 && zoom < prim.MinZoomLod * 0.25f`.
  - Skip if `prim.MaxZoomLod != 0 && zoom > prim.MaxZoomLod * 0.25f`.

**Sorting:** After filtering, sort the passing primitives by `(DebugLayer ascending, ZIndex ascending)`
before dispatching. Use a temporary `List<DebugPrimitive>` (rented from `ArrayPool` is fine;
allocation is acceptable in the render-sorting step since it happens once per frame, not per primitive).

**`DispatchShape` implementation (Raylib draw calls):**
The actual draw calls reference Raylib_cs. Implement at minimum:
- `DebugPrimitiveShape.Line`: Call `Raylib.DrawLineEx`. For gradient (EndColor != Color),
  draw two lines with alpha blend or a simple gradient approximation. Compute thickness:
  - `SizeMode.ScreenPixels`: `t = prim.ThicknessU16 * 0.1f / (ctx.Zoom > 0f ? ctx.Zoom : 1f)`.
  - `SizeMode.WorldMeters`: `t = prim.ThicknessU16 * 0.1f`.
- `DebugPrimitiveShape.Sphere`: `Raylib.DrawCircleV`.
- `DebugPrimitiveShape.Arrow`: line + filled triangle arrowhead.
- `DebugPrimitiveShape.Text`: `Raylib.DrawText` at start position.
- Unknown shape values: return without drawing.

**Helper:** `protected static Raylib_cs.Color ToRaylibColor(Rgba32 c)` converts the `Rgba32`
struct to `Raylib_cs.Color`.

**Tests** in `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugPrimitiveRenderer2DTests.cs`:

Create a test-only subclass:
```csharp
internal sealed class CapturingRenderer2D : DebugPrimitiveRenderer2D
{
    public readonly List<DebugPrimitive> Dispatched = new();
    protected override void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
        => Dispatched.Add(prim);
}
```

Minimum tests (using `CapturingRenderer2D`):
- **SC-GZ011-1**: Primitive with `TargetView = None` → `Dispatched.Count == 0`.
- **SC-GZ011-2**: Primitive on layer 5 with mask having bit 5 set → `Dispatched.Count == 1`.
- **SC-GZ011-3**: Same primitive with bit 5 clear → `Dispatched.Count == 0`.
- **SC-GZ011-6**: Two primitives on same layer, ZIndex 1 then ZIndex 0 (wrong order). After Render,
  `Dispatched[0].ZIndex == 0` (lower index rendered first).
- **SC-GZ011-7**: `MinZoomLod = 8` (threshold 2.0f). With `ctx.Zoom = 1.0f` → skipped.
  With `ctx.Zoom = 3.0f` → dispatched.
- **SC-GZ011-8**: `MaxZoomLod = 8` (threshold 2.0f). With `ctx.Zoom = 1.0f` → dispatched.
  With `ctx.Zoom = 3.0f` → skipped.
- **SC-GZ011-9**: Both `MinZoomLod = 0` and `MaxZoomLod = 0` → always dispatched regardless of zoom.

To construct `DebugPrimitive` test fixtures, use `DebugPrimitive.MakeLine` or `DebugPrimitive.MakeText`
from BATCH-01. Set fields directly using the factory output then mutate: assign `TargetView`,
`DebugLayer`, `ZIndex`, `MinZoomLod`, `MaxZoomLod` via the public fields (they are all publicly
settable since `DebugPrimitive` is an explicit-layout struct with public fields).

---

## Task GZ012 — Spatial Projection (CoordinateSpace + EntityLocal)

**Full spec:** TASK-DETAIL.md §TASK-GZ012

**Modify** `DebugPrimitiveRenderer2D.cs` from GZ011:

Before calling `DispatchShape`, resolve `EntityLocal` primitives:
- If `prim.Space == CoordinateSpace.EntityLocal`:
  - The anchor entity is stored in `prim.AnchorIndex` (the entity Index) and `prim.AnchorGeneration`
    (the entity Generation). Construct the `Entity` from these fields.
  - Check `_view != null && _view.IsAlive(entity) && _view.HasComponent<SimTransform>(entity)`.
    If any check fails, skip this primitive (call `continue`).
  - Read `ref readonly var tf = ref _view.GetComponentRO<SimTransform>(entity)`.
  - The `DebugPrimitive` payload is in local space. Transform line endpoints:
    `worldStart = tf.Position.Xy + localStart` (for Line).
    Skip Arrow/Text EntityLocal for now — document as deferred.
  - After resolving, pass the modified world-space coordinates to `DispatchShape` via a temporary
    copy of the primitive with overwritten position fields. Since `DebugPrimitive` is an unsafe
    explicit-layout struct, copy it with `DebugPrimitive resolved = prim;` and overwrite the
    relevant fields.

**Note on `SimTransform`:** Read the actual `SimTransform` struct from the codebase to understand
its fields. Look for it in `Hrot/Engine/Hrot.Core/` or `FDP/Toolkits/Fdp.Toolkits/`.

**Tests** (add to `DebugPrimitiveRenderer2DTests.cs`):
- **SC-GZ012-1**: Create a mock `ISimulationView` that returns a `SimTransform` with
  `Position = (10, 20, 0)`. Create an `EntityLocal` Line primitive with `LineStart = (1, 0, 0)`.
  After `Render`, verify the dispatched primitive's start position is `(11, 20, ...)`.
  Since `DebugPrimitive` has payload at fixed offsets, verify by reading the `LineStart` payload
  field of the dispatched primitive.
- **SC-GZ012-2**: Mock `ISimulationView.IsAlive` returns `false` → `EntityLocal` primitive is
  skipped, `Dispatched.Count == 0`.

Use `Mock<ISimulationView>` (Moq) for these tests.

---

## Task GZ013 — DebugGizmoLayer Integration

**Full spec:** TASK-DETAIL.md §TASK-GZ013

**Modify** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`.

The existing constructor `DebugGizmoLayer(int layerBitIndex = 31)` must continue to work for
backward compatibility. Add a new constructor overload:

```csharp
public DebugGizmoLayer(
    int layerBitIndex,
    DebugPrimitiveBuffer buffer,
    FdpEventBus eventBus,
    ISimulationView? view = null)
{
    LayerBitIndex = layerBitIndex;
    _buffer       = buffer;
    _eventBus     = eventBus;
    _renderer     = new DebugPrimitiveRenderer2D(view);
}
```

Add fields:
```csharp
private DebugPrimitiveBuffer? _buffer;
private DebugPrimitiveRenderer2D? _renderer;
private FdpEventBus? _eventBus;
```

Modify `Draw`:
```csharp
public void Draw(RenderContext ctx)
{
    uint maskBit = 1u << LayerBitIndex;
    if ((ctx.VisibleLayersMask & maskBit) == 0) return;

    if (_buffer != null && _renderer != null)
    {
        var primitives = _buffer.GetFrame();
        _renderer.Render(primitives, ctx);
    }
}
```

Modify `HandleInput`:
```csharp
public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
{
    if (_buffer == null || _eventBus == null) return false;
    if (!isPressed || button != MouseButton.Left) return false;

    // Hit-test: find closest pickable primitive within hit radius.
    const float hitRadiusWorld = 5f; // 5-world-unit radius
    var primitives = _buffer.GetFrame();
    DebugPrimitive? best = null;
    float bestDist = float.MaxValue;

    foreach (ref readonly var prim in primitives)
    {
        if (!prim.Token.IsValid) continue;
        // Use prim's 2D world position (LineStart for Line, SphereCenter for Sphere, etc.)
        // For simplicity: extract WorldPos as Vector2(LineStartX, LineStartY) for Line primitives.
        // Other shapes: use their center field.
        var primPos = GetPrimitive2DPos(in prim);
        float dist = Vector2.Distance(worldPos, primPos);
        if (dist < hitRadiusWorld && dist < bestDist &&
            prim.DebugLayer >= (best?.DebugLayer ?? 0))
        {
            best = prim;
            bestDist = dist;
        }
    }

    if (best.HasValue)
    {
        var proxy = new GizmoInteractionProxyTool(best.Value.Token, _eventBus);
        // _canvas is not available in the layer — layers don't have canvas access in the current design.
        // Deviation: publish GizmoInteractionStartedEvent only; let the canvas layer above
        // handle tool push. Document this as a design deviation.
        _eventBus.Publish(new GizmoInteractionStartedEvent
        {
            Token    = best.Value.Token,
            WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
        });
        return true;
    }
    return false;
}

private static Vector2 GetPrimitive2DPos(in DebugPrimitive prim)
{
    // Returns a representative 2D world position for hit-testing.
    return prim.Shape switch
    {
        DebugPrimitiveShape.Line   => new Vector2(prim.LineStartX, prim.LineStartY),
        DebugPrimitiveShape.Sphere => new Vector2(prim.SphereCenterX, prim.SphereCenterY),
        _                          => new Vector2(prim.LineStartX, prim.LineStartY),
    };
}
```

**IMPORTANT DEVIATION**: `DebugGizmoLayer` does not have access to `MapCanvas` (layers are not
wired to the canvas in the current design). Therefore the proxy tool CANNOT be pushed from
`HandleInput`. Instead, only publish `GizmoInteractionStartedEvent` and return `true` to consume
the click. The actual `GizmoInteractionProxyTool` push will be done by the caller who handles
`GizmoInteractionStartedEvent`. Document this deviation in the report.

**Accessing DebugPrimitive payload fields:** The `DebugPrimitive` struct has unsafe/explicit layout.
Check the actual field names in `DebugPrimitive.cs` (BATCH-01 output). Fields like `LineStartX`,
`LineStartY`, `SphereCenterX`, `SphereCenterY` may be named differently. Read the struct
definition before coding.

**Tests** (add to Presentation.Tests, new file or append to existing):
`FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugGizmoLayerGizmoTests.cs`

- **SC-GZ013-1**: Construct `DebugGizmoLayer` with a buffer and a `CapturingRenderer2D` (need
  to make the renderer injectable — add a constructor overload
  `DebugGizmoLayer(int, DebugPrimitiveBuffer, FdpEventBus, DebugPrimitiveRenderer2D)` for testing).
  Push a primitive into the buffer, call `layer.Draw(ctx)`, verify no exception thrown
  (rendering itself is verified by `CapturingRenderer2D`).
- **SC-GZ013-2**: `HandleInput` with a primitive that has `Token.IsValid == true` within hit
  radius → returns `true` and `GizmoInteractionStartedEvent` is published.
- **SC-GZ013-3**: `HandleInput` at a position outside hit radius of any pickable primitive →
  returns `false`.
- **SC-GZ013-4**: `VisibleLayersMask` with the layer's bit clear → `Draw` skips rendering
  (verify via `CapturingRenderer2D.Dispatched.Count == 0`).

---

## Task GZ014 — Entity Badge and Rich Text Rendering

**Full spec:** TASK-DETAIL.md §TASK-GZ014

### File: `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/RichTextRenderer.cs`

**Design for testability:** Extract the parsing logic into a testable `internal static` method
that returns chunks without calling Raylib:

```csharp
/// <summary>Returns parsed text chunks (text, color) without issuing draw calls.</summary>
internal static List<(string Text, Raylib_cs.Color Color)> ParseChunks(ref FixedString32 text)
```

The public `DrawRichTextBadge` calls `ParseChunks` then draws each chunk.

**Color mapping:**
- `0x01` = Red
- `0x02` = Green
- `0x03` = Yellow
- Any other byte or no control byte = White (default)

**Parsing algorithm:**
Iterate raw bytes of the `FixedString32` (via `MemoryMarshal.CreateReadOnlySpan` or unsafe pointer).
Stop at null byte (`0x00`). When a control byte (< `0x20`, i.e. non-printable ASCII) is encountered:
1. Flush current text run as a chunk with the current color.
2. Switch color based on the control byte.
When the end of string (null byte) is reached, flush remaining text as a chunk.

Use `stackalloc byte[32]` to accumulate ASCII bytes for each chunk. To convert to `string` for
the `ParseChunks` return value, use `System.Text.Encoding.ASCII.GetString(span)`.

**Tests** in `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/RichTextRendererTests.cs`:
- **SC-GZ014-1**: `FixedString32` bytes `[0x01, 'H', 'i', 0x02, '!', 0x00]` → chunks are
  `[("Hi", Red), ("!", Green)]`.
- **SC-GZ014-2**: `FixedString32("Hello")` (no control bytes) → `[("Hello", White)]`.
- **SC-GZ014-5**: Allocation test — call `ParseChunks` in a loop and verify no GC pressure
  (use `GC.GetTotalMemory` before and after; accept <1KB allocation for the returned `List<>`
  since it is returned by value and GC'd immediately — the spec says no heap allocation per
  `DrawRichTextBadge` call but `ParseChunks` returning a list IS allocating; document this
  deviation if the full DrawRichTextBadge is not testable without Raylib).

---

## Accessing DebugPrimitive fields

Before implementing, read:
`d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Primitives\DebugPrimitive.cs`

The struct uses `[StructLayout(LayoutKind.Explicit)]`. Field names and offsets:
- `[FieldOffset(0)] byte Shape`
- `[FieldOffset(1)] byte Space`
- `[FieldOffset(2)] Rgba32 Color` (4 bytes)
- `[FieldOffset(6)] byte TargetView` (PipelineTarget)
- `[FieldOffset(7)] byte DebugLayer`
- `[FieldOffset(14)] byte SizeMode`
- `[FieldOffset(15)] byte ZIndex`
- `[FieldOffset(16)] ushort ThicknessU16`
- `[FieldOffset(18)] byte MinZoomLod`
- `[FieldOffset(19)] byte MaxZoomLod`
- `[FieldOffset(8)] ushort AnchorIndex` (for EntityLocal or overlay anchor)
- `[FieldOffset(12)] ushort AnchorGeneration`
- Payload at [24]: Line uses `LineStartX/Y/Z` at [24]/[28]/[32] and `LineEndX/Y/Z` at [36]/[40]/[44]
  (check exact field names in the file)

Also read `PickToken.cs` to understand `Token.IsValid` and the `Entity Target`/`SubElementId` fields.

## SimTransform location

Search for `SimTransform` in the codebase:
`grep_search "struct SimTransform"` or look in `Hrot/Engine/Hrot.Core/`.

If `SimTransform` is not accessible from `Fdp.Presentation`, use `ISimulationView.HasComponent<SimTransform>`
may not work either. In that case, for GZ012, abstract the position resolution behind an
`Func<Entity, Vector2?> entityPositionResolver` constructor parameter injected in the renderer.
Tests pass a lambda; production code passes a lambda that reads `SimTransform`.

## Verification commands

From `d:\Work\IOS-IG-SimHost-FDP-2`:
```powershell
dotnet build FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj --nologo
dotnet test FDP\Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmos"
```

## Deliverables

1. `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`
2. `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` (modified)
3. `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/RichTextRenderer.cs`
4. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugPrimitiveRenderer2DTests.cs`
5. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugGizmoLayerGizmoTests.cs`
6. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/RichTextRendererTests.cs`

## Report

Write `d:\Work\IOS-IG-SimHost-FDP-2\.dev\gizmos-1\reports\BATCH-05-REPORT.md` listing:
- Files created/modified
- Test results: Presentation gizmo test pass count
- Design deviations (especially DebugGizmoLayer canvas access issue and SimTransform access)
