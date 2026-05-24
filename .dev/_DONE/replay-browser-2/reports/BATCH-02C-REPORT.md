# BATCH-02C Report

**Batch:** BATCH-02C (Corrective)  
**Status:** APPROVED  
**Prerequisites:** BATCH-02 complete; 38 + 4 tests green; build clean.

---

## Changes Made

### Fix 1 -- EX-T22 (P2): Replace null-serializer fallback test with custom-translator test

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs`

**What changed:**
- Removed `EX_T22_NullSerializer_FallsBackToAutoSerializer` (tested the wrong thing).
- Added `EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto`.
- Added private nested class `FooHarnessBlackboardTranslator : IEntityScenarioTranslator`.
- Added `using System.Collections.Generic;` and `using Fdp.Toolkit.Scenario;`.

**How the translator stub works:**

`FooHarnessBlackboardTranslator` claims `HarnessVelocity` (component ID 203):
- `GetConsumedComponentsMask()` returns a `BitMask256` with bit 203 set.
- `CanTranslate()` returns `repo.HasComponent<HarnessVelocity>(entity)`.
- `Extract()` returns a `Dictionary<string, object>` with key `"HarnessVelocity"` mapped to a
  `JsonObject` containing `{"Source": "FooBlackboard", "Vx": ..., "Vy": ...}`.
- `Inject()` is a no-op (not needed for this test).

**How the assertion works:**

`RecordingExportService` uses only the `FdpAutoSerializer` portion of a `ScenarioSerializer`
(translators are not called on the export path). The test therefore asserts the translator via
`ScenarioSerializer.Serialize()` directly, which DOES call `Extract()` on registered translators.
The scenario DOM JSON produced by `serializer.Serialize(repo, header)` contains `"FooBlackboard"`,
which is verified with `Assert.Contains("FooBlackboard", dom.ToJsonString())`.

The test also passes the same serializer to `RecordingExportService` and calls `ExportToJson()` to
confirm that the serializer is compatible with the export service (the AutoSerializer portion
handles component output without error).

---

### Fix 2 -- EX-T13 (P3): Tighten frame count and time bounds assertion

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs`

**What changed:**

Old assertion (too loose):
```csharp
Assert.True(frames.Count >= 1 && frames.Count <= 3,
    $"Expected 1-3 frames but got {frames.Count}");
// ...
Assert.True(t <= 3.0 + 1e-6, $"Frame RelativeWallTimeSec {t} exceeds end time 3.0");
```

New assertion (exact count + both bounds):
```csharp
Assert.Equal(2, frames.Count);
// All emitted frames must have RelativeWallTimeSec in [1.5, 3.0]
foreach (var f in frames)
{
    double t = f!["FrameHeader"]!["RelativeWallTimeSec"]!.GetValue<double>();
    Assert.True(t >= 1.5 - 1e-6 && t <= 3.0 + 1e-6,
        $"Frame RelativeWallTimeSec {t} outside [1.5, 3.0]");
}
```

The test records 5 frames at relative times 0, 1, 2, 3, 4 seconds. With
`StartTimeSec=1.5, EndTimeSec=3.0`, the seek skips past frame 1 (t=1.0) and the export
emits frames 2 (t=2.0) and 3 (t=3.0) -- exactly 2. The lower-bound check `>= 1.5 - 1e-6`
was missing before; it is now enforced.

---

## Test Results

### `Fdp.Toolkits.Tests` -- ReplayBrowser filter

```
Total tests: 38
     Passed: 38
 Total time: 1.99 s
```

Includes:
- `EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto` -- Passed
- `EX_T13_ByTime_WindowsCorrectly` -- Passed

### `Fdp.Tools.RecordingDumper.Tests`

```
Total tests: 4
     Passed: 4
 Total time: 0.92 s
```

---

## Build Output

```
Build succeeded.
    14 Warning(s)
    0 Error(s)
```

All 14 warnings are pre-existing (xUnit analyzer hints, NU1603 NuGet resolution, CS8892
dual-entry-point); none are introduced by this batch.
