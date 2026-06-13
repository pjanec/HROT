# BATCH-S2-G — Dynamic Bullet body must honor initial position + reposition (hosted muscle)

**Topic dir:** `.dev/stride-2/` · **Guide:** `.dev/.guides/DEV-GUIDE_claude.md` · **Mode:** sonnet.
CPU build + targeted tests. User GPU-verifies after.

## Root cause (CONFIRMED via DIAG-POS logging, do not re-investigate)
In hosted mode, a hill-attack tank spawns at FDP (668,427). `CreateBody` sets the Stride visual
entity's `Transform.Position` to the converted Stride pos and then `Add`s a **dynamic**
`RigidbodyComponent`. BUT it never calls `UpdateWorldMatrix()`, so the entity's WORLD matrix is still
origin when Stride's `PhysicsProcessor` creates the native `btRigidBody` → **the native body is created at
the world origin (0,0,0)** instead of (668,427). Over the next frames the entity transform interpolates
from the set position down to the native body's origin → on the editor 2D map the entity "teleports to
near origin". (Diagnostic proof: `GetBodyState` frame 1 = (668,…,427) correct, then 354→170→122→81→…→0;
`VehicleState.Speed=0` so the motor is NOT driving it — it's the native-body-at-origin anchor.)

**The fix keeps the body DYNAMIC** (full Bullet physics is the whole point of the Stride muscle). It must
behave like SimHost's simple motion wrt `SimTransform`: **(1) accept the initial position, (2) accept an
external reposition (operator drag → `SimTransform` updated), (3) keep being physics-driven while moving.**

OUT OF SCOPE (do NOT touch): the 3D fall for far-from-origin scenarios (arena is small — EXPECTED, the user
confirmed); preview/edit modes; authority bits; navigation; the `StrideNedRenderDescriptors`/TkbDb work.
The `[DIAG-POS]` logging from BATCH-S2-F may remain in place (harmless) — do not remove it.

## Reference facts (verified)
- `BulletPhysicsBodyService.CreateBody` (`Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`): sets
  `strideEntity.Transform.Position`/`.Rotation` (~lines 293-294 for non-box; the OrientedBox branch ALSO
  re-sets `Transform.Position` at ~line 418 for restingY). Then builds `physComp` and calls
  `strideEntity.Add(physComp)`.
- `entity.Transform.UpdateWorldMatrix()` is the canonical "commit the transform now" call — already used at
  `Stride/HrotStrideApp.Game/StrideSceneGeometrySource.cs:154`.
- `ApplyDynamicConfigIfReady(BodyEntry)` (~line 683) is the FIRST point each dynamic body is confirmed
  physics-ready (`rb.Simulation != null` and the deferred config applied without throwing). `BodyEntry`
  already carries shape/flags; `RigidbodyComponent.LinearVelocity`/`.AngularVelocity` setters are already
  used here (lines 822, 872).
- `BulletReverseSyncSystem.Execute` writes `SimTransform`/`SimVelocity` from the body every post-physics
  frame for owned entities; `PhysicsBodyLifecycleSystem` runs pre-physics.

## Task 1 — Honor the initial position (native body created AT SimTransform, not origin)
In `CreateBody`, immediately BEFORE `strideEntity.Add(physComp)` (i.e. after the shape/component is built
and the entity transform has been set to its final initial pose — for OrientedBox that's after the
`restingY` re-set at ~line 418), add:

```csharp
// Commit the entity's world matrix BEFORE the physics component is registered, so Stride's
// PhysicsProcessor initialises the native btRigidBody at the entity's actual world position
// (the spawned SimTransform) and NOT at the stale origin world matrix. Without this the dynamic
// body anchors at (0,0,0) and the entity interpolates to origin (BATCH-S2-G root cause).
strideEntity.Transform.UpdateWorldMatrix();
```

Note the `Add` happens once at the end of the switch for some shapes and inside each case for others — make
sure `UpdateWorldMatrix()` runs after the FINAL `Transform.Position` assignment and before the `Add` that
registers THIS body's component. If the simplest correct placement is one call right before each
`strideEntity.Add(...)`, do that (it is idempotent and cheap).

**Guarantee (belt-and-suspenders for hosted timing):** also slam the native body to the intended pose the
first frame it is ready. Add to `BodyEntry`: store the initial Stride pose +
an `bool InitialPoseApplied`. In `ApplyDynamicConfigIfReady`, AFTER the config block succeeds (right where
`NativeBodyNotReady` is cleared), if `!InitialPoseApplied`:
```csharp
entry.StrideEntity.Transform.Position = entry.InitialStridePos;
entry.StrideEntity.Transform.Rotation = entry.InitialStrideRot;
entry.StrideEntity.Transform.UpdateWorldMatrix();
rb.LinearVelocity  = SMath.Vector3.Zero;
rb.AngularVelocity = SMath.Vector3.Zero;
entry.InitialPoseApplied = true;
Log.Info("[BulletPhysicsBodyService] InitialPose slammed: '{0}' -> ({1:F2},{2:F2},{3:F2})",
    entry.StrideEntity.Name, entry.InitialStridePos.X, entry.InitialStridePos.Y, entry.InitialStridePos.Z);
```
Populate `InitialStridePos`/`InitialStrideRot` in `CreateBody` from the final `strideEntity.Transform`
(use the post-restingY value for OrientedBox) when constructing the `BodyEntry`.
(If `RigidbodyComponent` exposes a direct teleport such as `UpdatePhysicsTransformation()` in this Stride
version — VERIFY by reflection like other APIs in this file — prefer calling it after setting the transform
so the native body is moved authoritatively; otherwise the `UpdateWorldMatrix()` + zero-velocity above is
sufficient because the body is slammed before it has accumulated any motion.)

## Task 2 — Honor external reposition (drag → SimTransform changed → body follows)
An operator drag updates the entity's `SimTransform` (the in-process equivalent of the architect's
`UpdateEntityDescriptorRequest`). Because the body is Bullet-driven and `BulletReverseSyncSystem`
overwrites `SimTransform` from the body each frame, an external `SimTransform` change is otherwise
clobbered next frame. Detect and apply it PRE-physics.

Add a per-body baseline = the last position the muscle itself wrote/used (the body's own position). Each
pre-physics frame, for every owned entity with a dynamic body, compare the entity's current
`SimTransform.Position` against that baseline:
- If they differ by more than a small epsilon (e.g. `0.01 m`), the change came from OUTSIDE the muscle (an
  editor reposition) → **teleport the native body** to the new `SimTransform`: convert FDP→Stride
  (`FdpStrideTransform.ToStridePosition`/`ToStrideRotation`), set `entity.Transform.Position/.Rotation`,
  `UpdateWorldMatrix()`, zero `LinearVelocity`/`AngularVelocity`, and update the baseline.
- Otherwise leave the body alone (normal physics motion).

Implementation options (pick the cleanest that fits existing structure; state your choice in the report):
- (a) a small public method on `BulletPhysicsBodyService`, e.g.
  `void SyncBodyToExternalPose(object handle, in SimTransform simTf)`, that does the divergence check
  (storing the baseline in `BodyEntry`) and teleports on divergence — called for each owned dynamic body
  from a pre-physics pass; OR
- (b) extend `PhysicsBodyLifecycleSystem.Execute` (which already iterates owned entities pre-physics and
  has the `Bodies` map) to do the divergence check + teleport via a body-service call.

Keep the baseline owned by the body service / body entry so the reverse-sync's writes (which set
`SimTransform = body pos`) naturally keep the baseline and `SimTransform` in agreement on normal frames
(no false reposition). The simplest correct baseline = the body's current Stride position read at the start
of the pre-physics check (i.e. compare `SimTransform` to where the body actually is; if FDP `SimTransform`
maps to a Stride position far from the body's current Stride position, it was repositioned).

## Task 3 — Tests (CPU, must pass)
Use the existing fake physics service pattern (`RecordingFakePhysicsBodyService` /
`Stride/Hrot.Stride.Core.Tests` fakes) — these are headless, no GPU.
1. **Initial pose honored:** drive `PhysicsBodyLifecycleSystem` create for an owned entity whose
   `SimTransform` is far from origin (e.g. FDP (668,0,427)); assert the body is created with that initial
   pose (the fake records the `in SimTransform initialPose` passed to `CreateBody`) — i.e. `CreateBody`
   receives the non-origin pose, and (if you added `InitialStridePos` to the real service) a focused test
   that the slam path sets it. (For the real `BulletPhysicsBodyService`, the native-body behavior is GPU-only;
   assert what is CPU-observable: the `initialPose` propagation and the `BodyEntry.InitialStridePos` value.)
2. **Reposition teleports the body:** create a body, simulate the muscle writing `SimTransform` = body pos
   (no reposition) → assert NO teleport call. Then externally set `SimTransform` to a far pose → run the
   pre-physics check → assert a teleport/zero-velocity occurred and the recorded target equals the new pose.
3. **No false reposition on normal physics motion:** when `SimTransform` stays within epsilon of the body's
   position (normal reverse-synced motion), assert the reposition path does NOT fire.

Run the FULL Stride test suite filtered: `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`.
0 failed (the pre-existing `FileMenuHasSaveCommands` is the only acceptable red).

## HARD CONSTRAINTS
- Touch ONLY: `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`,
  `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs` (if you choose option (b)), and the test file(s).
  Do NOT modify any other production file. Do NOT touch icon files, TkbDb code, nav, motors' velocity math,
  the reverse-sync velocity logic, or the `[DIAG-POS]` lines. NO out-of-scope edits (a prior batch violated
  this — it will be reverted and counted against you).
- Keep the body DYNAMIC. Do NOT make it kinematic. Do NOT add gravity/floor handling.
- Build 0 errors, no new warnings.

## Definition of done
- [ ] Task 1: `UpdateWorldMatrix()` before `Add`, plus the first-ready slam with `InitialStridePos`/`Rot`.
- [ ] Task 2: external-reposition detection + native-body teleport (dynamic), with baseline that doesn't
      false-fire on normal physics motion.
- [ ] Task 3: three CPU tests verifying real values; full filtered suite 0-failed.
- [ ] Report `.dev/stride-2/reports/BATCH-S2-G-REPORT.md` (DEV-GUIDE §4): your option (a)/(b) choice, whether
      `UpdatePhysicsTransformation` exists in this Stride version, exact inserted code, test output. No commit.
