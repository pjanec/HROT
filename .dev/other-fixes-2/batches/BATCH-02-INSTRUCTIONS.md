# BATCH-02: DebugMap Emitter Population & Instance State Inspection

**Batch Number:** BATCH-02  
**Tasks:** FIX2-002, FIX2-009  
**Priority:** HIGH / MEDIUM  
**Dependencies:** BATCH-01 (approved and committed)

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in strict sequence. For each task:
1. Define the **success condition** BEFORE touching any code.
2. Implement the fix.
3. Write / update tests that drive the **production path**.
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish both tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading (in order)
1. **Task details:** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-002, FIX2-009
2. **Source findings BPF-002, BPF-021:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-002, BPF-021
3. **Source finding BPF-001:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-001
4. **Debug Design Document (§8.5, §8.6):** search for `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` under `Hrot/Subsystems/Blueprints/Docs/` or `docs/blueprints/`
5. **Editor Design Document (§8.5 state inspection):** search for `Blueprint_Subsystem_Editor_Detailed_Design.md`

### Source Code Areas
- **Emitter:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs`
- **DebugMap builder/index:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/DebugMapBuilder.cs`, `DebugMapIndex.cs`, `DebugMapSerializer.cs`
- **IR annotation:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/IR/IrDebugAnnotation.cs` (or nearby)
- **IrOp probe:** search for `IrOp_DebugProbe_NodeEnter` in the compiler tree
- **Stage 7 (DebugMap assembly):** search for calls to `DebugMapBuilder.Build()` or `SetAssetName` in `CSharpEmitter.cs`
- **Debug session instance state:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` -- method `CaptureInstanceStateFromDefinition`
- **Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-02-REPORT.md`

---

## Context

`DebugMap` has a fully-built type hierarchy (`DebugMapEntry`, `DebugMapBuilder`, `DebugMapSerializer`, `DebugMapIndex`), but the CSharp emitter only calls `RecordNodeStart` / `RecordNodeEnd` / `Build()`. All the richer builder API -- `SetAssetName`, `SetGeneratedSourcePath`, `AddGraph`, `AddPin`, `AddStateLayoutField` -- have zero callers. This means every produced `DebugMap` has empty `AssetName`, `Graphs`, `Pins`, `StateLayout.Fields` at runtime. `NodeKind`/`DisplayName` are also empty because `IrDebugAnnotation` carries no such fields.

FIX2-009 (instance state inspection) is directly blocked by FIX2-002: `CaptureInstanceStateFromDefinition` needs a non-empty `StateLayout` from the DebugMap to project slot bytes into named fields.

---

## Tasks

### Task 1 -- FIX2-002: Populate DebugMap fields during emit

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-002`

**Success condition (define before coding):**
A test compiles a blueprint asset and then reads the produced `DebugMap`. The map must have:
- Non-empty `AssetName` (matches the asset's name)
- Non-empty `GeneratedSourcePath`
- At least one entry in `Graphs`
- At least one entry in `Pins`
- At least one `StateLayout.Fields` entry (for Instance-type blueprints that have local state)
- At least one node with non-empty `NodeKind`/`DisplayName`

If you remove any of the new `AddPin` / `AddGraph` / etc. calls, the corresponding assertion must fail.

**What to fix:**

1. **Add `NodeKind` and `DisplayName` to `IrDebugAnnotation`** (or to the `IrOp_DebugProbe_NodeEnter` op, whichever is the cleaner approach given existing code structure). Populate them when the probe IR op is created (in the stage that inserts debug probes).

2. **In `CSharpEmitter` (around lines 43-53, 78):**
   - Call `builder.SetAssetName(asset.AssetName)` and `builder.SetGeneratedSourcePath(virtualPath)` at emit start.
   - Call `builder.AddGraph(graphId, graphName)` when emitting each graph class.
   - Call `builder.AddPin(nodeId, pinName, pinType)` for each pin encountered during emit.
   - Populate `NodeKind`/`DisplayName` from the annotation when `RecordNodeStart` is called.

3. **`AddStateLayoutField`:** For Instance blueprints, iterate the state layout fields (from the IR or asset metadata) and call `builder.AddStateLayoutField(fieldName, fieldOffset, fieldType)` before calling `Build()`.

**Test required:**
- Test name: `DebugMap_CompiledAsset_HasNonEmptyPinsAndGraphs` (or similar)
- Must: compile a blueprint with at least one graph, one branch/logic node, and one pin. Read `compiledAsset.DebugMap`. Assert `AssetName`, `Graphs.Count > 0`, `Pins.Count > 0`, `NodeKind` is not empty for at least one node.
- Must NOT: pre-populate any DebugMap fields manually -- must go through the normal compile path.

---

### Task 2 -- FIX2-009: Implement `CaptureInstanceStateFromDefinition`

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-009`

**NOTE:** This task depends on FIX2-002 producing a non-empty `StateLayout`. Complete Task 1 first.

**Success condition (define before coding):**
A test creates an `Instance` blueprint with at least one local state variable, compiles+loads it, attaches an entity, ticks once, then calls `GetCurrentStateSnapshot(entity)`. The returned `BlueprintStateSnapshot` must have at least one `Fields` entry whose `Name` and `Value` are non-null/non-empty (not the empty stub). If the stub body is present, the test fails.

**What to fix:**
- In `BlueprintDebugSession.cs`, method `CaptureInstanceStateFromDefinition` (around line 522-528), implement slot-byte reading:
  - Accept the `BlueprintSlotEntry` / partition allocator pointer.
  - Read the `BlueprintLatentCursor` from the first 16 bytes of the slot.
  - For each field in `stateLayout.Fields`, compute the offset, read the bytes, and project to a typed value.
  - Return a populated `BlueprintStateSnapshot` with all fields filled in.
- The comment says "requires the partition allocator, not wired in here." Wire it in -- locate where the partition pointer can be accessed from the debug session (via the `EntityRepository`/`ISimulationView` reference the session already holds, or by accepting it as a parameter to the capture call).

**Test required:**
- Test name: `StateInspection_Instance_ReturnsNonEmptyFields` (or similar)
- Must: run a full compile -> load -> tick pipeline, call `GetCurrentStateSnapshot`, assert at least one named field with a non-null value.
- Must NOT: call `CaptureInstanceStateFromDefinition` directly with pre-built data.

---

## Quality Standards

**PRODUCTION PATH:** Every test must go through the compile/load/tick pipeline. Tests that call builder methods directly or manually fill data structures do NOT count.

**REGRESSION:** All 880 existing tests must still pass after both tasks are done.

---

## Developer Insights (Report Questions)

1. What obstacles did you encounter populating `StateLayout.Fields`? What data is available vs. what had to be threaded through?
2. What design decisions did you make for wiring the partition allocator into `CaptureInstanceStateFromDefinition`?
3. Did you find any additional dead-code gaps while implementing these fixes?
4. What edge cases did you discover (e.g., blueprints with no state variables, blueprints with pins of unknown types)?
5. **Suggested commit message** for this batch.
