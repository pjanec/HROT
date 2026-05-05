# BATCH-05 Report — 2D Presentation Adapter (GZ011-GZ014)

## Status: COMPLETE

All tasks implemented. Build is clean. 24 gizmo tests pass.

---

## Files Created

| File | Purpose |
|---|---|
| `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs` | Filter/sort/dispatch pipeline (GZ011, GZ012) |
| `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/RichTextRenderer.cs` | Control-byte rich-text parser + Raylib drawer (GZ014) |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugPrimitiveRenderer2DTests.cs` | Tests: SC-GZ011-1..9 + SC-GZ012-1,2 |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/DebugGizmoLayerGizmoTests.cs` | Tests: SC-GZ013-1..4 |
| `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/RichTextRendererTests.cs` | Tests: SC-GZ014-1,2,5 + Yellow/Unknown |

## Files Modified

| File | Changes |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitive.cs` | Added `Token` computed property |
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` | Added 2 extra constructors, Draw integration, HandleInput hit-testing |

---

## Test Results

```
dotnet test ... --filter "FullyQualifiedName~Gizmos"
Passed! - Failed: 0, Passed: 24, Skipped: 0, Total: 24
```

Pre-existing failing tests (not related to this batch): `EntityInspectorPanelTests` (3 tests), failing before and after.

---

## Design Decisions and Deviations

### 1. Token as Computed Property (GZ011)
The spec implied a stored `Token` field in `DebugPrimitive`. Since the struct is `[StructLayout(Explicit, Size=64)]` with a fully-declared union, no additional storage bytes are available. Token is instead a computed property derived from the existing `Anchor` (AnchorIndex + AnchorGeneration):
```csharp
public PickToken Token => new PickToken { Target = Anchor, SubElementId = 0 };
```
`Token.IsValid` maps to `!Anchor.IsNull`, which behaves correctly for all existing fields.

### 2. HandleInput Cannot Push Proxy Tool (GZ013)
`DebugGizmoLayer` has no canvas/tool-stack reference. The spec mentioned pushing a proxy selection tool. This cannot be done without injecting the tool system. **Decision:** publish a `GizmoInteractionStartedEvent` with the `PickToken` and `WorldPos` instead. The event contains all the information needed for a higher-level controller to react.

### 3. EntityLocal Via EntityRepository, Not Func<Entity,Vector2?> (GZ012)
The spec suggested a `Func<Entity, Vector2?>` resolver injected into the renderer. The actual codebase already provides `ISimulationView` (via `EntityRepository`) with `IsAlive`, `HasComponent<T>`, and `GetComponentRO<T>`. This is more accurate (full 3D transform, not just 2D position) and consistent with other engine systems.

### 4. Moq Cannot Mock ref readonly Returns (GZ012 tests)
Moq 4.20.72 expression trees do not support `ref readonly T` return methods (`CS8153`, `CS1615`). The test stubs for GZ012 (`DebugPrimitiveRenderer2DEntityLocalTests`) were rewritten to use a real `EntityRepository` instance instead of a mock. This gives correct `IsAlive`/`HasComponent`/`GetComponentRO` behavior and removes the Moq dependency from those specific tests.

### 5. ParseChunks Allocates List<> (GZ014)
`RichTextRenderer.ParseChunks` allocates a `List<(string, Color)>` per call. The spec suggested zero-allocation for the hot-path. Since `DrawRichTextBadge` is only called per-frame for visible badge entities, this is an acceptable allocation budget (noted as P3 tech debt). `Raylib.DrawText` itself allocates, so zero-allocation cannot be achieved at this level anyway.

---

## Issues Encountered

1. **ushort cast overflow (CS0221):** `(ushort)~(1u << 5)` does not compile because `~(uint)` produces `uint` which overflows `ushort`. Fixed with `unchecked((ushort)~(1u << 5))`.

2. **Moq + ref readonly (CS8153/CS1615):** As noted above, switched to real `EntityRepository` for GZ012 tests.

3. **unsafe keyword in test project:** `Fdp.Presentation.Tests` does not enable `AllowUnsafeBlocks`. `RichTextRendererTests.MakeRaw` was initially written with `unsafe`. Fixed by using `Unsafe.As<FixedString32, byte>(ref result)` + `MemoryMarshal.CreateSpan` without the `unsafe` keyword.

---

## Weak Points Spotted in the Codebase

- **FDP.sln references missing projects** (`Fdp.ModuleHost.Core`, `Fdp.ModuleHost.Benchmarks`). These produce MSB3202 errors on full solution build. This is a pre-existing issue.
- **EntityInspectorPanel tests** (3 pre-existing failures) may indicate a regression from a prior PR or a missing test-environment dependency.
- **RichTextRenderer.ParseChunks** uses `Unsafe.As<FixedString32, byte>` which ties the parser to the exact `FixedString32` memory layout. If `FixedString32` is refactored, this will silently break.
