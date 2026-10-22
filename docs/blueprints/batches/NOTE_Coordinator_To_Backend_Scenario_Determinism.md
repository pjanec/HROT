<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note — §2 is the open item, §3 what is already done
related-designs:
  - ../../DESIGN_Scenario_Regression.md — the scenario verifier that measured these (§7)
  - ../DESIGN_Paused_And_Stepped_Execution.md — owns why a stepped run repeats (CE-3293, §7e) and the reload leak (CE-3294)
-->

# NOTE — coordinator → backend: what the scenario verifier found about determinism (CE-4044; CE-4043 done)

🔒 **User, `2026-10-22`:** *"Note backend is solving determinism issues so hand those to them."*

## 1. The tool — use it as your rail

The scenario verifier ([CE-4042](../Blueprint_Issues_Tracker.md), [design](../../DESIGN_Scenario_Regression.md)) runs a scenario in a FRESH editor
process, stepped once per frame, and compares the whole world with a recorded **golden**: every named unit, every component as a hash,
every 5–10 s. A difference names the FIRST snapshot, the unit and the component.

```bash
dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode editor --headless --verify bt-grenade-posture
# to see VALUES, not hashes: a scratch copy of the scenario with  "golden": {"every": 2, "full": true}  in its checks.json
```

## 2. ⛔ OPEN — CE-4044: `MapLayerAssignmentSystem` writes a component on a WALL-CLOCK schedule

| | |
|---|---|
| **measured** | `bt-grenade-posture`, the same code, two fresh processes: the golden DIFFERS at t = 2 s — `MapDisplayComponent` present on `Prone`, `Standing`, `Thrower` in one run, absent in the other. The second run was on a loaded machine (four other processes) |
| **why** | `Hrot/Engine/Hrot.Presentation/Map/MapLayerAssignmentSystem.cs` is `[TickMode(EveryFrame)]` and time-sliced by the STOPWATCH: a 1 ms budget per frame (`PerFrameBudgetMs`, `QueryTimeSliced`) and a 3 s rescan by `Stopwatch.GetTimestamp()` (`RescanIntervalSeconds`). It `AddComponent`s / `SetComponent`s `MapDisplayComponent` (`:123`, `:127`) — so WHEN a unit gets it depends on the machine's speed |
| **effect** | behaviour-neutral as far as I can see (a 2-D map layer mask), but the world is not reproducible: a feature scenario's golden flaps with machine load. ⚠ The golden of `bt-grenade-posture` is red until this is fixed — I am NOT exempting the component (Regression_Net N1: never normalise to hide non-determinism) |
| **lean (yours to decide)** | keep the view's cache out of the sim world's schedule: assign the mask once when the entity appears (one pass, not budgeted), or key the rescan on sim frames; or hold the mask in the presentation's own cache instead of a world component |
| **rail** | verify `bt-grenade-posture` twice in fresh processes, one of them under load — SAME both times |

## 3. ✅ Already done — do not redo

| | |
|---|---|
| **CE-4043** (fixed by coordinator, cross-lane, one line) | `MissionAdapterState.LastPlanVersion` folded `string.GetHashCode()`, which .NET randomises per process — 83239098 · 2673561298 · 1681038897 in three fresh runs of `ua-civ-routine-alarm`. `MissionAdapterSystem.cs:97` now uses `In.Fnv1a32` (the one string hash, `UtilityIdHash`). `MissionAdapterSystemTests` 4/4 |
| the same fold, left for you (compared only inside one process, so harmless today) | `StaticObstacleBake` signature (`Material.Name.GetHashCode()`), `BlueprintEventDispatch.ResolveUncached` (the untyped fallback, unreached since every event carries `[EventId]`), `ClusterDiagnosticsPanel` (an ImGui id). ⭐ A rail banning `string.GetHashCode()` in anything that reaches a component would keep it from coming back |

## 4. FYI — measured, already yours, no new item

| | |
|---|---|
| a RELOAD differs from a first load | an HTTP re-record in an editor that had loaded the scenario once: `SopState` / `SopStartRecord` and `CharacterAnimationDefRuntime` differ — the CE-3294 / §7e gap. The verifier now refuses a golden from a reload and runs `{fresh: true}` in a child process |
| HTTP-stepped in a window vs one-step-per-frame headless | the M1's first kinetic round in `ua-tank-ammo`: 8.42 s (Python, HTTP 15 steps a request, Xvfb window) vs 8.53 s (headless, one step per frame) — not run down; goldens are compared only with the same command |
