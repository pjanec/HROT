# RUNNER-BATCH-03 Report

**Batch:** RUNNER-BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2025-07-19  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| R2.1 — Extract `SimHostSubsystem` | ✅ Complete | `Bagira.Runner/Services/SimHostSubsystem.cs` — full kernel + DDS wiring |
| R2.2 — SimHost standalone build | ✅ Complete | `Bagira.SimHost.Standalone/` project added to solution |
| R2.3 — SimHost integration tests | ✅ Complete | 13 tests in `SimHostSubsystemTests.cs`, all pass |
| R2.4 — Refactor `IgApplication` for embedding | ✅ Complete | `InitializeEmbedded`, `Update`, `DrawWorld`, `DrawUI`, `Shutdown(ownsWindow)` |
| R2.5 — Extract `IgSubsystem` | ✅ Complete | `Bagira.Runner/Services/IgSubsystem.cs` delegates to `IgApplication` |
| R2.6 — IG standalone build | ✅ Complete | `Bagira.IG.Standalone/` project added to solution |
| R2.7 — Extract `IosSubsystem` | ✅ Complete | `Bagira.Runner/Services/IosSubsystem.cs` uses `IosMock` + `NullDdsWriter<T>` |
| R2.8 — IOS standalone build | ✅ Complete | `Bagira.IOS.Standalone/` project added to solution |
| R2.9 — IOS integration tests | ✅ Complete | 10 tests in `IosSubsystemTests.cs`, all pass |
| IG integration tests | ✅ Complete | 9 tests in `IgSubsystemTests.cs`, all pass |

---

## 🧪 Testing Results

**Unit Tests Passed:** 72 / 72 (Bagira.Runner.Tests)  
**Pre-existing IG Tests:** 229 / 229 (Bagira.IG.Tests — unchanged)  
**Pre-existing IOS Tests:** 252 / 252 (Bagira.IOS.Tests — unchanged)

**New Tests Written:**
- `SimHostSubsystemTests.cs` — 13 tests covering Name, Initialize, Update, Shutdown, FullLifecycle, Start/Stop background thread
- `IgSubsystemTests.cs` — 9 tests covering headless lifecycle, Update, DrawWorld, DrawUI, Shutdown
- `IosSubsystemTests.cs` — 10 tests covering Initialize, Update, DrawUI, multi-update, Shutdown, NullDdsWriter isolation

**Key Test Scenarios Verified:**
- [x] SimHostSubsystem initialises full kernel stack (ECS, doctrine registry, network modules) without crashing
- [x] SimHostSubsystem.Update() ticks ECS kernel and SystemGroup without exception
- [x] SimHostSubsystem.Start()/Stop() background thread starts and terminates cleanly
- [x] IgSubsystem.InitializeEmbedded(headless:true) completes without a Raylib window
- [x] IgSubsystem.Update() in headless mode suppresses all Raylib/ImGui calls
- [x] IosSubsystem isolates DDS with `NullDdsWriter<T>` — DDS runtime NOT required
- [x] All three Standalone projects build to runnable executables with 0 errors

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**Issue 1 — ECS Component ID Collision (root cause analysis required)**

The most complex issue was a `System.InvalidCastException` in `IgSubsystemTests`:
```
Unable to cast ManagedComponentTable<ITkbDatabase> to ComponentTable<GlobalTime>
```

**Root Cause:** The `ComponentTypeRegistry` assigns IDs with `_nextId++` for types without
`[ComponentId]`. In a fresh registry, `ContextMenuState` (class, auto) and `EditablePolyline`
(class, auto) claim IDs 0 and 1. `HistoryTrail` (struct, no attribute) takes 0 (first pass),
then explicit types `ResolvedStyle=[ComponentId(110)]` etc. leave IDs 0–2 free for auto types.
After exactly 2 auto-assigned class types, `ITkbDatabase` (interface, always auto) claims ID 3.
Later, `GlobalTime` with `[ComponentId(3)]` triggers `RelocateAutoAssigned(ITkbDatabase, 3)` in
the registry, but the `EntityRepository._singletons[3]` array slot still holds the
`ManagedComponentTable<ITkbDatabase>` placed during `SetSingletonManaged<ITkbDatabase>`. When
`UpdateInternal` later calls `SetSingletonUnmanaged<GlobalTime>`, it reads `_singletons[3]` (non-
null, wrong type) and throws.

**Fix:** Added explicit pre-anchor calls at the top of `IgApplication.InitializeEcs()`:
```csharp
_ = ComponentType<SimTransform>.ID;        // anchors slot 0
_ = ComponentType<SimVelocity>.ID;         // anchors slot 1
_ = ComponentType<HealthData>.ID;          // anchors slot 2
_ = ComponentType<GlobalTime>.ID;          // anchors slot 3
_ = ComponentType<IsActiveTag>.ID;         // anchors slot 4
_ = ComponentType<LifecycleDescriptor>.ID; // anchors slot 5
_ = ComponentType<HierarchyNode>.ID;       // anchors slot 6
_ = ComponentType<PartDescriptor>.ID;      // anchors slot 7
```
This forces all Fdp.Kernel core component types to occupy their designated explicit IDs before
any auto-assigned IG types or class/interface types can claim those low-numbered slots.

**Issue 2 — `AutoCycloneTranslator<EntityMaster>` in SimHostSubsystem**

`AutoCycloneTranslator<T>` requires `T` to have a `long EntityId` field (`UnsafeLayout<T>.IsValid`
check). `EntityMaster` is a DDS-generated struct that does not satisfy this constraint. The
constructor immediately throws `InvalidOperationException`. The original `Bagira.SimHost/Program.cs`
carries the same bug (comment `TASK-IF003`).

**Fix:** Removed the `AutoCycloneTranslator<EntityMaster>` translator from the `customTranslators`
list in `SimHostSubsystem.Initialize()`. `SimHostModule` provides its own mission egress translator
that handles the EntityMaster DDS topic.

---

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **R0 not yet implemented:** The `ComponentTypeRegistry` has no mechanism to pre-reserve IDs
   declared in `GlobalComponentIds` for types that have not yet been registered in the current
   process. Auto-assigned types freely take low-numbered IDs. The proper fix is Phase R0: the
   auto-assignment loop should skip all IDs present in `GlobalComponentIds` constants, not only
   those already in `_idToType`. Without R0, fresh-process scenarios (test isolation, Runner
   startup) are vulnerable to ordering-dependent ID collisions. The pre-anchor workaround added
   to `InitializeEcs()` handles the IG use case but is fragile — any new module that registers
   an auto-assigned type before `InitializeEcs()` runs would break again.

2. **`AutoCycloneTranslator<EntityMaster>` in Program.cs:** The TASK-IF003 comment in
   `Bagira.SimHost/Program.cs` marks a known bug that was never fixed. The same broken line
   would crash SimHost on startup. The `AutoCycloneTranslator` throws at construction time,
   not at network connect time, so there is no DDS-network dependency; it fails regardless of
   DDS availability. Recommend removing the line from Program.cs as well.

3. **`InitializeNetwork(enableNetwork: true)` is always called in headless mode:** Even when
   `headless=true` (no window, test environment), `InitializeEmbedded` calls
   `InitializeNetwork(enableNetwork: true)`. The `enableNetwork` flag only governs whether
   `DdsParticipant` is actually created; `TkbDatabase` and all module registrations still run.
   This is intentional (ECS state must be consistent), but the flag naming is a bit misleading.

---

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **Subsystem implementations live in `Bagira.Runner/Services`** (not in the subsystem projects
   themselves). This avoids circular project references: IG/IOS/SimHost are `OutputType=Exe`
   projects and cannot reference `Bagira.Runner`. Placing subsystem wrappers in
   `Bagira.Runner/Services` means Runner references each subsystem as a library.

2. **`IgApplication.InitializeEmbedded(bool headless)` added as the embedded entry point.**
   The existing `Initialize()` method was preserved and refactored to delegate to
   `InitializeEmbedded()` after Raylib window creation. New internal methods `Update(float)`,
   `DrawWorld()`, `DrawUI()`, and `Shutdown(bool ownsWindow)` were extracted so that both the
   standalone `Run()` loop and the orchestrator-driven embedded mode share the same logic.

3. **`IosSubsystem` uses a private `NullDdsWriter<T>`** to implement `IDdsWriter<T>` with a
   no-op `Write()`. This matches the pattern already used in `Bagira.IOS/Program.cs` (which
   uses `NullDdsWriter`) and makes IOS tests DDS-environment-independent. An alternative was
   to use a mock framework, but the `NullDdsWriter` approach is simpler and matches codebase
   conventions.

4. **Standalone projects reference `Bagira.Runner`** and use `SubsystemOrchestrator` (for IG and
   IOS) or call the subsystem directly (for SimHost stand-alone). This keeps the standalone apps
   as thin wrappers (< 30 lines each) and reuses all orchestrator lifecycle logic. An alternative
   was to duplicate the lifecycle in each standalone; this was rejected to avoid divergence.

---

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

1. **Component ID collision in multi-ECS-world test scenarios** (described in Q1). Not mentioned
   in R2 task specs; it only manifests when multiple `IgApplication` instances are created in the
   same process (or when a fresh process runs without pre-warming the registry).

2. **xUnit tests that call Update() always fail in isolation** (no prior `Initialize`-only test
   to seed the registry). The pre-anchor fix makes each test self-sufficient regardless of
   execution order.

3. **SimHostSubsystem tests don't actually require an external DDS daemon.** The test cases call
   `new DdsParticipant(domainId: 98)` with a high domain ID unlikely to have live writers. The
   `CycloneDDS.Runtime` participates in DDS discovery but tests pass without any external
   process, because the simulation loop is short (no wait for remote data).

4. **`Bagira.SimHost.Standalone` and similar projects reference an `OutputType=Exe` library
   (`Bagira.Runner`).** MSBuild requires `<StartupObject>` to disambiguate multiple `Main`
   methods when the referenced project is also an Exe. This was addressed via the
   `<StartupObject>` property in each standalone's `.csproj`.

---

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

1. **Pre-anchor registrations in `InitializeEcs()`** add 8 static-field lookups on first call
   (each triggers a dictionary lookup + lock in `ComponentTypeRegistry`). On subsequent calls
   they are all no-ops (already in `_typeToId`). No measurable performance impact.

2. **`SimHostSubsystem.Start()` background thread** uses `Thread.Sleep(1)` to yield between
   kernel ticks. This is a polling approach identical to the original `Program.cs`. A
   `Task.Delay` or timer-based approach would be more precise for 1000 Hz simulation, but the
   current approach matches the reference implementation and is adequate for the use case.

3. **`IosSubsystem.NullDdsWriter<T>`** creates a new generic class per type parameter. This is
   JIT-compiled once per `T` and has no run-time allocation after warm-up.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **R0 implementation required** — Apply `[ComponentId(N)]` to all components lacking explicit
  IDs (including `HistoryTrail`, `ContextMenuState`, `EditablePolyline`) and pre-reserve
  `GlobalComponentIds` slots in the auto-assignment loop. The pre-anchor workaround in
  `InitializeEcs()` is sufficient for IG but does not protect SimHost or IOS worlds.

- [ ] **Fix `AutoCycloneTranslator<EntityMaster>` in `Bagira.SimHost/Program.cs`** — The same
  bug removed from `SimHostSubsystem` still exists in the standalone entry point and would
  crash SimHost on startup.

- [ ] **RUNNER-BATCH-04** — Wire all three subsystems into `SubsystemOrchestrator` via
  `RunnerConfiguration` and test the combined multi-subsystem mode.
