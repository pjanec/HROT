# BATCH-09 Report

**Batch:** BATCH-09
**Tasks:** FBT-040, FBT-041, FBT-042, FBT-044
**Status:** COMPLETE

---

## Q1: Did `BTreeDefinitionGenerator.cs` require the return type to be `BehaviorTreeBlob`? Did you encounter the BTree002 diagnostic?

Yes. The source generator validates that the `[BTreeDefinition]`-annotated method must be `static`, return `BehaviorTreeBlob`, and have zero parameters. If any condition fails, it emits a BTree002 warning and skips the method. `AmbushTree.BuildAmbushTree()` satisfies all three constraints and was correctly processed.

The BTree002 diagnostic was NOT encountered for `BuildAmbushTree` (it was valid). The expected BTree001 informational diagnostics WERE produced for the three 3-parameter reusable delegates (`CheckAmmo`, `HasThreat`, `AimAndFire`) -- this is correct and expected behavior.

---

## Q2: What GUIDs did you assign to the new `Fbt.Examples.FluentBTree` project in `FastBTree.sln`?

Project GUID: `{5DDE0086-56C4-4218-AEE1-8B62003A10EE}`

The project was added to the `examples` solution folder (GUID `{B36A84DF-456D-A817-6EDD-3EC3E7F6E11F}`).

---

## Q3: What was the final test count in `Fbt.Tests` after adding the `SampleProjectTests`?

**160 tests total** (149 existing + 11 new SampleProjectTests). All 160 passed, 0 failed.

---

## Q4: Did any of the 11 SampleProjectTests fail? If so, what was the root cause and fix?

Yes, one test failed on the first run:

**Test:** `CombatBlackboard_EngagementRange_IsAtOffset8`
**Expected:** 8
**Actual:** 12

**Root cause:** `Marshal.OffsetOf` uses the unmanaged/P-Invoke marshaling layout. By default, `bool` in unmanaged layout is marshaled as a 4-byte `BOOL` (Win32 convention). This caused the unmanaged layout to be:
- `int AmmoCount`: offset 0 (4 bytes)
- `bool ThreatVisible` (as BOOL=4 bytes): offset 4
- `byte _pad0, _pad1, _pad2`: offsets 8, 9, 10
- (compiler padding byte): offset 11
- `float EngagementRange`: offset 12

In managed memory, `bool` is always 1 byte, so the managed offset of `EngagementRange` is 8. The mismatch caused `Marshal.OffsetOf` to return 12 instead of 8, and would have caused incorrect `Unsafe.AddByteOffset` projection in the expression-bound condition delegate.

**Fix:** Added `[MarshalAs(UnmanagedType.U1)]` to `ThreatVisible`, forcing the unmanaged layout to use 1 byte for the bool field. This aligns `Marshal.OffsetOf` results with the managed memory layout:
- `ThreatVisible`: offset 4 (1 byte)
- `_pad0, _pad1, _pad2`: offsets 5, 6, 7
- `EngagementRange`: offset 8

After the fix all 11 SampleProjectTests passed.

---

## Files Created

- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj`
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Program.cs`
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatBlackboard.cs`
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatActions.cs`
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/AmbushTree.cs`
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/SampleProjectTests.cs`

## Files Modified

- `FDP/ExtDeps/FastBTree/FastBTree.sln` -- added `Fbt.Examples.FluentBTree` project entries
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj` -- added `ProjectReference` to `Fbt.Examples.FluentBTree`
