# BATCH-S2-N — Fix half-buried vehicle (ShapeDims fallback misses restingY)

## Problem (the real test-move bug)
The `test-move` scenario vehicle (TkbType 101, IFV) loads HALF-BURIED in the floor and won't move,
even with sim time running. Root cause found:

`BulletPhysicsBodyService.CreateBody` OrientedBox case has two branches:
- **Model-bbox branch** (when `ModelComponent.Model.BoundingBox` is available): computes
  `RestingStrideY` and sets `strideEntity.Transform.Position.Y = RestingStrideY` so the box bottom
  sits on the floor (Y=0). ✓
- **ShapeDims fallback branch** (model bbox unavailable/degenerate — taken at scenario-load time
  before the model bbox is ready): sets the box half-extents but **does NOT apply any restingY** —
  the entity stays at its authored Stride Y = FDP.Z = 0, so the box CENTER is at floor level →
  bottom is `halfY` below the floor → **half-buried**.

A dynamic body embedded in the static floor collider has its horizontal motion arrested by the
contact solver → it also **won't move**. The autonomous harness spawned TkbType 100 which (model
loaded by spawn time) took the bbox branch and rested correctly — so it never reproduced this.

## Fix — TWO FILES

### File 1: `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`
In the OrientedBox `else` (ShapeDims fallback) branch (~lines 477-492), after computing
`useHalfX/useHalfY/useHalfZ` and `boxLocalOffset`, apply the resting-Y the same way the bbox branch
does. For a center-origin box (`boxLocalOffset == Zero`), the entity Y that puts the box BOTTOM at
floor (Y=0) is `useHalfY` (the Stride-Y half-extent). Add, right after `boxLocalOffset = SMath.Vector3.Zero;`:

```csharp
// BATCH-S2-N: the bbox branch above places the entity at its resting Stride Y so the box
// BOTTOM sits on the floor (Y=0); the fallback branch previously skipped this, leaving the
// entity at its authored Y (= FDP.Z, typically 0) → box CENTER at floor → half-buried, and a
// body embedded in the floor cannot move horizontally. Apply the same resting-Y here.
// Center-origin box (LocalOffset=Zero): bottom = entity.Y - useHalfY = 0  ⇒  entity.Y = useHalfY.
float fallbackRestingStrideY = useHalfY;
strideEntity.Transform.Position = new SMath.Vector3(
    strideEntity.Transform.Position.X,
    fallbackRestingStrideY,
    strideEntity.Transform.Position.Z);
```

Update the existing fallback `Log.Info` (the one that prints "from ShapeDims FALLBACK ...") to
include the applied resting Y, e.g. add `restingY={6:F3}` with `fallbackRestingStrideY` as the arg,
so the log shows the body is placed on the floor (mirrors the bbox-branch log).

Do NOT change the bbox branch, the box shape creation, the rigidbody config, or anything else.
`InitialStridePos` is captured AFTER the switch (line ~605) from `strideEntity.Transform.Position`,
so this corrected Y automatically flows into the initial-pose slam — no other change needed.

### File 2: `Stride/HrotStrideApp.Game/StrideSelfTest.cs`
Make the harness representative of the real scenario: change the spawned type from the M1 (100) to
the IFV (101 — the exact type test-move uses).
- Change `private const long SpawnTkbType = 100L;` to `101L`.
- Update the adjacent comment to note it is now the IFV (TkbType 101), the type the test-move
  scenario uses.
Everything else in the harness stays (PosA/B/D, the paused-freeze + drive phases). This exercises the
real IFV dims/path end-to-end.

## Constraints
- Two files only. No other behavior change.
- The bbox branch (model loaded) must keep working exactly as before — this only adds the missing
  resting-Y to the FALLBACK branch.

## Acceptance (lead verifies via harness; user verifies test-move)
- Builds clean (`Stride/HrotStrideApp.sln`).
- `STRIDE_SELFTEST=1` with TkbType 101: RESULT still
  `initialHold=PASS repos=PASS pausedFreeze=PASS drive=PASS` (the IFV rests on the floor and drives).
  NOTE: the harness may take the bbox branch (model loaded) so it validates the IFV end-to-end but
  may not exercise the fallback branch specifically; the fallback fix is independently correct for
  the scenario-load timing that triggers it.
- (User) the test-move IFV now spawns ON the floor (not half-buried) and moves when time runs.
