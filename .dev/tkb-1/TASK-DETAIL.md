# TASK-DETAIL: Transient Knowledge Base (TKB) — tkb-1

**Workstream:** tkb-1  
**Design reference:** `.dev/tkb-1/DESIGN.md`  
**Task ID prefix:** `TKB-`

---

## Phase 1: Domain Schema & Attributes

---

### TKB-001 — Define `[TkbDescriptor]` attribute and field-level relational attributes

**Phase:** 1  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Target namespace:** `Fdp.Toolkit.Tkb.Attributes`

**Description:**  
Create the semantic attributes that mark TKB descriptor DTOs and their reference fields. These
attributes are the single source of truth for the descriptor naming scheme; the C# class name
is decoupled from the JSON property key.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/TkbDescriptorAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/WeaponRefAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/AmmoRefAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/ModelRefAttribute.cs`

**Specification:**

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct,
                Inherited = false, AllowMultiple = false)]
public sealed class TkbDescriptorAttribute : Attribute
{
    public string HierarchicalName { get; }
    public TkbDescriptorAttribute(string hierarchicalName) { ... }
}
```

- `HierarchicalName` must not be null or whitespace (throw `ArgumentException`).
- `HierarchicalName` must NOT include a `#PartId` postfix.
- Every descriptor except `"TkbMaster"` must carry a domain prefix in name: `Gen.`, `CGFX.`,
  `BIG.`, etc. (enforced by the source generator as a warning, not the attribute itself).

Field-level attributes:
```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class WeaponRefAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AmmoRefAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ModelRefAttribute : Attribute { }
```

**Success conditions:**
- All four attribute types compile without errors.
- `[TkbDescriptor("")]` throws `ArgumentException` at construction time.
- `[TkbDescriptor(null)]` throws `ArgumentException` at construction time.
- `[TkbDescriptor("Platform#1")]` throws `ArgumentException`: the `#` character is the
  runtime instance delimiter and must not appear in a schema-level name.
- No references to ECS types, MessagePack, or any runtime framework.

---

### TKB-002 — Implement concrete descriptor DTOs

**Phase:** 1  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits` (or HROT-layer assembly for HROT-specific ones)  
**Target namespace:** `Fdp.Toolkit.Tkb.Domain`

**Description:**  
Create the four concrete DTOs that represent the minimum viable descriptor set for the initial
TKB implementation. All DTOs are pure POCOs; no ECS, no MessagePack.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/TkbMasterDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/VehicleParametersDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/WeaponCapabilitiesDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/AmmoWeaponBallisticsDto.cs`

**Specification:**

```csharp
[TkbDescriptor("TkbMaster")]
public record TkbMasterDto
{
    public string CustomName { get; init; } = string.Empty;
    [Description("SISO-REF-010-2015 DIS Entity Type (e.g. 1.1.225.1.1.1.0)")]
    public string DisType { get; init; } = string.Empty;
}

[TkbDescriptor("Gen.VehicleParameters")]
public record VehicleParametersDto
{
    [EditUnit("kg")] public float Mass { get; init; }
    [EditUnit("m")] public float Length { get; init; }
    [EditUnit("m")] public float Width { get; init; }
    [EditUnit("m/s")] public float MaxSpeedFwd { get; init; }
    [EditUnit("m/s")] public float MaxSpeedRev { get; init; }
    [EditUnit("m/s^2")] public float MaxAccel { get; init; }
}

[TkbDescriptor("Gen.WeaponCapabilities")]
public record WeaponCapabilitiesDto
{
    [EditUnit("m")] public float EffectiveRange { get; init; }
    [EditUnit("rpm")] public float RateOfFire { get; init; }
    public int MagazineCapacity { get; init; }
}

[TkbDescriptor("Gen.AmmoWeaponBallistics")]
public record AmmoWeaponBallisticsDto
{
    [WeaponRef]
    [Description("Weapon TKB GUID this ballistic profile applies to. 0 = Generic.")]
    public long WeaponGuid { get; init; }
    [EditUnit("m/s")] public float MuzzleSpeed { get; init; }
    [Description("Base damage applied on hit.")] public float Damage { get; init; }
}
```

**Success conditions:**
- All four DTOs carry `[TkbDescriptor]`.
- No type references ECS base classes, `EntityRepository`, `MessagePackObject`, etc.
- `AmmoWeaponBallisticsDto` supports multi-instance (same type registered with different `PartId`)
  — this is handled by `TkbTemplate.AddDescriptor<T>(dto, partId)` and does not require changes
  to the DTO itself.
- Sample JSON files (`M1_Abrams.json`, `120mm_M256.json`, `120mm_APFSDS.json`) deserialize
  correctly when parsed with `System.Text.Json` and the relevant DTO.

---

## Phase 2: VFS and Transport Tier

---

### TKB-003 — Implement `TkbEntityFile`, `ITkbStorageStrategy`, and `RawDirectoryTkbProvider`

**Phase:** 2  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Target namespace:** `Fdp.Toolkit.Tkb.Vfs`

**Description:**  
Define the VFS boundary types and the raw-directory backend. This is the authoring backend (TKB
Editor) and also used when a TKB is supplied as an unzipped folder.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/TkbEntityFile.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/ITkbStorageStrategy.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/RawDirectoryTkbProvider.cs`

**Specification:**

```csharp
public readonly record struct TkbEntityFile(
    string CategoryPath,
    string FileName,
    Stream JsonStream);

public interface ITkbStorageStrategy : IDisposable
{
    IEnumerable<TkbEntityFile> EnumerateEntityFiles();
    void WriteEntityFile(string relativeFilePath, string jsonContent);
    void DeleteEntityFile(string relativeFilePath);
}
```

`RawDirectoryTkbProvider`:
- Constructor: `RawDirectoryTkbProvider(string rootPath)`.
- `EnumerateEntityFiles()`: recursively enumerate `*.json`; compute `CategoryPath` from relative
  directory using forward slashes; `FileName` = `Path.GetFileNameWithoutExtension(path)`;
  open `FileStream` and yield; close on next iteration (lazy enumeration).
- `WriteEntityFile(relPath, content)`: `Directory.CreateDirectory` for missing intermediate
  dirs; `File.WriteAllText` with UTF-8 (no BOM).
- `DeleteEntityFile(relPath)`: `File.Delete` if exists; no error if not found.
- `Dispose()`: no-op (file streams are closed per-iteration).

**CategoryPath convention:** always uses forward slashes; relative to root; no leading or
trailing slash. E.g. for `<root>/Platform/Vehicle/Military/MBT/Merkava Mk4.json`, the
`CategoryPath` is `"Platform/Vehicle/Military/MBT"`.

**Success conditions:**
- `EnumerateEntityFiles()` on a test directory yields one `TkbEntityFile` per `.json` file.
- `CategoryPath` is computed correctly (forward slashes, relative, no trailing slash).
- `FileName` equals the filename without extension.
- Writing a file and then enumerating yields the updated content.
- Non-`.json` files are not yielded.
- Subdirectory `.json` files ARE yielded (recursive).

---

### TKB-004 — Implement `ZipTkbProvider`

**Phase:** 2  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Target namespace:** `Fdp.Toolkit.Tkb.Vfs`

**Description:**  
ZIP-backed storage strategy for runtime ingestion from pre-staged archives. **Strictly
read-only at runtime** — `WriteEntityFile` and `DeleteEntityFile` throw
`NotSupportedException`. ZIP archives are created by a CI/CD build step; they are never
written through the VFS interface.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/ZipTkbProvider.cs`

**Specification:**

```csharp
public sealed class ZipTkbProvider : ITkbStorageStrategy
{
    private readonly ZipArchive _archive;

    public ZipTkbProvider(string archivePath)
    {
        _archive = ZipFile.Open(archivePath, ZipArchiveMode.Read);
    }

    public IEnumerable<TkbEntityFile> EnumerateEntityFiles() { ... }

    public void WriteEntityFile(string relativeFilePath, string jsonContent)
        => throw new NotSupportedException(
            "ZipTkbProvider is read-only. Use RawDirectoryTkbProvider for authoring.");

    public void DeleteEntityFile(string relativeFilePath)
        => throw new NotSupportedException(
            "ZipTkbProvider is read-only. Use RawDirectoryTkbProvider for authoring.");

    public void Dispose() => _archive.Dispose();
}
```

Read path (`EnumerateEntityFiles`):
- Iterate `_archive.Entries`.
- Skip entries where `FullName` ends with `/` (directory markers) or does not end with `.json`.
- For each valid entry: derive `CategoryPath` from the directory portion of `FullName` (replace
  backslashes with forward slashes, strip trailing slash).
- `FileName` = entry name without extension.
- Yield a `TkbEntityFile` with `entry.Open()` as the stream.
- Consumer MUST read/dispose the stream before advancing.

**Success conditions:**
- `EnumerateEntityFiles()` on a ZIP produced from a known directory yields the same logical
  entities as `RawDirectoryTkbProvider` on the same directory.
- `CategoryPath` is derived correctly (forward slashes, no trailing slash).
- `FileName` equals the filename without extension.
- `WriteEntityFile(...)` throws `NotSupportedException` with a message indicating the provider
  is read-only.
- `DeleteEntityFile(...)` throws `NotSupportedException`.
- `ZipTkbProvider` is always opened with `ZipArchiveMode.Read`; no `ZipArchiveMode.Update`
  appears anywhere in the implementation.

---

### TKB-005 — Implement `TkbUnifiedLoader`

**Phase:** 2  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Target namespace:** `Fdp.Toolkit.Tkb.Vfs`

**Description:**  
Factory facade that auto-detects `.zip` vs directory and constructs the appropriate
`ITkbStorageStrategy`.

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/TkbUnifiedLoader.cs`

**Specification:**

```csharp
public sealed class TkbUnifiedLoader : IDisposable
{
    private readonly ITkbStorageStrategy _strategy;

    public TkbUnifiedLoader(string sourcePath)
    {
        if (File.Exists(sourcePath) &&
            sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            _strategy = new ZipTkbProvider(sourcePath);
        else if (Directory.Exists(sourcePath))
            _strategy = new RawDirectoryTkbProvider(sourcePath);
        else
            throw new ArgumentException($"TKB source path is not a .zip or directory: {sourcePath}");
    }

    public IEnumerable<TkbEntityFile> EnumerateEntityFiles()
        => _strategy.EnumerateEntityFiles();

    public void Dispose() => _strategy.Dispose();
}
```

**Success conditions:**
- Given a `.zip` path, `_strategy` is a `ZipTkbProvider`.
- Given a directory path, `_strategy` is a `RawDirectoryTkbProvider`.
- Given a nonexistent path or a path that is neither, throws `ArgumentException`.
- Disposing `TkbUnifiedLoader` disposes the underlying strategy.

---

## Phase 3: In-Memory Registry Refactoring

---

### TKB-006 — Refactor `TkbTemplate` to pure descriptor bag

**Phase:** 3  
**Target projects:** `FDP/Engine/Fdp.Core`, `FDP/Toolkits/Fdp.Toolkits`  
**Files to modify:**
- `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs` — primary change
- Any file that calls `template.ApplyTo(...)` — must be updated to use translators (Phase 6)

**Description:**  
Replace the `List<Action<EntityRepository, Entity, bool>> _applicators` field and the
`ApplyTo()` method with a descriptor bag keyed by `(Type, PartId)`. Add `CategoryPath` property.

**Specification:**

Add:
- `string CategoryPath { get; }` (constructor parameter, defaults to `""`)
- `void AddDescriptor<T>(T descriptor, int partId = 0) where T : notnull`
- `T? GetDescriptor<T>(int partId = 0) where T : class`
- `bool TryGetDescriptor<T>(int partId, out T descriptor) where T : struct`
- `bool HasDescriptor<T>(int partId = 0)`
- `IEnumerable<(Type Type, int PartId, object Data)> GetAllDescriptors()`

Remove:
- `List<Action<EntityRepository, Entity, bool>> _applicators`
- `void ApplyTo(EntityRepository repo, Entity entity, bool preserveExisting)`
- Any `AddApplicator(...)` method or delegate registration API

Retain:
- `long TkbType { get; }`
- `string Name { get; }`
- `DISEntityType DisType { get; set; }`
- `List<MandatoryComponent> MandatoryComponents { get; }`
- `List<ChildBlueprintDefinition> ChildBlueprints { get; }`
- Existing constructor overloads if other callers depend on them (check and preserve signatures)

**Descriptor bag implementation:**
```csharp
private readonly Dictionary<(Type, int), object> _descriptors = new();
```

**Success conditions:**
- Project compiles after removal of `_applicators` and `ApplyTo`.
- All existing callers of `ApplyTo()` have been identified (grep for `ApplyTo`) and updated or
  noted as requiring Phase 6 work (if GhostPromotionSystem is the only caller, it is updated
  in TKB-010).
- `AddDescriptor<T>` / `GetDescriptor<T>` round-trip test passes (add a `VehicleParametersDto`,
  retrieve it, check fields match).
- `CategoryPath` is accessible and defaults to `""` when not supplied.

---

### TKB-007 — Extend `ITkbDatabase` with `Clear()` and `GetEntitiesByCategory()`

**Phase:** 3  
**Target projects:** `FDP/Engine/Fdp.Core`  
**Files to modify:**
- `FDP/Engine/Fdp.Core/Abstractions/ITkbDatabase.cs`

**Description:**  
Add the two methods required by the file-driven pipeline. `Clear()` is required by the
differential reload in `TkbLoadClusterStateHandler`. `GetEntitiesByCategory()` is required
by editor tree-building and diagnostic tools.

**Specification:**
```csharp
// Add to ITkbDatabase:

/// <summary>
/// Removes all registered templates. Called by TkbLoadClusterStateHandler
/// before re-ingesting a TKB when a cache miss is detected.
/// </summary>
void Clear();

/// <summary>
/// Enumerates all templates whose CategoryPath exactly equals or is a child of
/// the given prefix (directory boundary semantics). An empty prefix returns all.
/// </summary>
IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath);

/// <summary>
/// The name of the TKB most recently loaded by TkbLoadClusterStateHandler.
/// Null when using the hardcoded fallback (NedTkbCatalog).
/// Set upon successful VFS ingestion; read by the save pipeline to stamp
/// TkbName into every saved ScenarioHeaderDto.
/// </summary>
string? ActiveTkbName { get; set; }
```

**Success conditions:**
- `ITkbDatabase` interface compiles with the three new members.
- All existing implementations of `ITkbDatabase` compile (there may be test doubles or
  alternative implementations — check and add stubs).

---

### TKB-008 — Update `TkbDatabase` to implement `Clear()` and `GetEntitiesByCategory()`

**Phase:** 3  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs`

**Description:**  
Implement the two new `ITkbDatabase` methods in the concrete `TkbDatabase` class.

**Specification:**

```csharp
// In TkbDatabase:

public void Clear()
{
    _byName.Clear();
    _byType.Clear();
}

public string? ActiveTkbName { get; set; }

public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath)
{
    if (string.IsNullOrEmpty(categoryPath))
        return _byType.Values;
    return _byType.Values.Where(t =>
        t.CategoryPath.Equals(categoryPath, StringComparison.OrdinalIgnoreCase) ||
        t.CategoryPath.StartsWith(categoryPath + "/", StringComparison.OrdinalIgnoreCase));
}
```

Note: `_byName` uses `StringComparer.OrdinalIgnoreCase` (already the case in existing code).

**Success conditions:**
- After `Register` then `Clear`, `GetAll()` returns empty.
- After clearing and re-registering, `GetByType` finds the re-registered templates.
- `GetEntitiesByCategory("Platform/Vehicle")` returns templates with `CategoryPath ==
  "Platform/Vehicle"` and `CategoryPath == "Platform/Vehicle/MBT"` but NOT templates with
  `CategoryPath == "Platform/Vehicle_Heavy"` or `"Platform/Vehicle_Heavy/MBT"`.
- Empty string prefix returns all entities.
- `ActiveTkbName` is readable/writable (`null` by default).

---

## Phase 4: Streaming Deserialization Pipeline

---

### TKB-009 — Implement `TkbDeserializer` and `TkbFormatException`

**Phase:** 4  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Target namespace:** `Fdp.Toolkit.Tkb`

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDeserializer.cs`
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbFormatException.cs`

**Description:**  
Process a stream from a `TkbEntityFile`, build a `TkbTemplate`, dispatch each root JSON
property to the registered parser thunk, and register the template in `ITkbDatabase`.
Zero-allocation on the hot path for descriptor key lookup.

**Specification:**

```csharp
public sealed class TkbDeserializer
{
    // AlternateLookup is obtained once at construction, not per-call.
    private readonly Dictionary<string, TkbDescriptorParserThunk>
        .AlternateLookup<ReadOnlySpan<char>> _parsers
        = TkbDescriptorRegistry.GetAlternateLookup();

    public void ParseAndRegister(TkbEntityFile file, ITkbDatabase db)
    {
        using var doc = JsonDocument.Parse(file.JsonStream);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$guid", out var guidProp))
            throw new TkbFormatException(
                $"Entity '{file.FileName}' in '{file.CategoryPath}' is missing $guid.");
        long tkbId = guidProp.GetInt64();

        var template = new TkbTemplate(file.FileName, tkbId, file.CategoryPath);

        foreach (var prop in root.EnumerateObject())
        {
            ReadOnlySpan<char> name = prop.Name;

            // Skip reserved metadata (key starts with non-letter) and $guid.
            if (name.IsEmpty || !char.IsLetter(name[0])) continue;

            // Zero-alloc split: "Gen.AmmoWeaponBallistics#2" -> key + partId.
            int hashIdx = name.IndexOf('#');
            ReadOnlySpan<char> key    = hashIdx < 0 ? name : name[..hashIdx];
            int                partId = hashIdx < 0 ? 0    : int.Parse(name[(hashIdx + 1)..]);

            if (_parsers.TryGetValue(key, out var thunk))
                thunk(template, partId, prop.Value);
            // Unknown descriptor: skip silently (zero allocation).
        }

        db.Register(template);
    }
}

public sealed class TkbFormatException : Exception
{
    public TkbFormatException(string message) : base(message) { }
    public TkbFormatException(string message, Exception inner) : base(message, inner) { }
}
```

**Memory guarantees:**
- One `JsonDocument` is alive per entity file. Disposed (via `using`) before the next file.
- No `string.Substring` on the hot path — `ReadOnlySpan<char>` slicing used for `#PartId` split.
- Unknown descriptors: `EnumerateObject()` walks pointers only; does not parse the JSON sub-tree.

**Success conditions:**
- Parsing `M1_Abrams.json` produces a `TkbTemplate` with `TkbType = 100`,
  `Name = "M1 Abrams"` (or `"M1_Abrams"` from `FileName`), `CategoryPath` as supplied.
- `GetDescriptor<TkbMasterDto>()` returns the parsed master with correct `DisType`.
- `GetDescriptor<VehicleParametersDto>()` returns `Mass = 61000.0f`.
- Parsing `120mm_APFSDS.json` registers two `AmmoWeaponBallisticsDto` at `partId = 1` and
  `partId = 2`.
- Entity file missing `$guid` throws `TkbFormatException`.
- Entity with unknown descriptors (e.g., `CGFX.ABSTRACT_ENTITY` on a SimHost node) parses
  successfully; unknown block is silently skipped.
- `_EditorMetadata` (starts with `_`) is silently skipped.
- A memory-profiling unit test ingests 10,000 TKB entities in a tight loop and asserts that
  zero bytes are allocated on the Large Object Heap (threshold: >= 85,000 bytes). The test
  also verifies that `Dictionary<K,V>.AlternateLookup` with `ReadOnlySpan<char>` is used
  for descriptor key resolution (no `string.Substring` on the hot path).

---

## Phase 5: Tkb.SourceGen

---

### TKB-010 — Implement `TkbDescriptorRegistry`

**Phase:** 5  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits`  
**Target namespace:** `Fdp.Toolkit.Tkb`

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDescriptorRegistry.cs`

**Description:**  
Static registry mapping `HierarchicalName` strings to `TkbDescriptorParserThunk` delegates.
Populated by source-generated `[ModuleInitializer]` code in each consuming assembly. Exposes
`AlternateLookup<ReadOnlySpan<char>>` for zero-alloc hot-path queries in `TkbDeserializer`.

**Specification:**

```csharp
public delegate void TkbDescriptorParserThunk(
    TkbTemplate entity, int partId, System.Text.Json.JsonElement jsonElement);

public static class TkbDescriptorRegistry
{
    private static readonly Dictionary<string, TkbDescriptorParserThunk> _parsers
        = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterParser(
        string hierarchicalName, TkbDescriptorParserThunk parser)
        => _parsers[hierarchicalName] = parser;

    public static Dictionary<string, TkbDescriptorParserThunk>
        .AlternateLookup<ReadOnlySpan<char>> GetAlternateLookup()
        => _parsers.GetAlternateLookup<ReadOnlySpan<char>>();
}
```

**Note on `AlternateLookup`:** Requires .NET 9+ or .NET 8 with `Dictionary<K,V>` that has the
`GetAlternateLookup` extension. If the target framework is .NET 8, verify availability; if .NET 7
or lower, fall back to `_parsers.TryGetValue(key.ToString(), out var thunk)` in the deserializer
and remove the `AlternateLookup` approach. Document the framework constraint in a comment.

**Success conditions:**
- `RegisterParser("Gen.VehicleParameters", thunk)` stores the thunk.
- `GetAlternateLookup().TryGetValue("Gen.VehicleParameters".AsSpan(), out var t)` retrieves it.
- Registration is case-insensitive (OrdinalIgnoreCase).
- Last registration wins (duplicate `HierarchicalName` overwrites — log a warning if needed).

---

### TKB-011 — Create `Tkb.SourceGen` project and `TkbDescriptorGenerator`

**Phase:** 5  
**Target projects:** NEW — `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/`

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/Tkb.SourceGen.csproj`
- `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/TkbDescriptorGenerator.cs`

**Description:**  
Roslyn `IIncrementalGenerator` that scans consuming assemblies for types decorated with
`[TkbDescriptor]` and emits a `[ModuleInitializer]` method that populates
`TkbDescriptorRegistry`.

**Project file** (mirrors `Fbt.SourceGen.csproj` exactly):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <NoWarn>CS8632</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Generator logic:**

1. **Syntax predicate:** filter `TypeDeclarationSyntax` nodes with non-empty `AttributeLists`.
2. **Semantic filter:** resolve type symbol; check whether any attribute has fully qualified
   name `"Fdp.Toolkit.Tkb.Attributes.TkbDescriptorAttribute"` (by string, no project reference).
3. **Extract:** `HierarchicalName` from the first constructor argument of the attribute.
   Fully qualified type name of the decorated class/struct.
4. **Emit per assembly:** one file `__TkbDescriptors_{AssemblyName}.g.cs` containing:
   - An `internal static class __TkbDescriptors_{AssemblyName}` (class name sanitized).
   - One `[System.Runtime.CompilerServices.ModuleInitializer] internal static void Register()`
     method.
   - One `TkbDescriptorRegistry.RegisterParser(...)` call per discovered type.
5. **Duplicate detection:** if two types in the same assembly share the same `HierarchicalName`,
   emit a `Diagnostic` at `DiagnosticSeverity.Warning` (not Error, to not block builds).

**Thunk body emitted for each type:**
```csharp
TkbDescriptorRegistry.RegisterParser(
    "{HierarchicalName}",
    static (template, partId, jsonElement) =>
    {
        var dto = jsonElement.Deserialize<{FullyQualifiedTypeName}>(
            Fdp.Toolkit.Json.FdpJsonOptionsRegistry.DefaultRelaxed)!;
        template.AddDescriptor(dto, partId);
    });
```

**Consuming projects** add the generator as:
```xml
<ProjectReference Include="..\Fdp.Toolkit.Tkb.SourceGen\Tkb.SourceGen.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

**Success conditions:**
- Generator project compiles targeting `netstandard2.0`.
- Adding the generator reference to a project containing `VehicleParametersDto` causes a
  `__TkbDescriptors_{AssemblyName}.g.cs` file to be emitted in the build output.
- The emitted file compiles without errors.
- `TkbDescriptorRegistry.GetAlternateLookup().TryGetValue("Gen.VehicleParameters".AsSpan(), ...)`
  succeeds at runtime after the consuming assembly's `ModuleInitializer` runs.
- A type with a duplicate `HierarchicalName` in the same assembly produces a build warning,
  not an error.

---

## Phase 6: ECS Projection & Translators

---

### TKB-012 — Define `ITkbEntityTranslator` interface

**Phase:** 6  
**Target projects:** `FDP/Engine/Fdp.Core`  
**Target namespace:** `Fdp.Core` (or `Fdp.Toolkit.Tkb` — decide based on existing translator
   placement; match where `IEntityScenarioTranslator` lives)

**Files to create:**
- `FDP/Engine/Fdp.Core/Abstractions/ITkbEntityTranslator.cs`
  *(or `FDP/Toolkits/Fdp.Toolkits/Tkb/ITkbEntityTranslator.cs` if the Toolkits layer is more appropriate)*

**Description:**  
N:M translator interface for projecting TKB descriptor DTOs onto ECS entities. Mirrors the
`IEntityScenarioTranslator` pattern established by the scenario serialization stack.

**Specification:**

```csharp
/// <summary>
/// Projects N TKB descriptor DTOs into M ECS components on a live entity.
/// Mirrors IEntityScenarioTranslator for scenario content; same N:M mechanics.
/// </summary>
public interface ITkbEntityTranslator
{
    /// <summary>
    /// Returns the CLR types of TKB descriptor DTOs this translator consumes.
    /// The pipeline uses this to track which descriptors have been projected.
    /// </summary>
    IEnumerable<Type> GetConsumedDescriptors();

    /// <summary>
    /// Projects data from the TKB template into ECS components.
    /// Implementations MUST call repo.IsComponentTypeRegistered<T>() before
    /// every repo.AddComponent<T>() call.
    /// </summary>
    void Inject(EntityRepository repo, Entity entity, TkbTemplate template);
}
```

**Success conditions:**
- Interface compiles with no external dependencies beyond `EntityRepository`, `Entity`, and
  `TkbTemplate` (all available in `Fdp.Core`).
- `System.Collections.Generic.IEnumerable<System.Type>` used for `GetConsumedDescriptors`.

---

### TKB-013 — Implement `VehicleKinematicsTkbTranslator`

**Phase:** 6  
**Target projects:** CarKinem assembly, or `Hrot.Core` if CarKinem types are accessible there.  
**Files to create:**
- `(CarKinem or Hrot.Core)/Tkb/VehicleKinematicsTkbTranslator.cs`

**Description:**  
Reference implementation of `ITkbEntityTranslator` showing 1:4 (N:M) descriptor-to-ECS
projection. Demonstrates the `IsComponentTypeRegistered<T>()` guard pattern that all
future translators must follow.

**Specification:**

```csharp
public sealed class VehicleKinematicsTkbTranslator : ITkbEntityTranslator
{
    public IEnumerable<Type> GetConsumedDescriptors()
    {
        yield return typeof(VehicleParametersDto);
    }

    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
    {
        var dto = template.GetDescriptor<VehicleParametersDto>();
        if (dto == null) return;

        if (repo.IsComponentTypeRegistered<VehicleParams>())
            repo.AddComponent(entity, new VehicleParams
            {
                Length = dto.Length,
                Width = dto.Width,
                MaxSpeedFwd = dto.MaxSpeedFwd,
                MaxAccel = dto.MaxAccel,
                WheelBase = dto.Length * 0.6f
            });

        if (repo.IsComponentTypeRegistered<VehicleState>())
            repo.AddComponent(entity, new VehicleState { Speed = 0, SteerAngle = 0 });

        if (repo.IsComponentTypeRegistered<NavState>())
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });

        if (repo.IsComponentTypeRegistered<PhysicsCollider>())
            repo.AddComponent(entity, new PhysicsCollider
            {
                Radius = Math.Max(dto.Length, dto.Width) / 2f,
                CollisionLayer = 1
            });
    }
}
```

**Success conditions:**
- Compiles against the real `VehicleParams`, `VehicleState`, `NavState`, `PhysicsCollider`
  ECS component types in the target assembly.
- All four `AddComponent` calls are guarded by `IsComponentTypeRegistered<T>()`.
- If `VehicleParametersDto` is absent from the template, `Inject` returns without error.
- On a node that lacks `VehicleParams` registration, `Inject` silently skips that component.

---

### TKB-014 — Migrate `ApplyTo` callsites in `GhostPromotionSystem`, `NetworkSpawningSystem`, and `BlueprintApplicationSystem`

**Phase:** 6  
**Target projects:** `FDP/Toolkits/Fdp.Toolkits` (or wherever these systems live)  
**Files to modify:**
- `GhostPromotionSystem.cs`
- `NetworkSpawningSystem.cs`
- `BlueprintApplicationSystem.cs`

**Description:**  
All three systems call `template.ApplyTo(...)`. All three must be migrated in one batch.
Once TKB-006 removes `ApplyTo()` from `TkbTemplate`, the build must not compile if any
caller remains. **`ApplyTo` must be deleted, not deprecated.**

**Migration pattern for each system:**

1. Add `IReadOnlyList<ITkbEntityTranslator> translators` constructor parameter.
2. At the entity-initialization point (after readiness check in `GhostPromotionSystem`, at
   spawning time in the others), replace the `template.ApplyTo(...)` call with:
   ```csharp
   foreach (var translator in _translators)
       translator.Inject(repo, entity, template);
   ```
3. Update all construction sites for each system to supply the translator list.

The translator list is the same `IReadOnlyList<ITkbEntityTranslator>` instance for all three
systems within a given node (composed at the bootstrapper, see TKB-022).

**Success conditions:**
- All three systems compile without any reference to `TkbTemplate.ApplyTo()`.
- The build fails if `TkbTemplate.ApplyTo()` is referenced anywhere in the solution after
  TKB-006 removes it (verified by ensuring the method is truly deleted, not just hidden).
- Existing unit tests for `GhostPromotionSystem` pass (or are updated to supply translator mocks).
- A promoted ghost entity gets `VehicleParams` set if the template has `VehicleParametersDto`
  and the world has the component type registered.

---

### TKB-015 — Register `ITkbDatabase` as ECS world singleton

**Phase:** 6  
**Target projects:** `Hrot.SimHost`, `Hrot.IG`, `Hrot.CGF` (whichever runs `GhostPromotionSystem`)  
**Files to modify:**
- `Hrot/Subsystems/Hrot.SimHost/` — `RegisterDomainComponents` call site
- `Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs` — already done; verify, do not duplicate
- `Hrot/Subsystems/Hrot.CGF/` — if applicable

**Description:**  
`GhostPromotionSystem` must be able to resolve `TkbType -> TkbTemplate` via the ECS world
singleton. Register `ITkbDatabase` via `world.SetSingletonManaged<ITkbDatabase>(tkb)`. Note
that `TkbDatabase` must also implement the new `ActiveTkbName { get; set; }` property added
in TKB-007/TKB-008 before this task is considered fully complete.

**Verified pattern:**  
`IgNodeBootstrapper.RegisterDomainComponents()` already calls:
```csharp
world.SetSingletonManaged<ITkbDatabase>(tkb);
```
The SimHost bootstrapper must do the same. Check CGF. Do NOT double-register in IG.

**Success conditions:**
- SimHost: `world.GetSingletonManaged<ITkbDatabase>()` returns the `TkbDatabase` instance
  after `RegisterDomainComponents` runs.
- IG: existing call is preserved; no duplicate registration.
- Code compiles and no `InvalidOperationException` is thrown at singleton resolution.

---

## Phase 7: Node-Side Integration

---

### TKB-016 — Extend `ScenarioHeaderDto` with `TkbName`

**Phase:** 7  
**Target projects:** `Hrot.Core` (or wherever `ScenarioHeaderDto` lives)  
**Files to modify:**
- `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs`

**Description:**  
Add a nullable `TkbName` property. Null means "no opinion" — the node uses the fallback
catalog (`NedTkbCatalog`). Non-null values must agree across all staged scenario files
(consensus checked by orchestrator, TKB-019).

**Specification:**
```csharp
public sealed class ScenarioHeaderDto
{
    public string? SubsystemType { get; set; }
    public string? SchemaVersion { get; set; }
    public string? TkbName { get; set; }    // NEW — null = no opinion
}
```

**Success conditions:**
- `ScenarioHeaderDto` deserializes from JSON with `"TkbName": "Sample_v1"` and stores the value.
- `ScenarioHeaderDto` deserializes from JSON without a `TkbName` property; value is null.
- No existing deserialization tests break.
- A scenario save integration test verifies that `ScenarioFileService` reads
  `_tkbDb.ActiveTkbName` and writes it into `ScenarioHeaderDto.TkbName` when persisting a
  scenario to disk. A scenario saved while `ActiveTkbName == "Sample_v1"` must contain
  `"TkbName": "Sample_v1"` in the header section of the output JSON.
  (The `ScenarioFileService` wiring is implemented in TKB-021.)

---

### TKB-018 — Implement orchestrator TkbName consensus check (sanity gate)

**Phase:** 8  
**Target projects:** Orchestrator subsystem (`Hrot.Orchestration` or `Hrot.Core.Orchestration`)

**Files to modify:**
- The orchestrator component that reads staged scenario files and builds the cluster transition
  payload (same component that currently processes `ScenarioHeaderDto`).

**Description:**  
When the orchestrator stages scenario files for a `PrepareLive` / `PrepareEdit` transition, it
acts as a **read-only sanity gate** for `TkbName` consistency. It does NOT distribute `TkbName`
to nodes over the wire. Each node reads its own TKB requirement from its local scenario file
(see TKB-019).

**Algorithm:**
1. For each staged scenario file, open a `Utf8JsonReader` (forward-only, no `JsonDocument`).
2. Read until the `"TkbName"` property is found in the header section; extract string value; stop.
3. Collect all non-null, non-empty `TkbName` values.
4. **Consensus rule:** all non-empty values must be equal. If any two differ, abort the
   transition immediately with a descriptive error message naming both conflicting values and
   their source files. No split-brain initialization is permitted.
5. If consensus passes (or all values are null/empty): continue. Do nothing further with
   `TkbName`. Do NOT embed it into `NodeTransitionPayloadDto` or any wire structure.

**Success conditions:**
- Two staged files with the same `TkbName` -> transition proceeds normally.
- Two staged files with different `TkbName` -> transition aborted; log message names both
  conflicting values and their source file paths.
- Staged files without `TkbName` -> treated as null (no opinion); transition proceeds if
  remaining files agree or all are null.
- No `JsonDocument` created; no DOM nodes allocated per file inspection.
- **The orchestrator does NOT add `TkbName` to `NodeTransitionPayloadDto` or
  `EditLoadHandlerPayload` on consensus success.**

---

### TKB-019 — Implement `TkbLoadClusterStateHandler`

**Phase:** 7  
**Target projects:** `Hrot.SimHost`  
**Target namespace:** `Hrot.SimHost.Orchestration.Handlers`

**Files to create:**
- `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs`

**Description:**  
Cluster state handler that intercepts `PrepareLive` and `PrepareEdit`. Checks the differential
cache (TkbName + file timestamp). On cache miss, clears and re-ingests the TKB. On fallback
(no TkbName), uses `NedTkbCatalog`.

**Full specification:** see `DESIGN.md` Phase 7 §7.2.

Key constraints:
- `CanHandle()` returns true for `NodeOpType.PrepareLive` and `NodeOpType.PrepareEdit`.
- `PrepareAsync()` is synchronous in behavior (file I/O is blocking; returns `Task.FromResult`).
- `Commit()` and `Abort()` are no-ops. TKB is not rolled back on abort.
- `_lastLoadedTkbName` and `_lastLoadedTimestamp` are instance state (no static fields).
- TkbName is read from the **node's own locally staged scenario file** using a forward-only
  `Utf8JsonReader` (method `PeekTkbNameFromLocalScenario`), NOT from the intent payload.
- After successful load, `_tkbDb.ActiveTkbName` is set to the loaded TKB name.
- If file is not found, throws `FileNotFoundException` with a clear message.

**Success conditions:**
- Cache hit: calling `PrepareAsync` twice with the same TkbName and same ZIP timestamp does NOT
  call `_tkbDb.Clear()` or re-ingest on the second call.
- Cache miss (name change): `_tkbDb.Clear()` is called and TKB is re-ingested.
- Cache miss (timestamp change): `_tkbDb.Clear()` is called and TKB is re-ingested.
- After a successful load, `_tkbDb.ActiveTkbName` equals the loaded TKB name.
- Fallback (null TkbName from local scenario): if `ITkbDatabase.GetAll()` is empty,
  `NedTkbCatalog.RegisterAll()` is called; if already populated, it is NOT called again.
- Missing ZIP: `FileNotFoundException` thrown with path in message.
- `PeekTkbNameFromLocalScenario` uses a forward-only `Utf8JsonReader`; no `JsonDocument`.

---

### TKB-020 — Wire `TkbLoadClusterStateHandler` in `NodeBootstrapper.BuildOrchestration()`

**Phase:** 7  
**Target projects:** `Hrot.SimHost`  
**Files to modify:**
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

**Description:**  
Register `TkbLoadClusterStateHandler` on the `ClusterSlave` **before** `HrotScenarioLoadHandler`.
Thread the `ITkbDatabase` instance through `BuildOrchestration()` parameters. Also thread the
`IReadOnlyList<ITkbEntityTranslator>` translator list, which is built at the composition root
(see TKB-022) and must be passed to `GhostPromotionSystem`, `NetworkSpawningSystem`, and
`BlueprintApplicationSystem`.

**Changes to `BuildOrchestration()`:**
1. Add `ITkbDatabase? tkbDb` parameter (nullable for backward compat if used before TKB exists).
2. Register `new TkbLoadClusterStateHandler(tkbDb, localTempRoot)` as the FIRST handler
   (or at least before the scenario handler block).
3. Verify that `HrotScenarioLoadHandler` is registered in the same block, after TKB handler.

**Updated registration order (simplified):**
1. `ReferenceArchiveHandler` (first — existing)
2. `ReferenceCheckpointHandler` (conditional — existing)
3. `ReferencePreviewHandler` (existing)
4. `ReferencePrefetchHandler` (existing)
5. `ReferenceReplayLoadHandler` (conditional — existing)
6. **`TkbLoadClusterStateHandler`** (NEW — registered here)
7. `HrotScenarioLoadHandler` (existing — must be AFTER TKB)
8. `HrotEditLoadHandler` (existing)
9. `ReferenceEpisodeLoadHandler` (existing)
10. `ReferenceLiveLoadHandler` (existing)
11. `DiagnosticsDumpClusterOpHandler` (conditional — existing)

**Success conditions:**
- `clusterSlave.IsHandlerRegistered<TkbLoadClusterStateHandler>()` returns true.
- `clusterSlave.IsHandlerRegistered<HrotScenarioLoadHandler>()` still returns true.
- Handler registration order places TKB before scenario (check by index in internal list or via
  integration test that fires `PrepareLive` and verifies TKB is populated before scenario loads).
- `BuildOrchestration()` compiles with the new parameters (`tkbDb` and `translatorList`).
- All three migration target systems (`GhostPromotionSystem`, `NetworkSpawningSystem`,
  `BlueprintApplicationSystem`) receive the same `IReadOnlyList<ITkbEntityTranslator>` instance.

---

### TKB-021 — Wire active TkbName into scenario save pipeline

**Phase:** 8  
**Target projects:** Scenario serialization assembly (wherever `ScenarioFileService` or the
equivalent scenario save entry point lives)  
**Files to modify:**
- `ScenarioFileService.cs` (or equivalent entry point that invokes `ScenarioSerializer.Serialize`)
- Wherever the `ScenarioHeader` / `ScenarioHeaderDto` record is constructed before serialization

**Description:**  
When a scenario is saved, `ScenarioFileService` must stamp the currently loaded TKB name into
`ScenarioHeaderDto.TkbName`. This ensures saved scenarios are self-describing with respect to
their TKB requirement, and enables the orchestrator's consensus check (TKB-018) to work on
subsequent loads.

**Specification:**  
`ScenarioFileService` must hold a reference to `ITkbDatabase`. When constructing the
`ScenarioHeader` for serialization, read `_tkbDb.ActiveTkbName` and set it on the header:

```csharp
var header = new ScenarioHeader(
    SubsystemType: _subsystemType,
    SchemaVersion: CurrentSchemaVersion,
    TkbName: _tkbDb.ActiveTkbName);  // from ITkbDatabase singleton
scenarioSerializer.Serialize(scenarioData, header, ...);
```

**Success conditions:**
- A scenario saved while `ITkbDatabase.ActiveTkbName == "Sample_v1"` produces output JSON
  containing `"TkbName": "Sample_v1"` in the `ScenarioHeaderDto` section.
- A scenario saved while `ActiveTkbName == null` (fallback catalog in use) produces output
  JSON with `TkbName` absent or null.
- Integration test: inject `ActiveTkbName = "Sample_v1"` into the database, save scenario,
  reload scenario header, assert `ScenarioHeaderDto.TkbName == "Sample_v1"`.
- `ScenarioFileService` does not hard-code any TKB name; it reads exclusively from
  `ITkbDatabase.ActiveTkbName`.

---

### TKB-022 — Instantiate and wire `ITkbEntityTranslator` list in composition root

**Phase:** 6/8 (bootstrapper wiring)  
**Target projects:** `Hrot.SimHost`, `Hrot.IG`, `Hrot.CGF` (any bootstrapper that constructs
`GhostPromotionSystem`, `NetworkSpawningSystem`, or `BlueprintApplicationSystem`)  
**Files to modify:**
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` (or `HrotNodeBuilder.cs`)
- Equivalent composition root files for IG and CGF nodes

**Description:**  
The composition root must instantiate all `ITkbEntityTranslator` implementations applicable to
the node's role and pass them as a single `IReadOnlyList<ITkbEntityTranslator>` to every system
that previously called `ApplyTo`. The list is created once per node startup and shared across
all three systems.

**Pattern:**

```csharp
// In the composition root (per node role):
var translators = new List<ITkbEntityTranslator>
{
    new VehicleKinematicsTkbTranslator(),
    // add role-specific translators here
};
var translatorList = translators.AsReadOnly();

var ghostPromotion      = new GhostPromotionSystem(world, tkbDb, translatorList);
var networkSpawning     = new NetworkSpawningSystem(..., translatorList);
var blueprintApplicator = new BlueprintApplicationSystem(..., translatorList);
```

**Success conditions:**
- All three systems receive the same `IReadOnlyList<ITkbEntityTranslator>` instance (not
  separately constructed lists).
- SimHost node includes physics/kinematics translators (e.g., `VehicleKinematicsTkbTranslator`).
- IG node includes rendering translators (if applicable).
- No system creates its own translator instances internally.
- `BuildOrchestration()` (or equivalent) receives the translator list as a parameter and
  passes it through to the systems (coordinated with TKB-020).

| ID | Title | Phase | Files |
|---|---|---|---|
| TKB-001 | `[TkbDescriptor]` and field attributes | 1 | 4 new |
| TKB-002 | Concrete DTOs | 1 | 4 new |
| TKB-003 | `TkbEntityFile`, `ITkbStorageStrategy`, `RawDirectoryTkbProvider` | 2 | 3 new |
| TKB-004 | `ZipTkbProvider` (read-only) | 2 | 1 new |
| TKB-005 | `TkbUnifiedLoader` | 2 | 1 new |
| TKB-006 | Refactor `TkbTemplate` to descriptor bag | 3 | 1 modify |
| TKB-007 | Extend `ITkbDatabase` | 3 | 1 modify |
| TKB-008 | Update `TkbDatabase` implementation | 3 | 1 modify |
| TKB-009 | `TkbDeserializer` and `TkbFormatException` | 4 | 2 new |
| TKB-010 | `TkbDescriptorRegistry` | 5 | 1 new |
| TKB-011 | `Tkb.SourceGen` project and generator | 5 | 2 new |
| TKB-012 | `ITkbEntityTranslator` interface | 6 | 1 new |
| TKB-013 | `VehicleKinematicsTkbTranslator` | 6 | 1 new |
| TKB-014 | Migrate `ApplyTo` in all three ECS systems | 6 | 3 modify |
| TKB-015 | Register `ITkbDatabase` as ECS singleton | 6 | 1-2 modify |
| TKB-016 | Extend `ScenarioHeaderDto` | 8 | 1 modify |
| TKB-018 | Orchestrator TkbName consensus (sanity gate) | 8 | 1 modify |
| TKB-019 | `TkbLoadClusterStateHandler` | 7 | 1 new |
| TKB-020 | Wire handler in `NodeBootstrapper` | 8 | 1 modify |
| TKB-021 | Wire `ActiveTkbName` into save pipeline | 8 | 1-2 modify |
| TKB-022 | Translator aggregation in composition root | 6/8 | 2-3 modify |
