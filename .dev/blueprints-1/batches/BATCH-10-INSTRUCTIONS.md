# BATCH-10 Instructions

**Branch:** `blueprints`
**Workspace root:** `d:\WORK\IOS-IG-SimHost-FDP`

**Scope:** TASK-CP-002 — Pipeline Stages 1-5 (Parse through Schedule)

**Design references (read these first):**
- `.dev/blueprints-1/TASK-DETAIL.md` — TASK-CP-002 section (constraints + success conditions)
- `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design.md` — §4 Stage 1, §5 Stage 2,
  §6 Stage 3, §7 Stage 4, §8 Stage 5
- `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md` — Patch 1
  (SiblingSignatures in ValidationContext — see constraint below)

---

## Context From BATCH-09

The following is already implemented (do not recreate):

- All IR types (`IrAsset`, `IrGraph`, `IrBlock`, `IrOperation` hierarchy, etc.)
- `FnvHasher`, `Sanitizer`, `DiagnosticCodes`, `DiagnosticSink`, `Diagnostic`
- `CompileOptions` (with `SiblingSignatures: IReadOnlyList<BlueprintSignature>`)
- Stage stubs `Stage1_Parse` through `Stage7_Emit` (all throw `NotImplementedException`)
- Catalog interfaces in `Compiler/Catalogs/`

Check the current state of `Stage2_Validate.cs` before writing — it may have validator stubs
from BATCH-09.

---

## Critical Constraint: SiblingSignatures (Patch 1)

The `ValidationContext.SiblingsById` field in the design doc `§5.2` is documented as:
```
IReadOnlyDictionary<Guid, BlueprintAsset> SiblingsById
```
**Patch 1 overrides this.** The actual field must be:
```csharp
IReadOnlyDictionary<Guid, BlueprintSignature> SiblingSignaturesById
```
Because `CompileOptions` carries `IReadOnlyList<BlueprintSignature>` (not full assets),
`V_PeerReferences` (§5.6 in the design doc) uses `SiblingSignaturesById` and checks:
1. `node.TargetPeerAssetId` is in `asset.CallablePeers`
2. target found in `SiblingSignaturesById`
3. target signature has the named exported function in `ExportedFunctionNames`

---

## Implementation Instructions

### Step 1: Read and understand the Asset model

Before implementing any stage, read these existing files to understand what `BlueprintAsset`,
`Node` subtypes, `Link`, `Graph`, etc. look like:
```
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/
```
Key types to understand: `BlueprintAsset`, `Graph` (`GraphKind`), `Node` subtypes
(`ReturnNode`, `LatentDelayNode`, `WaitForChannelNode`, `WaitForEventNode`,
`CallPeerBlueprintNode`), `Link`, `Pin` (`PinKind`, `BlueprintTypeRef`).

Also read `BlueprintJsonServices.cs` to understand how JSON options are set up.

### Step 2: Implement `ValidationContext`

Create/update `Compiler/Stages/ValidationContext.cs`:

```csharp
internal sealed class ValidationContext
{
    public DiagnosticSink Diagnostics { get; }
    public ITypeRegistry TypeRegistry { get; }
    public INodeRegistry NodeRegistry { get; }
    public IEngineEventCatalog EngineEvents { get; }
    public IChannelCommandCatalog ChannelCommands { get; }
    public IWaitPrimitiveCatalog WaitPrimitives { get; }
    // Patch 1: signatures only, NOT full assets
    public IReadOnlyDictionary<Guid, BlueprintSignature> SiblingSignaturesById { get; }
    public Guid AssetId { get; }  // for diagnostics context

    public ValidationContext(DiagnosticSink sink, CompileOptions options)
    {
        Diagnostics = sink;
        TypeRegistry = options.TypeRegistry;
        NodeRegistry = options.NodeRegistry;
        EngineEvents = options.EngineEvents;
        ChannelCommands = options.ChannelCommands;
        WaitPrimitives = options.WaitPrimitives;
        SiblingSignaturesById = options.SiblingSignatures
            .ToDictionary(s => s.AssetId);
    }
}
```

### Step 3: Implement `Stage1_Parse`

Follow design doc §4.2 exactly. The implementation is shown verbatim in the design doc.
Key method signature: `public static BlueprintAsset? Run(string json, DiagnosticSink sink)`.
Use `BlueprintJsonServices.GetDeserializeOptions()`.
After deserializing, check `AssetId == Guid.Empty` → BP0010, `Name == ""` → BP0011.

### Step 4: Implement `Stage2_Validate`

Follow design doc §5.2–§5.7. The validator pattern:

```csharp
internal interface IValidator
{
    void Validate(BlueprintAsset asset, ValidationContext ctx);
}
```

Implement all 14 validators. For the ones fully spelled out in §5.3–§5.6, copy them
almost verbatim. For the remaining ones, implement the logic matching the diagnostic
table in §5.7:

- `V_AssetStructure` — check AssetId non-empty, Name non-empty (BP0010, BP0011)
- `V_DispatchKindCompatibility` — full implementation in §5.3 (BP1010-BP1031)
- `V_NodeStructure` — each node has valid pins; no duplicate pin Ids
- `V_LinkStructure` — each link's `From`/`To` NodeId+PinId exist; no dupe links
- `V_GraphStructure` — entry node exists; all nodes exec-reachable from entry (BP1602, BP1601)
- `V_VariablesAndState` — full implementation in §5.5 (BP1200, BP1201, BP1210, BP1211)
- `V_AiPrimitiveIntent` — full implementation in §5.4 (BP1100, BP1101)
- `V_LatentRules` — latent nodes only in AiPrimitive/Instance; not in Library
- `V_ChannelCommandReferences` — each ChannelCommandNode's command in `ChannelCommands.GetEntries()` (BP1401)
- `V_EventGraphReferences` — each engine event subscription in `EngineEvents.GetEntries()` or custom event (BP1400)
- `V_WaitNodeReferences` — each WaitNode's target in `WaitPrimitives.GetEntries()` (BP1402)
- `V_PeerReferences` — full implementation in §5.6 BUT using `SiblingSignaturesById` per Patch 1 (BP1300, BP1301, BP1302)
- `V_TypeReferences` — for each data pin, `TypeRegistry.TryResolve(pin.Type)` (BP1500)
- `V_DeterminismOrdering` — no-op for Slice 1 (return immediately)

**Note on Diagnostic overloads:** The design doc shows `Diagnostic.Error(code, message, assetId, graphId?, nodeId?)`.
Add a corresponding overload to `Diagnostic.cs` that accepts these context parameters:
```csharp
public static Diagnostic Error(string code, string message,
    Guid? assetId = null, Guid? graphId = null, Guid? nodeId = null, Guid? pinId = null);
```
Store those in a `Location` nested record or as optional properties. The simplest approach:
add them as nullable Guid properties on `Diagnostic`.

**Note on DiagnosticSink.HasFatalErrors:** The design doc calls `ctx.Diagnostics.HasFatalErrors`
to short-circuit the validator loop. Add this property to `DiagnosticSink`:
```csharp
public bool HasFatalErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
```
(Same as `HasErrors` for now; the distinction becomes important in Slice 2 for warnings-as-errors.)

### Step 5: Implement `Stage3_Normalize`

Follow design doc §6.3-§6.4. Three passes:
1. `MaterializeDefaultPinLiterals` — for each unconnected data input pin with `DefaultLiteralJson != null`, synthesize a `LiteralNode` + link. Use `SynthesizedGuid("default-literal", graphId, pinId)`.
2. `InsertImplicitCasts` — for each link where source/dest pin types differ and `TypeRegistry.TryGetCoercion(from, to, ...)` succeeds, insert a synthesized cast node.
3. `EliminateOrphanNodes` — remove nodes unreachable from exec chain; emit BP2001 Warning.

`SynthesizedGuid` helper (§6.4):
```csharp
private static Guid SynthesizedGuid(string purpose, params object[] inputs)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    var sb = new System.Text.StringBuilder(purpose);
    foreach (var x in inputs) sb.Append('|').Append(x);
    var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    return new Guid(hash[..16]);
}
```

**Important:** Check what `LiteralNode` type exists in the asset model. If there is a specific
literal/constant node subtype, use it. If not, you may need to add one or find the equivalent.

### Step 6: Implement `Stage4_TypeResolve`

Follow design doc §7.4-§7.6. Key types:

```csharp
internal sealed record TypedAsset(
    BlueprintAsset Asset,
    IReadOnlyDictionary<Guid, IrTypeRef> PinTypes,
    IReadOnlyDictionary<Guid, IrTypeRef> FieldTypes);
```

Implement the `StaticTypeRegistry` class in `Compiler/` (or `Catalogs/`):
- Built-in primitives: `System.Boolean` (1 byte), `System.Byte` (1), `System.Int16` (2),
  `System.Int32` (4), `System.Int64` (8), `System.Single` (4), `System.Double` (8)
- `System.Numerics.Vector2` (8), `Vector3` (12), `Vector4` (16), `Quaternion` (16)
- `Fdp.Core.Entity` (8, IsEntityHandle=true)
- Add more as needed; the table is open-ended.
- Coercion table per §7.3 (8 entries).

Wildcard pins (§7.5): two-pass walk. Emit `BP1502_UnresolvableWildcard` if unresolved after both passes.
Managed type in state fields: emit `BP1503_ManagedTypeInState`.

### Step 7: Implement `Stage5_Schedule`

Follow design doc §8.1-§8.8. This is the most complex stage.

The `GraphScheduler` class:
- `AllocBlock(label)` → creates a new `IrBlock` with the next `IrBlockId`; entry is always `IrBlockId(0)`.
- `AllocValue(IrTypeRef)` → creates the next `IrValue` (monotonic index per graph).
- Topological sort from entry node using BFS over exec edges.
- For each `BranchNode`: emit `IrTerm_Branch`; create two new blocks.
- For each latent node (`LatentDelayNode`, `WaitForChannelNode`, `WaitForEventNode`):
  - Append the corresponding `IrOp_WaitForChannel/Event/LatentDelay` as a marker statement.
  - Emit `IrTerm_Suspend` terminator.
  - Create a resume block.
  - Resume block label: `"wait_resume_{n}"` where `n` increments.
- CSE via `pinValueCache : Dictionary<Guid, IrValue>` (§8.5). Cache is per-block.
- Block label conventions (§8.3): `entry`, `branch_{nodeId_short}_true/false`, `wait_resume_{n}`, `success`, `failure`.

BFS block numbering for determinism: use a `Queue<(IrBlockId, Node entryNode)>` — process in FIFO order. At each BFS level, sort by source `node.Id` for determinism.

`IrPrinter.PrettyPrint(IrAsset)`:
- Create `Compiler/IrPrinter.cs` with a deterministic, human-readable text output.
- Used by golden file tests in CP-006.
- Format (example):
  ```
  IrAsset: MoveToAndFire (0xA1B2C3D4) Library
  Graph: Main [Function] Entry=0
    Block 0 (entry):
      t0 = ReadParam(0)
      t1 = PureCall(System.Math.Sqrt, [t0])
      Goto -> 1
    Block 1 (success):
      Return t1
  ```

### Step 8: Wire `BlueprintCompiler.Compile` and `BlueprintCompiler.Validate`

Update `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs`
(the one in the `Compiler/` subfolder) to call through the stages:

```csharp
public CompileResult Compile(BlueprintAsset asset, CompileOptions options)
{
    var sink = new DiagnosticSink();

    // Stage 1 — skip if asset is already parsed
    // (When called from generator, asset may be pre-parsed)

    // Stage 2 — Validate
    var ctx = new ValidationContext(sink, options);
    ctx.AssetId = asset.AssetId;  // or pass in constructor
    Stage2_Validate.Run(asset, ctx);
    if (sink.HasErrors) return new CompileResult(false, null, 0, 0, null,
        sink.All, asset, null, null);

    // Stage 3 — Normalize
    asset = Stage3_Normalize.Run(asset, ctx);
    if (sink.HasErrors) return FailResult(sink, asset);

    // Stage 4 — TypeResolve
    var typed = Stage4_TypeResolve.Run(asset, ctx);
    if (sink.HasErrors) return FailResult(sink, asset);

    // Stage 5 — Schedule
    var ir = Stage5_Schedule.Run(typed, ctx);
    if (sink.HasErrors) return FailResult(sink, asset);

    // Stages 6-8 not yet implemented
    throw new NotImplementedException("Stage 6-8 not yet implemented (CP-003/004/005).");
}

public ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null)
{
    var sink = new DiagnosticSink();
    var compileOptions = CreateMinimalCompileOptions(options);  // with null catalogs as no-ops
    var ctx = new ValidationContext(sink, compileOptions);
    Stage2_Validate.Run(asset, ctx);
    return new ValidationResult(sink.All);
}
```

---

## Type Infrastructure Notes

### What `BlueprintTypeRef` looks like

Check `Assets/` for the `BlueprintTypeRef` type. It may be a simple record with a `TypeId` string
or a more complex discriminated union. The `Stage4_TypeResolve` needs to call
`ITypeRegistry.TryResolve(typeRef, out IrTypeRef)`.

### What `Node` subtypes exist

The asset model likely has a sealed hierarchy for `Node`. Run:
```
grep_search "class.*Node" in Hrot.Blueprints.Core/Assets
```
Map node kinds to `IrOperation` types in the scheduler:
- `LiteralNode` → `IrOp_Const`
- `GetVariableNode` → `IrOp_ReadVariable`
- `SetVariableNode` → `IrOp_WriteVariable`
- `CallFunctionNode` / `FunctionCallNode` → depends on target (Library, Peer, AiPrimitive)
- `ReturnNode` → `IrTerm_Return` or `IrTerm_ReturnStatus`
- `BranchNode` → `IrTerm_Branch`
- `WaitForChannelNode` → `IrOp_WaitForChannel` + `IrTerm_Suspend`
- `WaitForEventNode` → `IrOp_WaitForEvent` + `IrTerm_Suspend`
- `LatentDelayNode` → `IrOp_LatentDelay` + `IrTerm_Suspend`
- `GetSelfNode` → `IrOp_Self`
- `GetTimeNode` → `IrOp_Time`
- `GetDeltaTimeNode` → `IrOp_DeltaTime`
- etc.

If a node kind is missing from the asset model, emit `BP4004_UnknownNodeKind` and skip.

---

## Success Criteria

Verify all 7 success conditions from `TASK-DETAIL.md` TASK-CP-002:

1. `Stage1_Parse.Run` returns non-null for valid JSON; emits BP0002 for malformed JSON.
2. `V_AiPrimitiveIntent` emits BP1100 for `ReturnNode(Running)` in a Condition graph;
   emits BP1101 for `LatentDelayNode` in a Condition graph.
3. `V_VariablesAndState` emits BP1210 when Instance variable total exceeds 16096 bytes.
4. `V_PeerReferences` emits BP1301 when a sibling is in `CallablePeers` but absent from
   `SiblingSignatures` (using `SiblingSignaturesById` per Patch 1).
5. `Stage5_Schedule` splits a block at a `WaitForChannelNode`: block before has
   `IrTerm_Suspend`; block after is the resume block.
6. `IrPrinter.PrettyPrint` is deterministic (two calls on same `IrAsset` = identical output).
7. `dotnet build` zero errors; `dotnet test --filter "Stage1|Stage2|Stage3|Stage4|Stage5"` passes
   for stub test files (no golden tests yet; those are CP-006).

Also ensure the baseline is preserved:
- `dotnet test Hrot/.../Hrot.Blueprints.Tests.csproj --no-build` → 160 pass, 3 skip, 0 fail

---

## New DiagnosticCodes to add

The following codes are used in Stage 2 but may not yet be in `DiagnosticCodes.cs`.
Add them:
```csharp
// Stage 2 — Validate (additional)
public const string BP1012 = "BP1012";
public const string BP1013 = "BP1013";
public const string BP1020 = "BP1020";
public const string BP1021 = "BP1021";
public const string BP1022 = "BP1022";
public const string BP1023 = "BP1023";
public const string BP1024 = "BP1024";
public const string BP1025 = "BP1025";
public const string BP1030 = "BP1030";
public const string BP1031 = "BP1031";
public const string BP1400 = "BP1400";
public const string BP1401 = "BP1401";
public const string BP1402 = "BP1402";
public const string BP1502 = "BP1502";  // UnresolvableWildcard
public const string BP1503 = "BP1503";  // ManagedTypeInState
public const string BP1600 = "BP1600";  // OrphanedNode
public const string BP1601 = "BP1601";  // GraphHasNoReturn
public const string BP1602 = "BP1602";  // GraphHasNoEntry
// Stage 4
public const string BP3001_TypeResolveError = "BP3001";
// Stage 5
public const string BP4004 = "BP4004";  // UnknownNodeKind
```

---

## Output

Write completion report to `.dev/blueprints-1/reports/BATCH-10-REPORT.md` with:
- List of all files created/modified
- Any deviations from these instructions, with justification
- Answers:
  1. What Node subtypes existed in the asset model (list them)?
  2. Did `BlueprintTypeRef` already have a `TypeId` string property or was it different?
  3. Was `LiteralNode` present in the asset model? If not, what was used for constants?
  4. Were any Stage 3/4/5 issues encountered with the existing BlueprintAsset schema?
- Final build/test results (pass/skip/fail counts)
