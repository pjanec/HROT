# BATCH-11 Instructions — Presentation Fidelity + Data Plane Correctness

**Covers:** Phase 9 (Tasks GZ025–GZ028) + Phase 10 (Tasks GZ029–GZ030)
**Target branch:** main
**Build command:** `dotnet build IOS-IG-SimHost.sln`
**Test command:** `dotnet test FDP\Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj` and `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj`
**Pre-existing failures:** 26 in Fdp.Toolkits.Tests (Combat, Behavior, Geographic, Navigation, Scenario — do not touch these); 4 in Hrot.IG.Tests (EntityInfoTranslator CS011_*).
**You must not introduce any new failures.**

---

## Context

All files referred to below live under `d:\Work\IOS-IG-SimHost-FDP-2\`.

Key types:
- `DebugPrimitive` — 64-byte blittable tagged union (`FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Primitives\DebugPrimitive.cs`)
- `DebugPrimitiveBuffer` — frame buffer implementing `IDebugDrawBuilder` (`FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\DebugPrimitiveBuffer.cs`)
- `IDebugDrawBuilder` — draw builder interface (`FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\IDebugDrawBuilder.cs`)
- `DebugGizmoLayer` — `IMapLayer` rendering gizmo primitives (`FDP\Engine\Fdp.Presentation\Vis2D\Layers\DebugGizmoLayer.cs`)
- `DebugPrimitiveRenderer2D` — Raylib renderer, subclassable for tests (`FDP\Engine\Fdp.Presentation\Vis2D\Gizmos\DebugPrimitiveRenderer2D.cs`)
- `GizmoInteractionProxyTool` — `IMapTool` pushed on click (`FDP\Engine\Fdp.Presentation\Vis2D\Gizmos\GizmoInteractionProxyTool.cs`)
- `MapCanvas` — composable canvas with tool stack (`FDP\Engine\Fdp.Presentation\Vis2D\MapCanvas.cs`)
- `TestableMapCanvas` — Raylib-free test subclass in `Fdp.Presentation.Tests\Vis2D\MapCanvasTests.cs`
- `DataDrivenGizmoSystem` — ECS system that runs stateful gizmos (`FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Systems\DataDrivenGizmoSystem.cs`)

---

## TASK-GZ025 — Fix DebugGizmoLayer Activation Chain

**Problem:** `DebugGizmoLayer.HandleInput` can only publish `GizmoInteractionStartedEvent` via the
event bus; it cannot push `GizmoInteractionProxyTool` because it has no `MapCanvas` reference.

### GizmoInteractionProxyTool.cs

**Modify** `FDP\Engine\Fdp.Presentation\Vis2D\Gizmos\GizmoInteractionProxyTool.cs`:

1. Add `private Vector3 _worldPos;` field.
2. Change constructor to accept optional `worldPos`:
   ```csharp
   public GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus, Vector3 worldPos = default)
   {
       _token    = token;
       _eventBus = eventBus;
       _worldPos = worldPos;
   }
   ```
3. Modify `OnEnter` to publish `GizmoInteractionStartedEvent` when entering:
   ```csharp
   public void OnEnter(MapCanvas canvas)
   {
       _canvas = canvas;
       _eventBus.Publish(new GizmoInteractionStartedEvent
       {
           Token    = _token,
           WorldPos = _worldPos,
       });
   }
   ```

Existing `GizmoInteractionProxyToolTests` call `new GizmoInteractionProxyTool(token, bus)` — they
must still compile with the optional parameter. Do not change any existing test file in that class.

### DebugGizmoLayer.cs

**Modify** `FDP\Engine\Fdp.Presentation\Vis2D\Layers\DebugGizmoLayer.cs`:

1. Add field `private MapCanvas? _canvas;`.
2. **Extend the production constructor** (the 4-param one that takes buffer, eventBus, ISimulationView):
   - Add `MapCanvas? canvas = null` as the new 4th parameter (before `ISimulationView? view`).
   - Store it: `_canvas = canvas;`.
   - Do NOT change the no-buffer constructor (1-param) or the test-renderer constructor (4-param taking `DebugPrimitiveRenderer2D`).

   New signature:
   ```csharp
   public DebugGizmoLayer(
       int layerBitIndex,
       DebugPrimitiveBuffer buffer,
       FdpEventBus eventBus,
       MapCanvas? canvas = null,
       ISimulationView? view = null)
   ```

3. In `HandleInput`, replace the existing `DEVIATION` comment block and event publish with:
   ```csharp
   if (best.HasValue)
   {
       var worldPos3 = new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f);
       if (_canvas != null)
       {
           var proxy = new GizmoInteractionProxyTool(best.Value.Token, _eventBus!, worldPos3);
           _canvas.PushTool(proxy);
           // GizmoInteractionStartedEvent is published in proxy.OnEnter.
       }
       else
       {
           // Fallback: no canvas (unit test or stub setup); publish directly.
           _eventBus!.Publish(new GizmoInteractionStartedEvent
           {
               Token    = best.Value.Token,
               WorldPos = worldPos3,
           });
       }
       return true;
   }
   ```

### IgApplication.cs call site

**Modify** `Hrot\Subsystems\Hrot.IG\IgApplication.cs` line 1131:

Change:
```csharp
var gizmoLayer = new DebugGizmoLayer(31, _gizmoBuffer, _world.Bus);
```
To:
```csharp
var gizmoLayer = new DebugGizmoLayer(31, _gizmoBuffer, _world.Bus, _canvas);
```

### Tests for GZ025

**Create** `FDP\Engine\Fdp.Presentation.Tests\Vis2D\Layers\DebugGizmoLayerActivationTests.cs`
in project `Fdp.Presentation.Tests`.

Use `TestableMapCanvas` (from `MapCanvasTests.cs` in the same project) and `FdpEventBus`.
Build a `DebugPrimitiveBuffer` with one EntityLocal line primitive that has a valid Anchor
(AnchorIndex=1, AnchorGeneration=1) so `prim.Token.IsValid` returns true.

Required tests:
- **SC-GZ025-1**: After `HandleInput` with a world position at the primitive's location, `canvas.ActiveTool is GizmoInteractionProxyTool`.
- **SC-GZ025-2**: `GizmoInteractionStartedEvent` is published exactly once and contains the correct `Token`.
- **SC-GZ025-3**: Clicking outside any pickable primitive (far from all primitives) does NOT push any tool; `canvas.ActiveTool` is null.
- **SC-GZ025-5**: When `_canvas` is null (use the test-renderer constructor that already exists), the event is still published directly via the event bus.

For event capture, subscribe to `FdpEventBus` before calling `HandleInput`. Example:
```csharp
var events = new List<GizmoInteractionStartedEvent>();
bus.Subscribe<GizmoInteractionStartedEvent>(e => events.Add(e));
```

---

## TASK-GZ026 — Fix Spatial Hit-Testing in DebugGizmoLayer

**Problem:** Current hit-test only checks distance to `SphereCenter` or `LineStart`, ignoring
the body of lines, shape extents, and `SizeMode.ScreenPixels`.

### DebugGizmoLayer.cs (continued from GZ025 changes)

1. Add a `_lastCtx` field to capture the render context from `Draw`:
   ```csharp
   private RenderContext _lastCtx;
   ```
   In `Draw`, at the end of the method (after rendering), store: `_lastCtx = ctx;`

2. Replace `GetPrimitive2DPos` with geometry-aware `HitTest`:

   ```csharp
   private bool HitTest(in DebugPrimitive prim, Vector2 testPos, float hitRadius)
   {
       // SizeMode.ScreenPixels primitives rendered at fixed screen size — scale hit radius.
       float zoom = _lastCtx.Zoom > 0f ? _lastCtx.Zoom : 1f;
       float effectiveRadius = prim.SizeMode == SizeMode.ScreenPixels
           ? hitRadius / zoom
           : hitRadius;

       Vector2 checkPos = testPos;

       // Screen-space primitives: convert world pos to screen before testing.
       if (prim.Space == CoordinateSpace.Screen)
           checkPos = Raylib.GetWorldToScreen2D(testPos, _lastCtx.Camera);

       switch (prim.Shape)
       {
           case DebugPrimitiveShape.Sphere:
           {
               var center = new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y);
               return Vector2.Distance(checkPos, center) <= prim.SphereRadius + effectiveRadius;
           }
           case DebugPrimitiveShape.Line:
           {
               var p0 = new Vector2(prim.LineStart.X, prim.LineStart.Y);
               var p1 = new Vector2(prim.LineEnd.X,   prim.LineEnd.Y);
               return PointToSegmentDistance(checkPos, p0, p1) <= effectiveRadius;
           }
           case DebugPrimitiveShape.Arrow:
           {
               var p0 = new Vector2(prim.ArrowFrom.X, prim.ArrowFrom.Y);
               var p1 = new Vector2(prim.ArrowTo.X,   prim.ArrowTo.Y);
               return PointToSegmentDistance(checkPos, p0, p1) <= effectiveRadius;
           }
           case DebugPrimitiveShape.Box2D:
           {
               var center = new Vector2(prim.BoxCenterX, prim.BoxCenterY);
               return Vector2.Distance(checkPos, center) <= effectiveRadius
                   + MathF.Max(prim.BoxExtentX, prim.BoxExtentY);
           }
           default:
           {
               // Text, Icon, EntityBadge, others: AABB around origin.
               float tx = prim.Shape == DebugPrimitiveShape.Text ? prim.TextX
                   : prim.Shape == DebugPrimitiveShape.Sphere    ? prim.SphereCenter.X
                   : prim.LineStart.X;
               float ty = prim.Shape == DebugPrimitiveShape.Text ? prim.TextY
                   : prim.Shape == DebugPrimitiveShape.Sphere    ? prim.SphereCenter.Y
                   : prim.LineStart.Y;
               return Vector2.Distance(checkPos, new Vector2(tx, ty)) <= effectiveRadius;
           }
       }
   }

   private static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
   {
       var ab = b - a;
       float lenSq = ab.LengthSquared();
       if (lenSq < float.Epsilon) return Vector2.Distance(p, a);
       float t = MathF.Max(0f, MathF.Min(1f, Vector2.Dot(p - a, ab) / lenSq));
       var closest = a + ab * t;
       return Vector2.Distance(p, closest);
   }
   ```

3. In `HandleInput`, replace the `GetPrimitive2DPos` + Distance + `HitRadiusWorld` check with:
   ```csharp
   if (!HitTest(in prim, worldPos, HitRadiusWorld)) continue;
   if (best == null || prim.DebugLayer > best.Value.DebugLayer)
   {
       best = prim;
   }
   ```
   Remove the `bestDist` variable — layer preference is sufficient; within a layer the first match wins.

### Tests for GZ026

**Create** `FDP\Engine\Fdp.Presentation.Tests\Vis2D\Layers\DebugGizmoLayerHitTests.cs`.

Required tests:
- **SC-GZ026-1**: A click at the midpoint of a Line primitive (0,0)→(100,0) at world pos (50,0) triggers a hit.
- **SC-GZ026-2**: A click at (110,0) — past the endpoint by 10 units — does NOT trigger a hit (HitRadiusWorld=5).
- **SC-GZ026-3**: A click within SphereRadius=10 of Sphere center (50,50) at pos (55,50) triggers a hit.
- **SC-GZ026-4**: With SizeMode.ScreenPixels and a zoom of 2 stored in `_lastCtx`, a Sphere at (0,0) with radius 0 still has effective hit radius of HitRadiusWorld/zoom = 2.5. A point at (2,0) should hit; (6,0) should not.

For SC-GZ026-4, simulate zoom by setting `_lastCtx` via a Draw call with a fabricated RenderContext,
or by using the test constructor that accepts `DebugPrimitiveRenderer2D` (which still calls Draw).
Alternatively, expose a `SetLastCtxForTest(RenderContext ctx)` internal method or make `_lastCtx`
internal for test access.

---

## TASK-GZ027 — Fix EntityLocal Rendering for All Primitive Shapes

**Problem:** `DebugPrimitiveRenderer2D.Render` only applies the anchor's `SimTransform` to Line
primitives in `CoordinateSpace.EntityLocal`. Arrow, Sphere, Box2D, Text, Icon fall through to
world-space rendering at wrong positions.

### DebugPrimitiveRenderer2D.cs

**Modify** `FDP\Engine\Fdp.Presentation\Vis2D\Gizmos\DebugPrimitiveRenderer2D.cs`:

1. Add private static helpers after the class opening:
   ```csharp
   private static Vector3 ApplyTransform(in SimTransform tf, Vector3 local)
       => tf.Position + Vector3.Transform(local, tf.Rotation);

   private static Vector2 ApplyTransform2D(in SimTransform tf, float localX, float localY)
   {
       var world = ApplyTransform(tf, new Vector3(localX, localY, 0f));
       return new Vector2(world.X, world.Y);
   }

   private static float RotationDegrees2D(in SimTransform tf)
   {
       var q = tf.Rotation;
       return MathF.Atan2(
           2f * (q.W * q.Z + q.X * q.Y),
           1f - 2f * (q.Y * q.Y + q.Z * q.Z)
       ) * (180f / MathF.PI);
   }
   ```

2. In the `EntityLocal` resolution block (the `if (prim.Space == CoordinateSpace.EntityLocal)` branch),
   replace the current code (which only handles Line) with:

   ```csharp
   if (prim.Space == CoordinateSpace.EntityLocal)
   {
       var anchor = prim.Anchor;
       if (_view == null
           || !_view.IsAlive(anchor)
           || !_view.HasComponent<SimTransform>(anchor))
           continue;

       ref readonly var tf = ref _view.GetComponentRO<SimTransform>(anchor);
       DebugPrimitive resolved = prim;

       switch (prim.Shape)
       {
           case DebugPrimitiveShape.Line:
               resolved.LineStart = ApplyTransform(tf, prim.LineStart);
               resolved.LineEnd   = ApplyTransform(tf, prim.LineEnd);
               break;
           case DebugPrimitiveShape.Arrow:
               resolved.ArrowFrom = ApplyTransform(tf, prim.ArrowFrom);
               resolved.ArrowTo   = ApplyTransform(tf, prim.ArrowTo);
               break;
           case DebugPrimitiveShape.Sphere:
           {
               var c = ApplyTransform(tf, prim.SphereCenter);
               resolved.SphereCenter = c;
               break;
           }
           case DebugPrimitiveShape.Box2D:
           {
               var c = ApplyTransform2D(tf, prim.BoxCenterX, prim.BoxCenterY);
               resolved.BoxCenterX  = c.X;
               resolved.BoxCenterY  = c.Y;
               resolved.BoxAngleDeg = prim.BoxAngleDeg + RotationDegrees2D(tf);
               break;
           }
           case DebugPrimitiveShape.Text:
           {
               var c = ApplyTransform2D(tf, prim.TextX, prim.TextY);
               resolved.TextX = c.X;
               resolved.TextY = c.Y;
               break;
           }
           default:
           {
               // Icon and other shapes: transform the payload origin (IconWorldPos).
               var c = ApplyTransform2D(tf, prim.IconWorldPosX, prim.IconWorldPosY);
               resolved.IconWorldPosX = c.X;
               resolved.IconWorldPosY = c.Y;
               break;
           }
       }

       resolved.Space = CoordinateSpace.World;
       sortBuffer.Add(resolved);
       continue;
   }
   ```
   
   Note: The `continue` at the end means you must restructure the block so the `else { sortBuffer.Add(prim); }` path is still reached for non-EntityLocal primitives.

### Tests for GZ027

**Use existing** `FDP\Engine\Fdp.Presentation.Tests\Vis2D\Gizmos\DebugPrimitiveRenderer2DTests.cs`
(or create it if it does not exist). Inject a `CapturingRenderer2D` subclass that overrides
`DispatchShape` to record dispatched primitives without Raylib calls:

```csharp
private sealed class CapturingRenderer2D : DebugPrimitiveRenderer2D
{
    public List<DebugPrimitive> Captured { get; } = new();
    public CapturingRenderer2D(ISimulationView? view = null) : base(view) { }
    protected override void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
        => Captured.Add(prim);
}
```

For `ISimulationView`, use a minimal `FakeSimView` stub that implements `IsAlive`, `HasComponent<SimTransform>`, and `GetComponentRO<SimTransform>`. No need for `EntityRepository`.

Required tests:
- **SC-GZ027-1**: An EntityLocal Sphere at local offset (5,0,0) with no-rotation entity at world (10,10,0) dispatches `SphereCenter ≈ (15, 10, 0)`.
- **SC-GZ027-2**: An EntityLocal Arrow from (0,0,0) to (1,0,0) with entity rotated 90 degrees around Z-axis dispatches ArrowTo rotated 90 degrees (≈ (0,1,0) relative to entity position).
- **SC-GZ027-3**: An EntityLocal Text at local (0,2,0) with entity at (0,0,0) dispatches TextY ≈ 2.
- **SC-GZ027-4**: An EntityLocal Sphere for a dead entity (IsAlive returns false) is silently skipped — nothing dispatched.
- **SC-GZ027-5** (regression): An EntityLocal Line from (0,0,0) to (1,0,0) with entity at (5,5,0) dispatches LineStart ≈ (5,5,0) and LineEnd ≈ (6,5,0).

---

## TASK-GZ028 — Fix SizeMode.ScreenPixels for Shape Geometric Dimensions

**Problem:** `DispatchShape` scales `ThicknessU16` for ScreenPixels but leaves `SphereRadius`,
`ArrowHeadSize`, and `Box2D` extents in raw world units, so they grow with zoom.

### DebugPrimitiveRenderer2D.cs (continued)

In `DispatchShape`, after `float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;`:

```csharp
float geomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;
```

Apply `geomScale` to:
- **Sphere case**: `Raylib.DrawCircleV(center, prim.SphereRadius * geomScale, color);`
- **Arrow case**: `DrawArrow(from, to, prim.ArrowHeadSize * geomScale, color, thickness);`
- **Box2D case** (if Box2D dispatching exists; otherwise add it): pass `prim.BoxExtentX * geomScale` and `prim.BoxExtentY * geomScale` to the draw call.
- Do NOT alter the Text shape (font size is already screen pixels by convention).
- `WorldMeters` primitives are unaffected because `geomScale = 1f`.

### Tests for GZ028

Use the same `CapturingRenderer2D` pattern. Since CapturingRenderer2D records the resolved
primitive (not raw Raylib calls), you need to capture at the `DispatchShape` level. Create a
`SpyRenderer2D` that records the `zoom` and `geomScale` it computed, or (simpler) subclass
`DispatchShape` to compute and store the effective radius before calling base:

Actually, the simplest approach: override `DispatchShape` in a spy subclass that stores the
`prim` it received and what `geomScale` was used:

```csharp
private sealed class SpyRenderer2D : DebugPrimitiveRenderer2D
{
    public float LastGeomScale { get; private set; } = 1f;
    public float LastZoom { get; private set; } = 1f;
    public SpyRenderer2D() : base(null) { }
    protected override void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
    {
        LastZoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
        LastGeomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / LastZoom : 1f;
        // No Raylib calls.
    }
}
```

Required tests:
- **SC-GZ028-1**: A ScreenPixels Sphere at zoom=1.0 has geomScale=1.0; at zoom=2.0 has geomScale=0.5.
- **SC-GZ028-2**: A WorldMeters Sphere at zoom=2.0 has geomScale=1.0 (unaffected).
- **SC-GZ028-3**: An ScreenPixels Arrow at zoom=4.0 has geomScale=0.25.
- **SC-GZ028-4**: A Box2D with ScreenPixels at zoom=2.0 has geomScale=0.5.

---

## TASK-GZ029 — Implement LifetimeSeconds Persistent Primitive Re-emission

**Problem:** `DebugPrimitiveBuffer.Clear()` is never called in production code; the buffer fills
up and drops primitives after capacity is exhausted. Additionally, `LifetimeSeconds > 0` is
supposed to persist primitives across frames but is not implemented.

### IDebugDrawBuilder.cs

Add a default no-op `EndFrame` to the interface so existing mock implementations remain valid:

```csharp
/// <summary>
/// Called once per frame by the frame controller after all gizmos have drawn.
/// Advances the persistence clock by <paramref name="deltaTime"/> and prepares
/// the buffer for the next frame. Default no-op for implementations that do
/// not support persistence.
/// </summary>
void EndFrame(float deltaTime) { }
```

### DebugPrimitiveBuffer.cs

1. Add new fields after existing fields:
   ```csharp
   // Persistent re-emission: primitives with LifetimeSeconds > 0 survive across frames.
   private readonly DebugPrimitive[] _persistent;
   private readonly float[] _remainingLife;
   private int _persistentCount;
   private const int PersistentCapacity = 256;
   ```

2. Modify constructor to allocate:
   ```csharp
   public DebugPrimitiveBuffer(int capacity = 4096, StringInternMap? internMap = null)
   {
       _primitives  = new DebugPrimitive[capacity];
       _persistent  = new DebugPrimitive[PersistentCapacity];
       _remainingLife = new float[PersistentCapacity];
       _internMap   = internMap ?? new StringInternMap();
   }
   ```

3. Modify `Append` to also add persistent entries:
   ```csharp
   private void Append(DebugPrimitive p)
   {
       int slot = Interlocked.Increment(ref _count) - 1;
       if ((uint)slot < (uint)_primitives.Length)
           _primitives[slot] = p;
       else
           Interlocked.Increment(ref _droppedCount);

       // Persist primitives with a positive lifetime.
       if (p.LifetimeSeconds > 0f)
       {
           int pSlot = Interlocked.Increment(ref _persistentCount) - 1;
           if ((uint)pSlot < (uint)_persistent.Length)
           {
               _persistent[pSlot]  = p;
               _remainingLife[pSlot] = p.LifetimeSeconds;
           }
           else
           {
               Interlocked.Decrement(ref _persistentCount);
               Interlocked.Increment(ref _droppedCount);
           }
       }
   }
   ```

4. Add `EndFrame` method:
   ```csharp
   /// <summary>
   /// Advances the persistence clock, evicts expired entries, clears the transient buffer,
   /// and re-injects surviving persistent primitives. Call once per frame BEFORE gizmo
   /// systems execute.
   /// </summary>
   public void EndFrame(float deltaTime)
   {
       // Compact persistent array: keep entries whose remaining life exceeds deltaTime.
       int writeIdx = 0;
       int count = Math.Min(_persistentCount, _persistent.Length);
       for (int i = 0; i < count; i++)
       {
           float newLife = _remainingLife[i] - deltaTime;
           if (newLife > 0f)
           {
               _persistent[writeIdx]    = _persistent[i];
               _remainingLife[writeIdx] = newLife;
               writeIdx++;
           }
       }
       _persistentCount = writeIdx;

       // Reset the transient buffer.
       _count        = 0;
       _droppedCount = 0;

       // Re-inject surviving persistent primitives into the start of the transient buffer.
       for (int i = 0; i < _persistentCount; i++)
       {
           int slot = Interlocked.Increment(ref _count) - 1;
           if ((uint)slot < (uint)_primitives.Length)
               _primitives[slot] = _persistent[i];
           else
               Interlocked.Increment(ref _droppedCount);
       }
   }
   ```

5. The existing `Clear()` method remains but now only clears the transient part (which it already does).

### DataDrivenGizmoSystem.cs

At the **start** of `Execute(ISimulationView view, float deltaTime)` (before teardowns), add:
```csharp
// Advance the persistence clock and clear the previous frame's transient primitives.
_drawBuilder.EndFrame(deltaTime);
```

This means `DataDrivenGizmoSystem` owns the frame boundary. `StatelessGizmoSystem` does NOT call
`EndFrame` — it runs after DataDriven and adds to the already-reset buffer.

### Tests for GZ029

**Create** `FDP\Toolkits\Fdp.Toolkits.Tests\Diagnostics\Gizmos\DebugPrimitiveBufferPersistenceTests.cs`
in project `Fdp.Toolkits.Tests`.

Helper: use `DebugPrimitive.MakeLine(...)` with `LifetimeSeconds` set after construction, then
call `Append` via `DrawLine` or a public test-only method — actually, just call the public
`DrawLine`/`DrawSphere` methods and set `LifetimeSeconds` on the primitive via MakeLine. Since
`DrawLine` calls `Append(DebugPrimitive.MakeLine(...))` without LifetimeSeconds... you need an
approach. Best: use `DrawEntityLocal` with a `LifetimeSeconds`... but that also doesn't expose it.

Instead: add an internal `AppendRaw(DebugPrimitive p)` method (visible to tests in the same assembly)
OR just test `EndFrame` in terms of observable buffer size:

Simpler approach: Use `DrawLine` with a LifetimeSeconds = 0 (transient) and verify count after
EndFrame, then use the `DebugPrimitive.MakeLine` static helper directly + `Append` via reflection
OR test the indirect effect: add a persistent primitive, call EndFrame(smallDt), check GetFrame()
still contains it; add persistent primitive, call EndFrame(largeEnoughDt), check GetFrame() loses it.

Actually, the cleanest test approach is: make `Append` `internal` (it already is private — change to `internal`) so test project can call it. The test project references `Fdp.Toolkits`. Add `[assembly: InternalsVisibleTo("Fdp.Toolkits.Tests")]` to `Fdp.Toolkits.csproj` or to a new `AssemblyInfo.cs` if not already there.

Check if `InternalsVisibleTo` already exists in `Fdp.Toolkits`; if not, add it.

Required tests using internal `Append`:
- **SC-GZ029-1**: Append a primitive with LifetimeSeconds=0.5f. After EndFrame(0.1f), GetFrame() still contains it. After EndFrame(0.1f) three more times (total 0.4f consumed), still present. After EndFrame(0.15f) (total 0.55f > 0.5f), absent.
- **SC-GZ029-2**: Append a primitive with LifetimeSeconds=0. After EndFrame(0.016f), it does NOT appear in GetFrame().
- **SC-GZ029-3**: Fill _persistent to PersistentCapacity with LifetimeSeconds=1f primitives, then append one more persistent primitive — DroppedCount increments; no exception.
- **SC-GZ029-4**: Persistent primitive survives a Clear() cycle (Clear does NOT remove persistent entries). Then EndFrame(smallDt) re-injects it.
- **SC-GZ029-5**: EndFrame with deltaTime > LifetimeSeconds causes disappearance in next frame.

---

## TASK-GZ030 — Restore PickToken SubElementId Storage

**Problem:** `DebugPrimitive.Token` always returns `SubElementId = 0`, making multi-handle
gizmos unable to distinguish which handle was grabbed.

### DebugPrimitive.cs

**Modify** `FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Primitives\DebugPrimitive.cs`:

1. Verify offset 30 is used by `ComponentInspector` payload (`[FieldOffset(30)] public ScreenAnchor InspAnchor`). Do NOT place SubElementId at 30.

2. After `[FieldOffset(48)] public float ArrowHeadSize;` (which ends at 51), bytes 52-63 are free for Line and Arrow payloads. Place SubElementId here:
   ```csharp
   // SubElementId: used by interactive EntityLocal primitives to distinguish handles.
   // Offset 52 is free for Line (EndColor at 48-51) and Arrow (ArrowHeadSize at 48-51).
   // For Text/EntityBadge, offset 52 overlaps TextContent (FixedString32) — don't-care
   // since those shapes are never interactive.
   [FieldOffset(52)] public ushort SubElementId;
   ```

3. Update the `Token` computed property:
   ```csharp
   public PickToken Token => new PickToken { Target = Anchor, SubElementId = SubElementId };
   ```

4. Verify with a test that `Marshal.SizeOf<DebugPrimitive>() == 64` (this must remain true).

### IDebugDrawBuilder.cs

Add the interactive overload:
```csharp
void DrawEntityLocalInteractive(
    Entity anchor, Vector3 localStart, Vector3 localEnd,
    Rgba32 color, ushort subElementId,
    float thickness = 1f, byte layer = 0);
```

### DebugPrimitiveBuffer.cs

Implement the new interface method:
```csharp
public void DrawEntityLocalInteractive(
    Entity anchor, Vector3 localStart, Vector3 localEnd,
    Rgba32 color, ushort subElementId,
    float thickness = 1f, byte layer = 0)
{
    var p = default(DebugPrimitive);
    p.Shape            = DebugPrimitiveShape.Line;
    p.Space            = CoordinateSpace.EntityLocal;
    p.Color            = color;
    p.EndColor         = color;
    p.TargetView       = PipelineTarget.All;
    p.DebugLayer       = layer;
    p.SizeMode         = SizeMode.ScreenPixels;
    p.ThicknessU16     = (ushort)(thickness * 10f);
    p.AnchorIndex      = anchor.Index;
    p.AnchorGeneration = anchor.Generation;
    p.LineStart        = localStart;
    p.LineEnd          = localEnd;
    p.SubElementId     = subElementId;
    Append(p);
}
```

### Tests for GZ030

**Create** `FDP\Toolkits\Fdp.Toolkits.Tests\Diagnostics\Gizmos\DebugPrimitiveSubElementTests.cs`
in project `Fdp.Toolkits.Tests`.

Required tests:
- **SC-GZ030-1**: `Marshal.SizeOf<DebugPrimitive>() == 64`.
- **SC-GZ030-2**: `DrawEntityLocalInteractive(entity, start, end, color, subElementId: 3)` emits a primitive with `Token.SubElementId == 3`.
- **SC-GZ030-3**: Two calls with the same entity but different `subElementId` values (1 and 2) produce primitives with different `Token.SubElementId` values.
- **SC-GZ030-4**: A zero-value `DebugPrimitive` has `Token.SubElementId == 0`.
- **SC-GZ030-5** (regression): Existing `DebugPrimitive` size assertion (from prior SC-GZ002-* tests if they exist) still passes.

---

## Completion Checklist

Before submitting the report:

1. `dotnet build IOS-IG-SimHost.sln` → 0 errors.
2. `dotnet test FDP\Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj --no-build` → all new tests pass; no pre-existing regressions.
3. `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build` → all new tests pass; same 26 pre-existing failures; no new failures.
4. All 6 success-condition sets listed above are covered by the new tests.

Write the report to `.dev\gizmos-1\reports\BATCH-11-REPORT.md` following the standard report format:
- Summary section
- Files changed (list all modified and created files)
- Tests added (list each test method and which SC-GZ0xx condition it covers)
- Deviations from spec (explain any divergence from these instructions)
- Build and test output snippets
