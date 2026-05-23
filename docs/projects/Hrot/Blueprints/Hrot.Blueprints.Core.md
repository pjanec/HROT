# Hrot.Blueprints.Core

- **Project file**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj`
- **Target framework**: net8.0
- **Date documented**: 2026-05-23

---

## README Validation

**Missing** -- No `README.md` is present in the project folder
(`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/`).

---

## Executive Overview

Blueprints is HROT's visual-scripting and data-driven behaviour authoring system, inspired
in concept by Unreal Engine's Blueprint system but implemented as an ahead-of-time (AOT)
C#-generating compiler rather than an interpreter.  A Blueprint is a JSON asset that
describes a directed graph of typed nodes.  The compiler pipeline converts that JSON into
ordinary C# source, which is then compiled by Roslyn and hot-loaded into the running
simulation as a collectible `AssemblyLoadContext`.

`Hrot.Blueprints.Core` is the **runtime-facing assembly** within the Blueprints subsystem.
It contains:

1. **The full compiler pipeline** -- all stages from JSON parsing through C# source
   emission and Roslyn compilation.  (The compiler stages live in the sibling project
   `Hrot.Blueprints.Compiler` as source files, but `Hrot.Blueprints.Core` links the
   Roslyn-specific stages in via `<Compile Include=...>` so that the runtime assembly
   carries the complete feature set.)
2. **The asset model** -- the C# classes that represent a Blueprint asset and its graph
   structure (nodes, pins, links, variables, declarations).
3. **The intermediate representation (IR)** -- the typed, lowered form of an asset used
   internally during compilation.
4. **The debug infrastructure** -- probe injection, debug maps, watch lists, breakpoints,
   and the session interface consumed by the editor.

Three dispatch kinds exist:

| Kind          | Description |
|---------------|-------------|
| `Library`     | A stateless library of pure and impure functions callable by other Blueprints. |
| `AiPrimitive` | A behaviour tree / HSM action or condition; carries `WorkingState` fields and runs per-entity per-tick. |
| `Instance`    | An entity-attached state machine with persistent `Variables`, event-driven graphs, and latent (coroutine-style) execution. |

---

## Architecture

### High-level component map

```
+-------------------------------------------------------------------------+
|                        Hrot.Blueprints.Core                             |
|                                                                         |
|  +------------------+   +------------------+   +---------------------+ |
|  |   Asset Model    |   |   IR Model       |   |   Debug Layer       | |
|  |  (Assets/ ns)    |-->|  (Compiler.Ir/)  |-->|  (Core.Debug/ ns)  | |
|  +------------------+   +------------------+   +---------------------+ |
|           |                      |                       |             |
|           v                      v                       v             |
|  +------------------+   +------------------+   +---------------------+ |
|  |  BlueprintAsset  |   |    IrAsset       |   |  IBlueprintDebug-   | |
|  |  Graph, Node,    |   |  IrGraph,IrBlock |   |  Session interface  | |
|  |  Pin, Link       |   |  IrStatement,    |   |  DebugProbe static  | |
|  +------------------+   |  IrOperation     |   |  DebugMapIndex      | |
|                         +------------------+   +---------------------+ |
+-------------------------------------------------------------------------+
        |
        | references (full compiler pipeline embedded via <Compile Include>)
        v
+---------------------------+
|  Compiler Pipeline        |
|  Stage1_Parse             |
|  Stage2_Validate          |
|  Stage3_Normalize         |
|  Stage4_TypeResolve       |
|  Stage5_Schedule          |
|  Stage6_Lower             |
|  Stage7_Emit              |
|  Stage8_RoslynFinalize    |
+---------------------------+
        |
        v
+---------------------------+       +-------------------+
|  Roslyn (in-memory)       |       |  collectible ALC  |
|  InMemoryRoslynCompiler   |------>|  (loaded PE/PDB)  |
+---------------------------+       +-------------------+
```

### Compiler pipeline (stage-by-stage)

```
  JSON text
     |
     v
+--------------------+
| Stage 1 - Parse    |  System.Text.Json -> BlueprintAsset
+--------------------+
     |
     v
+--------------------+
| Stage 2 - Validate |  14 validators, produces Diagnostic list
+--------------------+
     |
     v
+--------------------+
| Stage 3 - Normalize|  implicit casts, default literals, orphan removal
+--------------------+
     |
     v
+--------------------+
| Stage 4 - TypeRes. |  BlueprintTypeRef -> IrTypeRef (two-pass wildcard)
+--------------------+
     |
     v
+--------------------+
| Stage 5 - Schedule |  Graph -> IrGraph (SSA-like IrBlock sequence)
+--------------------+
     |
     v
+--------------------+
| Stage 6 - Lower    |  dispatch lowering, FieldLayout, StructureHash,
|                    |  DebugProbeInsertion
+--------------------+
     |
     v
+--------------------+
| Stage 7 - Emit     |  IrAsset -> C# source string + DebugMap
+--------------------+
     |
     v
+--------------------+
| Stage 8 - Roslyn   |  C# source -> PE bytes + PDB bytes
| (Core only)        |
+--------------------+
     |
     v
  Assembly bytes
```

### Debug instrumentation flow

```
+--------------------+     DebugProbe.Sink     +--------------------+
|  Generated C# code |------------------------>| IBlueprintProbe-   |
|  (runtime entity   |  NodeEnter()            | Sink               |
|   tick method)     |  PinValueChanged<T>()   +--------------------+
+--------------------+  PeerCallEnter/Exit()           |
                                                        | routes to
                                                        v
                                               +--------------------+
                                               | IBlueprintDebug-   |
                                               | Session (editor or |
                                               | test impl.)        |
                                               +--------------------+
                                                        |
                                               +--------+--------+
                                               |                 |
                                    +----------v---+    +--------v------+
                                    | Breakpoint   |    | Watch list    |
                                    | management   |    | ExecutionHist.|
                                    +--------------+    +---------------+
```

---

## Source Structure

All source files that form `Hrot.Blueprints.Core` at build time are listed below.
Files in the Core project folder are prefixed `[CORE]`.  Files compiled via
`<Compile Include=...>` from `Hrot.Blueprints.Compiler` are prefixed `[COMPILER]`.

### Core project folder: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/`

| File | Namespace | Primary type(s) |
|------|-----------|-----------------|
| `AssemblyInfo.cs` | _(assembly level)_ | `[assembly: InternalsVisibleTo("Hrot.Blueprints.Tests")]` |
| `BlueprintsCore.cs` | `Hrot.Blueprints.Core` | Assembly placeholder; no public types |
| `InMemoryRoslynCompiler.cs` | `Hrot.Blueprints.Core` | `InMemoryRoslynCompiler` (stub, superseded by Compiler.Roslyn version) |
| `IBlueprintTimeController.cs` | `Hrot.Blueprints.Core.Debug` | `IBlueprintTimeController` |
| `IBlueprintProbeSink.cs` | `Hrot.Blueprints.Core.Debug` | `IBlueprintProbeSink` |
| `IBlueprintDebugSession.cs` | `Hrot.Blueprints.Core.Debug` | `BreakpointId`, `WatchId`, `StepMode`, `BreakpointHit`, `NodeExecuted`, `PinValueChanged`, `NodeHistoryEntry`, `Breakpoint`, `Watch`, `BlueprintStateSnapshot`, `IBlueprintDebugSession` |
| `ExecutionHistory.cs` | `Hrot.Blueprints.Core.Debug` | `ExecutionHistory` |
| `DebugProbe.cs` | `Hrot.Blueprints.Core.Debug` | `DebugProbe`, `NullProbeSink` |
| `DebugMapIndex.cs` | `Hrot.Blueprints.Core.Debug` | `NodeMapEntry`, `DebugMapIndex` |

### Compiler source files linked into Core (from `Hrot.Blueprints.Compiler`)

#### Assets/ (namespace `Hrot.Blueprints.Core.Assets`)

| File | Primary types |
|------|---------------|
| `Assets/BlueprintAsset.cs` | `BlueprintAsset`, `BlueprintDispatchKind`, `BlackboardTierHint`, `AiPrimitiveDecl`, `AiPrimitiveIntent`, `AiPrimitiveHosting` |
| `Assets/Declarations.cs` | `VariableDecl`, `ParameterDecl`, `EventDispatcherDecl`, `CustomEventDecl`, `BlueprintTypeRef` |
| `Assets/GraphTypes.cs` | `Graph`, `GraphKind`, `Pin`, `Link`, `AssetMetadata`, `GraphMetadata`, `NodeMetadata`, `Header`, `NodeStatus` |
| `Assets/Nodes.cs` | `Node` (abstract), `FunctionCallNode`, `BranchNode`, `SequenceNode`, `GetVariableNode`, `SetVariableNode`, `LiteralNode`, `EventEntryNode`, `ReturnNode`, `CastNode`, `ArrayMakeNode`, `ArrayGetNode`, `LatentDelayNode`, `CallEventDispatcherNode`, `BindEventDispatcherNode`, `CallCustomEventNode`, `CallPeerBlueprintNode`, `ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode` |

#### Compiler/ (namespace `Hrot.Blueprints.Core.Compiler` and sub-namespaces)

| File | Namespace | Primary types |
|------|-----------|---------------|
| `Compiler/BlueprintCompiler.cs` | `.Compiler` | `IBlueprintCompiler`, `BlueprintCompiler` |
| `Compiler/BlueprintSignature.cs` | `.Compiler` | `BlueprintSignature` |
| `Compiler/BlueprintSignatureParser.cs` | `.Compiler` | `BlueprintSignatureParser` |
| `Compiler/CompileOptions.cs` | `.Compiler` | `CompileOptions` |
| `Compiler/CompileResult.cs` | `.Compiler` | `CompileResult`, `ValidationOptions`, `ValidationResult` |
| `Compiler/IrPrinter.cs` | `.Compiler` | `IrPrinter` |
| `Compiler/Catalogs/CatalogInterfaces.cs` | `.Compiler.Catalogs` | `EngineEventCatalogEntry`, `ChannelCommandCatalogEntry`, `WaitKind`, `WaitPrimitiveCatalogEntry`, `IEngineEventCatalog`, `IChannelCommandCatalog`, `IWaitPrimitiveCatalog` |
| `Compiler/Catalogs/INodeRegistry.cs` | `.Compiler.Catalogs` | `INodeRegistry` |
| `Compiler/Catalogs/ITypeRegistry.cs` | `.Compiler.Catalogs` | `ITypeRegistry` |
| `Compiler/Catalogs/StaticTypeRegistry.cs` | `.Compiler.Catalogs` | `StaticTypeRegistry` |
| `Compiler/Catalogs/BuiltInNodeRegistry.cs` | `.Compiler.Catalogs` | `BuiltInNodeRegistry` |
| `Compiler/Catalogs/BuiltInEngineEventCatalog.cs` | `.Compiler.Catalogs` | `BuiltInEngineEventCatalog` |
| `Compiler/Catalogs/BuiltInChannelCommandCatalog.cs` | `.Compiler.Catalogs` | `BuiltInChannelCommandCatalog` |
| `Compiler/Catalogs/BuiltInWaitPrimitiveCatalog.cs` | `.Compiler.Catalogs` | `BuiltInWaitPrimitiveCatalog` |
| `Compiler/Compatibility/BlueprintCompilerContracts.cs` | `.Compiler` | `CompilerMode`, `BlackboardTier` |
| `Compiler/Compatibility/BlueprintIdHash.cs` | `.Compiler` | `BlueprintIdHash` |
| `Compiler/Compatibility/IsExternalInit.cs` | _(polyfill)_ | `IsExternalInit` |
| `Compiler/Determinism/DeterministicEnumerable.cs` | `.Compiler.Determinism` | `DeterministicEnumerable` |
| `Compiler/Determinism/FnvHasher.cs` | `.Compiler.Determinism` | `FnvHasher` |
| `Compiler/Diagnostics/Diagnostic.cs` | `.Compiler.Diagnostics` | `DiagnosticSeverity`, `Diagnostic` |
| `Compiler/Diagnostics/DiagnosticCodes.cs` | `.Compiler.Diagnostics` | `DiagnosticCodes` |
| `Compiler/Diagnostics/DiagnosticSink.cs` | `.Compiler.Diagnostics` | `DiagnosticSink` |
| `Compiler/Emit/AiPrimitiveEmitter.cs` | `.Compiler.Emit` | `AiPrimitiveEmitter` |
| `Compiler/Emit/BlockEmitter.cs` | `.Compiler.Emit` | `BlockEmitter` |
| `Compiler/Emit/ChannelCommandLowering.cs` | `.Compiler.Emit` | `ChannelCommandLowering` |
| `Compiler/Emit/CSharpEmitter.cs` | `.Compiler.Emit` | `CSharpEmitter` |
| `Compiler/Emit/DebugMapBuilder.cs` | `.Compiler.Emit` | `DebugMap`, `DebugMapEntry`, `DebugMapBuilder` |
| `Compiler/Emit/DebugMapSerializer.cs` | `.Compiler.Emit` | `DebugMapSerializer` |
| `Compiler/Emit/EmissionContext.cs` | `.Compiler.Emit` | `EmissionContext` |
| `Compiler/Emit/InstanceEmitter.cs` | `.Compiler.Emit` | `InstanceEmitter` |
| `Compiler/Emit/LibraryEmitter.cs` | `.Compiler.Emit` | `LibraryEmitter` |
| `Compiler/Emit/Sanitizer.cs` | `.Compiler.Emit` | `Sanitizer` |
| `Compiler/Emit/StatementEmitter.cs` | `.Compiler.Emit` | `StatementEmitter` |
| `Compiler/Emit/TerminatorEmitter.cs` | `.Compiler.Emit` | `TerminatorEmitter` |
| `Compiler/Ir/IrAsset.cs` | `.Compiler.Ir` | `IrField`, `IrCustomEvent`, `IrAsset` |
| `Compiler/Ir/IrBlock.cs` | `.Compiler.Ir` | `IrTerminator` (abstract), `IrTerm_Goto`, `IrTerm_Branch`, `IrTerm_Return`, `IrTerm_ReturnStatus`, `IrTerm_Suspend`, `IrTerm_FallThrough`, `IrBlock` |
| `Compiler/Ir/IrDebugAnnotation.cs` | `.Compiler.Ir` | `IrDebugAnnotation` |
| `Compiler/Ir/IrGraph.cs` | `.Compiler.Ir` | `IrGraphKind`, `IrGraph` |
| `Compiler/Ir/IrOperation.cs` | `.Compiler.Ir` | `IrOperation` (abstract) + 30+ sealed record subtypes |
| `Compiler/Ir/IrStatement.cs` | `.Compiler.Ir` | `IrStatement` |
| `Compiler/Ir/IrTypeRef.cs` | `.Compiler.Ir` | `IrTypeRef` |
| `Compiler/Ir/IrValue.cs` | `.Compiler.Ir` | `IrValue`, `IrBlockId` |
| `Compiler/Lowering/AiPrimitiveLowering.cs` | `.Compiler.Lowering` | `AiPrimitiveLowering` |
| `Compiler/Lowering/DebugProbeInsertion.cs` | `.Compiler.Lowering` | `DebugProbeInsertion` |
| `Compiler/Lowering/FieldLayout.cs` | `.Compiler.Lowering` | `FieldLayout` |
| `Compiler/Lowering/InstanceLowering.cs` | `.Compiler.Lowering` | `InstanceLowering` |
| `Compiler/Lowering/LibraryLowering.cs` | `.Compiler.Lowering` | `LibraryLowering` |
| `Compiler/Lowering/StructureHashComputation.cs` | `.Compiler.Lowering` | `StructureHashComputation` |
| `Compiler/Lowering/SynthesizedGuids.cs` | `.Compiler.Lowering` | `SynthesizedGuids` |
| `Compiler/Lowering/WaitLowering_AiPrimitive.cs` | `.Compiler.Lowering` | `WaitLowering_AiPrimitive` |
| `Compiler/Lowering/WaitLowering_Instance.cs` | `.Compiler.Lowering` | `WaitLowering_Instance` |
| `Compiler/Roslyn/BlueprintCompileException.cs` | `.Compiler.Roslyn` | `BlueprintCompileException` |
| `Compiler/Roslyn/EmbeddedTextHelper.cs` | `.Compiler.Roslyn` | `EmbeddedTextHelper` |
| `Compiler/Roslyn/InMemoryRoslynCompiler.cs` | `.Compiler.Roslyn` | `InMemoryRoslynCompiler` (full Roslyn impl) |
| `Compiler/Roslyn/MetadataReferenceResolver.cs` | `.Compiler.Roslyn` | `MetadataReferenceResolver` |
| `Compiler/Stages/Stage1_Parse.cs` | `.Compiler.Stages` | `Stage1_Parse` |
| `Compiler/Stages/Stage2_Validate.cs` | `.Compiler.Stages` | `Stage2_Validate`, 14 `IValidator` implementations |
| `Compiler/Stages/Stage3_Normalize.cs` | `.Compiler.Stages` | `Stage3_Normalize` |
| `Compiler/Stages/Stage4_TypeResolve.cs` | `.Compiler.Stages` | `Stage4_TypeResolve` |
| `Compiler/Stages/Stage5_Schedule.cs` | `.Compiler.Stages` | `Stage5_Schedule`, `GraphScheduler` |
| `Compiler/Stages/Stage6_Lower.cs` | `.Compiler.Stages` | `Stage6_Lower` |
| `Compiler/Stages/Stage7_Emit.cs` | `.Compiler.Stages` | `Stage7_Emit` |
| `Compiler/Stages/Stage8_RoslynFinalize.cs` | `.Compiler.Stages` | `Stage8_RoslynFinalize` (linked from Core only) |
| `Compiler/Stages/TypedAsset.cs` | `.Compiler.Stages` | `TypedAsset` |
| `Compiler/Stages/ValidationContext.cs` | `.Compiler.Stages` | `ValidationContext` |

---

## Public API Reference

### Namespace `Hrot.Blueprints.Core.Assets`

#### `BlueprintAsset`

Root deserialised representation of a `.blueprint.json` file.

```
public sealed class BlueprintAsset
    Header                      Header                 { get; set; }
    Guid                        AssetId                { get; set; }
    string                      Name                   { get; set; }
    BlueprintDispatchKind       Dispatch               { get; set; }
    BlackboardTierHint          TierHint               { get; set; }
    bool                        IsWorldSingleton       { get; set; }
    AiPrimitiveDecl?            Primitive              { get; set; }   // AiPrimitive only
    List<ParameterDecl>         Parameters             { get; set; }   // AiPrimitive only
    List<VariableDecl>          WorkingState           { get; set; }   // AiPrimitive only
    List<VariableDecl>          Variables              { get; set; }   // Instance only
    List<EventDispatcherDecl>   EventDispatchers       { get; set; }   // Instance only
    List<CustomEventDecl>       CustomEvents           { get; set; }   // Instance only
    List<Guid>                  CallablePeers          { get; set; }   // Instance only
    List<Graph>                 Graphs                 { get; set; }
    AssetMetadata               EditorMetadata         { get; set; }
```

#### `BlueprintDispatchKind` (enum)

`Library`, `AiPrimitive`, `Instance`

#### `BlackboardTierHint` (enum)

`Auto`, `Force1024`, `Force4096`, `Force16384`

#### `Graph`

```
public sealed class Graph
    Guid                Id             { get; set; }
    string              Name           { get; set; }
    GraphKind           Kind           { get; set; }
    List<ParameterDecl> Inputs         { get; set; }
    List<ParameterDecl> Outputs        { get; set; }
    List<Node>          Nodes          { get; set; }
    List<Link>          Links          { get; set; }
    GraphMetadata       EditorMetadata { get; set; }
```

#### `GraphKind` (enum)

`Function`, `Event`, `Construction`

#### `Node` (abstract, JSON-polymorphic)

Discriminator property `"kind"`.  Derived types:

| JSON kind | C# type | Notable properties |
|-----------|---------|--------------------|
| `FunctionCall` | `FunctionCallNode` | `TargetTypeId`, `MethodName`, `IsPure` |
| `Branch` | `BranchNode` | -- |
| `Sequence` | `SequenceNode` | -- |
| `GetVariable` | `GetVariableNode` | `VariableId` |
| `SetVariable` | `SetVariableNode` | `VariableId` |
| `Literal` | `LiteralNode` | `TypeId`, `ValueJson` |
| `EventEntry` | `EventEntryNode` | `EventTypeId` |
| `Return` | `ReturnNode` | `Status` |
| `Cast` | `CastNode` | `TargetTypeId` |
| `ArrayMake` | `ArrayMakeNode` | `ElementTypeId` |
| `ArrayGet` | `ArrayGetNode` | -- |
| `Delay` | `LatentDelayNode` | -- |
| `CallDispatcher` | `CallEventDispatcherNode` | `DispatcherId` |
| `BindDispatcher` | `BindEventDispatcherNode` | `DispatcherId` |
| `CallCustomEvent` | `CallCustomEventNode` | `EventId` |
| `CallPeerBlueprint` | `CallPeerBlueprintNode` | `PeerBlueprintId`, `FunctionRef` |
| `ChannelCommand` | `ChannelCommandNode` | `ChannelType`, `ActionId` |
| `WaitForChannel` | `WaitForChannelNode` | `ChannelType` |
| `WaitForEvent` | `WaitForEventNode` | `EventTypeId`, `FilterByField`, `CorrelationField` |

#### `Pin`

```
public sealed class Pin
    Guid              Id          { get; set; }
    string            Name        { get; set; }
    string            Direction   { get; set; }   // "Input" | "Output"
    BlueprintTypeRef  TypeRef     { get; set; }
    bool              IsExec      { get; set; }
    List<Guid>        LinkedToIds { get; set; }
```

#### `BlueprintTypeRef`

```
public sealed class BlueprintTypeRef
    string                   TypeId      { get; set; }
    bool                     IsArray     { get; set; }
    List<BlueprintTypeRef>   GenericArgs { get; set; }
```

#### `VariableDecl` / `ParameterDecl`

Both carry `Id`, `Name`, `Type` (`BlueprintTypeRef`), `DefaultValueJson`.
`VariableDecl` additionally has `IsEditable`, `IsExposedOnSpawn`, `Category`, `Tooltip`.

---

### Namespace `Hrot.Blueprints.Core.Compiler`

#### `IBlueprintCompiler`

```csharp
public interface IBlueprintCompiler
{
    CompileResult    Compile (BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null);
}
```

#### `BlueprintCompiler`

Default implementation of `IBlueprintCompiler`.  Runs Stages 2-7 sequentially; Stage 8
(Roslyn) is invoked separately when the caller requires a PE.

#### `CompileOptions` (record)

```
CompilerMode                         Mode
INodeRegistry                        NodeRegistry
ITypeRegistry                        TypeRegistry
IEngineEventCatalog                  EngineEvents
IChannelCommandCatalog               ChannelCommands
IWaitPrimitiveCatalog                WaitPrimitives
IReadOnlyList<BlueprintSignature>    SiblingSignatures
bool                                 EmitPdbWithEmbeddedSource  (default: false)
string?                              VirtualSourcePath          (default: null)
```

#### `CompileResult` (record)

```
bool                          Succeeded
string?                       GeneratedSource
string?                       GeneratedFileName
int                           BlueprintId
ulong                         StructureHash
DebugMap?                     DebugMap
IReadOnlyList<Diagnostic>     Diagnostics
BlueprintAsset?               CanonicalAsset
byte[]?                       PortablePdb
byte[]?                       PortablePe
```

#### `CompilerMode` (enum)

| Value | Meaning |
|-------|---------|
| `Debug` | Inserts `NodeEnter` probes into generated code. |
| `Release` | No probes; no debug map. |
| `Trace` | `Debug` + per-pin `PinValueChanged` probes after each value-producing statement. |

#### `BlackboardTier` (enum)

`Blackboard1024`, `Blackboard4096`, `Blackboard16384`.  Selects the ECS component size for
the entity-attached blackboard used by Instance Blueprints.

#### `BlueprintSignature` (record)

Lightweight cross-asset reference descriptor.

```
string                         Path
Guid                           AssetId
string                         Name
string                         SanitizedName
int                            BlueprintId
BlueprintDispatchKind          Dispatch
IReadOnlyList<string>          ExportedFunctionNames
IReadOnlyList<AiPrimitiveHosting> Hostings
IReadOnlyList<Guid>            DeclaredCallablePeers
```

#### `BlueprintIdHash`

```csharp
public static int Compute(Guid assetId);   // FNV-1 32-bit hash of AssetId bytes
```

---

### Namespace `Hrot.Blueprints.Core.Compiler.Catalogs`

#### `ITypeRegistry`

```csharp
bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType);
bool TryGetCoercion(IrTypeRef from, IrTypeRef to, out string coercionExpression);
```

`StaticTypeRegistry.Instance` provides a default registry covering all C# primitives,
`System.Numerics.*` vector types, `Fdp.Core.Entity`, and common HROT component types.

#### `IEngineEventCatalog` / `IChannelCommandCatalog` / `IWaitPrimitiveCatalog`

Read-only catalogs that expose `GetEntries()`.  Built-in singletons:
`BuiltInEngineEventCatalog.Instance`, `BuiltInChannelCommandCatalog.Instance`,
`BuiltInWaitPrimitiveCatalog.Instance`.

---

### Namespace `Hrot.Blueprints.Core.Compiler.Ir`

The intermediate representation is a flat, block-structured IR inspired by SSA form.
Values are indexed integers (`IrValue.Index`); blocks contain linear `IrStatement` lists
terminated by a single `IrTerminator`.

#### `IrAsset` (record)

Top-level IR container.  Key fields: `AssetId`, `SanitizedName`, `BlueprintId`,
`StructureHash`, `Dispatch`, `Parameters`, `WorkingState`, `Variables`, `CustomEvents`,
`Graphs`.

#### `IrGraph` (record)

`Id`, `Name`, `Kind` (`IrGraphKind`), `Inputs`, `Outputs`, `Blocks`, `Entry`.

#### `IrBlock` (record)

`Id` (`IrBlockId`), `Label`, `Statements`, `Terminator`.

#### `IrStatement` (record)

`ResultValue` (`IrValue?`), `Operation` (`IrOperation`), `Debug` (`IrDebugAnnotation`).

#### `IrOperation` (abstract record, 30+ subtypes)

Selected subtypes:

| Type | Purpose |
|------|---------|
| `IrOp_Const` | Inline C# literal |
| `IrOp_ReadParam` / `IrOp_ReadVariable` | Field reads |
| `IrOp_WriteVariable` | Field write (side-effect statement) |
| `IrOp_PureCall` | Method call without entity side-effects |
| `IrOp_LibraryCall` | Call into another Library Blueprint |
| `IrOp_PeerCall` | Call into a peer Instance Blueprint |
| `IrOp_GetComponent` / `IrOp_GetComponentRO` | ECS component read |
| `IrOp_AddComponent` / `IrOp_RemoveComponent` | ECB write |
| `IrOp_PublishEvent` | Simulation event publish |
| `IrOp_ChannelCommand` | Typed command to a channel component |
| `IrOp_WaitForChannel` / `IrOp_WaitForEvent` | Latent wait primitives |
| `IrOp_DebugProbe_NodeEnter` | Probe inserted by `DebugProbeInsertion` in Debug/Trace mode |
| `IrOp_DebugProbe_PinValue` | Probe inserted in Trace mode only |

#### `IrTerminator` (abstract record, 6 subtypes)

| Type | Purpose |
|------|---------|
| `IrTerm_Goto` | Unconditional branch |
| `IrTerm_Branch` | Conditional branch (true/false targets) |
| `IrTerm_Return` | Return a value |
| `IrTerm_ReturnStatus` | Return `NodeStatus` (AiPrimitive graphs) |
| `IrTerm_Suspend` | Yield control (latent / coroutine resume) |
| `IrTerm_FallThrough` | Implicit fall to next block |

#### `IrTypeRef` (record)

```
string   FullName
bool     IsArray
IrTypeRef? ElementType
bool     IsUnmanaged
int      SizeBytes
bool     IsEntityHandle
```

#### `IrField` (record)

`Id`, `Name`, `Type` (`IrTypeRef`), `DefaultValueCSharp`, `Offset`, `Size`.
Offsets are assigned by `FieldLayout.ComputeFieldLayouts` in Stage 6.

---

### Namespace `Hrot.Blueprints.Core.Compiler.Diagnostics`

#### `Diagnostic` (record)

```
DiagnosticSeverity   Severity
string               Code
string               Message
Guid?                AssetId, GraphId, NodeId, PinId   (optional location)
bool                 IsError   { get; }
```

Static factories: `Diagnostic.Error(...)`, `Diagnostic.Warning(...)`,
`Diagnostic.Info(...)`.

#### `DiagnosticCodes`

Public constants for all compiler diagnostic codes.  Series:

| Series | Stage |
|--------|-------|
| `BP0001`-`BP0011` | Stage 1 -- JSON parse |
| `BP1010`-`BP1031` | Stage 2 -- asset structure & link validation |
| `BP1100`-`BP1101` | Stage 2 -- AiPrimitive intent rules |
| `BP1200`-`BP1211` | Stage 2 -- variables and state |
| `BP1300`-`BP1302` | Stage 2 -- peer references |
| `BP1400`-`BP1402` | Stage 2 -- catalog references |
| `BP1500`-`BP1503` | Stage 2 -- type references |

---

### Namespace `Hrot.Blueprints.Core.Compiler.Emit`

#### `DebugMap` (record)

Immutable product of Stage 7.  Maps generated C# line numbers back to graph node Guids.

```
Guid                           AssetId
int                            BlueprintId
ulong                          StructureHash
IReadOnlyList<DebugMapEntry>   Entries
```

#### `DebugMapEntry` (record)

`NodeId`, `GraphId`, `StartLine`, `EndLine`, `NodeKind`, `DisplayName`, `PhaseIndex`.

---

### Namespace `Hrot.Blueprints.Core.Compiler.Roslyn`

#### `InMemoryRoslynCompiler` (full implementation, linked into Core)

```csharp
public sealed class InMemoryRoslynCompiler
{
    public InMemoryRoslynCompiler(MetadataReferenceResolver references);
    public (byte[] Pe, byte[] Pdb) Compile(
        string source,
        string virtualSourcePath,
        string assemblyName,
        DiagnosticSink sink);
}
```

Compiles the C# source string produced by Stage 7 using
`Microsoft.CodeAnalysis.CSharp` with `OptimizationLevel.Debug`, `deterministic: true`,
`allowUnsafe: true`, and embedded source text in the PDB.

#### `MetadataReferenceResolver`

Resolves `MetadataReference` objects for all assemblies that generated Blueprint code
needs to reference at Roslyn compile time.

---

### Namespace `Hrot.Blueprints.Core.Debug`

#### `IBlueprintProbeSink`

Thin interface that generated Blueprint code calls via `DebugProbe`:

```csharp
public interface IBlueprintProbeSink
{
    void OnNodeEnter(Entity self, string nodeId);
    void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged;
    void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName);
    void OnPeerCallExit(Entity entity);
}
```

#### `DebugProbe` (static)

Global dispatcher that generated code calls at runtime.

```csharp
public static class DebugProbe
{
    public static IBlueprintProbeSink? Sink { get; set; }
    public static void NodeEnter(Entity self, string nodeId);
    public static void PinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged;
    public static void PeerCallEnter(Entity self, string targetAssetName,
        string targetGraphName);
    public static void PeerCallExit(Entity self);
}
```

When `Sink` is `null`, all calls are no-ops with zero allocation.  The assignment is
a single reference write, which is atomic on 64-bit platforms; no lock is required.

#### `NullProbeSink`

Singleton no-op implementation.  Use when a non-null sink is required but no session is
attached: `DebugProbe.Sink = NullProbeSink.Instance;`

#### `IBlueprintTimeController`

Engine-side time control for the debugger (soft-pause semantics: methods return
immediately; halt occurs on the next engine tick).

```csharp
public interface IBlueprintTimeController
{
    bool IsPausedByDebugger { get; }
    void RequestPause();
    void RequestResume();
    void RequestStepOneTick();
}
```

#### `IBlueprintDebugSession`

Full debug session interface; extends `IBlueprintProbeSink`.

```csharp
public interface IBlueprintDebugSession : IBlueprintProbeSink
{
    // Lifecycle
    bool IsAttached { get; }
    void Detach();

    // Breakpoints
    BreakpointId             SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId);
    void                     ClearBreakpoint(BreakpointId id);
    void                     ClearAllBreakpoints();
    IReadOnlyList<Breakpoint> GetBreakpoints();
    bool                     IsAnyBreakpointActive { get; }

    // Watches
    WatchId                  AddWatch(Guid assetId, Guid graphId, Guid pinId,
                                      string displayName, Type expectedType);
    void                     RemoveWatch(WatchId id);
    void                     ClearAllWatches();
    IReadOnlyList<Watch>     GetWatches();
    bool                     IsAnyWatchActive { get; }

    // Entity filter
    void     SetEntityFilter(Entity? entity);
    Entity?  GetEntityFilter();

    // Active entity tracking
    IReadOnlyList<Entity>    GetActiveEntities(Guid assetId);

    // Pause state
    bool        IsPaused          { get; }
    Breakpoint? PausedAt          { get; }
    Entity?     PausedOnEntity    { get; }

    // Pause control (all return immediately -- soft-pause)
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();
    void Pause();

    // Inspection
    BlueprintStateSnapshot?       GetCurrentStateSnapshot();
    IReadOnlyList<NodeExecuted>   GetRecentNodeHistory(int maxCount = 100);

    // Map registration
    void RegisterDebugMap(DebugMap map);
    void UnregisterDebugMap(Guid assetId);

    // PDB locator
    void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver);

    // Hot reload
    void OnHotReloadBegin();
    void OnHotReloadCompleted(Guid[] reloadedAssetIds);

    // Events
    event Action<BreakpointHit>?  OnBreakpointHit;
    event Action<NodeExecuted>?   OnNodeExecuted;
    event Action<PinValueChanged>? OnPinValueChangedEvent;
    event Action?                 OnSessionStateChanged;
    event Action<Guid>?           OnBreakpointListChanged;
}
```

#### `Watch`

Encapsulates a live value observation.  Uses a 64-byte pre-allocated `byte[]` buffer;
`WriteValue<T>` is zero-allocation for unmanaged types of up to 64 bytes.

Key members: `Id`, `AssetId`, `GraphId`, `PinId`, `DisplayName`, `ExpectedType`,
`LastValueBytes` (`ReadOnlySpan<byte>`), `LastUpdateTick`, `UpdateCount`,
`HasEverBeenWritten`, `IsStale`.

#### `ExecutionHistory`

Per-entity ring-buffer of `NodeHistoryEntry` values.

```csharp
public sealed class ExecutionHistory
{
    public ExecutionHistory(int capacity = 256);
    public void Record(NodeHistoryEntry entry);   // zero-allocation
    public IReadOnlyList<NodeHistoryEntry> GetRecent(int maxCount);  // oldest-first
}
```

#### `DebugMapIndex`

Immutable O(1) lookup index built from a `DebugMap`.  Supports lookup by string node-id
(the hot probe path) and by `Guid` (the editor UI path).

```csharp
public sealed class DebugMapIndex
{
    public DebugMapIndex(DebugMap map);
    public NodeMapEntry? TryResolveNode(string nodeIdString);
    public NodeMapEntry? TryResolveNode(Guid nodeId);
    public IReadOnlyCollection<NodeMapEntry> AllNodes { get; }
    public Guid   AssetId       { get; }
    public string AssetName     { get; }
    public ulong  StructureHash { get; }
}
```

---

## Dependencies

### Project references

| Dependency | Notes |
|------------|-------|
| `Hrot.Blueprints.Compiler` | Full asset schema, compiler stages, IR, and emit code. In Core the Roslyn-specific sources (`Compiler/Roslyn/` and `Stage8_RoslynFinalize.cs`) are linked via `<Compile Include=...>` so that the runtime assembly includes Roslyn support while the netstandard2.0 Compiler project does not carry that dependency. |
| `FDP/Engine/Fdp.Core` | Provides `Entity` and ECS fundamentals used throughout the probe and debug interfaces. |
| `FDP/Toolkits/Fdp.Toolkits` | Blueprint toolkit integration (blackboard tiers, event catalogs). |

### NuGet packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.CodeAnalysis.CSharp` | 4.8.0 | Roslyn C# compiler API (Stage 8, in-memory compilation). |

The `Hrot.Blueprints.Compiler` project additionally references:
- `System.Runtime.Loader` 4.3.0 -- collectible `AssemblyLoadContext`
- `System.Text.Json` 8.0.5 -- Blueprint JSON deserialization

---

## Stage Detail

### Stage 1 -- Parse

Deserialises a JSON string into a `BlueprintAsset` using `System.Text.Json`.  Emits
`BP0001` (null result) or `BP0002` (parse exception).  Also performs basic null-guard
checks (`BP0010`, `BP0011`).

### Stage 2 -- Validate

Runs 14 independent `IValidator` implementations sequentially.  Stops early on fatal
errors.  Validators cover:

- `V_AssetStructure` -- non-empty `AssetId`, `Name`, valid `Dispatch`
- `V_DispatchKindCompatibility` -- ensures fields match dispatch kind
- `V_NodeStructure` -- pin count, exec-pin rules
- `V_LinkStructure` -- link endpoint existence and direction compatibility
- `V_GraphStructure` -- graph kind constraints
- `V_VariablesAndState` -- unique IDs, type present
- `V_AiPrimitiveIntent` -- intent/graph-kind consistency
- `V_LatentRules` -- latent nodes only in Instance event graphs
- `V_ChannelCommandReferences` -- catalog lookup
- `V_EventGraphReferences` -- event type catalog lookup
- `V_WaitNodeReferences` -- wait primitive catalog lookup
- `V_PeerReferences` -- declared peer Guids present in `SiblingSignatures`
- `V_TypeReferences` -- all pin type IDs resolvable
- `V_DeterminismOrdering` -- (reserved)

### Stage 3 -- Normalize

Three passes:
1. Materialise default pin literals (stub in Slice 1; no-op).
2. Insert implicit `CastNode` instances on links where types are coercible.
3. Remove orphan nodes (nodes with no execution connection).

### Stage 4 -- Type Resolve

Two-pass type resolution: first pass resolves concrete types via `ITypeRegistry`;
second pass propagates wildcard types through `ArrayMakeNode` / `ArrayGetNode`.
Unresolved types after both passes emit `BP1500` or `BP1502`.

### Stage 5 -- Schedule

Converts each `Graph` into an `IrGraph` via `GraphScheduler`.  The scheduler performs
topological ordering of the node DAG, allocates SSA-like value indices, and emits
`IrStatement` / `IrOperation` sequences grouped into `IrBlock`s.  Blocks are linked by
`IrTerminator` records.

Also assigns `BlueprintId` (FNV-1 hash of `AssetId`), builds `IrField` lists for
parameters, working state, variables, and custom events.

### Stage 6 -- Lower

Four sub-passes applied in order:
1. **Dispatch lowering** -- dispatch-specific structural transformations:
   - `LibraryLowering` -- marks graphs as pure where applicable
   - `AiPrimitiveLowering` + `WaitLowering_AiPrimitive` -- synthesises `__phase` field,
     converts `IrTerm_Suspend` into phase-indexed switch blocks
   - `InstanceLowering` + `WaitLowering_Instance` -- same pattern for Instance graphs
2. **`FieldLayout`** -- assigns byte `Offset` and `Size` to all `IrField`s using
   alignment rules (1/2/4/8 bytes).  `Parameters` start at offset 0, `WorkingState` at 8,
   `Variables` at 16.
3. **`StructureHashComputation`** -- 64-bit FNV-1a hash over field names, types, offsets,
   and sizes.  Stored in `IrAsset.StructureHash` and propagated to the generated class and
   `DebugMap`.
4. **`DebugProbeInsertion`** -- inserts `IrOp_DebugProbe_NodeEnter` at the start of each
   block in Debug mode.  In Trace mode, additionally inserts `IrOp_DebugProbe_PinValue`
   after each value-producing statement that has a pin annotation.  No-op in Release mode.

### Stage 7 -- Emit

`CSharpEmitter` dispatches to `LibraryEmitter`, `AiPrimitiveEmitter`, or
`InstanceEmitter` based on dispatch kind.  Each emitter calls `BlockEmitter`,
`StatementEmitter`, and `TerminatorEmitter` to generate idiomatic C# with `goto`-based
control flow for latent blocks.  `DebugMapBuilder` records line-number spans during
emission; `Build()` returns the final `DebugMap`.

Generated file name: `{SanitizedName}_{BlueprintId:X8}_Bp.g.cs`

### Stage 8 -- Roslyn Finalize (Core only)

Wraps `Roslyn.InMemoryRoslynCompiler`.  Parses the generated source into a
`CSharpSyntaxTree`, creates a `CSharpCompilation` with `deterministic: true`,
`allowUnsafe: true`, emits to in-memory `MemoryStream`s, and returns raw `byte[]` PE and
PDB arrays.  Roslyn diagnostics are forwarded to `DiagnosticSink`.

---

## Usage Examples

### Example 1 -- Compile a Blueprint from JSON

```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;

string json = File.ReadAllText("Assets/MoveToTarget.blueprint.json");

// Stage 1: parse JSON
var parseSink = new DiagnosticSink();
BlueprintAsset? asset = Stage1_Parse.Run(json, parseSink);
if (asset is null)
{
    foreach (var d in parseSink.All)
        Console.Error.WriteLine($"[{d.Code}] {d.Message}");
    return;
}

// Stages 2-7: compile to C# source
var options = new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>());

var compiler = new BlueprintCompiler();
CompileResult result = compiler.Compile(asset, options);

if (!result.Succeeded)
{
    foreach (var d in result.Diagnostics)
        Console.Error.WriteLine($"[{d.Code}] {d.Message}");
    return;
}

Console.WriteLine(result.GeneratedSource);
// result.DebugMap contains node-to-line mapping
```

### Example 2 -- Attach the debug probe to a session

```csharp
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Core.Compiler.Emit;

// Assuming 'session' is an IBlueprintDebugSession implementation.
IBlueprintDebugSession session = GetOrCreateDebugSession();

// Register the compiled asset's debug map so the session can resolve node IDs.
DebugMap debugMap = compiledResult.DebugMap!;
session.RegisterDebugMap(debugMap);

// Wire the global probe sink.
DebugProbe.Sink = session;

// Set a breakpoint on a specific node.
Guid assetId  = debugMap.AssetId;
Guid graphId  = debugMap.Entries[0].GraphId;
Guid nodeId   = debugMap.Entries[0].NodeId;
BreakpointId bp = session.SetBreakpoint(assetId, graphId, nodeId);

// Subscribe to the hit event.
session.OnBreakpointHit += hit =>
{
    Console.WriteLine($"Hit breakpoint on entity {hit.Self} at tick {hit.Tick}");
    session.StepOver();
};
```

### Example 3 -- Inspect recent execution history and watches

```csharp
using Hrot.Blueprints.Core.Debug;
using Fdp.Core;

IBlueprintDebugSession session = GetAttachedSession();

// Add a watch for a pin by its Guid.
Guid pinGuid = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
WatchId watchId = session.AddWatch(
    assetId:     knownAssetId,
    graphId:     knownGraphId,
    pinId:       pinGuid,
    displayName: "MoveSpeed",
    expectedType: typeof(float));

// After several simulation ticks, inspect the watch value.
IReadOnlyList<Watch> watches = session.GetWatches();
foreach (Watch w in watches)
{
    if (w.HasEverBeenWritten)
    {
        ReadOnlySpan<byte> bytes = w.LastValueBytes;
        float value = System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes);
        Console.WriteLine($"{w.DisplayName} = {value} (tick {w.LastUpdateTick})");
    }
}

// Retrieve recent node execution history.
IReadOnlyList<NodeExecuted> history = session.GetRecentNodeHistory(maxCount: 20);
foreach (NodeExecuted ne in history)
    Console.WriteLine($"  {ne.NodeIdString} @ tick {ne.Tick}");
```

### Example 4 -- Build a DebugMapIndex for fast node lookup

```csharp
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Core.Compiler.Emit;

DebugMap map    = compiledResult.DebugMap!;
var index = new DebugMapIndex(map);

// Fast lookup from the hot probe path (string Guid, "D" format).
string nodeIdStr = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
NodeMapEntry? entry = index.TryResolveNode(nodeIdStr);
if (entry is not null)
{
    Console.WriteLine($"Node '{entry.DisplayName}' ({entry.NodeKind}) "
        + $"at source lines {entry.SourceStartLine}-{entry.SourceEndLine}");
}
```

---

## Best Practices

1. **Always check `CompileResult.Succeeded`** before using `GeneratedSource` or
   `DebugMap`.  Both can be `null` on failure.

2. **Prefer `CompilerMode.Release` in production builds** to eliminate all probe
   overhead.  Switch to `Debug` or `Trace` only for sessions with an attached
   `IBlueprintDebugSession`.

3. **Keep `DebugProbe.Sink` null when no session is active.**  The generated code guards
   every call with a null-check; a null sink costs nothing beyond the null-check itself.

4. **Register `DebugMap` before attaching the probe sink.**  `IBlueprintDebugSession`
   implementations use the map to resolve node IDs as probes arrive.  Attaching the sink
   before registering the map can result in unresolvable node IDs in the first few ticks.

5. **Do not hold strong references to `Watch.LastValueBytes`.**  The `ReadOnlySpan<byte>`
   is a view into the watch's internal 64-byte buffer.  It becomes stale the next time
   `WriteValue<T>` is called on that watch.

6. **Use `ExecutionHistory` capacity of 256 (the default) for most entities.**  The
   `Record` call is zero-allocation; reduce capacity only if memory pressure is a
   concern on embedded targets.

7. **`StructureHash` is your hot-reload guard.**  Before a hot-reloaded assembly is
   accepted, compare the new `CompileResult.StructureHash` with the hash stored in the
   running entity's blackboard component.  A mismatch means the field layout has changed
   and state migration is required.

8. **Pass `SiblingSignatures` when compiling assets that call peer Blueprints.**  Stage 2
   (`V_PeerReferences`) validates all `CallPeerBlueprintNode` targets against the list.
   An empty list will cause BP1300-series errors for any asset that references peers.

9. **Treat diagnostic codes as stable API.**  The codes defined in `DiagnosticCodes` are
   intended to be stable across compiler versions.  Do not compare against the
   `Message` string; compare against the `Code` constant.

10. **Do not hold references to `BlueprintAsset` after compilation.**  The normalisation
    and lowering stages mutate the asset in place.  Retain `CompileResult.CanonicalAsset`
    if you need the post-normalised form; discard the original `BlueprintAsset`.

---

## Related Projects

```
+---------------------------+         +-----------------------------+
|  Hrot.Blueprints.Core     |<--------| Hrot.Blueprints.Compiler    |
|  (this project)           |         | (asset schema + pipeline    |
|  runtime + Roslyn stages  |         |  minus Roslyn stages,       |
+---------------------------+         |  netstandard2.0)            |
          ^                           +-----------------------------+
          |
          |  referenced by
          |
+---------+------------------+        +-----------------------------+
|  Hrot.Blueprints.Editor    |        | Hrot.Blueprints.Generators  |
|  (visual graph editor,     |        | (source generators for      |
|  breakpoint UI, watches)   |        |  Blueprints integration)    |
+----------------------------+        +-----------------------------+

          |                                         |
          | both tested by                          |
          v                                         v
+-----------------------------+
|  Hrot.Blueprints.Tests      |
|  (unit + integration tests  |
|  InternalsVisibleTo granted)|
+-----------------------------+

External dependencies:
+-------------------+        +-------------------+
|  FDP/Fdp.Core     |        |  FDP/Fdp.Toolkits |
|  Entity, ECS      |        |  Toolkit contracts |
+-------------------+        +-------------------+

+--------------------------------------------+
|  Microsoft.CodeAnalysis.CSharp 4.8.0       |
|  (Roslyn -- Stage 8 in-memory compilation) |
+--------------------------------------------+
```

| Project | Relationship |
|---------|--------------|
| `Hrot.Blueprints.Compiler` | Source-link donor.  Core embeds the Roslyn stages and `Stage8_RoslynFinalize.cs` via `<Compile Include>`.  The Compiler project itself targets netstandard2.0 and deliberately excludes those files. |
| `Hrot.Blueprints.Editor` | Consumes `IBlueprintDebugSession`, `IBlueprintTimeController`, `DebugMapIndex`, and the full asset model to render the visual graph editor and debugger UI. |
| `Hrot.Blueprints.Generators` | Source generators that produce integration glue (e.g. catalog registrations) consumed by other HROT subsystems. |
| `Hrot.Blueprints.Tests` | Test assembly granted `InternalsVisibleTo` access to both Core and Compiler; exercises all compiler stages and the debug layer. |
| `FDP/Fdp.Core` | Provides the `Entity` value type used in all probe and debug interfaces. |
| `FDP/Fdp.Toolkits` | Provides `BlueprintDispatchKind` mirror enum and blackboard-tier contracts consumed at runtime. |
