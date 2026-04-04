# BATCH-05 Review

**Batch:** BATCH-05  
**Tasks:** PACK2-A001, PACK2-E004, PACK2-R002  
**Reviewer:** GitHub Copilot (dev-lead)  
**Decision:** ✅ APPROVED

---

## Build Verification

| Check | Result |
|-------|--------|
| `dotnet build IOS-IG-SimHost.sln --no-incremental` | ✅ 0 errors, 336 warnings (all pre-existing) |

---

## Test Verification

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.ScenarioEditor.Tests` | 7 pass | 14 pass | +7 (WorldReset ×2, ScenarioFileService ×5) |
| `Hrot.Map.Common.Tests` | 99 pass | 99 pass | 0 |
| `Hrot.SimHost.Tests` | 439 pass, 1 fail | 439 pass, 1 fail | 0 |
| `Hrot.ClusterRunner.Tests` | 189 pass, 3 fail | 189 pass, 3 fail | +1 new CgfSubsystem test |

Pre-existing failures (all unchanged):
- `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` (SimHost.Tests)
- `OrchestratorSubsystemTests.PauseButton_WhenNotPaused_DispatchesPauseTime`
- `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent`
- `SwitchTimeModeEchoLoopTests.PollIngress_ThenScanAndPublish_DoesNotEchoBack`

---

## Scope Check

### PACK2-A001 — WorldResetEvent + selection flush ✅
- `WorldResetEvent.cs` created in `Hrot.ScenarioEditor/Events/`
- `StandardInteractionTool.FlushForWorldReset()` added — calls existing `ClearAllSelections()`
- 2 tests cover flush behavior and event instantiation

### PACK2-E004 — Scenario file operations ✅
- `ScenarioFileService.cs` in `Hrot.ScenarioEditor/Services/`
- `NewScenario`, `SaveScenario`, `LoadScenario` all implemented
- Observer pattern (`RegisterWorldResetObserver`) ensures synchronous flush before `repo.SoftClear()`
- `LoadScenario` validates `SubsystemType` (accepts `"Hrot.Scenario"`, `"Hrot.SimHost"`, `"Hrot.CGF"`)
- `ScenarioEditorModule` updated with optional `ScenarioFileService?` injection
- `FDP.Toolkit.Scenario` reference added to `Hrot.ScenarioEditor.csproj`
- 5 tests cover round-trip, NewScenario reset, Load reset, mismatch rejection, cross-app compatibility

### PACK2-R002 — CgfSubsystem pack installation ✅
- `PackRole { Ingress, Egress }` enum created in `Hrot.Map.Common/PackRole.cs`
- `EntityStatesIngressPack` updated: `PackRole` first param with validation guard
- `ActuatorIntentsEgressPack` updated: `PackRole` first param with validation guard
- All call sites updated (`EntityStatesIngressPackTests`, `ActuatorIntentsEgressPackTests`)
- `CgfApplication.Install(IEcsModule)` added — stores into `_simKernel` (lazy-init ModuleHostKernel)
- `CgfApplication.InstalledModuleNames` exposes `_simKernel.GetRegisteredModuleNames()`
- `CgfSubsystem.Initialize` installs `CgfLogicPack` + `EntityStatesIngressPack(Ingress)` + `ActuatorIntentsEgressPack(Egress)`
- `Hrot.ClusterRunner` added to `Hrot.CGF.csproj` InternalsVisibleTo (for `Participant`/`EventBus` access)
- 1 introspection test confirms exactly 3 modules registered

---

## Quality Notes

- `repo.Clear()` → `repo.SoftClear()` deviation is correct: `Clear()` is internal; `SoftClear()` is the intended public API
- `ScenarioHeader` positional record constructor caught early — instructions had incorrect syntax
- `PackRole` validation guards prevent accidental misuse (e.g., egress pack in ingress role)
- `_simKernel.Initialize()` deferred until first `CgfApplication.Tick()` — correct per ModuleHostKernel API contract (RegisterModule must precede Initialize)
- No NED or CycloneDDS leaks introduced in ScenarioEditor

---

## Suggested Commit Message

```
feat(packs-2): PACK2-A001 + PACK2-E004 + PACK2-R002 -- WorldReset, File Ops, CGF Packs

A001: WorldResetEvent + StandardInteractionTool.FlushForWorldReset()
E004: ScenarioFileService (New/Save/Load) wired into ScenarioEditorModule
R002: PackRole enum, CgfApplication.Install() + lazy sim kernel,
      CgfSubsystem installs CgfLogicPack + EntityStatesIngressPack(Ingress)
      + ActuatorIntentsEgressPack(Egress)

Tests: 14/14 ScenarioEditor, 99/99 Map.Common, 440 SimHost (1 pre-existing),
       192 ClusterRunner (3 pre-existing)
```
