# BATCH-02C Instructions -- Corrective Batch for EX-T22 and EX-T13

**Batch:** BATCH-02C (Corrective)
**Based on review:** `.dev/replay-browser-2/reviews/BATCH-02-REVIEW.md`
**Scope:** Two test fixes only. No production code changes.
**Prerequisites:** BATCH-02 is complete. All 38+4 tests are green. Build is clean.

---

## Context

Read DESIGN.md §3.8 (EX-T22, EX-T13 rows) for what these tests must do.
The full test file is at: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs`
The `IEntityScenarioTranslator` interface is at: `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs`

---

## Fix 1 -- EX-T22 (P2): Replace with proper custom-translator test

**Problem:** The current `EX_T22_NullSerializer_FallsBackToAutoSerializer` tests null-serializer
fallback. DESIGN.md §3.8 EX-T22 requires: "Custom `IEntityScenarioTranslator` (a stub
`FooBlackboardTranslator`) is honored; its projected DTO appears under `Payload`."

**What to implement:**

1. Inside `RecordingExportServiceTests.cs`, add a private/file-private sealed class
   `FooHarnessBlackboardTranslator` that implements `IEntityScenarioTranslator`. It must:

   a. Claim `HarnessVelocity` (type ID 203, already registered by the harness at component ID 203).
      Call `GetConsumedComponentsMask` to return a `BitMask256` with bit 203 set.

   b. Implement `Extract(EntityRepository repo, Entity entity, JsonSerializerOptions opts)`:
      - Read `HarnessVelocity` from the repo (via `repo.GetComponent<HarnessVelocity>(entity)`).
      - Return `JsonSerializer.SerializeToNode(new { Source = "FooBlackboard", Vx = comp.Vx, Vy = comp.Vy }, opts)`.

   c. Implement `Inject(EntityRepository repo, Entity entity, JsonElement dto, ...)`: no-op (not tested here).

2. Build a `ScenarioSerializer` with this translator registered. To do this, look at how
   `ScenarioSerializer` is constructed in the existing codebase (e.g., `HrotScenarioSerializerFactory`,
   or existing tests). Alternatively, check if `ScenarioSerializer` has a constructor that accepts
   an `IReadOnlyList<IEntityScenarioTranslator>` or similar. Explore
   `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` before writing code.

3. Pass this serializer to `RecordingExportService(serializer: theSerializer)` (or however the
   constructor accepts it -- inspect the actual ctor signature in production code).

4. Run `ExportToJson` on a recording that includes at least one entity with `HarnessVelocity`.

5. Assert that the resulting JSON contains `"FooBlackboard"` (the marker from the DTO's `Source`
   field) somewhere in the component payload for `HarnessVelocity`.

6. **Replace** (not add) the existing `EX_T22_NullSerializer_FallsBackToAutoSerializer` method
   with the new `EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto` method.

**Note on naming:** The design says `FooBlackboardTranslator` but since we are in a harness context
where the existing components are `HarnessVelocity`/`HarnessPosition`, naming it
`FooHarnessBlackboardTranslator` claiming `HarnessVelocity` is acceptable.

---

## Fix 2 -- EX-T13 (P3): Tighten frame count assertion

**Problem:** `Assert.True(frames.Count >= 1 && frames.Count <= 3, ...)` is too loose.

**What to fix:**

In `EX_T13_ByTime_WindowsCorrectly`, the test's own comment says:
"StartTimeSec=1.5 -> seek past frame 1 (t=1.0), should start at frame 2 (t=2.0); EndTimeSec=3.0
-> include frame 2 (t=2.0) and frame 3 (t=3.0)"

Change:
```csharp
Assert.True(frames.Count >= 1 && frames.Count <= 3,
    $"Expected 1-3 frames but got {frames.Count}");
```
To:
```csharp
Assert.Equal(2, frames.Count);
```

Also keep (or strengthen) the per-frame time bounds assertion:
```csharp
foreach (var f in frames)
{
    double t = f!["FrameHeader"]!["RelativeWallTimeSec"]!.GetValue<double>();
    Assert.True(t >= 1.5 - 1e-6 && t <= 3.0 + 1e-6,
        $"Frame RelativeWallTimeSec {t} outside [1.5, 3.0]");
}
```

---

## Acceptance Gate

After both fixes, run:

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "Category=ReplayBrowser" --no-build
dotnet test FDP/Tools/Fdp.Tools.RecordingDumper.Tests/Fdp.Tools.RecordingDumper.Tests.csproj --no-build
dotnet build FDP/FDP.sln
```

Expected:
- 38 or 39 tests in Fdp.Toolkits.Tests (39 if EX-T22 tests are split; 38 if 1-for-1 replacement)
- 4 tests in Fdp.Tools.RecordingDumper.Tests
- 0 build errors

---

## Report

Write report to `.dev/replay-browser-2/reports/BATCH-02C-REPORT.md`.

Include:
- What the translator stub does and how the assertion works
- The exact count change in EX-T13 and the new per-frame assertion
- Test output confirming all pass
- Build output
