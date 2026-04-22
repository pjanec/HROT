# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Verdict:** APPROVED  

---

## Assessment

All four tasks (S305–S308) are correctly implemented:

- **S305** — `MissionControlModule.RegisterSystems(inputGroup, simGroup)` correctly routes
  `DoctrineIngressSystem` to `inputGroup` and `MissionDirectorSystem` to `simGroup`, with null
  checks on both parameters. Existing single-group overload is unchanged.

- **S306** — `CgfLogicPack.RegisterSystems(inputGroup, simGroup)` correctly routes
  `MissionControlExecutionSystem` and `DoctrineIngressSystem` to `inputGroup` (2 systems), and
  all 13 remaining systems to `simGroup`. Null checks present. Four new tests cover all success
  conditions. Regression test for single-group overload passes.

- **S307** — `CgfInputGroupAdapter` is `public sealed`, carries `[UpdateInPhase(SystemPhase.Input)]`,
  implements `IEcsModuleSystem`, delegates to `_group.Run()`. No new project references added.
  Accessible from `Hrot.CGF` and `Hrot.Editor`.

- **S308** — `CgfSubsystem.Initialize()` creates `inputGroup` and `simGroup`, calls the new
  two-group overload, registers the Input-phase group via `RegisterGlobalSystem(new CgfInputGroupAdapter)`,
  and keeps `CgfSimGroupModule` for the Simulation phase. `Shutdown()` disposes both groups.

Test results: 455/455 pass (0 failures, 3 skipped — pre-existing).

---

## Commit Message

```
feat: Phase 2 CGF multi-phase system group split (BATCH-02) -- Completes S305-S308

S305: Add MissionControlModule.RegisterSystems(inputGroup, simGroup) overload.
      DoctrineIngressSystem -> inputGroup, MissionDirectorSystem -> simGroup.

S306: Add CgfLogicPack.RegisterSystems(inputGroup, simGroup) overload.
      MissionControlExecutionSystem + DoctrineIngressSystem -> inputGroup (2).
      All other Brain-tier systems -> simGroup (13). Existing single-group
      overload unchanged. Tests updated and 4 new tests added.

S307: Create Hrot.Common.Infrastructure.CgfInputGroupAdapter.
      [UpdateInPhase(SystemPhase.Input)] IEcsModuleSystem that runs a
      SystemGroup during the kernel Input phase.

S308: Update CgfSubsystem to use two-group registration. Adds _inputGroup
      field; registers Input group via RegisterGlobalSystem(CgfInputGroupAdapter)
      and Simulation group via CgfSimGroupModule (unchanged). Disposes both.

Tests: 455/458 pass in Hrot.SimHost.Tests (3 pre-existing skips).
```
