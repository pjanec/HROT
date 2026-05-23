# FIX1-BATCH-05 — Phases 8 & 9: Runtime Read-Only Inspection

## Tasks Covered
- **TASK-BT-S2-01, TASK-HS-S2-01** — Inject `EntityRepository` into debug sessions and implement `GetCurrentStateSnapshot()` to extract live ECS state into `BehaviorTreeStateSnapshot` and `HsmInstanceSnapshot`.
- **TASK-BT-S2-03** — Update `LiveBlackboardPanel.Draw()` to extract ECS parameter bytes and decode them to display actual runtime values.
- **TASK-BT-S2-05, TASK-HS-S2-05** — Implement per-frame unmanaged trace buffer polling in the debug sessions to populate timeline history records.

## Onboarding

You are working on the `IOS-IG-SimHost-FDP-2` project. This batch connects the debug sessions
to the live ECS world so the runtime overlays and inspector panes show actual data instead of
nulls and placeholders.

**Mandatory read before coding:**
- `.dev/blueprints-2/FIX1-TASK-DETAIL.md` — "ACTION PACKET 5: Phases 8 & 9 — Runtime Read-Only Inspection".
- `.dev/blueprints-2/FIX1-TASK-EXTRA-DETAILS.md` — "ACTION PACKET 5: Phases 8 & 9 — Runtime Inspection Detailed Fixes".
- `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md` — §13.4 (trace ring buffer polling), §8 (debug session contracts).
- `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md` — BTree debug session spec.
- `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md` — HSM debug session spec.
- `.dev/blueprints-2/ACCEPTANCE-CRITERIA.md` — F8-01 through F8-06, F9-01 through F9-06.
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/` — Existing BTree debug session code.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/` — Existing HSM debug session code.

## Developer Insights Section

After implementing, answer the following in your report:
1. What issues were encountered?
2. What weak points did you spot in the existing codebase?
3. What design decisions were made beyond the spec?

---

## Tasks

### TASK-BT-S2-01 & TASK-HS-S2-01: Session ECS Injection & Snapshot Generation

**Target files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-BT-S2-01 & TASK-HS-S2-01: Session ECS Injection & Snapshot Generation".

**Summary:**
1. Add `EntityRepository _repo` and `EditorSelectionStore _selection` to both session constructors (or expose an `Update(EntityRepository repo, Entity activeEntity)` method called by the editor frame loop).
2. **BTree Snapshot** — implement `GetCurrentStateSnapshot()`:
   - Get selected entity. If it has `BrainBTreeState`, read `RunningNodeIndex`, `StackPointer`, `TreeVersion`.
   - Copy `NodeIndexStack`, `LocalRegisters`, `AsyncHandles` fixed arrays into managed arrays.
   - Map `RunningNodeIndex` to visual Guid via asset's `NodeDebugMetadata` if available.
   - Return `BehaviorTreeStateSnapshot`.
3. **HSM Snapshot** — implement `GetCurrentStateSnapshot()`:
   - Get selected entity. Read `BehaviorState.BrainTier`.
   - Based on tier, read `BrainHsm64`, `BrainHsm128`, or `BrainHsm256`.
   - Cast to `InstanceHeader*`, extract `Phase`, `MicroStep`, `Generation`, `Flags`, `RngState`.
   - Extract `ActiveLeafIds`, `TimerDeadlines`, `HistorySlots`, `EventQueue` using tier-specific byte offsets.
   - Return `HsmInstanceSnapshot`.

**Acceptance criteria:** F8-05, F9-05.

**Tests required:**
- BTree: Test that `GetCurrentStateSnapshot()` returns non-null with correct `RunningNodeIndex` for an entity that has `BrainBTreeState` with `RunningNodeIndex = 7`.
- BTree: Test that it returns `null` for an entity with no `BrainBTreeState`.
- HSM: Test that snapshot extracts correct `Phase` from `BrainHsm64` for a tier-1 entity.
- HSM: Test that it returns `null` for an entity with no `BehaviorState`.

---

### TASK-BT-S2-03: Live Blackboard Values in Inspector

**Target files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/LiveBlackboardPanel.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-BT-S2-03: Live Blackboard Values in Inspector".

**Summary:**
1. Update `Draw()` to accept (or access via injected dependency) `EntityRepository` and selected `Entity`.
2. Check `if (repo.HasComponent<BrainBlackboard>(entity))`. If true, get ref to `BrainBlackboard.BehaviorParameters`.
3. Also check for `Blackboard1024`. Get ref to `Blackboard1024.Memory` if present.
4. For each schema field:
   - Determine if it lives in `BehaviorParameters` (light DTO) or `Blackboard1024` (heavy DTO).
   - Calculate `byte* fieldPtr = basePtr + field.FieldOffset`.
   - Use `Unsafe.ReadUnaligned<T>(fieldPtr)` or `MemoryMarshal.Read<T>(...)` based on `field.FieldType`.
   - Display actual value string instead of `"--"`.

**Acceptance criteria:** F8-04.

**Tests required:**
- Test that `Draw()` displays actual integer value for a field in `BrainBlackboard.BehaviorParameters`.
- Test that it shows `"--"` when entity has no `BrainBlackboard` component.
- Test that float fields are displayed with correct precision.

---

### TASK-BT-S2-05 & TASK-HS-S2-05: Trace Buffer Polling

**Target files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-BT-S2-05 & TASK-HS-S2-05: Unmanaged Trace Buffer Polling".

**Summary:**
1. Add `private ushort _lastReadPos;` to both session classes.
2. In the per-frame `Update(EntityRepository repo, Entity entity)` method:
   a. Check if entity has `BTreeTraceWorkingMemory1024` (BTree) or `HsmTraceWorkingMemory1024` (HSM).
   b. If `trace.WritePos == _lastReadPos`, return early (nothing new).
   c. Iterate from `_lastReadPos` to `trace.WritePos` by `RecordStride` (16 bytes).
   d. Handle ring-buffer wrapping at `trace.CapacityBytes` (1008).
   e. At each offset, cast pointer to `BTreeTraceRecord*` (or `TraceRecord*`), decode `OpCode`, and route to `RecordNodeExecuted`, `RecordAsyncEvent`, or `RecordTrace`.
   f. Update `_lastReadPos = trace.WritePos`.

**Acceptance criteria:** F8-02, F9-02.

**Tests required:**
- BTree: Test that calling `Update()` with an entity that has `BTreeTraceWorkingMemory1024` with 3 new records populates the session's trace history with 3 entries.
- BTree: Test that ring-buffer wrapping is handled correctly (write at position 1008 → reads from 0).
- HSM: Test equivalent trace polling.

---

## Mandatory Workflow: Test-Driven Task Progression

1. Read spec and acceptance criteria first.
2. Write/update tests alongside implementation.
3. Run tests and confirm they pass.
4. Do not mark a task complete unless tests pass.

Do not swallow exceptions silently.

---

## Build & Test Commands

```powershell
cd "d:\Work\IOS-IG-SimHost-FDP-2"
dotnet build "Hrot/Subsystems/AI/Hrot.BTree.Editor/"
dotnet build "Hrot/Subsystems/AI/Hrot.Hsm.Editor/"
dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/"
dotnet test "Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/"
```

---

## Report Format

Write your report to: `.dev/blueprints-2/reports/FIX1-BATCH-05-REPORT.md`

**Required sections:**
1. **Summary** — What was implemented.
2. **Task Status** — Per-task: Implemented / Partial / Blocked.
3. **Tests** — List of test methods added, and pass/fail status.
4. **Developer Insights**:
   - Issues encountered
   - Weak points spotted
   - Design decisions beyond spec
5. **Build Output** — Paste relevant output (last 30 lines minimum).
