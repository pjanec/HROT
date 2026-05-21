# BATCH-12 Completion Report

**Tasks:** TASK-CP-004 -- Stage 7 Emit (C# Code Generation)

---

## 1. Files Created or Modified

### Modified -- Runtime struct

- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs`  
  Added `public uint InstanceVersion;` field to the 16-byte struct.  
  Required by `IrOp_WriteCursorInstanceVersion` which emits `s.Cursor.InstanceVersion = instanceVersion;`.

### Modified -- Stage 7 orchestration

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage7_Emit.cs`  
  Replaced stub throwing `NotImplementedException` with real implementation.  
  Constructs `EmissionContext` and `CSharpEmitter`, calls `emitter.Emit(asset)`, returns `(GeneratedSource, DebugMap)`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs`  
  Replaced `throw new NotImplementedException("Stage 7 not yet implemented (CP-004)")` with
  `Stage7_Emit.Run(lowered, options.Mode, sink)` and a full `CompileResult` constructor
  returning `Succeeded=true` with all fields populated.

### Modified -- Emission context and debug map

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/EmissionContext.cs`  
  Replaced stub with full implementation. Added:
  - `IrGraph? CurrentGraph` (settable) -- for input arg resolution
  - `_blockLabels` dictionary pre-populated from all graphs/blocks
  - `LabelForBlock`, `VarFieldName`, `ParamFieldName`, `CustomEventName`, `ResolveLibraryClass`
  - `WorldVar` property: `"world"` for AiPrimitive, `"((global::Fdp.Core.EntityRepository)view)"` otherwise
  - `StateVar` property: `"ws"` for AiPrimitive, `"s"` for Instance/Library
  - Type alias `using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind`

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs`  
  Extended existing file. Added `_openNodes` dictionary, `RecordNodeStart(Guid, Guid, int)`, and
  `RecordNodeEnd(Guid, int)`. Existing `Record(Guid, Guid, int, int)` and `Build()` preserved unchanged.

### Modified -- CSharpEmitter

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/CSharpEmitter.cs`  
  Replaced stub with full implementation.  
  Manages `StringBuilder`, line counter, indent level. Exposes `WriteLine`, `Indent`, `Outdent`,
  `EmitNodeStart`, `EmitNodeEnd`, and top-level `Emit(IrAsset)` method.  
  Fixed `BlueprintDispatchKind` ambiguity by adding `using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind`
  and qualifying switch cases as `AssetDispatch.Library`, `AssetDispatch.AiPrimitive`, `AssetDispatch.Instance`.  
  `EmitRegistrarClass` uses `BlueprintRegistryStaging` everywhere (Patch C1 compliant).

### Created -- Emitter subsystems

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/BlockEmitter.cs`  
  Emits a single `IrBlock` as C# code. Non-entry blocks get a `__block_{label}:` label.
  Delegates statements to `StatementEmitter` and terminator to `TerminatorEmitter`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/StatementEmitter.cs`  
  Full dispatch over all `IrOperation` subtypes (36+ cases). Handles:
  - Pure/library/peer/AI-primitive calls, const/variable/param reads and writes
  - Component operations (`HasComponent`, `GetComponent`, `GetComponentRO`, `AddComponent`, `RemoveComponent`)
  - Entity operations (`DestroyEntity`, `PublishEvent`)
  - `IrOp_Self`, `IrOp_Time`, `IrOp_DeltaTime`, `IrOp_ReadInstanceVersion`
  - Custom event (comment placeholder for Slice 1), engine event poll loop
  - Channel command (delegates to `ChannelCommandLowering`)
  - All Stage 6 lowering ops: `WriteWorkingStatePhase`, `ReadWorkingStatePhase`,
    `WriteWorkingStateWaitUntilTime`, `ReadWorkingStateWaitUntilTime`,
    `WriteCursorResumeAt`, `ReadCursorResumeAt`, `WriteCursorInstanceVersion`,
    `WriteCursorWaitUntilTime`, `FieldRead`
  - `IrOp_CheckCursorVersion` -- emits inline staleness guard block
  - Debug probes -- emitted as comments in non-Release mode
  - Latent ops (`WaitForChannel`, `WaitForEvent`, `WaitForDelay`) -- throw `InvalidOperationException`
  - Contains internal `TypeRefToCSharp(IrTypeRef)` helper

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/TerminatorEmitter.cs`  
  Handles `IrTerm_Goto`, `IrTerm_Branch`, `IrTerm_Return`, `IrTerm_ReturnStatus`, `IrTerm_FallThrough`.
  `IrTerm_Suspend` throws `InvalidOperationException("IrTerm_Suspend reached Emit stage; should have been lowered in Stage 6.")`.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/ChannelCommandLowering.cs`  
  Emits `GetComponentRW`, `ActiveAction`, optional `unsafe fixed` params block, `ActionInstanceId++`.
  Uses `e.Ctx.WorldVar` for the world variable reference.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/LibraryEmitter.cs`  
  Emits `static class {SanitizedName}_{BlueprintId:X8}_Bp` with one public static method per function graph.
  Sets `e.Ctx.CurrentGraph` before and after each graph body to enable input arg resolution.

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/AiPrimitiveEmitter.cs`  
  Emits `Params` struct, `WorkingState` struct, `InitDefaultWorkingState`, `TickCore`, and thunks per hosting:
  `BTreeTick` (BTreeAction), `BTreeEvaluate` (BTreeCondition), `HsmActivity` with `[UnmanagedCallersOnly]`
  (HsmAction), `HsmGuard` with `[UnmanagedCallersOnly]` (HsmGuard), `Call` (BlueprintCall).

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/InstanceEmitter.cs`  
  Emits `State` struct (with `BlueprintLatentCursor Cursor` + per-variable fields), `VarIds` class,
  `StateSize` property, `InitDefault`, `Event_X` methods (with `float deltaTime` per Q-18.3),
  `Tick` method (with `uint instanceVersion` per Q-18.1), `TickThunk`, and `Event_X_Thunk` methods.

### Created -- Tests

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Stage7Tests.cs`  
  7 new xUnit tests (SC1-SC7).

---

## 2. Testing Results

**Before this batch:** 175 pass, 3 skip, 0 fail  
**After this batch:** 182 pass, 3 skip, 0 fail

| Test | Scenario | Result |
|------|----------|--------|
| SC1 | Library asset "MathUtils" -- class name, BlueprintId const, `Register(BlueprintRegistryStaging staging)` | PASS |
| SC2 | AiPrimitive with BTreeAction+HsmAction -- Params/WorkingState structs, TickCore, BTreeTick, HsmActivity, BehaviorRegistry behReg, static HsmActionDispatcher | PASS |
| SC3 | Instance with variable -- State struct, BlueprintLatentCursor Cursor, `uint instanceVersion` in Tick, StateSize | PASS |
| SC4 | Compile same asset twice -- GeneratedSource is equal (determinism) | PASS |
| SC5 | IrTerm_Suspend in lowered IR -- Stage7_Emit.Run throws InvalidOperationException containing "should have been lowered" | PASS |
| SC6 | Asset named "MoveToAndFire" -- class name matches `MoveToAndFire_{8 hex chars}_Bp` pattern | PASS |
| SC7 | Instance with custom event "OnHit" -- Event_OnHit method contains `float deltaTime` | PASS |

---

## 3. Output Questions

**Q1: What is the exact class name generated for a Library asset named "MathUtils"?**  
`MathUtils_{BlueprintId:X8}_Bp` where `{BlueprintId:X8}` is the 8-character uppercase hex representation
of the asset's `BlueprintId` integer (e.g. `MathUtils_A1B2C3D4_Bp`). The hex suffix is computed from the
asset's structure hash during Stage 5 and is deterministic for a given asset graph.

**Q2: Does the AiPrimitive `Register` method contain `BlueprintRegistryStaging` (not `BlueprintRegistry`)?**  
Yes. `EmitRegistrarClass` in `CSharpEmitter.cs` unconditionally places
`global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging` as the first parameter.
The old `BlueprintRegistry registry` signature is never emitted. The SC1 test asserts this explicitly
with `Assert.DoesNotContain("BlueprintRegistry registry", src)`.

**Q3: Does the Instance `Tick` method signature include `uint instanceVersion` as the last parameter?**  
Yes. `InstanceEmitter` emits:
```csharp
public static void Tick(ref State s, global::Fdp.Core.IWorldView view, float deltaTime, uint instanceVersion)
```
The `uint instanceVersion` parameter is last (Q-18.1). The SC3 test verifies this with
`Assert.Contains("uint instanceVersion)", src)`.

**Q4: List any `IrOperation` subtypes that were NOT handled in `StatementEmitter` and what fallback was used.**  
All subtypes defined in `IrOperation.cs` are handled. Three subtypes deliberately throw rather than emit:
- `IrOp_WaitForChannel`, `IrOp_WaitForEvent`, `IrOp_WaitForDelay` -- these throw
  `InvalidOperationException("... should have been lowered in Stage 6")` because they must be eliminated
  by Stage 6 before reaching Stage 7. Reaching Stage 7 with these ops present is an internal invariant violation.
- `IrOp_RaiseCustomEvent` -- emits a `// TODO Slice 2: raise custom event` comment (functional placeholder
  for the current Slice 1 scope; the event dispatch infrastructure is deferred).

**Q5: Were SC1-SC7 all verified by passing tests?**  
Yes. All 7 tests pass. Run with:
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "Stage7"
```
Result: `Total tests: 7  Passed: 7  Failed: 0  Skipped: 0`

---

## 4. Developer Insights

**Q1: Issues encountered and resolutions?**  
`BlueprintDispatchKind` is defined in both `Hrot.Blueprints.Core.Assets` and `Fdp.Toolkit.Blueprints`.
Both enumerations have the same members (Library=0, AiPrimitive=1, Instance=2). `CSharpEmitter.cs`
imports both namespaces (`using Fdp.Toolkit.Blueprints` for emitting generated-code strings and
`using Hrot.Blueprints.Core.Assets` for IrAsset access), causing 6 CS0104 ambiguity errors.
Resolution: added `using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind` and
qualified all switch cases as `AssetDispatch.Library`, etc. `EmissionContext.cs` uses the same alias
pattern.

**Q2: Weak points in the existing codebase?**  
`BlueprintLatentCursor` in `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs` was
missing the `InstanceVersion` field required by the design. The struct's `[StructLayout(Size=16)]`
explicit layout left 8 bytes of padding after `WaitUntilTime` that was not exposed as named fields.
Adding `InstanceVersion` (uint at offset 8) consumed 4 of those 8 padding bytes. The struct comment
was not updated but the layout is still correct for the 16-byte budget.

**Q3: Design decisions beyond the instructions?**  
- `AiPrimitiveEmitter` detects the presence of `__waitUntilTime` in `WorkingState` by scanning field
  names rather than checking a dedicated flag, keeping the emitter independent of the lowering pipeline.
- `StatementEmitter.TypeRefToCSharp` handles `System.Void` as a special case (returns `"void"`) to
  support function-graph return type resolution in `LibraryEmitter`.
- The `IrOp_PollEngineEvent` loop uses `Length` (not `Count`) for the event queue size, matching the
  ring-buffer API in `Fdp.Core`.

**Q4: Edge cases not mentioned in the spec?**  
- Empty `Hostings` list on an AiPrimitive asset: the registrar emits no BTree or HSM calls, just the
  `staging.Add(...)` call. The `TickCore` method is still emitted.
- Instance asset with zero custom events: `EmitInstanceRegistration` omits the `EventHandlers`
  dictionary initializer entirely (rather than emitting an empty dictionary).
- Library asset with multiple function graphs: each graph becomes an independent `public static` method;
  the class has no shared state so ordering is irrelevant.

**Q5: Performance concerns or optimization opportunities?**  
- `EmissionContext._blockLabels` is a flat `Dictionary<int, string>` scoped to the asset. For assets
  with many graphs it works fine, but if block labels ever need to be per-graph (e.g. two graphs with
  identically-valued `IrBlockId`s), the lookup would be ambiguous. Slice 2 should consider a
  `Dictionary<(int GraphId, int BlockId), string>` keyed by graph to be safe.
- `StringBuilder` is allocated per-emission; no pooling. For the hot batch-compile path this is fine
  since Stage 7 runs once per asset, but a future `ArrayPool<char>`-based writer would reduce GC
  pressure in mass-compilation scenarios.

---

## 5. Outstanding Issues / Next Steps

- `IrOp_RaiseCustomEvent` emits only a comment placeholder. Full custom event dispatch (Slice 2) requires
  the event routing system (TASK-CP-005 or later).
- The `ResolveLibraryClass` helper in `EmissionContext` uses a placeholder format
  (`__LibBp_{id:X8}_Bp`) because the peer blueprint lookup table is not yet wired to the compiler
  pipeline. Slice 2 should pass sibling signatures through to Stage 7 so peer calls resolve to real
  class names.
- `IrOp_AiPrimitiveCall` emits a simplified call that assumes the callee is accessible in the same
  generated namespace. Cross-assembly AI primitive calls are out of scope for Slice 1.
