# BATCH-09 Instructions

**Branch:** `blueprints`
**Workspace root:** `d:\WORK\IOS-IG-SimHost-FDP`

**Scope:** TASK-CP-000 (Catalog interface stubs) and TASK-CP-001 (Compiler infrastructure
+ IR data model skeleton). These are foundational: no stage logic implemented, only the
full type hierarchy, stubs, and project structure.

**Design references:**
- `.dev/blueprints-1/TASK-DETAIL.md` sections TASK-CP-000, TASK-CP-001
- `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design.md` §1 (architecture),
  §2 (pipeline overview), §3 (IR data model)
- `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md`
  Patch 1 (SiblingSignatures, not SiblingAssets), Q-18.1 (IrOp_ReadInstanceVersion),
  Q-18.4 (class name convention)

---

## Important Existing Context

### What already exists

Before writing any code, read these existing files:
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintCompiler.cs` — stub to replace
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/InMemoryRoslynCompiler.cs` — stub to replace
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/BlueprintAsset.cs` — existing asset types
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj` — check project refs
- `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` — check for any Blueprints/ path includes
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` — FNV hash used in `BlueprintIdHash`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/BlueprintDispatchKind.cs` — existing enum

Check what already exists under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/`
before creating any files — there may be stubs from Phase 0/1 work.

### Note on BlueprintDispatchKind duplication (DEBT-013)

`BlueprintDispatchKind` exists in BOTH `Hrot.Blueprints.Core.Assets` and
`Fdp.Toolkit.Blueprints`. The compiler should use `Hrot.Blueprints.Core.Assets.BlueprintDispatchKind`
(the asset model's version, which lives in the compiler's own namespace group).

### Note on CompileOptions (Patch 1 override)

The design doc §1.2 has `IReadOnlyList<BlueprintAsset> SiblingAssets` in `CompileOptions`.
**Patch 1 supersedes this:** use `IReadOnlyList<BlueprintSignature> SiblingSignatures`
instead. `BlueprintAsset` is NOT in `CompileOptions`.

---

## TASK-CP-000 — Catalog Interface Stubs

### New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/CatalogInterfaces.cs`

```csharp
namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed record EngineEventCatalogEntry(string Name, Type EventType);

public sealed record ChannelCommandCatalogEntry(
    string Name, Type ChannelType, ushort ActionId, Type ParamsType);

public enum WaitKind { Channel, Event, RingBufferResult }

public sealed record WaitPrimitiveCatalogEntry(
    string Name, WaitKind Kind, Type TargetType);

public interface IEngineEventCatalog
{
    IReadOnlyList<EngineEventCatalogEntry> GetEntries();
}

public interface IChannelCommandCatalog
{
    IReadOnlyList<ChannelCommandCatalogEntry> GetEntries();
}

public interface IWaitPrimitiveCatalog
{
    IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries();
}
```

### New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/INodeRegistry.cs`

Create a minimal `INodeRegistry` interface (stub). The full implementation is Phase 3 CP-005:

```csharp
namespace Hrot.Blueprints.Core.Compiler.Catalogs;

public interface INodeRegistry
{
    // Populated in TASK-CP-005
}
```

### New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/ITypeRegistry.cs`

```csharp
namespace Hrot.Blueprints.Core.Compiler.Catalogs;

using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Assets;

public interface ITypeRegistry
{
    bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType);
    bool TryGetCoercion(IrTypeRef from, IrTypeRef to, out string coercionExpression);
}
```

Note: `BlueprintTypeRef` is a type in `Hrot.Blueprints.Core.Assets`. Check if it already
exists; if not, create a minimal stub record with a `string Name` property.

### Catalog implementations in Fdp.Toolkits

Create stub implementations only (no real engine type references yet -- that requires knowing
what event/command types exist):

**File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInEngineEventCatalog.cs`**
**File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInChannelCommandCatalog.cs`**
**File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInWaitPrimitiveCatalog.cs`**

Each can return `new List<T>()` (empty) as a stub. The TASK-DETAIL.md §TASK-CP-000 has
the concrete sample code if the actual engine event types are available via existing
`using` references.

**Important:** Before attempting to reference engine types like `HitEvent`, `BehaviorFinishedEvent`,
etc., verify they exist in the codebase with:
```
grep_search "class HitEvent" or "class BehaviorFinishedEvent"
```
If they exist, use the concrete references per TASK-DETAIL.md. If not, use empty lists.

---

## TASK-CP-001 — Compiler Infrastructure + IR Data Model

### Directory structure to create

Under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/`:

```
Compiler/
├── BlueprintCompiler.cs                 # replace existing stub
├── CompileOptions.cs
├── CompileResult.cs
├── BlueprintSignature.cs               # Patch 1 type
├── Diagnostics/
│   ├── Diagnostic.cs
│   ├── DiagnosticCodes.cs
│   └── DiagnosticSink.cs
├── Ir/
│   ├── IrAsset.cs
│   ├── IrGraph.cs
│   ├── IrBlock.cs
│   ├── IrStatement.cs
│   ├── IrValue.cs
│   ├── IrOperation.cs
│   ├── IrTypeRef.cs
│   └── IrDebugAnnotation.cs
├── Stages/
│   ├── Stage1_Parse.cs                 # stub (throw NotImplementedException)
│   ├── Stage2_Validate.cs              # stub
│   ├── Stage3_Normalize.cs             # stub
│   ├── Stage4_TypeResolve.cs           # stub
│   ├── Stage5_Schedule.cs              # stub
│   ├── Stage6_Lower.cs                 # stub
│   └── Stage7_Emit.cs                  # stub
├── Lowering/
│   ├── LibraryLowering.cs              # stub
│   ├── AiPrimitiveLowering.cs          # stub
│   ├── InstanceLowering.cs             # stub
│   ├── WaitLowering_AiPrimitive.cs     # stub
│   └── WaitLowering_Instance.cs        # stub
├── Emit/
│   ├── CSharpEmitter.cs                # stub
│   ├── EmissionContext.cs              # stub
│   ├── Sanitizer.cs                    # IMPLEMENT FULLY (see below)
│   └── DebugMapBuilder.cs              # stub
├── Roslyn/
│   ├── InMemoryRoslynCompiler.cs       # minimal impl with package ref (see below)
│   ├── MetadataReferenceResolver.cs    # stub
│   └── EmbeddedTextHelper.cs          # stub
└── Determinism/
    ├── DeterministicEnumerable.cs      # stub
    └── FnvHasher.cs                    # IMPLEMENT FULLY (see below)
```

### Types to implement fully (not just stubs)

#### 1. `FnvHasher.cs` (in `Determinism/`)

FNV-1a hash — deterministic, no .NET hash seed:

```csharp
namespace Hrot.Blueprints.Core.Compiler.Determinism;

public static class FnvHasher
{
    private const uint Fnv32Prime   = 16777619u;
    private const uint Fnv32Offset  = 2166136261u;
    private const ulong Fnv64Prime  = 1099511628211UL;
    private const ulong Fnv64Offset = 14695981039346656037UL;

    public static uint Hash32(ReadOnlySpan<byte> data)
    {
        uint hash = Fnv32Offset;
        foreach (var b in data)
            hash = (hash ^ b) * Fnv32Prime;
        return hash;
    }

    public static ulong Hash64(ReadOnlySpan<byte> data)
    {
        ulong hash = Fnv64Offset;
        foreach (var b in data)
            hash = (hash ^ b) * Fnv64Prime;
        return hash;
    }
}
```

#### 2. `BlueprintIdHash.Compute(Guid)` (update/extend existing location)

Check if `BlueprintIdHash.cs` already exists (it should, used in tests). If it exists and
uses FNV-1a, leave it. If it doesn't exist, create it in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/`:

```csharp
namespace Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Compiler.Determinism;

public static class BlueprintIdHash
{
    public static int Compute(Guid assetId)
    {
        Span<byte> bytes = stackalloc byte[16];
        assetId.TryWriteBytes(bytes);
        return (int)FnvHasher.Hash32(bytes);
    }
}
```

#### 3. `Sanitizer.cs` (in `Emit/`)

```csharp
namespace Hrot.Blueprints.Core.Compiler.Emit;

public static class Sanitizer
{
    /// <summary>
    /// Convert a Blueprint name to a C# identifier.
    /// E.g. "Move To And Fire" → "MoveToAndFire"
    /// </summary>
    public static string SanitizeName(string name)
    {
        var sb = new System.Text.StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }
        return sb.Length > 0 ? sb.ToString() : "UnknownBlueprint";
    }

    /// <summary>
    /// E.g. "MoveToAndFire" + 0xA1B2C3D4 + false → "MoveToAndFire_A1B2C3D4_Bp.g.cs"
    ///                                   + true  → "BlueprintRegistrar_MoveToAndFire_A1B2C3D4_Bp.g.cs"
    /// Per Q-18.4 class name: {SanitizedName}_{BlueprintId:X8}_Bp
    /// </summary>
    public static string GeneratedFileName(string sanitizedName, int blueprintId, bool isRegistrar)
    {
        return isRegistrar
            ? $"BlueprintRegistrar_{sanitizedName}_{blueprintId:X8}_Bp.g.cs"
            : $"{sanitizedName}_{blueprintId:X8}_Bp.g.cs";
    }
}
```

#### 4. Full IR type hierarchy (in `Ir/`)

Implement ALL types from Compiler DD §3. Every type must compile. Key notes:
- All IR types are `record` or `sealed record` for structural equality + immutability.
- `IrValue` is `readonly record struct`.
- `IrBlockId` is `readonly record struct`.
- Add `IrOp_ReadInstanceVersion` to the operation hierarchy (Q-18.1):
  ```csharp
  public sealed record IrOp_ReadInstanceVersion : IrOperation;
  ```
- Put all operations in one file `IrOperation.cs` as an abstract record hierarchy.

#### 5. `DiagnosticCodes.cs`

Create `DiagnosticCodes` as a static class with `public const string` fields for all codes.
The critical ones from the design doc:

```csharp
namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public static class DiagnosticCodes
{
    // Stage 1 — Parse
    public const string BP0001_NullAsset     = "BP0001";
    public const string BP0002_JsonParseError = "BP0002";
    public const string BP0010_EmptyAssetId  = "BP0010";
    public const string BP0011_EmptyName     = "BP0011";

    // Stage 2 — Validate
    public const string BP1010 = "BP1010";
    public const string BP1011 = "BP1011";
    // ... (add BP1100, BP1101, BP1200, BP1201, BP1210, BP1211, BP1300, BP1301, BP1302)
    // ... (add BP1500 through BP1503)

    // Stage 3 — Normalize
    public const string BP2001 = "BP2001";
    public const string BP2002 = "BP2002";
    public const string BP2003 = "BP2003";

    // Stages 4-8
    public const string BP3001 = "BP3001";
    public const string BP4001 = "BP4001";
    public const string BP4002 = "BP4002";
    public const string BP4003 = "BP4003";
    public const string BP4004 = "BP4004";
    public const string BP5001 = "BP5001";
    public const string BP6001 = "BP6001";
    public const string BP7001 = "BP7001";

    // Internal compiler errors
    public const string BP9001 = "BP9001";
}
```

Add all codes mentioned in the design doc §2.2 and referenced throughout stages.

#### 6. `Diagnostic.cs` and `DiagnosticSink.cs`

```csharp
// Diagnostic.cs
namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public static Diagnostic Error(string code, string message)
        => new(DiagnosticSeverity.Error, code, message);
    public static Diagnostic Warning(string code, string message)
        => new(DiagnosticSeverity.Warning, code, message);
    public static Diagnostic Info(string code, string message)
        => new(DiagnosticSeverity.Info, code, message);
}
```

```csharp
// DiagnosticSink.cs
namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public sealed class DiagnosticSink
{
    private readonly List<Diagnostic> _diagnostics = new();

    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public IReadOnlyList<Diagnostic> All => _diagnostics;
}
```

#### 7. Full `BlueprintCompiler.cs` (replace the existing stub)

Implement the full API surface with stubs that throw:

```csharp
namespace Hrot.Blueprints.Core.Compiler;

public interface IBlueprintCompiler
{
    CompileResult Compile(BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null);
}

public sealed class BlueprintCompiler : IBlueprintCompiler
{
    public CompileResult Compile(BlueprintAsset asset, CompileOptions options)
        => throw new NotImplementedException("Compiler Stage 1-8 not yet implemented (Phase 3).");

    public ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null)
        => throw new NotImplementedException("Compiler Stage 2 not yet implemented (Phase 3).");
}
```

Update the existing top-level `BlueprintCompiler` class (not in Compiler/ subfolder) to delegate
to this new one, OR consolidate them. The cleanest approach: move the `BlueprintCompiler` class
from the root to `Compiler/` and update the `using` statements in tests.

Check if `BlueprintCompiler` in the root namespace is referenced in tests with a specific
namespace qualifier. If the existing `BlueprintTestFixture` uses `new BlueprintCompiler()`
without a namespace, the move must be backward-compatible (either add a using alias or keep
a forwarding class in the old namespace).

#### 8. `CompileOptions.cs` (Patch 1 form)

```csharp
namespace Hrot.Blueprints.Core.Compiler;

using Hrot.Blueprints.Core.Compiler.Catalogs;

public sealed record CompileOptions(
    CompilerMode Mode,
    INodeRegistry NodeRegistry,
    ITypeRegistry TypeRegistry,
    IEngineEventCatalog EngineEvents,
    IChannelCommandCatalog ChannelCommands,
    IWaitPrimitiveCatalog WaitPrimitives,
    IReadOnlyList<BlueprintSignature> SiblingSignatures,     // Patch 1: NOT SiblingAssets
    bool EmitPdbWithEmbeddedSource = false,
    string? VirtualSourcePath = null);

public enum CompilerMode { Release, Debug, Trace }
```

#### 9. `BlueprintSignature.cs` (Patch 1 type)

```csharp
namespace Hrot.Blueprints.Core.Compiler;

using Hrot.Blueprints.Core.Assets;

public sealed record BlueprintSignature(
    string Path,
    Guid AssetId,
    string Name,
    string SanitizedName,
    int BlueprintId,
    BlueprintDispatchKind Dispatch,
    IReadOnlyList<string> ExportedFunctionNames,
    IReadOnlyList<AiPrimitiveHosting> Hostings,
    IReadOnlyList<Guid> DeclaredCallablePeers);
```

#### 10. `InMemoryRoslynCompiler.cs` (move from root, update signature)

Replace the existing stub in the root with a proper compiler class in `Compiler/Roslyn/`.
Keep the root stub as a forwarding alias (or update all usages).

The new `InMemoryRoslynCompiler` in `Compiler/Roslyn/` should:
- Accept `(string source, string virtualSourcePath, string assemblyName, DiagnosticSink sink)` 
- Currently: throw `NotImplementedException("Phase 3 CP-005")` 

The existing `InMemoryRoslynCompiler` in the root was used by `BlueprintTestFixture.CompileAndLoadMany`.
Update the test fixture to use either the old stub or a simple bridge.

---

## Nuget packages needed

Check `Hrot.Blueprints.Core.csproj` for existing package references. The compiler will
eventually need `Microsoft.CodeAnalysis.CSharp` but that's for CP-005. For BATCH-09,
no new packages are needed. The compiler types (IR records, stubs, FNV hasher) are pure C#.

If `Microsoft.CodeAnalysis.CSharp` is NOT already in the csproj, do NOT add it yet.
CP-005 adds it.

---

## Success criteria

1. `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 warnings.
2. `dotnet test Hrot/.../Hrot.Blueprints.Tests.csproj --no-build` → 160 pass, 3 skip, 0 fail
   (all existing tests must continue passing; no new tests in this batch).
3. `BlueprintIdHash.Compute` method exists and compiles.
4. `FnvHasher.Hash32(guid.ToByteArray()) == FnvHasher.Hash32(guid.ToByteArray())` —
   verify determinism by inspection (no test needed yet).
5. `Sanitizer.GeneratedFileName("MoveToAndFire", 0xA1B2C3D4, false)` should return
   `"MoveToAndFire_A1B2C3D4_Bp.g.cs"` (verified by test in BATCH-09 if easy, otherwise
   deferred to CP-006 test suite).
6. `DiagnosticCodes.BP0001_NullAsset` and other critical constants are declared.
7. All IR types compile (IrAsset, IrGraph, IrBlock, IrStatement, IrValue, IrOperation
   hierarchy with all 30+ ops including `IrOp_ReadInstanceVersion`).
8. `CompileOptions.SiblingSignatures` property exists; `SiblingAssets` property does NOT exist.

---

## Output

Write completion report to `.dev/blueprints-1/reports/BATCH-09-REPORT.md` with:
- List of files created/modified
- Any deviations from this instruction file
- Answers: 
  1. Did existing `BlueprintTestFixture.CompileAndLoadMany` require changes to compile?
  2. Was `BlueprintIdHash.Compute` already present before this batch?
  3. Were any Roslyn packages already in the `.csproj`?
