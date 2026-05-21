# BATCH-10 Completion Report

**Tasks:** TASK-CP-002 (Pipeline Stages 1-5: Parse, Validate, Normalize, TypeResolve, Schedule)

---

## 1. Files Created or Modified

### Modified — Asset model
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/Nodes.cs`  
  Added `public NodeStatus Status { get; set; } = NodeStatus.Success;` to `ReturnNode`.

### Modified — Diagnostics
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/Diagnostic.cs`  
  Added optional context properties `Guid? AssetId`, `Guid? GraphId`, `Guid? NodeId`, `Guid? PinId` to the `Diagnostic` record. Added `Error(...)` and `Warning(...)` factory overloads that accept those context parameters.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/DiagnosticSink.cs`  
  Added `public bool HasFatalErrors` property (same as `HasErrors` for Slice 1).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/DiagnosticCodes.cs`  
  Added: BP1022/BP1023/BP1024/BP1025 (AiPrimitive compatibility codes), BP1400 (UnknownEngineEvent), BP1401 (UnknownChannelCommand), BP1402 (UnknownWaitPrimitive), BP1600/BP1601/BP1602 (graph-structure aliases/errors), BP2001 (OrphanedNode), BP2002 (ImplicitCastInserted).

### Modified — Compiler orchestration
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs`  
  Fully implemented `Compile(...)` (wires Stages 2-5; throws `NotImplementedException` for Stages 6-8) and `Validate(...)` (runs Stage 2 with built-in catalog stubs, returns `ValidationResult`).

### Modified — Stage stubs → full implementations
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage1_Parse.cs`  
  Implemented: JSON deserialization via `BlueprintJsonServices.Deserialize`, emits BP0002 on `JsonException`, BP0001 on null result, BP0010/BP0011 on empty AssetId/Name.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage2_Validate.cs`  
  Implemented: 14 validators (V_AssetStructure, V_DispatchKindCompatibility, V_NodeStructure, V_LinkStructure, V_GraphStructure, V_VariablesAndState, V_AiPrimitiveIntent, V_LatentRules, V_ChannelCommandReferences, V_EventGraphReferences, V_WaitNodeReferences, V_PeerReferences, V_TypeReferences, V_DeterminismOrdering). Includes `V_GraphStructure.FindEntryNode` as `internal static` for reuse.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage3_Normalize.cs`  
  Implemented: three passes — MaterializeDefaultPinLiterals (no-op, Slice 1), InsertImplicitCasts (uses `ITypeRegistry.TryGetCoercion`, emits BP2002), EliminateOrphanNodes (reachability from entry, emits BP2001). Includes `SynthesizedGuid` (SHA256 deterministic GUID helper).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage4_TypeResolve.cs`  
  Implemented: resolves field types (Variables/Parameters/WorkingState), two-pass wildcard propagation for ArrayMakeNode/ArrayGetNode, link type compatibility check. Emits BP1500/BP1501/BP1502/BP1503.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage5_Schedule.cs`  
  Implemented: BFS-based basic-block scheduler (`GraphScheduler`). Handles EventEntryNode, ReturnNode, BranchNode, latent nodes (LatentDelayNode, WaitForChannelNode, WaitForEventNode), SetVariableNode, FunctionCallNode (impure/pure), CastNode, CallPeerBlueprintNode, ChannelCommandNode, CallCustomEventNode, SequenceNode. Data-flow resolution with CSE cache. Emits `IrTerm_Suspend` + resume block for latent nodes.

### Created — New supporting files
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/AssemblyInfo.cs`  
  `[assembly: InternalsVisibleTo("Hrot.Blueprints.Tests")]` to expose internal stage classes to test project.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/ValidationContext.cs`  
  Mutable context threaded through Stage 2 validators; carries all catalog/registry references and `SiblingSignaturesById` (Patch 1 override).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/TypedAsset.cs`  
  Output record of Stage 4: `TypedAsset(BlueprintAsset Asset, IReadOnlyDictionary<Guid,IrTypeRef> PinTypes, IReadOnlyDictionary<Guid,IrTypeRef> FieldTypes)`.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/StaticTypeRegistry.cs`  
  Built-in type table (primitives + Vector2/3/4/Quaternion + Fdp.Core.Entity). Coercion table per DD §7.3. Implements `ITypeRegistry`.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInNodeRegistry.cs`  
  Stub implementation of `INodeRegistry`; populated in TASK-CP-005.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/IrPrinter.cs`  
  `internal static IrPrinter.PrettyPrint(IrAsset)` — deterministic human-readable text output, used for snapshot testing and debugging.

### Modified — Test helpers
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder.cs`  
  `GraphBuilder.Return(NodeStatus status)` now sets `ReturnNode.Status = status`.

### Created — New tests
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Stage1To5Tests.cs`  
  8 new tests covering SC1-SC6:  
  - SC1: `Stage1_Parse` returns non-null for valid JSON; emits BP0002 for malformed JSON (2 tests).  
  - SC2: `V_AiPrimitiveIntent` emits BP1100 for `ReturnNode(Running)` in Condition graph; BP1101 for LatentDelayNode (2 tests).  
  - SC3: `V_VariablesAndState` emits BP1210 when Instance state exceeds max tier (1 test).  
  - SC4: `V_PeerReferences` emits BP1301 when peer is in `CallablePeers` but absent from `SiblingSignatures` (1 test).  
  - SC5: `Stage5_Schedule` splits at `WaitForChannelNode`, producing `IrTerm_Suspend` in the pre-wait block and a resume block with `IrTerm_ReturnStatus` (1 test).  
  - SC6: `IrPrinter.PrettyPrint` is deterministic (1 test).

---

## 2. Key Design Decisions

- **Patch 1 override**: `ValidationContext.SiblingSignaturesById` is `IReadOnlyDictionary<Guid, BlueprintSignature>` (not full assets), built from `CompileOptions.SiblingSignatures.ToDictionary(s => s.AssetId)`.
- **Asset model discrepancy**: `CallPeerBlueprintNode` uses `PeerBlueprintId: string` (not `Guid`) and `FunctionRef: string` (not `TargetMethod`); V_PeerReferences uses `Guid.TryParse` on `PeerBlueprintId`.
- **Pin directions**: `"In"` / `"Out"` (not `"Input"` / `"Output"`), matching actual asset schema.
- **BlueprintJsonServices**: has no `GetDeserializeOptions()` method; Stage1 calls `Deserialize(json)` directly in a try/catch.
- **ReturnNode.Status**: missing in original model; added in this batch.
- **Empty catalog stubs**: `BuiltInChannelCommandCatalog`, `BuiltInEngineEventCatalog`, `BuiltInWaitPrimitiveCatalog` return empty lists; validators emit BP1401/BP1402/BP1400 for any nodes requiring catalog lookup. SC5 test bypasses Stage 2 and calls Stage 5 directly with an empty TypedAsset.
- **BlueprintDispatchKind ambiguity**: both `Hrot.Blueprints.Core.Assets` and `Fdp.Toolkit.Blueprints` define this enum; Stage5 and tests use a `using AssetDispatchKind = ...` alias to disambiguate.
- **IrTypeRef alignment**: no `AlignmentBytes` property; alignment computed as `SizeBytes switch { 1=>1, 2=>2, <=4=>4, _=>8 }`.
- **BFS scheduler block IDs**: `IrBlockId.Value` is the sequential index into `_blockBuilders`, enabling O(1) block lookup by ID.

---

## 3. Test Results

| Metric   | Before | After |
|----------|--------|-------|
| Passed   | 160    | 168   |
| Skipped  | 3      | 3     |
| Failed   | 0      | 0     |

Full solution build: **0 errors, 0 warnings**.

New tests added: 8 (`Stage1To5Tests` — all passing).

---

## 4. What Remains (Out of Scope for BATCH-10)

- Stage 6 (Lower): `throw new NotImplementedException` — TASK-CP-003
- Stage 7 (Emit): `throw new NotImplementedException` — TASK-CP-003/004
- Stage 8 (Roslyn compile): TASK-CP-003/004
- `BuiltInNodeRegistry` population: TASK-CP-005
- Catalog population (BuiltIn*Catalog): future task
- `IrPrinter` snapshot regression suite: future task
