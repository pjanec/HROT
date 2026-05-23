# Tkb.SourceGen (Fdp.Toolkit.Tkb.SourceGen)

| Field        | Value                                                                    |
|--------------|--------------------------------------------------------------------------|
| Project file | `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/Tkb.SourceGen.csproj`           |
| Namespace    | `Fdp.Toolkit.SourceGen`                                                  |
| Target       | `netstandard2.0`                                                         |
| Date         | 2026-05-23                                                               |

---

## README Validation

**Missing** -- there is no `README.md` in the project folder.

---

## Executive Overview

`Tkb.SourceGen` is a Roslyn `IIncrementalGenerator` that auto-registers TKB
descriptor DTO types at process startup, eliminating all hand-written registration
boilerplate.

### Problem it solves

The TKB (Toolkit Blackboard) system loads entity data from JSON files. Each JSON
object has a named section (called a "hierarchical name", e.g. `"Gen.VehicleParameters"`)
whose value must be deserialized into a specific C# DTO class and handed to a
`TkbTemplate`. Without a generator, every DTO type would require a hand-written call
to `TkbDescriptorRegistry.RegisterParser(...)` somewhere at startup, with the risk of
forgetting to add the call when adding a new DTO, introducing a silent data loss bug.

### What it generates

For each assembly that contains one or more types decorated with
`[TkbDescriptor("some.name")]`, the generator emits a single C# file:

```
__TkbDescriptors_<SanitizedAssemblyName>.g.cs
```

The file contains a `static` class with a `[ModuleInitializer]` method. The CLR
calls the method automatically the first time any type in the assembly is accessed.
The method iterates through every decorated type and calls
`TkbDescriptorRegistry.RegisterParser(...)`, wiring each hierarchical name to a
strongly-typed JSON deserialization lambda.

### Why it matters

- **Zero-touch registration**: add `[TkbDescriptor("...")]` to a new DTO and it is
  automatically registered. No other change is needed.
- **Correctness at compile time**: duplicate hierarchical names across types in the
  same assembly produce a `TKB001` compiler warning immediately.
- **No reflection at runtime**: the generated lambdas call
  `JsonSerializer.Deserialize<T>` with concrete type arguments -- there is no
  dictionary-based type lookup or `Type.GetType` at runtime.

---

## Architecture

### Roslyn IIncrementalGenerator Pattern

Roslyn source generators run inside the compiler host process. The
`IIncrementalGenerator` API (introduced in Roslyn 4.x) models the pipeline as a
series of incremental steps, each of which is cached and only re-executed when its
inputs change. This is the preferred API over the older `ISourceGenerator`, which
re-ran fully on every keystroke.

The pipeline has three stages:

```
+---------------------------+
|  SyntaxProvider (filter)  |
|  Predicate: TypeDecl &&   |
|  AttributeLists.Count > 0 |
+---------------------------+
            |
            | ImmutableArray<TypeDeclarationSyntax>
            v
+---------------------------+
| CompilationProvider       |
| (provides semantic model) |
+---------------------------+
            |
            | Combine
            v
+---------------------------+
| Execute()                 |
| - resolve attribute FQN   |
| - collect TkbDescriptorInfo|
| - deduplicate + warn TKB001|
| - emit .g.cs source       |
+---------------------------+
```

The `Combine` operator merges the `ImmutableArray<TypeDeclarationSyntax>` snapshot
with the `Compilation` object so that `Execute` has everything it needs in one call.

### Overall System Flow

```
+-------------------+   compile-time   +---------------------------+
| DTO source file   |----------------->| TkbDescriptorGenerator    |
| [TkbDescriptor(   |                  | (IIncrementalGenerator)   |
|  "Gen.Vehicle     |                  +---------------------------+
|   Parameters")]   |                            |
| public record     |                            | emits
| VehicleParametersDto{}                         v
+-------------------+          +-------------------------------+
                               | __TkbDescriptors_<Asm>.g.cs  |
                               | [ModuleInitializer]           |
                               | Register() {                  |
                               |   Registry.RegisterParser(    |
                               |     "Gen.VehicleParameters",  |
                               |     (t, id, elem) => {        |
                               |       var dto = Deserialize   |
                               |         <VehicleParametersDto>|
                               |         (elem, opts);         |
                               |       t.AddDescriptor(dto,id);|
                               |     });                       |
                               | }                             |
                               +-------------------------------+
                                              |
                                  process startup (CLR auto-calls)
                                              |
                                              v
                               +-------------------------------+
                               | TkbDescriptorRegistry         |
                               | Dictionary<string, Thunk>     |
                               | "Gen.VehicleParameters" -> fn |
                               | "TkbMaster"            -> fn  |
                               | ...                           |
                               +-------------------------------+
                                              |
                                    JSON parsing at runtime
                                              |
                                              v
                               +-------------------------------+
                               | TkbDeserializer               |
                               | reads JSON, splits by key,    |
                               | calls Registry.TryGetParser() |
                               | invokes thunk for each section|
                               +-------------------------------+
```

### Incremental Pipeline Detail

```
context.SyntaxProvider.CreateSyntaxProvider(
    predicate,   <-- fast syntax-only check (no semantic model)
    transform    <-- return the syntax node
)
         |
         | .Collect()  --> ImmutableArray snapshot
         |
context.CompilationProvider
         |
         | .Combine(candidateSyntax)
         |
         v
context.RegisterSourceOutput(compilationAndTypes, Execute)
```

The predicate runs on every syntax tree edit; it is intentionally minimal
(`node is TypeDeclarationSyntax t && t.AttributeLists.Count > 0`) to avoid
expensive work in the hot path. The full semantic resolution (attribute FQN
matching, type symbol extraction) happens only in `Execute`, which is invoked at
most once per compilation snapshot.

---

## Source Structure

The project contains exactly one source file.

### `TkbDescriptorGenerator.cs`

**Namespace:** `Fdp.Toolkit.SourceGen`

**Class:** `TkbDescriptorGenerator : IIncrementalGenerator`

#### Fields / Constants

| Member | Purpose |
|--------|---------|
| `TkbDescriptorAttributeMetadataName` | Fully qualified metadata name of the trigger attribute: `"Fdp.Toolkit.Tkb.Attributes.TkbDescriptorAttribute"`. Used for `Compilation.GetTypeByMetadataName`. |
| `DuplicateHierarchicalName` | `DiagnosticDescriptor` for `TKB001`, emitted as a `Warning` when two types in the same assembly share a hierarchical name. |

#### Methods

| Method | Signature | Purpose |
|--------|-----------|---------|
| `Initialize` | `void Initialize(IncrementalGeneratorInitializationContext)` | Wires up the incremental pipeline. Called once by the compiler host. |
| `Execute` | `static void Execute(SourceProductionContext, Compilation, ImmutableArray<TypeDeclarationSyntax>)` | Core logic: resolves attribute symbol, filters candidate nodes, deduplicates, emits source. |
| `SanitizeIdentifier` | `static string SanitizeIdentifier(string)` | Converts an assembly name to a valid C# identifier for use as a class name suffix. Replaces non-alphanumeric characters with `_`. Prepends `_` if the first character is a digit. |
| `GenerateSource` | `static string GenerateSource(string, List<TkbDescriptorInfo>)` | Produces the complete C# source text for the generated file. |

#### Internal type

| Type | Purpose |
|------|---------|
| `TkbDescriptorInfo` | Simple data carrier holding `HierarchicalName` (string) and `FullyQualifiedTypeName` (string) for one annotated type. |

---

## Public API Reference

### Trigger Attribute: `TkbDescriptorAttribute`

**Assembly:** `Fdp.Toolkits` (project `Fdp.Toolkits.csproj`)  
**Namespace:** `Fdp.Toolkit.Tkb.Attributes`  
**Full metadata name:** `Fdp.Toolkit.Tkb.Attributes.TkbDescriptorAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct,
                Inherited = false, AllowMultiple = false)]
public sealed class TkbDescriptorAttribute : Attribute
{
    public string HierarchicalName { get; }
    public TkbDescriptorAttribute(string hierarchicalName);
}
```

**Rules enforced at runtime (attribute constructor):**

| Rule | Exception |
|------|-----------|
| `hierarchicalName` must not be null, empty, or whitespace | `ArgumentException` |
| `hierarchicalName` must not contain `'#'` | `ArgumentException` |

The `'#'` restriction exists because `#PartId` is a runtime instance delimiter used
by `TkbDeserializer` to distinguish multiple instances of the same descriptor type
on one entity. The schema-level name must not contain this separator.

**Rules enforced at compile time (generator):**

| Rule | Diagnostic |
|------|-----------|
| Two types in the same assembly share the same hierarchical name (case-insensitive) | `TKB001` Warning |

### Generated Code Structure

For an assembly named `MySimModule` containing types decorated with
`[TkbDescriptor]`, the generator emits:

**File name:** `__TkbDescriptors_MySimModule.g.cs`

```csharp
// <auto-generated/>

internal static class __TkbDescriptors_MySimModule
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        global::Fdp.Toolkit.Tkb.TkbDescriptorRegistry.RegisterParser(
            "<hierarchical-name-1>",
            static (template, partId, jsonElement) =>
            {
                var dto = global::System.Text.Json.JsonSerializer.Deserialize<global::<FQN1>>(
                    jsonElement,
                    global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });

        // ... one block per annotated type ...
    }
}
```

Key properties of the generated code:

- The class is `internal static` -- it does not pollute the public surface.
- `[ModuleInitializer]` guarantees CLR invocation before any user code in the
  assembly runs. No explicit `Init()` call is required anywhere.
- All type references use `global::` fully qualified form to avoid namespace
  collisions.
- Deserialization uses `FdpJsonOptionsRegistry.DefaultRelaxed` -- the project-wide
  permissive JSON options (case-insensitive property matching, number tolerance).
- `template.AddDescriptor(dto, partId)` stores the strongly-typed DTO on the
  `TkbTemplate` under the given part ID. Part IDs support multiple instances of
  the same descriptor category on a single entity (e.g. two weapon mounts).

### `TkbDescriptorRegistry` (runtime counterpart)

**Assembly:** `Fdp.Toolkits`  
**Namespace:** `Fdp.Toolkit.Tkb`

```csharp
public delegate void TkbDescriptorParserThunk(
    TkbTemplate template, int partId, JsonElement jsonElement);

public static class TkbDescriptorRegistry
{
    public static void RegisterParser(string hierarchicalName,
                                      TkbDescriptorParserThunk parser);

    public static bool TryGetParser(ReadOnlySpan<char> hierarchicalName,
                                    out TkbDescriptorParserThunk? thunk);

    internal static void Clear(); // for testing only
}
```

The dictionary uses `StringComparer.OrdinalIgnoreCase` so that lookup and
registration are both case-insensitive.

---

## Dependencies

### NuGet Packages

| Package | Version | Note |
|---------|---------|------|
| `Microsoft.CodeAnalysis.CSharp` | 4.8.0 | Roslyn compiler APIs (`IIncrementalGenerator`, `SyntaxProvider`, `Compilation`, `INamedTypeSymbol`, etc.) |
| `Microsoft.CodeAnalysis.Analyzers` | 3.3.4 | Analyzer packaging rules; enables `IsRoslynComponent` validation |

Both packages are `PrivateAssets="all"` -- they are only used during compilation
and are not transitive dependencies of consumers.

### Project References (consumers of this generator)

| Consumer project | Reference type | Purpose |
|-----------------|----------------|---------|
| `Fdp.Toolkits` (`Fdp.Toolkits.csproj`) | `OutputItemType="Analyzer"` `ReferenceOutputAssembly="false"` | Production use: auto-registers all `[TkbDescriptor]` types in the Fdp.Toolkits assembly |
| `Fdp.Toolkits.Tests` (`Fdp.Toolkits.Tests.csproj`) | Plain `ProjectReference` (non-analyzer) | Test use: exposes `TkbDescriptorGenerator` class directly for Roslyn compilation tests |

The `OutputItemType="Analyzer"` + `ReferenceOutputAssembly="false"` pattern is the
standard way to reference a source generator in MSBuild. The generator assembly is
passed to the compiler as an analyzer, but the generator types are not available at
runtime in the consuming project.

### MSBuild Project Properties

| Property | Value | Reason |
|----------|-------|--------|
| `TargetFramework` | `netstandard2.0` | Required for Roslyn generator compatibility |
| `IsRoslynComponent` | `true` | Enables generator-specific MSBuild rules |
| `EnforceExtendedAnalyzerRules` | `true` | Enforces additional restrictions for analyzer/generator assemblies |
| `LangVersion` | `latest` | Allows modern C# syntax in generator code itself |
| `NoWarn` | `CS8632;RS2008` | Suppresses nullable annotation advisory (CS8632) and suppressed analyzer diagnostic rule (RS2008) |
| `TreatWarningsAsErrors` | `true` | All warnings in the generator project are errors |

---

## Usage Examples

### Example 1 -- Basic single descriptor

**Before (without the generator)**: the developer must remember to call
`RegisterParser` somewhere at startup, typically in a module initializer or
application bootstrap:

```csharp
// Somewhere in application startup -- easy to forget:
TkbDescriptorRegistry.RegisterParser(
    "Gen.VehicleParameters",
    static (template, partId, elem) =>
    {
        var dto = JsonSerializer.Deserialize<VehicleParametersDto>(
            elem, FdpJsonOptionsRegistry.DefaultRelaxed)!;
        template.AddDescriptor(dto, partId);
    });
```

**After (with the generator)**: decorate the DTO class and do nothing else:

```csharp
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    [TkbDescriptor("Gen.VehicleParameters")]
    public record VehicleParametersDto
    {
        public float Mass    { get; init; }
        public float Length  { get; init; }
        public float Width   { get; init; }
        public float MaxSpeedFwd { get; init; }
        public float MaxSpeedRev { get; init; }
    }
}
```

**Generated file** (`__TkbDescriptors_Fdp_Toolkits.g.cs`, excerpt):

```csharp
// <auto-generated/>

internal static class __TkbDescriptors_Fdp_Toolkits
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        global::Fdp.Toolkit.Tkb.TkbDescriptorRegistry.RegisterParser(
            "Gen.VehicleParameters",
            static (template, partId, jsonElement) =>
            {
                var dto = global::System.Text.Json.JsonSerializer
                    .Deserialize<global::Fdp.Toolkit.Tkb.Domain.VehicleParametersDto>(
                        jsonElement,
                        global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
    }
}
```

---

### Example 2 -- Multiple descriptors across domains

The generator aggregates all annotated types from the same assembly into a single
`Register()` call, regardless of which namespace they live in.

```csharp
// Domain/TkbMasterDto.cs
[TkbDescriptor("TkbMaster")]
public record TkbMasterDto
{
    public string CustomName { get; init; } = string.Empty;
    public string DisType    { get; init; } = string.Empty;
}

// Domain/WeaponCapabilitiesDto.cs
[TkbDescriptor("Gen.WeaponCapabilities")]
public record WeaponCapabilitiesDto
{
    public int  MaxWeaponCount  { get; init; }
    public bool HasDirectFire   { get; init; }
    public bool HasIndirectFire { get; init; }
}

// Domain/BehaviorProfileDto.cs
[TkbDescriptor("AI.BehaviorProfile")]
public record BehaviorProfileDto
{
    public string ProfileName { get; init; } = string.Empty;
    public float  AggressionLevel { get; init; }
}
```

**Generated output** (single file for the assembly):

```csharp
// <auto-generated/>

internal static class __TkbDescriptors_Fdp_Toolkits
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        global::Fdp.Toolkit.Tkb.TkbDescriptorRegistry.RegisterParser(
            "TkbMaster",
            static (template, partId, jsonElement) =>
            {
                var dto = global::System.Text.Json.JsonSerializer
                    .Deserialize<global::Fdp.Toolkit.Tkb.Domain.TkbMasterDto>(
                        jsonElement,
                        global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });

        global::Fdp.Toolkit.Tkb.TkbDescriptorRegistry.RegisterParser(
            "Gen.WeaponCapabilities",
            static (template, partId, jsonElement) =>
            {
                var dto = global::System.Text.Json.JsonSerializer
                    .Deserialize<global::Fdp.Toolkit.Tkb.Domain.WeaponCapabilitiesDto>(
                        jsonElement,
                        global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });

        global::Fdp.Toolkit.Tkb.TkbDescriptorRegistry.RegisterParser(
            "AI.BehaviorProfile",
            static (template, partId, jsonElement) =>
            {
                var dto = global::System.Text.Json.JsonSerializer
                    .Deserialize<global::Fdp.Toolkit.Tkb.Domain.BehaviorProfileDto>(
                        jsonElement,
                        global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
                template.AddDescriptor(dto, partId);
            });
    }
}
```

---

### Example 3 -- Duplicate name detection (TKB001 warning)

If two types in the same assembly accidentally share the same hierarchical name,
the compiler emits diagnostic `TKB001`:

```csharp
[TkbDescriptor("Gen.VehicleParameters")]
public record VehicleParametersDto { /* ... */ }

// INCORRECT: same name as above
[TkbDescriptor("Gen.VehicleParameters")]
public record VehicleParametersV2Dto { /* ... */ }
```

**Compiler output:**

```
warning TKB001: Multiple types in assembly 'Fdp.Toolkits' share the TKB
hierarchical name 'Gen.VehicleParameters'
```

The generator emits the registration for the first type encountered and silently
drops the second (last-writer-wins would be non-deterministic; first-wins with a
warning is predictable).

---

### Example 4 -- Adding a new descriptor to a different assembly

Any project that references `Tkb.SourceGen` as an analyzer can have its own
types auto-registered. The generated class name is unique per assembly, so
there is no collision.

**Project file setup:**

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Fdp.Toolkit.Tkb.SourceGen\Tkb.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**DTO in the new assembly (`MySimModule.dll`):**

```csharp
[TkbDescriptor("MySim.SensorData")]
public record SensorDataDto
{
    public float DetectionRange { get; init; }
    public float FieldOfView    { get; init; }
}
```

**Generated file in `MySimModule`:** `__TkbDescriptors_MySimModule.g.cs`  
**Generated file in `Fdp.Toolkits`:** `__TkbDescriptors_Fdp_Toolkits.g.cs`

Both `[ModuleInitializer]` methods run at process startup, each registering their
own assembly's types. No coordination between assemblies is needed.

---

## Diagnostic Reference

| ID | Severity | Message format | When emitted |
|----|----------|----------------|-------------|
| `TKB001` | Warning | `Multiple types in assembly '{0}' share the TKB hierarchical name '{1}'` | Two or more types in the same assembly carry `[TkbDescriptor]` with the same `HierarchicalName` (case-insensitive). The first type wins; subsequent ones are skipped and this warning is emitted. |

---

## Key Implementation Details

### Attribute resolution strategy

The generator does not ship the `TkbDescriptorAttribute` type. Instead, it
resolves the attribute by fully qualified metadata name using
`Compilation.GetTypeByMetadataName(...)`. If the attribute assembly is not
referenced in the consuming project, the method returns `null` and the generator
exits silently. This means the generator is safe to reference in assemblies that
do not use TKB at all -- it produces no output and no error.

### Identifier sanitization

Assembly names can contain characters that are invalid in C# identifiers (dots,
dashes, spaces). The `SanitizeIdentifier` method replaces every character that is
not a letter, digit, or underscore with `_`. If the result starts with a digit,
a leading `_` is prepended. For example:

| Assembly name | Sanitized |
|---------------|-----------|
| `Fdp.Toolkits` | `Fdp_Toolkits` |
| `My-Sim.Module 2` | `My_Sim_Module_2` |
| `123Assembly` | `_123Assembly` |

### Deduplication order

The generator iterates over the `ImmutableArray<TypeDeclarationSyntax>` produced
by `Collect()`. The order in which types appear in the array is determined by the
order in which files are processed by the Roslyn compiler, which is generally
stable within a compilation but not guaranteed across incremental rebuilds. For
this reason:

- Duplicate detection uses a `Dictionary<string, string>` keyed on hierarchical
  name (case-insensitive).
- The first occurrence is registered; all later duplicates trigger `TKB001` and
  are dropped.
- Since duplicates are a misconfiguration, the exact winner is irrelevant -- the
  warning prompts the developer to fix the naming conflict.

### ModuleInitializer constraints

`[ModuleInitializer]` is a .NET 5+ CLR feature. It requires:
- The method must be `static`.
- The method must have no parameters and return `void`.
- The containing type must be accessible from the module (the generated class is
  `internal`, which satisfies this requirement).
- The consuming project must target `net5.0` or later (`net8.0` in this codebase).

The generator itself targets `netstandard2.0` (as required for Roslyn components)
but the *generated* code targets whatever framework the consuming project uses.
Because the consuming projects target `net8.0`, the generated `[ModuleInitializer]`
is fully supported.

### `static` lambda keyword

All parser lambdas in the generated code use the `static` modifier:

```csharp
static (template, partId, jsonElement) => { ... }
```

This prevents accidental closure capture and avoids delegate allocation on repeated
calls. Since the generator controls the emitted code, it can always guarantee the
lambdas are capture-free.

---

## Best Practices

### For DTO authors

1. **Use dot-separated hierarchical names** that reflect the domain category, e.g.
   `"Gen.VehicleParameters"`, `"Combat.WeaponSuite"`, `"AI.BehaviorProfile"`.
   The name appears verbatim as a JSON key in entity template files.

2. **One type per name.** The `[TkbDescriptor]` attribute allows `AllowMultiple = false`
   and the generator enforces uniqueness. Versioning strategies (e.g. `V2`) must
   use a distinct name, not replace the old one in the same assembly.

3. **Do not use `'#'` in names.** The `#` character is reserved as a part ID
   delimiter by `TkbDeserializer` and is rejected by `TkbDescriptorAttribute`'s
   constructor. The restriction is enforced at runtime (attribute construction) and
   implicitly at compile time (if the attribute constructor throws during compilation,
   the build fails).

4. **Prefer `record` types for DTOs.** Records are immutable by default, which is
   appropriate for data that is loaded once at startup and treated as read-only
   configuration.

### For project maintainers

5. **Reference as analyzer only.** When adding `Tkb.SourceGen` to a new consuming
   project, always use:
   ```xml
   <ProjectReference Include="...\Tkb.SourceGen.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```
   Omitting `OutputItemType="Analyzer"` makes the generator run as a plain
   project reference and the generator will not execute. Omitting
   `ReferenceOutputAssembly="false"` causes the consuming assembly to carry a
   runtime dependency on the generator assembly, which is unnecessary.

6. **Do not call `Register()` manually.** The `[ModuleInitializer]` ensures the
   CLR calls it automatically. A manual call would result in double registration
   (last-write-wins semantics of the dictionary mean the second call is idempotent,
   but it is still unnecessary and confusing).

7. **Use `TkbDescriptorRegistry.Clear()` only in tests.** The `Clear()` method is
   `internal` and intended exclusively for test isolation. It must not be called
   in production code. The registry is designed to be populated once at startup
   and read-only thereafter.

8. **Avoid modifying `TkbDescriptorAttributeMetadataName`.** The constant must
   match the production attribute's fully qualified name exactly. Changing it
   (e.g. to test a different attribute) would silently disable all generation.

---

## Related Projects

```
+----------------------------------+
| Fdp.Toolkit.Tkb.SourceGen        |
| (this project)                   |
| - TkbDescriptorGenerator.cs      |
| - IIncrementalGenerator          |
+----------------------------------+
         |
         | references (compile-time)
         v
+----------------------------------+
| Microsoft.CodeAnalysis.CSharp    |
| (Roslyn SDK NuGet package)       |
+----------------------------------+

         ^ consumed by (as Analyzer)
         |
+----------------------------------+
| Fdp.Toolkits                     |
| - TkbDescriptorAttribute.cs      |  <-- trigger attribute
| - TkbDescriptorRegistry.cs       |  <-- runtime registry
| - TkbDeserializer.cs             |  <-- reads registry at runtime
| - Domain/TkbMasterDto.cs         |  <-- [TkbDescriptor("TkbMaster")]
| - Domain/VehicleParametersDto.cs |  <-- [TkbDescriptor("Gen.VehicleParameters")]
| - Domain/WeaponCapabilitiesDto.cs|  <-- [TkbDescriptor("Gen.WeaponCapabilities")]
| - Domain/AmmoWeaponBallisticsDto |  <-- [TkbDescriptor("Gen.AmmoWeaponBallistics")]
| - Domain/BehaviorProfileDto.cs   |  <-- [TkbDescriptor("AI.BehaviorProfile")]
| - Domain/CombatPlatformDefDto.cs |  <-- [TkbDescriptor("Combat.PlatformDef")]
| - Domain/SensorCapabilitiesDto.cs|  <-- [TkbDescriptor("Perception.SensorCapabilities")]
| - Domain/UnitCompositionDto.cs   |  <-- [TkbDescriptor("Gen.UnitComposition")]
| - Domain/VisualDefinitionDto.cs  |  <-- [TkbDescriptor("IG.VisualDef")]
| - Domain/WeaponSuiteDto.cs       |  <-- [TkbDescriptor("Combat.WeaponSuite")]
+----------------------------------+

         ^ test reference
         |
+----------------------------------+
| Fdp.Toolkits.Tests               |
| - TkbDescriptorGeneratorTests.cs |  <-- in-process Roslyn compilation tests
| - TkbDescriptorRegistryTests.cs  |  <-- registry unit tests
| - TkbDeserializerTests.cs        |  <-- end-to-end JSON parsing tests
| - TkbDescriptorAttributeTests.cs |  <-- attribute validation tests
+----------------------------------+
```

### Sister project: `Fdp.Toolkits.Analyzers`

`Fdp.Toolkits.Analyzers` is the other source generator project in the Toolkits
folder. It follows the same `IIncrementalGenerator` pattern and is referenced via
the same `OutputItemType="Analyzer"` mechanism. `Fdp.Toolkits.Analyzers` targets
gizmo registration (the `GizmoRegistrarGenerator`), while `Tkb.SourceGen` targets
TKB descriptor registration. The two are independent and do not share code.

---

## Testing Strategy

Tests for this generator live in `Fdp.Toolkits.Tests/Tkb/TkbDescriptorGeneratorTests.cs`.
Because source generators run inside the compiler, tests use an in-process Roslyn
compilation approach: they compile a small synthetic C# snippet (including a stub
of `TkbDescriptorAttribute` with the correct FQN) using `CSharpCompilation.Create`,
then invoke `CSharpGeneratorDriver.Create(generator).RunGeneratorsAndUpdateCompilation(...)`,
and assert on the generated source text or emitted diagnostics.

This approach:
- Is fully self-contained (no file system, no project loading).
- Runs in the same process as the test runner (fast).
- Does not require the generator to be installed as an analyzer; the generator
  class is instantiated directly.
- Can test both positive cases (output shape) and negative cases (TKB001 warning,
  no output when no decorated types are present).

Test cases covered:

| Test | Assertion |
|------|-----------|
| Single decorated type | Exactly 1 generated tree; contains `RegisterParser`, the hierarchical name string, and the FQN of the DTO type |
| Single decorated type | Generated tree contains `[ModuleInitializer]` and `internal static void Register` |
| No decorated types | 0 generated trees |
| Duplicate hierarchical name | Diagnostic `TKB001` with `Warning` severity |
| Multiple distinct decorated types | All three hierarchical names present in the single generated tree |
