# BATCH-01: Phases 1, 2, 3 + DoctrineIngressSystem HSM reset

**Batch Number:** BATCH-01
**Tasks:** BHU-001, BHU-002, BHU-003, BHU-004, BHU-005, BHU-006, BHU-007, BHU-008, BHU-009, BHU-010, BHU-015, BHU-016
**Phase:** 1 (Unified Hot Reload), 2 (HSM Terminal State), 3 (Cognitive Interrupt Decoupling) + BHU-016
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/btree-hsm-unif/TASK-DETAIL.md` — full specs for BHU-001 through BHU-016
2. **Design Document:** `.dev/btree-hsm-unif/DESIGN.md` — architecture for all five phases
3. **Onboarding:** `.dev/btree-hsm-unif/ONBOARDING.md` — codebase map

### Key source files you will touch

| File | Task |
|------|------|
| `Hrot/Subsystems/Hrot.AI.Doctrines/Hrot.AI.Doctrines.csproj` | BHU-001 |
| `Hrot/Subsystems/Hrot.AI.Doctrines/Brains/CgfHsmNodes.cs` (NEW) | BHU-001 |
| `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` | BHU-002 |
| `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` (NEW) | BHU-003 |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | BHU-004 |
| `Hrot/Subsystems/Hrot.AI.Doctrines/AiDoctrineFactory.cs` | BHU-004 |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateNode.cs` | BHU-005 |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` | BHU-005 |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmFlattener.cs` | BHU-005 |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs` | BHU-006 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs` | BHU-007, BHU-009 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveInterruptSystem.cs` (NEW) | BHU-008 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveCleanupSystem.cs` (NEW) | BHU-015 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/CognitiveRuntimeModule.cs` | BHU-010 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmDamageBridgeSystem.cs` (DELETE) | BHU-010 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/DoctrineIngressSystem.cs` | BHU-016 |

### Test projects you will run

- `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj`
- `dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj`
- `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj`
- `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` (final build check)

### Report Submission

Submit your report to: `.dev/btree-hsm-unif/reports/BATCH-01-REPORT.md`

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence. Do NOT move to the next task until ALL tests for the current one pass.**

1. BHU-001 → build passes + test pass
2. BHU-002 → build passes + test pass
3. BHU-003 → build passes + test pass
4. BHU-004 → build passes + test pass
5. BHU-005 → build passes + test pass
6. BHU-006 → build passes + test pass
7. BHU-007 → build passes + test pass
8. BHU-008 → build passes + test pass
9. BHU-009 → build passes + test pass
10. BHU-010 + BHU-015 → build passes + test pass (do together, they are coupled)
11. BHU-016 → build passes + test pass
12. Final: `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` — zero errors

Do not stop to ask permission for obvious actions (running tests, fixing compile errors, iterating on failures). Complete everything and write the report only when ALL tests pass.

---

## Context

This batch implements the core BTree+HSM unification: the unified hot-reload coordinator, full HSM terminal state routing (so HSM doctrines emit `DoctrineFinishedEvent` just like BTree), cognitive interrupt decoupling (replacing `HsmDamageBridgeSystem` with a paradigm-agnostic blackboard byte approach), and the defensive `DoctrineIngressSystem` HSM reset.

After this batch:
- HSM doctrines can hot-reload through the same path as BTree doctrines.
- An HSM that enters a `.Final()` state publishes `DoctrineFinishedEvent` exactly once.
- `CanMove→false` edge triggers byte 126 in the shared blackboard, consumed by both HSM and BTree tiers in the same frame, then cleared.

---

## Tasks

### BHU-001 — Add Fhsm references to `Hrot.AI.Doctrines.csproj`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-001".

Key points:
- Add three `ProjectReference` entries (Fhsm.Kernel, Fhsm.Compiler, Fhsm.SourceGen as Analyzer).
- Create `Hrot/Subsystems/Hrot.AI.Doctrines/Brains/CgfHsmNodes.cs` with one stub `[HsmAction]` static method (empty body, correct unmanaged signature: `static unsafe void StubIdle(void* instance, void* ctx, HsmCommandWriter* writer)`). Namespace must be `Hrot.AI.Doctrines`.
- After build, confirm `obj/` contains `HsmActionRegistrar.g.cs`.

**Tests required:**
- `dotnet build Hrot/Subsystems/Hrot.AI.Doctrines/Hrot.AI.Doctrines.csproj` — zero errors.
- All existing BTree tests for `Hrot.AI.Doctrines` continue to pass (run via solution build).

---

### BHU-002 — Add `HsmActionDispatcher.ClearAll()` via SourceGen

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-002".

In `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`, method `GenerateKernelDispatcher()`, after the two `RegisterAction`/`RegisterGuard` lines, append:

```csharp
sb.AppendLine("        public static void ClearAll()");
sb.AppendLine("        {");
sb.AppendLine("            ActionTable.Clear();");
sb.AppendLine("            GuardTable.Clear();");
sb.AppendLine("        }");
```

**Tests required** (add to `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/`):
- After `HsmActionDispatcher.ClearAll()`, calling `EvaluateGuard` for a previously registered ID returns `true` (default pass-through, table empty).
- `RegisterGuard` → `EvaluateGuard(id)` dispatches correctly BEFORE `ClearAll`.

---

### BHU-003 — Build `AiHotReloadCoordinator`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-003". Read the full spec; it is detailed.

Create `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` in namespace `Hrot.Editor`.

Critical design constraints (MUST follow exactly):
- Background thread: loads new ALC, reflects `AiDoctrineFactory.BuildRegistrationAction`, builds staging registry, enqueues `(stagingRegistry, newAlc, oldAlc)` via `ConcurrentQueue`. Does NOT touch `HsmActionDispatcher` here.
- Main-thread `DrainPendingCallbacks()`: **step order is mandatory**:
  1. `HsmActionDispatcher.ClearAll()`
  2. Reflect `Hrot.AI.Doctrines.Generated.HsmActionRegistrar.RegisterAll()` from newAlc assembly and invoke.
  3. Apply staging registry to live `DoctrineRegistry`.
  4. For each HSM doctrine in staging: iterate `world.GetComponentTable<BrainHsmNN>().GetSpan(chunkIndex)` over all chunks (use `world.GetComponentTable<BrainHsm64/128>().GetChunkTable().TotalChunks`) and call `HotReloadManager.TryReload()` per-chunk span.
  5. Store `PreviousAlcRef = new WeakReference<AssemblyLoadContext>(oldAlc)` then null out old ALC field.
- `TriggerInitialLoad()` method.
- `IDisposable` — dispose `FileSystemWatcher`, debounce timer.
- `event Action<string>? OnReloadCompleted` and `event Action<string, Exception>? OnReloadFailed`.
- Expose `WeakReference<AssemblyLoadContext>? PreviousAlcRef` (internal) for test verification.

**Tests required** (add to `Hrot/Subsystems/Hrot.Editor.Tests/`):
- After two simulated reload cycles and `GC.Collect()` + `GC.WaitForPendingFinalizers()`, `PreviousAlcRef.TryGetTarget` returns `false` (old ALC unloaded).
- Verify `ClearAll()` is called BEFORE `RegisterAll()` in the drain sequence (instrument via a subclass or mock).

---

### BHU-004 — Wire `AiHotReloadCoordinator` into `EditorSubsystem`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-004".

Two files:

**`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`:**
- Replace `FbtAssemblyHotReloader _aiHotReloader` field with `AiHotReloadCoordinator _aiCoordinator`.
- Replace the `FbtAssemblyHotReloader` constructor call (around line 367) with `AiHotReloadCoordinator` constructor.
- Replace `_aiHotReloader.DrainPendingCallbacks()` (around line 730) with `_aiCoordinator.DrainPendingCallbacks()`.
- Wire `OnReloadCompleted`/`OnReloadFailed` events to existing log source.
- Remove `_pendingDoctrineApply` field and the `Interlocked.Exchange` staging lambda (now inside coordinator).
- Keep `_aiHotReloader.TriggerInitialLoad()` call as `_aiCoordinator.TriggerInitialLoad()`.
- Dispose in `Shutdown()`.

**`Hrot/Subsystems/Hrot.AI.Doctrines/AiDoctrineFactory.cs`:**
- Extend `BuildRegistrationAction(...)` returned lambda to build a real `HsmDefinitionBlob` for `Idle_HSM` using `HsmBuilder` + `HsmCompiler.Compile()`:
  - Single state `"Idle"` marked `.Initial()` (no transitions, no final state — it's a steady-state idle).
  - Register via `registry.Register(DoctrineIds.Idle_HSM, "Idle_HSM", new DoctrineDefinition { Name="Idle_HSM", BrainTier=BehaviorConstants.BrainTierHsm, HsmDefinition=blob })`.

**Tests required:**
- `dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj` — zero errors.
- All existing `Hrot/Subsystems/Hrot.Editor.Tests/` tests pass.

---

### BHU-005 — Implement `IsFinal` in `Fhsm.Compiler`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-005".

Three files — exact changes:

1. `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateNode.cs`: add `public bool IsFinal { get; set; }` after the `IsParallel` property.
2. `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` (`StateBuilder` class): add `public StateBuilder Final() { _state.IsFinal = true; return this; }`.
3. `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmFlattener.cs` (`BuildStateFlags` method, around line 195): add `if (node.IsFinal) flags |= StateFlags.IsFinal;` immediately after the `IsParallel` line.

**Tests required** (add to `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/`):
- Build a graph with `.State("Done").Final()`, compile, assert `StateFlags.IsFinal` is set for the "Done" state in the resulting blob.
- Regression: graph without any `.Final()` state — assert no state has `StateFlags.IsFinal`.

---

### BHU-006 — `StateFlags.IsFinal` → `InstanceFlags.Terminated` in `HsmKernelCore`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-006".

File: `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs`.

Two changes:

1. In the state-entry path (loop over `EntryPath`, around lines 299-312), after executing `OnEntryActionId` for each entered state, add the final-state check for that state. Apply for BOTH the 64-byte and 128-byte code paths (lines ~304 and ~703 if they exist as separate copies):

```csharp
if ((state.Flags & StateFlags.IsFinal) != 0)
{
    header->Flags |= InstanceFlags.Terminated;
}
```

`header` is `(InstanceHeader*)instancePtr` — already available in context. Use the pointer that is already in scope.

2. At the top of the event-dispatch method (wherever `ProcessEventPhase`/`ProcessRTCPhase` begins its work, before evaluating any transitions), add an early-return guard:

```csharp
if ((header->Flags & InstanceFlags.Terminated) != 0)
    return;
```

**Tests required** (add to `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/`):
- Drive a 2-state machine to a final state; assert `(InstanceHeader.Flags & InstanceFlags.Terminated) != 0` after `HsmKernel.Update`.
- Call `HsmKernel.Update` a second time on the terminated instance; assert no transitions fire (stays in final state, no crash).

---

### BHU-007 — `HsmTickSystem<T>`: detect Terminated + publish `DoctrineFinishedEvent`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-007". Read all 6 steps in the spec carefully.

File: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`.

Add three fields:
```csharp
private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();
private readonly HashSet<int>          _seenThisFrame                  = new();
private readonly List<int>             _staleKeys                      = new();
```

At top of entity loop body: `_seenThisFrame.Add(entity.Index);`

Before the `_seenThisFrame.Clear()` which should be at the very top of Execute (outside the loop), clear it there.

After `HsmKernel.Update(...)`:

```csharp
ref var hdr = ref Unsafe.As<T, InstanceHeader>(ref component);
if ((hdr.Flags & InstanceFlags.Terminated) != 0)
{
    int  entityIdx  = entity.Index;
    uint instanceId = doctrine.InstanceId;
    if (!_publishedTerminalForInstanceId.TryGetValue(entityIdx, out uint prev)
        || prev != instanceId)
    {
        _publishedTerminalForInstanceId[entityIdx] = instanceId;
        repo.Bus.Publish(new DoctrineFinishedEvent { Entity = entity });
        // Terminal latch fix: clear flag so new doctrine doesn't fire spurious event
        hdr.Flags &= (InstanceFlags)(~(byte)InstanceFlags.Terminated);
        hdr.Phase  = InstancePhase.Idle;
    }
}
```

After the entity loop, add stale-key pruning (mirrors `BTreeTickSystem`):
```csharp
_staleKeys.Clear();
foreach (var key in _publishedTerminalForInstanceId.Keys)
    if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
foreach (var key in _staleKeys) _publishedTerminalForInstanceId.Remove(key);
```

The `_eventBus` / `repo.Bus` pattern: `BTreeTickSystem` uses `repo.Bus.Publish` directly inside the Execute method, passing `repo` which comes from the `ISimulationView view` cast. Follow the exact same pattern — do not add a constructor parameter for the bus.

**Tests required** (add to `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/`):
- Entity with `BrainHsm64` advances to final state: assert exactly one `DoctrineFinishedEvent` published.
- Second tick on same entity (Terminated cleared): assert no second event.
- Assign new doctrine (InstanceId bumped): assert a new event fires for the new doctrine's terminal.
- Destroyed entity: `_publishedTerminalForInstanceId` no longer contains that key after one tick without the entity.

---

### BHU-008 — Create `CognitiveInterruptSystem`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-008".

New file: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveInterruptSystem.cs`
Namespace: `Fdp.Toolkit.Behavior.Systems`

Key constants (must be exactly these names and values):
```csharp
internal const int InterruptRegister_MobilityLost = 126;
```

Query: `BrainBlackboard`, `ActorCapabilityState`, `PreviousCapabilities`.

Edge-triggered logic (see TASK-DETAIL.md BHU-008 for the exact code). Byte 126 is set to 1 when `CanMove` transitions from set to clear. Previous capabilities are updated at end of each iteration. Do NOT clear byte 126 here.

**Tests required** (add to `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/`):
- `CanMove` lost → byte 126 == 1 after one `Execute`.
- Entity stays `!CanMove` on second frame → byte 126 not set again (edge, not level).
- Entity always `CanMove` → byte 126 stays 0.

---

### BHU-009 — `HsmTickSystem<T>`: inject interrupt events

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-009".

File: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs` (same file as BHU-007).

Before `HsmKernel.Update(...)` in the per-entity loop, add:

```csharp
if (repo.HasComponent<BrainBlackboard>(entity))
{
    ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
    unsafe
    {
        if (bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] == 1)
            HsmEventQueue.TryEnqueue(ref component, BehaviorConstants.EventId_MobilityLost);
    }
}
```

Do NOT clear byte 126 here. `CognitiveCleanupSystem` owns that.

**Tests required:**
- `bb.Memory[126] = 1` → HSM receives `EventId_MobilityLost`; byte 126 is still 1 after tick.
- `bb.Memory[126] = 0` → no event injected.

---

### BHU-010 — Update `CognitiveRuntimeModule` + BHU-015 `CognitiveCleanupSystem`

Do both together.

**BHU-015 first** — new file `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveCleanupSystem.cs`:

```csharp
// Namespace: Fdp.Toolkit.Behavior.Systems
[UpdateInPhase(SystemPhase.Simulation)]
internal sealed class CognitiveCleanupSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        var q = repo.Query().With<BrainBlackboard>().Build();
        foreach (var entity in q)
        {
            ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
            unsafe
            {
                bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] = 0;
                bb.Memory[127] = 0;
            }
        }
    }
}
```

**BHU-010** — `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/CognitiveRuntimeModule.cs`:

Replace `HsmDamageBridgeSystem` registration with `CognitiveInterruptSystem`. Add `CognitiveCleanupSystem` as the LAST registered system. Final order must be:
1. `ChannelArbitrationSystem`
2. `CognitiveInterruptSystem`
3. `BTreeTickSystem`
4. `HsmTickSystem<BrainHsm128>`
5. `HsmTickSystem<BrainHsm64>`
6. `CognitiveCleanupSystem`

Then **delete** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmDamageBridgeSystem.cs`.

Update `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Modules/CognitiveRuntimeModuleTests.cs` to assert the new 6-system order (no `HsmDamageBridgeSystem`).

**Tests required:**
- `CognitiveRuntimeModuleTests`: 6 systems, correct types, correct positions.
- `CognitiveCleanupSystem` unit test: set byte 126 = 1, run system, assert byte == 0.
- No reference to `HsmDamageBridgeSystem` anywhere in solution after delete.

---

### BHU-016 — `DoctrineIngressSystem`: reset `BrainHsm64`/`BrainHsm128` on HSM doctrine assignment

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-016".

File: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/DoctrineIngressSystem.cs`

In the `AssignDoctrineEvent` handler, after the BTree reset (`btState.State = default`), add HSM reset for both sizes. Use `Unsafe.As` to get the `InstanceHeader*` from the component ref and manually apply the same reset that `HotReloadManager.HardReset` performs:

```csharp
// Reset HSM instance if present
if (def.BrainTier == BehaviorConstants.BrainTierHsm)
{
    if (repo.HasComponent<BrainHsm64>(evt.Entity))
    {
        ref var hsm = ref repo.GetComponentRW<BrainHsm64>(evt.Entity);
        unsafe
        {
            fixed (BrainHsm64* p = &hsm)
            {
                InstanceHeader* hdr = (InstanceHeader*)p;
                hdr->Flags &= (InstanceFlags)(~(byte)InstanceFlags.Terminated);
                hdr->Phase  = InstancePhase.Idle;
                hdr->QueueHead = 0;
                hdr->ActiveTail = 0;
                hdr->DeferredTail = 0;
                hdr->MicroStep = 0;
                // Reset active leaf IDs
                HsmInstance64* inst = (HsmInstance64*)p;
                inst->ActiveLeafIds[0] = 0xFFFF;
                inst->ActiveLeafIds[1] = 0xFFFF;
                inst->EventCount = 0;
            }
        }
    }
    if (repo.HasComponent<BrainHsm128>(evt.Entity))
    {
        // Analogous reset for BrainHsm128 — 4 leaf IDs, 4 timers, 8 history slots
        ref var hsm = ref repo.GetComponentRW<BrainHsm128>(evt.Entity);
        unsafe
        {
            fixed (BrainHsm128* p = &hsm)
            {
                InstanceHeader* hdr = (InstanceHeader*)p;
                hdr->Flags &= (InstanceFlags)(~(byte)InstanceFlags.Terminated);
                hdr->Phase  = InstancePhase.Idle;
                hdr->QueueHead = 0;
                hdr->ActiveTail = 0;
                hdr->DeferredTail = 0;
                hdr->MicroStep = 0;
                HsmInstance128* inst = (HsmInstance128*)p;
                for (int i = 0; i < 4; i++) inst->ActiveLeafIds[i] = 0xFFFF;
                inst->EventCount = 0;
                inst->InterruptSlotUsed = 0;
            }
        }
    }
}
```

Also apply the same reset in the `AssignDoctrineHashEvent` handler (same pattern, look at existing handler around line 155 of `DoctrineIngressSystem.cs`).

**Tests required** (add to `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/`):
- Assign doctrine A (HSM, reaches final state → `Terminated` set). Then assign doctrine B. Assert `Terminated` is cleared and `Phase == InstancePhase.Idle`.
- Assign two consecutive HSM doctrines. Assert `ActiveLeafIds[0] == 0xFFFF` after second assignment.

---

## Quality Standards

**Test quality expectations:**
- Tests must verify ACTUAL runtime behavior (flag values, event counts, byte values), not just that code compiles.
- Do NOT write tests that only check `Assert.NotNull(newObject)`.
- For source generator tests: verify the generated code RUNS correctly, not just that it contains a substring.

**No stopping mid-batch.** If a test fails, fix the root cause. If a build breaks, fix it. Do not ask permission to iterate.

---

## Success Criteria

This batch is DONE when:
- [ ] BHU-001: `Hrot.AI.Doctrines` builds with Fhsm references; `HsmActionRegistrar.g.cs` generated
- [ ] BHU-002: `HsmActionDispatcher` has `ClearAll()`; test proves it empties tables
- [ ] BHU-003: `AiHotReloadCoordinator` built; ALC unload test passes
- [ ] BHU-004: `EditorSubsystem` uses coordinator; `Hrot.Editor.Tests` all pass
- [ ] BHU-005: `IsFinal` in compiler; test proves `StateFlags.IsFinal` emitted
- [ ] BHU-006: `Terminated` set on final state entry; second `Update` is no-op
- [ ] BHU-007: `DoctrineFinishedEvent` published once; dedup + latch clear confirmed
- [ ] BHU-008: `CognitiveInterruptSystem` edge-triggers byte 126
- [ ] BHU-009: `HsmTickSystem` reads byte 126 → injects event; does NOT clear it
- [ ] BHU-010 + BHU-015: 6-system order confirmed; `HsmDamageBridgeSystem` deleted
- [ ] BHU-016: HSM state reset on doctrine reassignment; `Terminated` cleared
- [ ] `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` — zero `error CS` lines

---

## Reference Materials

- **Task Specs:** `.dev/btree-hsm-unif/TASK-DETAIL.md`
- **Design:** `.dev/btree-hsm-unif/DESIGN.md`
- **BTreeTickSystem (dedup pattern to mirror):** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs`
- **HotReloadManager (reset helpers):** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HotReloadManager.cs`
- **FbtAssemblyHotReloader (ALC pattern to adapt):** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs`
- **HsmInstance64/128 layouts:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/HsmInstance64.cs`, `HsmInstance128.cs`
- **InstanceHeader layout:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/InstanceHeader.cs`
- **Enums (StateFlags, InstanceFlags, InstancePhase):** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/Enums.cs`
- **TestWorldFactory:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/TestWorldFactory.cs`
- **DoctrineRegistry:** `FDP/Toolkits/Fdp.Toolkits/Behavior/DoctrineRegistry.cs`
- **BehaviorConstants (EventId_MobilityLost = 1):** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorConstants.cs`
