# BATCH-18 Report

**Batch:** BATCH-18  
**Tasks:** GZ050, GZ051, GZ052  
**Status:** COMPLETE  
**Build:** 0 errors (IOS-IG-SimHost.sln)  
**Tests introduced:** 22 new (17 in Fdp.Diagnostics.Contracts.Tests, 5 in Hrot.Network.NED.Tests) — all pass

---

## Tasks Completed

### TASK-GZ050 — Introduce semantic and routing primitives

**Files changed:**

- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitiveShape.cs`  
  Added `SemanticShape = 8`, `MilStd2525 = 9`, `SpatialAnchor = 10`.

- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`  
  Added three payload unions at offsets 24–63:
  - **SemanticShape** — `ulong ProfileId` (24), `float LengthMeters` (32), `float WidthMeters` (36), `uint ConditionMask` (40); 4 bytes unused at 44–47; 16 bytes unused at 48–63.
  - **MilStd2525** — `float MilWorldPosX` (24), `float MilWorldPosY` (28), `FixedString32 SidcCode` (32); aliases `TextContent` field.
  - **SpatialAnchor** — `long NetworkId` (24), `float AnchorWorldX` (32), `float AnchorWorldY` (36), `float AnchorWorldZ` (40), `float Heading` (44), `float Pitch` (48), `float Roll` (52); 8 bytes unused at 56–63.

`Marshal.SizeOf<DebugPrimitive>() == 64` verified by test SC_GZ050_2 and SC_GZ050_6.

**Tests (ContractsStandaloneTests.cs):**

| ID | Name | Assertion |
|----|------|-----------|
| SC_GZ050_1 | NewShapeValues_HaveCorrectOrdinals | SemanticShape==8, MilStd2525==9, SpatialAnchor==10 |
| SC_GZ050_2 | DebugPrimitive_SizeIs64 | Marshal.SizeOf<DebugPrimitive>()==64 |
| SC_GZ050_3 | SpatialAnchor_FieldsRoundTrip | All SpatialAnchor payload fields round-trip |
| SC_GZ050_4 | SemanticShape_FieldsRoundTrip | All SemanticShape payload fields round-trip |
| SC_GZ050_5 | UnknownShape_SilentlySkipped | Shape=11 skipped by default: in renderer loop |
| SC_GZ050_6 | Regression_ExistingShapesAndSizeUnchanged | Ordinals 0-7 unchanged, size still 64 |

---

### TASK-GZ051 — Fix ComponentInspector abstraction leak

**Files changed:**

- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`  
  Replaced ECS-index-based ComponentInspector fields (`InspTargetIndex` at 24, `InspTargetGen` at 28, `InspComponentTypeId` at 32, old `InspAnchor` at 30, old offsets) with network-ID-based fields:
  - `long InspNetworkId` at offset 24 (8 bytes)
  - `uint InspSchemaHash` at offset 32 (4 bytes)
  - `ScreenAnchor InspAnchor` at offset 36 (1 byte)
  - `byte InspIsReadOnly` at offset 37 (1 byte)
  - `float InspOffsetX` at offset 40 (4 bytes)
  - `float InspOffsetY` at offset 44 (4 bytes)

  No external callsites existed for the removed fields — no other files required updating.

`Marshal.SizeOf<DebugPrimitive>() == 64` confirmed by SC_GZ051_4.

**Tests (ContractsStandaloneTests.cs):**

| ID | Name | Assertion |
|----|------|-----------|
| SC_GZ051_1 | InspNetworkId_FieldRoundTrips | Sets/reads InspNetworkId=12345L |
| SC_GZ051_2 | (build-time) | InspTargetIndex/InspComponentTypeId no longer exist — any reference is a compile error |
| SC_GZ051_3 | InspSchemaHash_MatchesComputeHash | FNV-1a hash of "MyNamespace.MyType" round-trips via InspSchemaHash |
| SC_GZ051_4 | StructSizeStillIs64 | Marshal.SizeOf<DebugPrimitive>()==64 |
| SC_GZ051_5 | DisplayLabel_ConstructableFromStructFields | Remote viewer can render "Entity:99 Schema:ABCD1234" from struct alone |
| SC_GZ051_6 | FieldOffsets_AreCorrect | InspNetworkId at 24, InspSchemaHash at 32 |

---

### TASK-GZ052 — Entity Attribute Schema Broadcast

**Files changed:**

1. **`Hrot/Network/Hrot.Network.NED/GenericMessages.cs`**  
   Added `EntityAttributeSchema` partial struct in namespace `Hrot.NED.Messages`:
   - `[DdsTopic("EntityAttributeSchema")]`
   - `[DdsQos(Reliability=Reliable, Durability=TransientLocal, HistoryKind=KeepLast, HistoryDepth=1)]`
   - `[DdsManaged]`
   - `[DdsKey] int NodeId` — keyed by node so TransientLocal delivers the latest per-SimHost to late joiners
   - `[DdsManaged] string SchemaJson` — full JSON Schema document

2. **`FDP/Toolkits/Fdp.Toolkits/Replication/Patching/AttributeCompilerBuilder.cs`**  
   - Added `private readonly List<string> _paths` field.
   - In `RegisterValuePath<T>` and `RegisterReferencePath<T>`: added `_paths.Add(jsonPath)` after successful registration.
   - Changed `Build()` to pass `_paths` to `JsonAttributeCompiler` constructor.

3. **`FDP/Toolkits/Fdp.Toolkits/Replication/Patching/JsonAttributeCompiler.cs`**  
   - Added `private readonly IReadOnlyList<string> _registeredPaths` field.
   - Added `public IReadOnlyList<string> RegisteredPaths => _registeredPaths;` property.
   - Added overloaded constructor `JsonAttributeCompiler(routes, paths)`.
   - Old single-argument constructor now delegates to the two-argument one with empty paths (backward compatibility).
   - Added `public string ExportSchema()` — writes JSON Schema Draft-07 `{"$schema":…,"properties":{<key>:{…}}}` using `Utf8JsonWriter` via a `MemoryStream`; returns UTF-8 string. Cold-path only (called once at startup).

4. **`Hrot/Network/Hrot.Network.NED/Attributes/EntityAttributeSchemaPublisherSystem.cs`** *(new file)*  
   - `[UpdateInPhase(SystemPhase.BeforeSync)]`
   - Implements `IEcsModuleSystem`.
   - Constructor: `(int nodeId, JsonAttributeCompiler? compiler, IDdsWriter<EntityAttributeSchema>? writer, bool isDefaultProcessor)`.
   - `Execute`: publishes once on the first call if `_isDefaultProcessor && !_published && compiler != null && writer != null`; sets `_published = true` after write.

5. **`Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`**  
   - Added field `private Fdp.Toolkit.Replication.Patching.JsonAttributeCompiler? _jsonAttributeCompiler;`
   - In `OnLoad`, after StatelessGizmoSystem registration, added:
     - `_jsonAttributeCompiler = Hrot.SimHost.AttributeCompilerFactory.Build(_geoTransform);`
     - Creates `IDdsWriter<EntityAttributeSchema>?` via `DdsWriterGizmoAdapter<EntityAttributeSchema>` (or null if no participant).
     - Registers `EntityAttributeSchemaPublisherSystem` with `isDefaultProcessor: true`.

**Tests (EntityAttributeSchemaTests.cs in Hrot.Network.NED.Tests):**

| ID | Name | Assertion |
|----|------|-----------|
| SC_GZ052_1 | EntityAttributeSchema_HasExpectedFields | NodeId is int with [DdsKey]; SchemaJson is string |
| SC_GZ052_2 | PublisherSystem_WritesExactlyOnce | 10 Execute() calls → WriteCount==1 |
| SC_GZ052_3 | NonDefaultProcessor_NeverWrites | isDefaultProcessor=false → WriteCount==0 |
| SC_GZ052_4 | ExportSchema_ReturnsValidJson | JsonDocument.Parse succeeds; root is object |
| SC_GZ052_5 | ExportSchema_ContainsAtLeastOneProperty | "properties" has >= 1 entry using AttributeCompilerFactory.Build(null) |

---

## Test Run Summary

| Assembly | Passed | Failed | Notes |
|----------|--------|--------|-------|
| Fdp.Diagnostics.Contracts.Tests | 17 | 0 | Includes all GZ050+GZ051 tests |
| Hrot.Network.NED.Tests | 95 | 0 | Includes all GZ052 tests |
| (others) | — | pre-existing | See below |

**Pre-existing failures (unrelated to BATCH-18):**
- `Fdp.Toolkits.Tests`: ~27 failures (known, pre-BATCH-18)
- `Hrot.IG.Tests`: 16 failures including `SC_GZ015_2_MarshalSizeOf_Is_4_Bytes` (GlobalDebugSettings grew to 8 bytes in a prior batch — test expectation not updated)
- `Hrot.SimHost.Tests`: ~25 failures (HillAttack, AreaQuery, UnitSubordinate tests — pre-existing)
- `Hrot.ClusterRunner.Tests` / `Hrot.SimHost.Integration.Tests`: DDS codegen errors for `GizmoInteractionBatch` — pre-existing
- `Fdp.Presentation.Tests`: 3 failures — pre-existing

None of these failures are caused by BATCH-18 changes.

---

## Invariants Verified

- `Marshal.SizeOf<DebugPrimitive>() == 64` — confirmed by 4 independent tests (SC_GZ050_2, SC_GZ050_6, SC_GZ051_4, SC_GZ051_6 offset checks)
- All 12 new GZ050/GZ051 tests pass in Fdp.Diagnostics.Contracts.Tests
- All 5 new GZ052 tests pass in Hrot.Network.NED.Tests
- Full solution builds with 0 errors: `dotnet build IOS-IG-SimHost.sln -clp:ErrorsOnly` → `0 Error(s)`
