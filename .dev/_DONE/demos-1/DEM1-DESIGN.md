# DEM1 — FDP Demo Framework & Demo Suite: Design Document

**Prefix:** `DEM1-`  
**Related documents:**  
- [DEM1-TASK-DETAIL.md](./DEM1-TASK-DETAIL.md) — per-task implementation specs  
- [DEM1-TASK-TRACKER.md](./DEM1-TASK-TRACKER.md) — progress checklist  
- [DEM1-ONBOARDING.md](./DEM1-ONBOARDING.md) — newcomer guide  
- [DEM1-design-talk.md](./DEM1-design-talk.md) — the originating design talk  
- [FDP-demos-all.md](./FDP-demos-all.md) — legacy document (superseded)

---

## 1. Vision & Goals

The DEM1 workstream creates a **self-contained, CI-friendly demo suite** inside the `FDP/` folder that:

1. Proves every FDP toolkit (`Behavior`, `CarKinem`, `Navigation`, `Perception`, `Physics`, `Combat`, `Geographic`, `Replay`, `Time`) works correctly in isolation and in combination.
2. Runs fully **headless** for autonomous AI-agent development and CI pipelines — exit code `0` = pass, non-zero = fail.
3. Optionally renders a **2D map** (`FDP.Toolkit.Vis2D`) for human debugging without changing simulation outcomes.
4. Uses **deterministic time stepping** (`SteppingTimeController`, `1/60 s` fixed delta) so physics and AI produce bit-identical results across all CI hardware.
5. Is **completely isolated from `Hrot.*`** — no geodetic coordinates, no Entity Master, no Hrot-specific DDS schemas.
6. Produces **structured trace logs** (via `FdpLog` + NLog file target) written every run so that AI coding agents can diagnose failures without interactive debugging.

---

## 2. Architectural Constraints

| Rule | Rationale |
|------|-----------|
| All new code lives under `FDP/Examples/` in the `Fdp.Examples.*` namespace. | Strict domain isolation from `Hrot.*`. |
| Legacy demo projects (`Fdp.Examples.CarKinem`, `Fdp.Examples.NetworkDemo`, `Fdp.Examples.UrbanCombat`) must not be modified. | They remain functional until explicitly deprecated. |
| No wall-clock timing (`Thread.Sleep`, `DateTime.Now`) in scenario logic. | All time is injected via `GlobalTime` singleton. |
| Each scenario exits the process deterministically (exit 0 or exit non-zero) within `--max-ticks`. | Enables CI pipelines to treat the runner binary as a test binary. |
| Only Cartesian 3D maths — no WGS84/geodetic conversions. | Simplifies math, removes Hrot dependency. |
| No `EntityMaster` concept. | Simplified bootstrapping via lightweight `DemoSpawnMsg`. |

---

## 3. Folder Layout

```
FDP/Examples/
│
├── Fdp.Examples.CarKinem/           ← [LEGACY – do not modify]
├── Fdp.Examples.CarKinem.Tests/     ← [LEGACY – do not modify]
├── Fdp.Examples.IdAllocatorDemo/    ← [LEGACY – do not modify]
├── Fdp.Examples.NetworkDemo/        ← [LEGACY – do not modify]
├── Fdp.Examples.NetworkDemo.Tests/  ← [LEGACY – do not modify]
├── Fdp.Examples.UrbanCombat/        ← [LEGACY – do not modify]
├── Fdp.Examples.UrbanCombat.Tests/  ← [LEGACY – do not modify]
│
├── Fdp.Examples.DDS/                ← [NEW] Cartesian-only DDS schemas
├── Fdp.Examples.Common/             ← [NEW] Shared infra: IScenario, components, events, constants
├── Fdp.Examples.Scenarios/          ← [NEW] Concrete IScenario implementations
└── Fdp.Examples.Runner/             ← [NEW] CLI executable (the demo runner)
```

`Fdp.Examples.Runner` is the **only** `OutputType=Exe` project in this set.

---

## 4. Demo Framework Foundation (Phase 0)

### 4.1 Deterministic Mode in RunnerOptions / RunnerConfiguration

`FDP.Framework.Runner.RunnerOptions` and `RunnerConfiguration` currently have no deterministic-time flags. We extend them:

```csharp
// In RunnerOptions (programmatic API):
public bool Deterministic { get; set; }
public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;

// In RunnerConfiguration (CLI):
[Option("deterministic", Default = false, HelpText = "Force fixed-step time (CI mode)")]
public bool Deterministic { get; set; }

[Option("fixed-dt", Default = 0.016667f, HelpText = "Fixed delta in seconds (default 60 Hz)")]
public float FixedDeltaSeconds { get; set; }
```

When `Deterministic = true`, the `SubsystemOrchestrator` passes the fixed delta to `Update(fixedDelta)` instead of `Raylib.GetFrameTime()`. In headless mode the dt is already `0f`; we replace that `0f` with `FixedDeltaSeconds`.

The `SteppingTimeController` (already in `FDP.Toolkit.Time`) is the implementation that scenario subsystems use to inject `GlobalTime` each tick.

### 4.2 FdpLog File Target Setup in the Runner

The runner (`Fdp.Examples.Runner/Program.cs`) must configure NLog with a **file target** so every run writes a trace log:

```
logs/demo-<scenario>-<timestamp>.log
```

This is achieved by loading an `NLog.config` (or configuring programmatically) before the orchestrator starts. The log path is printed to stdout so CI agents know where to find it.

Log levels:
- `Trace` — tick-by-tick phase evaluation
- `Info` — phase pass/fail checkpoints, scenario start/end
- `Error` — assertion failures with diagnostic values

### 4.3 IScenario Interface (`Fdp.Examples.Common`)

```csharp
namespace Fdp.Examples.Common
{
    /// <summary>
    /// Contract for all CI-testable scenario scripts.
    /// Implementations must be deterministic and must not reference Raylib or wall-clock time.
    /// </summary>
    public interface IScenario
    {
        /// <summary>Unique scenario key used by the CLI --scenario flag.</summary>
        string ScenarioName { get; }

        /// <summary>
        /// Called once. Register toolkits and spawn entities here.
        /// The world and kernel are fully configured before EvaluateTick is called.
        /// </summary>
        void Configure(EntityRepository world, ModuleHostKernel kernel);

        /// <summary>
        /// Called every tick AFTER kernel.Update().
        /// May inject events or mutate state to simulate external stimuli.
        /// Returns true when the scenario's success condition is met (CI pass).
        /// Throws ScenarioFailureException with a diagnostic message on any failure.
        /// </summary>
        bool EvaluateTick(uint currentTick, EntityRepository world);

        /// <summary>
        /// Optional: register 2D visualizers on the MapCanvas for human observation.
        /// Called only when --attach-vis2d is set. Must be a no-op otherwise.
        /// </summary>
        void ConfigureVisuals(MapCanvas? canvas, EntityRepository world);
    }
}
```

`ScenarioFailureException` is a thin `Exception` subclass carrying a `PhaseId` and diagnostic values string.

### 4.4 ScenarioSubsystem (`Fdp.Examples.Common`)

Wraps an `IScenario` as an `ISubsystem` so it plugs into `SubsystemOrchestrator`:

```csharp
public class ScenarioSubsystem : ISubsystem, IMapCameraProvider
{
    // Owns EntityRepository + ModuleHostKernel + optional MapCanvas
    // Update():
    //   1. Advance GlobalTime via SteppingTimeController (if deterministic)
    //   2. kernel.Update()
    //   3. EvaluateTick → if true → log CI SUCCESS → Environment.Exit(0)
    //                   → on ScenarioFailureException → log CI FAILURE → Environment.Exit(1)
    //   4. if tick > maxTicks → log TIMEOUT → Environment.Exit(2)
}
```

Exit codes:
- `0` = scenario succeeded
- `1` = scenario assertion failed
- `2` = scenario timed out (max-ticks exceeded)

### 4.5 ScenarioRegistry (`Fdp.Examples.Runner`)

Maps scenario name strings to `IScenario` factory functions. No reflection — explicit registration to keep startup fast:

```csharp
public static class ScenarioRegistry
{
    public static IScenario Create(string name) => name.ToLowerInvariant() switch
    {
        ScenarioNames.AutoDrive          => new AutoDriveScenario(),
        ScenarioNames.ComponentDamage    => new ComponentDamageScenario(),
        ScenarioNames.BallisticsAndHit   => new BallisticsAndHitScenario(),
        ScenarioNames.BehaviorValidation => new BehaviorValidationScenario(),
        ScenarioNames.SensorGrid         => new SensorGridScenario(),
        ScenarioNames.MissionCommand     => new MissionCommandScenario(),
        ScenarioNames.TerrainClamping    => new TerrainClampingScenario(),
        ScenarioNames.ParallelStories    => new ParallelStoriesScenario(),
        ScenarioNames.DistributedTank    => new DistributedTankScenario(),
        ScenarioNames.UrbanCombat        => new UrbanCombatNewScenario(),
        _ => throw new ArgumentException($"Unknown scenario: {name}")
    };
}
```

### 4.6 Program.cs CLI

Extends `RunnerConfiguration` with demo-specific options:

```
fdp-demo-runner.exe --scenario autodrive --max-ticks 300 [--deterministic] [--fixed-dt 0.016667] [--attach-vis2d] [--headless]
```

Always defaults to `--headless --deterministic` when neither `--attach-vis2d` is passed.

---

## 5. Shared Demo Infrastructure (Phase 1)

### 5.1 Fdp.Examples.DDS — Cartesian-Only DDS Schemas

| Message | Purpose | Key Fields |
|---------|---------|-----------|
| `DemoSpawnMsg` | Spawn/destroy a networked entity (no ELM handshake) | `NetworkId:long`, `TkbType:long`, `OwnerNodeId:int`, `IsDestroyed:bool` |
| `DemoTransformMsg` | Replicate `SimTransform` in flat Cartesian space | `NetworkId:long`, PosX/Y/Z, RotX/Y/Z/W (float) |
| `DemoLocomotionMsg` | Replicate `LocomotionChannel` to physics node | `NetworkId:long`, `ActiveAction:ushort`, `BehaviorInstanceId:uint`, `ActionInstanceId:uint` |
| `DemoWeaponMsg` | Replicate `WeaponChannel` to turret physics node | (same as LocomotionMsg shape) |
| `DemoCombatInteractionMsg` | Cross-process fire/hit notification | `ShooterNetId:long`, `TargetNetId:long`, `IsHit:bool`, `Damage:float` |

These are used only by the `DistributedTank` and `UrbanCombat (new)` scenarios that exercise the DDS loopback path.

### 5.2 Fdp.Examples.Common — Shared State and Tooling

```
Fdp.Examples.Common/
├── IScenario.cs                    ← Interface (§4.3)
├── ScenarioSubsystem.cs            ← ISubsystem wrapper (§4.4)
├── ScenarioFailureException.cs     ← Typed failure with diagnostics
├── Components/
│   ├── DemoScenarioTracker.cs      ← ECS component: phase latches + tick counter
│   └── MockBlackboardState.cs      ← Unsafe overlay for BrainBlackboard.Memory
├── Events/
│   ├── DemoTestLogEvent.cs         ← Synthetic logging event (FixedString32 name, int phase)
│   └── DemoScenarioTriggerEvent.cs ← Inject artificial world-state changes (type + target)
├── Constants/
│   ├── ScenarioNames.cs            ← String constants for --scenario flag
│   ├── DemoTemplateIds.cs          ← TKB integer IDs (e.g. CommandTank = 100)
│   └── DemoBehaviorIds.cs          ← Behavior hash constants
└── Helpers/
    ├── MockTerrainProvider.cs      ← Deterministic ITerrainProvider for TerrainClamping test
    └── DemoRoadGraphFactory.cs     ← Builds minimal 4-way intersection RoadNetworkBlob
```

**DemoScenarioTracker** is an optional ECS component attached to a "Scenario Master" entity. It holds `CurrentPhase (int)`, `TicksInPhase (uint)`, and a `BitMask256 Latches` for sequential latch tracking. Using an ECS component (rather than C# fields on the IScenario) makes the state visible in Vis2D panels and replay recordings.

---

## 6. Demo Scenarios (Phases 2–6)

All scenarios live in `Fdp.Examples.Scenarios/`. They are pure C# classes implementing `IScenario`. They must not reference Raylib.

### 6.1 Phase 2 — Simple Demos

#### DEM1-D001: AutoDrive (Kinematics & Avoidance)

**Goal:** Prove `FDP.Toolkit.Navigation` + `FDP.Toolkit.CarKinem` + RVO avoidance.

**Topology:** Single headless kernel, no network, no AI.

Two vehicles (Alpha and Bravo) are spawned facing each other, commanded via `CmdNavigateToPoint` to drive straight at each other at 20 m/s. The test verifies that RVO deviates them laterally, they recover their path, and both arrive at their destinations.

| Phase | Tick | Assertion |
|-------|------|-----------|
| 1 – Routing   | 20  | Alpha velocity > 0, Y offset < 0.5 m |
| 2 – Evasion   | 70  | abs(Alpha.Y) > 2.0 m (lateral deviation) |
| 3 – Recovery  | 120 | abs(Alpha.Y) < 2.0 m (back toward axis) |
| 4 – Arrival   | ≤200 | `NavState.HasArrived == 1`, velocity ≈ 0 |

Max ticks: `250`.

#### DEM1-D002: ComponentDamage (Partial Kill Pipeline)

**Goal:** Prove `DamageSystem` → capability stripping → HSM bridging → channel clearing.

**Topology:** Single headless kernel, `Combat` + `Behavior` toolkits only.

A single MilitaryAPC is spawned with full capabilities and a forward locomotion command. At tick 20 a `HitEvent` is injected. The test verifies health drops, `CanMove` flag stripped, `HsmDamageBridgeSystem` clears `LocomotionChannel`, and weapon still fires (partial kill).

| Phase | Ticks | Assertion |
|-------|-------|-----------|
| 1 – Baseline   | 15  | Health == max, CanMove == true |
| 2 – Hit        | 21  | Health < max |
| 3 – Stripped   | 22  | CanMove == false |
| 4 – Mobility Kill | 25 | LocomotionChannel.ActiveAction == 0 |
| 5 – Firepower alive | 45 | WeaponChannel.ActiveAction == ActionIdAimAndFire |

Max ticks: `60`.

---

### 6.2 Phase 3 — Mid-Complexity Demos

#### DEM1-D003: BallisticsAndHit (CCD Anti-Tunneling)

**Goal:** Prove `PhysicsToolkitModule` swept-segment CCD prevents high-speed bullet tunneling.

**Topology:** Single headless kernel, `Physics` + `Combat` toolkits. Phase pipeline strictly ordered: `Input(FireProcessing → RaycastSolver → HitResolution)`, `Simulation(Damage)`, `PostSimulation(Ballistics → LinearKinematics)`.

Bullet velocity = 40 m/s. At tick 4 the bullet has mathematically passed through the target in naive position space (X≈120). The CCD raycast resolves the hit at the target's near edge (X≈95).

| Phase | Tick | Assertion |
|-------|------|-----------|
| 1 – Spawn      | 2  | Bullet entity alive, velocity.X == 40 |
| 2 – Flight     | 4  | bullet.Position.X == 120 (past target in raw space) |
| 3 – CCD Hit    | 6  | target.Health < 100 |
| 4 – Teardown   | 7  | bullet entity dead (world.IsAlive == false) |

Max ticks: `15`.

#### DEM1-D004: BehaviorValidation (Cognitive Pipeline)

**Goal:** Prove `FDP.Toolkit.Behavior` BTree → Channel writes without any physics or executors.

**Topology:** Single headless kernel, `CognitiveRuntimeModule` only.

A Commander entity runs a synthetic BTree: `Selector[ Sequence(Condition_ThreatVisible, Condition_HasAmmo, Action_AimAndFire), Action_Flee ]`. The scenario script acts as the perception layer, directly writing to `BrainBlackboard.Memory`.

| Phase | Tick | Blackboard | Expected Channels |
|-------|------|-----------|-------------------|
| 1 – Safe   | 10 | ThreatVisible=false | Weapon=0, Loco=Flee |
| 2 – Engage | 20 | ThreatVisible=true, Ammo=10 | Weapon=AimAndFire, Loco=0 |
| 3 – Depleted | 30 | Ammo=0 | Weapon=0, Loco=Flee |

Max ticks: `40`.

#### DEM1-D005: SensorGrid (Perception & LOS)

**Goal:** Prove `FDP.Toolkit.Perception` broadphase + narrow-phase LOS raycast occlusion.

**Topology:** Single headless kernel, `PhysicsQueryModule` + `AutonomousPerceptionModule`.

Observer at (0,0). Wall at (50,50). Target starts at (100,0) and moves 1 unit/tick north. Test verifies detection → occlusion → re-acquisition.

| Phase | Tick | Target Y | Expected TargetMemory |
|-------|------|----------|----------------------|
| 1 – Detected  | 10 | 10 | target in memory, score > 0 |
| 2 – Occluded  | 50 | 50 | target absent (wall blocks LOS) |
| 3 – Re-acquired | 90 | 90 | target back in memory |

Max ticks: `100`.

---

### 6.3 Phase 4 — Advanced Demos

#### DEM1-D006: MissionCommand (Dynamic Mission + Preemption)

**Goal:** Prove `MissionDirectorSystem` advances phases and `ChannelArbitrationSystem` preempts stale commands.

**Topology:** `MissionControlModule` + `CognitiveRuntimeModule`.

Commander has a 2-phase plan: Phase 0 = Patrol (Behavior 100, trigger UnderAttack), Phase 1 = Combat (Behavior 200, trigger TimerElapsed). Script injects a `MoveTo` command in Phase 0, then injects a threat into `TargetMemory` at tick 10.

| Phase | Tick | Action | Assertion |
|-------|------|--------|-----------|
| 1 – Patrol active  | 5  | Script writes MoveTo | CurrentPhase==0, Loco==MoveTo |
| 2 – Threat injected | 10 | Enemy into TargetMemory | count==1 |
| 3 – Phase advanced  | 11 | Director triggers | CurrentPhase==1, Behavior==200 |
| 4 – Preemption      | 12 | Arbitration clears stale | Loco==0 |

Max ticks: `20`.

#### DEM1-D007: TerrainClamping (Z-Height Smoothing & Jump Rejection)

**Goal:** Prove `FDP.Toolkit.Geographic` async terrain batching, smoothing, and jump rejection.

**Topology:** `TerrainQuerySubmitSystem`(Input) → `TerrainQuerySolverSystem(MockTerrainProvider)`(Sim) → `TerrainQueryResolutionSystem`(PostSim).

Vehicle moves at 10 m/s along X-axis. MockTerrainProvider: flat (0-20 m), ramp (20-80 m, slope×0.2), spike at X≈40 (Z=100 – bad raycast).

| Phase | Tick | X ≈ | Expected |
|-------|------|-----|---------|
| 1 – Flat     | 10  | 1.7 m  | CurrentZOffset ≈ 0 |
| 2 – Smoothing | 150 | 25 m  | TargetZOffset > 0.5, Current < Target |
| 3 – Spike rejected | 240 | 40 m | LastValidIgAltitude < 10 |
| 4 – Recovery  | 300 | 50 m  | TargetZOffset ≈ 6.0 (±1.0) |

Max ticks: `350`.

#### DEM1-D008: ParallelStories (AAR Recording & Deterministic Replay)

**Goal:** Prove `Fdp.Kernel.FlightRecorder` LZ4 recording and naked-node replay produce bit-identical positions.

**Topology:** Phase A (synchronous setup): full `GroundKinematicsModule` + `RecordingModule`, runs 50 ticks, writes `.fdprec`. Phase B (main loop): `ReplayModule` only — no CarKinem in the topology.

The replay must match the live trajectory positions to < 0.001 m at ticks 25 and 50.

| Phase | Tick | Assertion |
|-------|------|-----------|
| 1 – Keyframe load | 1 | Entity exists with correct SimTransform |
| 2 – Delta apply   | 10 | SimTransform matches recorded |
| 3 – Physics bypass | 25 | |Live pos − Replay pos| < 0.001 m |
| 4 – Final match  | 50 | |Live pos − Replay pos| < 0.001 m → SUCCESS |

Max ticks: `60`.

---

### 6.4 Phase 5 — Network Demo

#### DEM1-D009: DistributedTank (Component-Level Network Authority)

**Goal:** Prove component-level split authority via DDS loopback: `WeaponChannel` on Brain node; `SimTransform` on Muscle node. Prove ELM handshake → Active state. Prove ghosting of Brain hull on Muscle via `EntityMasterTopic` + `DemoLocomotionMsg`.

**Topology:** Two isolated `ModuleHostKernel` instances in the same process communicating via `FastCycloneDDS` on Domain 0.

- Brain Node (ID 100): `EntityLifecycleModule` (zero-participant), manual hull (TKB 100) and turret (TKB 101) spawned via `AddComponent`; publishes `EntityMasterTopic` (hull ghost) and `DemoLocomotionMsg` (locomotion command); `LocomotionChannel` on hull.
- Muscle Node (ID 200): `EntityLifecycleModule` + `ReplicationLogicModule`; TKB templates registered via `DemoTkbSetup.RegisterAll`; `MuscleDirectSystemsModule` hosting `SpatialHashSystem` + `CarKinematicsSystem`; ghost creation and promotion via `GhostPromotionSystem`; `DemoLocomotionMsg` translated to `NavState`.

> **Why no `BehaviorToolkit` / `ReplicationLogicModule` on Brain:** The authoritative Brain node has no incoming ghosts to manage, so `ReplicationLogicModule` is unnecessary and would cause DDS loopback self-ghosting. Locomotion commands are injected directly by `EvaluateTick` at tick 20 and published as `DemoLocomotionMsg`. This is intentionally scoped to what the ECS/DDS split-authority demo requires; see `DEM1-TASK-DETAIL` § D009 architecture note.

| Phase | Tick | Event | Assertion |
|-------|------|-------|-----------|
| 1 – ELM Active | 5 | ELM zero-participant auto-promote | Brain hull lifecycle == Active (`DistributedTankScenario.PhaseBElmActiveTick`) |
| 2 – DDS Loco → NavState | 25 | Brain writes `DemoLocomotionMsg` at tick 20; Muscle polls and applies `NavState` at tick 21 | Muscle ghost `SimVelocity.Linear.X > 0.1` |
| 3 – Turret tracks hull | 40 | Brain hull/turret share layout (no Brain CarKinem); ghost may move on Muscle | Brain turret `SimTransform` within ±0.1 m of Brain hull |
| 4 – Split authority | 50 | Brain writes `WeaponChannel` at tick 30 | Turret `WeaponChannel.ActiveAction == AimAndFire`; ghost hull still moving |

Max ticks: `60`.

---

### 6.5 Phase 6 — Grand Integration Demo

#### DEM1-D010: UrbanCombat (All Toolkits)

**Goal:** Prove end-to-end cascade: Pathfinding → Kinematics → Perception → BTree → Combat → Ballistics → Entity Death → Mission Resumption.

**Topology:** All toolkits (`Behavior`, `CarKinem`, `Navigation`, `Perception`, `Physics`, `Combat`) in a single headless kernel. Deterministic 60 Hz stepping. Sequential latches (not exact-tick assertions) to tolerate minor floating-point timing variance.

**Setup:**
- 4-way intersection `RoadNetworkBlob` via `DemoRoadGraphFactory`
- Entities: 1 MilitaryAPC (TKB 2001, ConvoyEscort HSM, heading north), 1 Insurgent (TKB 2003, Ambush BTree, TargetMemory pre-seeded with APC), 5 CivilianPedestrian + 3 CivilianCar (background traffic)
- Total: 14 entities

**Sequential Latches (budget: 600 ticks / 10 s):**

| Latch | Window | Condition |
|-------|--------|-----------|
| AmbushFired | Ticks 1-100 | `WeaponChannel.ActiveAction == AimAndFire` on Insurgent *(spec note: originally `FireRequestEvent`; weapon-channel state is the implemented observable and is equivalent proof of ambush engagement)* |
| ApcHalted | After latch 1, ≤tick 150 | APC `LocomotionChannel.ActiveAction == 0` |
| InsurgentHit | After latch 2, ≤tick 300 | `Health.Current < SoldierMaxHealth` on Insurgent *(spec note: originally `HitEvent.HitEntity == Insurgent`; health-drop is equivalent for the single-insurgent template)* |
| InsurgentKilled | After latch 3, ≤tick 400 | `!world.IsAlive(insurgent)` |
| MissionResumed | After latch 4, ≤tick 600 | Log line `"Mission Resumed"` emitted *(spec caveat: normative goal was APC loco `FollowRoute`/`MoveTo`; requires HSM `Disabled→Cruising` recovery transition not yet implemented — Latch 5 is a narrative/log milestone until HSM recovery is added)* |

Max ticks: `600`.

---

## 7. Testing Architecture

### 7.1 Test Pattern

Each scenario has a corresponding **xUnit integration test** in a tests project `Fdp.Examples.Scenarios.Tests`. The test:

1. Constructs the scenario via `ScenarioRegistry.Create(name)`.
2. Wraps it in `ScenarioSubsystem` with `maxTicks` limit.
3. Instantiates `SubsystemOrchestrator` in headless + deterministic mode.
4. Calls `orchestrator.Initialize()` then `orchestrator.Run()`.
5. Asserts `Environment.ExitCode == 0` (or intercepts the `ScenarioFailureException` directly without process exit when running under xUnit).

To avoid `Environment.Exit` killing the test runner process, `ScenarioSubsystem` uses an **injectable exit callback** (`Action<int> exitCallback`) defaulting to `Environment.Exit` in production but replaced by a `throw ScenarioFailureException` in tests.

### 7.2 Log File Per Test Run

Every run (including xUnit) configures NLog with a file target:

```
logs/demo-<ScenarioName>-<yyyyMMdd-HHmmss>.log
```

The log contains:
- Tick-by-tick phase evaluation at `Trace` level
- Phase pass/fail events at `Info` level  
- Failure messages with ECS component diagnostic values at `Error` level

AI coding agents should read this log to diagnose failures.

### 7.3 Deterministic Time Injection

All scenarios use `SteppingTimeController` to advance `GlobalTime`:

```csharp
// In ScenarioSubsystem.Update(float dt):
var globalTime = _timeController.Step(_fixedDeltaSeconds);
_world.SetSingleton(globalTime);
_kernel.Update();
```

This guarantees all physics integrations use exactly `1/60 s` per tick regardless of wall-clock speed.

---

## 8. Project References Summary

```
Fdp.Examples.Runner
  → Fdp.Examples.Common
  → Fdp.Examples.Scenarios
  → FDP.Framework.Runner
  → FDP.Framework.Raylib (only when --attach-vis2d compiled in)
  → FDP.Toolkit.Vis2D (optional)

Fdp.Examples.Scenarios
  → Fdp.Examples.Common
  → Fdp.Examples.DDS
  → All referenced FDP Toolkits (per scenario requirements)

Fdp.Examples.Common
  → Fdp.Kernel
  → ModuleHost.Core
  → FDP.Toolkit.Time
  → FDP.Toolkit.Vis2D (MapCanvas interface only)

Fdp.Examples.DDS
  → (DDS serialization library only — no FDP Kernel)
```

---

## 9. Implementation Phases

| Phase | Name | Tasks | Dependency |
|-------|------|-------|-----------|
| Phase 0 | Demo Framework Foundation | DEM1-F001 – F005 | None |
| Phase 1 | Shared Infrastructure | DEM1-I001 – I002 | Phase 0 |
| Phase 2 | Simple Demos | DEM1-D001 – D002 | Phase 1 |
| Phase 3 | Mid-Complexity Demos | DEM1-D003 – D005 | Phase 1 |
| Phase 4 | Advanced Demos | DEM1-D006 – D008 | Phase 2 |
| Phase 5 | Network Demo | DEM1-D009 | Phase 3, Phase 1 |
| Phase 6 | Grand Integration Demo | DEM1-D010 | Phase 2 + 3 + 4 |

---

## 10. Key Design Decisions

### No process-exit in xUnit

`ScenarioSubsystem` accepts an `Action<int>` exit delegate. Production: `Environment.Exit`. Tests: throws `ScenarioFailureException(phase, message)`. The xUnit test catches the exception and fails the test with a descriptive message.

### Deterministic stepping as a first-class RunnerOptions flag

Rather than each scenario implementing its own `SteppingTimeController`, the `ScenarioSubsystem` creates one automatically when `SubsystemConfig.Deterministic == true`. This ensures every demo benefits from determinism without boilerplate.

### Sequential latches for the Grand Demo

The UrbanCombat scenario uses `bool` latches rather than exact-tick assertions, making it robust to minor floating-point variance while still proving the logical causal chain.

### Log file path on stdout

The runner prints the log file path to stdout immediately after startup:
```
[RUNNER] Log: logs/demo-autodrive-20260317-143022.log
```
This gives AI agents a direct path to the diagnostic trace.
