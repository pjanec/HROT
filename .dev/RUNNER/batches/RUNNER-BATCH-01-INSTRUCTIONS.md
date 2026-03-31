# RUNNER-BATCH-01: ECS Component ID Safety (Phase R0)

**Batch Number:** RUNNER-BATCH-01  
**Tasks:** R0.1, R0.2  
**Phase:** R0 - ECS Component ID Safety  
**Estimated Effort:** 12-14 hours (2 days)  
**Priority:** CRITICAL  
**Dependencies:** None (operates on `Fdp.Kernel` only)

> **?? CRITICAL PREREQUISITE:** This batch MUST complete and be merged BEFORE any other Runner work begins. Phase R0 addresses non-deterministic ECS component ID assignment that would corrupt the Flight Recorder and cause memory safety violations when merging three independent binaries into one Runner process.

---

## ?? Onboarding & Workflow

### Developer Instructions

Welcome to the Runner implementation! Before we can build the aggregated application that combines SimHost, IG, and IOS into one process, we must first solve a critical Flight Recorder safety issue.

**The Problem:** `ComponentTypeRegistry` assigns component IDs using `_nextId++`, which depends on static constructor execution order. When three standalone binaries (SimHost.exe, IG.exe, IOS.exe) each load different assemblies, the same component struct (e.g., `SimTransform`) gets assigned different IDs in each binary. When we merge all three into one `Runner.exe` process, this causes:
1. **Flight Recorder corruption** � recordings are binary dumps indexed by component ID. Wrong IDs = wrong memory offsets = silent data corruption.
2. **Cross-subsystem memory safety violations** � component tables accessed with wrong type IDs.

**The Solution (Phase R0):** Make component IDs **deterministic** by introducing explicit `[ComponentId(byte)]` attributes (mirroring the existing `[EventId(int)]` pattern) and a central `GlobalComponentIds` constant catalog. Additionally, save a schema manifest in Flight Recorder `.meta.json` files to detect struct layout drift across versions.

This batch operates entirely on `Fdp.Kernel` and toolkit libraries. It has no dependency on SimHost/IG/IOS application code and can be completed independently.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream\README.md` � How to work with batches
2. **Design Document:** `docs\design\DESIGN-RUNNER.md` � Read **Section 11: ECS Component ID Safety** carefully
3. **Task Details:** `docs\design\TASK-DETAILS-RUNNER.md` � Read **Phase R0** section
4. **Task Tracker:** `docs\design\TASK-TRACKER.md` � See RUNNER Phase R0 tasks
5. **Code Standards:** `.dev-workstream\guides\CODE-STANDARDS.md` � �0 (Test Quality), �1 (No Magic Numbers)

### Architect Context
- **Architect Decision Q10 (2026-03-05):** Component IDs MUST be deterministic before merging binaries
- **Design Talk (2026-03-05):** Flight Recorder schema manifest required for safety

### Source Code Location
- **Primary Work Area:** `FDP\Kernel\Fdp.Kernel\` (ComponentTypeRegistry, attributes)
- **Secondary Areas:** 
  - `FDP\Toolkits\FDP.Toolkit.Replication\` (component attributes)
  - `FDP\Toolkits\FDP.Toolkit.Vis2D\` (component attributes)
  - `Hrot.IG\` (component attributes)
- **Test Project:** `FDP\Kernel\Fdp.Kernel.Tests\` (new test file required)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream\reports\RUNNER-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream\questions\RUNNER-BATCH-01-QUESTIONS.md`

---

## Context

Phase R0 is a **blocking prerequisite** for all other Runner phases. It was identified during the Design Talk (2026-03-05) after discovering that `ComponentTypeRegistry` in `Fdp.Kernel` uses non-deterministic ID assignment (`_nextId++` based on static constructor order).

This batch implements two solutions:
1. **Explicit Component IDs** (R0.1) � `[ComponentId(byte)]` attribute + `GlobalComponentIds` catalog
2. **Flight Recorder Schema Safety** (R0.2) � Schema manifest + validator

**Related Tasks:**
- [R0.1](../../docs/design/TASK-DETAILS-RUNNER.md#r01-make-component-ids-deterministic) - Make Component IDs Deterministic
- [R0.2](../../docs/design/TASK-DETAILS-RUNNER.md#r02-implement-flight-recorder-schema-manifest) - Implement Flight Recorder Schema Manifest

---

## ?? Batch Objectives

**Primary Goal:** Make ECS component IDs deterministic and add Flight Recorder schema validation to prevent silent memory corruption when three binaries merge into one process.

**Success Criteria:**
- Component IDs are assigned from explicit attributes, not execution order
- ID collisions are detected at runtime
- Flight Recorder saves schema manifests in `.meta.json` files
- Playback validates schema before reading binary frames
- All existing tests pass
- New tests cover ID collisions, enforcement flags, and schema mismatches

---

## ? Tasks

### Task 1: Make Component IDs Deterministic (R0.1)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R0.1](../../docs/design/TASK-DETAILS-RUNNER.md#r01-make-component-ids-deterministic)

**Estimated:** 6-7 hours

#### Subtask 1.1: Create `ComponentIdAttribute`

**File:** `FDP\Kernel\Fdp.Kernel\ComponentIdAttribute.cs` (NEW FILE)

**Requirements:**
- Mirror the structure of existing `FDP\Kernel\Fdp.Kernel\EventIdAttribute.cs`
- Single `byte Id` property (components are limited to 0-255 by `BitMask256`)
- `[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]`

**Code Pattern:**
```csharp
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ComponentIdAttribute : Attribute
{
    public byte Id { get; }
    public ComponentIdAttribute(byte id) => Id = id;
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.2](../../docs/design/DESIGN-RUNNER.md#112-solution-explicit-componentid-attribute)

#### Subtask 1.2: Create `GlobalComponentIds` Catalog

**File:** `FDP\Kernel\Fdp.Kernel\GlobalComponentIds.cs` (NEW FILE)

**Requirements:**
- `public static class GlobalComponentIds`
- Block-allocated ID ranges (see DESIGN-RUNNER.md Section 11.3 for full table)
- All known component IDs from Fdp.Kernel, toolkits, and Hrot projects
- Comments clearly mark each block's reserved range

**Code Structure:**
```csharp
public static class GlobalComponentIds
{
    // Fdp.Kernel (0�19)
    public const byte SimTransform        = 0;
    public const byte SimVelocity         = 1;
    public const byte HealthData          = 2;
    public const byte GlobalTime          = 3;
    public const byte IsActiveTag         = 4;
    public const byte LifecycleDescriptor = 5;
    public const byte HierarchyNode       = 6;
    public const byte PartDescriptor      = 7;

    // FDP.Toolkit.Replication (50�79)
    public const byte NetworkIdentity     = 50;
    public const byte NetworkAuthority    = 51;
    public const byte NetworkPosition     = 52;
    public const byte NetworkVelocity     = 53;
    public const byte NetworkSpawnRequest = 54;
    public const byte PartMetadata        = 55;

    // FDP.Toolkit.Vis2D (80�109)
    public const byte MapDisplayComponent = 80;
    public const byte VisHierarchyNode    = 81;
    public const byte AggregateState      = 82;
    public const byte AggregateRoot       = 83;

    // Hrot.IG (110�139)
    public const byte ResolvedStyle       = 110;
    public const byte CullingState        = 111;
    public const byte SelectionState      = 112;
    public const byte VisualEffectState   = 113;
    public const byte TracerTarget        = 114;
    
    // Reserved (200�255) � Future use
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.3](../../docs/design/DESIGN-RUNNER.md#113-solution-globalcomponentids-central-catalog)

**Important:** Find ALL component structs across the codebase and assign them IDs. Use file search to locate `struct` definitions in:
- `FDP\Kernel\Fdp.Kernel\` 
- `FDP\Toolkits\FDP.Toolkit.Replication\`
- `FDP\Toolkits\FDP.Toolkit.Vis2D\`
- `Hrot.IG\`
- Any other toolkit/project with ECS components

#### Subtask 1.3: Update `ComponentTypeRegistry`

**File:** `FDP\Kernel\Fdp.Kernel\ComponentTypeRegistry.cs` (UPDATE)

**Requirements:**
- Modify `GetOrRegisterManaged<T>()` to read `ComponentIdAttribute` via reflection
- Modify unmanaged equivalent (if separate method exists)
- If attribute present: use `attribute.Id` as the component ID
- If attribute absent and `FdpConfig.EnforceExplicitComponentIds == true`: throw `InvalidOperationException` with clear message
- If attribute absent and enforcement off: fall back to `_nextId++` (legacy behavior)
- Always detect ID collisions: throw `InvalidOperationException` if two types declare the same ID

**Code Pattern:**
```csharp
public static int GetOrRegisterManaged<T>() where T : struct
{
    var type = typeof(T);
    if (_typeToId.TryGetValue(type, out var existingId))
        return existingId;

    // Check for explicit ComponentId attribute
    var attr = type.GetCustomAttribute<ComponentIdAttribute>();
    
    int assignedId;
    if (attr != null)
    {
        assignedId = attr.Id;
        
        // Collision detection
        if (_idToType.ContainsKey(assignedId))
        {
            var existingType = _idToType[assignedId];
            throw new InvalidOperationException(
                $"Component ID collision: {type.Name} and {existingType.Name} both declare [ComponentId({assignedId})]");
        }
    }
    else
    {
        // Enforcement check
        if (FdpConfig.EnforceExplicitComponentIds)
        {
            throw new InvalidOperationException(
                $"Component {type.Name} missing [ComponentId] attribute. " +
                "Set FdpConfig.EnforceExplicitComponentIds = false to allow auto-assignment.");
        }
        
        // Legacy auto-assignment
        assignedId = _nextId++;
    }
    
    _typeToId[type] = assignedId;
    _idToType[assignedId] = type;
    return assignedId;
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.2](../../docs/design/DESIGN-RUNNER.md#112-solution-explicit-componentid-attribute)

#### Subtask 1.4: Add Enforcement Flag to `FdpConfig`

**File:** `FDP\Kernel\Fdp.Kernel\FdpConfig.cs` (UPDATE)

**Requirements:**
- Add `public static bool EnforceExplicitComponentIds { get; set; }` property
- Default value: `false` (allows legacy auto-assignment for existing tests)
- Production entry-points (`Program.cs` of SimHost/IG/IOS) will set it to `true` in future batches

**Code Pattern:**
```csharp
public static class FdpConfig
{
    // ... existing config ...
    
    /// <summary>
    /// When true, all components MUST have explicit [ComponentId] attributes.
    /// When false, components without attributes fall back to auto-increment assignment.
    /// Default: false (for test compatibility during transition).
    /// Production binaries should set this to true before constructing any ECS world.
    /// </summary>
    public static bool EnforceExplicitComponentIds { get; set; } = false;
}
```

#### Subtask 1.5: Apply Attributes to All Component Structs

**Files:** Multiple files across `Fdp.Kernel`, toolkits, and `Hrot.IG` (UPDATE)

**Requirements:**
- Add `[ComponentId(GlobalComponentIds.X)]` attribute to every component struct
- Structs to annotate (find with file search):
  - **Fdp.Kernel:** `SimTransform`, `SimVelocity`, `HealthData`, `GlobalTime`, `IsActiveTag`, `LifecycleDescriptor`, `HierarchyNode`, `PartDescriptor`
  - **FDP.Toolkit.Replication:** `NetworkIdentity`, `NetworkAuthority`, `NetworkPosition`, `NetworkVelocity`, `NetworkSpawnRequest`, `PartMetadata`
  - **FDP.Toolkit.Vis2D:** `MapDisplayComponent`, `VisHierarchyNode`, `AggregateState`, `AggregateRoot`
  - **Hrot.IG:** `ResolvedStyle`, `CullingState`, `SelectionState`, `VisualEffectState`, `TracerTarget`

**Code Pattern:**
```csharp
[ComponentId(GlobalComponentIds.SimTransform)]
public struct SimTransform
{
    // ... existing fields ...
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.2](../../docs/design/DESIGN-RUNNER.md#112-solution-explicit-componentid-attribute)

**Important:** Use file search to find ALL component structs. Do not miss any. Missing components will cause crashes when enforcement is enabled.

#### Subtask 1.6: Write Unit Tests

**File:** `FDP\Kernel\Fdp.Kernel.Tests\ComponentIdAttributeTests.cs` (NEW FILE)

**Requirements:**
- Test ID collision detection (two structs with `[ComponentId(42)]` ? must throw)
- Test enforcement flag (struct without attribute + `EnforceExplicitComponentIds = true` ? must throw)
- Test explicit ID assignment (declared ID is returned, not auto-incremented value)
- Test registry clear and re-read (IDs are read from attribute after `Clear()`)
- Minimum 6 tests covering all success criteria from R0.1

**Test Pattern:**
```csharp
[Fact]
public void ComponentTypeRegistry_ThrowsOnIdCollision()
{
    ComponentTypeRegistry.Clear();
    
    // Arrange: Two test structs with same ID
    [ComponentId(42)] struct TestA { }
    [ComponentId(42)] struct TestB { }
    
    // Act: Register first
    var idA = ComponentTypeRegistry.GetOrRegisterManaged<TestA>();
    Assert.Equal(42, idA);
    
    // Act/Assert: Second throws
    var ex = Assert.Throws<InvalidOperationException>(
        () => ComponentTypeRegistry.GetOrRegisterManaged<TestB>());
    Assert.Contains("collision", ex.Message, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("TestA", ex.Message);
    Assert.Contains("TestB", ex.Message);
}

[Fact]
public void ComponentTypeRegistry_EnforcesExplicitIds_WhenFlagSet()
{
    ComponentTypeRegistry.Clear();
    FdpConfig.EnforceExplicitComponentIds = true;
    
    try
    {
        struct TestNoAttribute { }
        
        var ex = Assert.Throws<InvalidOperationException>(
            () => ComponentTypeRegistry.GetOrRegisterManaged<TestNoAttribute>());
        Assert.Contains("missing [ComponentId]", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
        FdpConfig.EnforceExplicitComponentIds = false; // Reset for other tests
    }
}

[Fact]
public void ComponentTypeRegistry_ReturnsExplicitId_NotAutoIncrement()
{
    ComponentTypeRegistry.Clear();
    
    [ComponentId(100)] struct TestExplicit { }
    
    var id = ComponentTypeRegistry.GetOrRegisterManaged<TestExplicit>();
    Assert.Equal(100, id);
}
```

**Success Criteria (from TASK-DETAILS-RUNNER.md R0.1 SC-6):**
- ? ID collision between two structs ? throws
- ? Struct without attribute + enforcement ? throws
- ? Explicit ID is returned (not auto-incremented value)
- ? Registry clear re-reads from attribute

---

### Task 2: Implement Flight Recorder Schema Manifest (R0.2)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R0.2](../../docs/design/TASK-DETAILS-RUNNER.md#r02-implement-flight-recorder-schema-manifest)

**Estimated:** 6-7 hours

**Dependencies:** R0.1 complete (stable IDs must exist before schema snapshots are meaningful)

#### Subtask 2.1: Create `ComponentSchemaInfo` Model

**File:** `FDP\Kernel\Fdp.Kernel\FlightRecorder\ComponentSchemaInfo.cs` (NEW FILE)

**Requirements:**
- Create alongside existing `RecordingMetadata.cs` in Flight Recorder namespace
- Serializable class (used in JSON `.meta.json` files)
- Properties: `string Name`, `int Size`, `ulong LayoutHash`, `bool IsManaged`
- Extend `RecordingMetadata` with `public Dictionary<int, ComponentSchemaInfo>? SchemaManifest { get; set; }` (nullable for backward compatibility)

**Code Pattern:**
```csharp
namespace Fdp.Kernel.FlightRecorder
{
    public class ComponentSchemaInfo
    {
        public string Name { get; set; } = string.Empty;
        public int Size { get; set; }
        public ulong LayoutHash { get; set; }
        public bool IsManaged { get; set; }
    }
}
```

**Update `RecordingMetadata.cs`:**
```csharp
public class RecordingMetadata
{
    // ... existing properties ...
    
    /// <summary>
    /// Schema manifest captured at record time. Null for old recordings without manifest.
    /// Key: component ID, Value: schema info (size, layout hash, type name).
    /// </summary>
    public Dictionary<int, ComponentSchemaInfo>? SchemaManifest { get; set; }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.4](../../docs/design/DESIGN-RUNNER.md#114-the-second-problem-silent-flight-recorder-schema-drift)

#### Subtask 2.2: Create `ComponentLayoutHasher`

**File:** `FDP\Kernel\Fdp.Kernel\FlightRecorder\ComponentLayoutHasher.cs` (NEW FILE)

**Requirements:**
- `public static ulong ComputeHash(Type type)` method
- FNV-1a 64-bit hash (prime `0x100000001B3`, offset `0xcbf29ce484222325`)
- Iterate all instance fields in declaration order (`BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic`)
- For each field: hash `{field.Name}|{field.FieldType.FullName}|{Marshal.OffsetOf(type, field.Name)}`
- **CRITICAL:** Do NOT use `GetHashCode()` (not deterministic across runs)

**Code Pattern:**
```csharp
public static class ComponentLayoutHasher
{
    private const ulong FnvPrime = 0x100000001B3;
    private const ulong FnvOffsetBasis = 0xcbf29ce484222325;
    
    public static ulong ComputeHash(Type type)
    {
        var hash = FnvOffsetBasis;
        
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .OrderBy(f => f.MetadataToken) // Declaration order
                         .ToArray();
        
        foreach (var field in fields)
        {
            // Hash field name
            hash = HashString(hash, field.Name);
            hash ^= (byte)'|';
            hash *= FnvPrime;
            
            // Hash field type name
            hash = HashString(hash, field.FieldType.FullName ?? field.FieldType.Name);
            hash ^= (byte)'|';
            hash *= FnvPrime;
            
            // Hash field offset
            var offset = (int)Marshal.OffsetOf(type, field.Name);
            hash = HashInt(hash, offset);
        }
        
        return hash;
    }
    
    private static ulong HashString(ulong hash, string str)
    {
        foreach (var ch in str)
        {
            hash ^= ch;
            hash *= FnvPrime;
        }
        return hash;
    }
    
    private static ulong HashInt(ulong hash, int value)
    {
        hash ^= (byte)(value & 0xFF);
        hash *= FnvPrime;
        hash ^= (byte)((value >> 8) & 0xFF);
        hash *= FnvPrime;
        hash ^= (byte)((value >> 16) & 0xFF);
        hash *= FnvPrime;
        hash ^= (byte)((value >> 24) & 0xFF);
        hash *= FnvPrime;
        return hash;
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.4](../../docs/design/DESIGN-RUNNER.md#114-the-second-problem-silent-flight-recorder-schema-drift)

#### Subtask 2.3: Create `SchemaValidator`

**File:** `FDP\Kernel\Fdp.Kernel\FlightRecorder\SchemaValidator.cs` (NEW FILE)

**Requirements:**
- `public static void Validate(RecordingMetadata meta)` method
- If `meta.SchemaManifest` is `null`: log warning, return without throwing (old recording compatibility)
- For each entry in manifest: resolve current type, recompute hash/size, compare
- On mismatch: throw `InvalidOperationException` with detailed message showing recorded vs current hash/size

**Code Pattern:**
```csharp
public static class SchemaValidator
{
    public static void Validate(RecordingMetadata meta)
    {
        if (meta.SchemaManifest == null)
        {
            // Old recording without manifest
            Console.WriteLine("WARNING: Recording has no SchemaManifest. Playback may fail if struct layouts changed.");
            return;
        }
        
        foreach (var (componentId, recordedSchema) in meta.SchemaManifest)
        {
            // Resolve current type from registry
            var currentType = ComponentTypeRegistry.GetTypeById(componentId);
            if (currentType == null)
            {
                throw new InvalidOperationException(
                    $"Component ID {componentId} ({recordedSchema.Name}) not registered in current binary.");
            }
            
            // Recompute current schema
            var currentSize = Marshal.SizeOf(currentType);
            var currentHash = ComponentLayoutHasher.ComputeHash(currentType);
            
            // Validate size
            if (currentSize != recordedSchema.Size)
            {
                throw new InvalidOperationException(
                    $"Component {recordedSchema.Name} layout has changed: " +
                    $"recorded size={recordedSchema.Size}, current size={currentSize}");
            }
            
            // Validate layout hash
            if (currentHash != recordedSchema.LayoutHash)
            {
                throw new InvalidOperationException(
                    $"Component {recordedSchema.Name} layout has changed: " +
                    $"recorded hash 0x{recordedSchema.LayoutHash:X16} vs current 0x{currentHash:X16} " +
                    $"(recorded size={recordedSchema.Size}, current size={currentSize})");
            }
        }
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.4](../../docs/design/DESIGN-RUNNER.md#114-the-second-problem-silent-flight-recorder-schema-drift)

#### Subtask 2.4: Update `AsyncRecorder.Dispose()`

**File:** `FDP\Kernel\Fdp.Kernel\FlightRecorder\AsyncRecorder.cs` (UPDATE)

**Requirements:**
- Before calling `MetadataSerializer.Serialize(...)`, populate `_metadata.SchemaManifest`
- Iterate `ComponentTypeRegistry.GetRecordableTypeIds()` (or equivalent method to get all registered component IDs)
- For each ID: create `ComponentSchemaInfo` using `ComponentLayoutHasher.ComputeHash()` and `Marshal.SizeOf()`
- Store in `_metadata.SchemaManifest = new Dictionary<int, ComponentSchemaInfo>()`

**Code Pattern:**
```csharp
public void Dispose()
{
    // ... existing frame finalization ...
    
    // Populate schema manifest
    _metadata.SchemaManifest = new Dictionary<int, ComponentSchemaInfo>();
    var recordableIds = ComponentTypeRegistry.GetRecordableTypeIds(); // Or equivalent
    
    foreach (var componentId in recordableIds)
    {
        var type = ComponentTypeRegistry.GetTypeById(componentId);
        if (type == null) continue;
        
        _metadata.SchemaManifest[componentId] = new ComponentSchemaInfo
        {
            Name = type.FullName ?? type.Name,
            Size = Marshal.SizeOf(type),
            LayoutHash = ComponentLayoutHasher.ComputeHash(type),
            IsManaged = /* determine if managed */ false // Adjust based on registry metadata
        };
    }
    
    // Serialize metadata with manifest
    MetadataSerializer.Serialize(_metadataPath, _metadata);
    
    // ... existing cleanup ...
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.4](../../docs/design/DESIGN-RUNNER.md#114-the-second-problem-silent-flight-recorder-schema-drift)

**Note:** If `ComponentTypeRegistry` does not have a `GetRecordableTypeIds()` method, you may need to add one or iterate `_idToType` dictionary directly.

#### Subtask 2.5: Update `PlaybackController` Constructor

**File:** `FDP\Kernel\Fdp.Kernel\FlightRecorder\PlaybackController.cs` (UPDATE)

**Requirements:**
- After deserializing `.meta.json` (via `MetadataSerializer.Deserialize()`), call `SchemaValidator.Validate(_metadata)`
- This must happen BEFORE opening the binary frame stream
- Let exceptions surface to caller (do not catch and suppress)

**Code Pattern:**
```csharp
public PlaybackController(string recordingPath)
{
    _metadataPath = Path.ChangeExtension(recordingPath, ".meta.json");
    _binaryPath = Path.ChangeExtension(recordingPath, ".bin");
    
    // Deserialize metadata
    _metadata = MetadataSerializer.Deserialize(_metadataPath);
    
    // Validate schema BEFORE opening binary stream
    SchemaValidator.Validate(_metadata);
    
    // Open binary stream
    _binaryStream = File.OpenRead(_binaryPath);
    
    // ... rest of initialization ...
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 11.4](../../docs/design/DESIGN-RUNNER.md#114-the-second-problem-silent-flight-recorder-schema-drift)

#### Subtask 2.6: Write Unit Tests

**File:** `FDP\Kernel\Fdp.Kernel.Tests\FlightRecorderSchemaTests.cs` (NEW FILE)

**Requirements:**
- Test hash stability (same struct ? identical hash across multiple calls)
- Test hash changes when field added (even if size unchanged due to padding)
- Test hash changes when fields reordered
- Test validator throws on layout hash mismatch
- Test validator throws on size mismatch
- Test validator succeeds silently when `SchemaManifest` is `null` (old recording)
- Minimum 6 tests covering all success criteria from R0.2

**Test Pattern:**
```csharp
[Fact]
public void ComponentLayoutHasher_StableAcrossCalls()
{
    struct TestStruct
    {
        public int Field1;
        public float Field2;
    }
    
    var hash1 = ComponentLayoutHasher.ComputeHash(typeof(TestStruct));
    var hash2 = ComponentLayoutHasher.ComputeHash(typeof(TestStruct));
    
    Assert.Equal(hash1, hash2);
}

[Fact]
public void ComponentLayoutHasher_ChangesWhenFieldAdded()
{
    struct TestA
    {
        public int Field1;
    }
    
    struct TestB
    {
        public int Field1;
        public int Field2; // Added field
    }
    
    var hashA = ComponentLayoutHasher.ComputeHash(typeof(TestA));
    var hashB = ComponentLayoutHasher.ComputeHash(typeof(TestB));
    
    Assert.NotEqual(hashA, hashB);
}

[Fact]
public void SchemaValidator_ThrowsOnLayoutHashMismatch()
{
    struct TestComponent { public int Value; }
    
    var metadata = new RecordingMetadata
    {
        SchemaManifest = new Dictionary<int, ComponentSchemaInfo>
        {
            [1] = new ComponentSchemaInfo
            {
                Name = "TestComponent",
                Size = 4,
                LayoutHash = 0xDEADBEEF // Wrong hash
            }
        }
    };
    
    // Mock registry to return TestComponent for ID 1
    // (May need to set up ComponentTypeRegistry or mock it)
    
    var ex = Assert.Throws<InvalidOperationException>(
        () => SchemaValidator.Validate(metadata));
    Assert.Contains("layout has changed", ex.Message, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("0xDEADBEEF", ex.Message);
}

[Fact]
public void SchemaValidator_SucceedsWhenManifestNull()
{
    var metadata = new RecordingMetadata
    {
        SchemaManifest = null // Old recording
    };
    
    // Should not throw
    SchemaValidator.Validate(metadata);
}
```

**Success Criteria (from TASK-DETAILS-RUNNER.md R0.2 SC-6):**
- ? Hash stable across two calls on identical struct
- ? Hash changes when field added
- ? Hash changes when fields reordered
- ? Validator throws on layout hash mismatch with descriptive message
- ? Validator throws on size mismatch
- ? Validator succeeds silently when manifest is null

---

## ?? Testing Requirements

**Minimum Test Count:** 12 tests total
- R0.1 Component ID tests: 6+ tests
- R0.2 Schema validation tests: 6+ tests

**Test Categories:**
1. **ID Assignment:** Explicit IDs, collisions, enforcement
2. **Registry Behavior:** Clear and re-read, auto-increment fallback
3. **Schema Hashing:** Stability, field changes, reordering
4. **Schema Validation:** Hash mismatch, size mismatch, null manifest

**Quality Standards:**
- Tests MUST verify actual behavior (collisions throw, enforcement throws, hash changes)
- Tests MUST NOT just check compilation or string presence
- Edge cases: null manifest (old recordings), ID collisions, missing attributes

---

## ?? Report Requirements

When submitting your report, answer these questions:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase (ComponentTypeRegistry, FlightRecorder)? What would you improve?

**Q3:** How many component structs did you find across the entire codebase? Which projects had them? (List exact counts and paths)

**Q4:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q5:** Did you discover any edge cases not mentioned in the spec (e.g., generic component structs, nested structs, etc.)?

**Q6:** Are there any performance concerns with the schema hashing or validation? (e.g., reflection overhead during recording/playback startup)

---

## ?? Success Criteria

This batch is DONE when:
- [ ] R0.1 Complete: `ComponentIdAttribute`, `GlobalComponentIds`, registry update, attributes applied, tests passing
- [ ] R0.2 Complete: Schema manifest, hasher, validator, recorder/playback integration, tests passing
- [ ] All existing `Fdp.Kernel.Tests` still pass (no regressions)
- [ ] All new tests pass (12+ tests)
- [ ] Report submitted with answers to all 6 questions

---

## ?? Quality Standards

**? CODE QUALITY EXPECTATIONS**
- Follow CODE-STANDARDS.md �1 (No Magic Numbers): Use named constants for hash prime/offset, max component ID, etc.
- All new public APIs have XML doc comments
- Exception messages are descriptive and actionable
- Hash algorithm is deterministic (no `GetHashCode()`)

**? TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I create this object"
- **REQUIRED:** Tests that verify actual collisions throw, enforcement works, hash changes on struct modification
- **REQUIRED:** Tests verify exception messages contain expected keywords (case-insensitive)
- See CODE-STANDARDS.md �0 for full test quality checklist

**? REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document ALL component structs found (count per project/toolkit)
- **REQUIRED:** Document design decisions made (e.g., how you handled generic structs, if encountered)
- **REQUIRED:** Share insights on Flight Recorder architecture and potential improvements

---

## ?? Common Pitfalls to Avoid

1. **Missing Component Structs:** Use file search to find ALL component structs across ALL projects. Missing even one will cause crashes when enforcement is enabled.

2. **Wrong Hash Algorithm:** Do NOT use `GetHashCode()` (not deterministic). Use FNV-1a as specified.

3. **Field Order:** `GetFields()` returns fields in arbitrary order unless sorted. Use `OrderBy(f => f.MetadataToken)` for declaration order.

4. **Metadata Token Stability:** Field order via `MetadataToken` is stable within a single compilation but may change across recompiles. The hash WILL change if struct is recompiled with fields reordered in source code. This is CORRECT behavior (detects reorders).

5. **Null Manifest Handling:** Old recordings will have `SchemaManifest = null`. Validator must handle this gracefully (log warning, don't throw).

6. **Registry Clear in Tests:** Use `ComponentTypeRegistry.Clear()` at the start of each test to avoid interference between tests.

7. **Enforcement Flag Reset:** Always reset `FdpConfig.EnforceExplicitComponentIds = false` in test cleanup (use `try/finally` or `IDisposable` fixture) to avoid breaking other tests.

---

## ?? Reference Materials

- **Design:** `docs\design\DESIGN-RUNNER.md` � Section 11 (ECS Component ID Safety)
- **Task Details:** `docs\design\TASK-DETAILS-RUNNER.md` � Phase R0
- **Task Tracker:** `docs\design\TASK-TRACKER.md` � RUNNER Phase R0
- **Code Standards:** `.dev-workstream\guides\CODE-STANDARDS.md` � �0 (Test Quality), �1 (No Magic Numbers)
- **Existing Attribute Pattern:** `FDP\Kernel\Fdp.Kernel\EventIdAttribute.cs` (mirror this for ComponentIdAttribute)
- **Existing Metadata Model:** `FDP\Kernel\Fdp.Kernel\FlightRecorder\RecordingMetadata.cs` (extend with SchemaManifest)

---

## ?? Workflow Reminder

1. **Read all required documents** (in order listed in Onboarding)
2. **Implement R0.1 first** (component IDs must be stable before schema manifest makes sense)
3. **Write tests as you go** (don't defer all tests to the end)
4. **Run ALL Fdp.Kernel tests** after each subtask (verify no regressions)
5. **Submit complete report** when both R0.1 and R0.2 are done

---

**Questions?** Create `.dev-workstream\questions\RUNNER-BATCH-01-QUESTIONS.md`

Good luck! This is critical infrastructure work � take your time and be thorough. ??
