# BATCH-S2-I — Harness DRIVE-phase (diagnose "vehicle won't move") + fix mannequin (capsule) reposition

**Topic dir:** `.dev/stride-2/` · **Guide:** `.dev/.guides/DEV-GUIDE_claude.md` · **Mode:** sonnet. Build only (Lead runs the GPU app).

## Context (verified this session — do not re-investigate)
- Initial-position hold + dynamic-vehicle reposition already work (verified by the `STRIDE_SELFTEST` harness).
- **"Vehicle won't move" (bug C):** the brain's `MoveToExecutor` (`FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`) sets `NavigationIntent` once in `OnEnter` (IntentId++), then in `Execute` returns **Failure** if the muscle wrote `NavigationStatus.Result ∈ {NoPath, FailedBlocked, FailedUnreachable, ...}`. Observed `NavigationIntent.IntentId` incrementing EVERY frame ⇒ the node fails + re-enters each tick ⇒ the **Stride muscle's path planning is returning NO PATH** (`[VehicleNav] PlanPath returned 0 corners`) even for in-arena points. We must DIAGNOSE this autonomously: does the Stride muscle move a vehicle given a single direct in-arena `NavigationIntent`?
- **Mannequin drag fails (bug D):** `BulletPhysicsBodyService.SyncBodyToExternalPose` early-returns `if (entry.IsKinematic) return;`. `CharacterComponent` bodies are created with `isKinematic=true`, so capsule (mannequin) reposition is skipped entirely. `CharacterComponent.Teleport(Vector3)` exists in Stride.Physics 4.2.1.2487.

## Task 1 — Fix capsule (CharacterComponent) reposition in `SyncBodyToExternalPose`
**File:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`.
The method currently handles only dynamic `RigidbodyComponent`. Make it ALSO reposition kinematic
`CharacterComponent` bodies (mannequins):
- Remove/relax the blanket `if (entry.IsKinematic) return;` so capsules are processed.
- Keep the XZ-only divergence check + Y-preserve logic already there (compare target XZ vs current body XZ;
  epsilon `RepositionEpsilonM`; preserve current Y).
- Branch on the component type:
  - `RigidbodyComponent rb` (dynamic vehicle): existing path — set Transform, `UpdateWorldMatrix()`,
    `rb.UpdatePhysicsTransformation(true)`, zero `LinearVelocity`/`AngularVelocity`.
  - `CharacterComponent ch` (capsule): set `entry.StrideEntity.Transform.Position = newPos` (+Rotation),
    `UpdateWorldMatrix()`, then `ch.Teleport(newPos)` (the Stride character-controller teleport; [VERIFY]
    exact signature — it takes a Stride `Vector3` world position). Do NOT call rigidbody-only APIs on it.
- Guard the `InitialPoseApplied`/readiness checks appropriately: characters don't go through
  `ApplyDynamicConfigIfReady`, so don't gate the capsule branch on `InitialPoseApplied` (that flag is only
  set for dynamic bodies). Use a sensible readiness guard for characters (e.g. component attached /
  `ch.Simulation != null` if available; otherwise just attempt the teleport in a try/catch like the dynamic
  path). Keep it crash-safe (never throw out of this method).
- Update the `[ExternalReposition]` log to note the body kind (vehicle/character).

## Task 2 — Add a DRIVE phase to the self-test harness (diagnose bug C)
**File:** `Stride/HrotStrideApp.Game/StrideSelfTest.cs`. After the existing SPAWN → CHECK_A → REPOSITION →
CHECK_B sequence (all in-arena), add a **DRIVE** phase that issues a single direct navigation intent and
checks whether the Stride muscle actually moves the vehicle:

- **DRIVE_ISSUE:** the entity is at B=(-7,5) after reposition. Pick an in-arena destination **D=(4,11,0)**.
  Set a single `NavigationIntent` on the entity (do NOT spawn a behavior — we bypass the brain to isolate the
  muscle): `Mode = NavigationMode.DirectPoint`, `FinalDestination = D` (FDP Cartesian, the executor copies it
  raw — no geo conversion), `TargetSpeed = 5`, `ArrivalRadius = 2`, `IntentId = 1`, `ReverseAllowed = 0`.
  Use the real component/enum names (`Fdp.Toolkit.Navigation` `NavigationIntent` + `NavigationMode`; verify
  field names against the struct). `world.SetComponent(entity, intent)`. Log
  `[SELFTEST] DRIVE_ISSUE intent → D=(4,11) IntentId=1`.
- **DRIVE_SETTLE (~240 frames):** each ~30 frames log `[SELFTEST] drive frame=N pos=(x,y) navResult=<NavigationStatus.Result> navIntentId=<status.IntentId>`
  (read `NavigationStatus` if the component is present; tolerate absence). This captures whether the muscle
  reports PathFound/InProgress/Arrived vs NoPath/Failed, and whether the position changes.
- **CHECK_DRIVE:** read final position. `distMoved = distance(finalXY, B.xy)`; `errToDest = distance(finalXY, D.xy)`.
  Verdict: `drive = PASS if errToDest <= 3.0 (arrived near D) OR distMoved >= 3.0 (made real progress toward D); else FAIL`.
  Log `[SELFTEST] CHECK_DRIVE end=(x,y) distMoved=.. errToDest=.. navResult=<..> -> PASS/FAIL`.
- Extend the final RESULT line: `[SELFTEST] RESULT initialHold=.. repos=.. drive=.. (… D=(4,11) endDrive=..)`.
- Keep the timeout/always-exit guard. The whole run must still end with `game.Exit()`.

This is DIAGNOSTIC: a FAIL with `navResult=NoPath` confirms the muscle path-planner can't path in-arena
(navmesh/PlanPath issue); a PASS means the muscle moves fine given a direct intent (so C is purely the
brain re-issuing — different fix). Either outcome is the goal — capture it cleanly.

## HARD CONSTRAINTS
- Touch ONLY `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` (Task 1) and
  `Stride/HrotStrideApp.Game/StrideSelfTest.cs` (Task 2). Nothing else. No out-of-scope edits (prior batches
  violated this — reverted + counted against you). Leave `[DIAG-POS]` logging in place.
- Do NOT attempt to fix bug C (the navmesh/path planning) or the time-control bug in this batch — Task 2 only
  DIAGNOSES C. Do NOT touch nav systems, motors, the reverse-sync, or the time hook.
- Match real APIs (`CharacterComponent.Teleport`, `NavigationIntent`/`NavigationMode`/`NavigationStatus`
  field+enum names). Verify via Grep/codebase-memory; adapt + note in report. Do NOT invent.
- Keep everything crash-safe; the self-test must always reach `game.Exit()`.

## Build / done
- Build `Stride/HrotStrideApp.Game` + `Stride/HrotStrideApp.Windows`: 0 errors, no new warnings.
- Report `.dev/stride-2/reports/BATCH-S2-I-REPORT.md` (DEV-GUIDE §4): the exact capsule-teleport call used,
  the NavigationIntent field names/enum you set, the new `[SELFTEST]` log line formats, any API adaptations.
  Do NOT commit.
