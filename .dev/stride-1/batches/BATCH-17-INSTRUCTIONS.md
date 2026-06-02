# BATCH-17: Concrete Bullet physics — entities move/collide in Stride (STR-D11 + STR-D13)
**Tasks:** STR-D11 (concrete `BulletPhysicsBodyService`), STR-D13 (visual↔body unification), + a visible physics demo scenario   **Phase:** GPU bring-up (the real physics integration)   **Est:** ~10–12h
**Dependencies:** Phases 0–4 (motors, reverse-sync, lifecycle, visual binding, animation all wired against the `IPhysicsBodyService` / `IStrideVisualFactory` seams; currently using `NoOpPhysicsBodyService` so nothing moves).

**Goal (user priority):** make FDP-spawned entities **actually move and collide under real Bullet physics in the live Stride app**, and provide a **keyboard/button-triggerable harness scenario** that visibly demonstrates it (an entity falls under gravity, lands on the arena floor, walks, and collides with walls). This replaces the `NoOpPhysicsBodyService` stub with the real Bullet-backed implementation and unifies the physics body with the visual entity so Bullet motion moves the model.

**Verification reality:** the Stride `Simulation` only exists in the running game (GPU/window). The seam *logic* (motors, reverse-sync, lifecycle) is already headless-tested; the concrete `BulletPhysicsBodyService` is **GPU-verified by the human**. Build it correctly against the verified Stride Bullet API (modeled on the template `PlayerController`) + the seam's documented mapping, compile clean, keep tests green, and **add thorough NLog diagnostics** (body created, grounded state, per-entity position over time) so any issue is diagnosable from `editor_stride.log`. Do **not** claim it physically works — the human confirms.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.1 (Bullet sim config + velocity invariant), §6.2 (motors; "each Stride physics entity IS the entity's spatial representation; the animation entity is the same entity or a child of the physics body" — the unification), §5.6 (body lifecycle), §12 (the MainScene's 144 static colliders are the collision world / authoritative floor).
3. The seam + its documented concrete mapping: `Stride/Hrot.Stride.Core/IPhysicsBodyService.cs` (CreateBody/RemoveBody/SetCharacterVelocity/Jump/IsGrounded/GetBodyState/MoveKinematic; the XML docs specify Capsule→`CharacterComponent`, OrientedBox→`RigidbodyComponent`). The current stub: `Stride/Hrot.Stride.Core/NoOpPhysicsBodyService.cs`.
4. The proven Stride `CharacterComponent` reference: `Stride/HrotStrideApp.Game/Player/PlayerController.cs` (`Entity.Get<CharacterComponent>()`, `character.SetVelocity(v)`, `character.Jump()`, `character.IsGrounded`, `this.GetSimulation().FixedTimeStep`).
5. The visual binding (where the mannequin Stride entity + `AnimationComponent` are created): `Stride/HrotStrideApp.Game/StrideVisualFactory.cs`, `Stride/Hrot.Stride.Core/StrideVisualBindingSystem.cs` (`StrideVisualReference` holds the Stride visual entity handle + shape). The motors: `BulletCharacterMotor.cs` / `KinematicVehicleMotor.cs`. Reverse-sync: `BulletReverseSyncSystem.cs`. Lifecycle: `PhysicsBodyLifecycleSystem.cs` (creates bodies for `.WithOwned<SimTransform>()` entities that have a visual ref). Live wiring: `EditorStrideSubsystem.cs` / `StrideHrotGame.cs`.
6. `reviews/BATCH-16-REVIEW.md`, `DEBT-TRACKER.md` (STR-D11, STR-D13).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

**Work autonomously; build clean + keep tests green. Do not change the headless seam contracts in a way that breaks the existing motor/reverse-sync/lifecycle tests. Use FdpStrideTransform for all FDP↔Stride.** Only stop on a genuine breaking design flaw.

---

## Task 1: Concrete `BulletPhysicsBodyService : IPhysicsBodyService` (STR-D11)
**File:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` (NEW). Spec: the interface XML docs + §6.1/§6.2; reference `PlayerController`.
Implement the full seam against the **running Stride `Simulation`**:
- **CreateBody:** resolve the entity's **Stride visual entity** (from the visual binding — see Task 2 unification), and add the physics component **to that visual entity** so Bullet moves the model:
  - `CollisionShapeKind.Capsule` → a `CharacterComponent` with a `CapsuleColliderShape` sized from `ShapeDims` (radius/height); enable gravity so it falls + rests on the static arena floor (authoritative Z, §5.6). Place at `FdpStrideTransform.ToStride(initialPose)`.
  - `CollisionShapeKind.OrientedBox` → a **kinematic** `RigidbodyComponent` with a `BoxColliderShape` from `ShapeDims` (the `KinematicVehicleMotor` owns collision response, §6.2).
  - Add to the simulation (adding the component to the in-scene entity registers it with the `PhysicsProcessor`). [VERIFY] how to obtain the `Simulation` at the wiring point + add/remove bodies (`this.GetSimulation()` analog; collider-shape construction; `CharacterComponent`/`RigidbodyComponent` setup on 4.2.1.2487). Return an opaque handle (the component or a wrapper).
- **RemoveBody:** remove the physics component from the entity / simulation + dispose.
- **SetCharacterVelocity / Jump / IsGrounded:** delegate to `CharacterComponent` (per `PlayerController`).
- **GetBodyState:** read the body entity's world pose (Bullet-updated) → Stride pose; for a dynamic `RigidbodyComponent` read `LinearVelocity`/`AngularVelocity`; characters are `IsKinematic=true` (velocity comes from the motor channel per the seam doc).
- **MoveKinematic:** swept/penetration-tested kinematic move for the vehicle body (block-or-slide against the static world), returning the actual clamped delta. [VERIFY] the Bullet sweep/kinematic-move mechanism; a reasonable first cut (move + sweep-test, clamp on contact) is acceptable — document limitations.
- **Diagnostics (required):** NLog at body create/remove, grounded transitions, and a throttled per-entity position log, so the human can see (and we can debug) physics from `editor_stride.log`.

**Tests:** the concrete service needs a live `Simulation` so it can't be unit-tested headlessly — that's expected (document it). Do NOT weaken the existing seam tests. If any pure helper (e.g. shape-dims → collider-shape params, or the FDP↔Stride conversions) can be unit-tested without a `Simulation`, test those.

## Task 2: Unify the visual entity with the physics body (STR-D13)
The physics component must live on the **same Stride entity as the `ModelComponent`** (design §6.2) so Bullet moving the body moves the visible model (and the `AnimationComponent` composes on it). Wire `BulletPhysicsBodyService.CreateBody` to look up the entity's Stride visual entity via `StrideVisualBindingSystem`/`StrideVisualReference` and attach the physics component there (do not create a separate physics-only entity). Confirm owned entities' visuals are therefore physics-driven (the §7 sync fork already skips forward-syncing owned entities — correct).

## Task 3: Wire the real service into editor_stride + the demo scenario
- In `StrideHrotGame`/`EditorStrideSubsystem`, **use `BulletPhysicsBodyService` instead of `NoOpPhysicsBodyService` when running live** (the headless tests keep using fakes/NoOp). Construct it with the running `Simulation` + the visual binding. [VERIFY] the right lifecycle point so the `Simulation` is available.
- **Harness demo case(s)** (register via `registry.Register(new VisualTestCase(...))`, keyboard/button, per the standing requirement), driving the **proper physics path** (NOT direct `SimTransform` writes):
  - **"Physics Drop"** — spawn a mannequin (capsule character) a couple of meters **above the arena floor**; it should **fall under gravity and land** (resting contact). Log its Z over time.
  - **"Physics Walk"** — give the spawned character a `CrowdMotorIntent` velocity (the real input → `BulletCharacterMotor` → `CharacterComponent.SetVelocity` → Bullet moves it → reverse-sync writes `SimTransform`); it should **walk across the floor and collide with a wall/obstacle** (no tunneling). Combined with the BATCH-13/14 animation, the walking mannequin should also play the walk blend.
  - (Optionally a vehicle "Physics Drive" using the kinematic box, if time permits.)
  - Each case logs what it did via NLog.

## Success Criteria
- [ ] `BulletPhysicsBodyService` implements the full `IPhysicsBodyService` against the running `Simulation`; physics component created on the visual entity (STR-D11 + STR-D13).
- [ ] Live `editor_stride` uses `BulletPhysicsBodyService` (NoOp only in headless tests).
- [ ] Harness "Physics Drop" + "Physics Walk" cases registered (keyboard/button), driving the real motor/intent path; thorough NLog diagnostics.
- [ ] Full headless suite still green (motors/reverse-sync/lifecycle seam tests unchanged); Stride solution builds clean; report submitted.
- [ ] (Human-verified on GPU: mannequin falls, lands, walks, collides — confirmed by the user; not claimed by you.)

## Report Requirements (`reports/BATCH-17-REPORT.md`)
Answer: how you obtain the `Simulation` + create/remove Bullet bodies (the [VERIFY]'d API); the Capsule→CharacterComponent / OrientedBox→kinematic-Rigidbody mapping + gravity setup; exactly how the body is attached to the visual entity (STR-D13) and how `CreateBody` resolves the visual entity; the `MoveKinematic` approach + limitations; how the real service is selected live vs NoOp in tests; the harness Drop/Walk cases + controls (D-keys); the NLog diagnostics added (so the human knows what to look for); what is GPU-verified-only; the headless test status; suggested commit message. Be explicit: you cannot run the GPU app — describe precisely what the human should now see (fall/land/walk/collide) and what the log will show if a step fails.
