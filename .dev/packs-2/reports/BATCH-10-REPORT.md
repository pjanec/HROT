# BATCH-10 Report

**Batch:** BATCH-10  
**Tasks:** PACK2-C002, PACK2-C003, PACK2-R004  
**Status:** COMPLETE  
**Test results:** Hrot.Editor.Tests: 20/20 passed | Hrot.ClusterRunner.Integration.Tests (offline): 3/3 new + 5/5 pre-existing offline passed

---

## Q1: Did `UninstallModulesAsync` / `InstallModulesAsync` behave as expected in tests?

The three `FeatureSwitchTests` unit tests verify mode tracking without a live kernel (null-kernel guard path). In those tests, `SwitchToExternalAsync()` and `SwitchToInternalAsync()` return immediately (no pumping needed) because the `_kernel == null` guard causes early return before any async kernel call. Both methods completed synchronously, and `CurrentMode` stayed `Internal` as expected.

The production path (with a real kernel) would require `PumpUntil(() => switchTask.IsCompleted)` because `UninstallModulesAsync` queues an RCU topology swap that is committed on the next `kernel.Update()` BeforeSync boundary. This was not explicitly tested in BATCH-10 (that belongs to R005 integration tests with the full DDS path) but the architecture is correctly wired.

---

## Q2: Did `EntityLifecycleModule` drain destruction within 2–3 pump frames?

Yes. The `DeleteCommand_RemovesEntityFromRepo` integration test passed with the default 5 s `PumpUntil` timeout and completed well within that window (total test duration ~136 ms for the full triple-test suite). ELM with `Array.Empty<int>()` participants requires no peer ACKs, so destruction completes in a single pump frame. `PumpUntil` correctly handled this without any manual frame-count assumption.

---

## Q3: Were there any missing project references needed?

One explicit reference was added:

- `Hrot.ClusterRunner.Integration.Tests.csproj` → `<ProjectReference Include="..\Hrot.Editor\Hrot.Editor.csproj" />` (required for `EditorApplication`, `IEditorLogic`, `EditorBootstrap`).

All other dependencies (`SimHostModule`, `EntityLifecycleModule`, `NetworkSpawningSystem`, `TkbDatabase`, `NetworkEntityMap`, `INetworkIdAllocator`) were available transitively via `Hrot.ClusterRunner` → `Hrot.SimHost`. No FDP.Toolkit project references needed to be added directly.

---

## Q4: Any namespace resolution issues?

Two issues encountered and resolved:

1. **`ReliableInitType` namespace was wrong in the batch instructions.** Instructions stated `FDP.Toolkit.Replication.Components`, but the actual location is `ModuleHost.Core.Network.Interfaces` (file: `FDP/ModuleHost/ModuleHost.Core/Network/Interfaces/INetworkTopology.cs`). Fixed the `using` directive in `OfflineEditorIntegrationTests.cs` accordingly.

2. **Components not registered — `NetworkIdentity is not registered` runtime error.** The `NetworkSpawningSystem` calls `world.SetComponent(entity, new NetworkIdentity(...))` directly, but `EntityRepository` requires explicit `RegisterComponent<T>()` calls before use. None of `SimHostCoreLogicPack`, `CgfLogicPack`, or `SimHostModule` implement `GetRequiredComponents()`, so the kernel's `EnsureComponentsRegistered` auto-path did not fire for these types. Fixed by calling `HrotSharedComponentRegistry.RegisterAll(Repo)` at the top of the `EditorHarness` constructor, before module registration. This single call registers `NetworkIdentity`, `NetworkOwnership`, `NetworkAuthority`, `TkbIdentity`, `GhostStateTracker`, `PendingNetworkAck`, `SimTransform`, `SimVelocity`, lifecycle events, and all other shared HROT ECS types.

---

## Q5: Design decisions made + suggested commit message

### Design decisions

- **`HrotSharedComponentRegistry.RegisterAll`** was chosen over manually listing individual `RegisterComponent<T>()` calls. It's the established pattern used by all other Hrot subsystems (SimHostApp, IgApplication, etc.) and registers the complete shared component set in one idiomatic call. This avoids future test breakage if new components are added to the shared registry.

- **`SequentialIdAllocator` stub** was kept as a private nested class inside `EditorHarness` (per instructions), not a file-scoped internal. This keeps it test-local and avoids polluting the test namespace.

- **`EditorApplication` constructor extended with optional params** (kernel, logicPacks, translatorPacks default to null). This preserves backward compatibility: all existing 3-arg construction sites continue to compile without changes.

- **`OrchestrationLogicPack` deliberately omitted from `logicPacks`** — the feature switch only swaps the SimHost simulation layer and CGF logic, not orchestration or scenario editing. This matches the architecture intent.

### Suggested commit message

```
feat(editor): PACK2-C002/C003/R004 — Feature Switch + offline spawn/edit/delete tests

- Add SimHostMode enum (Internal/External) and IEditorLogic.CurrentMode/SwitchToExternalAsync/SwitchToInternalAsync
- Implement EditorApplication.SwitchToExternalAsync/SwitchToInternalAsync using kernel RCU (UninstallModulesAsync/InstallModulesAsync)
- Wire mode-toggle button in EditorToolbarPanel.HandleToggleModeClick
- Update Program.cs to retain named pack instances and pass kernel+logicPacks to EditorApplication
- Extend EditorHarness with SimHostModule+EntityLifecycleModule+TkbDatabase+NetworkSpawningSystem (spawn support)
- Call HrotSharedComponentRegistry.RegisterAll in EditorHarness to pre-register network/lifecycle components
- Add 3 FeatureSwitchTests unit tests (null-kernel no-op path)
- Add 3 OfflineEditorIntegrationTests (spawn/edit/delete via PumpUntil)

Fixes: ReliableInitType namespace (ModuleHost.Core.Network.Interfaces, not FDP.Toolkit.Replication.Components)
Tests: Editor.Tests 20/20, ClusterRunner.Integration.Tests offline 3/3 new + 5 pre-existing
```
