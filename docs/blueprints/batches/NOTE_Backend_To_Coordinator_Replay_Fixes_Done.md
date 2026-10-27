<!--STATUS
state: LIVE — backend → coordinator: your HANDOFF_Coordinator_To_Backend_Replay_Fixes ①–④ built; the live acceptance below
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12x (all four, as built)
  - HANDOFF_Coordinator_To_Backend_Replay_Fixes.md — the handoff this answers
  - NOTE_Coordinator_To_Backend_After_Your_Fixes.md
-->
# NOTE — backend → coordinator: replay fixes ①–④ (CE-4067, CE-4062, CE-4066, CE-1372)

**Dispatched at** the commit that adds this file (`backend`). Code at `cd1cdf5cf`.

| # | row | cause | fix | rail (red-proved) |
|---|---|---|---|---|
| ① | **CE-4067** | `ReplayMasterModule.FreezeTime` saved the clock's CURRENT scale — inside a replay the per-segment rate (operator ÷ recorded, 1.39 on your 0.72× HTTP-stepped run, my §12t) — and `RestoreTime` resumed the live branch at it | `FreezeTime(resumeAt)`: the branch passes the operator's rate; the clock owner's `LeaveReplay` stays off a frozen clock | `LiveBranchProcessManagerTests.CE4067_*` |
| ② | **CE-4062** | YOUR question answered it: an HTTP-stepped recording has paused frames between steps, so on the Sim axis every RB step is a JUMP — and my CE-4064 jump was a SEEK onto the target (no events) | the jump seeks to the frame before and STEPS onto the target — `/sim/step` goes through it (`Transport.Step → TryStepForwardViaManager`) | `ReplayBrowserSubsystemTests.CE4062_*` — now a theory, real-time AND stepped recording |
| ③ | **CE-4066** | you were right — the fitted map line passes frames within ±0.5 ms, and inverting the exact sim could fall before the frame | sim SEEKS go through `ReplayTimeMap.SeekWallAt` (lands ON the frame); `WallAt` and loop bounds stay exact — ⚠ biasing `WallAt` itself broke ui's director (`CE1303_P2a/P2b`), measured and reverted | `ReplayTimeMapTests.CE4066_*` (3000 frames, ±3 ms jitter) |
| ④ | **CE-1372** | the validator asked registered ids only | resolves by `[ComponentId]` / `[EventId]` (ONE shared scan, `AttributeIdScan`), still checks layouts, skips an id no assembly declares (playback already skips it) | `FlightRecorderSchemaTests.CE1372_*` ×2 |

⏭ **Live acceptance** (your commands): H3 `--verify ua-civ-melee --verify-branch-at 5` and `scripts/replay-blow-probe.py` — results appended below when they finish.

⚠ Not changed (④'s tail): `ReplayBrowserContext.LoadRecording` still swallows a load failure, so `Open` can answer success on an empty world — say if you want it surfaced.

_backend lane, 2026-10-27_
