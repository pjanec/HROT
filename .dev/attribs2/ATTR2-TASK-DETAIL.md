# ATTR2 Task Detail

**Design Reference:** [ATTR2-DESIGN.md](./ATTR2-DESIGN.md)  
**Task Tracker:** [ATTR2-TASK-TRACKER.md](./ATTR2-TASK-TRACKER.md)

---

## Phase 1: Binary Contract & Schema Foundation

### ATTR2-P1T1 — `AttributeValueUnion` and `AttributeRecord` DDS Types

**Design Reference:** [ATTR2-DESIGN.md §3.1](./ATTR2-DESIGN.md#31-binary-wire-contract-attributerecord)

**Scope:**  
Add two new C# structs to `Hrot.NED/GenericMessages.cs`:

1. `AttributeValueUnion` — a tagged-union value container holding one of:   `Int32`, `Int64`, `Float32`, `Float64`, `Bool`, `String`, `Vec3f`, `Vec3d`, `Vec4f`.
2. `AttributeRecord` — the wire atom: `ushort AttributeId`, `short SubIndex1`, `short SubIndex2`, `AttributeValueUnion Value`.

Both types carry `[DdsIdlFile(...)]` attributes consistent with the project's CycloneDDS
attribute conventions (see existing `GenericMessages.cs` for pattern).  `AttributeValueUnion`
is marked `[DdsManaged]` because the `String` branch references a managed type.

**Not in scope:** changes to request/response messages (see ATTR2-P1T3).

**Constraints:**
- No modifications to existing types in `GenericMessages.cs`.
- `AttributeValueUnion` must carry a `ValueType` discriminator tag enum
  (`AttributeValueType`) to allow receivers to identify the active branch without reflection.
- Field names match the IDL pseudo-code in the design talk (`AttributeId`, `SubIndex1`,
  `SubIndex2`, `Value`).
- Fixed-size array fields (`Vec3f`, `Vec3d`, `Vec4f`) are represented as C# arrays of the
  appropriate length, consistent with DDS fixed array patterns already in the project.

**Success Conditions:**

*Test (unit, Hrot.NED.Tests):*  
1. Construct `AttributeRecord { AttributeId = 10, SubIndex1 = 0, Value = { Type = Float64, DoubleValue = 32.085 } }` and verify each field round-trips to JSON via `JsonSerializer` without data loss.  
2. Construct an `AttributeRecord` carrying a `String` value `"Alpha"` and verify the `String` branch is set and all other branches are in default/zero state.  
3. Construct an `AttributeRecord` carrying a `Vec3d` value `[1.0, 2.0, 3.0]` and verify the three doubles are accessible and correct.  
4. Verify `AttributeValueType` enum covers all nine types listed in the design.

---

### ATTR2-P1T2 — `AttributeId` Schema Constants

**Design Reference:** [ATTR2-DESIGN.md §3.4](./ATTR2-DESIGN.md#34-attribute-id-schema)

**Scope:**  
Create `FDP/Toolkits/FDP.Toolkit.Replication/Patching/AttributeIds.cs` with a static class
containing `ushort` constants for the initial well-known attribute IDs:

| Constant | Value | Meaning |
|----------|-------|---------|
| `Name` | 1 | `IgEntityData.Name` |
| `Affiliation` | 2 | `IgEntityData.ForceId` |
| `GeoLat` | 10 | `SimTransform` WGS-84 latitude |
| `GeoLon` | 11 | `SimTransform` WGS-84 longitude |
| `GeoAlt` | 12 | `SimTransform` WGS-84 altitude |

Document the reserved numeric range strategy (e.g. 1–99 core entity data, 100–199 geo/spatial,
200+ domain extensions).

**Not in scope:** registering these IDs in any compiler or interpreter builder (done in Phase 4 /
Phase 6).

**Constraints:**
- File lives in `FDP.Toolkit.Replication` (generic toolkit), not in a domain project.
- Intentionally does not reference any ECS component types or DDS descriptor types.

**Success Conditions:**

*No runtime tests required; static compilation is the verification.*  
1. The file compiles in isolation — only references `System` namespace.  
2. All five constants exist with correct values as documented.  
3. A new constant can be added in a domain project by declaring a partial/extending class or a
   companion file, without modifying the core file (document the pattern with an example in the
   XML doc comment).

---

### ATTR2-P1T3 — Update Wire Messages (`CreateEntityRequest`, `UpdateEntityAttributeRequest`)

**Design Reference:** [ATTR2-DESIGN.md §3.1](./ATTR2-DESIGN.md#31-binary-wire-contract-attributerecord)

**Scope:**  
In `Hrot.NED/GenericMessages.cs`:

1. Add `[DdsManaged] public List<AttributeRecord>? InitialAttributeRecords;` to `CreateEntityRequest`.  
2. Add `[DdsManaged] public List<AttributeRecord>? AttributeRecords;` to `UpdateEntityAttributeRequest`.  

Existing `InitialAttributesJson` and `AttributePatchJson` fields are **retained unchanged**.

**Not in scope:** any runtime logic changes; systems that consume these messages are updated in
Phase 5.

**Constraints:**
- New fields must appear after the existing fields (DDS field ordering is significant for IDL
  generation order; see existing conventions in the file).
- Add XML doc comments referencing ATTR2-DESIGN.md §3.1.

**Success Conditions:**

*Test (unit, Hrot.NED.Tests):*  
1. `CreateEntityRequest` can be constructed with `InitialAttributeRecords = null` — the existing
   JSON-only creation path is unaffected.  
2. `CreateEntityRequest` can be constructed with a non-null `List<AttributeRecord>` containing 2
   records and the list is accessible.  
3. `UpdateEntityAttributeRequest` can be constructed with `AttributeRecords = null` — the value
   must default to null without exception.  
4. All existing `CreateEntityRequest` and `UpdateEntityAttributeRequest` tests in the project
   still pass (zero regressions).

---

## Phase 2: Edge Compiler

### ATTR2-P2T1 — `JsonToRecordCompiler` and `JsonToRecordCompilerBuilder`

**Design Reference:** [ATTR2-DESIGN.md §3.2](./ATTR2-DESIGN.md#32-edge-compiler-jsontorecordcompiler)

**Scope:**  
Create two new files in `FDP/Toolkits/FDP.Toolkit.Replication/Patching/`:

1. `JsonToRecordCompilerBuilder.cs` — fluent builder with a `Register(string path, ushort id, AttributeValueType expectedType)` method.  Hashes paths using `JsonAttributeCompiler.HashPath` (existing FNV-1a implementation — reuse, do not duplicate).  Stores `Dictionary<ulong, EdgeSchemaEntry>` mapping hash → `(AttributeId, ExpectedType)`.  Builds an immutable `JsonToRecordCompiler`.

2. `JsonToRecordCompiler.cs` — the runtime compiler:
   - Public API: `int Compile(ReadOnlySpan<byte> utf8Json, Span<AttributeRecord> output)`.
   - Returns the number of `AttributeRecord`s written to `output`.
   - Uses `Utf8JsonReader` with a `stackalloc PathSegment[16]` depth stack.
   - Handles **flat keys** (`"GeoPoint.Latitude": 32.0`) and **nested objects**
     (`"GeoPoint": { "Latitude": 32.0 }`), including **integer-keyed children** for array
     indexing (`"Weapon": { "2": { "Ammo": 5 } }`).
   - When a numeric string key is encountered as an object-key token, it captures the value as
     `SubIndex1` (first numeric key in current branch) or `SubIndex2` (second).
   - On leaf value: computes final path hash from the stack (excluding numeric segments),
     looks up the routing table, writes `AttributeRecord` to the output span.
   - **No heap allocations on the hot path** — no `string`, no `Dictionary` lookups during
     `Compile` except a single `_routes` read-only dictionary lookup.

**Constraints:**
- Does NOT replace `JsonAttributeCompiler`; both coexist.
- Reuse `JsonAttributeCompiler.HashPath` (make it `internal static` accessible, or extract to a
  static helper in the same file/namespace).
- `PathSegment` is a `readonly struct` holding a `ReadOnlySpan<byte>` view into the
  `Utf8JsonReader` buffer — no copies.
- If the `output` span is too small, stop emitting and return the count so far (log a warning).

**Success Conditions:**

*Tests (unit, FDP.Toolkit.Replication project or new Hrot.SimHost.Tests class):*  

1. **Flat single field:** `Compile("{\"Name\":\"Alpha\"}", buffer)` writes exactly 1 record with
   `AttributeId = AttributeId.Name`, `SubIndex1 = 0`, `Value.StringValue = "Alpha"`.  
2. **Flat dotted path:** `Compile("{\"GeoPoint.Latitude\":32.085}", buffer)` writes 1 record
   with `AttributeId = AttributeId.GeoLat`, `Value.DoubleValue = 32.085`.  
3. **Nested object:** `Compile("{\"GeoPoint\":{\"Latitude\":32.085,\"Longitude\":34.78}}", buffer)`
   writes 2 records with correct IDs and values (in order encountered).  
4. **Array indexing via integer key:**
   `Compile("{\"Weapon\":{\"2\":{\"Ammo\":10}}}", buffer)` writes 1 record with the correct weapon
   ammo ID, `SubIndex1 = 2`, `Value.Int32Value = 10` (assuming weapon ammo is registered).  
5. **Mixed flat + nested in same JSON:** both forms produce correct records and combined count is
   the sum of individual counts.  
6. **Unknown path:** a JSON key not registered in the routing table produces no record (emit
   count unchanged, no exception).  
7. **Empty JSON `{}`:** returns 0.  
8. **Output buffer overflow:** if `output.Length = 1` and two records match, returns 1 and does
   not write out of bounds.  
9. **Zero allocation:** no `GC.GetTotalAllocatedBytes` increase during `Compile` when using a
   stackalloc or pool-rented output buffer (verified via an allocation-counting test helper).

---

### ATTR2-P2T2 — `EdgeCompilerFactory` (Domain Schema Registration)

**Design Reference:** [ATTR2-DESIGN.md §3.2](./ATTR2-DESIGN.md#32-edge-compiler-jsontorecordcompiler), §6

**Scope:**  
Add a static `EdgeCompilerFactory` (or extend `AttributeCompilerFactory`) in `Hrot.SimHost`
that registers the SimHost-specific schema with `JsonToRecordCompilerBuilder`:

- `"Name"` → `AttributeId.Name`, `String`
- `"Affiliation"` → `AttributeId.Affiliation`, `String`
- `"GeoPoint.Latitude"` → `AttributeId.GeoLat`, `Float64`
- `"GeoPoint.Longitude"` → `AttributeId.GeoLon`, `Float64`
- `"GeoPoint.Altitude"` → `AttributeId.GeoAlt`, `Float64`

Returns a `JsonToRecordCompiler` instance ready for injection.

**Not in scope:** injection into `CreationTool` (Phase 6).

**Constraints:**
- Mirrors the structure of `AttributeCompilerFactory.Build()` — same path strings,
  same ordering — so the two compilers (JSON→ECS and JSON→Records) stay in sync.

**Success Conditions:**

1. `EdgeCompilerFactory.Build()` compiles without error.  
2. The built compiler's schema covers all five paths listed above (verified by compiling a
   fixture JSON containing all five paths and asserting 5 records are emitted).  
3. Paths missing from the schema produce no record (forward-compatibility: unknown paths are
   silently ignored, not thrown).

---

## Phase 3: Binary Interpreter Core

### ATTR2-P3T1 — `IBinaryAttributeInstaller`, `BinaryPatchContext`, `BinaryInterpreterBuilder`, `BinaryInterpreter`

**Design Reference:** [ATTR2-DESIGN.md §3.3](./ATTR2-DESIGN.md#33-binary-interpreter-binaryinterpreter)

**Scope:**  
Create four new files in `FDP/Toolkits/FDP.Toolkit.Replication/Patching/`:

**`IBinaryAttributeInstaller.cs`**
```csharp
public interface IBinaryAttributeInstaller
{
    void Install(BinaryInterpreterBuilder builder);
}
```

**`BinaryPatchContext.cs`**  
A context class (not `ref struct`) wrapping:
- `EntityRepository Repo` (live ECS) — nullable for staged/creation use
- `Entity Entity`
- `IEntityPatchContext PatchContext` — reused for actual component access (delegates to
  `ListPatchContext` on creation path, `EcsPatchContext` on live path)
- `byte[] ScratchpadData` — pre-allocated block (size determined at build time)
- `uint DirtySubsystemsMask`
- `ulong DirtyDescriptorMask`

Expose `T GetScratchpad<T>(int byteOffset) where T : struct` via `MemoryMarshal.Cast`.  
Expose `void MarkSubsystemDirty(int bit)` and `void MarkDescriptorDirty(long ordinal)`.

**`BinaryInterpreterBuilder.cs`**  
Fluent builder:
- `RegisterHandler(ushort id, Action<BinaryPatchContext, AttributeRecord> handler)` — stores
  handler in `_handlers[id]`.  Handlers are `static` lambdas (no closures; verified by the
  `[RequiresStaticDelegate]`-style convention).
- `RegisterSubsystemFlusher(int bit, Action<BinaryPatchContext> flusher)` — stores in
  `_flushers[bit]`.
- `ReserveScratchpad(int bytes)` → returns `int byteOffset`; accumulates total scratchpad size.
- `AddInstaller(IBinaryAttributeInstaller installer)` — calls `installer.Install(this)`.
- `Build()` → returns `BinaryInterpreter`.

**`BinaryInterpreter.cs`**  
Runtime interpreter:
- Stores `_handlers` (`Action<BinaryPatchContext, AttributeRecord>[]`, length = max id + 1).
- Stores `_flushers` (`Action<BinaryPatchContext>[]`, length = 32).
- Stores total `_scratchpadSize` (from builder accumulator).
- `BinaryPatchContext CreateContext(IEntityPatchContext patchCtx)` — allocates scratchpad once,
  returns context.
- `void Apply(BinaryPatchContext ctx, ReadOnlySpan<AttributeRecord> records)`:
  1. For each record: look up `_handlers[record.AttributeId]`; invoke if non-null.
  2. After all records: iterate set bits in `ctx.DirtySubsystemsMask`; call corresponding
     flusher.
  3. Call `ctx.PatchContext.FlushDirtyMarks()` (handles SmartEgress).

**Constraints:**
- `BinaryInterpreterBuilder` does not know about `IgEntityData`, `SimTransform`, or any domain
  type.
- The scratchpad block is allocated once per `BinaryInterpreter.CreateContext()` call — amortized
  at context creation, not per-record.
- The `_handlers` array is sized to `maxRegisteredId + 1`, not always 65536. Builder tracks max.
- Unused handler slots remain null; `Apply` skips null slots silently.

**Success Conditions:**

*Tests (unit):*

1. **Basic dispatch:** build interpreter with one handler for id=1 that sets a flag;
   `Apply` with a single record id=1 → flag is set.  
2. **Unknown id ignored:** `Apply` with id=999 (not registered) → no exception, no side effect.  
3. **Flusher called once:** register handler for id=10 that marks bit 0 dirty; register flusher
   for bit 0 that increments a counter; `Apply` with three records of id=10 → flusher counter = 1
   (flusher runs once, not three times).  
4. **Flusher not called when not dirty:** `Apply` with records that do not mark bit 0 dirty →
   flusher counter = 0.  
5. **Multiple installers:** two `IBinaryAttributeInstaller` instances registered via
   `AddInstaller`; both install their handlers; all handlers dispatch correctly.  
6. **Scratchpad reservation:** two installers each reserve 8 bytes; total scratchpad is 16 bytes;
   each installer reads/writes only its own offset slice without overlap.

---

## Phase 4: Domain Installers

### ATTR2-P4T1 — `EntityDataAttributeInstaller`

**Design Reference:** [ATTR2-DESIGN.md §3.3](./ATTR2-DESIGN.md#33-binary-interpreter-binaryinterpreter), §6

**Scope:**  
Create `Hrot.SimHost/Installers/EntityDataAttributeInstaller.cs`.

Implements `IBinaryAttributeInstaller`.  In `Install`:

- Registers handler for `AttributeId.Name`: extracts `Value.StringValue` from the record and
  calls `ctx.PatchContext.GetManagedComponent<IgEntityData>().Name = value`.  Marks
  `EDescriptorType.dtEntityInfo` ordinal dirty.
- Registers handler for `AttributeId.Affiliation`: maps the string (or int) value to `ForceId`
  using the same helpers already in `AttributeCompilerFactory` (`MapAffiliationString`,
  `MapAffiliationInt`).  Marks `dtEntityInfo` ordinal dirty.

No scratchpad needed (no grouped math).  Both handlers call
`ctx.MarkDescriptorDirty((long)EDescriptorType.dtEntityInfo)`.

**Constraints:**
- Re-use the mapping helper logic from `AttributeCompilerFactory` (extract to a shared
  static helper or keep a reference).
- Authority check: delegate to `ctx.PatchContext.CanWriteManaged<IgEntityData>()` before
  touching the component.

**Success Conditions:**

1. `Apply` with `[{AttributeId=Name, Value="Bravo-2"}]` on a context backed by a
   `ListPatchContext` seeded with a default `IgEntityData` → the resulting component has
   `Name = "Bravo-2"` and `ForceId` is unchanged.  
2. `Apply` with `[{AttributeId=Affiliation, Value="HOSTILE"}]` → `ForceId = ForceId.Hostile`
   (or the domain-appropriate enum value).  
3. Authority guard: when `CanWriteManaged<IgEntityData>()` returns `false`, neither `Name` nor
   `ForceId` are modified.  
4. `DirtyDescriptorMask` has `dtEntityInfo` bit set after applying a Name or Affiliation record.  
5. Unknown attribute IDs handled by the base interpreter (not by this installer) — no exception.

---

### ATTR2-P4T2 — `SimTransformAttributeInstaller`

**Design Reference:** [ATTR2-DESIGN.md §3.3](./ATTR2-DESIGN.md#33-binary-interpreter-binaryinterpreter), §4

**Scope:**  
Create `Hrot.SimHost/Installers/SimTransformAttributeInstaller.cs`.

Implements `IBinaryAttributeInstaller`.  In `Install`:

1. **Reserve scratchpad** for `GeoCoordScratchpad`:
   ```csharp
   struct GeoCoordScratchpad { public double Lat, Lon, Alt; public bool Initialized; }
   _scratchpadOffset = builder.ReserveScratchpad(Unsafe.SizeOf<GeoCoordScratchpad>());
   ```
2. **Register handlers** for `GeoLat` (id=10), `GeoLon` (id=11), `GeoAlt` (id=12):
   - Authority check: `ctx.PatchContext.CanWrite<SimTransform>()`.
   - On first call for this context (detect via `!scratch.Initialized`): pre-fill scratchpad
     from current entity's `SimTransform.Position` via reverse geodetic conversion (use
     `IGeographicTransform` injected into the installer).  Set `scratch.Initialized = true`.
   - Write the appropriate coordinate (`Lat`, `Lon`, or `Alt`) to the scratchpad.
   - Mark subsystem bit dirty.
3. **Register subsystem flusher**:
   - Read scratchpad, call `_geoTransform.ToCartesian(lat, lon, alt)`.
   - Write result to `ctx.PatchContext.GetUnmanagedComponent<SimTransform>().Position`.
   - Call `ctx.MarkDescriptorDirty((long)EDescriptorType.dtWorldPos)`.

**Constraints:**
- `IGeographicTransform` is injected via the installer constructor.
- Pre-fill on first write ensures partial updates (e.g. only `GeoLat`) leave `GeoLon`/`GeoAlt`
  unchanged, mirroring the existing `GeoCoordAccumulator` pattern in `AttributeCompilerFactory`.
- `GeoMath.CartesianToGeodetic` or equivalent reverse transform is called at most once per
  `Apply` invocation (the `Initialized` flag prevents repeated pre-fills).
- The flusher calls `ToCartesian` exactly once regardless of how many of the three lat/lon/alt
  records appear in the packet.

**Success Conditions:**

1. **Full update:** `Apply` with `[GeoLat, GeoLon, GeoAlt]` → `SimTransform.Position` updated to
   the correct Cartesian vector; flusher invoked once; `dtWorldPos` bit set in
   `DirtyDescriptorMask`.  
2. **Partial update — Lat only:** seed entity at known position (50°N, 30°E, 0m);
   `Apply` with `[{GeoLat=32.0}]` → position is recalculated with new Lat but original Lon and
   Alt.  
3. **Three records, flusher called once:** pass all three lat/lon/alt records; mock the
   `ToCartesian` call and verify it was invoked exactly once.  
4. **Authority guard:** `CanWrite<SimTransform>()` returns false → position not modified, flusher
   not called, no exception.  
5. **Pre-fill accuracy:** scratchpad `Lon` and `Alt` match the entity's original position when
   only `Lat` is patched (requires reverse geodetic conversion to be consistent with the forward
   conversion).

---

### ATTR2-P4T3 — `BinaryInterpreterFactory` (SimHost Wiring)

**Design Reference:** [ATTR2-DESIGN.md §6](./ATTR2-DESIGN.md#6-files--modules-affected)

**Scope:**  
Extend `Hrot.SimHost/AttributeCompilerFactory.cs` with a static
`BuildBinaryInterpreter(IGeographicTransform? geoTransform)` method that:

1. Creates a `BinaryInterpreterBuilder`.
2. Calls `builder.AddInstaller(new EntityDataAttributeInstaller())`.
3. Conditionally calls `builder.AddInstaller(new SimTransformAttributeInstaller(geoTransform))`
   when `geoTransform != null`.
4. Returns `builder.Build()`.

**Success Conditions:**

1. `BuildBinaryInterpreter(null)` → interpreter handles `Name` and `Affiliation` records; geo
   records produce no side-effects (no registration → silently skipped).  
2. `BuildBinaryInterpreter(geoTransform)` → interpreter handles all five attribute IDs.  
3. The `IgsApplication` DI wiring compiles (this is an integration-level check).

---

## Phase 5: System Integration

### ATTR2-P5T1 — `CreateEntityRequestSystem` Binary Branch

**Design Reference:** [ATTR2-DESIGN.md §3.5](./ATTR2-DESIGN.md#35-createentityrequestsystem-changes)

**Scope:**  
Modify `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`:

1. Add optional constructor parameter `BinaryInterpreter? binaryInterpreter = null`.
2. In `ProcessRequest` (or the equivalent pending-queue drain):
   - If `request.InitialAttributeRecords != null && request.InitialAttributeRecords.Count > 0`
     **and** `_binaryInterpreter != null`: apply via `BinaryInterpreter.Apply` using a
     `ListPatchContext`-backed `BinaryPatchContext`.
   - Else if `request.InitialAttributesJson != null` **and** `_jsonCompiler != null`: apply via
     existing JSON path.
   - (`CollectionsMarshal.AsSpan` can be used to avoid enumerator allocation for the list.)

**Not in scope:** removal of the JSON path.

**Constraints:**
- Must not break any existing `CreateEntityRequestSystem` unit tests.
- The `_reusablePatchContext` instance reuse pattern is preserved (binary path creates a context
  from the existing `ListPatchContext`).

**Success Conditions:**

1. **Binary path:** send a `CreateEntityRequest` with `InitialAttributeRecords = [Name="Gamma"]`
   and null `InitialAttributesJson`; entity spawns with `Name = "Gamma"`.  
2. **JSON fallback:** send with null `InitialAttributeRecords` and `InitialAttributesJson =
   "{\"Name\":\"Delta\"}"` ; entity spawns with `Name = "Delta"`.  
3. **Both null:** entity spawns with TKB-default name; no exception.  
4. **Binary preferred over JSON when both set:** send with non-null binary list AND non-null JSON;
   binary list is applied; JSON is ignored.  
5. All existing `CreateEntityRequestSystem` tests pass.

---

### ATTR2-P5T2 — `UpdateEntityAttributeRequestSystem` Binary Branch

**Design Reference:** [ATTR2-DESIGN.md §3.6](./ATTR2-DESIGN.md#36-updateentityattributerequestsystem-changes)

**Scope:**  
Modify `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`:

1. Add optional constructor parameter `BinaryInterpreter? binaryInterpreter = null`.
2. In `ProcessRequest`:
   - If `request.AttributeRecords != null && request.AttributeRecords.Count > 0` and interpreter
     available: apply via binary path using the existing `EcsPatchContext` wrapped in a
     `BinaryPatchContext`.
   - Else: existing JSON path.
3. ACK bitmask logic: the binary path's `BinaryPatchContext.DirtyDescriptorMask` propagates
   to the existing `_appliedComponentIds` bitmask used for `OpaqueData` in the ACK, so callers
   can still use the ACK receipt to verify which components were updated.

**Constraints:**
- `EcsPatchContext` is still created for the JSON fallback; reuse for binary path's
  `IEntityPatchContext`.
- Silent-bystander rule (no ACK if no mutations applied) is preserved for the binary path.
- Existing tests must pass.

**Success Conditions:**

1. **Binary path live update:** live entity with name "Alpha"; send
   `UpdateEntityAttributeRequest { AttributeRecords = [{Name="Bravo"}] }` → entity's
   `IgEntityData.Name` becomes "Bravo".  
2. **Partial geo update:** live entity at known position; send `[{GeoLat=32.0}]` → position
   updated with correct Lat, original Lon/Alt preserved.  
3. **ACK bitmask:** with `RequireAck = true`, the ACK `OpaqueData` has the appropriate
   ECS component type bit set.  
4. **Authority guard via scratchpad:** entity without `SimTransform` authority; send geo record
   → position unchanged, ACK component bit for SimTransform NOT set.  
5. All existing `UpdateEntityAttributeRequestSystem` tests pass.

---

## Phase 6: Client-Side Integration

### ATTR2-P6T1 — `CreationTool` EdgeCompiler Injection

**Design Reference:** [ATTR2-DESIGN.md §3.7](./ATTR2-DESIGN.md#37-creationtool-ig-side-changes)

**Scope:**  
Modify `Hrot.IG/Tools/CreationTool.cs`:

1. Add optional constructor parameter `JsonToRecordCompiler? edgeCompiler = null`.
2. Before building `CreateEntityRequest`:
   - If `_edgeCompiler != null` and `_initialPropertiesJson != null`:
     - Rent a buffer: `ArrayPool<AttributeRecord>.Shared.Rent(64)`.
     - Call `_edgeCompiler.Compile(Encoding.UTF8.GetBytes(_initialPropertiesJson), buffer)`.
     - Set `request.InitialAttributeRecords = buffer[..count].ToList()` (**note:** `ToList()`
       does allocate; this is the creation/placement path, not high-frequency; acceptable
       trade-off per design decision A2).
     - Return rented buffer.
   - Leave `InitialAttributesJson = null` (clean wire).
   - If `_edgeCompiler == null` (backward compat mode): keep existing JSON path.

**Constraints:**
- `CreationTool` tests must continue to pass — the constructor change is additive (optional param).
- The edge compiler is injected; `CreationTool` does not instantiate it.
- The IG `IgApplication` wiring must inject the `JsonToRecordCompiler` from
  `EdgeCompilerFactory.Build()`.

**Success Conditions:**

1. `CreationTool` constructed **without** edge compiler: publishes `CreateEntityRequest` with
   `InitialAttributesJson` set and `InitialAttributeRecords` null (legacy path unchanged).  
2. `CreationTool` constructed **with** edge compiler: publishes `CreateEntityRequest` with
   `InitialAttributeRecords` non-null and `InitialAttributesJson` null.  
3. Record count in `InitialAttributeRecords` matches expected count for the JSON fixture used in
   tests (e.g. a JSON with 3 registered paths → 3 records).  
4. Round-trip test: `CreationTool` sends binary records → `CreateEntityRequestSystem` (with
   binary interpreter) → entity spawned with correct attribute values.  
5. All existing `CreationToolTests` pass.
