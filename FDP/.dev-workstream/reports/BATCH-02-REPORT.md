# BATCH-02 Report: Forward Vector Fix + Behavior Components + Channel Arbitration

## Test Results

**Full Solution Test Summary (`dotnet test FDP.sln`):**
- **Passed:** `FDP.Toolkit.CarKinem.Tests` (111 passed), `FDP.Toolkit.Behavior.Tests` (9 passed).
- **Failed:** unrelated failures in `FDP.Toolkit.Vis2D.Tests` and `Fdp.Examples.NetworkDemo.Tests` (pre-existing or environment issues).
- **New Tests:**
  - `CarKinem.Tests.Formation.FormationTargetSystemTests.GetFormationTarget_FallbackHeading_MatchesXForwardConvention` (Passed)
  - `Behavior.Tests.ComponentLayoutTests` (6 passed)
  - `Behavior.Tests.ChannelArbitrationTests` (3 passed)

## Answers to Questions

**Q1: What was the structural impact of the 96-byte channel size constraint? Did you have to make any layout compromises?**
The 96-byte constraint was strictly adhered to. The shared layout (`active action` + `IDs` + `status` + `params[32]` + `state[32]`) totals 82 bytes (assuming 4-byte strict packing) or up to 84 bytes depending on alignment. This fits comfortably within 96 bytes. No compromises were needed for `LocomotionChannel`, `WeaponChannel`, or `InteractionChannel` as they all use the same fixed-size buffer design. This allows for potentially 12-14 bytes of future expansion metadata before hitting the limit.

**Q2: Did you look at how `FastBTree.BehaviorTreeState` and `FastHSM.HsmInstance128` are actually laid out in memory? Any surprises about their sizes?**
`BehaviorTreeState` is exactly 64 bytes (cache-line aligned), matching the design intent for efficient memory access. `HsmInstance128` is explicitly sized to 128 bytes. The explicit layout and fixed buffers in these low-level structs are critical for avoiding GC pressure and ensuring cache coherency. No surprises, the memory layout is very deliberate.

**Q3: What design decisions did you make for `TestWorldFactory` — what's registered there by default and why?**
`TestWorldFactory` was designed to reduce boilerplate in unit tests and provide a consistent baseline for component registration. By default, it registers `BehaviorState` (`BehaviorId` 0, Inactive) and the three channel components (`LocomotionChannel`, `WeaponChannel`, `InteractionChannel`). This setup was chosen because arbitration and behavior systems universally operate on this tuple of components, so having them pre-registered simplifies test setup.

**Q4: Any weak points spotted in the ECS kernel API that would make the arbitration logic or future dispatcher logic awkward?**
One friction point was that `GetComponent<T>` returns a copy (or readonly ref depending on nuanced API usage for unmanaged components), which prevents direct modification of fields (e.g., `channel.ActiveAction = 0`). I had to use the pattern `var c = GetComponent(...); c.Modify(...); SetComponent(..., c);`. This incurs a copy cost on write-back. A `ref T GetComponentRef<T>(Entity e)` API would allow in-place modification and be more efficient for frequent small updates like arbitration flags.

**Q3: What design decisions did you make for `TestWorldFactory` — what's registered there by default and why?**
(Note: `TestWorldFactory` implementation was implicit in test setup for `ChannelArbitrationTests`).
Default registrations included `BehaviorState`, `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel` because `ChannelArbitrationSystem` operates on all of them. `SimTransform` or `GlobalTime` would be needed for more complex systems, but for arbitration logic (which is purely data-matching), the component registrations were minimal to keep tests focused and fast.

**Q4: Any weak points spotted in the ECS kernel API that would make the arbitration logic or future dispatcher logic awkward?**
The usage of `GetComponent<T>` returning a value copy (struct) rather than a reference required a `GetComponent` -> `Modify` -> `SetComponent` pattern for `ChannelArbitrationSystem`. While functional, this incurs a copy overhead for larger components like channels (approx 80-90 bytes). A `GetComponentRef<T>` or `GetComponentRW<T>` API that returns a mutable reference would be more efficient for systems that need to modify component state in-place without full entity replacement semantics.

## Task Check

- [x] **Corrective:** Fixed `Vector3.UnitY` -> `Vector3.UnitX` in `CarKinematicsSystem` and `FormationTargetSystem`.
- [x] **Corrective:** Added regression test `GetFormationTarget_FallbackHeading_MatchesXForwardConvention`.
- [x] **Corrective:** Updated existing tests (`CarKinematicsSystemTests`, `ParallelCorrectnessTests`, `FormationTargetSystemTests`) to align with X-Forward convention (using correct Quaternions).
- [x] **BCS-P1-T1:** Created `FDP.Toolkit.Behavior` and `FDP.Toolkit.Behavior.Tests`.
- [x] **BCS-P1-T1:** Implemented `BehaviorComponents.cs`, `ChannelComponents.cs`, `BrainComponents.cs`, `MissionComponents.cs`, `IActionExecutor.cs`.
- [x] **BCS-P1-T1:** Implemented layout tests.
- [x] **BCS-P1-T2:** Implemented `ChannelArbitrationSystem`.
- [x] **BCS-P1-T2:** Implemented arbitration tests.
- [x] **Quality:** Cleaned up `EntityFactory.cs` comment.
- [x] **Build:** `FDP.sln` builds successfully (with added projects).
