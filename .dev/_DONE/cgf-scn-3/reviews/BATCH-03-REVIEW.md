# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Assessment

All five tasks (S309–S313) are correctly implemented:

- **S309** — `EditorSubsystem` now uses the three-group wiring pattern with
  `SimGroupModule` and `PostSimGroupModule` (both implementing `IEcsModule` with
  all required members). Systems are correctly routed: `simHostCorePack` and
  `cgfLogicPackInst` populate their respective groups via the typed overloads.
  The `CgfInputGroupAdapter` is registered via `_kernel.RegisterGlobalSystem`.

- **S310** — `SteppingTimeController` replaced by `MasterSyncController` via
  `TimeControllerFactory.Create(TimeRole.Standalone)`. `SwitchToDeterministic`
  is called on init; `_timeController` is set/cleared correctly. Internal
  accessor `TimeController` enables test verification.

- **S311** — `EditorPreviewController` correctly calls `SwitchToContinuous()` on
  `EnterPreviewMode` and `SwitchToDeterministic(new HashSet<int>())` on
  `ExitPreviewMode`. The `PreviewController` internal accessor enables test access.

- **S312** — `EditorHarness` mirrors the `EditorSubsystem` wiring pattern exactly.
  `SimGroupModule` (Name="HarnessSimGroup") and `PostSimGroupModule`
  (Name="HarnessPostSimGroup") are correctly implemented. `PumpFrames`/`PumpUntil`
  use `_timeController?.Step(...)`.

- **S313** — `RemapComponentNetworkIds` is extracted as a private static method and
  called for both root entity components and child entity components. All five Intent
  DTO types are correctly remapped. The existing root-entity test 13 continues to
  pass; new Test 15 verifies child-entity remapping.

Test results:
- `Hrot.SimHost.Tests`: 456/459 pass (0 failures, 3 skipped — pre-existing; +1 Test 15)
- `Hrot.ClusterRunner.Integration.Tests`: 4 new tests pass (T-ES28 through T-ES31)

Notes:
- T-ES28/T-ES30/T-ES31 use a real-time poll loop (3s deadline) to wait for the
  Future Barrier crossing — acceptable for integration tests; production behavior is correct.
- Test 15 assertion uses `requests.Single(r => r.ChildComponentOverrides != null)` to
  disambiguate from the passenger entity request — correct and clear.

---

## Commit Message

```
feat: Phase 3/4 Editor system group wiring and child entity remapping (BATCH-03)

S309: EditorSubsystem three-group wiring — simHostCorePack and cgfLogicPackInst
      registered via typed overloads into inputGroup/simGroup/postSimGroup.
      Adds SimGroupModule and PostSimGroupModule nested IEcsModule classes.
      Registers CgfInputGroupAdapter for Input phase.

S310: Replace SteppingTimeController with MasterSyncController in EditorSubsystem.
      Uses TimeControllerFactory.Create(TimeRole.Standalone); calls
      SwitchToDeterministic on init. Adds TimeController/PreviewController
      internal accessors for test access.

S311: EditorPreviewController wires time mode transitions:
      EnterPreviewMode -> SwitchToContinuous(),
      ExitPreviewMode -> SwitchToDeterministic(empty set).

S312: EditorHarness updated to mirror EditorSubsystem wiring (same 3-group
      pattern). SimGroupModule/PostSimGroupModule added. Step() call fixed.

S313: StagingEntityExtractor.RemapComponentNetworkIds extracted as private static
      and called for child entity components (was only root). Ensures Intent DTOs
      in ChildComponentOverrides have network IDs remapped.

Tests:
- T-ES28: TimeController_AfterInit_IsInDeterministicMode (S310)
- T-ES29: KernelUpdate_WithoutStep_DoesNotThrow (S309)
- T-ES30: EnterPreviewMode_SwitchesTimeModeToContinuous (S311)
- T-ES31: ExitPreviewMode_SwitchesTimeModeToDeterministic (S311)
- Test15: Extract_ChildEntity_InitialPassengersIntent_NetworkIdIsRemapped (S313)

Build: 0 errors. Hrot.SimHost.Tests: 456/459 pass.
Hrot.ClusterRunner.Integration.Tests: all 4 new tests pass.
```
