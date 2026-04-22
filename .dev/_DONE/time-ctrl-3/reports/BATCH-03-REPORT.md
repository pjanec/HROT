# BATCH-03 Report — Corrective-01 + Translators + NetworkModule Factory

**Date:** 2026-04-02  
**FDP commit at start:** `0cea5df` (BATCH-02)  
**Tasks:** Corrective-01 · TC3-P4-T01 · TC3-P4-T02 · TC3-P4-T03

---

## 1. Task Completion Table

| Task | Description | Status |
|------|-------------|--------|
| Corrective-01 | Fix `_lastUpdateRawTicks = currentAbsTicks` → `_getTick()` in hard-snap path | ✅ Complete |
| TC3-P4-T01 | Create `MasterTimeSyncTranslator` | ✅ Complete |
| TC3-P4-T02 | Create `SlaveTimeSyncTranslator` | ✅ Complete |
| TC3-P4-T03 | Add `CreateMasterTimeSyncTranslator` / `CreateSlaveTimeSyncTranslator` factory methods to `TimeNetworkModule` | ✅ Complete |

---

## 2. Test Results

```
dotnet test Toolkits\FDP.Toolkit.Time.Tests\FDP.Toolkit.Time.Tests.csproj --verbosity minimal

Passed!  - Failed: 0, Passed: 118, Skipped: 0, Total: 118, Duration: 837 ms
```

| Test File | New Tests | Status |
|-----------|-----------|--------|
| `SlaveSyncControllerTests.cs` | 1 (`SlaveSyncController_HardSnap_DoesNotCorruptRawDelta`) | ✅ Green |
| `TimeSyncTranslatorTests.cs` | 8 (TC3-P4-T01-SC1/SC2, TC3-P4-T02-SC1/SC2/SC3, TC3-P4-T03-SC1/SC2/SC3) | ✅ Green |
| All pre-existing 109 tests | — | ✅ Green |

---

## 3. Developer Insights

### Corrective-01 — Domain mismatch bug

The bug was introduced in BATCH-02 (TC3-P3-T04) when `OnTimePulseReceived` was updated to use
`SyncedWallTicks` (= `_getTick() + _masterWallClockOffset`) for the PLL.  The hard-snap branch
then assigned the same `SyncedWallTicks` value to `_lastUpdateRawTicks`.

The problem: `UpdateContinuous` and `UpdateBarrierPending` both compute `rawDelta = _getTick() - _lastUpdateRawTicks`.  With a non-zero offset (e.g. −500M ticks) the next frame's raw delta is inflated by `|offset|` ticks — approximately 50 seconds for a 500M-tick offset — producing a catastrophically large `DeltaTime`.

The fix is a single-character-scope change: replace `currentAbsTicks` (synced domain) with `_getTick()` (raw domain) so the baseline is always in the same measurement domain as the subtrahend on the next frame.

**Regression test design note:** The test injects an offset of exactly −500,000,000 ticks by using `ticks=500_000_000` as `t4` when consuming the sync response. This makes `SyncedWallTicks = 0` while `_getTick() = 500_000_000`. After a hard snap, advancing by exactly `TicksFromSeconds(0.016)` and verifying `DeltaTime ≈ 16ms` conclusively catches the bug: before the fix `rawDelta ≈ 500,000,016` ticks (≈50 s), after the fix `rawDelta = frameTicks` (≈16ms).

### Part B — Translator pair design

Both translators follow the exact same pattern as `MasterLockstepTranslator` / `SlaveLockstepTranslator`:
- `DdsReader<T>?` / `DdsWriter<T>?` nullable fields; null for test hosts.
- `PollIngress` guards early with `if (_xxxReader is null) return;`.
- `ScanAndPublish` drains the bus unconditionally (important for `SlaveTimeSyncTranslator`), skipping the DDS write when the writer is null. This prevents bus accumulation when running without DDS.

**Key asymmetry:** `MasterTimeSyncTranslator` has no bus dependency — it reads from DDS and writes back to DDS with no intermediate bus event, keeping the master path as a pure DDS ↔ DDS echo with timestamps inserted. This is intentional: the master only responds, never initiates.

### Bus registration

`SlaveTimeSyncTranslator` does not call `_eventBus.Register<TimeSyncRequest>()` in its constructor because `SlaveSyncController` already registers all NTP message types on the shared bus. In tests that exercise `ScanAndPublish` without a `SlaveSyncController`, the test must call `bus.Register<TimeSyncRequest>()` explicitly (which the test suite does).

### FdpLog fully-qualified form

`MasterTimeSyncTranslator.PollIngress` uses the fully-qualified `FDP.Kernel.Logging.FdpLog<T>.Debug(...)` call (same as `SlaveSyncController`) to avoid a namespace ambiguity if `FDP.Kernel.Logging` is not imported at file scope.

---

## 4. Files Changed

| File | Type | Change |
|------|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` | Modified | 1-line fix: `_lastUpdateRawTicks = _getTick()` |
| `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs` | Created | Master NTP translator (ordinal 205) |
| `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs` | Created | Slave NTP translator (ordinal 206) |
| `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` | Modified | +2 factory methods with XML doc |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` | Modified | +1 Corrective-01 test |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncTranslatorTests.cs` | Created | 8 new translator/factory tests |
