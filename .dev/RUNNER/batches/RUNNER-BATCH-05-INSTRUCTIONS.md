# RUNNER-BATCH-05 Instructions

**Phase R0.2 (Completion) & Phase R3 (Headless Test Actions)**

The previous batch succeeded in removing the ECS auto-assignment behavior and fixing network `EntityId` marshaling. However, the requirement to attribute *all* components with `[ComponentId]` was only applied to `Fdp.Kernel`. 

Since `auto-assignment` is now disabled, any component missing its ID crashes the application on startup. Your first priority is to finish attributing the rest of the FDP ecosystem.

## Part 1: Fix R0.2 — Attribute All Remaining Components

**Objective:** Audit the entire repository for valid ECS components and attribute them properly using `GlobalComponentIds`.

### Task 1: Survey and Attribute Toolkits & Shared Libraries
1. Search the `FDP\Toolkits` and `FDP\ModuleHost` folders for structs/classes being used as components.
   - *Hint A:* `grep` for `public struct` inside `FDP\Toolkits` reveals components like `MapDisplayComponent`, `GeoTransform`, `NetworkIdentity`, `NetworkVelocity`, `NetworkSpawnRequest`, `RaycastRequest`, `Faction`, etc.
   - *Hint B:* Search for `.RegisterComponent<` or `.RegisterManagedComponent<` across the whole solution to ensure you find them all.
   - Note: DO NOT attribute `Events` (like `AudioStimulusEvent` or `HitEvent`) unless they are actually registered into the ECS via `world.RegisterEvent<T>()`.
2. Map these components to the unused constants defined in `Fdp.Kernel/GlobalComponentIds.cs`.
   - Update `GlobalComponentIds.cs` if you need to define new constants for components that are missing them. Pay attention to the allocated ranges!
3. Open each component file and decorate it with `[ComponentId(GlobalComponentIds.YourConstant)]`.

### Task 2: Survey and Attribute Application Layers
1. Search the `Hrot.*` folders (`Hrot.IG`, `Hrot.SimHost`, `Hrot.ClusterRunner`).
2. Map their components (e.g., `SimHostSubsystem` internal components, `IgSubsystem` components, UI-related components) to the constants defined in `GlobalComponentIds.cs`.
3. Add the `[ComponentId]` attributes correctly.

### Task 3: Survey and Attribute Test Components
1. In the `FDP\Examples` and test projects (`*.Tests`), there are test-specific components (e.g., `Position`, `Velocity`, `TestComponentA`, `MockDescriptor`). 
2. Assign them explicit `[ComponentId(###)]` attributes using hardcoded integer literals in the `200-255` range (or unused gaps) directly since they are test components and don't need to pollute `GlobalComponentIds.cs`.

*Validation: Run `dotnet test IOS-IG-SimHost.sln`. DO NOT PROCEED TO PART 2 UNTIL ALL TESTS PASS (except known skipped/flaky ones).*

---

## Part 2: Phase R3 — Test Action Handlers

**Objective:** With the `TestScript` parser from Batch 04 functioning, we must now implement the command handlers that execute script test steps and evaluate assertions.

### Task 4: Implement Test Action Handlers
1. In `Hrot.ClusterRunner/Services`, create an interface `ITestActionHandler` (or use a registry) that can process a `TestStep` dynamically.
2. Implement specific handlers for common test actions:
   - `spawn`: Uses the orchestrator or `EventBus` to publish a `SpawnEntityCommand`.
   - `move`: Updates a target's position or triggers a `MoveTo` behavior.
   - `tick`: Advances the simulation by waiting for the specified update duration.
   - `assert_position`: Extracts the requested entity's bounding box/position and evaluates the given `AssertionRule` JSON.

### Task 5: Implement Metrics Collection
1. Implement a `TestMetricsCollector` that hooks into the ECS to evaluate variables like entity count, simulation frame durations, network latency (if applicable).
2. Save test results to a file (e.g., `TestRunSummary.json`) after a test finishes.

### Deliverables
- Fully attributed ecosystem (`[ComponentId]` everywhere).
- Passing `dotnet test IOS-IG-SimHost.sln` execution.
- Operational `HeadlessTestExecutor` capable of executing `spawn`, `tick`, and `assert` steps.
- **REPORT:** Create `.dev-workstream/reports/RUNNER-BATCH-05-REPORT.md`. Include a note about which test components were most difficult to extract/attribute.
