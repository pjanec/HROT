# BATCH-05: BulletCharacterMotor + KinematicVehicleMotor (Phase 1 motors)
**Tasks:** STR-P1-T3, STR-P1-T4   **Phase:** P1   **Est:** ~9–11h
**Dependencies:** BATCH-04 (`StrideKinematicsModule`, `PhysicsBodyLifecycleSystem`, `IPhysicsBodyService` seam, `PhysicsBodyReference`).

Goal: the two motors that feed Bullet bodies. (T3) `BulletCharacterMotor` reads `CrowdMotorIntent`, applies the stance speed multiplier, and drives the Bullet `CharacterComponent`. (T4) `KinematicVehicleMotor` integrates `VehicleCommandSystem` output as a kinematic move **with owned collision response**, computing the post-collision linear+angular velocity for the reverse-sync. Because headless Bullet is infeasible (BATCH-04 finding: `Simulation` is `internal`), both motors are built against the **extended `IPhysicsBodyService` seam** — the motor *logic* (intent→velocity, stance scaling, blocked→zero) is unit-tested with a recording/scriptable fake; the real Bullet behavior lands in the concrete service at GPU bring-up (STR-D11).

No Corrective Task 0 (BATCH-04 approved, no P1 issues).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.2 (motors — the spec), §5.3 (`CrowdMotorIntent` rationale), §6.1 (velocity invariant — kinematic bodies own their velocity, blocked move ⇒ zero).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P1-T3, STR-P1-T4.
4. `reviews/BATCH-04-REVIEW.md` (the headless-Bullet finding + accumulating-risk note) and `DEBT-TRACKER.md` (STR-D11, STR-D12).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **`CrowdMotorIntent` does not exist yet — create it.** It is the engine-agnostic steering output (design §5.3: "prefer a dedicated `CrowdMotorIntent` over reusing `SimVelocity`"). Place it where both `CrowdAgentUpdateSystem` (writer, P2-T4) and `Hrot.Stride.Core`'s motor (reader) can reference it without a cycle — i.e. in `Fdp.Toolkits` (Navigation), **not** `Hrot.Stride.Core`. [VERIFY] the best namespace + register it in the appropriate component registry so spawned crowd entities can carry it. Suggested shape: a steering velocity (`Vector3`, FDP space) + optional flags (e.g. jump). Keep it minimal; document fields.
- **`CrowdAgent`** component: `Fdp.Toolkits.Navigation.NavigationComponents` ([NavigationComponents.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs)).
- **Stance** (Standing/Crouched/Prone) for the speed multiplier — [VERIFY] the live stance source for a CrowdAgent entity (candidates: a stance field on `CrowdAgent`, or `StanceStatus`/`StanceIntent` in `Hrot.MuscleCharacter.Animation`). Use the real one; if none is cleanly available on the muscle entity, define the motor's stance input explicitly and document it. The multipliers are motor config (Standing=1.0, Crouched/Prone < 1.0) — make them configurable, assert via test.
- **`VehicleCommandSystem`**: `Fdp.Toolkits.CarKinem.Systems` ([VehicleCommandSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/VehicleCommandSystem.cs)) — [VERIFY] the output component it writes (throttle/steer / desired motion) that `KinematicVehicleMotor` consumes.
- **`CharacterComponent` reference**: the template `PlayerController` at [Stride/HrotStrideApp.Game/Player/PlayerController.cs](../../../Stride/HrotStrideApp.Game/Player/PlayerController.cs) is the working example of `CharacterComponent.SetVelocity(...)` / `.Jump()` / `.IsGrounded` — use it when writing the **concrete** service, not the headless tests.
- `IPhysicsBodyService` + `PhysicsBodyReference` (BATCH-04, `Hrot.Stride.Core`) — extend the seam here.
- `FdpStrideTransform` for any FDP↔Stride vectors.

**Complete tasks in sequence; do NOT start T4 until T3 is implemented, tested, and ALL tests (incl. prior batches') pass.** Work autonomously. Only stop on a genuine breaking design flaw or unrecoverable blocker.

---

## Task 1: `CrowdMotorIntent` + `BulletCharacterMotor` (STR-P1-T3)
**Files:** `CrowdMotorIntent` (NEW, in `Fdp.Toolkits` per above) + `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs` (NEW). Extend `IPhysicsBodyService`. Spec: design §6.2, §5.3.
Extend `IPhysicsBodyService` with character driving, e.g. `SetCharacterVelocity(bodyHandle, Vector3 strideVelocity)`, `Jump(bodyHandle)`, and `bool IsGrounded(bodyHandle)`. `BulletCharacterMotor` (runs pre-physics, for `.WithOwned<SimTransform>()` humanoid/`CrowdAgent` entities): read `CrowdMotorIntent`, apply the stance speed multiplier, and call the service to set the character velocity (converted via `FdpStrideTransform.ToStrideVelocity`); on a jump request, call `Jump` only when `IsGrounded`.

**Tests required** (headless, scriptable fake `IPhysicsBodyService`):
- A `CrowdMotorIntent` of magnitude `v` (some direction) yields a character velocity of magnitude `v` with **direction preserved** (assert the vector passed to the fake `SetCharacterVelocity`, in the correct space/swizzle).
- Stance Standing/Crouched/Prone scales the applied speed by the configured multipliers (assert the scaled magnitude for each stance).
- Jump: a jump-requesting intent calls `Jump` **only when `IsGrounded` is true** (fake returns grounded/not-grounded; assert `Jump` called/not-called accordingly).

## Task 2: `KinematicVehicleMotor` (STR-P1-T4)
**File:** `Stride/Hrot.Stride.Core/KinematicVehicleMotor.cs` (NEW). Extend `IPhysicsBodyService` for the swept move. Spec: design §6.2 + §6.1 ("kinematic bodies own their velocity").
Extend `IPhysicsBodyService` with a swept/penetration-tested kinematic move, e.g. `KinematicMoveResult MoveKinematic(bodyHandle, Vector3 desiredDeltaStride, Quaternion desiredRotDelta)` returning the **actual executed (collision-clamped) delta** (block-or-slide against the static world). `KinematicVehicleMotor` (pre-physics, for `.WithOwned<SimTransform>()` vehicle entities): read `VehicleCommandSystem` output, integrate the commanded motion into a desired delta, call `MoveKinematic`, then compute the **post-collision linear + angular velocity** from the actual executed delta (`vel = actualDelta / dt`; angular from the executed rotation delta), **zeroing it on a fully blocked move**. Expose that velocity for the reverse-sync (STR-P1-T5 will read it) — e.g. store it on `PhysicsBodyReference` or a motor-output component; document the chosen channel.

**Tests required** (headless, scriptable fake that simulates collision outcomes):
- Unobstructed command (fake returns `actualDelta == desiredDelta`) → the kinematic transform integrates along the commanded heading; computed linear velocity ≈ `desiredDelta/dt` (assert vector).
- A move into a static obstacle (fake returns a clamped/partial delta = block-or-slide) → the motor does **not** tunnel (it uses the returned clamped delta), and a **fully blocked** move (fake returns zero delta) yields **zero** output velocity (assert exactly zero, lin and ang).
- The motor's computed post-collision linear + angular velocity is exposed on the documented channel for the reverse-sync (assert it's readable and correct).

---

## Success Criteria
- [ ] STR-P1-T3: `CrowdMotorIntent` created + registered; `BulletCharacterMotor` converts intent→character velocity with stance scaling and grounded-gated jump, via the extended seam; all tests pass.
- [ ] STR-P1-T4: `KinematicVehicleMotor` integrates commands as a collision-responding kinematic move and computes post-collision lin+ang velocity (zero on full block), exposed for the reverse-sync; all tests pass.
- [ ] Full test suite green (all prior batches + this); Stride solution builds clean (no new warnings beyond pre-existing NU1608); report submitted.

## Report Requirements (`reports/BATCH-05-REPORT.md`)
Answer: where you placed/registered `CrowdMotorIntent` and why; the live stance source you found ([VERIFY] result) and the multiplier config; the `VehicleCommandSystem` output component the vehicle motor consumes; the exact `IPhysicsBodyService` extensions you added (signatures) and the channel by which the vehicle motor's post-collision velocity reaches the reverse-sync; how the fake service simulates block-or-slide for the T4 tests; what remains for the concrete `BulletPhysicsBodyService` (STR-D11) — i.e. which behaviors are still only seam-tested; weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
