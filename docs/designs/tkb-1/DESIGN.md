<!--STATUS
state: LIVE
updated: 2026-08-30
current-answer: the whole document is the TKB design and is BUILT. §6.5b is the section added on
  2026-08-30 and is the one to read before composing a node's translator list — it states the
  consequence of §6.1's registration guard, which the rest of the document leaves implicit.
known-rot: §6.5's closing sentence ("an IG node would include BIG-specific translators; a SimHost
  node would not") reads as if per-node LIST curation were the intended narrowing lever. It is not;
  §6.5b corrects that reading. Do not quote that sentence without §6.5b.
-->
# DESIGN: Transient Knowledge Base (TKB) — File-Driven Blueprint Registry

**Workstream:** tkb-1  
**Status:** Draft (as authored) — ⭐ **BUILT**; see the STATUS block and §6.5b  
**Input documents:** `.dev/tkb-1/tkb-design-ideas.md`, `.dev/tkb-1/design-talk.md`

---

## 1. Overview

The **Transient Knowledge Base (TKB)** is the cluster-wide, engine-agnostic blueprint registry that
defines every entity type that can be instantiated in a simulation. The current codebase uses a
hardcoded `NedTkbCatalog.RegisterAll()` call at startup. This workstream replaces that with a
file-driven pipeline where:

- TKB is **authored as JSON files on disk** (one file per entity), version-controlled in Git.
- TKB is **staged to each node's local directory** by an out-of-band file sync mechanism (not the
  orchestrator state machine — this was an explicit architectural decision documented in the design
  talk).
- Each node **loads its own selective view** of the TKB via a memory-resident `ITkbDatabase`
  singleton — selectively because each engine registers only the descriptor types it knows about.
- TKB is **loaded before scenario content** during `PrepareLive` / `PrepareEdit` transitions.
- TKB **survives `Idle`** and is reloaded only when TkbName or file timestamp changes.

### 1.1 What This Workstream Does NOT Cover

- **TKB file distribution** — out-of-band file sync. Not the orchestrator's responsibility.
- **TKB Editor** — conceptual outline in Phase 9 only; not implemented this workstream.
- **Physics / AI domain logic** — only the infrastructure for descriptor-to-ECS projection.

---

## 2. Architectural Principles

The TKB enforces hard separation of concerns across five bounded contexts:

| Context | Responsibility |
|---|---|
| **Domain Schema** | Pure C# DTOs annotated with `[TkbDescriptor]` and field attributes |
| **Storage (Authoring)** | Raw JSON files, one per entity; or ZIP archive for runtime |
| **In-Memory Registry** | `TkbTemplate` + `ITkbDatabase`: O(1) lookup by `TkbType` or name |
| **Deserialization** | `TkbDeserializer` + `TkbDescriptorRegistry`: zero-reflection, source-generated |
| **ECS Projection** | `ITkbEntityTranslator` implementations: N descriptors -> M ECS components |

Key invariants:

1. **DTOs are pure POCOs.** No `[MessagePackObject]`, no ECS base classes, no transport markers.
2. **One JSON file = one entity = one `TkbTemplate`.** No merged DOM at runtime.
3. **Engines load selectively.** Each engine registers only descriptors it compiled against.
   Unknown descriptors are silently skipped during ingestion with zero allocation.
4. **ZIP is strictly read-only at runtime.** `ZipTkbProvider` opens archives with
   `ZipArchiveMode.Read` only. `WriteEntityFile` and `DeleteEntityFile` throw
   `NotSupportedException`. Packing a raw directory into a transport ZIP is an out-of-band
   CI/CD build step, never a runtime VFS operation.
5. **TKB is engine-agnostic.** `TkbTemplate` holds descriptor POCOs; ECS projection is a
   downstream concern performed by `ITkbEntityTranslator` implementations.

---

## 3. Phases

### Phase 1: Domain Schema & Attributes

**Goal:** Define the attribute and DTO vocabulary that all TKB descriptor POCOs use.

#### 1.1 `[TkbDescriptor]` Attribute

A pure semantic marker applied to a class or struct, binding it to a hierarchical descriptor name
that matches the JSON property name:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct,
                Inherited = false, AllowMultiple = false)]
public sealed class TkbDescriptorAttribute : Attribute
{
    public string HierarchicalName { get; }
    public TkbDescriptorAttribute(string hierarchicalName) { ... }
}
```

Naming rules:
- Every descriptor except `TkbMaster` **must** carry a domain prefix: `Gen.`, `CGFX.`, `BIG.`, etc.
- `HierarchicalName` is the user-perspective name as it appears in JSON. The C# class name is
  decoupled from it — refactoring the class does not break data binding.
- The `#PartId` postfix is **not** part of `HierarchicalName`; it is a runtime index.

#### 1.2 Field-Level Attributes

Relational markers for the TKB Editor's picker UI and cross-entity validation:

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class WeaponRefAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AmmoRefAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ModelRefAttribute : Attribute { }
```

`[EditRange]`, `[EditUnit]`, `[EditDisplayName]`, `[ReadOnly]` are reused from the existing
`StructEdit` / ConfigEditor stack.

#### 1.3 Concrete DTOs

All POCOs placed in `Fdp.Toolkit.Tkb.Domain` (or a HROT-layer namespace for HROT-specific
descriptors). None inherit from ECS base types. None carry `[MessagePackObject]`.

| DTO | Descriptor Name | Key Fields |
|---|---|---|
| `TkbMasterDto` | `"TkbMaster"` | `CustomName`, `DisType` |
| `VehicleParametersDto` | `"Gen.VehicleParameters"` | `Mass`, `Length`, `Width`, `MaxSpeedFwd`, `MaxSpeedRev`, `MaxAccel` |
| `WeaponCapabilitiesDto` | `"Gen.WeaponCapabilities"` | `EffectiveRange`, `RateOfFire`, `MagazineCapacity` |
| `AmmoWeaponBallisticsDto` | `"Gen.AmmoWeaponBallistics"` | `[WeaponRef] WeaponGuid`, `MuzzleSpeed`, `Damage` |

`TkbMasterDto` is the only descriptor without a domain prefix; it is mandatory on all entities.

**Sample JSON entity** (`Platform/Vehicle/Military/MBT/M1_Abrams.json`):
```json
{
  "$guid": 100,
  "TkbMaster": { "CustomName": "M1 Abrams", "DisType": "1.1.225.1.1.1.0" },
  "Gen.VehicleParameters": {
    "Mass": 61000.0, "Length": 7.93, "Width": 3.66,
    "MaxSpeedFwd": 20.0, "MaxSpeedRev": 12.0, "MaxAccel": 2.5
  },
  "_EditorMetadata": { "LastModifiedBy": "AuthoringTool" }
}
```

The `_EditorMetadata` block starts with `_` (non-letter), so the parser skips it with zero
allocation at runtime. Multi-instance descriptors use `#PartId` postfixes:
```json
{
  "$guid": 3001,
  "TkbMaster": { "CustomName": "120mm APFSDS", "DisType": "2.2.225.2.1.1.0" },
  "Gen.AmmoWeaponBallistics#1": { "WeaponGuid": 2001, "MuzzleSpeed": 1500.0, "Damage": 600.0 },
  "Gen.AmmoWeaponBallistics#2": { "WeaponGuid": 2005, "MuzzleSpeed": 1450.0, "Damage": 550.0 }
}
```

---

### Phase 2: VFS and Transport Tier

**Goal:** Provide an `ITkbStorageStrategy` abstraction that hides whether the TKB lives in a raw
directory or a ZIP archive.

#### 2.1 Core Types

```csharp
// One entity file yielded by the VFS. The JsonStream must be consumed (or disposed)
// before the enumerator is advanced — this bounds memory to one file at a time.
public readonly record struct TkbEntityFile(
    string CategoryPath,   // Forward-slash relative dir, e.g. "Platform/Vehicle/Military/MBT"
    string FileName,       // Name without extension, e.g. "Merkava Mk4"
    Stream JsonStream);    // Open stream positioned at start of JSON content

public interface ITkbStorageStrategy : IDisposable
{
    IEnumerable<TkbEntityFile> EnumerateEntityFiles();
    void WriteEntityFile(string relativeFilePath, string jsonContent);
    void DeleteEntityFile(string relativeFilePath);
}
```

#### 2.2 `RawDirectoryTkbProvider`

- Backed by a directory on disk.
- `EnumerateEntityFiles()`: recursively enumerates `*.json`; for each, computes `CategoryPath`
  from the relative directory path (forward slashes); opens a `FileStream`; yields; closes on
  next iteration.
- `WriteEntityFile`: creates missing intermediate directories; sparse write (only touched files
  change — Git-friendly diffs).
- Used for: authoring (TKB Editor writes here), debug/dev runs with raw folder layout.

#### 2.3 `ZipTkbProvider`

- Backed by a ZIP archive (`System.IO.Compression.ZipArchive`), opened with `ZipArchiveMode.Read`.
- **Strictly read-only.** `WriteEntityFile` and `DeleteEntityFile` throw `NotSupportedException`.
  Packing a raw directory into a transport ZIP is an explicit, out-of-band CI/CD build step; it
  is never performed at runtime through the VFS interface. This eliminates ZIP central-directory
  repack latency and the need for a write mutex entirely.
- Read path: iterate `_archive.Entries`; skip non-`.json` and directory entries; derive
  `CategoryPath` from the directory portion of `FullName` (replace backslashes with forward
  slashes, strip trailing slash); `FileName` = entry name without extension; yield decompression
  stream via `entry.Open()`.
- Used for: runtime ingestion from pre-staged ZIP in the node's local staging area.

#### 2.4 `TkbUnifiedLoader`

Thin factory that picks the right strategy from the source path:

```csharp
public sealed class TkbUnifiedLoader : IDisposable
{
    public TkbUnifiedLoader(string sourcePath)
    {
        if (File.Exists(sourcePath) &&
            sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            _strategy = new ZipTkbProvider(sourcePath, readOnly: true);
        else if (Directory.Exists(sourcePath))
            _strategy = new RawDirectoryTkbProvider(sourcePath);
        else
            throw new ArgumentException($"Invalid TKB source path: {sourcePath}");
    }

    public IEnumerable<TkbEntityFile> EnumerateEntityFiles()
        => _strategy.EnumerateEntityFiles();

    public void Dispose() => _strategy.Dispose();
}
```

Consumers code against `TkbUnifiedLoader` and are unaware of the underlying medium.

---

### Phase 3: In-Memory Registry Refactoring

**Goal:** Refactor `TkbTemplate` from a delegate-bag to a pure descriptor-bag. Extend
`ITkbDatabase` with the methods needed by the file-driven pipeline.

#### 3.1 `TkbTemplate` Refactoring

**Current state:** `TkbTemplate` has a `List<Action<EntityRepository, Entity, bool>> _applicators`
that was added by callers doing runtime component injection. This must be replaced.

**New contract:**

```csharp
public sealed class TkbTemplate
{
    public long TkbType { get; }
    public string Name { get; }
    public string CategoryPath { get; }           // NEW — relative folder path from VFS

    // NEW — replaces _applicators
    // Composite key (DescriptorClrType, PartId) -> boxed DTO instance
    private readonly Dictionary<(Type, int), object> _descriptors = new();

    public TkbTemplate(string name, long tkbType, string categoryPath = "") { ... }

    public void AddDescriptor<T>(T descriptor, int partId = 0) where T : notnull { ... }

    public T? GetDescriptor<T>(int partId = 0) where T : class { ... }

    public bool TryGetDescriptor<T>(int partId, out T descriptor) where T : struct { ... }

    public bool HasDescriptor<T>(int partId = 0) { ... }

    public IEnumerable<(Type Type, int PartId, object Data)> GetAllDescriptors() { ... }

    // Retained from existing contract — still used by GhostPromotionSystem readiness check:
    public List<MandatoryComponent> MandatoryComponents { get; } = new();
    public List<ChildBlueprintDefinition> ChildBlueprints { get; } = new();
    public DISEntityType DisType { get; set; }
}
```

The `_applicators` list and the `ApplyTo(EntityRepository, Entity, bool)` method are removed.
All callers of `ApplyTo` must migrate to `ITkbEntityTranslator.Inject()` (Phase 6).

#### 3.2 `ITkbDatabase` Extensions

Add to `FDP/Engine/Fdp.Core/Abstractions/ITkbDatabase.cs`:

```csharp
// Clear all registered templates (for differential reload).
void Clear();

// Enumerate templates under a category path prefix (for editor tree building).
// Enforces directory boundary semantics (see Phase 3.3).
IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath);

// The name of the TKB most recently loaded by TkbLoadClusterStateHandler.
// Null when using the hardcoded fallback (NedTkbCatalog).
// Set by the handler upon successful VFS ingestion; read by the save pipeline
// to stamp the active TkbName into every saved ScenarioHeaderDto.
string? ActiveTkbName { get; set; }
```

#### 3.3 `TkbDatabase` Implementation

- Add `Clear()`: clear both `_byName` and `_byType` dictionaries.
- Add `GetEntitiesByCategory(string)`: filter `_byType.Values` enforcing **directory boundary
  semantics** — a raw `StartsWith` is structurally incorrect for VFS paths. The query
  `"Platform/Vehicle"` must NOT match `"Platform/Vehicle_Heavy/MBT"`. The match condition is:
  ```csharp
  t.CategoryPath.Equals(categoryPath, OrdinalIgnoreCase)
  || t.CategoryPath.StartsWith(categoryPath + "/", OrdinalIgnoreCase)
  || string.IsNullOrEmpty(categoryPath)
  ```
  Consider an `ILookup<string, TkbTemplate>` if the editor needs frequent enumeration.
- Add `ActiveTkbName` property: auto-implemented `string?`, default null.

---

### Phase 4: Streaming Deserialization Pipeline

**Goal:** Implement `TkbDeserializer` that processes `TkbEntityFile` streams from the VFS and
registers `TkbTemplate` objects into `ITkbDatabase`. Strict memory guarantees.

#### 4.1 Memory Contract

- **One `JsonDocument` alive per file.** Created, used, and disposed before the next file is opened.
- **No LOH allocation for descriptor keys.** Use `ReadOnlySpan<char>` for `#PartId` splitting and
  `Dictionary.AlternateLookup<ReadOnlySpan<char>>` to query `TkbDescriptorRegistry`.
- **Unknown descriptors cost nothing.** `JsonDocument` does not parse sub-trees that are not
  accessed; skipping an unknown descriptor walks a pointer only.

#### 4.2 `TkbDeserializer`

```csharp
public sealed class TkbDeserializer
{
    // Obtains the AlternateLookup once at construction — Dictionary must use
    // StringComparer.OrdinalIgnoreCase to support Span-based keys.
    private readonly Dictionary<string, TkbDescriptorParserThunk>.AlternateLookup<ReadOnlySpan<char>>
        _parsers = TkbDescriptorRegistry.GetAlternateLookup();

    public void ParseAndRegister(TkbEntityFile file, ITkbDatabase db)
    {
        using var doc = JsonDocument.Parse(file.JsonStream);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$guid", out var guidProp))
            throw new TkbFormatException($"Entity '{file.FileName}' is missing $guid.");
        long tkbId = guidProp.GetInt64();

        var template = new TkbTemplate(file.FileName, tkbId, file.CategoryPath);

        foreach (var prop in root.EnumerateObject())
        {
            ReadOnlySpan<char> name = prop.Name;

            // Skip reserved metadata (non-letter first char) and the $guid field.
            if (name.IsEmpty || !char.IsLetter(name[0])) continue;

            // Zero-alloc split on '#': "Gen.AmmoWeaponBallistics#2" -> ("Gen.AmmoWeaponBallistics", 2)
            int hashIdx = name.IndexOf('#');
            ReadOnlySpan<char> key    = hashIdx < 0 ? name : name[..hashIdx];
            int                partId = hashIdx < 0 ? 0    : int.Parse(name[(hashIdx + 1)..]);

            if (_parsers.TryGetValue(key, out var thunk))
                thunk(template, partId, prop.Value);
            // else: engine does not know this descriptor — skip silently, zero allocation.
        }

        db.Register(template);
    }
}
```

---

### Phase 5: Tkb.SourceGen — Roslyn Source Generator

**Goal:** Eliminate runtime reflection from the deserialization hot path. A new Roslyn source
generator scans assemblies at compile time and emits `TkbDescriptorRegistry` population code
via `[ModuleInitializer]`.

#### 5.1 `TkbDescriptorRegistry`

```csharp
public delegate void TkbDescriptorParserThunk(
    TkbTemplate entity, int partId, System.Text.Json.JsonElement jsonElement);

public static class TkbDescriptorRegistry
{
    private static readonly Dictionary<string, TkbDescriptorParserThunk> _parsers
        = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterParser(string hierarchicalName, TkbDescriptorParserThunk parser)
        => _parsers[hierarchicalName] = parser;

    // Returns the AlternateLookup so TkbDeserializer can query by ReadOnlySpan<char>.
    public static Dictionary<string, TkbDescriptorParserThunk>.AlternateLookup<ReadOnlySpan<char>>
        GetAlternateLookup() => _parsers.GetAlternateLookup<ReadOnlySpan<char>>();
}
```

#### 5.2 Generator Project: `Tkb.SourceGen`

New project: `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/Tkb.SourceGen.csproj`  
Mirrors the `Fbt.SourceGen` / `Fhsm.SourceGen` pattern exactly:

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

Consumed by other projects as an analyzer reference:
```xml
<ProjectReference Include="..\..\Fdp.Toolkit.Tkb.SourceGen\Tkb.SourceGen.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

#### 5.3 Generator Logic: `TkbDescriptorGenerator`

`[Generator] public class TkbDescriptorGenerator : IIncrementalGenerator`

Pipeline:
1. Syntax provider predicate: `TypeDeclarationSyntax` nodes with `AttributeLists.Count > 0`.
2. Transform: resolve the type symbol; check whether any attribute's fully qualified name
   matches `"Fdp.Toolkit.Tkb.Attributes.TkbDescriptorAttribute"` (no project reference needed —
   check by string, same pattern as `Fhsm.SourceGen` checks `SharedAiConditionAttribute`).
3. For each match: collect `HierarchicalName` constructor argument and the fully qualified type name.
4. `Execute`: emit one `__TkbDescriptors_{AssemblyName}.g.cs` per assembly, containing a
   `[ModuleInitializer]` method that calls `TkbDescriptorRegistry.RegisterParser(...)` for each
   discovered type.

#### 5.4 Example Generated Code

```csharp
// Auto-generated by Tkb.SourceGen — DO NOT EDIT
internal static class __TkbDescriptors_MyAssembly
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        TkbDescriptorRegistry.RegisterParser(
            "Gen.VehicleParameters",
            static (template, partId, jsonElement) =>
            {
                var dto = jsonElement.Deserialize<VehicleParametersDto>(
                    FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });

        TkbDescriptorRegistry.RegisterParser(
            "Gen.AmmoWeaponBallistics",
            static (template, partId, jsonElement) =>
            {
                var dto = jsonElement.Deserialize<AmmoWeaponBallisticsDto>(
                    FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
        // ...one entry per [TkbDescriptor]-decorated type in this assembly
    }
}
```

The thunk only stores the descriptor (`AddDescriptor`). ECS projection is Phase 6.

---

### Phase 6: ECS Projection & Translators

**Goal:** Define the `ITkbEntityTranslator` interface and update `GhostPromotionSystem` to use
translators instead of `template.ApplyTo()`. Register `ITkbDatabase` as an ECS world singleton.

#### 6.1 `ITkbEntityTranslator` Interface

Located in `FDP/Engine/Fdp.Core/Abstractions/` or `FDP/Toolkits/Fdp.Toolkits/Tkb/`:

```csharp
/// <summary>
/// Custom translator that handles N TKB descriptors -> M ECS components (N:M mapping).
/// Mirrors the IEntityScenarioTranslator pattern for scenario serialization.
/// </summary>
public interface ITkbEntityTranslator
{
    /// <summary>
    /// Returns the CLR types of TKB descriptor DTOs this translator consumes.
    /// Used by the pipeline to track which descriptors have been projected.
    /// </summary>
    IEnumerable<Type> GetConsumedDescriptors();

    /// <summary>
    /// Projects data from the TKB template into ECS components on the entity.
    /// MUST call repo.IsComponentTypeRegistered<T>() before AddComponent<T>().
    /// </summary>
    void Inject(EntityRepository repo, Entity entity, TkbTemplate template);
}
```

Key constraint: every `Inject` implementation **must** guard each ECS component allocation with
`repo.IsComponentTypeRegistered<T>()`. Silent no-op on silent failures is an anti-pattern that
hides schema mismatches. The guard makes the bypass explicit.

#### 6.2 Example: `VehicleKinematicsTkbTranslator`

Located in the CarKinem or equivalent assembly. Demonstrates 1:4 (N:M) translation:

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
                Length = dto.Length, Width = dto.Width,
                MaxSpeedFwd = dto.MaxSpeedFwd, MaxAccel = dto.MaxAccel,
                WheelBase = dto.Length * 0.6f
            });

        if (repo.IsComponentTypeRegistered<VehicleState>())
            repo.AddComponent(entity, new VehicleState { Speed = 0, SteerAngle = 0 });

        if (repo.IsComponentTypeRegistered<NavState>())
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });

        if (repo.IsComponentTypeRegistered<PhysicsCollider>())
            repo.AddComponent(entity, new PhysicsCollider
            {
                Radius = Math.Max(dto.Length, dto.Width) / 2f, CollisionLayer = 1
            });
    }
}
```

#### 6.3 `ApplyTo` Callsite Audit and Full Migration

**Current state:** `TkbTemplate.ApplyTo()` is called by at minimum three systems:
- `GhostPromotionSystem` — ghost readiness/promotion path
- `NetworkSpawningSystem` — network-driven entity spawning
- `BlueprintApplicationSystem` — blueprint-driven entity instantiation

**All three must be migrated before the `ApplyTo` method is deleted (Phase 3, TKB-006). The
build must not compile if any reference to `TkbTemplate.ApplyTo()` remains in the codebase.**

Migration pattern: replace every `template.ApplyTo(...)` call with the translator loop:

```csharp
foreach (var translator in _translators)
    translator.Inject(repo, entity, template);
```

`ITkbEntityTranslator` instances are passed into each system via constructor injection (same
pattern as `IEntityScenarioTranslator` in the scenario serialization stack). The translator list
is identical for all three systems within the same node — they share the same
composition-root-injected `IReadOnlyList<ITkbEntityTranslator>`.

#### 6.4 `ITkbDatabase` as ECS World Singleton

`GhostPromotionSystem` needs access to `ITkbDatabase` to resolve `TkbIdentity.TkbType` to a
`TkbTemplate`. Register it during `RegisterDomainComponents()`:

```csharp
// In RegisterDomainComponents():
world.SetSingletonManaged<ITkbDatabase>(tkbDb);
```

**Verified pattern:** `IgNodeBootstrapper.RegisterDomainComponents()` already calls
`world.SetSingletonManaged<ITkbDatabase>(tkb)`. The same pattern must be applied in the SimHost
and CGF bootstrappers.

#### 6.5 Composition Root: Translator Aggregation

Domain-specific translators live in isolated assemblies (e.g., `CarKinem`, `Hrot.Core`). They
must be aggregated at the composition root — `SharedApplicationBootstrapper` or
`HrotNodeBuilder` — and passed down together to every system that replaced `ApplyTo`.

```csharp
// In the composition root (per node role):
var translators = new List<ITkbEntityTranslator>
{
    new VehicleKinematicsTkbTranslator(),
    // add per-node domain translators here
};
var translatorList = translators.AsReadOnly();

// Pass the same list to all three migration targets:
var ghostPromotion      = new GhostPromotionSystem(world, tkbDb, translatorList);
var networkSpawning     = new NetworkSpawningSystem(..., translatorList);
var blueprintApplicator = new BlueprintApplicationSystem(..., translatorList);
```

This ensures a single point of truth for which translators are active on a given node type. An
IG node would include BIG-specific translators; a SimHost node would not.

⚠⚠ **Read §6.5b before acting on that last sentence.**

#### 6.5b ⭐⭐⭐ THE LIST IS NOT THE NARROWING LEVER — **the REGISTRATION SET is** *(added `2026-08-30`)*

> 📌 **Added because a session got this exactly backwards.** It found a host passing **no** translators,
> read §6.5's closing sentence as licence — *"per-node lists, so an empty one may be deliberate"* — and
> filed the omission as *"possibly intentional Brain-node narrowing."* ⛔ **Wrong.** The correcting
> question came from the user: *"how is the component creation gated — TKB can hardly instantiate an ECS
> component that is not registered on CGF (because subsystems register just what they need)?"*

⭐⭐ **§6.1's guard is not merely defensive; it is the composition mechanism.** Every write in every
translator is **double-gated**:

| gate | asks | expressed by |
|---|---|---|
| **①** | *does this TYPE carry the data?* | `template.GetDescriptor<TDto>() == null ⇒ return` — the **TKB author's** decision |
| **②** | *does THIS HOST want the component?* | `repo.IsComponentTypeRegistered<TComponent>()` — the **host composition root's** decision |

📐 **Verified `2026-08-30` across all nine production implementations** — `SpatialCore`,
`VehicleKinematics`, `Combat`, `Perception`, `Behavior`, `Presentation`, `Animation`, `AiDiagnostics`,
`InfantryVehicleStateStrip`. ⚠ The guard is load-bearing: `EntityRepository` **throws**
`InvalidOperationException("Component type … is not registered")` on an unregistered write.

⇒ 🔒🔒 **A translator whose components a host never registered is ALREADY a no-op on that host.**

##### ⭐ So the rule, stated plainly

| ⭐ do | ⛔ don't |
|---|---|
| give every node its **full projection set** | ⛔ hand a node a short list to make it materialise less |
| express *"this host does not want X"* by **not registering X** | ⛔ express it by omitting X's translator |
| pass the **same instance** to `NetworkSpawningSystem`, `BlueprintApplicationSystem` and `GhostPromotionSystem` *(§6.3)* | ⛔ let the three drift apart |

⭐⭐ **Why registration is the better lever:** not registering a component is **one** decision, in the
host's component registry, and any code that then tries to write it **throws** — a loud, single-site
failure. Omitting a translator is a decision taken at a composition root and **fails silently for every
entity that host ever spawns**, with the entity still looking spawned *(it keeps `NetworkIdentity`,
`NetworkOwnership`, `TkbIdentity` and its DIS header)*.

⛔⛔ **An EMPTY list is never a curation choice** — it disables gates ① and ② together. On a node that
spawns, it means *"born with an identity but no type."*

⚠ **What §6.5's closing sentence actually means:** per-node variation is *allowed* — a node may **add**
translators no other node has *(`AiDiagnosticsTkbTranslator` on SimHost, `InfantryVehicleStateStrip` on
Stride)*. ⛔ It does **not** mean a node should **subtract** translators to avoid components; gate ② does
that for free.

##### 📌 The instance this section exists for

📐 `2026-08-30`, filed as **`CE-138`**: CGF — which
[`Hrot-Simulation-Pipeline.md`](../../projects/relationships/Hrot-Simulation-Pipeline.md) §2 names the
*"entity spawning authority"*, and whose §4.3 step reads **"Apply TKB template components"** — passes
translators on **none** of the three seams. Rails:
`Hrot.SimHost.Tests/TkbTranslatorSpawnParityRails.cs`.

---

### Phase 7: Node-Side Integration (TkbLoadClusterStateHandler)

**Goal:** Implement the cluster state handler that intercepts `PrepareLive` and `PrepareEdit`,
checks the differential cache, and ingests the TKB from local staging.

#### 7.1 Key Design Decision: No Orchestrator Prefetch

TKB file distribution is NOT handled by the orchestrator state machine. This was an explicit
architectural reversal documented in the design-talk:

> "Removing the orchestrator prefetch is the correct architectural decision. Tightly coupling the
> orchestrator's state machine to the distribution of specific domain assets is an anti-pattern.
> Asset synchronization belongs in a dedicated, out-of-band delivery pipeline."

The node **assumes the TKB artifact (`.zip` or raw directory) is already present in its local
staging area** before `PrepareLive` / `PrepareEdit` arrives. No orchestrator file push occurs.

#### 7.2 `TkbLoadClusterStateHandler`

Located in `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/` (or HROT-common layer):

```csharp
public sealed class TkbLoadClusterStateHandler : IClusterStateHandler
{
    private readonly ITkbDatabase _tkbDb;
    private readonly string _localTkbStagingRoot;

    private string? _lastLoadedTkbName;
    private DateTime _lastLoadedTimestamp;

    public TkbLoadClusterStateHandler(ITkbDatabase tkbDb, string localStagingRoot)
    {
        _tkbDb = tkbDb ?? throw new ArgumentNullException(nameof(tkbDb));
        _localTkbStagingRoot = Path.Combine(localStagingRoot, "TKB");
    }

    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareLive || operation == NodeOpType.PrepareEdit;

    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        // Read TkbName from the node's own local scenario file, not from the wire payload.
        string? requestedTkb = ExtractTkbNameFromLocalScenario(_localTkbStagingRoot);

        if (string.IsNullOrWhiteSpace(requestedTkb))
        {
            // No TkbName in local scenario -> use hardcoded fallback catalog.
            // NedTkbCatalog.RegisterAll() is called only if the db is empty.
            if (!_tkbDb.GetAll().Any())
                NedTkbCatalog.RegisterAll((TkbDatabase)_tkbDb);
            return Task.FromResult<object?>(null);
        }

        string localPath = Path.Combine(_localTkbStagingRoot, $"{requestedTkb}.zip");

        // Differential cache check using file modification time.
        DateTime currentFileTime = File.Exists(localPath)
            ? File.GetLastWriteTimeUtc(localPath)
            : DateTime.MinValue;

        if (_lastLoadedTkbName == requestedTkb && _lastLoadedTimestamp == currentFileTime)
            return Task.FromResult<object?>(null); // Cache hit.

        if (!File.Exists(localPath))
            throw new FileNotFoundException(
                $"[TkbLoad] TKB artifact not found at '{localPath}'. " +
                "Ensure the TKB file is staged before transitioning to Live/Edit.");

        _tkbDb.Clear();
        using var loader = new TkbUnifiedLoader(localPath);
        var deserializer = new TkbDeserializer();
        foreach (var entityFile in loader.EnumerateEntityFiles())
            deserializer.ParseAndRegister(entityFile, _tkbDb);

        _lastLoadedTkbName = requestedTkb;
        _lastLoadedTimestamp = currentFileTime;
        _tkbDb.ActiveTkbName = requestedTkb;  // expose for save pipeline

        FdpLog<TkbLoadClusterStateHandler>.Info(
            "[TkbLoad] Loaded TKB '{0}' ({1} entities).",
            requestedTkb, _tkbDb.GetAll().Count());

        return Task.FromResult<object?>(null);
    }

    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    // Abort is no-op: TKB survives Idle and is cached across transitions.
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    private static string? ExtractTkbNameFromLocalScenario(string localStagingRoot)
    {
        // Peek the TkbName from the node's own locally staged scenario header file
        // using a forward-only Utf8JsonReader — no JsonDocument, no DOM allocation.
        string headerPath = Path.Combine(localStagingRoot, "ScenarioHeader.json");
        if (!File.Exists(headerPath)) return null;
        var bytes = File.ReadAllBytes(headerPath);
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.ValueTextEquals("TkbName"))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : null;
            }
        }
        return null;
    }
}
```

#### 7.3 TkbName Resolution — Node Reads Its Own Local Scenario

The `TkbLoadClusterStateHandler` does NOT read `TkbName` from the orchestrator's wire payload.
The orchestrator is kept ignorant of TKB domain logic. Instead, the handler calls
`ExtractTkbNameFromLocalScenario(localStagingRoot)` which peeks the node's own locally staged
scenario header file with a forward-only `Utf8JsonReader` (zero DOM, zero string allocation
beyond the result). If the local scenario file is absent or has no `TkbName`, the fallback
catalog is used.

If the local TKB staging file is missing and the scenario requires a specific TKB, the node
throws `FileNotFoundException` and aborts its own transition. Because all nodes run the same
check independently, a missing TKB artifact naturally halts the cluster without any
orchestrator-level asset tracking.

#### 7.4 Differential Cache Semantics

- Cache key: `(TkbName, File.GetLastWriteTimeUtc(zipPath))`.
- Cache hit: skip `_tkbDb.Clear()` and re-ingestion entirely. TKB already in memory.
- Cache miss (name changed OR file timestamp changed): call `_tkbDb.Clear()` then re-ingest.
- On success: sets `_tkbDb.ActiveTkbName = requestedTkb` for the save pipeline.
- `Abort()` is a no-op — TKB survives `Idle` state and is not rolled back on abort.

---

### Phase 8: Scenario Header, Save Pipeline & Bootstrapper Wiring

**Goal:** Extend the scenario envelope with `TkbName`; wire the save pipeline so that the active
TKB name is persisted into saved scenarios; implement the orchestrator sanity check; wire the
load handler and translator aggregation into the bootstrapper.

#### 8.1 `ScenarioHeaderDto` Extension

Add to `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs`:

```csharp
public sealed class ScenarioHeaderDto
{
    public string? SubsystemType { get; set; }
    public string? SchemaVersion { get; set; }
    public string? TkbName { get; set; }   // NEW — null means "no opinion"
}
```

#### 8.2 Save Pipeline: Stamping the Active TKB Name

**Problem:** if the save pipeline does not persist `TkbName`, newly authored or re-saved
scenarios will lose the TKB requirement, degrading the cluster to the fallback catalog on the
next load.

**Solution — three-point wiring:**

1. **`ITkbDatabase.ActiveTkbName`** (defined in Phase 3.2) — set by `TkbLoadClusterStateHandler`
   on every successful VFS ingestion. Null when using the `NedTkbCatalog` fallback.

2. **`ScenarioHeader` record** (used by `ScenarioSerializer.Serialize`) — extend to accept
   `TkbName`:  
   ```csharp
   public record ScenarioHeader(
       string SubsystemType, string SchemaVersion,
       string? TkbName);  // NEW
   ```

3. **`ScenarioFileService.SaveScenario()`** (or the equivalent save entry point) — must query
   `ITkbDatabase.ActiveTkbName` and pass it into the header when calling the serializer:
   ```csharp
   var header = new ScenarioHeader(
       SubsystemType: _subsystemType,
       SchemaVersion: CurrentSchemaVersion,
       TkbName: _tkbDb.ActiveTkbName);   // from ITkbDatabase singleton
   scenarioSerializer.Serialize(scenarioData, header, ...);
   ```

With this wiring in place, every scenario file saved from an active session will carry the
correct `TkbName`. Because all distributed partial scenario files are saved by nodes that share
the same active TKB, they will naturally agree on `TkbName`, making orchestrator consensus
validation straightforward.

#### 8.3 Orchestrator Consensus Check (Sanity Gate Only)

The orchestrator's `AssetPrefetchProcessManager` (or the equivalent scenario staging component)
performs a **read-only sanity check** when staging files for a transition. It does NOT embed
`TkbName` into any wire payload — nodes resolve their own TKB requirement from their local
scenario copy (see Phase 7.3).

**Algorithm:**
1. For each staged scenario file, open a forward-only `Utf8JsonReader` (no `JsonDocument`).
   Read until `"TkbName"` is found in the header section; extract the string value; stop
   reading. Total allocation: one string per file.
2. Collect all non-null, non-empty values.
3. **Consensus rule:** all non-empty values must be equal. If any two differ, immediately abort
   the `TransitionState` operation and log a fatal error naming both conflicting values and
   their source files. The cluster halts before any node begins `PrepareLive`.
4. If consensus passes (or all values are null): proceed with the transition. The orchestrator
   does nothing further with the `TkbName`. It is not embedded in `NodeTransitionPayloadDto`
   or any other wire structure.

#### 8.4 `NodeBootstrapper.BuildOrchestration()` Handler Registration

**Critical ordering:** `TkbLoadClusterStateHandler` must be registered **before**
`HrotScenarioLoadHandler`. Handlers execute in registration order. TKB must be fully loaded in
memory before the scenario parser creates entity creation requests that look up `TkbTemplate`
blueprints.

```csharp
// Inside NodeBootstrapper.BuildOrchestration()
// (translatorList is built at composition root — see Phase 6.5)

// ...existing handlers (ReferenceArchiveHandler, ReferenceCheckpointHandler, etc.)...

// NEW: Register TKB loader FIRST, before any scenario loaders.
if (tkbDb != null)
{
    clusterSlave.RegisterHandler(
        new TkbLoadClusterStateHandler(tkbDb, localTempRoot));
}

// Existing: scenario handlers registered AFTER TKB loader.
if (scenarioSerializer != null)
{
    clusterSlave.RegisterHandler(
        new HrotScenarioLoadHandler(scenarioSerializer, scenarioLoader, zoneService,
            scenarioExtractor, scenarioSource, scenarioIdAllocator,
            world: world, controller: controller, storageDirectory: localTempRoot));
    // ... HrotEditLoadHandler, ReferenceEpisodeLoadHandler ...
}
```

The `tkbDb` and `translatorList` parameters are threaded through `BuildOrchestration()` from
the composition root where `HrotEnvironment.CreateTkb()` is called.

---

### Phase 9: TKB Editor (Conceptual — Not Implemented This Workstream)

The TKB Editor is a separate WPF application (not a mode of the existing ConfigEditor). It
provides a 3-pane layout: Entity Tree | Descriptor List | Descriptor Editor.

**Reuses from ConfigEditor stack:**
- `JsonDomDeserializer` — reading existing entity files
- `SchemaLoaderService` (adapted) — loading descriptor schemas
- `DomValidator` — validating descriptor values
- `EditHistoryService` — undo/redo
- `DomNodeFactory` — creating new descriptor instances

**New components** (not in scope this workstream):
- New shell and navigation VM
- Per-entity isolated DOMs (no global merged DOM)
- `ITkbStorageStrategy`-backed persistence (raw directory for authoring)
- Focused descriptor editor VMs

Editor implementation is deferred to a separate workstream.

---

## 4. Dependency Notes

| New Component | Recommended Location |
|---|---|
| `[TkbDescriptor]`, `[WeaponRef]`, etc. | `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/` |
| `TkbEntityFile`, `ITkbStorageStrategy`, providers | `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/` |
| `TkbUnifiedLoader` | `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/` |
| `TkbDeserializer` | `FDP/Toolkits/Fdp.Toolkits/Tkb/` |
| `TkbDescriptorRegistry` | `FDP/Toolkits/Fdp.Toolkits/Tkb/` |
| `Tkb.SourceGen` project | `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/` |
| `ITkbEntityTranslator` | `FDP/Engine/Fdp.Core/Abstractions/` |
| Concrete DTOs (`VehicleParametersDto`, etc.) | `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/` or HROT layer |
| `VehicleKinematicsTkbTranslator` | CarKinem assembly or `Hrot.Core` |
| `TkbLoadClusterStateHandler` | `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/` |

`Tkb.SourceGen` must be `netstandard2.0`. It must NOT reference `Fdp.Core` or any FDP runtime
assemblies directly — it recognizes `[TkbDescriptor]` by fully qualified string name only, same
as `Fhsm.SourceGen` recognizes `SharedAiConditionAttribute`.

---

## 5. Critical Implementation Constraints

1. **Zero-alloc `#PartId` parsing:** Use `ReadOnlySpan<char>`, `IndexOf('#')`, `int.Parse(span)`,
   and `Dictionary.AlternateLookup<ReadOnlySpan<char>>` in `TkbDeserializer`. No `Substring` on
   the hot path.

2. **`ZipTkbProvider` is read-only:** `ZipArchiveMode.Read` only. `WriteEntityFile` and
   `DeleteEntityFile` throw `NotSupportedException`. ZIP creation is a CI/CD build step.

3. **`IsComponentTypeRegistered<T>()` guard:** Every `ITkbEntityTranslator.Inject` implementation
   must call this before `AddComponent<T>`. Never rely on silent no-op.

4. **Orchestrator is TKB-ignorant:** The orchestrator performs a `Utf8JsonReader`-based sanity
   check for `TkbName` consensus across staged files, but does NOT embed `TkbName` into any
   wire payload. Nodes resolve their own TKB requirement from their local scenario file.

5. **No orchestrator TKB prefetch:** File sync is out-of-band. Node assumes local ZIP present.

6. **`TkbLoadClusterStateHandler` registered FIRST:** Before `HrotScenarioLoadHandler` on the
   `ClusterSlave`.

7. **Ghost shell allocation:** `EntityMasterIngressTranslator` / `NetworkSpawningSystem` stamps
   `TkbIdentity` and `GhostStateTracker` at entity shell creation. `GhostPromotionSystem` later
   evaluates readiness and runs translators.

8. **`ITkbDatabase` as ECS singleton:** `world.SetSingletonManaged<ITkbDatabase>(tkb)` in
   `RegisterDomainComponents()` of each bootstrapper (SimHost, IG, CGF) that runs
   `GhostPromotionSystem`.

9. **`NedTkbCatalog` fallback:** If the local scenario file has no `TkbName` (or is absent),
   use the hardcoded fallback catalog. Do NOT double-register if already populated.

10. **`ApplyTo` fully removed:** The build must not compile with any reference to
    `TkbTemplate.ApplyTo()`. `GhostPromotionSystem`, `NetworkSpawningSystem`, and
    `BlueprintApplicationSystem` all migrate to the translator loop in the same batch.

11. **Directory boundary semantics:** `GetEntitiesByCategory("Platform/Vehicle")` must NOT match
    `"Platform/Vehicle_Heavy/MBT"`. Match requires exact path or a trailing `/` separator.

12. **`ActiveTkbName` drives the save pipeline:** After successful VFS ingestion,
    `_tkbDb.ActiveTkbName` is set. `ScenarioFileService` reads this value and embeds it in the
    `ScenarioHeader` when serializing. This is the sole mechanism by which the TKB requirement
    is persisted into scenario files.

---

## 6. Overall Success Conditions

The TKB refactor is considered complete and correct when ALL of the following hold:

**Domain Schema Purity**
- All descriptor DTOs are pure POCOs: no ECS base classes, no `[MessagePackObject]`, no
  `EntityRepository` references.
- C# class names are fully decoupled from JSON keys; renaming a DTO class does not break
  deserialization as long as `[TkbDescriptor("...")]` is unchanged.
- `[TkbDescriptor]` constructor throws `ArgumentException` if the name contains `#` or is null/empty.

**Storage and Transport Abstraction**
- One JSON file = one entity = one `TkbTemplate`. No merged DOM at runtime.
- `RawDirectoryTkbProvider` supports read AND write (authoring path).
- `ZipTkbProvider` is read-only; `WriteEntityFile` / `DeleteEntityFile` throw
  `NotSupportedException`. No write mutex exists.
- Consumers of `TkbUnifiedLoader` cannot tell whether the source is a directory or a ZIP.

**Zero-Allocation Streaming Ingestion**
- Exactly one `JsonDocument` is alive at any moment during ingestion (confirmed by a
  memory-profiling test over 10,000 entities asserting zero LOH allocations).
- `TkbDescriptorRegistry` is populated at app startup by `[ModuleInitializer]`-emitted code;
  no runtime reflection or `GetAssemblies()` scanning occurs.
- `#PartId` splitting uses `ReadOnlySpan<char>` and `Dictionary.AlternateLookup<ReadOnlySpan<char>>`
  with zero `string.Substring` calls on the hot path.
- Unknown descriptors are silently skipped with zero allocation.

**Decoupled ECS Projection**
- `TkbTemplate` contains no ECS applicator delegates; `ApplyTo()` does not exist.
- The build fails if any reference to `TkbTemplate.ApplyTo()` exists anywhere in the solution.
- `GhostPromotionSystem`, `NetworkSpawningSystem`, and `BlueprintApplicationSystem` all accept
  `IReadOnlyList<ITkbEntityTranslator>` via constructor and execute the translator loop.
- Every `ITkbEntityTranslator.Inject` guards each `AddComponent<T>` with
  `IsComponentTypeRegistered<T>()`.

**Category Path Correctness**
- `GetEntitiesByCategory("Platform/Vehicle")` returns entities in `"Platform/Vehicle"` and
  `"Platform/Vehicle/MBT"` but NOT entities in `"Platform/Vehicle_Heavy"`.

**Cluster Orchestration and Lifecycle**
- `TkbLoadClusterStateHandler` is wired before `HrotScenarioLoadHandler` in the bootstrapper.
- The differential cache correctly prevents re-ingestion when `TkbName` and ZIP timestamp are
  unchanged across two consecutive `PrepareLive` calls.
- `TkbLoadClusterStateHandler` reads `TkbName` from the node's own local scenario file (not
  from the intent payload).
- The orchestrator's consensus check uses `Utf8JsonReader` only; it aborts on conflict and does
  NOT embed `TkbName` into `NodeTransitionPayloadDto` or any other wire structure.

**Save Pipeline**
- After a successful TKB load, `ITkbDatabase.ActiveTkbName` returns the loaded TKB name.
- A scenario saved during an active session contains `"TkbName"` in `ScenarioHeaderDto` with
  the correct value (integration test: load TKB "Sample_v1", save scenario, open saved file,
  assert `ScenarioHeaderDto.TkbName == "Sample_v1"`).
- When the fallback `NedTkbCatalog` is used, `ActiveTkbName` is null and saved scenarios
  omit or null-out the `TkbName` field.
