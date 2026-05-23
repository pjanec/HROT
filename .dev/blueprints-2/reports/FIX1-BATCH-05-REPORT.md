# FIX1-BATCH-05 Report

## 1. Summary

Implemented Phases 8 & 9 ("Runtime Read-Only Inspection") across five tasks:

- **TASK-BT-S2-01** — Extended `BlackboardField` with a fourth positional parameter
  `FieldOffset` (int). Updated `BlackboardSchemaBuilder.Build()` to compute native struct
  offsets via `Marshal.OffsetOf` (with `try/catch` fallback to -1). No existing tests were
  broken; the schema builder tests rely on `.Kind` / `.FieldType` / `.Name` only.

- **TASK-BT-S2-03** — Implemented `BTreeDebugSession.Update(EntityRepository, Entity)`.
  On each call it:
  - Reads `BrainBTreeState` from the entity (if present) and builds a
    `BehaviorTreeStateSnapshot` with running node index, stack, local registers, and async
    handles via `Unsafe.AsPointer` pointer arithmetic on the fixed-array fields.
  - Polls `BTreeTraceWorkingMemory1024` from the entity (if present), consuming all
    unread 16-byte slots since the last call and routing `NodeEvaluated`, `WaitStarted`,
    and `WaitCompleted` opcodes into the existing `RecordNodeExecuted` / `RecordAsyncEvent`
    history. `_lastReadPos` advances ring-wrap correctly using `% PayloadBytes`.
  - `OnDetachImpl` resets `_currentSnapshot` and `_lastReadPos`.
  Required project changes: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` and a
  `<ProjectReference>` to `Fdp.Toolkits` added to `Hrot.BTree.Editor.csproj`.

- **TASK-BT-S2-05** — Wired live blackboard values into `LiveBlackboardPanel.Draw()`.
  Added `SetEntityContext(EntityRepository?, Entity)` to supply ECS context.  When a
  session is attached, ECS context is set, and `BrainBlackboard` exists on the entity,
  `ReadFieldValue` reads field bytes via `Unsafe.AsPointer` and formats them by CLR type
  (`bool`, integer primitives, float/double, `Vector2/3/4`). Fields with `FieldOffset < 0`
  or out-of-range fall through to `"--"`. The offline path is unchanged.

- **TASK-HS-S2-01** — Implemented `HsmDebugSession.Update(EntityRepository, Entity)`.
  On each call it:
  - Checks for `BrainHsm64` then `BrainHsm128` and builds an `HsmInstanceSnapshot` from
    `InstanceHeader` fields (Phase, MicroStep, Flags, RngState, Generation). Active leaf
    IDs, event queue, timers, and history slots are passed as `Array.Empty<T>()` pending
    kernel wiring.
  - Polls `HsmTraceWorkingMemory1024` and routes `StateEnter`, `StateExit`, and
    `Transition` headers into the existing `RecordTrace` history. Uses
    `TraceRecordHeader*` to read the opcode/timestamp, and `TraceTransition*` for the
    `TriggerEventId` field in the Transition case.
  - `OnDetachImpl` resets `_currentSnapshot` and `_lastReadPos`.
  Required project changes: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` and
  `<ProjectReference>` entries for `Fdp.Core` and `Fdp.Toolkits` added to
  `Hrot.Hsm.Editor.csproj`.

- **TASK-HS-S2-05** — Added five new ECS-wired test methods to `BTreeDebugSessionTests`
  and four new test methods to `HsmDebugSessionTests`. All tests use a helper
  `CreateWorld()` that registers the required component types before creating entities.
  The BTree tests use `BTreeTraceWorkingMemory1024.WriteNodeEvaluated()` directly. The HSM
  test writes `TraceRecordHeader` structs manually via `Unsafe.AsPointer` (since
  `HsmTraceWorkingMemory1024` has no write helpers).

---

## 2. Task Status

| Task | Status |
|------|--------|
| TASK-BT-S2-01 | Implemented |
| TASK-BT-S2-03 | Implemented |
| TASK-BT-S2-05 | Implemented |
| TASK-HS-S2-01 | Implemented |
| TASK-HS-S2-05 | Implemented |

---

## 3. Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Hrot.BTree.Editor.csproj` | Added `AllowUnsafeBlocks`, `Fdp.Toolkits` reference |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj` | Added `AllowUnsafeBlocks`, `Fdp.Core` + `Fdp.Toolkits` references |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj` | Added `AllowUnsafeBlocks`, `Fdp.Toolkits` reference |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj` | Added `AllowUnsafeBlocks`, `Fdp.Toolkits` reference |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/BlackboardField.cs` | Added `FieldOffset` positional parameter |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/BlackboardSchemaBuilder.cs` | Added `Marshal.OffsetOf` offset capture |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` | Implemented `Update()`, wired `GetCurrentStateSnapshot()`, reset in `OnDetachImpl` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs` | Implemented `Update()`, wired `GetCurrentStateSnapshot()`, reset in `OnDetachImpl` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/LiveBlackboardPanel.cs` | Added `SetEntityContext`, live value reading in `Draw()`, `ReadFieldValue` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Debug/BTreeDebugSessionTests.cs` | Added 5 new ECS Update tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Debug/HsmDebugSessionTests.cs` | Added 4 new ECS Update tests |

---

## 4. Build & Test Results

```
Hrot.BTree.Editor:       Build succeeded. 0 Warning(s), 0 Error(s)
Hrot.Hsm.Editor:         Build succeeded. 0 Warning(s), 0 Error(s)
Hrot.BTree.Editor.Tests: Passed! Failed: 0, Passed: 145, Total: 145
Hrot.Hsm.Editor.Tests:   Passed! Failed: 0, Passed: 150, Total: 150
```

---

## 5. Design Decisions

- **`Unsafe.AsPointer` over `fixed` statement**: ECS component memory lives in unmanaged
  storage; `GetComponentRO<T>` returns a `ref readonly` backed by that storage. Using
  `Unsafe.AsRef(in x)` to obtain a mutable `ref` then `Unsafe.AsPointer(ref x)` to get
  the raw pointer is consistent with existing integration tests in the codebase
  (`HsmBehaviorIntegrationTests`).

- **`FieldOffset = -1` fallback**: `Marshal.OffsetOf` can throw for types that cannot be
  marshaled (e.g., auto-layout managed types). The `try/catch` ensures
  `BlackboardSchemaBuilder` remains robust for arbitrary struct types, and the
  `LiveBlackboardPanel` skips reading bytes when `FieldOffset < 0`.

- **`TraceRecordHeader*` in HSM polling**: HSM trace records use separate typed structs
  (`TraceStateChange`, `TraceTransition`, etc.) all prefixed with an 8-byte
  `TraceRecordHeader`. The opcode dispatch reads only the header; `TraceTransition*` is
  used when the `TriggerEventId` payload is needed.

- **`Array.Empty<T>()` for HSM snapshot lists**: Active leaf IDs, event queue entries,
  timer slots, and history slots require decoding additional fixed-array fields in
  `HsmInstance64/128` that are planned for a later kernel wiring slice. Passing empty
  arrays keeps the snapshot valid and non-null without overreaching.
