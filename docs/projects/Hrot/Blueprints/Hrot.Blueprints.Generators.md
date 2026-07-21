# Hrot.Blueprints.Generators

> Hand-synced 2026-07-21 to match shipped state; regenerate to fully refresh.

**Path**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/`
**Date**: 2026-05-23
**Target Framework**: `netstandard2.0`
**Generator Attribute**: `[Generator(LanguageNames.CSharp)]`

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder
(`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/`).
There is no separate README to be out of date or diverged.

---

## Executive Overview

`Hrot.Blueprints.Generators` is a **Roslyn incremental source generator** that compiles
Blueprint visual-script assets (`.bp.json` files) into C# source code at **MSBuild
analysis time**.

### What Is Generated

For every `.bp.json` file registered as an `<AdditionalFiles>` item in a consuming
project, the generator produces one `.g.cs` file containing:

- A **Blueprint class** (`{SanitizedName}_{BlueprintId:X8}_Bp`) whose exact shape
  depends on the asset's dispatch kind:
  - `Library` -- static helper class with one `static` method per exported graph
    function.
  - `AiPrimitive` -- static class with a sequential `Params` struct, a sequential
    `WorkingState` struct, an `InitDefaultWorkingState` method, a `TickCore` method,
    and unsafe thunk delegates compatible with the behavior-tree / HSM runtimes.
  - `Instance` -- static class with a sequential `State` struct (first field is a
    `BlueprintLatentCursor`), a nested `VarIds` string-constant class, an
    `InitDefaultState` method, a `Tick` method, event-handler methods, tick and event
    thunk delegates.
- A **registrar class** (`BlueprintRegistrar_{SanitizedName}_{BlueprintId:X8}_Bp`)
  decorated with `[BlueprintRegistrar]` that registers the Blueprint in the runtime
  staging registry.

### What Triggers Generation

Generation is triggered automatically whenever:
1. A `.bp.json` file is added/modified in a project that references
   `Hrot.Blueprints.Generators` as an `<Analyzer>` reference.
2. A sibling `.bp.json` asset that is referenced by `callablePeers` is modified
   (because the sibling catalog changes and invalidates the downstream compile).

The generator is **incremental**: unchanged assets skip the full compilation pipeline
and reuse cached `CompileResult` values stored by the Roslyn incremental infrastructure.

---

## Architecture

### High-Level Flow

```
+---------------------------+
|  MSBuild / Roslyn Host    |
|  (IDE or dotnet build)    |
+----------+----------------+
           |
           |  AdditionalTexts  (*.bp.json)
           v
+---------------------------+
|  BlueprintIncrementalGen  |
|  (IIncrementalGenerator)  |
|                           |
|  Provider 1: rawFiles     |  --> (Path, Text) per *.bp.json
|  Provider 2: signatures   |  --> BlueprintSignature (lightweight)
|  Provider 3: siblingCatalog| --> ImmutableArray<BlueprintSignature>
|  Provider 4: compileResults| --> CompileResult per asset
+----------+----------------+
           |
           |  RegisterSourceOutput
           v
+---------------------------+
|  Roslyn Source Output     |
|  spc.AddSource(...)       |
|  spc.ReportDiagnostic(...)| --> BP0001..BP3xxx errors/warnings
+---------------------------+
           |
           v
+---------------------------+
|  Generated *.g.cs files   |
|  in consuming project     |
+---------------------------+
```

### Incremental Pipeline Detail

```
AdditionalTextsProvider
  | .Where(*.bp.json)
  | .Select(read text)
  v
rawFiles  (Path, Text)
  |
  +-- .Select(BlueprintSignatureParser.Parse)
  |         v
  |   signatures  (BlueprintSignature per file)
  |         |
  |         +-- .Collect()
  |               v
  |         siblingCatalog  (ImmutableArray<BlueprintSignature>)
  |
  +-- .Combine(siblingCatalog)
        | .Select(CompileOneAsset)
        v
  compileResults  (CompileResult per file)
        |
        +-- .RegisterSourceOutput
              |
              +-- success: spc.AddSource(fileName, generatedSource)
              +-- failure: spc.ReportDiagnostic(...)
```

### Compilation Pipeline (inside `CompileOneAsset`)

```
  JSON text
      |
      v
+---------------+
| Stage 1 Parse |  BlueprintJsonServices.Deserialize
+-------+-------+
        | BlueprintAsset
        v
+--------------------+
| Stage 0 Rehydrate  |  reflection-free pin/link reconstruction; mirrors editor's NodePinSchema
+--------+-----------+
        |
        v
+--------------------+
| Stage 2 Validate   |  17 validators (V_AssetStructure, V_NodeStructure, ..., V_WhenNodeRules, V_ReadEqsResultNodeRules, V_SpawnEqsSensorNodeRules)
+--------+-----------+
         | validated asset
         v
+--------------------+
| Stage 3 Normalize  |  implicit casts, default literals, orphan elimination
+--------+-----------+
         | normalized asset
         v
+----------------------+
| Stage 4 TypeResolve  |  two-pass wildcard resolution, IrTypeRef mapping
+--------+-------------+
         | TypedAsset
         v
+--------------------+
| Stage 5 Schedule   |  per-graph GraphScheduler -> IrAsset
+--------+-----------+
         | IrAsset
         v
+-------------------+
| Stage 6 Lower     |  dispatch-specific lowering, FieldLayout, StructureHash
+--------+----------+
         | final IrAsset
         v
+------------------+
| Stage 7 Emit     |  CSharpEmitter -> string C# source + DebugMap
+--------+---------+
         |
         v
  CompileResult (GeneratedSource, GeneratedFileName, Diagnostics, ...)
```

### Dispatch-Specific Output Shape

```
+-------------------+       +---------------------------------------------+
|  Library          |------>|  static class {Name}_{Id:X8}_Bp              |
|  dispatch         |       |    const int BlueprintId                      |
+-------------------+       |    static ReturnType FunctionName(...)        |
                            |    ...one method per exported Function graph  |
                            +---------------------------------------------+

+-------------------+       +---------------------------------------------+
|  AiPrimitive      |------>|  static class {Name}_{Id:X8}_Bp              |
|  dispatch         |       |    const int BlueprintId                      |
+-------------------+       |    const ulong StructureHash                 |
                            |    [StructLayout(Sequential)]                 |
                            |    struct Params { ... }                      |
                            |    [StructLayout(Sequential)]                 |
                            |    struct WorkingState { ... }                |
                            |    static unsafe void InitDefaultWorkingState |
                            |    static unsafe NodeStatus TickCore(...)    |
                            |    static unsafe delegate thunks             |
                            +---------------------------------------------+

+-------------------+       +---------------------------------------------+
|  Instance         |------>|  static class {Name}_{Id:X8}_Bp              |
|  dispatch         |       |    const int BlueprintId                      |
+-------------------+       |    const ulong StructureHash                 |
                            |    [StructLayout(Sequential)]                 |
                            |    struct State { Cursor; variables... }      |
                            |    static class VarIds { string consts }      |
                            |    static int StateSize                       |
                            |    static unsafe void InitDefaultState        |
                            |    static unsafe void Tick(...)               |
                            |    static unsafe void On{EventName}(...)      |
                            |    static unsafe delegate thunks             |
                            +---------------------------------------------+
```

---

## Source Structure

The generator project contains exactly **one source file**.

### `BlueprintIncrementalGenerator.cs`

**Namespace**: `Hrot.Blueprints.Generators`
**Class**: `BlueprintIncrementalGenerator`
**Implements**: `IIncrementalGenerator`
**Attribute**: `[Generator(LanguageNames.CSharp)]`

#### `Initialize(IncrementalGeneratorInitializationContext context)`

Sets up the four-provider incremental pipeline:

| Provider | Type | Purpose |
|---|---|---|
| `rawFiles` | `IncrementalValuesProvider<(string Path, string Text)>` | Filter `AdditionalTextsProvider` to `*.bp.json` and read text |
| `signatures` | `IncrementalValuesProvider<BlueprintSignature>` | Lightweight header parse for cross-reference resolution |
| `siblingCatalog` | `IncrementalValueProvider<ImmutableArray<BlueprintSignature>>` | Collect all signatures into one array |
| `compileResults` | `IncrementalValuesProvider<CompileResult>` | Full per-asset compile (Combine rawFile with siblingCatalog) |

Calls `context.RegisterSourceOutput` on `compileResults`:
- On success: `spc.AddSource(result.GeneratedFileName, result.GeneratedSource)`.
- On failure: iterates `result.Diagnostics` and calls `spc.ReportDiagnostic(...)`.

#### `CompileOneAsset(string path, string text, ImmutableArray<BlueprintSignature> siblings, CancellationToken ct)` (private static)

1. Checks `ct.ThrowIfCancellationRequested()`.
2. Calls `BlueprintJsonServices.Deserialize(text)` to obtain a `BlueprintAsset`.
3. Returns `FailedParse(path)` if deserialization fails or returns null.
4. Creates a `BpCompiler` (`BlueprintCompiler`) and a `CompileOptions` record wired to
   the built-in catalogs and the current `siblings` list.
5. Calls `compiler.Compile(asset, options)` and returns the `CompileResult`.
6. Wraps any unexpected exception in a `CompileResult` with `BP0002_JsonParseError`.

#### `FailedParse(string path)` (private static)

Returns a failed `CompileResult` carrying one `BP0002_JsonParseError` diagnostic.
Used as the error path for JSON deserialization failures.

#### `ToRoslynDiagnostic(BpDiagnostic diag)` (private static)

Converts a `Hrot.Blueprints.Core.Compiler.Diagnostics.Diagnostic` record to a
`Microsoft.CodeAnalysis.Diagnostic` by constructing a `DiagnosticDescriptor` with
`category: "Blueprints"`. Error diagnostics map to `DiagnosticSeverity.Error`;
warnings map to `DiagnosticSeverity.Warning`.

---

## Public API Reference

### Generator Entry Point

| Member | Description |
|---|---|
| `BlueprintIncrementalGenerator` | The sole generator class; registered via `[Generator(LanguageNames.CSharp)]` |
| `Initialize(...)` | The IIncrementalGenerator entry point called once per compilation session |

### Trigger Mechanism (MSBuild)

To activate the generator in a consuming project, two MSBuild items are required:

```xml
<!-- Reference the generator assembly as an analyzer -->
<ItemGroup>
  <ProjectReference Include="..\Hrot.Blueprints.Generators\Hrot.Blueprints.Generators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<!-- Register each .bp.json file as an AdditionalFile -->
<ItemGroup>
  <AdditionalFiles Include="Assets\**\*.bp.json" />
</ItemGroup>
```

Without the `AdditionalFiles` item, no `.bp.json` files are visible to the generator
and no code is generated.

### Generated Members Reference

The table below lists generated symbols by dispatch kind.

#### Library

| Symbol | Kind | Description |
|---|---|---|
| `{Name}_{Id}_Bp` | static class | Container for all exported functions |
| `BlueprintId` | `const int` | FNV-32 hash of the asset GUID |
| `{FunctionName}(...)` | static method | One per exported `Function`-kind graph |

#### AiPrimitive

| Symbol | Kind | Description |
|---|---|---|
| `{Name}_{Id}_Bp` | static class | Root container |
| `BlueprintId` | `const int` | FNV-32 hash of the asset GUID |
| `StructureHash` | `const ulong` | Structural hash for runtime compatibility checks |
| `Params` | `[StructLayout(Sequential)] struct` | Blueprint parameters (tunable from outside) |
| `WorkingState` | `[StructLayout(Sequential)] struct` | Per-primitive tick state |
| `InitDefaultWorkingState(WorkingState*)` | `static unsafe void` | Writes zero + non-zero defaults |
| `TickCore(...)` | `static unsafe NodeStatus` | Main tick logic |
| `*_Thunk` | `static unsafe delegate*` | Low-level function pointers for BTree/HSM runtimes |
| `BlueprintRegistrar_*` | static class + `[BlueprintRegistrar]` | Self-registration in the runtime staging |

#### Instance

| Symbol | Kind | Description |
|---|---|---|
| `{Name}_{Id}_Bp` | static class | Root container |
| `BlueprintId` | `const int` | FNV-32 hash of the asset GUID |
| `StructureHash` | `const ulong` | Structural hash |
| `State` | `[StructLayout(Sequential)] struct` | Per-instance state starting with `BlueprintLatentCursor` |
| `VarIds` | nested static class | String constants for each variable (GUID strings) |
| `StateSize` | `static int` property | `Unsafe.SizeOf<State>()` |
| `InitDefaultState(State*)` | `static unsafe void` | Writes zero + non-zero defaults |
| `Tick(...)` | `static unsafe void` | Main per-frame execution |
| `On{EventName}(...)` | `static unsafe void` | One per Event-kind graph |
| `Tick_Thunk`, `On*_Thunk` | function pointer fields | Low-level thunk delegates |
| `BlueprintRegistrar_*` | static class + `[BlueprintRegistrar]` | Self-registration |

### Diagnostic Codes Emitted by the Generator Layer

| Code | Stage | Meaning |
|---|---|---|
| `BP0002` | Parse | JSON deserialization threw or returned null |

All `BP0010`-`BP3xxx` codes originate inside `BlueprintCompiler` and are forwarded
via `ToRoslynDiagnostic`.

---

## Dependencies

### NuGet Packages

| Package | Version | Note |
|---|---|---|
| `Microsoft.CodeAnalysis.CSharp` | 4.8.0 | Roslyn compiler APIs (`IIncrementalGenerator`, `AdditionalTextsProvider`, etc.) |
| `Microsoft.CodeAnalysis.Analyzers` | 3.3.4 | Roslyn analyzer rules (enforced by `EnforceExtendedAnalyzerRules`) |
| `System.Collections.Immutable` | 8.0.0 | `ImmutableArray<T>` for the sibling catalog |

All three packages use `PrivateAssets="all"` which means they are not propagated to
consumers of this generator.

### Project References

| Project | Note |
|---|---|
| `Hrot.Blueprints.Compiler` | Provides `BlueprintCompiler`, `BlueprintSignatureParser`, `CompileResult`, `CompileOptions`, all stages, and all emitters. `PrivateAssets="all"; ExcludeAssets="runtime"` so the compiler assembly is not deployed to consumers. |

### Indirect Dependencies (via Hrot.Blueprints.Compiler)

| Type | Source |
|---|---|
| `BlueprintAsset` | `Hrot.Blueprints.Core.Assets` |
| `BlueprintJsonServices` | `Hrot.Blueprints.Core` |
| `BlueprintCompiler` | `Hrot.Blueprints.Core.Compiler` |
| `CompileOptions`, `CompileResult` | `Hrot.Blueprints.Core.Compiler` |
| `BlueprintSignature`, `BlueprintSignatureParser` | `Hrot.Blueprints.Core.Compiler` |
| `BuiltInNodeRegistry` | `Hrot.Blueprints.Core.Compiler.Catalogs` |
| `StaticTypeRegistry` | `Hrot.Blueprints.Core.Compiler.Catalogs` |
| `BuiltInEngineEventCatalog` | `Hrot.Blueprints.Core.Compiler.Catalogs` |
| `BuiltInChannelCommandCatalog` | `Hrot.Blueprints.Core.Compiler.Catalogs` |
| `BuiltInWaitPrimitiveCatalog` | `Hrot.Blueprints.Core.Compiler.Catalogs` |
| `DiagnosticCodes` | `Hrot.Blueprints.Core.Compiler.Diagnostics` |

---

## Usage Examples

### Example 1 -- Library Blueprint

**Asset file** (`Assets/Shared/MathHelpers.bp.json`, simplified):
```json
{
  "assetId": "11111111-0000-0000-0000-000000000001",
  "name": "Math Helpers",
  "dispatch": "Library",
  "graphs": [
    {
      "id": "aaaa0001-...",
      "name": "Clamp",
      "kind": "Function",
      "inputs":  [{ "id": "...", "name": "Value", "typeRef": { "typeId": "float" } },
                  { "id": "...", "name": "Min",   "typeRef": { "typeId": "float" } },
                  { "id": "...", "name": "Max",   "typeRef": { "typeId": "float" } }],
      "outputs": [{ "id": "...", "name": "Result","typeRef": { "typeId": "float" } }],
      "nodes": [ ... ],
      "links": [ ... ]
    }
  ]
}
```

**Generated output** (`MathHelpers_A1B2C3D4_Bp.g.cs`):
```csharp
// <auto-generated />
// Asset: Math Helpers (11111111-0000-0000-0000-000000000001)
// BlueprintId: 0xA1B2C3D4
// StructureHash: 0x0000000000000000

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;

namespace Hrot.AI.Behaviors.Generated;

public static class MathHelpers_A1B2C3D4_Bp
{
    public const int BlueprintId = unchecked((int)0xA1B2C3D4);

    public static float Clamp(float Value, float Min, float Max)
    {
        // ... generated block body ...
    }
}

[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]
public static class BlueprintRegistrar_MathHelpers_A1B2C3D4_Bp
{
    public static void Register(global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging)
    {
        staging.Register(MathHelpers_A1B2C3D4_Bp.BlueprintId, ...);
    }
}
```

**Consumer code** (no boilerplate needed):
```csharp
// Directly call the generated static method
float clamped = MathHelpers_A1B2C3D4_Bp.Clamp(value, 0f, 1f);
```

---

### Example 2 -- AiPrimitive Blueprint (BTree Action)

**Asset file** (`Assets/AI/PatrolAction.bp.json`, simplified):
```json
{
  "assetId": "22222222-0000-0000-0000-000000000002",
  "name": "Patrol Action",
  "dispatch": "AiPrimitive",
  "primitive": {
    "intent": "Action",
    "hostings": ["BTreeAction"]
  },
  "parameters": [
    { "id": "pp01", "name": "WaypointRadius", "typeRef": { "typeId": "float" },
      "defaultValueJson": "5.0f" }
  ],
  "workingState": [
    { "id": "ws01", "name": "CurrentWaypointIndex", "typeRef": { "typeId": "int" } },
    { "id": "ws02", "name": "Phase",                "typeRef": { "typeId": "int" } }
  ],
  "graphs": [ ... ]
}
```

**Generated output** (`PatrolAction_B2C3D4E5_Bp.g.cs`):
```csharp
// <auto-generated />
// Asset: Patrol Action (22222222-0000-0000-0000-000000000002)
// BlueprintId: 0xB2C3D4E5
// StructureHash: 0xFEDCBA9876543210

namespace Hrot.AI.Behaviors.Generated;

public static class PatrolAction_B2C3D4E5_Bp
{
    public const int BlueprintId = unchecked((int)0xB2C3D4E5);
    public const ulong StructureHash = 18364758544493064720UL;

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Params
    {
        public float WaypointRadius;
    }

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct WorkingState
    {
        public int CurrentWaypointIndex;
        public int Phase;
    }

    private static unsafe void InitDefaultWorkingState(WorkingState* dst)
    {
        *dst = default;
        // WaypointRadius default is on Params not WorkingState
    }

    private static unsafe global::Hrot.Blueprints.Core.Assets.NodeStatus TickCore(
        global::Fdp.Core.EntityRepository world,
        int entityId,
        Params* p,
        WorkingState* ws)
    {
        // ... generated graph body ...
    }

    // Thunk compatible with BTree runtime dispatch
    public static unsafe delegate* <...> TickThunk = &TickCore;
}

[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]
public static class BlueprintRegistrar_PatrolAction_B2C3D4E5_Bp
{
    public static void Register(
        global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging,
        global::Fdp.Toolkit.Behavior.BehaviorRegistry behReg)
    {
        staging.Register(PatrolAction_B2C3D4E5_Bp.BlueprintId, ...);
        behReg.RegisterAction(...);
    }
}
```

---

### Example 3 -- Instance Blueprint

**Asset file** (`Assets/Entities/SoldierBehavior.bp.json`, simplified):
```json
{
  "assetId": "33333333-0000-0000-0000-000000000003",
  "name": "Soldier Behavior",
  "dispatch": "Instance",
  "variables": [
    { "id": "vv01", "name": "Health",        "typeRef": { "typeId": "float" },
      "defaultValueJson": "100.0f" },
    { "id": "vv02", "name": "AlertLevel",    "typeRef": { "typeId": "int"   } }
  ],
  "graphs": [
    { "id": "gg01", "name": "Tick",     "kind": "Function", ...  },
    { "id": "gg02", "name": "OnDamage", "kind": "Event",    ...  }
  ]
}
```

**Generated output** (`SoldierBehavior_C3D4E5F6_Bp.g.cs`):
```csharp
// <auto-generated />
// Asset: Soldier Behavior (33333333-0000-0000-0000-000000000003)
// BlueprintId: 0xC3D4E5F6
// StructureHash: 0x123456789ABCDEF0

namespace Hrot.AI.Behaviors.Generated;

public static class SoldierBehavior_C3D4E5F6_Bp
{
    public const int BlueprintId = unchecked((int)0xC3D4E5F6);
    public const ulong StructureHash = 1311768467294899695UL;

    [global::System.Runtime.InteropServices.StructLayout(
        global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct State
    {
        public global::Fdp.Toolkit.Blueprints.BlueprintLatentCursor Cursor;  // first 16 bytes
        public float Health;
        public int AlertLevel;
    }

    public static class VarIds
    {
        public const string Health     = "vv01";
        public const string AlertLevel = "vv02";
    }

    public static int StateSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<State>();

    private static unsafe void InitDefaultState(State* dst)
    {
        *dst = default;
        dst->Health = 100.0f;
    }

    public static unsafe void Tick(
        global::Fdp.Core.EntityRepository view,
        int entityId,
        State* s)
    {
        // ... generated Tick graph body ...
    }

    public static unsafe void OnDamage(
        global::Fdp.Core.EntityRepository view,
        int entityId,
        State* s)
    {
        // ... generated OnDamage graph body ...
    }

    // Tick thunk for runtime dispatch table
    public static unsafe delegate* <...> TickThunk = &Tick;
}

[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]
public static class BlueprintRegistrar_SoldierBehavior_C3D4E5F6_Bp
{
    public static void Register(global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging)
    {
        staging.Register(SoldierBehavior_C3D4E5F6_Bp.BlueprintId, ...);
    }
}
```

---

### Example 4 -- Error Reporting in the IDE

When a `.bp.json` file contains an invalid node reference, the generator surfaces the
error inline in Visual Studio / Rider as a build diagnostic:

```
error BP1021: Node 'aaaa-bbbb-...' references output pin 'out0' that does not exist.
```

The diagnostic carries no file/line location (all Blueprint errors use
`Location.None`) but the error code prefix (`BP1xxx`, `BP2xxx`, etc.) identifies the
compilation stage:

| Range | Stage |
|---|---|
| BP0001-BP0011 | Parse / asset identity |
| BP1010-BP1602 | Stage 2 structural validation |
| BP2001-BP2xxx | Stage 3 normalization |
| BP3xxx+       | Stages 4-7 type, schedule, emit |

---

## Best Practices

### Asset File Management

- Always assign a unique, stable `assetId` (GUID) when creating a `.bp.json`.
  The `BlueprintId` constant in the generated code is an FNV-32 hash of this GUID;
  changing the GUID is a **breaking API change** because all callers referencing
  the old constant will no longer resolve.
- Keep asset names short and word-separated (spaces or underscores). The
  `Sanitizer.SanitizeName` function converts them to PascalCase C# identifiers.
  Names that differ only in punctuation can collide after sanitization.

### Cross-Asset References (Sibling Catalog)

- Blueprints that call other Blueprints via `callablePeers` depend on the sibling
  catalog being complete at generation time. Always ensure that all peer `.bp.json`
  files are included as `<AdditionalFiles>` in the same project.
- If a peer is defined in a different project, it must be surfaced through a shared
  mechanism (e.g., `<AdditionalFiles>` glob across project boundaries or a central
  catalog project).

### Incremental Build Performance

- The generator is fully incremental. Modifying a single `.bp.json` file only
  recompiles that asset. However, because the sibling catalog is collected into a
  single `ImmutableArray`, changing any asset's signature (dispatch kind, exported
  functions, hostings, peer GUIDs) invalidates the full catalog and triggers
  recompilation of all assets in the same project.
- Minimize signature-affecting changes during development. Only structural changes
  (graphs that other assets call) truly need to propagate; internal implementation
  changes do not affect the signature.

### Compiler Mode

- The generator always uses `CompilerMode.Release` and
  `EmitPdbWithEmbeddedSource: false`. There is no debug-mode code path inside
  `BlueprintIncrementalGenerator`; PDB support (Stage 8) is not invoked.
- If debug information is needed for a Blueprint, it must be requested through a
  different code path that calls `Stage8_RoslynFinalize` explicitly (e.g., from the
  Blueprint Editor's offline compile path).

### Handling Diagnostics

- Treat any `BPxxxx` error as a hard build failure. The `CompileResult.Succeeded`
  flag is `false` whenever there are error-severity diagnostics; no source is emitted
  for failed assets.
- Warning diagnostics (`IsError == false`) do not block source emission. They are
  still forwarded to Roslyn and will appear in the Error List pane.

### Naming Conventions in Generated Code

- Generated class names encode both the sanitized name and the numeric `BlueprintId`
  (hex, 8 digits). This ensures that renaming an asset (which changes the sanitized
  portion but not the hash) produces a compile error at all call sites, making
  renames detectable rather than silently breaking behavior.
- The registrar class is always `internal` to the generated namespace
  `Hrot.AI.Behaviors.Generated`, preventing accidental direct calls from application
  code.

---

## Related Projects

### Direct Dependency

| Project | Relationship |
|---|---|
| [Hrot.Blueprints.Compiler](Hrot.Blueprints.Compiler.md) | Provides the entire compilation pipeline (`BlueprintCompiler`, all 7 stages, all emitters, all catalogs). The generator is a thin Roslyn wrapper around this library. |

### Sibling Subsystem Projects

| Project | Relationship |
|---|---|
| [Hrot.Blueprints.Core](Hrot.Blueprints.Core.md) | Defines `BlueprintAsset`, `Graph`, `Node`, `Link`, `Pin`, `BlueprintDispatchKind`, and all asset-model types consumed by the compiler. |
| `Hrot.Blueprints.Editor` | The visual-script editor that creates and edits `.bp.json` files. Also uses `BlueprintCompiler` directly (live validation) but does not use this generator. |
| `Hrot.Blueprints.Tests` | Integration tests for the compiler and generator pipeline. |

### Consuming Projects

Any HROT subsystem project that contains Blueprint assets must:
1. Reference `Hrot.Blueprints.Generators` as an `<Analyzer>`.
2. Declare its `.bp.json` files as `<AdditionalFiles>`.
3. Reference the FDP runtime packages that provide `Fdp.Toolkit.Blueprints`,
   `Fdp.Core`, and `Fdp.ModuleHost.Abstractions` (which the generated code uses).

### FDP Runtime Packages (used by generated code)

| Namespace | Purpose |
|---|---|
| `Fdp.Toolkit.Blueprints` | `BlueprintLatentCursor`, `BlueprintRegistryStaging`, `[BlueprintRegistrar]` attribute |
| `Fdp.Toolkit.Behavior` | `BehaviorRegistry` (required only for AiPrimitive with BTree/HSM hostings) |
| `Fdp.Core` | `EntityRepository` (world access in generated Tick methods) |
| `Fdp.Interfaces` | General engine interfaces |
| `Fdp.ModuleHost.Abstractions` | Module host abstractions used by registrar registration |

---

## Appendix: File Naming Convention

Generated filenames follow the pattern defined in `Sanitizer.GeneratedFileName`:

```
{SanitizedName}_{BlueprintId:X8}_Bp.g.cs
BlueprintRegistrar_{SanitizedName}_{BlueprintId:X8}_Bp.g.cs
```

The `.g.cs` suffix marks the file as auto-generated; Roslyn excludes it from
analyzer and formatting runs automatically.

Examples:

| Asset Name | AssetId (first 8 hex of GUID hash) | Generated Class File |
|---|---|---|
| `Math Helpers` | `A1B2C3D4` | `MathHelpers_A1B2C3D4_Bp.g.cs` |
| `Patrol Action` | `B2C3D4E5` | `PatrolAction_B2C3D4E5_Bp.g.cs` |
| `Soldier Behavior` | `C3D4E5F6` | `SoldierBehavior_C3D4E5F6_Bp.g.cs` |

The `BlueprintId` constant in the generated class is stored as `unchecked((int)0xXXXXXXXX)`
to allow the 8-character hex value to exceed `int.MaxValue` without a compile error.

---

## Appendix: csproj Configuration for netstandard2.0

The generator targets `netstandard2.0` as mandated by the Roslyn analyzer hosting
requirements. This means:

- `ImplicitUsings` must be **disabled** (`disable`) -- the global usings introduced
  by `ImplicitUsings=enable` are not available under `netstandard2.0`.
- `LangVersion` is set to `latest` so that C# 12 features (primary constructors,
  collection expressions, etc.) can be used in the generator code itself even though
  the output target is `netstandard2.0`.
- `IsRoslynComponent=true` activates the `Microsoft.CodeAnalysis.Analyzers` rules
  that enforce correct incremental generator patterns (no capturing of
  `GeneratorInitializationContext` references, etc.).
- `EnforceExtendedAnalyzerRules=true` enforces the extended rule set which includes
  checks for mutable state captured in incremental pipelines.
