# BATCH-01 Report

## Completion Status

**Completed** — All three tasks implemented successfully.

| Task | Status |
|---|---|
| TCU-M001 — Fix Network Wire DTOs | ✅ Done |
| TCU-M002 — Introduce Local Domain Message Types | ✅ Done |
| TCU-T005 — Unit Tests: DTO Round-Trip and Domain Events | ✅ Done |

---

## Test Results

```
Passed!  - Failed:     0, Passed:    87, Skipped:     1, Total:    88, Duration: 1 s
```

All 87 tests pass. The 1 skipped test (`LockstepIntegrationTests.MasterSlave_Lockstep_WaitsForSlowPeer`) was already skipped before this batch — no regression.

New tests added in `TimeMessagesTests.cs`: **9 tests** (exceeds the required 6).

---

## Build Status

```
FDP.Toolkit.Time → bin\Debug\net8.0\FDP.Toolkit.Time.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

The only warning in the full `FDP.sln` build is a pre-existing XML doc warning in `CycloneDDS.Schema` (third-party dependency, unrelated to this batch).

---

## Files Changed

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | All `{ get; set; }` properties converted to plain fields across all 6 structs |
| `FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs` | **New file** — `AdvanceFrameIntent` and `FrameStepCompletedEvent` structs |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeMessagesTests.cs` | **New file** — 9 unit tests |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/FutureBarrierTests.cs` | Fixed regression: `GetProperty` → `GetField` for `BarrierWallTicks` |

---

## Developer Insights

### Q1: Issues Encountered

**Issue 1 — FdpEventBus requires `[EventId]` on `Publish<T>`**

`FdpEventBus.Publish<T>()` resolves type IDs via `EventType<T>.Id` which mandates an `[EventId]`
attribute. The spec forbids any attributes on domain types. Resolution: domain event tests use
`PublishManaged<T>()` / `ConsumeManaged<T>()`, which identifies types by a hash of the full type
name and imposes no attribute constraint. This is the correct path for in-process-only types.

**Issue 2 — Pre-existing reflection test broke after property migration**

`FutureBarrierTests.SwitchTimeModeEvent_FieldIsBarrierWallTicks_NotFrameCounter` used
`GetProperty("BarrierWallTicks")` which returned `null` after converting to a plain field. Fixed by
switching to `GetField("BarrierWallTicks")` / `.FieldType` and additionally asserting no
`BarrierFrame` *field* exists alongside the existing property check.

**Issue 3 — Transient file-lock on DDS codegen during first `dotnet build FDP.sln`**

MSB3501 appeared once on `CycloneDDS.CodeGen.csproj.FileListAbsolute.txt`. Retry cleared it;
the root cause is another process (IDE background build) holding the file. Not related to the
changes in this batch.

### Q2: Weak Points Spotted

- The `[EventId]` enforcement in `EventTypeRegistry` is all-or-nothing: there is no opt-out path
  for legitimate in-process types that never touch DDS or MessagePack. The managed bus path
  (`PublishManaged`/`ConsumeManaged`) is a viable workaround but the distinction is not documented
  anywhere at the call site.
- `FutureBarrierTests.SwitchTimeModeEvent_FieldIsBarrierWallTicks_NotFrameCounter` used
  `GetProperty` to test a structural invariant, making it brittle to the properties-vs-fields
  distinction. Structural tests should use `GetMember` or test both.
- `FrameOrderDescriptor` now has **5 fields** (`FrameID`, `FixedDelta`, `SequenceID`, `TimeScale`,
  `TargetSimTime`), but the spec table says "add `TargetSimTime` at `[Key(3)]`" while `TimeScale`
  is already at `[Key(3)]` and `TargetSimTime` is at `[Key(4)]`. This discrepancy suggests the
  spec was written before `TimeScale` was added to the struct. The constraint "do NOT renumber
  existing fields" takes precedence; `TargetSimTime` stays at `[Key(4)]`/`[DdsId(4)]`.

### Q3: Design Decisions Made Beyond the Spec

- Converted `SetTimeScaleDescriptor` (not listed in the change table) to a plain field as well,
  since the success criterion states "all wire DTO structs use plain fields" and this struct is
  in the same scope/file.
- Added 3 extra reflection tests (`FrameAckDescriptor_PlainFields_NoCsharpProperties`,
  `TimePulseDescriptor_PlainFields_NoCsharpProperties`,
  `SwitchTimeModeEvent_PlainFields_NoCsharpProperties`) beyond the required 6 to guard the
  property-free invariant across every converted struct.

### Q4: Edge Cases Discovered

- `ConsumeManaged<T>` returns `IReadOnlyList<T>`. The test calls `.ToArray()` on it — but since
  the return type already exposes indexers via `IReadOnlyList`, I indexed directly without
  `.ToArray()` to keep the code clean.
- `AdvanceFrameIntent.FixedDelta` is `float`. The assertion `Assert.Equal(0.016f, events[0].FixedDelta)`
  uses a float literal to avoid false failures from float→double precision loss.

### Q5: Suggested Commit Message

```
feat(TCU-M001/M002/T005): convert wire DTOs to plain fields, add domain events and tests
```
