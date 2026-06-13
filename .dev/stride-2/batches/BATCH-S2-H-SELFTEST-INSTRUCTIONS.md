# BATCH-S2-H — Autonomous self-test: scenario entity must hold position (initial + reposition)

**Topic dir:** `.dev/stride-2/` · **Guide:** `.dev/.guides/DEV-GUIDE_claude.md` · **Mode:** sonnet.
Build the affected projects (0 errors). This batch adds an env-triggered **self-test** so the Lead can run
the real GPU app fully autonomously (launch → it spawns a vehicle, checks it holds position, simulates a
reposition, writes a verdict, exits) — no human in the loop.

## Why
In hosted mode (`STRIDE_HOST_REAL_EDITOR=1`) a loaded scenario vehicle (dynamic Bullet body) **glides to
FDP origin** over the first frames, and an external reposition (operator drag → `SimTransform` changed) also
**snaps to origin**. The fixes so far (UpdateWorldMatrix-before-Add, first-ready slam, `UpdatePhysicsTransformation`)
did NOT resolve it. We need a deterministic, autonomous reproduction to iterate the real fix. This batch
builds ONLY the test harness — NOT a fix. Do not attempt to fix the glide here.

## What to build
A new self-test that runs inside the real app and reproduces both failures with a clear PASS/FAIL verdict.

### 1. Env trigger
- New env var `STRIDE_SELFTEST=1`. When set, the app runs the self-test automatically and exits when done.
- The self-test requires the hosted real-editor + Stride muscle path. In the Windows launcher
  (`Stride/HrotStrideApp.Windows`) and/or `StrideHrotGame` boot, when `STRIDE_SELFTEST=1`, ensure
  `STRIDE_HOST_REAL_EDITOR` behaves as if `=1` (hosted path). `STRIDE_EDITOR_WINDOW` should default OFF for
  the self-test (no raylib editor window needed — we only need the 3D Stride window + physics).
- Do NOT change default behavior when `STRIDE_SELFTEST` is unset.

### 2. Self-test driver (new file, e.g. `Stride/HrotStrideApp.Game/StrideSelfTest.cs`)
Drive it via `TestHarnessContext.RegisterUpdate(Func<float,bool> hook)` (per-frame; return `true` to keep
running, `false` to unregister) — register it during boot when `STRIDE_SELFTEST=1`, AFTER the editor
subsystem + test harness + navmesh are ready (the same place `BuildTestHarness`/`BakeNavmesh` run in
`StrideHrotGame`). The hook is a frame-counted state machine (all positions FDP unless noted; the editor
2D-map / SimTransform is FDP X,Y — that is what we assert; ignore Z/height):

- **WARMUP** (~30 frames): do nothing (let scene/physics/navmesh settle).
- **SPAWN**: enqueue ONE vehicle via `ctx.ScenarioSource.Enqueue(...)` — a TkbType that maps to an
  `OrientedBox` body (TkbType `100` = Tank_M1Abrams; the editor TkbDb has it, and BATCH-S2-E gave it a
  Stride render-def). Spawn it at a clearly-non-origin FDP position **A = (120, 80, 0)** with a known
  `NetworkIdentity` you can find again (pick an unused id, e.g. 9001). Record A. Log
  `[SELFTEST] SPAWN tkb=100 netId=9001 at A=(120,80)`.
- **SETTLE_A** (~150 frames): wait. Each frame, if you can resolve the spawned entity, you MAY log its
  SimTransform every ~30 frames as `[SELFTEST] track A frame=N pos=(x,y)` (helps the Lead see the glide
  curve).
- **CHECK_A**: resolve the spawned entity (by `NetworkIdentity==9001`, via `NetworkEntityMap` or a World
  query for SimTransform+VehicleState whose NetworkIdentity matches). Read `SimTransform.Position`. Compute
  `driftToOrigin_A = distance((x,y),(0,0))` and `errA = distance((x,y), A.xy)`.
  Verdict: `initialHold = PASS if errA <= 5.0 m else FAIL`. Log
  `[SELFTEST] CHECK_A end=(x,y) errA=.. driftToOrigin=.. -> PASS/FAIL`.
- **REPOSITION**: externally set the entity's `SimTransform.Position` to **B = (220, 40, 0)** via
  `world.SetComponent(entity, new SimTransform{Position=B(stride-agnostic FDP), Rotation=current})` — this
  mimics the operator drag writing SimTransform. Log `[SELFTEST] REPOSITION to B=(220,40)`.
- **SETTLE_B** (~120 frames): wait (optionally track as above).
- **CHECK_B**: read `SimTransform.Position`. `errB = distance((x,y), B.xy)`,
  `driftToOrigin_B = distance((x,y),(0,0))`.
  Verdict: `repos = PASS if errB <= 5.0 m else FAIL`. Log
  `[SELFTEST] CHECK_B end=(x,y) errB=.. driftToOrigin=.. -> PASS/FAIL`.
- **DONE**: log a single summary line the Lead will grep:
  `[SELFTEST] RESULT initialHold=PASS/FAIL repos=PASS/FAIL errA=.. errB=.. (A=(120,80) endA=.. B=(220,40) endB=..)`
  then exit the process cleanly: prefer `game.Exit()` if reachable, otherwise `System.Environment.Exit(0)`.
- **TIMEOUT guard**: if the entity can't be resolved within ~120 frames after SPAWN, or the whole run
  exceeds ~1200 frames, log `[SELFTEST] RESULT initialHold=FAIL repos=FAIL reason=<entity-not-found/timeout>`
  and exit. The app MUST always exit (never hang) when `STRIDE_SELFTEST=1`.

Notes:
- Use the SAME spawn path the scenario loader uses (`ScenarioSource.Enqueue`) so the body goes through the
  identical `CreateEntityRequestSystem → NetworkSpawningSystem → translators → PhysicsBodyLifecycleSystem →
  BulletPhysicsBodyService.CreateBody` pipeline. Do NOT shortcut it.
- Position assertions are FDP X,Y only (the glide is horizontal; the 3D fall in Z is expected/irrelevant).
- Tolerance 5.0 m is generous — a correct hold sits at ~0 m error; the bug produces tens-to-hundreds of m.
- Keep all `[SELFTEST]` logs at `Log.Info` (NLog) so they land in `logs/editor_stride.log`.

## HARD CONSTRAINTS
- This batch is TEST INFRASTRUCTURE ONLY. Do NOT modify the physics fix code
  (`BulletPhysicsBodyService.cs`, `PhysicsBodyLifecycleSystem.cs`, `BulletReverseSyncSystem.cs`,
  `SplitAuthorityStrideSyncScript.cs`, motors). Do NOT try to fix the glide. Leave the existing
  `[DIAG-POS]` logging in place.
- New code goes in: a new `StrideSelfTest.cs` + minimal boot wiring in `StrideHrotGame.cs` (env check +
  RegisterUpdate) + minimal launcher env handling in `Stride/HrotStrideApp.Windows` if needed. Touch nothing
  else. NO out-of-scope edits (prior batches violated this — it will be reverted).
- Match real APIs (`ScenarioEntityCreationRequestSource.Enqueue` signature, `NetworkEntityMap`,
  `EntityRepository.SetComponent`, how to reach `Game.Exit`). Read them via codebase-memory/Grep; adapt to
  reality and note adaptations in the report. Do NOT invent.

## Build / done
- Build `Stride/HrotStrideApp.Game` + `Stride/HrotStrideApp.Windows` (and Hrot.Stride.Core if touched): 0 errors.
- No need to run the GPU app (the Lead will). You MAY add a tiny headless unit test of any pure helper you
  extract (e.g. the distance/verdict calc), but it's optional.
- Report `.dev/stride-2/reports/BATCH-S2-H-REPORT.md` (DEV-GUIDE §4): exact spawn API used, how you resolve
  the spawned entity, how you exit the process, the env wiring, and the exact `[SELFTEST]` log lines emitted.
  Do NOT commit.
