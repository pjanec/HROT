# RUNNER-BATCH-04: Technical Debt Resolution & Phase R3 Prep

**Batch Number:** RUNNER-BATCH-04
**Tasks:** R0.1 Fix, R0.2 Fix, R0.3 Fix, R3.1, R3.2
**Phase:** R0/R3 - Tech Debt & Test Executor Preparation
**Estimated Effort:** 16-24 hours
**Priority:** CRITICAL
**Dependencies:** RUNNER-BATCH-03 complete

---

## 🚫 STOP! CRITICAL ARCHITECTURE CORRECTIONS 🚫

**Developer Notice:** In RUNNER-BATCH-03, you successfully embedded the systems, but two critical compromises were made regarding FDP core architecture. **You may not proceed to any Phase R3 tasks until the following Phase R0 architecture violations are resolved.**

### Violation 1: Component ID Pre-Registration Hack & Auto-Assignment
In `IgApplication.cs`, you injected `_ = ComponentType<SimTransform>.ID;` to force component IDs to register before auto-assigned types. **This is an unacceptable hack.** FDP relies on a strict `BitMask256` which has an absolute limit of exactly 256 component types (IDs 0-255). There is no "auto-assignment" fallback because there is no room for dynamically generated IDs. ALL components must have an explicitly assigned ID!

### Violation 2: `EntityMaster` Translator Deletion
You deleted `AutoCycloneTranslator<EntityMaster>` from `SimHostSubsystem.cs` because it threw an `InvalidOperationException` due to `EntityId` being an `int` instead of a `long`. You incorrectly stated `MissionEgressTranslator` replicated `EntityMaster` (it only handles `EntityMission`).
**Deleting the translator blinds SimHost to networked entities.** The actual root cause is that `UnsafeLayout<T>` lacked support for 32-bit (4-byte) `EntityId` fields, which is the correct size for the DDS network.

---

## ✅ Tasks - Part 1: Architecture Fixes (DO THESE FIRST)

### Task 1: Eliminate Auto-Assignment in `ComponentTypeRegistry.cs` (R0.1)
**Objective:** There is no auto-assignment. Remove `_nextId` completely.
1. Open `FDP/Kernel/Fdp.Kernel/ComponentIdAttribute.cs` and update `[AttributeUsage]` to include `AttributeTargets.Class | AttributeTargets.Interface` so that classes and interfaces can also be explicitly driven by ID.
2. Open `FDP/Kernel/Fdp.Kernel/ComponentType.cs`.
3. **DELETE** all logic related to `_nextId`, fallback registration loops, and `RelocateAutoAssigned()`.
4. If a component (struct, class, or interface) is registered and does not have a `[ComponentId]` attribute, **THROW an `InvalidOperationException` unconditionally.** 
5. In `Hrot.IG/IgApplication.cs`, **DELETE** the `_ = ComponentType<...>.ID;` hack block.

### Task 2: Explicitly Attribute All Components (R0.2)
**Objective:** Since auto-assignment is dead, every component needs a declared real ID.
1. Find EVERY struct, class, and interface used as a component anywhere in the system (e.g., `ContextMenuState`, `EditablePolyline`, `HistoryTrail`, `ITkbDatabase` etc.).
2. Define explicit ID constants for them in `GlobalComponentIds.cs`.
3. Decorate every single one of those component types with their newly assigned `[ComponentId(GlobalComponentIds.YourComponent)]`.
4. Run all tests. They will crash immediately at runtime anywhere an unregistered, unattributed component is pushed into the ECS. Fix them systematically.

### Task 3: Fix `UnsafeLayout<T>` and `MultiInstanceLayout<T>` for 32-bit Entity IDs
**Objective:** The DDS network strictly uses 4-byte `int` for `EntityId`. FDP's unsafe blitting tools currently hardcode a check for `long` (8-byte) `EntityId`s, causing translators like `AutoCycloneTranslator<EntityMaster>` to crash on startup.
1. Open `FDP/Toolkits/FDP.Toolkit.Replication/Utilities/UnsafeLayout.cs` AND `MultiInstanceLayout.cs`.
2. Add a `public static readonly bool IsEntityId32Bit;` field to both.
3. In the static constructors, update the type checking to accept `typeof(int)` and `typeof(uint)`, setting `IsEntityId32Bit = true`.
4. Update `ReadEntityId` and `WriteEntityId` (or `ReadId`/`WriteId`) to branch on the new 32-bit flag (e.g., if true, read/write as `int` and cast to/from `long`).
5. Restore the `translators.Add(new AutoCycloneTranslator<EntityMaster>(...))` line in both `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` and `Hrot.SimHost/Program.cs`.

*DO NOT PROCEED TO PART 2 UNTIL ALL TESTS PASS WITH THESE FIXES IN PLACE.*

---

## ✅ Tasks - Part 2: Headless Test Executor (Phase R3)

Once Part 1 is complete and pushed, begin implementing the infrastructure for the Runner to execute automated `TestScripts` in headless mode.

### Task 4: Implement Test Script JSON Parser (R3.2)
**Objective:** See `TASK-DETAILS-RUNNER.md` Task R3.2.
1. Create `Models/TestScript.cs`, `Models/TestStep.cs`, and `Models/AssertionRule.cs`.
2. Write the JSON parser logic to deserialize scripts and expand repeated steps using `interval` logic.
3. Write associated xUnit tests.

### Task 5: Implement `HeadlessTestExecutor` Core (R3.1)
**Objective:** See `TASK-DETAILS-RUNNER.md` Task R3.1.
1. Create `Services/HeadlessTestExecutor.cs`.
2. Implement the main loop `RunAsync()` that initializes the `SubsystemOrchestrator`, reads the time-sequenced steps from the parsed script, executes handlers, collects metrics, and cleanly shuts down.
3. Stub the `ITestActionHandler` interface.

---

## 🧪 Testing Requirements
- **Crucial:** You must ensure `Hrot.IG.Tests` and `Hrot.SimHost.Tests` pass seamlessly unconditionally throwing on unattributed components.
- Supply full Unit Tests for `TestScript` json parsing.

## 📊 Report Requirements
In your `RUNNER-BATCH-04-REPORT.md`:
- Document how many components were previously silently generated without `[ComponentId]`.
- Detail the implementation choices made inside the new `EntityMasterTranslator`.
- Provide an example JSON snippet validated by your new `TestScript` parser.
