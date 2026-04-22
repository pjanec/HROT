# Task Detail: Attributes-to-ECS — Zero-Allocation JSON Entity Patching

**Design Reference:** See [ATTR-DESIGN.md](./ATTR-DESIGN.md) for architectural context, principles,
and phase goals.  
**Tracker:** See [ATTR-TASK-TRACKER.md](./ATTR-TASK-TRACKER.md) for progress status.

---

## Phase 1: DDS API Migration

### ATTR-S1T1 — Replace `InitialAttributes` with `InitialAttributesJson` in `CreateEntityRequest`

**File:** `Hrot.NED/GenericMessages.cs`

**Context:** See [ATTR-DESIGN.md §3.1](./ATTR-DESIGN.md#31-new-wire-field-initialattributesjson) and
[Phase 1](./ATTR-DESIGN.md#phase-1-dds-api-migration).

**Change:**  
In the `CreateEntityRequest` struct, remove the field:
```csharp
[DdsManaged]
public List<EntityAttributePayload>? InitialAttributes;
```
and replace it with:
```csharp
// Fine-grained attribute overrides applied AFTER TKB defaults and InitialDescriptors.
// JSON string matching the EntityPropertyPatch schema  e.g. {"Name":"Bravo-1","Affiliation":"FORCE_FRIENDLY"}.
// Processed by JsonAttributeCompiler on the authoritative node without heap allocations.
public string? InitialAttributesJson;
```

`EntityAttributePayload` and `EntityAttribute` are removed in ATTR-S1T2 below once
`UpdateEntityAttributeRequest` is also migrated. Do not remove them in this task alone.

**Success Conditions:**

1. **Compilation gate:** `Hrot.NED` builds without warnings. All downstream projects
   (`Hrot.SimHost`, `Hrot.Map.Common`, `Hrot.IG`) compile after adapting call sites.

2. **Unit test — field shape** (`Hrot.NED.Tests/DdsIntegrationTests.cs` or new test):  
   `CreateEntityRequest_HasInitialAttributesJsonField`:  
   Assert `typeof(CreateEntityRequest).GetField("InitialAttributesJson")` is not null and is of
   type `string`.

3. **Unit test — no InitialAttributes field:**  
   `CreateEntityRequest_HasNoInitialAttributesField`:  
   Assert `typeof(CreateEntityRequest).GetField("InitialAttributes")` is null.

4. **Existing tests:** All tests in `Hrot.SimHost.Tests/CreateEntityRequestSystemTests.cs` and
   `Hrot.Map.Common.Tests/EntityAttributeCompilerTests.cs` continue to compile and pass after
   adapting any constructor calls that previously set `InitialAttributes`.

---

### ATTR-S1T2 — Replace `AttributeId`+`Payload` in `UpdateEntityAttributeRequest` with `AttributePatchJson`

**File:** `Hrot.NED/GenericMessages.cs`

**Context:** See [ATTR-DESIGN.md §3.8](./ATTR-DESIGN.md#38-updateentityattributerequest--wire-format-change) and
[Phase 1](./ATTR-DESIGN.md#phase-1-dds-api-migration).

**Change:**  
In the `UpdateEntityAttributeRequest` struct, remove:
```csharp
public EntityAttribute AttributeId;
public EntityAttributePayload Payload;
```
and replace with:
```csharp
// Hierarchical JSON attribute patch.  Processed by JsonAttributeCompiler using the
// same routing table as CreateEntityRequest, enabling deep paths like
// { "Weapons": { "0": { "Ammo": { "Count": 10 } } } } with zero heap allocations.
public string AttributePatchJson;
```

Then remove the now-unused `EntityAttribute` enum and `EntityAttributePayload` DDS union
from `GenericMessages.cs` entirely (no other message references them after this change).

**Success Conditions:**

1. **Compilation gate:** Solution builds cleanly after the two message field changes. Adapt
   every call site in `UpdateEntityAttributeRequestSystem`, tests, and any IOS/IG code that
   previously constructed `UpdateEntityAttributeRequest` with `AttributeId`/`Payload`.

2. **Unit test — field shape:**  
   `UpdateEntityAttributeRequest_HasAttributePatchJsonField`:  
   Assert `typeof(UpdateEntityAttributeRequest).GetField("AttributePatchJson")` is `string`.

3. **Unit test — no legacy fields:**  
   `UpdateEntityAttributeRequest_HasNoAttributeIdField`:  
   Assert `typeof(UpdateEntityAttributeRequest).GetField("AttributeId")` is null.  
   `UpdateEntityAttributeRequest_HasNoPayloadField`:  
   Assert `typeof(UpdateEntityAttributeRequest).GetField("Payload")` is null.

4. **Unit test — enum/union removed:**  
   `GenericMessages_EntityAttribute_EnumDoesNotExist`:  
   Assert `Type.GetType("Hrot.NED.Messages.EntityAttribute, Hrot.NED")` is null.

5. **Existing tests:** Tests in `Hrot.Map.Common.Tests` related to attribute request handling
   compile and pass after adapting constructors.

---

## Phase 2: IG Pipe Simplification

### ATTR-S2T1 — `CreationTool`: forward JSON verbatim, remove `dtEntityInfo` descriptor

**File:** `Hrot.IG/Tools/CreationTool.cs`

**Context:** See [ATTR-DESIGN.md §3.2](./ATTR-DESIGN.md#32-ig-creationtool--dumb-pipe) and
[Phase 2](./ATTR-DESIGN.md#phase-2-ig-pipe-simplification).

**Changes:**

1. In `BuildAndPublishCreateRequest`, remove the third `EntityDescriptorUnion` entry from
   `InitialDescriptors` — the one with `_d = EDescriptorType.dtEntityInfo`. The list must contain
   only `dtEntityMaster` and `dtWorldPos`.

2. Remove the local variable `string entityName = _nameResolver?.Invoke() ?? ParseNameFromJson(...)`.
   The name is no longer set on the IG side for the spawning path.

3. Remove the local variable `ForceId aff = _affiliationForDisplay;` from the spawning path
   (affiliation is no longer embedded in a descriptor).

4. Assign `InitialAttributesJson = _initialPropertiesJson` on the `CreateEntityRequest`.

5. Remove the private helper `ParseNameFromJson(string? json)` from the class. It is no longer
   needed on the spawning path.

6. **Retain** `ParseAffiliationFromJson` — it is still called in the constructor to set
   `_affiliationForDisplay` for ghost rendering. Do not remove it.

7. **Retain** `_nameResolver` field and constructor parameter — it is still valid for IOS-level
   session naming. However, since the name is now forwarded as JSON via `InitialAttributesJson`,
   `_nameResolver` output must be serialised and embedded into the JSON before being passed to the
   constructor (responsibility shifts to the call site in `MapCommandController`/`IgApplication`).
   For Phase 2, simply remove the `_nameResolver?.Invoke()` from the spawning path; the
   auto-name feature is re-wired in a follow-up task.

**Success Conditions:**

1. **Unit test** `CreationTool_EmitsOnly_EntityMaster_And_WorldPos_Descriptors`:  
   Construct a `CreationTool` with `initialPropertiesJson = "{\"Name\":\"Bravo-1\"}"`.  
   Simulate a left-click. Capture the `CreateEntityRequest`.  
   Assert `request.InitialDescriptors.Count == 2`.  
   Assert `request.InitialDescriptors.Any(d => d._d == EDescriptorType.dtEntityInfo)` is **false**.

2. **Unit test** `CreationTool_SetsInitialAttributesJson_FromInitialPropertiesJson`:  
   Same setup.  
   Assert `request.InitialAttributesJson == "{\"Name\":\"Bravo-1\"}"` (string equality).

3. **Unit test** `CreationTool_InitialAttributesJson_IsNull_WhenNoPropertiesJson`:  
   Construct without `initialPropertiesJson`.  
   Assert `request.InitialAttributesJson == null` (or empty string depending on implementation).

4. **Unit test** `CreationTool_GhostColor_StillReflectsAffiliation`:  
   Construct with `initialPropertiesJson = "{\"Affiliation\":\"FORCE_FRIENDLY\"}"`.  
   Assert `_affiliationForDisplay == ForceId.Friend` via reflection or a test-accessible property.

5. **Existing passing tests** in `Hrot.IG.Tests/CreationToolTests.cs` must be updated to match
   the new `InitialDescriptors.Count == 2` expectation. Any assertion that currently checks for a
   `dtEntityInfo` entry must be removed or replaced.

---

## Phase 3: Zero-Allocation Compiler Core

### ATTR-S3T1 — Create `JsonAttributeCompiler` with `Utf8JsonReader` streaming

**File:** `Hrot.Map.Common/Replication/Utils/JsonAttributeCompiler.cs` *(new file)*

**Context:** See [ATTR-DESIGN.md §3.3](./ATTR-DESIGN.md#33-zero-allocation-json-attribute-compiler) and
[Phase 3](./ATTR-DESIGN.md#phase-3-zero-allocation-compiler-core).

**Change:**  
Create a new `JsonAttributeCompiler` class. Its public API must be:

```csharp
/// <summary>
/// Streams a JSON attribute patch string into an <see cref="IEntityPatchContext"/> using
/// zero heap allocations on the hot path.
/// </summary>
public sealed class JsonAttributeCompiler
{
    private readonly IReadOnlyDictionary<ulong, RoutingEntry> _routes;

    internal JsonAttributeCompiler(IReadOnlyDictionary<ulong, RoutingEntry> routes)
    {
        _routes = routes;
    }

    /// <summary>
    /// Applies all JSON attribute overrides in <paramref name="json"/> to
    /// <paramref name="context"/>. No heap allocations occur if <paramref name="json"/>
    /// is null or empty.
    /// </summary>
    public void Compile(string? json, IEntityPatchContext context);
}
```

`Compile` internals (all on the stack):
- Convert `json` string to UTF-8 span via `Encoding.UTF8.GetBytes` or a pre-allocated buffer.
- Construct `Utf8JsonReader reader = new(utf8Bytes)`.
- `stackalloc ulong[MaxDepth]` for `hashStack`.
- `stackalloc int[MaxDepth * MaxArrayDimensions]` for `indexStack` (or a flat pre-allocated array).
- Loop `reader.Read()`: switch on token type to maintain depth / hash / index state (see §3.3.2 of
  ATTR-DESIGN.md).
- On a primitive value token: look up `currentHash` in `_routes`; if found, invoke the registered
  delegate via the `RoutingEntry`, passing `context`, `indexStack[0..depth]`, and `ref reader`.

**Internal type `RoutingEntry`:**
```csharp
internal readonly struct RoutingEntry
{
    public readonly Type ComponentType;
    public readonly bool IsValueType;
    // One of these is set depending on IsValueType:
    public readonly Delegate? ValueSetter;     // ValueAttributeSetter<T>
    public readonly Delegate? ReferenceSetter; // ReferenceAttributeSetter<T>
}
```

**Constants:**
```csharp
private const int MaxDepth              = 16;
private const int MaxArrayDimensions    = 4;
private const ulong FnvOffset           = 14695981039346656037UL;
private const ulong FnvPrime            = 1099511628211UL;
private static ReadOnlySpan<byte> WildcardBytes => "*"u8;
```

**Success Conditions:**

1. **Unit test** `JsonAttributeCompiler_NullJson_DoesNotThrow`:  
   Build a compiler with zero routes. Call `Compile(null, context)`. Assert no exception and
   context is unchanged.

2. **Unit test** `JsonAttributeCompiler_EmptyJson_DoesNotThrow`:  
   Same with `json = ""`.

3. **Unit test** `JsonAttributeCompiler_FlatStringProperty_InvokesDelegate`:  
   Register a `ReferenceAttributeSetter<IgEntityData>` for path `"Name"`.  
   Call `Compile("{\"Name\":\"Alpha-1\"}", context)`.  
   Assert the delegate was invoked and `IgEntityData.Name == "Alpha-1"` in the context.

4. **Unit test** `JsonAttributeCompiler_NestedProperty_InvokesCorrectDelegate`:  
   Register paths for `"GeoPoint"` nested object (or a flat path for the Latitude leaf).  
   Call `Compile("{\"GeoPoint\":{\"Latitude\":32.5,\"Longitude\":34.8,\"Altitude\":0}}", context)`.  
   Assert the registered delegate was invoked with correct reader state.

5. **Unit test** `JsonAttributeCompiler_UnknownProperty_IsIgnored`:  
   Build a compiler with only a `"Name"` route. Call `Compile("{\"Unknown\":42}", context)`.  
   Assert no exception and `IgEntityData.Name` is unchanged (delegate never invoked).

6. **No GC pressure test** (optional but recommended):  
   Call `Compile` 1 000 times in a tight loop.  
   Assert `GC.CollectionCount(0)` did not increase (no Gen-0 allocations from the compile loop).

---

### ATTR-S3T2 — FNV-1a Incremental Path Hashing

**File:** `Hrot.Map.Common/Replication/Utils/JsonAttributeCompiler.cs` *(within same class)*

**Context:** See [ATTR-DESIGN.md §3.3.3](./ATTR-DESIGN.md#333-incremental-fnv-1a-path-hashing).

This task covers the internal hashing logic inside `JsonAttributeCompiler`:

- Helper `HashBytes(ulong current, ReadOnlySpan<byte> bytes)` → `ulong`:  
  FNV-1a byte-by-byte: `hash = (hash ^ b) * FnvPrime`.
- Helper `IsAllDigits(ReadOnlySpan<byte> bytes)` → `bool`: used to detect numeric array-index
  property names.
- On `PropertyName` token with digits-only name: push integer to `indexStack` and hash
  `WildcardBytes` instead of the literal digits.
- On `PropertyName` token with string name: hash the raw UTF-8 bytes.
- On `StartObject`: push `currentHash` onto `hashStack`; increment `depth`.
- On `EndObject`: pop `currentHash` from `hashStack`; decrement `depth`.
- Hash separator byte (`'.'` = 0x2E) between path segments to avoid hash collisions between
  a property `"AB"` vs property `"A"` + property `"B"`.

**Success Conditions:**

1. **Unit test** `FnvHash_SamePathSameHash`:  
   Call `HashPath("Name")` twice. Assert both calls return the same `ulong`.

2. **Unit test** `FnvHash_DifferentPathDifferentHash`:  
   Assert `HashPath("Name") != HashPath("Affiliation")`.

3. **Unit test** `FnvHash_ArrayIndexNormalisedToWildcard`:  
   Simulate streaming `"Weapons"."0"."Ammo"."Count"` and `"Weapons"."5"."Ammo"."Count"` through
   the state machine. Both must produce the same final hash.

4. **Unit test** `FnvHash_DepthRestoreOnEndObject`:  
   Simulate `{ "A": { "B": "x" }, "C": 1 }`. Assert that the hash seen for the `"C"` token
   equals `HashPath("C")`, confirming the state was correctly restored after the `EndObject`.

---

## Phase 4: Pre-Compiled Delegate Registry

### ATTR-S4T1 — Define Delegate Types and `IEntityPatchContext`

**File:** `Hrot.Map.Common/Replication/Utils/IEntityPatchContext.cs` *(new file)*

**Context:** See [ATTR-DESIGN.md §3.4](./ATTR-DESIGN.md#34-dual-mode-pre-compiled-delegates) and
[§3.6](./ATTR-DESIGN.md#36-ientitypatchcontext-and-ecspatchcontext).

**Change:** Create the following in the `Hrot.Map.Common.Replication.Utils` namespace:

```csharp
/// <summary>Delegate for mutating an unmanaged struct ECS component via ref.</summary>
public delegate void ValueAttributeSetter<T>(
    ref T component,
    ReadOnlySpan<int> indices,
    ref Utf8JsonReader reader) where T : struct;

/// <summary>Delegate for mutating a managed class ECS component via reference.</summary>
public delegate void ReferenceAttributeSetter<T>(
    T component,
    ReadOnlySpan<int> indices,
    ref Utf8JsonReader reader) where T : class;

/// <summary>
/// Provides the JSON attribute compiler with access to baseline ECS component instances.
/// Two implementations exist: <see cref="ListPatchContext"/> for entity creation,
/// <see cref="EcsPatchContext"/> for live updates.
/// </summary>
public interface IEntityPatchContext
{
    ref T GetUnmanagedComponent<T>() where T : struct;
    T GetManagedComponent<T>() where T : class;
    /// <summary>
    /// Called after all JSON compilation is complete to flush dirty-marks for every
    /// component type touched during this session.  ListPatchContext is a no-op;
    /// EcsPatchContext calls SmartEgressUtil.MarkDirty for each distinct ordinal.
    /// </summary>
    void FlushDirtyMarks();
}
```

**Note — `MarkUnmanagedDirty<T>` / `MarkManagedDirty<T>` removed:** the design talk clarified
that chunk-level ticks are too coarse for per-entity egress precision (see
[ATTR-DESIGN.md §3.10](./ATTR-DESIGN.md#310-chunk-tick-egress-correction)). Dirty-marking is
now aggregated and flushed in one batch via `FlushDirtyMarks()` using the `descriptorOrdinal`
stored in each `RoutingEntry` (registered in ATTR-S4T2).

**Success Conditions:**

1. **Compilation gate:** `Hrot.Map.Common` compiles with the new types. `JsonAttributeCompiler`
   (Phase 3) references `IEntityPatchContext` without circular dependency.

2. **Unit test** `IEntityPatchContext_ValueAttributeSetter_AcceptsRef`:  
   Declare a `ValueAttributeSetter<SimTransform>` lambda that mutates `component.Position`.
   Assert compiler accepts `ref SimTransform` parameter (compile-time check).

---

### ATTR-S4T2 — Create `AttributeCompilerBuilder`

**File:** `Hrot.Map.Common/Replication/Utils/AttributeCompilerBuilder.cs` *(new file)*

**Context:** See [ATTR-DESIGN.md §3.5](./ATTR-DESIGN.md#35-attributecompilerbuilder-api).

**Change:** Create `AttributeCompilerBuilder`:

```csharp
public sealed class AttributeCompilerBuilder
{
    private readonly Dictionary<ulong, RoutingEntry> _routes = new();

    /// <summary>
    /// Registers a JSON path for a struct-based ECS component.
    /// The <paramref name="jsonPath"/> is hashed at registration time.
    /// <paramref name="descriptorOrdinal"/> is stored in the routing entry and used by
    /// <see cref="EcsPatchContext.FlushDirtyMarks"/> to call SmartEgressUtil.MarkDirty
    /// after all mutations are applied (bypassing coarse chunk-level ticks).
    /// </summary>
    public AttributeCompilerBuilder RegisterValuePath<T>(
        string jsonPath,
        ValueAttributeSetter<T> setter,
        long descriptorOrdinal = 0) where T : struct;

    /// <summary>
    /// Registers a JSON path for a class-based ECS component.
    /// </summary>
    public AttributeCompilerBuilder RegisterReferencePath<T>(
        string jsonPath,
        ReferenceAttributeSetter<T> setter,
        long descriptorOrdinal = 0) where T : class;

    /// <summary>Builds the immutable <see cref="JsonAttributeCompiler"/>.</summary>
    public JsonAttributeCompiler Build();
}
```

`RegisterValuePath<T>` / `RegisterReferencePath<T>` must:
- Call the same FNV-1a path hashing function used by `JsonAttributeCompiler.Compile` to compute
  the hash for `jsonPath`.
- Store `descriptorOrdinal` in the `RoutingEntry` so `EcsPatchContext.FlushDirtyMarks()` can
  call `SmartEgressUtil.MarkDirty(repo, entity, ordinal)` for every touched component.
- Throw `InvalidOperationException` if `jsonPath` is null or empty.
- Throw `InvalidOperationException` if the same path hash is registered twice (collision guard).

**Success Conditions:**

1. **Unit test** `AttributeCompilerBuilder_RegisterValuePath_CanBuildAndCompile`:  
   Register a `ValueAttributeSetter<SimTransform>` for `"GeoPoint"`. Call `Build()`.  
   Assert result is not null and `typeof(JsonAttributeCompiler)`.

2. **Unit test** `AttributeCompilerBuilder_DuplicatePath_Throws`:  
   Register the same path twice. Assert `InvalidOperationException` is thrown on the second call.

3. **Unit test** `AttributeCompilerBuilder_RegisterReferencePath_CanBuildAndCompile`:  
   Register a `ReferenceAttributeSetter<IgEntityData>` for `"Name"`. Call `Build()`.
   Assert success.

4. **Unit test** `AttributeCompilerBuilder_EmptyBuilder_BuildsValidCompilerThatIgnoresAllJson`:  
   Build with no registrations. Call `Compile("{\"Name\":\"X\"}", context)`.
   Assert no exception and context unchanged.

---

### ATTR-S4T3 — Create `ListPatchContext` and `EcsPatchContext`

**Files:**  
- `Hrot.Map.Common/Replication/Utils/ListPatchContext.cs` *(new file)*  
- `Hrot.Map.Common/Replication/Utils/EcsPatchContext.cs` *(new file)*

**Context:** See [ATTR-DESIGN.md §3.6](./ATTR-DESIGN.md#36-ientitypatchcontext-and-ecspatchcontext).

**`ListPatchContext` specification:**

- Constructor: `ListPatchContext(List<object>? baseComponents)`.
- `GetManagedComponent<T>()` — search `baseComponents` for an instance of `T`; if found return it;
  otherwise create `Activator.CreateInstance<T>()` (once, lazy, cached internally by type).
- `GetUnmanagedComponent<T>()` — search `baseComponents` for a boxed `T`; unbox to a local
  field; return `ref` to that field. Track it in a strongly-typed lookup to avoid boxing on
  repeated accesses.
- `FlushComponents()` → `List<object>` — returns the `baseComponents` list with all touched
  components replaced/inserted (respecting per-component-compilation: each type appears exactly once).
- `MarkUnmanagedDirty<T>()` and `MarkManagedDirty<T>()` are no-ops (creation context has no
  egress to trigger).

**`EcsPatchContext` specification:**

```csharp
public sealed class EcsPatchContext : IEntityPatchContext
{
    /// <param name="repo">Live ECS world.</param>
    /// <param name="entity">Entity being patched.</param>
    /// <param name="routes">The same routing table the compiler uses — needed to look up
    ///   the descriptor ordinal when FlushDirtyMarks() is called.</param>
    public EcsPatchContext(
        EntityRepository repo,
        Entity entity,
        IReadOnlyDictionary<ulong, RoutingEntry> routes);

    public ref T GetUnmanagedComponent<T>() where T : struct
        // => repo.GetComponentRW<T>(entity)
        // NOTE: chunk tick is bumped but NOT relied on for egress.
        // Egress is driven exclusively by FlushDirtyMarks().

    public T GetManagedComponent<T>() where T : class
        // => ((ISimulationView)repo).GetManagedComponentRO<T>(entity)

    /// <summary>
    /// After compilation is complete, calls SmartEgressUtil.MarkDirty for every distinct
    /// descriptor ordinal touched during this session.  Deduplicates ordinals so a JSON
    /// string patching both "Name" and "Affiliation" (both ordinal dtEntityInfo) emits
    /// only a single MarkDirty call.
    /// </summary>
    public void FlushDirtyMarks()
        // iterate _touchedOrdinals (HashSet<long> populated per delegate invocation)
        // => SmartEgressUtil.MarkDirty(repo, entity, ordinal) for each
}
```

**Success Conditions:**

1. **Unit test** `ListPatchContext_GetManagedComponent_ReturnsExistingInstance`:  
   Create a `ListPatchContext` seeded with `new IgEntityData { Name = "existing" }`.  
   Call `GetManagedComponent<IgEntityData>()`. Assert `Name == "existing"`.

2. **Unit test** `ListPatchContext_GetManagedComponent_CreatesDefaultWhenMissing`:  
   Create a `ListPatchContext` with empty base.  
   Call `GetManagedComponent<IgEntityData>()`. Assert result is not null and is `IgEntityData`.

3. **Unit test** `ListPatchContext_FlushComponents_ContainsExactlyOnePerType`:  
   Get `IgEntityData` twice from the same context (simulating two attribute setters targeting it).
   Call `FlushComponents()`. Assert the list contains exactly one `IgEntityData` entry.

4. **Unit test** `ListPatchContext_OverwriteFlaw_DualPatch_BothChangesPreserved`:  
   Seed with `IgEntityData { Name = "old", ForceId = ForceId.Friend }`.  
   Create a `JsonAttributeCompiler` that patches `"Name"` and `"Affiliation"` separately.  
   Compile `{ "Name": "new", "Affiliation": "FORCE_HOSTILE" }`.  
   Assert `IgEntityData.Name == "new"` AND `IgEntityData.ForceId == ForceId.Hostile` — both
   changes preserved in the single instance.

5. **Unit test** `EcsPatchContext_GetUnmanagedComponent_ReturnsRefToEcs`:  
   Set up a live `EntityRepository` with a `SimTransform` component.  
   Create `EcsPatchContext(repo, entity, routes)`.  
   Get `ref SimTransform` and mutate `Position.X = 42f`.  
   Read back from repo. Assert `repo.GetComponentRO<SimTransform>(entity).Position.X == 42f`.

6. **Unit test** `EcsPatchContext_FlushDirtyMarks_CallsSmartEgressForTouchedComponents`:  
   Set up an entity with `IgEntityData`. Build an `EcsPatchContext` with a mock egress sink.  
   Call `GetManagedComponent<IgEntityData>()` (simulating a delegate invocation that marks the
   `dtEntityInfo` ordinal as touched).  
   Call `FlushDirtyMarks()`.  
   Assert `SmartEgressUtil.MarkDirty` (or the mock sink) was called exactly once with the
   `dtEntityInfo` ordinal.

7. **Unit test** `EcsPatchContext_FlushDirtyMarks_DeduplicatesOrdinals`:  
   Simulate two delegate invocations targeting different paths but the same ordinal
   (e.g. `"Name"` and `"Affiliation"` both map to `dtEntityInfo`).  
   Call `FlushDirtyMarks()`. Assert the egress sink was called exactly **once** (not twice).

---

## Phase 5: Registration and Integration

### ATTR-S5T1 — Register Component Paths in SimHost Startup

**File:** `Hrot.SimHost/SimHostApp.cs` *(or a dedicated `AttributeCompilerFactory.cs`)*

**Context:** See [ATTR-DESIGN.md Phase 5](./ATTR-DESIGN.md#phase-5-registration-and-integration) and the
[property path table](./ATTR-DESIGN.md#phase-5-registration-and-integration).

**Change:**  
Create a `JsonAttributeCompiler` instance at startup by constructing an `AttributeCompilerBuilder`
and registering the following paths:

```csharp
var compiler = new AttributeCompilerBuilder()

    // IgEntityData — class (reference setter)
    .RegisterReferencePath<IgEntityData>("Name",
        (IgEntityData c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c.Name = r.GetString() ?? string.Empty)

    .RegisterReferencePath<IgEntityData>("Affiliation",
        (IgEntityData c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c.ForceId = MapAffiliationString(r.GetString()))

    // SimTransform — struct (value setter via ref)
    // GeoPoint is a nested object; register individual leaf paths:
    //   "GeoPoint.Latitude", "GeoPoint.Longitude", "GeoPoint.Altitude"
    // These accumulate into a pending Lat/Lon/Alt triple; the actual ToCartesian
    // call fires when all three are present or when the GeoPoint object closes.
    // See implementation note: use a helper struct to accumulate the triple.

    .Build();
```

Inject the `compiler` into `CreateEntityRequestSystem` and `UpdateEntityAttributeRequestSystem`
constructors.

**Note on GeoPoint:** Since `ToCartesian` requires all three coordinates simultaneously,
the `GeoPoint` registration may use a dedicated nested-object setter that handles the
`StartObject`/`EndObject` events around the `GeoPoint` key. An alternative is to register
`"GeoPoint"` as a whole-object path whose setter receives the `Utf8JsonReader` positioned at
`StartObject` and reads the three sub-fields itself. Chose the approach that aligns with the
existing `DescriptorMapper` coordinate conversion logic.

**Success Conditions:**

1. **Integration test** (existing) `CreateEntityRequestSystem` tests pass with the new compiler injected.

2. **Unit test** `SimHostAttributeCompiler_Name_Registered`:  
   Call `Compile("{\"Name\":\"Test\"}", context)` with a `ListPatchContext` containing an
   `IgEntityData` baseline. Assert `IgEntityData.Name == "Test"`.

3. **Unit test** `SimHostAttributeCompiler_Affiliation_Registered`:  
   Call `Compile("{\"Affiliation\":\"FORCE_FRIENDLY\"}", context)`.
   Assert `IgEntityData.ForceId == ForceId.Friend`.

4. **Unit test** `SimHostAttributeCompiler_Affiliation_PreservesExistingName`:  
   Seed context with `IgEntityData { Name = "Alpha", ForceId = ForceId.Unknown }`.  
   Call `Compile("{\"Affiliation\":\"FORCE_HOSTILE\"}", context)`.  
   Assert `IgEntityData.Name == "Alpha"` (unchanged) AND `ForceId == ForceId.Hostile`.

---

### ATTR-S5T2 — Update `CreateEntityRequestSystem` to Use `JsonAttributeCompiler`

**File:** `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`

**Context:** See [ATTR-DESIGN.md §3.7](./ATTR-DESIGN.md#37-creation-path-integration-simhost).

**Change:**

1. Add a `_jsonCompiler` field of type `JsonAttributeCompiler` (nullable, injected via constructor):  
   ```csharp
   private readonly JsonAttributeCompiler? _jsonCompiler;
   ```

2. Add an optional `JsonAttributeCompiler? jsonAttributeCompiler = null` parameter to the
   constructor, assigned to `_jsonCompiler`.

3. In `ProcessPendingRequest`, replace:
   ```csharp
   if (pending.Request.InitialAttributes?.Count > 0)
       allComponents = EntityAttributeCompiler.CompileOverrides(
           pending.Request.InitialAttributes, allComponents, _geoTransform);
   ```
   with:
   ```csharp
   if (_jsonCompiler != null && !string.IsNullOrEmpty(pending.Request.InitialAttributesJson))
   {
       var context = new ListPatchContext(allComponents);
       _jsonCompiler.Compile(pending.Request.InitialAttributesJson, context);
       allComponents = context.FlushComponents();
   }
   ```

**Success Conditions:**

1. **Unit test** `CreateEntityRequestSystem_InitialAttributesJson_PatchesName`:  
   Build a system with a `JsonAttributeCompiler` that handles `"Name"`.  
   Post a `CreateEntityRequest` with `InitialAttributesJson = "{\"Name\":\"Delta-7\"}"`.  
   Execute one tick. Capture the emitted `SpawnEntityCommand`.  
   Assert `SpawnEntityCommand.InitialComponents` contains an `IgEntityData` with `Name == "Delta-7"`.

2. **Unit test** `CreateEntityRequestSystem_InitialAttributesJson_DoesNotOverwriteAffiliation`:  
   Post a request with `InitialDescriptors` containing a `dtEntityInfo` with
   `ForceIdentifier = FORCE_FRIENDLY` and `InitialAttributesJson = "{\"Name\":\"Echo-1\"}"`.
   Assert the resulting `IgEntityData` has `Name == "Echo-1"` AND `ForceId == ForceId.Friend`.

3. **Unit test** `CreateEntityRequestSystem_NullJson_NoPatch`:  
   Post a request with `InitialAttributesJson = null`. Assert system does not throw and processes
   normally.

4. **Existing tests** in `CreateEntityRequestSystemTests.cs` continue passing (update any that
   reference `InitialAttributes`).

---

### ATTR-S5T3 — `UpdateEntityAttributeRequestSystem`: Full JSON Pipeline Integration

**File:** `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`

**Context:** See [ATTR-DESIGN.md §3.9](./ATTR-DESIGN.md#39-live-update-path-integration) and
[§3.10](./ATTR-DESIGN.md#310-chunk-tick-egress-correction). Depends on ATTR-S1T2 (new wire field)
and ATTR-S4T3 (`EcsPatchContext.FlushDirtyMarks`).

**Changes:**

1. Add a `_jsonCompiler` field of type `JsonAttributeCompiler` (injected via constructor, same
   pattern as ATTR-S5T2).

2. Remove all code that reads `request.AttributeId` and `request.Payload` — those fields no longer
   exist after ATTR-S1T2.

3. Replace the body of the per-request handling method with:
   ```csharp
   // 1. Resolve entity
   if (!_entityMap.TryGetEntity(request.EntityId, out var entity))
   {
       WriteAck(request.RequestId, SstErrorCode.EntityNotFound);
       return;
   }

   // 2. Build live-ECS patch context
   var context = new EcsPatchContext(repo, entity, _jsonCompiler.Routes);

   // 3. Fan JSON through the same zero-alloc compiler
   _jsonCompiler.Compile(request.AttributePatchJson, context);

   // 4. Flush per-entity dirty marks (bypasses chunk ticks)
   context.FlushDirtyMarks();

   WriteAck(request.RequestId, SstErrorCode.Success);
   ```

4. Remove the now-dead `EntityAttributeCompiler.CompileFromWorld` call and the hardcoded
   `SmartEgressUtil.MarkDirty` for just `EntityInfoOrdinal`.

**Success Conditions:**

1. **Unit test** `UpdateEntityAttributeRequestSystem_JsonPatch_PatchesNameOnLiveEntity`:  
   Spawn entity into a live `EntityRepository` with `IgEntityData { Name = "old" }`.  
   Post `UpdateEntityAttributeRequest { EntityId = e.NetworkId, AttributePatchJson = "{\"Name\":\"new\"}" }`.  
   Execute one tick. Assert `repo.GetManagedComponentRO<IgEntityData>(entity).Name == "new"`.

2. **Unit test** `UpdateEntityAttributeRequestSystem_JsonPatch_FlushDirtyMarksCalledForEntityInfoOrdinal`:  
   Same setup with a mock `SmartEgressUtil` sink.  
   Assert dirty was marked for `(long)EDescriptorType.dtEntityInfo` exactly once.

3. **Unit test** `UpdateEntityAttributeRequestSystem_DualFieldPatch_BothApplied_SingleDirtyFlush`:  
   Post `{ "Name": "new", "Affiliation": "FORCE_HOSTILE" }`.  
   Assert `IgEntityData.Name == "new"` AND `ForceId == ForceId.Hostile`.  
   Assert `SmartEgressUtil.MarkDirty` called **once** for `dtEntityInfo` (ordinal deduplication).

4. **Unit test** `UpdateEntityAttributeRequestSystem_UnknownEntityId_AcksEntityNotFound`:  
   Post a request with an `EntityId` not in `_entityMap`.  
   Assert the ack carries `SstErrorCode.EntityNotFound` and no ECS mutation occurred.

5. **Unit test** `UpdateEntityAttributeRequestSystem_EmptyJson_AcksSuccess_NoMutation`:  
   Post `AttributePatchJson = "{}"` for a valid entity.  
   Assert ack is `Success` and the entity's components are unchanged.

6. **Existing tests** in the `UpdateEntityAttributeRequest` test class compile and pass after
   removing any references to `AttributeId` / `Payload`.

---

### ATTR-S5T4 — Register Descriptor Ordinals in SimHost Compiler Startup

**File:** `Hrot.SimHost/SimHostApp.cs` *(or `AttributeCompilerFactory.cs`)*

**Context:** See [ATTR-DESIGN.md Phase 5](./ATTR-DESIGN.md#phase-5-registration-and-integration)
and [§3.10](./ATTR-DESIGN.md#310-chunk-tick-egress-correction). Depends on ATTR-S4T2 (ordinal
param) and ATTR-S5T1 (compiler startup).

**Change:**  
Update the `AttributeCompilerBuilder` registrations added in ATTR-S5T1 to include the
correct `descriptorOrdinal` argument on every `Register*Path` call:

```csharp
const long EntityInfoOrdinal = (long)EDescriptorType.dtEntityInfo;
const long WorldPosOrdinal = (long)EDescriptorType.dtWorldPos;

var compiler = new AttributeCompilerBuilder()
    .RegisterReferencePath<IgEntityData>("Name",
        (IgEntityData c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c.Name = r.GetString() ?? string.Empty,
        descriptorOrdinal: EntityInfoOrdinal)

    .RegisterReferencePath<IgEntityData>("Affiliation",
        (IgEntityData c, ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            c.ForceId = MapAffiliationString(r.GetString()),
        descriptorOrdinal: EntityInfoOrdinal)

    .RegisterValuePath<SimTransform>(/* GeoPoint leaf paths */,
        /* setter */,
        descriptorOrdinal: WorldPosOrdinal)

    .Build();
```

Expose `JsonAttributeCompiler.Routes` (the internal routing table read-only dictionary) so that
`EcsPatchContext` can be constructed with it.

**Success Conditions:**

1. **Integration test** `AttributeCompiler_NamePatch_TriggersEntityInfoDirtyOnEcsPatchContext`:  
   Build the production compiler (with ordinals).  
   Construct `EcsPatchContext(repo, entity, compiler.Routes)`.  
   Call `Compile("{\"Name\":\"X\"}", context)`.  
   Call `context.FlushDirtyMarks()`.  
   Assert `SmartEgressUtil.MarkDirty` was called with `(long)EDescriptorType.dtEntityInfo`.

2. **Integration test** `AttributeCompiler_GeoPatch_TriggersWorldPosDirty`:  
   Same setup with a GeoPoint JSON string.  
   Assert `SmartEgressUtil.MarkDirty` was called with `(long)EDescriptorType.dtWorldPos`.

3. **Unit test** `ListPatchContext_FlushDirtyMarks_IsNoOp`:  
   Call `FlushDirtyMarks()` on a `ListPatchContext` after mutations.  
   Assert no `SmartEgressUtil.MarkDirty` calls were made (creation path never triggers egress
   from the compiler — spawning pipeline handles it separately).

---

## Phase 6: Unified Descriptor Routing (Advanced)

### ATTR-S6T1 — `DescriptorMapper` `dtEntityInfo` Uses Routing Delegates

**File:** `Hrot.Map.Common/Replication/Utils/DescriptorMapper.cs`

**Context:** See [ATTR-DESIGN.md Phase 6](./ATTR-DESIGN.md#phase-6-unified-descriptor-routing-advanced).

**Change:**  
Refactor the `dtEntityInfo` switch case so it invokes the registered `"Name"` and `"Affiliation"`
delegates from the shared `JsonAttributeCompiler` routing table via a `ListPatchContext`, rather
than constructing an `IgEntityData` inline:

```csharp
case EDescriptorType.dtEntityInfo:
    // Previously: result.Add(new IgEntityData { Name = ..., ForceId = ..., CommanderId = ... });
    // Now: feed into the shared routing table
    var ctx = new ListPatchContext(result);
    _compiler.ApplyNameField(ctx, d.EntityInfo.Name);
    _compiler.ApplyAffiliationField(ctx, d.EntityInfo.ForceIdentifier);
    // CommanderId is not in the JSON schema — set directly:
    ctx.GetManagedComponent<IgEntityData>().CommanderId = d.EntityInfo.CommanderId;
    result = ctx.FlushComponents();
    break;
```

A helper `DescriptorMapper` overload that accepts a `JsonAttributeCompiler` must be introduced;
the existing overload without the compiler retains its current behaviour for backward compatibility.

**Success Conditions:**

1. **Existing unit tests** for `DescriptorMapper` (`DescriptorMapperTests`) pass unchanged when
   called via the existing overload.

2. **Unit test** `DescriptorMapper_WithCompiler_DtEntityInfoProducesIgEntityData`:  
   Call the new overload with a compiler and a `dtEntityInfo` descriptor.  
   Assert the result contains an `IgEntityData` with the correct `Name`, `ForceId`, `CommanderId`.

3. **Unit test** `DescriptorMapper_WithCompiler_NoDuplicateIgEntityData`:  
   Call with `dtEntityInfo` only. Assert exactly one `IgEntityData` in the result.

---

### ATTR-S6T2 — `DescriptorMapper` `dtWorldPos` Uses Routing Delegates

**File:** `Hrot.Map.Common/Replication/Utils/DescriptorMapper.cs`

**Context:** See [ATTR-DESIGN.md Phase 6](./ATTR-DESIGN.md#phase-6-unified-descriptor-routing-advanced).

**Change:**  
The `dtWorldPos` case currently manually calls `geoTransform.ToCartesian` and constructs a
`SimTransform` inline. Under unified routing, a shared GeoPoint delegate handles this conversion.

Introduce a method `DescriptorMapper.ApplyWorldPosDescriptor(ListPatchContext ctx, WorldPos geo,
IGeographicTransform geoTransform)` that applies the coordinate conversion via the same logic
used by the `"GeoPoint"` JSON path setter. Wire the `dtWorldPos` case to call this method.

**Success Conditions:**

1. **Existing unit tests** for the `dtWorldPos` case in `DescriptorMapperTests` pass unchanged.

2. **Unit test** `DescriptorMapper_WorldPos_SharedDelegate_ProducesSameResultAsDirectPath`:  
   Process the same `WorldPos` data via `DescriptorMapper` (descriptor path) and via
   `JsonAttributeCompiler.Compile("{\"GeoPoint\":{\"Latitude\":32.1,...}}", ...)` (JSON path).  
   Assert both produce a `SimTransform` with the same `Position` and `Rotation` values.
