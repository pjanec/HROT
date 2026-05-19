# Behavior Diagnostics — Task Details

**Design reference:** [DESIGN.md](./DESIGN.md)
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)
**Tech-debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

Every task below has a unique ID and verifiable success conditions, usually expressed as new or amended unit tests. Task descriptions intentionally avoid duplicating design content — each task points at the relevant DESIGN.md chapter.

---

## Phase 1 — Foundation: Memory & Kernel Contracts

### T1.1 — BTreeTraceOpCode and ITreeTracer in Fbt.Kernel

**Design ref:** [DESIGN §3.1](./DESIGN.md#31-btreetraceopcode-new-enum-in-fbtkernel), [§4.1](./DESIGN.md#41-the-itreetracer-contract-new-in-fbtkernel)

**Scope:**
- Add `Fbt.BTreeTraceOpCode : byte` enum in `Fbt.Kernel` (new file `BTreeTraceOpCode.cs`).
- Add `Fbt.ITreeTracer` interface in `Fbt.Kernel` (new file `ITreeTracer.cs`) with the five methods listed in DESIGN §4.1.

**Constraints:**
- No reference to ECS components, `Fdp.Toolkits`, or any 1024-byte struct from inside `Fbt.Kernel`.
- `ITreeTracer` defines methods only; no default implementations.

**Success conditions:**
- New unit test in `Fbt.Tests`: a custom test context struct `struct TestTracerContext : IAIContext, ITreeTracer` compiles, can be instantiated, and its interface methods record call counts to a static counter for verification.
- `dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln` succeeds.

---

### T1.2 — HsmTraceContext in Fhsm.Kernel.Data

**Design ref:** [DESIGN §5.2](./DESIGN.md#52-hsmtracecontext-new-unmanaged-in-fhsmkerneldata)

**Scope:**
- Add `Fhsm.Kernel.Data.HsmTraceContext` unmanaged struct (new file `Data/HsmTraceContext.cs`).
- Implement instance methods: `WriteTransition`, `WriteStateChange`, `WriteEventHandled`, `WriteGuardEvaluated`, `WriteActionExecuted`, `WriteError`, `WriteConflict`.
- Each `WriteX` short-circuits on `FilterLevel` bit-mask test before writing.
- **All writes use a strict 16-byte stride** regardless of payload struct size — see DESIGN §5.2. Internal helper `WriteRecord<T>(T* payload)` zero-fills 16 bytes at the current offset, copies the actual payload (≤16 bytes) into the slot, advances cursor by exactly 16, wraps `*WritePos = (ushort)((*WritePos + 16) % CapacityBytes);`. Saturate `*RecordCount` at `MaxRecords`.
- Do **not** advance the cursor by `sizeof(T)`. Mixing 12- and 16-byte strides would let a 16-byte write at offset 1004 overrun 4 bytes into adjacent ECS-chunk memory.

**Constraints:**
- All buffer pointers come from caller; struct owns no allocation.
- All methods `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.
- The 12-byte structs (`TraceStateChange`, `TraceEventHandled`, `TraceActionExecuted`, `TraceError`, `ConflictRecord`) retain their existing layouts; the unused 4 trailing bytes of their 16-byte slot are zero-padded by the helper.

**Success conditions:**
- New unit tests in `Fhsm.Tests/Tooling/HsmTraceContextTests.cs`:
  - Allocate a `stackalloc byte[1008]` buffer (matching `HsmTraceWorkingMemory1024.PayloadBytes`), attach an `HsmTraceContext` over it, write 100 records, verify ring wrap and that `RecordCount` saturates at 63.
  - `WritePos_AlwaysWithinCapacityBytes` — after every write, `0 <= *WritePos < CapacityBytes`.
  - `WriteStride_IsAlways16` — write a 12-byte `TraceStateChange` followed by a 16-byte `TraceTransition`; assert the second record starts exactly 16 bytes after the first (not 12).
  - `WriteAtOffset_NearWrap_DoesNotOverflowBuffer` — manually set `*WritePos = 1000`, write any 16-byte record, then dump the entire byte buffer and assert bytes `[1008, 1024)` are unchanged (no overrun past `CapacityBytes`).
  - `FilterLevel` of `TraceLevel.None` results in zero records written.
  - For each opcode, the bytes written are payload-identical to the equivalent old `HsmTraceBuffer.WriteX` output (golden-bytes comparison) for the in-payload bytes — trailing pad bytes are zero.
- `dotnet build FDP/ExtDeps/FastHSM/FastHSM.sln` succeeds.

---

### T1.3 — BTreeTraceRecord + BTreeTraceWorkingMemory1024 + write APIs

**Design ref:** [DESIGN §3.2](./DESIGN.md#32-btreetracerecord-16-byte-union-fdptoolkitbehaviordiagnostics), [§3.3](./DESIGN.md#33-btreetraceworkingmemory1024), [§4.5](./DESIGN.md#45-write-api-on-btreetraceworkingmemory1024), [§4.6](./DESIGN.md#46-instanceid-on-btree-records)

**Scope:**
- Create namespace `Fdp.Toolkit.Behavior.Diagnostics` in `Fdp.Toolkits`. New files: `BTreeTraceRecord.cs`, `BTreeTraceWorkingMemory1024.cs`.
- `BTreeTraceRecord`: `[StructLayout(LayoutKind.Explicit, Size = 16)]` with the union fields from DESIGN §3.2.
- `BTreeTraceWorkingMemory1024`: 1024 bytes total. Header `ushort WritePos; ushort RecordCount; uint LastInstanceId;` (8 bytes). Payload `fixed byte Buffer[1016]`. Constants `RecordStride = 16`, `CapacityRecords = 63`, `PayloadBytes = 1008` (= 63 × 16), `BufferBytes = 1016`.
- Implement instance write methods (all `[MethodImpl(AggressiveInlining)]`): `WriteNodeEvaluated`, `WriteScopePushed`, `WriteScopePopped`, `WriteWaitStarted`, `WriteWaitCompleted`, `WriteChannelMutated`, `WriteError`.
- **`NextRecord()` MUST pre-wrap `WritePos` within `[0, PayloadBytes)` exactly as shown in DESIGN §4.5** — `WritePos = (ushort)((WritePos + RecordStride) % PayloadBytes);`. Do **not** use the pattern `offset = WritePos % CapacityBytes; WritePos += 16;` — `ushort` overflow occurs at 65536, and 65536 % 1008 ≠ 0, so deferred-modulo arithmetic drifts after the first overflow and corrupts chronological ordering.
- `NextRecord()` zeros the 16-byte slot before returning, stamps `record->InstanceId = LastInstanceId`, and returns the typed pointer.

**Constraints:**
- Component decorated `[ComponentId(BehaviorApplicationComponentIds.BTreeTraceWorkingMemory)]` `[DataPolicy(DataPolicy.NoSave)]`.
- `sizeof(BTreeTraceWorkingMemory1024) == 1024` and `sizeof(BTreeTraceRecord) == 16` — assert via static unit test.

**Success conditions:**
- Unit tests in `Fdp.Toolkits.Tests`:
  - `BTreeTraceWorkingMemory1024_SizeIs1024_Test`.
  - `WriteNodeEvaluated_FillsRingThenWraps_RecordCountSaturatesAt63`.
  - `WritePos_AlwaysWithinPayloadBytes` — after writing 200 records (~3.2× capacity), assert `0 <= WritePos < PayloadBytes` on every iteration; assert the slot indices form the expected modulo-63 cycle (`0, 1, …, 62, 0, 1, …`).
  - `WriteChannelMutated_UnionFieldsRoundTrip` — write a channel record, read back via the union layout, confirm `Channel`, `ActiveAction`, `ChannelStatus` fields are intact.
  - `NextRecord_ZeroesRecordBeforeReturn` — pre-fill buffer with `0xFF`, write a `NodeEvaluated` record, confirm bytes 1, 13, 14, 15 (Reserved + unused union bytes for NodeEvaluated) are zero in the resulting record.
  - `NextRecord_StampsLastInstanceId` — set `mem.LastInstanceId = 0xABCDEF12`, write a record, confirm `record->InstanceId == 0xABCDEF12`.

---

### T1.4 — HsmTraceWorkingMemory1024

**Design ref:** [DESIGN §3.4](./DESIGN.md#34-hsmtraceworkingmemory1024)

**Scope:**
- New file `Fdp.Toolkit.Behavior.Diagnostics/HsmTraceWorkingMemory1024.cs`.
- Identical 1024-byte layout to BTree variant (`WritePos`, `RecordCount`, `LastInstanceId` header, 1016-byte buffer).
- Decorated `[ComponentId(BehaviorApplicationComponentIds.HsmTraceWorkingMemory)]` `[DataPolicy(DataPolicy.NoSave)]`.

**Success conditions:**
- Unit test `HsmTraceWorkingMemory1024_SizeIs1024_Test` in `Fdp.Toolkits.Tests`.
- The component can be registered via `world.RegisterComponent<HsmTraceWorkingMemory1024>()` without throwing in an isolated test repository.

---

### T1.5 — Component IDs + CognitiveComponentRegistry registration

**Design ref:** [DESIGN §3.5](./DESIGN.md#35-id-allocation), [§3.6](./DESIGN.md#36-registration)

**Scope:**
- In [BehaviorApplicationComponentIds.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorApplicationComponentIds.cs), add:
  - `public const byte BTreeTraceWorkingMemory = 163;`
  - `public const byte HsmTraceWorkingMemory   = 164;`
  - `public const byte DebugState              = 165;` (used in T3.2)
  - Verify these are not already used by other components in either `BehaviorApplicationComponentIds` or `HrotComponentIds`.
- In [CognitiveComponentRegistry.cs](Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs), add:
  - `world.RegisterComponent<BTreeTraceWorkingMemory1024>();`
  - `world.RegisterComponent<HsmTraceWorkingMemory1024>();`
  - (DebugState registration is done in T3.2.)

**Success conditions:**
- `CognitiveComponentRegistry_RegistersTraceBuffers` test in `Hrot.SimHost.Tests`: calls `RegisterAll(world)`, then asserts `world.IsComponentTypeRegistered<BTreeTraceWorkingMemory1024>()` and `world.IsComponentTypeRegistered<HsmTraceWorkingMemory1024>()` both return true.
- No component-ID collision: a sanity test asserts the new IDs (163, 164) are not used by any existing registered component (gather all distinct `[ComponentId]` values and assert uniqueness).

---

## Phase 2 — Kernel Refactoring

### T2.1 — Interpreter ITreeTracer constraint + emit hooks

**Design ref:** [DESIGN §4.2](./DESIGN.md#42-interpreter-generic-constraint-update), [§4.3](./DESIGN.md#43-engine-hooks)

**Scope:**
- Update the type parameter `TContext` constraint on `Interpreter<TBlackboard, TContext>` to add `, ITreeTracer`.
- In `ExecuteAction`: after computing `status`, call `ctx.TraceNodeEvaluated(nodeIndex, status)` before the running-state mutation.
- In `ExecuteWait`: at wait start, call `ctx.TraceWaitStarted(nodeIndex, duration)`; at wait completion, call `ctx.TraceWaitCompleted(nodeIndex, duration)`.
- For scope changes: at every site in `Interpreter` that calls `state.PushNode(...)` / `state.PopNode()`, follow up with `ctx.TraceScopePushed(state.StackPointer)` / `ctx.TraceScopePopped(state.StackPointer)`.

**Constraints:**
- No new `unsafe` blocks inside `Interpreter` — all unsafe code lives in the concrete `BTreeContext` implementation.
- All other `Interpreter` behavior is unchanged.

**Success conditions:**
- New test in `Fbt.Tests/Interpreter/InterpreterTracingTests.cs`:
  - A custom `RecordingTracer` struct records `(opcode, nodeIndex, status, depth, duration)` tuples.
  - Drive a simple tree (selector → sequence → leaf condition + leaf action) and assert the recorded sequence matches the expected ordered evaluation log.
  - Drive a Wait node and assert `WaitStarted` is logged on the first tick and `WaitCompleted` on the completing tick.
- All existing `Fbt.Tests` continue to pass once the test-context structs are updated to implement `ITreeTracer` (no-op implementation is allowed for legacy tests; covered under T6.1).

---

### T2.2 — BTreeContext implements ITreeTracer + holds TraceBuffer pointer

**Design ref:** [DESIGN §4.4](./DESIGN.md#44-concrete-btreecontext-update)

**Scope:**
- In [BTreeContext.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/BTreeContext.cs):
  - Add `unsafe BTreeTraceWorkingMemory1024* TraceBuffer;` field.
  - Add `internal uint _instanceId;`.
  - Add `IAIContext, ITreeTracer` to the implements list.
  - Implement the five `Trace*` methods. Each follows the pattern `if (TraceBuffer != null) TraceBuffer->WriteXxx(...)` with `[MethodImpl(AggressiveInlining)]`.
- `_instanceId` is stamped via the tick system (T4.2).

**Success conditions:**
- New test in `Fdp.Toolkits.Tests/Behavior/BTreeContextTracingTests.cs`:
  - Construct a `BTreeContext` with `TraceBuffer = null`; calling each `Trace*` method does not throw.
  - Construct a `BTreeContext` with an attached `BTreeTraceWorkingMemory1024`; call each `Trace*` method; verify the corresponding `BTreeTraceRecord` was written with the right opcode.

---

### T2.3 — Delete HsmTraceBuffer and SetTraceBuffer

**Design ref:** [DESIGN §5.1](./DESIGN.md#51-eradicate-the-global-static)

**Scope:**
- Delete file [FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmTraceBuffer.cs](FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmTraceBuffer.cs).
- In [HsmKernelCore.cs](FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs): delete the static `_traceBuffer` field and the `SetTraceBuffer` method.
- Internal call sites that previously read `_traceBuffer?.WriteX` are rewritten in T2.4 to consume the new `HsmTraceContext*` parameter — they cannot be left in a broken intermediate state inside the same compilation. **T2.3 and T2.4 must land as a single atomic change**.

**Success conditions:**
- A grep over `FDP/ExtDeps/FastHSM/src` for `_traceBuffer` and `SetTraceBuffer` returns zero matches.
- A grep over `FDP/ExtDeps/FastHSM/src` for the type name `HsmTraceBuffer` returns zero matches.

---

### T2.4 — Thread HsmTraceContext* through kernel pipeline

**Design ref:** [DESIGN §5.3](./DESIGN.md#53-pipeline-threading)

**Scope:**
- Add `HsmTraceContext* traceCtx = null` parameter to:
  - `HsmKernel.Update<TInstance,TContext>` and `HsmKernel.UpdateBatch<...>`
  - `HsmKernelCore.UpdateBatchCore`
  - `HsmKernelCore.ProcessInstancePhase`
  - `HsmKernelCore.ExecuteTransition`
  - Any other internal call site that previously dereferenced `_traceBuffer`.
- At each former trace-write site, replace with: `if (traceCtx != null && (header->Flags & InstanceFlags.DebugTrace) != 0) traceCtx->WriteX(...);`.

**Success conditions:**
- `Fhsm.Tests/Tooling/TraceTests.cs` and `TraceSymbolicationTests.cs` are rewritten (under T6.3) to construct a stack-local `HsmTraceContext` over a `stackalloc byte[1008]` buffer, pass its pointer into `HsmKernel.Update`, and assert the same expected opcodes are recorded as before.
- A new integration test in `Fhsm.Tests/Integration/ConcurrentTraceTest.cs`: two instances with distinct `MachineId` and distinct `traceCtx` pointers, executed in parallel `Task.Run`s, produce disjoint trace record streams (no cross-instance corruption).

---

### T2.5 — HsmKernelBridge.TraceContext field

**Design ref:** [DESIGN §5.4](./DESIGN.md#54-hsmkernelbridge-extension)

**Scope:**
- In [HsmTickSystem.cs:23](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs#L23), the `HsmKernelBridge` struct gains a `public HsmTraceContext* TraceContext;` field.

**Success conditions:**
- A new test verifies that user-authored HSM guards/actions can write a `TraceError` via `bridge.TraceContext->WriteError(...)` when the bridge is constructed with a non-null pointer.
- `bridge.TraceContext == null` is the default state and does not break existing HSM execution (covered by all existing `Fhsm.Tests` continuing to pass post-T6.3).

---

## Phase 3 — Generic Debug-State Plumbing

### T3.1 — Move GlobalDebugSettings into Hrot.Common

**Design ref:** [DESIGN §13.1](./DESIGN.md#131-move-globaldebugsettings-into-hrotcommon)

**Scope:**
- Move [GlobalDebugSettings.cs](Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs) from `Hrot.IG.Gizmos` to `Hrot.Common`. Change namespace accordingly.
- Add field `[MarshalAs(UnmanagedType.I1)] public bool AutoEnableAiTracing;`.
- Update every `using Hrot.IG.Gizmos;` (or equivalent) that referenced this type to point to `Hrot.Common`.

**Constraints:**
- Default value of `AutoEnableAiTracing` is `false`.
- The component ID and `DataPolicy` attributes remain unchanged.

**Success conditions:**
- `Hrot.SimHost.Tests/GlobalDebugSettingsMigrationTests.cs`: registering the singleton from `Hrot.Common.GlobalDebugSettings` succeeds; the field `AutoEnableAiTracing` is reachable and defaults to `false`.
- `IOS-IG-SimHost.sln` builds cleanly with no leftover references to `Hrot.IG.Gizmos.GlobalDebugSettings`.

---

### T3.2 — DebugState, BehaviorDebugFlags, BehaviorApplicationComponentIds.DebugState

**Design ref:** [DESIGN §6.1](./DESIGN.md#61-the-component) (note the project-dependency deviation explained in §6 header)

**Scope:**
- In `Fdp.Toolkits` under namespace `Fdp.Toolkit.Behavior.Diagnostics`: new files `DebugState.cs`, `BehaviorDebugFlags.cs`.
- `BehaviorDebugFlags` is `[Flags] : uint` with the six members listed in DESIGN §6.1.
- `DebugState` is `[StructLayout(LayoutKind.Sequential)]` `[ComponentId(BehaviorApplicationComponentIds.DebugState)]` `[DataPolicy(DataPolicy.Transient)]` containing `public BehaviorDebugFlags Behavior;`.
- The component ID constant `DebugState = 165` is added to `BehaviorApplicationComponentIds` in T1.5.
- In `CognitiveComponentRegistry.RegisterAll`, add `world.RegisterComponent<DebugState>()`.

**Success conditions:**
- Unit test: `world.RegisterComponent<DebugState>()`, then `world.AddComponent(entity, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer | BehaviorDebugFlags.EmitToLog })` succeeds; reading back yields the same bits.
- `Hrot.Presentation.Tests/DebugStateRenderingTests.cs`: when `DebugState` is rendered via the standard `ComponentEditDrawer`, the `Behavior` field is drawn as a `[Flags]` enum (six checkboxes), and clicking the checkboxes updates the underlying value (test via direct call to `DrawPrimitiveInput` with a fake ImGui backend, or skip if no fake exists — at minimum unit-test that `Attribute.IsDefined(typeof(BehaviorDebugFlags), typeof(FlagsAttribute))` is true).

---

### T3.3 — PatchDebugStateCommand managed event

**Design ref:** [DESIGN §6.2](./DESIGN.md#62-patchdebugstatecommand-managed-event-in-fdptoolkitbehaviordiagnostics)

**Scope:**
- New file `Fdp.Toolkits/Behavior/Diagnostics/PatchDebugStateCommand.cs`.
- Define `public sealed class PatchDebugStateCommand { public Entity Target; public string PatchJson = string.Empty; }`.
- Register the managed event in the existing event registration site (same place where other managed events register — search for `RegisterManagedEvent<` to find it; likely `CognitiveComponentRegistry` or a similar bootstrapper).

**Success conditions:**
- `Fdp.Toolkits.Tests/Behavior/Diagnostics/PatchDebugStateCommandRegistrationTests.cs`: confirms `bus.PublishManaged(new PatchDebugStateCommand { ... })` does not throw post-registration.
- Confirms the event has no `[EventId]` (it's managed-only, not blittable).

---

### T3.4 — DebugStatePatchCompiler

**Design ref:** [DESIGN §6.3](./DESIGN.md#63-expression-tree-patch-compiler)

**Scope:**
- New file `Fdp.Toolkits/Behavior/Diagnostics/DebugStatePatchCompiler.cs`.
- Public `static void Build()` — compiles per-top-level-field setter delegates at startup.
- Public `static void ApplyPatch(ref DebugState state, string json)` — parses JSON, dispatches each property to its setter.
- For `[Flags]` enum fields: nested JSON object with per-member boolean → bitwise `|=` / `&= ~` assignments.
- For primitive fields and `FixedString32`: direct assignment from `JsonElement.GetXxx()`.

**Constraints:**
- Compilation happens once at startup (cache compiled delegates in a static dictionary).
- `ApplyPatch` performs zero allocations per call besides what `JsonDocument.Parse` requires (acceptable — UI-thread only).

**Success conditions:**
- New unit tests in `Fdp.Toolkits.Tests/Behavior/Diagnostics/DebugStatePatchCompilerTests.cs`:
  - `ApplyPatch_EnableTraceBuffer_SetsBit` — `ApplyPatch` with `{"Behavior":{"EnableTraceBuffer":true}}` sets the correct bit.
  - `ApplyPatch_EnableTraceBuffer_False_ClearsBit` — same with `false` clears the bit while preserving other set bits.
  - `ApplyPatch_UnknownProperty_IgnoredSilently`.
  - `ApplyPatch_MalformedJson_Throws_JsonException`.
  - `ApplyPatch_PatchesMultipleBitsInOneCall`.

---

### T3.5 — DebugStatePatchSystem

**Design ref:** [DESIGN §6.4](./DESIGN.md#64-debugstatepatchsystem)

**Scope:**
- New file `Fdp.Toolkits/Behavior/Diagnostics/DebugStatePatchSystem.cs`.
- `[UpdateInPhase(SystemPhase.Input)]`, implements `IEcsModuleSystem`.
- Drains `repo.Bus.ReadManaged<PatchDebugStateCommand>()`. For each:
  - If `!repo.IsAlive(target)`, skip.
  - If `!repo.HasComponent<DebugState>(target)`, `AddComponent(target, new DebugState())`.
  - `DebugStatePatchCompiler.ApplyPatch(ref repo.GetComponentRW<DebugState>(target), cmd.PatchJson)`.

**Success conditions:**
- Integration test in `Fdp.Toolkits.Tests/Behavior/Diagnostics/DebugStatePatchSystemTests.cs`:
  - Publish a patch command, run one `Tick()`; the target entity has the expected `DebugState`.
  - Publish a second patch that clears a bit, run another `Tick()`; the bit is cleared without resetting other bits.
  - Publish a patch for a non-alive entity; no exception, no component appears on any other entity.

---

## Phase 4 — Runtime Wiring

### T4.1 — TraceBufferLifecycleSystem

**Design ref:** [DESIGN §7](./DESIGN.md#7-trace-buffer-provisioning-system)

**Scope:**
- New file `Fdp.Toolkit.Behavior.Diagnostics/TraceBufferLifecycleSystem.cs`.
- `[UpdateInPhase(SystemPhase.BeforeSync)]`. Iterates all entities with `DebugState`. For each:
  - If `BehaviorState.BrainTier == BrainTierBTree` and `DebugState.Behavior.EnableTraceBuffer` set → ensure `BTreeTraceWorkingMemory1024` exists (add if missing). If flag cleared and component present → remove component.
  - Mirror logic for HSM tier.

**Success conditions:**
- Unit test in `Fdp.Toolkits.Tests/Behavior/TraceBufferLifecycleSystemTests.cs`:
  - Entity with BTree tier + `DebugState.Behavior = EnableTraceBuffer` → after one tick, has `BTreeTraceWorkingMemory1024`.
  - Same entity, then clear bit and tick → component removed.
  - Entity with HSM tier behaves the same way for `HsmTraceWorkingMemory1024`.

---

### T4.2 — BTreeTickSystem wiring

**Design ref:** [DESIGN §8.1](./DESIGN.md#81-btreeticksystem)

**Scope:**
- In [BTreeTickSystem.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs):
  - Inside the per-entity loop, resolve `tracePtr` per DESIGN §8.1.
  - Stamp `traceMem.LastInstanceId = behavior.InstanceId` before tick.
  - Inject `TraceBuffer = tracePtr` and `_instanceId = behavior.InstanceId` into `BTreeContext`.
  - **Crucial:** when `EnableTraceBuffer` is off, do **not** call `GetComponentRW<BTreeTraceWorkingMemory1024>` — the chunk version must not be bumped.

**Success conditions:**
- Integration test in `Fdp.Toolkits.Tests/Behavior/BTreeTickSystemTracingTests.cs`:
  - Run a tree with `DebugState.EnableTraceBuffer` set: after one tick, the `BTreeTraceWorkingMemory1024.RecordCount > 0` and at least one `NodeEvaluated` record is present.
  - Same tree with the flag cleared: zero records appear.
  - Bonus: assert that with the flag cleared, the chunk's `LastChangeTick` for the trace component does not advance between ticks (requires reading `_chunkVersions` via `EntityRepository` internals — if not exposed, skip and document as DEBT).

---

### T4.3 — HsmTickSystem wiring

**Design ref:** [DESIGN §8.2](./DESIGN.md#82-hsmticksystemt)

**Scope:**
- In [HsmTickSystem.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs):
  - Resolve `tracePtr` and construct `HsmTraceContext` per DESIGN §8.2.
  - Update `component.Header.Flags |= InstanceFlags.DebugTrace` when tracing is enabled, clear when disabled.
  - Map `BehaviorDebugFlags.HsmTraceTier1/2/3` bits to `TraceLevel` via a small helper `ResolveTraceLevel(BehaviorDebugFlags)`.
  - Pass `traceCtxPtr` to both `HsmKernelBridge.TraceContext` and `HsmKernel.Update(...)`.

**Success conditions:**
- Integration test: an HSM with `EnableTraceBuffer | HsmTraceTier3` set produces transition/state-change/action/guard records on `HsmTraceWorkingMemory1024` after the first tick that performs a transition.
- With the flag cleared, zero records appear.
- Concurrency test: two HSMs ticked in the same frame produce non-interleaved records (each buffer carries only its own entity's traces).

---

### T4.4 — BehaviorDefinition.HsmMetadata + AiBehaviorFactory

**Design ref:** [DESIGN §10](./DESIGN.md#10-behaviordefinitionhsmmetadata)

**Scope:**
- In `BehaviorDefinition`: add `public MachineMetadata? HsmMetadata { get; init; }`.
- In `Fhsm.Compiler.HsmEmitter`: add an `Emit(HsmFlatGraph flat, out MachineMetadata metadata)` overload that returns both the blob and metadata. Existing single-argument `Emit` can keep working by chaining and discarding metadata.
- In [AiBehaviorFactory.cs](Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs): for every site that calls `HsmEmitter.Emit(idleFlat)`, switch to the new overload and populate `HsmMetadata = metadata` on the resulting `BehaviorDefinition`.

**Success conditions:**
- Unit test: every HSM-backed `BehaviorDefinition` produced by `AiBehaviorFactory` has `HsmMetadata != null`.
- Unit test: `HsmMetadata.GetStateName(0)` returns a non-empty string for at least one known state (verifying the metadata is actually populated and not just non-null with empty dictionaries).

---

## Phase 5 — UI, Translators, Auto-Enable

### T5.1 — ImGui renderers

**Design ref:** [DESIGN §9](./DESIGN.md#9-imgui-renderers)

**Scope:**
- New file `Hrot.Presentation/Renderers/BTreeTraceWorkingMemoryRenderer.cs`. `[ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]`. Static `BehaviorRegistryAccessor` set at composition root (where `BrainBlackboardRenderer.BehaviorRegistryAccessor` is already set).
- New file `Hrot.Presentation/Renderers/HsmTraceWorkingMemoryRenderer.cs`. Reads `def.HsmMetadata` from the `BehaviorDefinition` looked up via `BehaviorRegistryAccessor`.
- Both renderers iterate the ring in chronological order; render an ImGui table with columns: `Tick | OpCode | Detail (numeric id + resolved name) | Result`.

**Success conditions:**
- Manual smoke test: load a scenario, enable tracing on an entity via context menu, observe trace records in the inspector. (Documented in ONBOARDING.md.)
- Unit test for the chronological iteration helper: given a buffer with `RecordCount = CapacityRecords` and `WritePos = X`, the returned record order starts at the oldest written record and ends at the newest.

---

### T5.2 — JSON translators

**Design ref:** [DESIGN §11](./DESIGN.md#11-json-dump-translators)

**Scope:**
- New file `Hrot.SimHost/Serializers/BTreeTraceWorkingMemoryTranslator.cs`. Constructor `(BehaviorRegistry registry)`. `Inject` empty.
- New file `Hrot.SimHost/Serializers/HsmTraceWorkingMemoryTranslator.cs`. Same shape.
- In [HrotScenarioSerializerFactory.cs](Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs), `.RegisterTranslator(...)` for both.

**Success conditions:**
- Unit test in `Hrot.SimHost.Tests/Serializers/BTreeTraceTranslatorTests.cs`:
  - Construct an entity with a populated `BTreeTraceWorkingMemory1024`; call `translator.Extract(...)`; assert the resulting JSON object has a `History` array with the expected record count and each record's `NodeIndex`, `NodeName`, `Status` are present.
  - Call `translator.Inject(...)` — verify it does nothing (the entity gains no `BTreeTraceWorkingMemory1024` component).
- Mirror tests for HSM.

---

### T5.3 — Context menu + GlobalActionIds

**Design ref:** [DESIGN §12](./DESIGN.md#12-ui-context-menu-integration)

**Scope:**
- In [GlobalActionIds.cs](Hrot/Engine/Hrot.Common/Constants/GlobalActionIds.cs), add `ToggleAiTrace = 251`, `ToggleAiTraceLog = 252`.
- Register the action delegates in the composition root where other `GlobalActionRegistry.Register` calls happen (search for `actionRegistry.Register` or `GlobalActionRegistry`).
- Add the context menu items via `LambdaEntityContextMenuHandler` in the same place the entity inspector context menus are registered (find via grep for `RegisterContextMenuHandler` — most likely `EditorSubsystem.cs:870`, `CgfSubsystem.cs:514`, `SimHostVisualization.cs:166`).

**Success conditions:**
- Unit test in `Hrot.SimHost.Tests/Actions/ToggleAiTraceActionTests.cs`:
  - Dispatch `GlobalActionRequestedEvent { ActionId = GlobalActionIds.ToggleAiTrace, Target = entity }` to the interaction bus; tick the dispatcher and patch systems; the entity's `DebugState.Behavior` has `EnableTraceBuffer` set.
  - Dispatch again → the bit is cleared.
- Same for `ToggleAiTraceLog`.

---

### T5.4 — AiDiagnosticsTkbTranslator

**Design ref:** [DESIGN §13.2](./DESIGN.md#132-aidiagnosticstkbtranslator)

**Scope:**
- New file `Hrot.SimHost/Bootstrapping/AiDiagnosticsTkbTranslator.cs` (not in `Fdp.Toolkits` — see DESIGN §13.2 for the project-location rationale).
- `GetConsumedDescriptors() => Array.Empty<Type>()` (observer pattern).
- `Inject` per DESIGN §13.2 — read singleton, check `AutoEnableAiTracing`, read `BehaviorProfileDto`, stamp appropriate trace component + `DebugState.EnableTraceBuffer`.
- In [SimHostNodeBootstrapper.cs:133](Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs#L133), add `new AiDiagnosticsTkbTranslator()` to the translator list.

**Success conditions:**
- Unit test in `Hrot.SimHost.Tests/AiDiagnosticsTkbTranslatorTests.cs`:
  - With `AutoEnableAiTracing = false`, promoting an AI ghost yields an entity with no `BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024` / `DebugState`.
  - With `AutoEnableAiTracing = true`, promoting an AI ghost (BTree tier) yields an entity with `BTreeTraceWorkingMemory1024` present and `DebugState.Behavior & EnableTraceBuffer != 0`.
  - Same with HSM tier yields `HsmTraceWorkingMemory1024`.
- Verify the translator does not block or duplicate work done by `BehaviorTkbTranslator` (existing AI-bootstrap tests still pass).

---

### T5.5 — BehaviorLog emission

**Design ref:** [DESIGN §14](./DESIGN.md#14-behaviorlog-integration)

**Scope:**
- In [BTreeTickSystem.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs):
  - Before invoking `Tick`, capture `startWritePos = tracePtr->WritePos` if tracing is enabled.
  - After `Tick`, if `EmitToLog` set and `BehaviorLog.IsTraceEnabled`, compute the delta correctly across the 1008-byte payload wrap (see DESIGN §14.1): `int bytesWritten = end - start; if (bytesWritten < 0) bytesWritten += PayloadBytes;`. Do **not** use the `(ushort)(end - start)` ushort-wrap trick — the ring wraps at 1008, not at 65536, so that trick produces garbage when a frame crosses the wrap.
  - Iterate records using `offset = (startWritePos + i * 16) % PayloadBytes`.
  - Dispatch each formatted record to `BehaviorLog.Trace(entity, repo, message, "BTreeTrace")`.
- Mirror in [HsmTickSystem.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs) — same wrap math, using `def.HsmMetadata` to resolve names.

**Success conditions:**
- Unit test: with `EmitToLog` enabled and an NLog memory target configured to capture `AI.Behavior` logs, after one tick the captured log lines contain the expected node names and statuses.
- Unit test: with `EmitToLog` cleared, **no** log lines appear from the trace system (independent of trace buffer activity).
- `Delta_AcrossPayloadWrap_DecodesCorrectRecordCount` — manually preset `WritePos = 992` (close to wrap) and `RecordCount = 62`; run a tick that writes 3 records (causing wrap to ~32); assert the emitter logs exactly 3 lines, not 4034 (the bug-version count from a `ushort` subtraction).
- Allocation test (informational, not gating): with `EmitToLog` disabled, profiler shows zero string allocations from the tick path stemming from log emission code.

---

## Phase 6 — Out-of-Solution Examples & Unit Tests

**Important — these tasks address projects NOT in `IOS-IG-SimHost.sln`** (they live in the FastBTree / FastHSM sub-solutions). Without these tasks, the kernel sub-solutions will fail to build even though the main solution is green.

### T6.1 — Fix Fbt.Tests for ITreeTracer constraint

**Scope:**
- Every test that constructs an `Interpreter<TBlackboard, TContext>` or runs `interpreter.Tick(...)` must update its `TContext` struct to also implement `ITreeTracer` (a no-op `struct : IAIContext, ITreeTracer` is acceptable).
- Add a shared `NullTracerContext` helper struct in `Fbt.Tests/Helpers/NullTracerContext.cs` so individual tests can use it instead of duplicating the trivial implementation.

**Success conditions:**
- `dotnet test FDP/ExtDeps/FastBTree/FastBTree.sln` passes.

---

### T6.2 — Fix Fbt.Benchmarks, Fbt.Examples.*, Fbt.Demo.Visual.Tests

**Scope:**
- Same change as T6.1 across all FastBTree out-of-solution projects: `Fbt.Benchmarks`, `Fbt.Examples.Console`, `Fbt.Examples.FluentBTree`, `Fbt.Examples.FluentBTree.Trees`, `Fbt.Demo.Visual.Tests`.

**Success conditions:**
- `dotnet build` each of these projects individually succeeds.
- Benchmarks still run (`dotnet run -c Release` against `Fbt.Benchmarks` does not crash on startup).

---

### T6.3 — Fix Fhsm.Tests after kernel refactor

**Scope:**
- Rewrite `Fhsm.Tests/Tooling/TraceTests.cs`:
  - Replace `HsmKernelCore.SetTraceBuffer(...)` and the managed `HsmTraceBuffer` usage with a stack-local `HsmTraceContext` constructed over a `stackalloc byte[1008]` buffer plus header `ushort WritePos; ushort RecordCount;`.
  - Pass the `HsmTraceContext*` to `HsmKernel.Update(...)` as the new `traceCtx` parameter.
  - Iterate the resulting raw byte buffer (re-implementing the opcode dispatch inline or via a test helper).
- Rewrite `Fhsm.Tests/Tooling/TraceSymbolicationTests.cs` the same way.
- Update `Fhsm.Tests/Kernel/OrthogonalRegionTests.cs` and `FailSafeTests.cs` if they reference `HsmTraceBuffer` (verified during T1.2 setup that they do — confirm during implementation).
- Add a shared `TestHsmTraceContext` helper in `Fhsm.Tests/Helpers/TestHsmTraceContext.cs`.

**Success conditions:**
- `dotnet test FDP/ExtDeps/FastHSM/FastHSM.sln` passes.
- Trace assertions in the rewritten tests verify byte-for-byte equivalence with the old `HsmTraceBuffer` output for the same scenarios.

---

### T6.4 — Fix Fhsm.Benchmarks, Fhsm.Examples.Console, Fhsm.Demo.Visual, Fhsm.Demo.Visual.Tests

**Scope:**
- Same kernel-signature update across the remaining FastHSM out-of-solution projects.
- Note the `Fhsm.Demo.Visual.Tests` project has a misnamed csproj (`Fbt.Demo.Visual.Tests.csproj`) — leave the filename alone for now (rename is a separate concern).

**Success conditions:**
- `dotnet build` succeeds for each project.
- The `Fhsm.Examples.Console.TrafficLightExample` runs (`dotnet run -c Release`) and prints expected output.

---

## Verification Checklist

When all tasks are complete, run the following from repo root to confirm a clean final state:

1. `dotnet build IOS-IG-SimHost.sln` — green.
2. `dotnet test IOS-IG-SimHost.sln` — green.
3. `dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln` — green.
4. `dotnet test FDP/ExtDeps/FastBTree/FastBTree.sln` — green.
5. `dotnet build FDP/ExtDeps/FastHSM/FastHSM.sln` — green.
6. `dotnet test FDP/ExtDeps/FastHSM/FastHSM.sln` — green.
7. Manual smoke: launch the Editor / SimHost, load a scenario with AI entities, right-click an entity → "Toggle AI Trace Buffer" → verify the inspector shows live trace records.
8. Manual smoke: save a flight recording with tracing on, replay it, scrub the timeline, and verify trace records appear in the inspector for the corresponding historical frame.
9. Profiler check (informational): tracing enabled but `EmitToLog` off → zero managed allocations per simulation tick.
