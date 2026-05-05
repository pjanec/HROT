# BATCH-01 Report

**Batch:** BATCH-01
**Tasks:** TASK-GZ001, TASK-GZ002, TASK-GZ003, TASK-GZ019
**Status:** COMPLETE
**Build:** Succeeded (0 errors, pre-existing warnings in unrelated Hrot test files only)
**Tests:** 52 passed / 0 failed / 0 skipped

---

## Files Delivered

### TASK-GZ001 — Primitive Type Definitions

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/Rgba32.cs`
  - `[StructLayout(LayoutKind.Sequential, Size = 4)]` struct
  - Fields: `byte R, G, B, A`; constructor with optional `a=255`
  - Constants: `Red`, `Green`, `Yellow`, `White`, `Black`, `Transparent`
  - `IEquatable<Rgba32>`, `==`/`!=` operators

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/PipelineTarget.cs`
  - `[Flags] enum PipelineTarget : byte` — `None=0`, `Map2D=1`, `Viewport3D=2`, `All=3`

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/CoordinateSpace.cs`
  - `enum CoordinateSpace : byte` — `World=0`, `Screen=1`, `EntityLocal=2`

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/SizeMode.cs`
  - `enum SizeMode : byte` — `WorldMeters=0`, `ScreenPixels=1`

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitiveShape.cs`
  - `enum DebugPrimitiveShape : byte` — 8 values (Line..ComponentInspector)

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/ScreenAnchor.cs`
  - `enum ScreenAnchor : byte` — 7 values (TopLeft..BottomRight)

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/PickToken.cs`
  - `struct PickToken` — `Entity Target`, `uint SubElementId`, `bool IsValid`

### TASK-GZ002 — DebugPrimitive 64-byte struct

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitive.cs`
  - `[StructLayout(LayoutKind.Explicit, Size = 64)] unsafe struct DebugPrimitive`
  - Header: offsets 0-23 (Shape, Space, Color, TargetView, DebugLayer, AnchorIndex/StringHash overlay, AnchorGeneration, SizeMode, ZIndex, ThicknessU16, MinZoomLod, MaxZoomLod, LifetimeSeconds)
  - Payload union at offsets 24-63 covering: Line, Sphere, Box2D, Arrow, Text, EntityBadge, Icon, ComponentInspector
  - Icon uses `float IconWorldPosX/Y` at [24]/[28] (2D) so FixedString32 fits at [32]-[63]
  - `BadgeRichText` and `IconAtlasCoord` alias `TextContent` at offset 32
  - Properties: `float Thickness`, `Entity Anchor`
  - Factory methods: `MakeLine`, `MakeSphere`, `MakeArrow`, `MakeText`

### TASK-GZ003 — IDebugDrawBuilder + DebugPrimitiveBuffer

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IDebugDrawBuilder.cs`
  - Interface with: `DrawLine`, `DrawLineGradient`, `DrawSphere`, `DrawArrow`, `DrawText`, `DrawTextLong`, `DrawEntityBadge`, `DrawEntityLocal`

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs`
  - `sealed class DebugPrimitiveBuffer : IDebugDrawBuilder`
  - Pre-allocated `DebugPrimitive[]`; `Interlocked.Increment` for lock-free thread-safe appends
  - Capacity overflow: increments `_droppedCount`, no exception
  - `GetFrame()` returns `ReadOnlySpan<DebugPrimitive>`
  - `Clear()` resets `_count` and `_droppedCount`

### TASK-GZ019 — StringInternMap + DrawTextLong

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs`
  - `sealed class StringInternMap`; `Dictionary<uint, string>` backing store
  - `Intern(uint hash, string text)` — idempotent; first registration wins
  - `TryResolve(uint hash)` — returns `null` for unknown hashes
  - `Flush()` clears all entries
  - `static uint Fnv1a32(string text)` — FNV-1a 32-bit hash

- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/StringInternBatch.cs`
  - DDS partial struct: `[DdsTopic("StringInternBatch")]`; reliable/TransientLocal/KeepLast(1)
  - Fields: `[DdsKey] uint FrameNumber`, `[DdsManaged] uint[] Hashes`, `[DdsManaged] string[] Texts`

### Tests

- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosPrimitiveTests.cs`
  - `Rgba32Tests` (15 tests): size, constructor, constants, equality
  - `DebugPrimitiveTests` (12 tests): size=64, factory values, thickness round-trip, offset isolation, alias properties
  - `DebugPrimitiveBufferTests` (10 tests): draw methods, gradient, capacity overflow, Clear, InternMap exposure, IDebugDrawBuilder assignment
  - `StringInternMapTests` (10 tests): Fnv1a32 determinism, empty string, Intern idempotency, TryResolve, Flush, Entries, DrawTextLong hash+inline

---

## Design Deviations

- **Icon payload**: Spec listed `Vector3 IconWorldPos` at [24] + `FixedString32` at [36], which would extend to byte 68, exceeding the 64-byte limit. Resolution: use `float IconWorldPosX/Y` at [24]/[28] (Icon is always rendered in 2D), keeping FixedString32 at [32]-[63] within budget.

---

## Known Issues / Gaps

None. All SC acceptance conditions from TASK-DETAIL.md covered.
