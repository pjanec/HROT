# Batch Instructions: Unified Dynamic Module Hot-Plugging (RCU)

**Component:** `ModuleHostKernel`
**Focus:** Architectural Unification, Lock-Free RCU, Memory Provisioning, Honest Testing

## 1. Context & Onboarding

Welcome! Your objective in this batch is to refine and complete the dynamic module hot-plugging architecture for the `ModuleHostKernel`. 

Currently, our engine uses a **Read-Copy-Update (RCU)** pattern to swap execution topologies at runtime without blocking the 60Hz main simulation loop. This allows us to load and unload entire "micro-scenarios" (or "Stories") on-the-fly, allocating and destroying heavy memory pools strictly when needed.

We have a foundational implementation of this, but it suffers from duplicated code paths and incomplete memory/schema logic. The previous iteration correctly implemented the `KernelExecutionTopology` atomic pointer swap, but it had two separate tracks: 
1. A static initialization track for modules present at startup.
2. A dynamic track for modules loaded at runtime (which cut corners on complex features).

**Your Goal:** Unify these paths. The `ModuleHostKernel` should boot up completely empty (aside from global systems). ALL modules—whether added at application startup or injected 3 hours later—must use the exact same batch-based async installation pipeline.

---

## 2. Tasks & Explicit Success Conditions

### Task 1: Architectural Unification & Smart Batch API

We need to support batch operations to avoid "torn states" and performance hits while maintaining backwards compatibility for existing applications that expect a simple registration pipeline.

*   **Add Batch APIs**: Implement `Task InstallModulesAsync(IReadOnlyList<IModule> modules)` and `Task UninstallModulesAsync(IReadOnlyList<IModule> modules)`.
*   **Atomic Compilation**: The background task should compile *all* requested modules into a *single* new `KernelExecutionTopology`. The atomic pointer swap in `SystemPhase.BeforeSync` will activate them all simultaneously.
*   **Smart `RegisterModule` (Backwards Compatibility)**: Do *not* delete `RegisterModule(IModule)`. Instead, make it state-aware:
    *   **Pre-Initialization Phase**: If `Initialize()` has not yet been called, `RegisterModule` simply appends the module to a lightweight internal list (`_pendingInitializationModules`). No async compilation or hot-swapping occurs.
    *   **Runtime Phase**: If `Initialize()` *has* been called, `RegisterModule` should throw a descriptive exception or route to the `InstallModuleAsync` pipeline (depending on what makes sense for the API contract), but discourage one-by-one runtime calls.
*   **Batched Initialization**: Modify `ModuleHostKernel.Initialize()` so that it takes the `_pendingInitializationModules` list, synchronously provisions memory once, calculates the `UnionMask` once, and builds the initial `KernelExecutionTopology`.

**Success Condition 1:** Existing apps using sequential `RegisterModule` calls during startup compile and run exactly as before, but the internal architecture builds the topology only once during `Initialize()`. The dynamic runtime pipeline uses the new batch APIs to prevent torn states.

### Task 2: ECS Schema Mutation

When dynamically loading a module, it might introduce a brand new `IComponentTable` that the live `EntityRepository` (and internal snapshot replicas) do not know about yet. Currently, the implementation just logs a warning and ignores them.

*   **Implement Schema Upgrade**: During the background compilation task (inside the new `InstallModulesAsync`), inspect each new module's `GetRequiredComponents()`.
*   **Safe Mutation**: Safely invoke `EntityRepository.RegisterComponent` (or your ECS registry equivalent) for any unrecognized components *before* the atomic pointer swap occurs.

**Success Condition 2:** Dynamically loading a module with a completely novel component type works seamlessly. The new component is properly synchronized and available in snapshot views.

### Task 3: Memory Provisioning Re-evaluation

This is the most critical missing feature. Currently, if a dynamic module is configured for `DataStrategy.SoD` (Shared Snapshot Provider) or `DataStrategy.GDB` (Double Buffer Provider), the dynamic installer simply re-uses the existing shared provider without recalculating memory boundaries.

*   **Recalculate UnionMask**: When compiling the new topology, you must re-evaluate the `UnionMask` of all required components across the entire convoy of modules sharing the `SoD` or `GDB` provider.
*   **Allocate New Providers**: If the `UnionMask` expands (because the new module needs different components), you **must** allocate a *brand new* `SharedSnapshotProvider` or `DoubleBufferProvider` in the background.
*   **Swap & Drain**: The new topology points to the new provider. The old provider must be gracefully drained and disposed of by the existing native main-thread draining logic once all in-flight tasks finish using it.

**Success Condition 3:** Loading a new `SoD` module that requires an extra component causes the `UnionMask` to cleanly recalculate, allocating a fresh shared provider without disrupting running modules.

### Task 4: Honest Test Suite

The current test suite (`DynamicModuleTests.cs`) is "illusionary." It boasts high coverage but exclusively uses a `CountingDirectModule` configured for `DataStrategy.Direct`. The `Direct` strategy bypasses ALL memory provisioning and snapshot pools, meaning the complex logic from Task 2 and 3 was never tested.

*   **Rewrite/Expand Tests**: Create specific tests that use `DataStrategy.SoD` and `DataStrategy.GDB`.
*   **Batch Test**: Write a test verifying that `InstallModulesAsync` atomically activates 3 distinct `SoD` modules simultaneously.
*   **UnionMask Expansion Test**: Write a test that starts with 1 `SoD` module (requiring Component A), and then dynamically installs a 2nd `SoD` module (requiring Component B). Verify that the `SharedSnapshotProvider` correctly expands its `UnionMask` to include both A and B.

**Success Condition 4:** The test suite visibly demonstrates successful `SoD` and `GDB` dynamic hot-plugging, including proper component mask propagation.

---

## 3. Workflow & Verification

1.  Please ensure zero allocations happen on the main thread's hot path (`SystemPhase.BeforeSync` swap). 
2.  Do NOT introduce disconnected background monitor threads for disposal. Continue using the `_drainingModules` list so the main thread (`HarvestEntry`) natively releases leased views.
3.  Once completed, ensure `dotnet test ModuleHost\ModuleHost.Core.Tests` passes successfully, and review the tests to ensure no "shortcuts" were taken.
