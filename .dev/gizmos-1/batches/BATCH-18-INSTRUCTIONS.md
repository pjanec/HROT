# BATCH-18: Phase 18 Data Plane Correctness and Schema Discovery

**Batch Number:** BATCH-18
**Tasks:** TASK-GZ050, TASK-GZ051, TASK-GZ052
**Phase:** Phase 18 (Data Plane Correctness and Schema Discovery)
**Estimated Effort:** 10-14 hours
**Priority:** HIGH — GZ050/GZ051 fix abstraction leaks in the DDS primitive stream; GZ052 enables runtime schema discovery
**Dependencies:** BATCH-17 complete (PipelineTarget.NodeGraph, CoordinateSpace on events)

---

## Mandatory Reading (IN ORDER)

1. **Task Definitions:** `.dev/gizmos-1/TASK-DETAIL.md` — sections TASK-GZ050, TASK-GZ051, TASK-GZ052
2. **Design Document:** `.dev/gizmos-1/DESIGN.md`
3. **Coding Standards:** `AGENTS.md`
4. **Previous Review:** `.dev/gizmos-1/reviews/BATCH-17-REVIEW.md`
5. **Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

## Source Code Locations

- **DebugPrimitiveShape enum:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitiveShape.cs`
- **DebugPrimitive struct:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`
- **GenericMessages.cs:** `Hrot/Network/Hrot.Network.NED/GenericMessages.cs`
- **SimHostApp:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- **JsonAttributeCompiler:** search: `grep -r "JsonAttributeCompiler" Hrot/ FDP/`
- **Test projects:**
  - `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/`
  - `Hrot/Network/Hrot.Network.NED.Tests/`
  - `FDP/Toolkits/Fdp.Toolkits.Tests/`

## Build and Test Commands

```
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
dotnet test FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/Fdp.Diagnostics.Contracts.Tests.csproj
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
```

## Pre-existing Failures (Do NOT count against your work)
- ~26 in Fdp.Toolkits.Tests (non-gizmo areas)
- ~4 in Hrot.IG.Tests (CS011 EntityInfoTranslator)
- ~3 in Fdp.Presentation.Tests (EntityInspectorPanelTests)
- ~20 in Hrot.SimHost.Tests

---

## Context

Phase 18 addresses three inter-related problems with the primitive data plane:

1. **GZ050** extends `DebugPrimitiveShape` with three new values and adds their payload layouts
   to `DebugPrimitive`. `SpatialAnchor` (shape 10) is architecturally significant: it enables
   decoupled map viewers with no ECS access.
2. **GZ051** fixes the `ComponentInspector` abstraction leak: ECS entity slot indices and
   runtime-assigned component type integers are replaced with globally-stable `NetworkId` (long)
   and `SchemaHash` (uint FNV-1a). This makes the primitive DDS-safe.
3. **GZ052** adds a `EntityAttributeSchemaPublisherSystem` that broadcasts the SimHost attribute
   schema as a JSON document over DDS so ExCon can discover fields at runtime.

**CRITICAL ORDER:** GZ050 and GZ051 both modify `DebugPrimitive`. Do GZ050 first (shape enum +
payload fields for the new shapes), then GZ051 (rearrange `ComponentInspector` fields). This
avoids intermediate struct size violations. Verify `Marshal.SizeOf<DebugPrimitive>() == 64` after
EACH of the two tasks before moving to GZ052.

---

## Mandatory Workflow

Complete tasks in sequence. Build and verify tests pass before proceeding:

1. **GZ050** → extend shapes + payloads → write tests → `Marshal.SizeOf<DebugPrimitive>() == 64` → pass
2. **GZ051** → rearrange ComponentInspector fields → write tests → `Marshal.SizeOf<DebugPrimitive>() == 64` → pass
3. **GZ052** → add EntityAttributeSchema topic + publisher system → write tests → pass

Run `dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly` after each task.

---

## Tasks

### Task 1: GZ050 — Introduce Semantic and Routing Primitives

**Task Definition:** Read TASK-GZ050 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to modify:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitiveShape.cs`
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`

**Changes:**

**Step 1 — Extend `DebugPrimitiveShape`:**
Add three new values to the byte enum after `ComponentInspector = 7`:
```csharp
SemanticShape = 8,  // Entity semantic profile primitive (DIS type / tactical shape)
MilStd2525    = 9,  // NATO MIL-STD-2525 symbology frame
SpatialAnchor = 10  // Pre-resolved world position + orientation; severs SimTransform dependency
```
Existing values 0-7 must NOT change.

**Step 2 — Add payload fields to `DebugPrimitive`:**

The struct uses `[StructLayout(LayoutKind.Explicit)]`. Add payload fields for each new shape
value as described in TASK-GZ050. Key layout constraints:

For `SemanticShape` (bytes 24-63):
- `[FieldOffset(24)] public ulong ProfileId;` (8 bytes)
- `[FieldOffset(32)] public float LengthMeters;` (4 bytes)
- `[FieldOffset(36)] public float WidthMeters;` (4 bytes)
- `[FieldOffset(40)] public uint ConditionMask;` (4 bytes)
- bytes 44-63: unused padding

For `MilStd2525` (bytes 24-63):
- `[FieldOffset(24)] public float MilWorldPosX;` (4 bytes)
- `[FieldOffset(28)] public float MilWorldPosY;` (4 bytes)
- `[FieldOffset(32)] public FixedString32 SidcCode;` — aliases `TextContent` at offset 32

For `SpatialAnchor` (bytes 24-63):
- `[FieldOffset(24)] public long NetworkId;` (8 bytes)
- `[FieldOffset(32)] public float AnchorWorldX;` (4 bytes)
- `[FieldOffset(36)] public float AnchorWorldY;` (4 bytes)
- `[FieldOffset(40)] public float AnchorWorldZ;` (4 bytes)
- `[FieldOffset(44)] public float Heading;` (4 bytes)
- `[FieldOffset(48)] public float Pitch;` (4 bytes)
- `[FieldOffset(52)] public float Roll;` (4 bytes)
- bytes 56-63: unused

**IMPORTANT:** `SidcCode` aliases `TextContent`. Both are `FixedString32` at offset 32.
This is intentional — you do NOT add a separate field at an overlapping offset that is not a
`FixedString32`. The same physical bytes serve both shapes.

After all additions, verify:
```csharp
Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());
```
This MUST pass before writing any tests. If the size is wrong, fix the field offsets.

**Test file:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` (add to)

**Required tests (SC-GZ050):**
- SC-GZ050-1: Three assertions: `(int)SemanticShape == 8`, `(int)MilStd2525 == 9`, `(int)SpatialAnchor == 10`
- SC-GZ050-2: `Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());`
- SC-GZ050-3: `DebugPrimitive` with `Shape = SpatialAnchor`, `NetworkId = 42L`, `AnchorWorldX = 100f`, `Heading = 45f` round-trips through assignment and field access (struct field round-trip, not necessarily DDS serialization — just verify the fields read back correctly after assignment)
- SC-GZ050-4: `DebugPrimitive` with `Shape = SemanticShape`, `ProfileId = 0x3400010001000000UL`, `LengthMeters = 12.5f`, `ConditionMask = 3u` — fields read back correctly
- SC-GZ050-5: A renderer loop using `switch(prim.Shape)` with `default: continue` silently skips an unrecognized shape value (test: set `Shape = (DebugPrimitiveShape)11`, iterate, verify no exception and the primitive is not processed)
- SC-GZ050-6 (regression): All existing `GizmosPrimitiveTests` / `ContractsStandaloneTests` SC-GZ assertions still pass

---

### Task 2: GZ051 — Fix ComponentInspector Abstraction Leak

**Task Definition:** Read TASK-GZ051 in `.dev/gizmos-1/TASK-DETAIL.md`

**File to modify:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs` (rearrange ComponentInspector fields)
- Any callsite that sets `InspTargetIndex`, `InspTargetGen`, or `InspComponentTypeId` directly

**Step 1 — Replace ComponentInspector payload fields:**

Current layout (bytes 24-47 for ComponentInspector):
- `[FieldOffset(24)] public int InspTargetIndex;`
- `[FieldOffset(28)] public ushort InspTargetGen;`
- `[FieldOffset(30)] public ScreenAnchor InspAnchor;` (1 byte enum)
- `[FieldOffset(32)] public int InspComponentTypeId;`
- `[FieldOffset(36)] public float InspOffsetX;`
- `[FieldOffset(40)] public float InspOffsetY;`
- `[FieldOffset(44)] public byte InspIsReadOnly;`

New layout:
- `[FieldOffset(24)] public long InspNetworkId;` (8 bytes, offsets 24-31)
- `[FieldOffset(32)] public uint InspSchemaHash;` (4 bytes, offsets 32-35)
- `[FieldOffset(36)] public ScreenAnchor InspAnchor;` (1 byte, offset 36)
- `[FieldOffset(37)] public byte InspIsReadOnly;` (1 byte, offset 37)
- bytes 38-39: unused padding
- `[FieldOffset(40)] public float InspOffsetX;` (4 bytes, offset 40)
- `[FieldOffset(44)] public float InspOffsetY;` (4 bytes, offset 44)
- bytes 48-63: unused

After the change, run `Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());` again.

**Step 2 — Find and update all callsites:**

Search for any code that assigns `InspTargetIndex`, `InspTargetGen`, or `InspComponentTypeId`.
These are now build errors. Update each callsite:
- Replace `InspTargetIndex` / `InspTargetGen` with `InspNetworkId` — resolve the entity's network ID via the appropriate adapter
- Replace `InspComponentTypeId` with `InspSchemaHash` — compute via `GizmoSettingsRegistry.ComputeHash(typeof(T).FullName!)`

If `ISimulationView` needs a `GetEntityNetworkId(Entity)` extension, add it as described in TASK-GZ051.

**Step 3 — Update `DrawComponentInspector<T>` in the builder implementation** (wherever the
primitive is actually constructed) to use `InspNetworkId` and `InspSchemaHash` instead of the
removed fields.

**IMPORTANT:** Do NOT change the signature of `DrawComponentInspector<T>` — only the
implementation.

**Test file:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` (add to)

**Required tests (SC-GZ051):**
- SC-GZ051-1: `DebugPrimitive` with `Shape = ComponentInspector`, `InspNetworkId = 12345L` — field reads back as `12345L`
- SC-GZ051-2: Compile-time: `InspTargetIndex` and `InspComponentTypeId` no longer exist (verified by the build succeeding with no reference to those names)
- SC-GZ051-3: `InspSchemaHash` for a sample type name equals `GizmoSettingsRegistry.ComputeHash("MyNamespace.MyType")` (FNV-1a consistency — compute hash via the registry method and compare)
- SC-GZ051-4: `Marshal.SizeOf<DebugPrimitive>() == 64` still holds
- SC-GZ051-5: A string built as `$"Entity:{prim.InspNetworkId} Schema:{prim.InspSchemaHash:X8}"` is constructable without ECS dependencies (pure struct field access test)
- SC-GZ051-6: `InspNetworkId` is at `FieldOffset(24)` and `InspSchemaHash` is at `FieldOffset(32)` (verified via `Marshal.OffsetOf`)

---

### Task 3: GZ052 — Entity Attribute Schema Broadcast

**Task Definition:** Read TASK-GZ052 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to create:**
- `Hrot/Network/Hrot.Network.NED/Attributes/EntityAttributeSchemaPublisherSystem.cs`

**Files to modify:**
- `Hrot/Network/Hrot.Network.NED/GenericMessages.cs` — add `EntityAttributeSchema` DDS topic struct
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — register the new publisher system
- `Hrot/Engine/Hrot.Core/Attributes/JsonAttributeCompiler.cs` (or wherever it lives — find it) — add `ExportSchema()` method

**Step 1 — Add `EntityAttributeSchema` DDS topic to `GenericMessages.cs`:**
```csharp
[DdsTopic("EntityAttributeSchema")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
[DdsManaged]
public partial struct EntityAttributeSchema
{
    [DdsKey]
    public int NodeId;

    // JSON Schema document describing all attribute paths, types, and validation rules.
    [DdsManaged] public string SchemaJson;
}
```

**Step 2 — Create `EntityAttributeSchemaPublisherSystem`:**
See TASK-GZ052 for the exact class body. Key points:
- `[UpdateInPhase(SystemPhase.PreSimulation)]` attribute
- `_isDefaultProcessor` guard: only the default processor publishes
- `_published` gate: publish once only
- Calls `_compiler.ExportSchema()` to get the JSON

**Step 3 — Add `ExportSchema()` to `JsonAttributeCompiler`:**
Find `JsonAttributeCompiler` (search: `grep -r "class JsonAttributeCompiler" Hrot/ FDP/`).
Add a method that returns a JSON string. The JSON must be parseable by `JsonDocument.Parse`.
If the compiler has a list of registered attribute processors/paths, enumerate them as JSON
properties. If not, return a minimal valid JSON schema: `{"$schema":"draft-07","properties":{}}`.

The goal is for `ExportSchema()` to return valid JSON. The exact content richness depends on what
`JsonAttributeCompiler` already exposes internally. Do the minimum to make SC-GZ052-4 and
SC-GZ052-5 pass — if `JsonAttributeCompiler` already has registered paths, include them.

**Step 4 — Wire in `SimHostApp`:**
Register `EntityAttributeSchemaPublisherSystem` in the SimHost kernel. Find the
`_isDefaultProcessor` field in `SimHostApp` (used by other systems — search
`grep -r "isDefaultProcessor\|_isDefaultProcessor\|IsDefaultProcessor" Hrot/Subsystems/Hrot.SimHost/`).
Add the `EntityAttributeSchemaWriter` property to the SimHost network adapter interface if absent.

**Test file:** Create `Hrot/Network/Hrot.Network.NED.Tests/EntityAttributeSchemaTests.cs`

**Required tests (SC-GZ052):**
- SC-GZ052-1: `EntityAttributeSchema` struct has `DdsTopicAttribute` with name `"EntityAttributeSchema"`, has `NodeId` key field, has `SchemaJson` string field (reflection checks)
- SC-GZ052-2: `Execute` called 10 times writes exactly once to DDS writer
- SC-GZ052-3: `isDefaultProcessor = false` → `Execute` never writes
- SC-GZ052-4: `ExportSchema()` returns valid JSON parseable by `JsonDocument.Parse` without exception
- SC-GZ052-5: Exported JSON contains at least one property entry (not empty `{}`) if the compiler has any registered paths; or is valid minimal JSON if not

---

## Quality Standards

**TEST QUALITY:**
- NOT ACCEPTABLE: Tests that only verify "no exception thrown"
- REQUIRED: Tests that verify struct field values after assignment
- REQUIRED: `Marshal.SizeOf` and `Marshal.OffsetOf` assertions for layout verification
- REQUIRED: Tests for negative cases (wrong discriminator silently skipped, non-default processor never writes)

**STRUCT LAYOUT:**
- `DebugPrimitive` is a 64-byte explicit-layout struct. Every new or relocated field must fit within bytes 0-63
- After EVERY structural change to `DebugPrimitive`, run `Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>())`
- Fields at the same offset must be the same size (union members); check this when `SidcCode` aliases `TextContent`

**CODE QUALITY:**
- `EntityAttributeSchemaPublisherSystem` must be in `Hrot.Network.NED` (not `Hrot.SimHost`) — it is a network concern
- Do not expose ECS types (`Entity`, `ISimulationView`) in `Fdp.Diagnostics.Contracts` — the assembly boundary is strict
- Do not modify `GizmoSettingsRegistry.ComputeHash` — it is used as-is for `InspSchemaHash`

---

## Success Criteria

This batch is DONE when:
- [ ] GZ050: `SemanticShape=8`, `MilStd2525=9`, `SpatialAnchor=10`; payload fields in `DebugPrimitive`; `Marshal.SizeOf == 64`; 6 tests pass
- [ ] GZ051: `InspNetworkId` (long at offset 24) and `InspSchemaHash` (uint at offset 32); old ECS fields removed; `Marshal.SizeOf == 64`; 6 tests pass
- [ ] GZ052: `EntityAttributeSchema` topic struct; `EntityAttributeSchemaPublisherSystem`; `ExportSchema()`; 5 tests pass
- [ ] Build: `dotnet build IOS-IG-SimHost.sln` → 0 errors
- [ ] All new tests pass; no new pre-existing failures introduced
- [ ] TASK-TRACKER.md updated (GZ050, GZ051, GZ052 marked done)
- [ ] Report submitted

---

## Report Submission

**Submit your report to:** `.dev/gizmos-1/reports/BATCH-18-REPORT.md`

**If you have questions:** `.dev/gizmos-1/questions/BATCH-18-QUESTIONS.md`

## Developer Insights (required in report)

**Q1:** What was the most difficult part of GZ051 (the ComponentInspector field relayout)?

**Q2:** Did `JsonAttributeCompiler` already expose registered paths, or did you have to add infrastructure?

**Q3:** How did you verify that `SidcCode` and `TextContent` correctly alias at offset 32?

**Q4:** What issues arose from updating callsites of the removed `InspTargetIndex`/`InspComponentTypeId` fields?

**Q5:** Suggested commit message.
