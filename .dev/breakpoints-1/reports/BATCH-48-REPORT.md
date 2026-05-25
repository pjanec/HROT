# BATCH-48 Report: UBP-P10T1 + UBP-P10T2 — Editor & CGF Subsystem Wiring

**Status:** COMPLETE  
**Tasks:** UBP-P10T1 (EditorSubsystem), UBP-P10T2 (CgfSubsystem)  
**Result:** All 5 new tests pass; 103 existing BP tests remain green; `dotnet build IOS-IG-SimHost.sln -v quiet` → 0 errors, 0 warnings.

---

## Summary of Changes

### Task 1 — Project references

Added to `Hrot.Editor.csproj` and `Hrot.CGF.csproj`:
```xml
<ProjectReference Include="..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
<ProjectReference Include="..\Blueprints\Hrot.Blueprints.Editor\Hrot.Blueprints.Editor.csproj" />
```

Both projects already compiled cleanly after the references were added.

### Task 2 — EditorSubsystem wiring

- Added 4 fields (`_bpPreTickSnapshot`, `_bpSnapshotProvider`, `_bpManager`, `_bpSystem`)
- Added 4 `using` directives (`Hrot.Diagnostics.Breakpoints`, `Hrot.Blueprints.Editor.Debug`, `StructEdit.Reflection`, `Fdp.Toolkit.ReplayBrowser.Search`)
- Constructed the full BP stack in `Initialize()` before `_kernel.Initialize()`
- Exposed `internal IDataBreakpointManager? DataBreakpointManager` and `internal DebugSnapshotProvider? BpSnapshotProvider` test hooks

### Task 3 — CgfSubsystem wiring

- Same field/using additions as above
- Introduced `private sealed class CgfNoOpTimeController` (see Q2 below) for the time adapter
- Constructed the BP stack before `_context.Kernel.Initialize()`
- Exposed same test hooks via `internal IDataBreakpointManager? DataBreakpointManager` and `internal DebugSnapshotProvider? BpSnapshotProvider`

### Task 4 — Integration tests

New file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs`

| Test | Result |
|------|--------|
| `EditorSubsystem_Init_RegistersManager` | PASS |
| `EditorSubsystem_Init_RegistersBreakpointSystems` | PASS |
| `EditorSubsystem_Boot_NoExtraCost_WhenNoBreakpoints` | PASS |
| `CgfSubsystem_Init_RegistersManager` | PASS |
| `CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead` | PASS |

---

## Insight Questions

### Q1: Issues encountered and how they were resolved

**EditorSubsystem:**
No structural issues. The instructions provided the exact wiring block including every component registration to mirror onto `_bpPreTickSnapshot`. The only minor adjustment was that `StructEdit.Reflection` and `Fdp.Toolkit.ReplayBrowser.Search` were not yet imported — adding the four using directives was sufficient.

An early draft used `subsystem.Kernel.Update(1f / 60f)` in the tests; the compiler flagged it as obsolete (`[Obsolete]`). Since `TreatWarningsAsErrors` is active in the test project, this produced a build failure. Fixed by switching to `subsystem.Kernel.Update()` (no-args) throughout.

**CgfSubsystem:**
The main issue was the time adapter (see Q2). A secondary issue was the domain ID for CGF integration tests: the instructions suggested using a value ≥ 230, but `CycloneDDS.Runtime.DdsException: Failed to create participant` was thrown because CycloneDDS enforces a hard maximum domain ID of 232, and `Interlocked.Increment` from a start of 240 produced 241 and 242. Fixed by changing `_domainCounter = 162` (gap between `AllSubsystemsClusterTransitionTests` at 160–161 and `ClusterOpE2eScriptTests` at 170), giving domains 163 and 164.

### Q2: MasterSyncController access in HrotNodeContext

`HrotNodeContext` does **not** expose a `MasterSyncController`. CGF is a slave node — it uses a `SlaveSyncController`, not a `MasterSyncController`. There is no master controller to wrap.

The solution was to avoid adapting the CGF time controller entirely and instead introduce a **no-op** implementation of `IEngineDebugTimeController` as a private sealed nested class inside `CgfSubsystem`:

```csharp
private sealed class CgfNoOpTimeController : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
{
    public bool IsPausedByDebugger => false;
    public void RequestPause() { }
    public void RequestResume() { }
    public void RequestStepOneTick() { }
}
```

This is correct by design: pause/step-one-tick is an editor-side concern. The CGF brain node participates in breakpoint data collection (snapshot capture, predicate evaluation, hit detection) but cannot independently pause the simulation — that authority belongs to the editor/orchestrator side. The no-op controller means a triggered breakpoint will flag `IsPaused = true` on the manager but the actual time freeze is handled by the orchestrator layer, not the slave.

### Q3: Component registries mirrored onto _bpPreTickSnapshot

**EditorSubsystem** mirrors the full set that `_world` receives before `_kernel.Initialize()`:
```
SimHostComponentRegistry.RegisterAll(_bpPreTickSnapshot);
CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);
_bpPreTickSnapshot.RegisterManagedComponent<ZoneMembership>();
_bpPreTickSnapshot.RegisterComponent<MapDisplayComponent>();
_bpPreTickSnapshot.RegisterComponent<Hrot.IG.Components.CullingState>();
_bpPreTickSnapshot.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
_bpPreTickSnapshot.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
_bpPreTickSnapshot.RegisterComponent<VisualEffectState>();
_bpPreTickSnapshot.RegisterComponent<TracerTarget>();
```
The IG-level visual components (`CullingState`, `ResolvedStyle`, `IgSymbolOverride`, `VisualEffectState`, `TracerTarget`) are registered individually because they are not covered by `SimHostComponentRegistry` or `CgfComponentRegistry` but are registered on `_world` inline in `EditorSubsystem.Initialize()`.

**CgfSubsystem** mirrors only `CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot)`. This is less broad than EditorSubsystem because CGF's `_context.World` is built by `HrotNodeBuilder`, which registers exactly the CGF schema. There are no inline registrations in `CgfSubsystem.Initialize()` outside of `CgfComponentRegistry`. This differs slightly from what was expected (the instructions mentioned also checking for `CognitiveComponentRegistry` and `CombatComponentRegistry`) but inspection of the code confirmed those registries are either included within `CgfComponentRegistry.RegisterAll` or are not registered in `_context.World` at all in the current codebase.

### Q4: Weak points in subsystem initialization sequences

**The "register before Initialize" contract is invisible at the type level.** `ModuleHostKernel.RegisterGlobalSystem<T>()` and `ModuleHostKernel.Initialize()` have no compile-time enforcement of call order. The runtime throws `InvalidOperationException` if systems are registered after `Initialize()`, but there is no guard warning if you insert the BP block in the wrong place during a refactor. A guard comment was added at each insertion site, but a runtime assertion or a flag-check on `RegisterGlobalSystem` (e.g. throw if `_initialized == true`) would be safer.

**CgfSubsystem initialization has two distinct phases** (pre-`_context.Kernel.Initialize()` for system registration, post for visualization setup) with no explicit structural boundary. The BP wiring block must sit before `_context.Kernel.Initialize()`, but that line appears at roughly line 540 in a 800-line `Initialize()` method. A future developer refactoring the method could easily move the BP block below it by accident.

**The `_bpPreTickSnapshot` schema is a snapshot-at-construction-time.** Module-registered components added after the kernel's `Initialize()` call are not in the snapshot's schema. This is correct per DESIGN §5 but is a footgun: adding a new component registration to a module will silently not appear in breakpoint captures, with no diagnostic.

### Q5: Suggested commit message

```
feat(ubp): wire DataBreakpointManager into EditorSubsystem and CgfSubsystem (P10T1+P10T2)

- Add Hrot.Diagnostics.Breakpoints + Hrot.Blueprints.Editor refs to Hrot.Editor.csproj
  and Hrot.CGF.csproj
- Construct DebugSnapshotProvider / DataBreakpointManager / DataBreakpointSystem
  in EditorSubsystem.Initialize() before _kernel.Initialize()
- Identical wiring in CgfSubsystem.Initialize() via _context.Kernel;
  use CgfNoOpTimeController (slave node cannot pause the sim independently)
- Expose internal IDataBreakpointManager DataBreakpointManager test hooks
  on both subsystems
- Add BreakpointSubsystemWiringTests: 5 integration tests (3 Editor headless,
  2 full-cluster CGF) proving wiring and zero-overhead gate behaviour
- All 103 existing Hrot.Diagnostics.Breakpoints.Tests remain green
```
