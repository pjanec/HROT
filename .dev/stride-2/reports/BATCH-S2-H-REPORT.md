# BATCH-S2-H Report — Autonomous self-test: scenario entity hold position

## Implementation Summary

Added autonomous self-test infrastructure triggered by `STRIDE_SELFTEST=1`.

**New file:** `Stride/HrotStrideApp.Game/StrideSelfTest.cs`
- A sealed class that implements the full WARMUP → SPAWN → SETTLE_A → CHECK_A → REPOSITION → SETTLE_B → CHECK_B → DONE state machine as specified.
- Registered as a `TestHarnessContext.RegisterUpdate(Func<float,bool>)` hook so it is pumped by the existing `StrideTestHarness.Update` → `TestHarnessContext.PumpUpdates` path each frame.
- Total timeout guard: exits with FAIL/reason=timeout at 1200 frames.
- Resolve timeout guard: exits with FAIL/reason=entity-not-found at 120 frames after SPAWN if entity never appears.

**Modified file:** `Stride/HrotStrideApp.Game/StrideHrotGame.cs` (minimal wiring only)
- Added `STRIDE_SELFTEST=1` env check at the top of `BootEditorSubsystem`.
- Forces `hostRealEditor = true` when `STRIDE_SELFTEST=1` (self-test needs the full hosted real-editor + Stride muscle pipeline).
- After `BuildTestHarness(scene)` (step 6), added step 6a: calls `StrideSelfTest.RegisterIfEnabled(ctx, entityMap, this)` when `selfTestEnabled && _testHarness != null && _editorSubsystem != null`.
- `STRIDE_EDITOR_WINDOW` remains gated by its own flag (defaults OFF — no raylib window needed).

No other files were touched.

---

## Exact spawn API call used

```csharp
_ctx.ScenarioSource.Enqueue(new EntityCreationRequest
{
    RequestId             = Guid.NewGuid(),
    OwnerAppInstanceId    = 0,            // localNodeId=0 → WithOwned authority granted
    TkbType               = 100L,         // Tank_M1Abrams → OrientedBox Bullet body
    PreAllocatedNetworkId = 9001L,        // deterministic netId for lookup
    InitialComponents     = new List<object>
    {
        new SimTransform { Position = new(120f, 80f, 0f), Rotation = Quaternion.Identity },
        new TkbIdentity  { TkbType = 100L },
    },
});
```

`ScenarioSource` is `EditorStrideSubsystem.ScenarioSource` (a `ScenarioEntityCreationRequestSource`), exposed on the `TestHarnessContext`. This is the identical path the scenario loader and all existing harness cases use.

`PreAllocatedNetworkId = 9001` is the key adaptation: the spec calls for looking up the entity by NetworkIdentity 9001. `EntityCreationRequest.PreAllocatedNetworkId` (documented: "CreateEntityRequestSystem uses this value directly as the entity's network ID and skips INetworkIdAllocator.AllocateId()") guarantees netId=9001 without inventing any lookup mechanism.

---

## How the spawned entity is resolved by NetworkIdentity

The `NetworkEntityMap` (exposed as `EditorStrideSubsystem.EntityMap`, a `NetworkEntityMap` singleton also registered in the world via `SetSingletonManaged`) is passed directly to `StrideSelfTest` at registration time:

```csharp
StrideSelfTest.RegisterIfEnabled(harnessCtx, _editorSubsystem.EntityMap, this);
```

Inside `StrideSelfTest.TryGetEntityHandle`:

```csharp
if (_entityMap.TryGetEntity(9001L, out var e) && _ctx.World.IsAlive(e))
```

`NetworkEntityMap.TryGetEntity(long netId, out Entity entity)` maps netId→FDP Entity via an internal `Dictionary<long, Entity>`. This is populated by `NetworkSpawningSystem` when the spawn request is processed.

---

## How the entity's SimTransform is repositioned

```csharp
world.SetComponent(entity, new SimTransform
{
    Position = new(220f, 40f, 0f),
    Rotation = currentRot,   // preserved from current state
});
```

`EntityRepository.SetComponent<T>(Entity, T)` overwrites the existing component value in-place. This is the exact same call pattern used throughout the codebase (e.g. `BulletReverseSyncSystem`, harness cases).

---

## How the process exits

```csharp
try { _game.Exit(); }
catch { System.Environment.Exit(0); }
```

`_game` is the `StrideHrotGame` instance (a `Stride.Engine.Game` subclass), passed as `this` at registration. `Game.Exit()` is Stride's clean shutdown path. `Environment.Exit(0)` is the fallback if `Exit()` throws (e.g. already in teardown).

NLog is flushed with a 2-second timeout before the exit call so the summary line is written to disk.

---

## Env / boot wiring locations

| What | Where |
|------|-------|
| `STRIDE_SELFTEST=1` check | `StrideHrotGame.BootEditorSubsystem()`, before step 4a |
| Force `hostRealEditor=true` | Same location — `bool hostRealEditor = selfTestEnabled \|\| …` |
| Self-test registration | `StrideHrotGame.BootEditorSubsystem()`, step 6a (after `BuildTestHarness`) |
| Per-frame pump | `TestHarnessContext.PumpUpdates(dt)` called by `StrideTestHarness.Update(dt)` called by `StrideHrotGame.Update(GameTime)` |

The `HrotStrideApp.Windows` launcher (`HrotStrideAppApp.cs`) required no changes: it calls `game.Run()` unconditionally and `StrideHrotGame.BeginRun` handles all boot logic.

---

## Exact `[SELFTEST]` log line formats

All via `NLog.LogManager.GetLogger("StrideSelfTest").Info(…)`, landing in `logs/editor_stride.log`.

```
[SELFTEST] Self-test registered (STRIDE_SELFTEST=1). Entering WARMUP.
[SELFTEST] SPAWN tkb=100 netId=9001 at A=(120,80)
[SELFTEST] track A frame=N pos=(x,y)           ← every 30 frames during SETTLE_A
[SELFTEST] CHECK_A end=(x,y) errA=.. driftToOrigin=.. -> PASS/FAIL
[SELFTEST] REPOSITION to B=(220,40)
[SELFTEST] track B frame=N pos=(x,y)           ← every 30 frames during SETTLE_B
[SELFTEST] CHECK_B end=(x,y) errB=.. driftToOrigin=.. -> PASS/FAIL
[SELFTEST] RESULT initialHold=PASS/FAIL repos=PASS/FAIL errA=.. errB=.. (A=(120,80) endA=(x,y) B=(220,40) endB=(x,y))
[SELFTEST] Exiting process.
```

Timeout paths:
```
[SELFTEST] RESULT initialHold=FAIL repos=FAIL reason=entity-not-found
[SELFTEST] RESULT initialHold=FAIL repos=FAIL reason=timeout
```

---

## Design Decisions

1. **Passed `EntityMap` directly** instead of calling `world.GetSingletonManaged<NetworkEntityMap>()`. `EditorStrideSubsystem.EntityMap` is already a public property; passing it by reference is safer and avoids dependency on the singleton infrastructure. The `NetworkEntityMap` is the same object registered as the singleton — no inconsistency.

2. **`Stride.Engine.Game` not `Stride.Games.Game`** — the project uses `Game` from `Stride.Engine` (the project's `StrideHrotGame : Game` base). Initial use of `Stride.Games.Game` caused CS0234 which was fixed by adding `using Stride.Engine` and using the unqualified `Game`.

3. **`Fdp.Core.Entity` qualified** — `Stride.Engine` and `Fdp.Core` both define `Entity`. The `TryGetEntityHandle` out-parameter and return type are explicitly qualified as `Fdp.Core.Entity` to avoid CS0104.

4. **No changes to `HrotStrideApp.Windows`** — the launcher just calls `game.Run()`. All boot logic is in `StrideHrotGame.BootEditorSubsystem`, which is the correct single entry point.

5. **No demo spawns skipped** — `EnqueueDemoSpawns()` still runs before the self-test registration. The demo entities (TkbType 2001/2002) don't interfere: they have auto-allocated netIds (not 9001) and spawn at FDP Y=5–7, well away from PosA=(120,80) and PosB=(220,40).

---

## Deviations

None. All APIs matched real implementations exactly. `PreAllocatedNetworkId` was the intended mechanism for deterministic netId allocation (per its docstring). No invented APIs.

---

## Test Results

No headless unit tests were added (the spec marks this as optional). The self-test is integration-level and requires the GPU app. The build is the verification gate here:

```
HrotStrideApp.Game:    0 errors, 0 new warnings (pre-existing CS0108 on StrideHrotGame.Log)
HrotStrideApp.Windows: 0 errors, 0 new warnings (pre-existing bone/shader AssetCompiler warnings)
```

---

## Developer Insights

- **The glide bug will likely produce FAIL on both checks.** `errA` and `errB` will be large (tens–hundreds of metres toward origin). The `driftToOrigin` field in the log will show the characteristic value — if near `sqrt(120²+80²) ≈ 144` for check A, the entity is indeed near origin (full drift). This is the expected baseline before a fix.

- **`BulletReverseSyncSystem` writes `SimTransform` every frame** when the body is active. The REPOSITION step writes `SimTransform` directly via `world.SetComponent`. Whether this write "sticks" before `BulletReverseSyncSystem` overwrites it the next frame is exactly what the reposition check measures. The tolerance of 5 m is generous — a working reposition sits at ~0 m error; the bug produces tens–hundreds of m.

- **Frame counts are fixed, not wall-clock.** At 60 FPS: WARMUP=0.5 s, SETTLE_A=2.5 s, SETTLE_B=2 s, total cap=20 s. At 30 FPS they double. The total timeout guard (1200 frames) is generous enough for low frame rates.

- **`NLog.LogManager.Flush(TimeSpan.FromSeconds(2))`** before `game.Exit()` ensures the summary line is on disk. Without this, in a fast exit the async NLog file appender may not have flushed.

---

## Known Issues

- None introduced by this batch.
- The pre-existing CS0108 warning (`StrideHrotGame.Log` hides `GameBase.Log`) is unchanged; it is not caused by this batch.

---

## Suggested Commit Message

```
feat(stride-selftest): BATCH-S2-H — autonomous STRIDE_SELFTEST=1 harness (spawn + hold + reposition verdict)
```
