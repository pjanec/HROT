

Here is the suite of hard-constraint integration and unit tests you must implement. They act as a vice, squeezing out split-buses, domain leakage, and orphaned threads. 

### 1. The Strict Headless Hexagonal Boundary Test
**What it enforces:** Absolute decoupling from CycloneDDS and proper dependency injection.
**The Test:**
*   Construct `OrchestratorSubsystem` and `ExConSubsystem` using a `MockNetworkFactory` (a pure C# stub returning no-op adapters).
*   Do not pass any DDS `Participant`, `DdsReader`, or `DdsWriter`. 
*   Call `Initialize()` and pump `Update(0.016f)` for 100 frames.
**The Trap it Prevents:** If the developer left a rogue `HrotEnvironment.CreateParticipant()` call inside `Initialize()`, or if they failed to remove the concrete DDS translators from the subsystem fields, this test will throw a null reference exception or fail to compile because the test project lacks CycloneDDS references.

### 2. The Single-Swap Pipeline Determinism Test (Fixing the UI Desync)
**What it enforces:** The eradication of the split-bus anti-pattern and adherence to the strict single-swap phase discipline.
**The Test:**
*   Boot `OrchestratorSubsystem` headlessly.
*   Directly inject a `PauseTimeIntent` into the unified `FdpEventBus` write buffer. 
*   Call `bus.SwapBuffers()` (simulating Phase 2 pre-logic swap) and then call `Update(0f)`.
*   Assert immediately that `ClusterUiCache.IsPaused == true`.
*   Inject `ResumeTimeIntent`, swap, and assert `IsPaused == false`.
**The Trap it Prevents:** If the developer maintains isolated buses (e.g., `_orchestrationBus` vs `_eventBus`), the UI cache will never see the intent, and the test fails. If the developer double-swaps the bus mid-frame to cheat, the event is wiped from the read buffer before the UI cache can consume it, and the test fails.

### 3. The C# Event Severing Test
**What it enforces:** `MasterSyncController` natively consumes event bus intents, completely bypassing `ClusterMaster` for time control.
**The Test:**
*   Initialize `MasterSyncController` with the unified bus.
*   Publish a `PauseTimeIntent` directly to the bus and swap.
*   Call `MasterSyncController.Update()`.
*   Assert that its internal mode transitioned to `BarrierPending` (or `Deterministic`).
*   Assert via reflection or a mock spy that `ClusterMaster.HandleClusterOpRequest` was NEVER invoked, and no C# events were fired.
**The Trap it Prevents:** If the developer simply routed the new network intents through `ClusterMaster` to fire the old C# `TimeControlRequested` delegate, the test will catch the illegal invocation. This forces the time controller to listen directly to the bus read buffer.

### 4. The Slave Egress Translation Test
**What it enforces:** A unified domain language where local UI commands and network bounds speak the same canonical intents, eliminating the `ClusterOpIntent` wrapper.
**The Test:**
*   Boot `ExConSubsystem` with a mock `ISlaveOrchestrationTranslator`.
*   Trigger the Pause button on the `ClusterScenarioPanel` (which should now write `PauseTimeIntent` natively to the shared bus).
*   Assert that the mock `ISlaveOrchestrationTranslator.Tick()` method successfully drains a `PauseTimeIntent` from the read buffer.
**The Trap it Prevents:** If the developer forgot to merge `_clusterOpEgressBus` or left the slave-side UI publishing the generic `ClusterOpIntent`, the mock translator will never see the strongly-typed `PauseTimeIntent`, failing the test.

### 5. The Infrastructure Lifecycle Teardown Test
**What it enforces:** The `DdsIdAllocatorServer` background thread is properly encapsulated and deterministically joined upon shutdown.
**The Test:**
*   Create a tracking mock for `INetworkFactory` that returns a spy `IDisposable` when `CreateIdAllocatorServer()` is called.
*   Call `OrchestratorSubsystem.Initialize()`, ensuring the spy is returned to the subsystem.
*   Call `OrchestratorSubsystem.Shutdown()`.
*   Assert that `Dispose()` was called exactly once on the spy.
**The Trap it Prevents:** If the developer orphans the background thread by burying it inside a composite translator, or forgets to retain the `IDisposable` handle in the subsystem, the spy will never be disposed, and the test will fail.
