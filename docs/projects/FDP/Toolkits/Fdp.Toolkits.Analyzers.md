# Fdp.Toolkits.Analyzers

**Project path**: `FDP/Toolkits/Fdp.Toolkits.Analyzers/Fdp.Toolkits.Analyzers.csproj`
**Date**: 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary reference.

---

## Executive Overview

`Fdp.Toolkits.Analyzers` is a Roslyn analyzer and source-generator assembly that enforces
FDP-specific rules at **compile time** and generates **boilerplate registration code** so that
no runtime reflection or hand-written dispatch tables are needed.

The project contains **one pure diagnostic analyzer** and **four source generators**:

| Component                    | Kind                   | Primary concern                                          |
|------------------------------|------------------------|----------------------------------------------------------|
| `BehaviorParameterSizeAnalyzer` | DiagnosticAnalyzer  | Memory-safety: DTO size <= 100 bytes in BrainBlackboard  |
| `BTreeActionGenerator`       | IIncrementalGenerator  | Emit `FbtActionRegistrar.g.cs` for BTree action dispatch |
| `BTreeDefinitionGenerator`   | IIncrementalGenerator  | Emit `FbtTreeCatalog.g.cs` for named tree catalog        |
| `HsmActionGenerator`         | IIncrementalGenerator  | Emit `HsmActionDispatcher/Registrar.g.cs` for HSM        |
| `GizmoRegistrarGenerator`    | ISourceGenerator       | Emit per-namespace `GizmoRegistrar.g.cs` files           |

### Why these rules matter

**BrainBlackboard layout** is a fixed 128-byte unmanaged struct partitioned into three
adjacent regions: `BehaviorParameters` (100 bytes), `SoftAdvice`, and `Interrupt`.  Any DTO
written into the `BehaviorParameters` region that is larger than 100 bytes silently overwrites
the `SoftAdvice` and `Interrupt` registers, causing non-obvious runtime bugs that would be
extremely difficult to diagnose without the compile-time enforcement provided by `FDP_001`.

The source generators eliminate a class of maintenance problems:

- **BTree**: without `BTreeActionGenerator`, every new `[BTreeAction]` or `[BTreeCondition]`
  method would require a manual entry in a dispatch table. Mistakes are silent (wrong key string
  -> action never fires).
- **HSM**: without `HsmActionGenerator`, function-pointer tables in the kernel dispatcher must
  be manually updated and are unsafe to get wrong (wrong `ushort` hash -> wrong action executes).
- **Gizmos**: without `GizmoRegistrarGenerator`, every `[GizmoProjector]` class must be
  manually added to a registry, and missing entries produce no error at compile time.

---

## Architecture

### How Roslyn Analyzers and Source Generators Work

The C# compiler exposes a **Compiler Platform (Roslyn) API** that allows user-authored code to
participate in compilation.  There are two extension points used in this project:

1. **DiagnosticAnalyzer** (`Microsoft.CodeAnalysis.Diagnostics`):
   Runs during semantic analysis.  Reports additional diagnostics (errors, warnings, info) based
   on the semantic model.  Cannot produce new source; only gates or warns about existing code.

2. **ISourceGenerator / IIncrementalGenerator** (`Microsoft.CodeAnalysis`):
   Runs after syntax parsing and semantic binding.  May inspect the compilation and add new
   `.cs` source files.  The incremental variant (`IIncrementalGenerator`) uses a pipeline of
   `IncrementalValueProvider<T>` steps so that only changed inputs are reprocessed on
   incremental builds - essential for IDE responsiveness.

Both kinds are delivered to the compiler via the `Analyzer` item group in a `.csproj`, or by
referencing a project that has `<IsRoslynComponent>true</IsRoslynComponent>`.  Because Roslyn
analyzers must run inside the compiler host process (which may target any .NET runtime), they
must target `netstandard2.0` and must not reference assemblies that target net8.0.

### DiagnosticAnalyzer Pattern

```
Compilation start
       |
       v
  RegisterSymbolAction / RegisterSyntaxNodeAction
       |
       v
  For each matching symbol/node: call analysis callback
       |
       v
  context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args...))
       |
       v
  Compiler merges all diagnostics -> IDE squiggles / build errors
```

### Incremental Generator Pipeline

```
SyntaxProvider.CreateSyntaxProvider
    predicate: (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0
    transform: (ctx, _)  => GetMethodInfo(ctx)   -- returns null to filter
        |
        v
    .Where(m => m != null)
        |
        v
    .Combine(CompilationProvider)
        |
        v
    RegisterSourceOutput -> Execute(SourceProductionContext, Compilation, ImmutableArray<T>)
        |
        v
    context.AddSource("FileName.g.cs", generatedSourceText)
```

---

## ASCII Block Diagrams

### Diagram 1: Project role in the build pipeline

```
+---------------------------+     +----------------------------+     +--------------------------+
|  User project (.csproj)   |     |  Fdp.Toolkits.Analyzers    |     |  C# Compiler (Roslyn)    |
|                           |     |  (Analyzer reference)      |     |                          |
|  [BTreeAction] methods    +---->+  BTreeActionGenerator      +---->+  FbtActionRegistrar.g.cs |
|  [HsmAction]   methods    +---->+  HsmActionGenerator        +---->+  HsmActionRegistrar.g.cs |
|  [BTreeDefinition] methods+---->+  BTreeDefinitionGenerator  +---->+  FbtTreeCatalog.g.cs     |
|  [GizmoProjector] classes +---->+  GizmoRegistrarGenerator   +---->+  *_GizmoRegistrar.g.cs   |
|  [SharedAiAction] methods +---->+  BehaviorParameterSize     +---->+  FDP_001 error if DTO    |
|                           |     |    Analyzer                |     |  exceeds 100 bytes       |
+---------------------------+     +----------------------------+     +--------------------------+
```

### Diagram 2: BrainBlackboard memory layout enforced by FDP_001

```
+----------------------------------------------+  offset 0
|  BehaviorParameters  (100 bytes)             |
|                                              |
|  [SharedAiAction(typeof(MyDto), "Field")]    |
|  MyDto must fit entirely inside this region  |
+----------------------------------------------+  offset 100
|  SoftAdvice  (N bytes)                       |
+----------------------------------------------+
|  Interrupt   (N bytes)                       |
+----------------------------------------------+  offset 128
```

If `sizeof(MyDto) > 100` the `BehaviorParameterSizeAnalyzer` emits `FDP_001` (error) and the
build fails, preventing the struct from overflowing into the adjacent regions.

### Diagram 3: BTreeActionGenerator -- from attribute to generated registrar

```
Source method with attribute           Generator output (FbtActionRegistrar.g.cs)
+-------------------------------+      +------------------------------------------+
| [BTreeAction]                 |      | public static class FbtActionRegistrar   |
| static NodeStatus Move(       |      | {                                        |
|     ref BB bb,                |      |   public static void RegisterAll(        |
|     ref BTS st,               | ---> |       ActionRegistry<BB,TC> registry)    |
|     ref TC ctx,               |      |   {                                      |
|     int pi) { ... }           |      |     registry.Register(                   |
+-------------------------------+      |       "Ns.Class.Move",                   |
                                       |       global::Ns.Class.Move);            |
+-------------------------------+      |   }                                      |
| [SharedAiAction(              |      | }                                        |
|   typeof(MyDto), "Speed")]    |      |                                          |
| static NodeStatus SetSpeed(   | ---> | registry.Register("Ns.C.SetSpeed@12",   |
|     ref float speed,          |      |   static (ref BB bb, ...) =>             |
|     Entity self,              |      |   {                                      |
|     EntityRepository repo)    |      |     ref var f = ref Unsafe.As<...>(bb); |
+-------------------------------+      |     return Ns.C.SetSpeed(ref f,...);     |
                                       |   });                                    |
                                       +------------------------------------------+
```

### Diagram 4: HsmActionGenerator -- kernel vs user assembly paths

```
+----------------------------+
|  HsmActionGenerator        |
|  Execute()                 |
+----------------------------+
           |
           v
  Is assemblyName == "Fhsm.Kernel"?
           |
     +-----+------+
     | YES        | NO
     v            v
+----------+  +-------------------+
|Kernel    |  |User Assembly      |
|path      |  |path               |
+----------+  +-------------------+
     |              |
     v              v
HsmAction      HsmActionRegistrar.g.cs
Dispatcher     - thunks per [SharedAiAction]
.g.cs          - thunks per [SharedAiHeavyAction]
- ActionTable  - ExitCleanup thunks for [WritesChannel]
  (unsafe fn   - RegisterAll() calling
   pointers)     HsmActionDispatcher.Register*
- GuardTable   - RequiredExitCleanups dictionary
- ExecuteAction
- EvaluateGuard
- ClearAll
```

### Diagram 5: GizmoRegistrarGenerator flow

```
  ISyntaxReceiver.OnVisitSyntaxNode
         |
         | Collect ClassDeclarationSyntax where AttributeLists.Count > 0
         v
  Execute: foreach candidate class
         |
         +-- Has [GizmoProjector] ? --No--> skip
         |         |
         |        Yes
         |         v
         |  Implements IStatelessGizmo or IGlobalStatelessGizmo?
         |         |                |
         |        No               Yes
         |         |                |
         |   FDP_002 warning        |
         |   (skip registration)    v
         |                   Group by namespace
         |
         v
  For each namespace group: emit {ns}_GizmoRegistrar.g.cs
         |
         v
  GizmoRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settings)
    statelessRegistry.Register(new MyGizmo(), new Type[]{typeof(ComponentA)})
    statelessRegistry.RegisterGlobal(new MyGlobalGizmo())
```

---

## Source Structure

### `BehaviorParameterSizeAnalyzer.cs`

**Namespace**: `Fdp.Toolkit.Behavior.Analyzers`
**Kind**: `DiagnosticAnalyzer`
**Registered action**: `SymbolKind.Method`

Fires on every method symbol.  For each `[SharedAiActionAttribute]` or
`[SharedAiConditionAttribute]` it finds, it:

1. Extracts the DTO type from the first constructor argument.
2. Computes the unmanaged struct size by walking all instance fields, respecting both
   sequential layout (default) and `LayoutKind.Explicit` with `[FieldOffset]`.
3. If the computed size exceeds `MaxBehaviorParamByteSize` (100), it reports `FDP_001`.

Size computation is intentionally duplicated from `BTreeActionGenerator` and `HsmActionGenerator`
because the analyzer targets `netstandard2.0` and cannot reference the runtime assembly that
defines `BehaviorConstants.MaxBehaviorParamByteSize`.

**Struct layout rules implemented**:
- Sequential: fields are packed with natural alignment; total size rounded up to struct alignment.
- Explicit: size = max of `(fieldOffset + fieldSize)` across all fields with `[FieldOffset]`.
- Enums: treated as their underlying integer type (default int = 4 bytes).
- Nested structs: recursively computed.
- Unknown types (reference types, generics with unknown layout): return -1 -> analysis skipped
  safely.

---

### `SharedBhuDiagnostics.cs`

**Namespace**: `Fdp.Toolkit.Behavior.Analyzers`
**Kind**: internal static class (diagnostic descriptor repository)

Centralises the three BHU diagnostic descriptors to prevent RS1019
("DiagnosticDescriptor with the same ID already defined") warnings when both
`BTreeActionGenerator` and `HsmActionGenerator` are included in the same compilation.

| Field                | Diagnostic | Severity | Description                                       |
|----------------------|------------|----------|---------------------------------------------------|
| `BHU001_TypeMismatch`| BHU_001    | Error    | `ref` parameter type mismatches DTO field type    |
| `BHU002_NonStatic`   | BHU_002    | Warning  | `[SharedAi*]` method is not static                |
| `BHU003_UnknownField`| BHU_003    | Error    | Named DTO field not found or offset uncomputable  |

---

### `BTreeActionGenerator.cs`

**Namespace**: `Fdp.Toolkit.Behavior.Analyzers`
**Kind**: `IIncrementalGenerator`
**Output file**: `FbtActionRegistrar.g.cs`

Recognized attributes (all from `Fbt.Kernel` namespace):

| Attribute                   | Method kind            |
|-----------------------------|------------------------|
| `[BTreeAction]`             | 4-param or 3-param node logic delegate |
| `[BTreeCondition]`          | 4-param or 3-param node logic delegate |
| `[SharedAiCondition]`       | Shared condition reading a DTO field   |
| `[SharedAiAction]`          | Shared action writing a DTO field      |
| `[SharedAiHeavyAction]`     | Shared action with an extra heavy ECS component |
| `[SharedAiHeavyCondition]`  | Shared condition with an extra heavy ECS component |
| `[WritesChannel]`           | Modifier: marks which channel (0=Loco, 1=Weapon, 2=Interact) the action writes |

**Method categories**:

- **Registrable (4-param)**: `NodeStatus Method(ref TB bb, ref BTS st, ref TC ctx, int pi)`.
  Registered directly via `registry.Register("FQN", method)`.

- **Reusable/Bridge (3-param)**: `NodeStatus Method(ref TValue v, ref BTS st, ref TC ctx)`.
  Registered as a closure that casts the blackboard to `TValue` using `Unsafe.As`.
  Key suffix: `"@0"`.

- **SharedAi**: attribute carries `(dtoType, fieldName)`.  Generator resolves the byte offset
  of `fieldName` inside `dtoType` at compile time, emits a lambda that projects
  `bb.BehaviorParameters[offset]` as `ref fieldType` and calls the user method.
  Key: `"{FQN}@{offset}"`.

- **SharedAiHeavy**: additionally fetches a second ECS component (`heavyCompType`).
  Managed heavy components: fetched with `GetComponent<T>` (class reference).
  Unmanaged heavy components: fetched with `GetComponentRW<T>` then reinterpreted with
  `Unsafe.As` to `heavyDtoType`.

Filtering:
- Private and protected methods are excluded (they are test fixtures not to be registered).
- Generators skip nulls returned by the transform step (`.Where(m => m != null)`).

Groups are formed by `(TBlackboardType, TContextType)` so each unique pair gets one
`RegisterAll` overload.  SharedAi entries are assigned to groups whose context type has a
`Self` member.

---

### `BTreeDefinitionGenerator.cs`

**Namespace**: `Fdp.Toolkit.Behavior.Analyzers`
**Kind**: `IIncrementalGenerator`
**Output file**: `FbtTreeCatalog.g.cs`

Recognized attributes:

| Attribute             | Where expected |
|-----------------------|----------------|
| `[BTreeDefinition]`   | Static method returning `BehaviorTreeBlob` or `BTreeBuilder<TBB,TCtx>` |

Valid method signature constraints:
- Must be `static`.
- Must have zero parameters.
- Must return `BehaviorTreeBlob` or a `BTreeBuilder<TBB,TCtx>`.

Violations produce `BTree002` (warning, not error) and the offending method is omitted from
the catalog.

Generated catalog: one static `Get{TreeName}()` method per valid definition.  Tree name is
sanitized to a valid C# identifier (non-alphanumeric characters replaced with `_`; leading
digit prefixed with `_`).

Builder-returning definitions are called `.Compile(treeName)` inside the generated accessor;
blob-returning definitions are called directly.

---

### `HsmActionGenerator.cs`

**Namespace**: `Fdp.Toolkit.Behavior.Analyzers`
**Kind**: `IIncrementalGenerator`
**Output files**: `HsmActionDispatcher.g.cs` (kernel assembly only) or `HsmActionRegistrar.g.cs`

Recognized attributes:

| Attribute                   | Method kind                                          |
|-----------------------------|------------------------------------------------------|
| `[HsmAction]`               | HSM state action (void, unsafe function pointer)    |
| `[HsmGuard]`                | HSM transition guard (bool, unsafe function pointer)|
| `[SharedAiCondition]`       | Same semantics as BTree side but HSM thunks         |
| `[SharedAiAction]`          | Same semantics as BTree side but HSM thunks         |
| `[SharedAiHeavyAction]`     | Heavy variant for HSM                               |
| `[SharedAiHeavyCondition]`  | Heavy variant for HSM                               |
| `[WritesChannel]`           | Modifier: designates exit-cleanup thunk generation  |

**Dual output paths** (selected by assembly name):

1. **Kernel assembly** (`Fhsm.Kernel`):
   Generates `HsmActionDispatcher.g.cs` containing:
   - `Dictionary<ushort, IntPtr> ActionTable` and `GuardTable` populated at type-init time
     using unsafe function pointers.
   - `ExecuteAction(ushort, void*, void*, HsmCommandWriter*)` dispatcher.
   - `EvaluateGuard(ushort, void*, void*, ushort)` dispatcher.
   - `RegisterAction` / `RegisterGuard` extension hooks for user-side additions.
   - `ClearAll()` for hot-reload scenarios.

2. **User assembly** (any other assembly name):
   Generates `HsmActionRegistrar.g.cs` containing:
   - Per-SharedAi entry thunks that cast `void* contextPtr` to `HsmKernelBridge*`,
     obtain `EntityRepository` from a `GCHandle`, and call the user method.
   - `ExitCleanup_*` thunks for every action annotated with `[WritesChannel]` that reset
     the named channel's `ActiveAction` and increment `ActionInstanceId`.
   - `RegisterAll()` calling `HsmActionDispatcher.RegisterAction/RegisterGuard` for all
     collected entries.
   - `RequiredExitCleanups` (read-only dictionary) mapping action name to its exit-cleanup
     thunk name, for use in HSM graph validation tools.

**Thunk safety constraint** (emitted as a comment in every generated thunk):
> Do NOT add or remove ECS components from this thunk.  Shared action thunks write directly
> to EntityRepository, bypassing FastHSM's deferred HsmCommandWriter.  Structural ECS
> mutations during chunk iteration corrupt the chunk arrays.  Only read/write fields of
> existing components.

**FNV-1a hash**: Both `BTreeActionGenerator` and `HsmActionGenerator` use the same 16-bit
FNV-1a hash to compute `ushort` action/guard IDs from string keys.  The implementations are
kept identical to guarantee cross-assembly consistency when compound keys are used.

---

### `GizmoRegistrarGenerator.cs`

**Namespace**: `Fdp.Toolkit.Diagnostics.Analyzers`
**Kind**: `ISourceGenerator` (classic, non-incremental)
**Output files**: `{namespace}_GizmoRegistrar.g.cs` (one per namespace group)

Uses an `ISyntaxReceiver` to collect all `ClassDeclarationSyntax` nodes with at least one
attribute list during the syntax walk.

During `Execute`:
1. Resolves `GizmoProjectorAttribute`, `IStatelessGizmo`, `IGlobalStatelessGizmo`, and
   `GizmoSettingsRegistry` symbols from the compilation.
2. Checks each candidate class for `[GizmoProjector]`.
3. If the class does not implement `IStatelessGizmo` or `IGlobalStatelessGizmo`, emits
   `FDP_002` (warning) and skips it.
4. Collects component type arguments from the `[GizmoProjector]` constructor.
5. Detects whether the class has a constructor accepting `GizmoSettingsRegistry`; if so,
   injects `settings` as the constructor argument in generated code.
6. Groups qualifying classes by their containing namespace.
7. Emits one `partial class GizmoRegistrar` per namespace with a `RegisterAll` method.

Global gizmos (implementing `IGlobalStatelessGizmo`) are registered via
`statelessRegistry.RegisterGlobal(...)` without component type constraints.
Per-entity gizmos are registered via `statelessRegistry.Register(..., new Type[]{...})`.

---

## Public API Reference

### Diagnostics

| ID        | Severity | Category          | Title                                                                 |
|-----------|----------|-------------------|-----------------------------------------------------------------------|
| FDP_001   | Error    | Fdp.Memory        | Behavior parameter DTO exceeds BrainBlackboard capacity               |
| FDP_002   | Warning  | Fdp.Gizmos        | GizmoProjector class must implement IStatelessGizmo or IGlobalStatelessGizmo |
| BHU_001   | Error    | BTreeActionGenerator | SharedAi parameter type mismatch                                   |
| BHU_002   | Warning  | BTreeActionGenerator | SharedAi method must be static                                     |
| BHU_003   | Error    | BTreeActionGenerator | SharedAi DTO field not found                                       |
| BTree002  | Warning  | BTreeSourceGen    | Invalid BTreeDefinition method                                        |

### FDP_001 -- message format

```
Method '{methodName}': DTO type '{dtoTypeName}' requires {actualBytes} bytes,
exceeding the {maxBytes}-byte BehaviorParameters region. This would corrupt the
SoftAdvice and Interrupt registers in BrainBlackboard.
```

### BHU_001 -- message format

```
Method '{methodName}': ref parameter type '{paramType}' does not match
DTO field '{dtoType}.{fieldName}' of type '{fieldType}'
```

### BHU_002 -- message format

```
Method '{methodName}' annotated with [SharedAiCondition] or [SharedAiAction]
must be static; skipping
```

### BHU_003 -- message format

```
Method '{methodName}': field '{fieldName}' not found on type '{dtoType}'
or offset cannot be computed
```

### BTree002 -- message format

```
Method '{methodName}' annotated with [BTreeDefinition] must be static,
return BehaviorTreeBlob, and have no parameters
```

### FDP_002 -- message format

```
Type '{typeName}' is decorated with [GizmoProjector] but does not implement
IStatelessGizmo or IGlobalStatelessGizmo and was not registered
```

### Generated output files

| File                              | Generator                  | Contents                                     |
|-----------------------------------|----------------------------|----------------------------------------------|
| `FbtActionRegistrar.g.cs`         | BTreeActionGenerator       | `FbtActionRegistrar` with `RegisterAll(ActionRegistry<TB,TC>)` overloads |
| `FbtTreeCatalog.g.cs`             | BTreeDefinitionGenerator   | `FbtTreeCatalog` with `Get{TreeName}()` statics |
| `HsmActionDispatcher.g.cs`        | HsmActionGenerator (kernel)| Unsafe function-pointer dispatch tables       |
| `HsmActionRegistrar.g.cs`         | HsmActionGenerator (user)  | SharedAi thunks, exit-cleanup, `RegisterAll` |
| `{ns}_GizmoRegistrar.g.cs`        | GizmoRegistrarGenerator    | Per-namespace gizmo registration              |

---

## Dependencies

### NuGet packages

| Package                           | Version | Purpose                                                         |
|-----------------------------------|---------|-----------------------------------------------------------------|
| `Microsoft.CodeAnalysis.CSharp`   | 4.8.0   | Roslyn C# compiler API (syntax trees, semantic model, symbols)  |
| `Microsoft.CodeAnalysis.Analyzers` | 3.3.4  | Meta-analyzer: validates the analyzer code itself (RS* rules)   |

Both packages are declared with `PrivateAssets="all"` so they are not propagated to consumers
of this project.  Consumers only see the compiled analyzer DLL.

### Project references

None.  The project is intentionally self-contained.  It must not reference any FDP runtime
assembly (which targets `net8.0`) because the analyzer runs inside the Roslyn compiler host,
which may use a different runtime.

This is why `MaxBehaviorParamByteSize` (100) and the struct-layout math are duplicated inside
the analyzer rather than pulled from `Fdp.Toolkits`.

### Target framework

`netstandard2.0` -- required by the Roslyn hosting contract.  `LangVersion` is set to
`latest` so that C# 11 features (e.g., `is not` patterns) are available in the generator
source while the output assembly remains `netstandard2.0`-compatible.

### Compiler flags

| Flag                              | Value  | Reason                                                   |
|-----------------------------------|--------|----------------------------------------------------------|
| `TreatWarningsAsErrors`           | true   | Prevents any diagnostic from being silently ignored      |
| `IsRoslynComponent`               | true   | Tells the SDK to apply Roslyn-specific build logic       |
| `EnforceExtendedAnalyzerRules`    | true   | Activates RS2xxx rules (analyzer API correctness checks) |
| `NoWarn`                          | CS8632;RS2008 | CS8632: nullable annotation in netstandard2.0; RS2008: suppressed where intentional |

---

## Usage Examples

### FDP_001: DTO too large

**Before (compile error)**:

```csharp
// MyDto is 104 bytes - exceeds the 100-byte BehaviorParameters limit
[StructLayout(LayoutKind.Sequential)]
public struct MyDto
{
    public float X;     // 4 bytes
    public float Y;     // 4 bytes
    public float Z;     // 4 bytes
    // ...24 more floats... (96 bytes total)
    public float Extra; // 4 bytes -> total 104 bytes
}

public static class MyActions
{
    // FDP_001 error: MyDto requires 104 bytes, exceeds 100-byte region
    [SharedAiAction(typeof(MyDto), "X")]
    public static NodeStatus SetX(ref float x, Entity self, EntityRepository repo)
        => NodeStatus.Success;
}
```

**Fix -- reduce DTO size to <= 100 bytes**:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct MyDto
{
    public float X;
    public float Y;
    public float Z;
    // ... keep total <= 100 bytes
}
```

**Fix -- suppress if genuinely intentional** (rare; requires a comment explaining why):

```csharp
#pragma warning disable FDP_001  // MyDto partitioned across multiple actions intentionally
[SharedAiAction(typeof(MyDto), "X")]
public static NodeStatus SetX(ref float x, Entity self, EntityRepository repo) { ... }
#pragma warning restore FDP_001
```

---

### BHU_001: Type mismatch

**Before (compile error)**:

```csharp
public struct MovementDto { public float Speed; public int Flags; }

// BHU_001: ref parameter is 'int', but MovementDto.Speed is 'float'
[SharedAiAction(typeof(MovementDto), "Speed")]
public static NodeStatus SetSpeed(ref int speed, Entity self, EntityRepository repo)
    => NodeStatus.Success;
```

**Fix**:

```csharp
[SharedAiAction(typeof(MovementDto), "Speed")]
public static NodeStatus SetSpeed(ref float speed, Entity self, EntityRepository repo)
    => NodeStatus.Success;
```

---

### BHU_002: Non-static SharedAi method

**Before (compile warning)**:

```csharp
public class MyBehaviors
{
    // BHU_002: method is not static; no adapter generated
    [SharedAiAction(typeof(MovementDto), "Speed")]
    public NodeStatus SetSpeed(ref float speed, Entity self, EntityRepository repo)
        => NodeStatus.Success;
}
```

**Fix**:

```csharp
public class MyBehaviors
{
    [SharedAiAction(typeof(MovementDto), "Speed")]
    public static NodeStatus SetSpeed(ref float speed, Entity self, EntityRepository repo)
        => NodeStatus.Success;
}
```

---

### BHU_003: Unknown DTO field

**Before (compile error)**:

```csharp
public struct MovementDto { public float Speed; }

// BHU_003: field 'Velocity' not found on MovementDto
[SharedAiAction(typeof(MovementDto), "Velocity")]
public static NodeStatus SetVelocity(ref float v, Entity self, EntityRepository repo)
    => NodeStatus.Success;
```

**Fix** -- use the correct field name:

```csharp
[SharedAiAction(typeof(MovementDto), "Speed")]
public static NodeStatus SetSpeed(ref float speed, Entity self, EntityRepository repo)
    => NodeStatus.Success;
```

---

### BTree002: Invalid BTreeDefinition method

**Before (compile warning, method skipped from catalog)**:

```csharp
public class MyTrees
{
    // BTree002: not static, has parameters
    [BTreeDefinition("Patrol")]
    public BehaviorTreeBlob GetPatrol(int x) => BuildPatrol(x);
}
```

**Fix**:

```csharp
public class MyTrees
{
    [BTreeDefinition("Patrol")]
    public static BehaviorTreeBlob GetPatrol() => BuildPatrol();
}
```

---

### FDP_002: GizmoProjector without required interface

**Before (compile warning, gizmo not registered)**:

```csharp
// FDP_002: MyGizmo does not implement IStatelessGizmo or IGlobalStatelessGizmo
[GizmoProjector(typeof(TransformComponent))]
public class MyGizmo { }
```

**Fix**:

```csharp
[GizmoProjector(typeof(TransformComponent))]
public class MyGizmo : IStatelessGizmo
{
    public void Render(Entity entity, RenderContext ctx) { ... }
}
```

---

### Consuming generated code

**BTree side**:

```csharp
// Generated FbtActionRegistrar is picked up automatically by FbtRuntime
// during ActionRegistry<TB, TC>.Initialize():
var registry = new ActionRegistry<MyBlackboard, MyContext>();
MyAssembly.Generated.FbtActionRegistrar.RegisterAll(registry);
```

**HSM side**:

```csharp
// Called once at startup (or after hot-reload ClearAll):
MyAssembly.Generated.HsmActionRegistrar.RegisterAll();

// Channel-safety validation (tooling):
var missing = HsmGraphValidator.ValidateChannelSafety(
    graph,
    MyAssembly.Generated.HsmActionRegistrar.RequiredExitCleanups);
```

**Tree catalog**:

```csharp
BehaviorTreeBlob patrolBlob = MyAssembly.Generated.FbtTreeCatalog.GetPatrol();
```

---

## Best Practices

1. **Keep DTOs small**.  The 100-byte hard limit is derived from the physical memory layout of
   `BrainBlackboard`.  If you need more state, add a separate ECS component and use a
   `[SharedAiHeavyAction]` or `[SharedAiHeavyCondition]` attribute instead.

2. **Always mark SharedAi methods as static**.  The generator will issue BHU_002 and skip
   non-static methods.  Non-static shared AI methods cannot be wired into the generated
   thunks because thunks receive only raw `void*` pointers, not object instances.

3. **Match ref parameter types exactly**.  The compiler and the generator both check that
   the `ref` parameter type matches the DTO field type.  Using `int` for a `float` field
   is a BHU_001 error; using an alias or derived struct is also flagged.

4. **Do not mutate ECS structure inside SharedAi thunks**.  The generated comment in every
   thunk explains why: structural mutations (add/remove component) during chunk iteration
   corrupt array pointers.  Read and write only existing field values.

5. **One DTO per action/condition call site**.  Each `[SharedAiAction]` or `[SharedAiCondition]`
   attribute binds one DTO field.  A method may carry multiple attributes to bind to multiple
   fields, but each attribute generates a separate registration entry with its own compound key.

6. **Use `[WritesChannel]` for every action that activates a locomotion, weapon, or interaction
   channel**.  Forgetting this attribute means the HSM generator will not produce an
   `ExitCleanup_*` thunk, which in turn means the channel stays active after the state is
   exited, causing the entity to continue an action it should have stopped.

7. **Validate the generated `RequiredExitCleanups` dictionary** with
   `HsmGraphValidator.ValidateChannelSafety` as part of your test suite.  This catches
   states that write a channel but have no corresponding exit-cleanup registered.

8. **Do not reference FDP runtime assemblies from this project**.  The analyzer runs inside
   the compiler host.  Adding a `net8.0` project reference will cause the analyzer to fail
   to load in Visual Studio and during `dotnet build`.

9. **Add new diagnostic IDs to `SharedBhuDiagnostics`** when the same ID must be reported
   by multiple generators.  Duplicating a `DiagnosticDescriptor` with the same ID across
   classes triggers RS1019.

10. **Do not suppress FDP_001 without a code review**.  This diagnostic exists to prevent
    silent memory corruption.  Any suppression must be reviewed and justified in a comment.

---

## Related Projects

| Project                          | Relationship                                                             |
|----------------------------------|--------------------------------------------------------------------------|
| `Fdp.Toolkits`                   | Runtime counterpart.  Defines `BehaviorConstants.MaxBehaviorParamByteSize`, `BrainBlackboard`, `SharedAiActionAttribute`, `SharedAiConditionAttribute`, and the channel component types the generators reference. |
| `Fdp.Toolkit.Tkb.SourceGen`      | Sibling source generator project targeting TKB (Toolkit Blackboard) layer; different domain but similar pattern. |
| `FDP.Toolkit.DER`                | Uses the generated registrars from this project's output for DER entity AI behavior. |
| `FastBTree` (ExtDeps)            | Defines `IIncrementalGenerator` entry points consumed by `BTreeActionGenerator`; provides `BehaviorTreeBlob`, `BTreeBuilder<TBB,TCtx>`, and the action attribute types. |
| `FastHSM` (ExtDeps)              | Defines `HsmActionAttribute`, `HsmGuardAttribute`, `HsmCommandWriter`, `HsmKernelBridge`, and the HSM kernel dispatch infrastructure consumed by `HsmActionGenerator`. |
| `Fdp.Diagnostics.Contracts`      | Defines `GizmoProjectorAttribute`, `IStatelessGizmo`, `IGlobalStatelessGizmo`, and `GizmoSettingsRegistry` consumed by `GizmoRegistrarGenerator`. |
| `Fdp.Engine`                     | Consumer: registers BTree actions and HSM actions at simulation startup using the generated `RegisterAll` methods. |
| `Fdp.Toolkits.Analyzers` tests   | Currently no dedicated test project exists.  Analyzer behavior is validated indirectly through compilation of projects that use the attributes. |

---

## Architectural Notes

### Why `netstandard2.0`?

Roslyn analyzers and generators must load into the compiler host process.  MSBuild / the C#
compiler itself is a `net472` or `netstandard2.0` process on many platforms.  Using
`netstandard2.0` guarantees that the assembly can be loaded regardless of whether the build
host is .NET Framework, .NET Core, or .NET 8+.

### Why struct-layout math is duplicated in three places?

`BehaviorParameterSizeAnalyzer`, `BTreeActionGenerator`, and `HsmActionGenerator` all
compute struct field offsets.  This logic cannot be shared via a common helper assembly because:

1. The analyzer must not reference any runtime project.
2. The generators target `netstandard2.0` and share the same assembly as the analyzer.
3. Extraction into a separate `netstandard2.0` helper assembly would introduce a new NuGet
   dependency or project reference chain that the Roslyn host would need to load.

The duplication is therefore intentional and is documented with comments in the source files.

### Why `ISourceGenerator` for gizmos and `IIncrementalGenerator` for BTree/HSM?

`GizmoRegistrarGenerator` uses the older `ISourceGenerator` API because gizmo registrations
are infrequent (changed only when a new gizmo class is added), so the incremental overhead is
not justified.  `BTreeActionGenerator` and `HsmActionGenerator` use the incremental API
because AI action methods are edited frequently, and incremental compilation dramatically
reduces IDE latency in large solutions.

### Compound key convention for SharedAi entries

The compound key format `"{FullyQualifiedMethodName}@{byteOffset}"` encodes both the method
identity and the specific field within the DTO.  This is required because the same method
may be annotated with multiple `[SharedAiAction]` attributes (one per DTO field it reads),
and each attribute generates a distinct registration entry that must have a unique key.

### FNV-1a hash

Both generators compute a 16-bit FNV-1a hash from the action/guard key string to produce a
`ushort` ID.  This hash is stable across builds (deterministic for a given key) and
collision-resistant enough for the action table sizes seen in practice.  The hash function is
intentionally identical in both generators and must be kept in sync if changed.

---

*Generated by documentation tooling on 2026-05-23.*
