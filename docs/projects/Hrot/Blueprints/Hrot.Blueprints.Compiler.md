# Hrot.Blueprints.Compiler

- **Project file**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj`
- **Target framework**: netstandard2.0
- **Namespace root**: `Hrot.Blueprints.Core.Compiler`
- **Date documented**: 2026-05-23

---

## README Validation

**Missing** -- No `README.md` is present in the project folder
(`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/`).

---

## Executive Overview

`Hrot.Blueprints.Compiler` is the **ahead-of-time (AOT) compiler** for the HROT Blueprints
visual-scripting system. It transforms a `BlueprintAsset` -- a JSON-serialized directed
graph authored in the editor -- into a self-contained C# source file (`.g.cs`). That
generated source is subsequently compiled by Roslyn and hot-loaded into the running
simulation.

The compiler is structured as a strict **seven-stage pipeline**. Each stage has a single
responsibility and passes an immutable or append-only data structure to the next stage.
Stages communicate errors and warnings through a shared `DiagnosticSink`; the pipeline
aborts early whenever errors accumulate.

The Roslyn finalization stage (Stage 8) exists as source in a sibling `Roslyn/` folder and
in `Stage8_RoslynFinalize.cs`, but is **excluded from this project's compilation** via
`<Compile Remove>` directives. The Roslyn step is linked into the runtime-facing assembly
(`Hrot.Blueprints.Core`) instead.

Three Blueprint dispatch kinds are supported:

| Kind          | Emitted class shape    | State struct         | Tick entry-point         |
|---------------|------------------------|----------------------|--------------------------|
| `Library`     | `static class`         | None                 | `static` methods per graph |
| `AiPrimitive` | `static class`         | `Params` + `WorkingState` structs | `TickCore(ref Params, ref WorkingState, ...)` |
| `Instance`    | `static class`         | `State` struct with embedded `BlueprintLatentCursor` | per-event and per-tick methods |

---

## Architecture

### Compilation pipeline overview

```
  +------------------+
  |  BlueprintAsset  |  (JSON-deserialized graph; input to the pipeline)
  +------------------+
           |
           v
  +------------------+     +-----------------+
  |  Stage1_Parse    |---->| DiagnosticSink  |  BP0001, BP0002, BP0010, BP0011
  +------------------+     +-----------------+
           |
           v
  +------------------+     +-----------------+
  | Stage2_Validate  |---->| DiagnosticSink  |  BP1xxx series (14 validators)
  +------------------+     +-----------------+
           |
           v
  +------------------+     +-----------------+
  | Stage3_Normalize |---->| DiagnosticSink  |  BP2xxx (implicit casts, orphan removal)
  +------------------+     +-----------------+
           |
           v
  +-------------------+    +-----------------+
  | Stage4_TypeResolve|---->| DiagnosticSink  |  BP3xxx, BP1500-BP1503
  +-------------------+    +-----------------+
           |
           v   TypedAsset
  +------------------+     +-----------------+
  | Stage5_Schedule  |---->| DiagnosticSink  |  BP4xxx
  +------------------+     +-----------------+
           |
           v   IrAsset (with IrGraph / IrBlock / IrStatement)
  +------------------+     +-----------------+
  |  Stage6_Lower    |---->| DiagnosticSink  |  BP5xxx
  +------------------+     +-----------------+
           |
           v   IrAsset (lowered; Suspend terminators gone; field offsets assigned)
  +------------------+     +-----------------+
  |  Stage7_Emit     |---->| DiagnosticSink  |  BP6xxx
  +------------------+     +-----------------+
           |
           v
  +--------------------+   +----------+
  | GeneratedSource    |   | DebugMap |
  | (string, .g.cs)    |   | (JSON)   |
  +--------------------+   +----------+
```

### IR data model

```
  IrAsset
  |-- AssetId, Name, SanitizedName, BlueprintId (FNV-32 of AssetId)
  |-- StructureHash (FNV-64 over field layout; computed in Stage 6)
  |-- Dispatch: Library | AiPrimitive | Instance
  |-- Parameters[]     (AiPrimitive only; unmanaged fields)
  |-- WorkingState[]   (AiPrimitive only; unmanaged fields; includes __phase, __waitUntilTime)
  |-- Variables[]      (Instance only; unmanaged fields)
  |-- CustomEvents[]   (Instance only)
  |-- CallablePeerBlueprintIds[] (Instance only)
  |-- Graphs[]
       |-- IrGraph
            |-- Kind: Function | Event | AiPrimitiveMain | Construction
            |-- Inputs[], Outputs[]
            |-- Entry: IrBlockId
            |-- Blocks[]
                 |-- IrBlock
                      |-- Id: IrBlockId
                      |-- Label (human-readable string for goto targets)
                      |-- Statements[]
                      |    |-- IrStatement
                      |         |-- ResultValue?: IrValue (SSA index + type)
                      |         |-- Operation: IrOperation (abstract record)
                      |         |-- Debug: IrDebugAnnotation
                      |-- Terminator: IrTerminator
                           |-- IrTerm_Goto
                           |-- IrTerm_Branch
                           |-- IrTerm_Return
                           |-- IrTerm_ReturnStatus
                           |-- IrTerm_Suspend  (removed by Stage 6 lowering)
                           |-- IrTerm_FallThrough
```

### Dispatch-specific lowering and emit

```
  +-----------+      Stage 6 Lowering          Stage 7 Emit
  | Library   |-->  LibraryLowering.Apply  --> LibraryEmitter.EmitClass
  |           |     (validates no latents)      (static funcs per graph)
  +-----------+
  +-----------+      AiPrimitiveLowering.Apply  AiPrimitiveEmitter.EmitClass
  | AiPrim.   |-->  WaitLowering_AiPrimitive --> (Params+WorkingState structs,
  |           |     (phase-byte state machine)   TickCore method, thunks)
  +-----------+
  +-----------+      InstanceLowering.Apply      InstanceEmitter.EmitClass
  | Instance  |-->  WaitLowering_Instance    --> (State struct, event methods,
  |           |     (cursor-based state machine)  Tick method, thunks)
  +-----------+
```

---

## Source Structure

### `Assets/` -- Authoring-format model

These classes represent the JSON asset as deserialized. They are the **input** to the
compiler pipeline and are read-only from Stage 3 onward (Stage 3 itself may rewrite them
during normalization).

| File | Description |
|------|-------------|
| `BlueprintAsset.cs` | Root asset object. Holds header, identity, dispatch kind, declarations, and graphs. |
| `Declarations.cs` | `VariableDecl`, `ParameterDecl`, `EventDispatcherDecl`, `CustomEventDecl`, `BlueprintTypeRef`. |
| `GraphTypes.cs` | `Graph`, `Pin`, `Link`, `GraphKind`, `NodeMetadata`, `AssetMetadata`, `Header`, `NodeStatus`. |
| `Nodes.cs` | `Node` hierarchy (21 concrete node types), polymorphic JSON via `[JsonDerivedType]`. |

### `Compiler/` -- Pipeline root

| File | Description |
|------|-------------|
| `BlueprintCompiler.cs` | `IBlueprintCompiler` interface + `BlueprintCompiler` implementation. Orchestrates all stages. |
| `BlueprintSignature.cs` | Lightweight metadata snapshot of a compiled Blueprint (id, name, exports, hostings). |
| `BlueprintSignatureParser.cs` | Reads only identity/dispatch/export fields from JSON without parsing node/link data. |
| `CompileOptions.cs` | Input parameters record: `CompilerMode`, registries, catalogs, sibling signatures. |
| `CompileResult.cs` | Output record: success flag, generated source, file name, blueprint ID, structure hash, debug map, diagnostics, canonical asset, PE/PDB bytes. |
| `IrPrinter.cs` | `internal` utility for human-readable SSA dump (snapshot tests, debugging). |
| `BlueprintJsonServices.cs` | `System.Text.Json` serialize/deserialize with `DefaultJsonTypeInfoResolver`. |

### `Compiler/Stages/` -- Pipeline stages

| File | Description |
|------|-------------|
| `Stage1_Parse.cs` | Deserializes JSON to `BlueprintAsset`; validates non-null result and non-empty identity. |
| `Stage2_Validate.cs` | Runs 14 sequential validators (see below). Stops on fatal errors. |
| `Stage3_Normalize.cs` | Three normalization passes: materialize default pin literals, insert implicit casts, eliminate orphan nodes. |
| `Stage4_TypeResolve.cs` | Resolves `BlueprintTypeRef` strings to `IrTypeRef` records; two-pass wildcard propagation for array nodes; enforces unmanaged constraint on state fields. |
| `Stage5_Schedule.cs` | BFS-based basic-block scheduler. Converts the graph's node/link structure into SSA-style `IrBlock` lists via `GraphScheduler`. |
| `Stage6_Lower.cs` | Dispatch-specific lowering; field-layout assignment; structure-hash computation; debug-probe insertion. |
| `Stage7_Emit.cs` | Delegates to `CSharpEmitter` to produce the final `.g.cs` source and a `DebugMap`. |
| `TypedAsset.cs` | `record TypedAsset(BlueprintAsset, IrPinTypes, IrFieldTypes)` -- output of Stage 4. |
| `ValidationContext.cs` | Mutable context carrying catalogs, registries, sibling signatures, and the `DiagnosticSink`. Threaded through all Stage 2 validators. |

#### Stage 2 validators in execution order

| Validator | Codes | Description |
|-----------|-------|-------------|
| `V_AssetStructure` | BP0010, BP0011 | Non-empty `AssetId` and `Name`. |
| `V_DispatchKindCompatibility` | BP1010-BP1031 | Ensures `Library`, `AiPrimitive`, and `Instance` use the correct declaration shape. |
| `V_NodeStructure` | BP1601 | No duplicate pin IDs within a node. |
| `V_LinkStructure` | BP1601, BP1602 | No duplicate links; source and target node/pin IDs must exist. |
| `V_GraphStructure` | BP1601, BP1602 | Each graph has a reachable entry node; a `ReturnNode` is exec-reachable. |
| `V_VariablesAndState` | BP1200-BP1211 | No duplicate variable/parameter IDs; name uniqueness across tables. |
| `V_AiPrimitiveIntent` | BP1100, BP1101 | Action/Condition intent is consistent with graph return types. |
| `V_LatentRules` | BP9001 | Library assets must not contain latent (wait) nodes. |
| `V_ChannelCommandReferences` | BP1400-BP1402 | `ChannelCommandNode` references exist in `IChannelCommandCatalog`. |
| `V_EventGraphReferences` | BP1400 | `EventEntryNode.EventTypeId` references a known engine event. |
| `V_WaitNodeReferences` | BP1401 | `WaitForChannelNode` / `WaitForEventNode` targets exist in `IWaitPrimitiveCatalog`. |
| `V_PeerReferences` | BP1300-BP1302 | Peer blueprint Guids appear in `SiblingSignatures`; callee functions are exported. |
| `V_TypeReferences` | BP1500-BP1503 | All `BlueprintTypeRef` strings resolve in `ITypeRegistry`; unmanaged constraint on state fields. |
| `V_DeterminismOrdering` | (info only) | Checks that node/pin ordering in the JSON is stable. |

### `Compiler/Ir/` -- Intermediate representation

| File | Description |
|------|-------------|
| `IrAsset.cs` | Top-level IR record; holds all fields, custom events, peer IDs, dispatch metadata, and graph list. Also defines `IrField`, `IrCustomEvent`. |
| `IrGraph.cs` | `IrGraph` record + `IrGraphKind` enum. |
| `IrBlock.cs` | `IrBlock` record + all `IrTerminator` subtypes: `Goto`, `Branch`, `Return`, `ReturnStatus`, `Suspend`, `FallThrough`. |
| `IrStatement.cs` | `IrStatement` record: optional result `IrValue`, `IrOperation`, `IrDebugAnnotation`. |
| `IrOperation.cs` | 30+ `IrOperation` record subtypes covering constants, variable reads/writes, pure calls, library/peer/AiPrimitive calls, ECS reads/writes, channel commands, wait primitives, and debug probes. |
| `IrTypeRef.cs` | `IrTypeRef` record: fully qualified name, array/element info, unmanaged flag, size in bytes, entity-handle flag. |
| `IrValue.cs` | `IrValue(Index, Type)` -- SSA value reference. `IrBlockId(Value)` -- block reference. |
| `IrDebugAnnotation.cs` | `IrDebugAnnotation` -- `GraphId`, optional `NodeId`, optional `PinId`, optional synthesized label. |

### `Compiler/Catalogs/` -- Extension points

| File | Description |
|------|-------------|
| `CatalogInterfaces.cs` | `IEngineEventCatalog`, `IChannelCommandCatalog`, `IWaitPrimitiveCatalog` + their entry record types. |
| `INodeRegistry.cs` | `INodeRegistry` (stub; population deferred to TASK-CP-005). |
| `ITypeRegistry.cs` | `ITypeRegistry`: `TryResolve(BlueprintTypeRef) -> IrTypeRef` + `TryGetCoercion(from, to) -> string`. |
| `StaticTypeRegistry.cs` | Default registry: C# primitives, `System.Numerics` vectors, `Fdp.Core.Entity`, common aliases. Coercion table (8 widening numeric rules). |
| `BuiltInNodeRegistry.cs` | Singleton stub for `INodeRegistry`. |
| `BuiltInEngineEventCatalog.cs` | Three built-in engine events: `HitEvent`, `BehaviorFinishedEvent`, `TargetVisibleEvent`. |
| `BuiltInChannelCommandCatalog.cs` | Five built-in channel commands: `MoveTo`, `FollowRoute`, `AimAndFire`, `OpenDoor`, `EjectPassengers`. |
| `BuiltInWaitPrimitiveCatalog.cs` | Five built-in wait primitives for channel and event waits. |

### `Compiler/Lowering/` -- Stage 6 passes

| File | Description |
|------|-------------|
| `LibraryLowering.cs` | Validates that no latent ops slipped through; checks at least one function graph exists. |
| `AiPrimitiveLowering.cs` | Adds synthesized `__phase` (byte) and `__waitUntilTime` (float) fields to `WorkingState` when latent ops are present; delegates to `WaitLowering_AiPrimitive`. |
| `InstanceLowering.cs` | Delegates each graph containing latent ops to `WaitLowering_Instance`. |
| `WaitLowering_AiPrimitive.cs` | Transforms `IrTerm_Suspend` terminators into a **phase-byte state machine**: a dispatch block switches on `__phase`, branching into per-phase check blocks that test channel/event readiness and loop or resume. |
| `WaitLowering_Instance.cs` | Transforms `IrTerm_Suspend` terminators into a **cursor-based state machine**: `State.Cursor.ResumeAt` is an integer dispatch index; a chain of comparison blocks dispatches to per-resume check blocks. |
| `ChannelCommandLowering.cs` | (inside `Emit/`) Emits inline `GetComponentRW` + action field writes + `ActionInstanceId++` for `IrOp_ChannelCommand`. |
| `FieldLayout.cs` | Assigns `Offset` and `Size` to all `IrField` records using sequential layout with natural alignment (1/2/4/8-byte). `Parameters` starts at offset 0, `WorkingState` at 8, `Variables` at 16. |
| `StructureHashComputation.cs` | Computes FNV-64 hash over the concatenation of dispatch kind, field names, type full names, offsets, and sizes. Used to detect breaking API changes at hot-reload time. |
| `DebugProbeInsertion.cs` | In `Debug` mode: inserts `IrOp_DebugProbe_NodeEnter` at the start of each block's first node. In `Trace` mode: also inserts `IrOp_DebugProbe_PinValue` after each value-producing statement that has a pin annotation. No-op in `Release` mode. |
| `SynthesizedGuids.cs` | Generates deterministic `Guid` values for synthesized IR elements using SHA-256 hashing of purpose + inputs. |

### `Compiler/Emit/` -- Stage 7 code generation

| File | Description |
|------|-------------|
| `CSharpEmitter.cs` | Orchestrator. Writes to a `StringBuilder`; tracks indent level and line number. Dispatches to the three dispatch-specific emitters. Also emits file header, `using` directives, and the `BlueprintRegistrar_*` class. |
| `EmissionContext.cs` | Per-asset mutable state: block-label lookup, local counter per prefix, field-name resolvers, `WorldVar` and `StateVar` expressions that vary by dispatch kind. |
| `LibraryEmitter.cs` | Emits `public static class {Name}_{Id}_Bp` with one `static` method per `Function` graph. |
| `AiPrimitiveEmitter.cs` | Emits `[StructLayout(Sequential)] struct Params`, `struct WorkingState`, `InitDefaultWorkingState`, `TickCore`, and B-Tree/HSM thunks. |
| `InstanceEmitter.cs` | Emits `struct State` (with `BlueprintLatentCursor`), `VarIds` inner class, `InitDefault`, per-event methods, `Tick`, and `[UnmanagedFunctionPointer]` thunks. |
| `BlockEmitter.cs` | Emits one `IrBlock` as a C# labeled block (`__block_{label}:`) followed by all statements and then the terminator. |
| `StatementEmitter.cs` | Switches over all `IrOperation` subtypes and emits the corresponding C# expression or statement. Uses `__t{index}` as SSA temporary variable names. |
| `TerminatorEmitter.cs` | Emits `goto`, `if/else goto`, `return`, `return NodeStatus.*`. Throws on `IrTerm_Suspend` (must have been lowered). |
| `ChannelCommandLowering.cs` | Inline emission of `GetComponentRW` + field writes for `IrOp_ChannelCommand`. |
| `DebugMapBuilder.cs` | Maintains an open-node dictionary keyed on `NodeId`; records `(NodeId, GraphId, StartLine, EndLine)` spans. Builds a `DebugMap` record at the end. |
| `DebugMapSerializer.cs` | Serializes/deserializes `DebugMap` to/from camelCase JSON, ordered deterministically by `GraphId` then `StartLine`. |
| `Sanitizer.cs` | `SanitizeName`: converts Blueprint names to PascalCase C# identifiers by capitalizing after non-alphanumeric characters. `GeneratedFileName`: produces `{Name}_{Id:X8}_Bp.g.cs` or `BlueprintRegistrar_...` variants. |

### `Compiler/Diagnostics/` -- Error reporting

| File | Description |
|------|-------------|
| `Diagnostic.cs` | Immutable record: `Severity`, `Code`, `Message`, optional `AssetId`/`GraphId`/`NodeId`/`PinId` location context. |
| `DiagnosticCodes.cs` | String constants for all `BPxxxx` codes organized by stage (BP0xxx parse, BP1xxx validate, BP2xxx normalize, BP3xxx type-resolve, BP4xxx schedule, BP5xxx lower, BP6xxx emit, BP7xxx Roslyn, BP9xxx internal). |
| `DiagnosticSink.cs` | Mutable accumulator; exposes `HasErrors`, `HasFatalErrors`, and `All`. |

### `Compiler/Compatibility/` -- Polyfills and public contracts

| File | Description |
|------|-------------|
| `BlueprintCompilerContracts.cs` | `CompilerMode` enum (`Debug`, `Release`, `Trace`); `BlackboardTier` enum. |
| `BlueprintIdHash.cs` | FNV-32 hash of `Guid.ToByteArray()` to produce the 32-bit `BlueprintId` integer used as a class-name suffix and runtime lookup key. |
| `IsExternalInit.cs` | C# 9 `init` accessor polyfill for `netstandard2.0`. |

### `Compiler/Determinism/` -- Hash utilities

| File | Description |
|------|-------------|
| `FnvHasher.cs` | Static `Hash32` and `Hash64` methods using FNV-1a constants. |
| `DeterministicEnumerable.cs` | `OrderById<T>` and `OrderByName<T>` helpers used by compiler stages to produce stable iteration order. |

---

## Public API Reference

### `IBlueprintCompiler` / `BlueprintCompiler`

```csharp
namespace Hrot.Blueprints.Core.Compiler;

public interface IBlueprintCompiler
{
    CompileResult Compile(BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null);
}

public sealed class BlueprintCompiler : IBlueprintCompiler { ... }
```

`Compile` runs the full pipeline (Stage 2 through Stage 7) and returns a `CompileResult`.
`Validate` runs only Stage 2 with default built-in registries, suited for real-time
editor feedback without a full compile.

### `CompileOptions`

```csharp
public sealed record CompileOptions(
    CompilerMode Mode,
    INodeRegistry NodeRegistry,
    ITypeRegistry TypeRegistry,
    IEngineEventCatalog EngineEvents,
    IChannelCommandCatalog ChannelCommands,
    IWaitPrimitiveCatalog WaitPrimitives,
    IReadOnlyList<BlueprintSignature> SiblingSignatures,
    bool EmitPdbWithEmbeddedSource = false,
    string? VirtualSourcePath = null);
```

All catalogs and registries are injectable for testing. Pass `BuiltIn*` singletons for
production use.

### `CompileResult`

```csharp
public sealed record CompileResult(
    bool Succeeded,
    string? GeneratedSource,        // C# text of the .g.cs file
    string? GeneratedFileName,      // e.g. "MyBP_A1B2C3D4_Bp.g.cs"
    int BlueprintId,                // FNV-32 of AssetId
    ulong StructureHash,            // FNV-64 of field layout
    DebugMap? DebugMap,
    IReadOnlyList<Diagnostic> Diagnostics,
    BlueprintAsset? CanonicalAsset, // normalized/typed asset
    byte[]? PortablePdb,            // null until Stage 8 is linked
    byte[]? PortablePe);            // null until Stage 8 is linked
```

### `ValidationResult`

```csharp
public sealed record ValidationResult(IReadOnlyList<Diagnostic> Diagnostics);
```

### `BlueprintSignature`

```csharp
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

Used by the build system to resolve cross-Blueprint references without performing a full
compile of all dependencies.

### `BlueprintSignatureParser`

```csharp
public static class BlueprintSignatureParser
{
    public static BlueprintSignature Parse(string filePath, string jsonText);
}
```

Reads identity, dispatch, exported function graph names, hosting declarations, and callable
peers from raw JSON using `JsonDocument`. Does not deserialize the `nodes` or `links`
arrays.

### `CompilerMode` enum

| Value | Effect |
|-------|--------|
| `Debug` | Debug probe nodes (`IrOp_DebugProbe_NodeEnter`) inserted at block entry. |
| `Trace` | All of Debug plus `IrOp_DebugProbe_PinValue` after each value-producing statement. |
| `Release` | No probe insertion; slightly smaller generated output. |

### `Diagnostic`

```csharp
public sealed record Diagnostic(DiagnosticSeverity Severity, string Code, string Message)
{
    public Guid? AssetId { get; init; }
    public Guid? GraphId { get; init; }
    public Guid? NodeId  { get; init; }
    public Guid? PinId   { get; init; }
    public bool IsError => Severity == DiagnosticSeverity.Error;

    public static Diagnostic Error(string code, string message, ...);
    public static Diagnostic Warning(string code, string message, ...);
    public static Diagnostic Info(string code, string message, ...);
}
```

### `DebugMap` and `DebugMapSerializer`

```csharp
public sealed record DebugMap
{
    public Guid AssetId { get; init; }
    public int  BlueprintId { get; init; }
    public ulong StructureHash { get; init; }
    public IReadOnlyList<DebugMapEntry> Entries { get; init; }
}

public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine)
{
    public string NodeKind    { get; init; }
    public string DisplayName { get; init; }
    public int?   PhaseIndex  { get; init; }
}

public static class DebugMapSerializer
{
    public static string   Serialize(DebugMap debugMap);
    public static DebugMap? Deserialize(string json);
}
```

Entries map generated C# line numbers back to the original Blueprint node IDs, enabling
the editor debugger to highlight the executing node.

### `Sanitizer`

```csharp
public static class Sanitizer
{
    public static string SanitizeName(string name);
    public static string GeneratedFileName(string sanitizedName, int blueprintId, bool isRegistrar);
}
```

### `ITypeRegistry` / `StaticTypeRegistry`

```csharp
public interface ITypeRegistry
{
    bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType);
    bool TryGetCoercion(IrTypeRef from, IrTypeRef to, out string coercionExpression);
}

public sealed class StaticTypeRegistry : ITypeRegistry
{
    public static readonly StaticTypeRegistry Instance;
}
```

Built-in type table covers C# primitive aliases, `System.Numerics` vectors
(`Vector2`/`Vector3`/`Vector4`/`Quaternion`), and `Fdp.Core.Entity`. The coercion table
contains eight widening numeric rules (e.g., `byte -> int`, `int -> float`).

### `BlueprintIdHash`

```csharp
public static class BlueprintIdHash
{
    public static int Compute(Guid assetId);
}
```

FNV-32 hash of the 16-byte GUID. The result is used as a 32-bit integer suffix in
generated class names and as the runtime registry key.

### `IrPrinter` (internal)

```csharp
internal static class IrPrinter
{
    public static string PrettyPrint(IrAsset asset);
}
```

Produces a deterministic multi-line text dump of the IR for use in snapshot tests.
Format: `IrAsset: {Name} (0x{Id}) {Dispatch}`, then per-graph block listings with
one line per statement and one line for the terminator.

---

## Dependencies

### NuGet packages

| Package | Version | Used for |
|---------|---------|----------|
| `Microsoft.CodeAnalysis.CSharp` | 4.8.0 | Roslyn; the excluded Stage 8 folder; syntax tree analysis in future catalog work. |
| `System.Runtime.Loader` | 4.3.0 | `AssemblyLoadContext` support for hot-reload in the runtime assembly. |
| `System.Text.Json` | 8.0.5 | `BlueprintJsonServices`, `BlueprintSignatureParser`, `DebugMapSerializer`. |

### Excluded source (compile-time)

```xml
<Compile Remove="Compiler\Roslyn\**\*.cs" />
<Compile Remove="Compiler\Stages\Stage8_RoslynFinalize.cs" />
```

The `Roslyn/` folder and `Stage8_RoslynFinalize.cs` are compiled into
`Hrot.Blueprints.Core` (the runtime assembly) via `<Compile Include>` from that project's
`.csproj`. They are excluded here to avoid duplicate symbol errors.

### Project references

No `<ProjectReference>` entries are present in this project file. The project is
self-contained and references only NuGet packages. The `Assets/` model classes live inside
this project (not in a separate Core project) under the namespace
`Hrot.Blueprints.Core.Assets`.

### InternalsVisibleTo

```xml
<InternalsVisibleTo Include="Hrot.Blueprints.Tests" />
```

The test project has access to all `internal` compiler types (validators, IR printer,
scheduling internals).

---

## Usage Examples

### Example 1 -- Compile a Blueprint from JSON with default options

```csharp
// Load JSON text from disk (or from editor in-memory).
string jsonText = File.ReadAllText("Assets/Behaviors/Patrol.bp.json");

// Deserialize the asset model.
BlueprintAsset? asset = BlueprintJsonServices.Deserialize(jsonText);
if (asset is null) throw new InvalidDataException("Empty or malformed Blueprint JSON.");

// Build compile options referencing built-in catalogs.
var options = new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>());

// Run the compiler.
IBlueprintCompiler compiler = new BlueprintCompiler();
CompileResult result = compiler.Compile(asset, options);

if (!result.Succeeded)
{
    foreach (var diag in result.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"[{diag.Code}] {diag.Message}");
    return;
}

// Write the generated source to disk for Roslyn (Stage 8) to pick up.
File.WriteAllText(
    Path.Combine("Generated", result.GeneratedFileName!),
    result.GeneratedSource!,
    Encoding.UTF8);

// Optionally serialize and save the debug map.
string debugMapJson = DebugMapSerializer.Serialize(result.DebugMap!);
File.WriteAllText(
    Path.Combine("Generated", "debugmaps", $"{result.BlueprintId:X8}.debugmap.json"),
    debugMapJson,
    Encoding.UTF8);
```

### Example 2 -- Editor-time validation without full compilation

```csharp
// During editing, validate in real-time to provide instant feedback.
// Uses built-in catalogs; no sibling signature resolution needed.
IBlueprintCompiler compiler = new BlueprintCompiler();
ValidationResult validation = compiler.Validate(asset);

var errors   = validation.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
var warnings = validation.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

Console.WriteLine($"Validation: {errors.Count} error(s), {warnings.Count} warning(s)");

foreach (var d in validation.Diagnostics)
{
    string loc = d.NodeId.HasValue ? $" (node {d.NodeId})" : "";
    Console.WriteLine($"  [{d.Code}] {d.Severity}: {d.Message}{loc}");
}
```

### Example 3 -- Parse signatures for a build dependency graph

```csharp
// Before compiling a set of Blueprints, collect all signatures.
// This is cheap: it only reads identity, dispatch, and exported function names.
string[] blueprintFiles = Directory.GetFiles("Assets/Behaviors", "*.bp.json");

var signatures = new List<BlueprintSignature>(blueprintFiles.Length);
foreach (var file in blueprintFiles)
{
    string json = File.ReadAllText(file);
    BlueprintSignature sig = BlueprintSignatureParser.Parse(file, json);
    signatures.Add(sig);
}

// Now compile each Blueprint, providing all siblings so that cross-Blueprint
// peer calls and library calls can be validated.
var options = new CompileOptions(
    Mode:              CompilerMode.Release,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: signatures);

IBlueprintCompiler compiler = new BlueprintCompiler();
foreach (var file in blueprintFiles)
{
    var asset = BlueprintJsonServices.Deserialize(File.ReadAllText(file))!;
    var result = compiler.Compile(asset, options);
    // process result ...
}
```

### Example 4 -- Custom type registry with game-specific types

```csharp
public sealed class GameTypeRegistry : ITypeRegistry
{
    public static readonly GameTypeRegistry Instance = new();

    // Delegate unknown types to the built-in registry.
    private static readonly StaticTypeRegistry _builtin = StaticTypeRegistry.Instance;

    private static readonly IReadOnlyDictionary<string, IrTypeRef> _gameTypes =
        new Dictionary<string, IrTypeRef>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hrot.Game.Waypoint"] = new IrTypeRef
            {
                FullName    = "Hrot.Game.Waypoint",
                IsUnmanaged = true,
                SizeBytes   = 12,
            },
            ["Hrot.Game.ThreatLevel"] = new IrTypeRef
            {
                FullName    = "Hrot.Game.ThreatLevel",
                IsUnmanaged = true,
                SizeBytes   = 4,
            },
        };

    public bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType)
    {
        if (_gameTypes.TryGetValue(typeRef.TypeId, out irType!))
            return true;
        return _builtin.TryResolve(typeRef, out irType);
    }

    public bool TryGetCoercion(IrTypeRef from, IrTypeRef to, out string coercionExpression)
        => _builtin.TryGetCoercion(from, to, out coercionExpression);
}

// Use in compile options:
var options = new CompileOptions(
    Mode:              CompilerMode.Debug,
    TypeRegistry:      GameTypeRegistry.Instance,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>());
```

### Example 5 -- Inspect the IR for debugging or testing

```csharp
// Manually run stages to inspect intermediate results (e.g. in tests).
var sink = new DiagnosticSink();
var compileOptions = new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>());

var ctx = new ValidationContext(sink, compileOptions);

Stage2_Validate.Run(asset, ctx);
asset = Stage3_Normalize.Run(asset, ctx);
var typed = Stage4_TypeResolve.Run(asset, ctx);
IrAsset ir = Stage5_Schedule.Run(typed, ctx);

// Dump human-readable IR for a snapshot test.
string dump = IrPrinter.PrettyPrint(ir);
Console.WriteLine(dump);
// Expected output shape:
//   IrAsset: Patrol (0xA1B2C3D4) AiPrimitive
//     Graph: Main [AiPrimitiveMain] Entry=0
//       Block 0 (entry):
//         t0 = read_param[0]
//         pure_call System.Math.Max(t0, t1)
//         goto block_1
//       Block 1 (exit):
//         return_status Success
```

---

## Best Practices

### 1. Always provide sibling signatures for cross-Blueprint projects

The Stage 2 validator `V_PeerReferences` checks that peer blueprint IDs resolve to known
siblings and that the called function names are exported. Without sibling signatures, all
`CallPeerBlueprintNode` and cross-Blueprint `FunctionCallNode` references will produce
`BP1300`-series errors. Collect signatures cheaply with `BlueprintSignatureParser.Parse`
before the full compile pass.

### 2. Keep state fields unmanaged

Only types with `IrTypeRef.IsUnmanaged = true` are permitted in `WorkingState`
(AiPrimitive) and `Variables` (Instance). Managed types such as `string` or `object`
trigger `BP1503`. State structs are laid out sequentially in raw memory via
`FieldLayout.ComputeFieldLayouts`, so references are unsafe. Use `int` identifiers or
`Fdp.Core.Entity` handles as cross-frame references.

### 3. Never mutate the input asset between Stage 2 and Stage 3

`BlueprintCompiler.Compile` passes the same asset reference to Stage 2 and Stage 3.
Stage 3 (`Normalize`) may return a new instance with rewritten graph lists. Callers
should treat `CompileResult.CanonicalAsset` as the authoritative post-normalization asset
and discard their original copy after a successful compile.

### 4. Use `CompilerMode.Release` in production builds

`DebugProbeInsertion.Apply` is a no-op in `Release` mode. This avoids inserting
`IrOp_DebugProbe_NodeEnter` and `IrOp_DebugProbe_PinValue` statements into the IR, which
reduces generated source size and eliminates function-call overhead per basic block.

### 5. Register custom types before interpreting `BP1500` errors

If the project uses game-specific value types (e.g., custom blittable structs for waypoints
or threat levels), implement `ITypeRegistry` and pass the custom instance in
`CompileOptions.TypeRegistry`. A `BP1500` ("type could not be resolved") error is the
symptom of a missing type registration, not a malformed asset.

### 6. Use `BlueprintIdHash.Compute` for consistent cross-system identification

The `BlueprintId` integer is derived deterministically from `AssetId` via FNV-32. It
appears in generated class names, in the `DebugMap`, and is the runtime registry key.
Store the `AssetId` (GUID) as the authoritative identifier in your asset database; derive
`BlueprintId` on demand using `BlueprintIdHash.Compute` rather than persisting both.

### 7. Treat `StructureHash` as a breaking-change indicator

`StructureHash` is an FNV-64 hash of the full field layout (dispatch kind, field names,
type full names, byte offsets and sizes). It changes whenever the compiled Blueprint's
memory layout changes. Use it to gate hot-reload: if the hash matches the currently loaded
assembly, in-place memory patch is safe; if not, a full re-registration is required.

---

## Diagnostic Code Reference

| Code | Stage | Severity | Meaning |
|------|-------|----------|---------|
| BP0001 | Parse | Error | JSON deserialized to null (empty or malformed file). |
| BP0002 | Parse | Error | JSON parse exception (syntax error). |
| BP0010 | Parse/Validate | Error | Asset has empty/zero `AssetId`. |
| BP0011 | Parse/Validate | Error | Asset has empty `Name`. |
| BP1010 | Validate | Error | Library asset has a `primitive` block. |
| BP1011 | Validate | Error | Library asset declares member variables. |
| BP1012 | Validate | Error | Library asset declares custom events. |
| BP1013 | Validate | Error | Library asset contains event graphs. |
| BP1020 | Validate | Error | AiPrimitive asset missing `primitive` block. |
| BP1021 | Validate | Error | AiPrimitive declares no hostings. |
| BP1022 | Validate | Error | Action intent with a condition-shaped hosting. |
| BP1023 | Validate | Error | Condition intent with an action-shaped hosting. |
| BP1024 | Validate | Error | AiPrimitive uses `variables` instead of `parameters`/`workingState`. |
| BP1025 | Validate | Error | AiPrimitive contains event graphs. |
| BP1030 | Validate | Error | Instance asset has a `primitive` block. |
| BP1031 | Validate | Error | Instance asset uses `parameters`/`workingState`. |
| BP1300-1302 | Validate | Error | Peer blueprint or called function not found in sibling signatures. |
| BP1400-1402 | Validate | Error | Channel/event/wait catalog reference not found. |
| BP1500 | Validate/TypeResolve | Error | Type string not found in registry. |
| BP1501 | Validate | Error | Type reference constraint violation. |
| BP1502 | TypeResolve | Error | Unresolvable wildcard (ArrayMakeNode/ArrayGetNode). |
| BP1503 | TypeResolve | Error | Managed type in unmanaged state struct. |
| BP1601 | Validate | Error/Warning | Duplicate pin IDs or duplicate links. |
| BP1602 | Validate | Error | Broken link reference; graph has no entry node. |
| BP2001 | Normalize | Warning | Orphan node eliminated. |
| BP2002 | Normalize | Warning | Implicit cast inserted. |
| BP4001-4004 | Schedule | Error | Scheduling errors (unreachable code, missing pin). |
| BP5001 | Lower | Error | Library asset has no function graphs. |
| BP9001 | Internal | Error | Library asset contains latent op (should have been caught earlier). |

---

## Related Projects

| Project | Relationship |
|---------|--------------|
| `Hrot.Blueprints.Core` | The runtime-facing assembly that links Stage 8 (Roslyn finalization) and provides the hot-reload orchestration layer. This Compiler project is a `netstandard2.0` library intended to be referenced by both editor tooling and the runtime. |
| `Hrot.Blueprints.Tests` | The test project. Has `InternalsVisibleTo` access. Tests cover each pipeline stage, the IR model, and the emit layer. |
| `Fdp.Toolkit.Blueprints` | The FDP runtime library that defines `BlueprintRegistryStaging`, `BlueprintDefinition`, `BlueprintDispatchKind`, `BlueprintLatentCursor`, and the `[BlueprintRegistrar]` attribute referenced by generated code. |
| `Fdp.Core` | Provides `Entity`, `EntityRepository`, and ECS interfaces referenced in `StaticTypeRegistry`, `EmissionContext.WorldVar`, and generated code. |
| `Fdp.Toolkit.Behavior` | Provides `BehaviorRegistry`, `LocomotionChannel`, `WeaponChannel`, `BehaviorFinishedEvent`, and other types appearing in the built-in catalogs. |
| `Fdp.Toolkit.Combat.Contracts` | Source of `HitEvent` referenced in `BuiltInEngineEventCatalog`. |
| `Fdp.Toolkit.Perception.Events` | Source of `TargetVisibleEvent` referenced in `BuiltInEngineEventCatalog`. |
