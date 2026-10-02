#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Core.Network;
using Hrot.Stride.Core.TestHarness;
using Stride.Engine;

namespace HrotStrideApp;

/// <summary>
/// Autonomous self-test driver for <c>STRIDE_SELFTEST=1</c> (BATCH-S2-H).
///
/// <para>
/// When the env var <c>STRIDE_SELFTEST=1</c> is set, this class is registered as a
/// per-frame <see cref="TestHarnessContext.RegisterUpdate"/> hook from
/// <see cref="StrideHrotGame.BootEditorSubsystem"/> (after harness/navmesh are ready).
/// It is a frame-counted state machine that:
/// <list type="number">
///   <item>Waits ~30 frames for scene/physics/navmesh to settle (WARMUP).</item>
///   <item>Spawns ONE OrientedBox vehicle (TkbType 100 = Tank_M1Abrams) at FDP A=(120,80,0)
///     via the normal scenario spawn path with <c>PreAllocatedNetworkId=9001</c>.</item>
///   <item>Waits ~150 frames (SETTLE_A), logging position every 30 frames.</item>
///   <item>Checks position vs A (tolerance 5 m); records <c>initialHold</c> verdict.</item>
///   <item>Externally repositions the entity's SimTransform to B=(220,40,0) via
///     <c>world.SetComponent</c> — mimicking an operator drag.</item>
///   <item>Waits ~120 frames (SETTLE_B), logging position every 30 frames.</item>
///   <item>Checks position vs B; records <c>repos</c> verdict.</item>
///   <item>Logs a single <c>[SELFTEST] RESULT …</c> summary line.</item>
///   <item>Calls <c>game.Exit()</c> (or <c>Environment.Exit(0)</c> as fallback).</item>
/// </list>
/// </para>
///
/// <para>
/// A timeout guard exits the process if the entity cannot be resolved within ~120 frames
/// after SPAWN, or if the total run exceeds ~1200 frames. This guarantees the process
/// always exits when <c>STRIDE_SELFTEST=1</c>.
/// </para>
///
/// <para>
/// All <c>[SELFTEST]</c> log lines are at <c>Log.Info</c> (NLog) so they land in
/// <c>logs/editor_stride.log</c>.
/// </para>
/// </summary>
public sealed class StrideSelfTest
{
    // ── NLog logger ──────────────────────────────────────────────────────────
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("StrideSelfTest");

    // ── Timing constants (frames) ────────────────────────────────────────────
    private const int WarmupFrames         = 30;
    private const int SettleAFrames        = 150;
    private const int SettleBFrames        = 120;
    private const int PausedSettleFrames   = 120;   // frames to confirm no motion while paused
    private const int DriveSettleFrames    = 240;   // drive phase: frames to wait for movement
    private const int TrackLogInterval     = 30;
    private const int ResolveTimeoutFrames = 120;   // after SPAWN: give up if not found
    private const int TotalTimeoutFrames   = 1800;  // whole-run hard limit (extended for drive phase)

    private const float PausedFreezeTolerance = 1.0f; // metres: movement <= this while paused => frozen

    // ── Test parameters ──────────────────────────────────────────────────────
    private const long  SpawnTkbType   = 101L;  // IFV (TkbType 101) — the exact type the test-move scenario uses
    private const long  SpawnNetId     = 9001L;
    private const float HoldTolerance  = 5.0f;  // metres (FDP X,Y)

    // IN-ARENA coords (arena ≈ FDP X∈[-10,10], Y∈[0,15]) so entities are on the floor.
    private static readonly System.Numerics.Vector3 PosA = new(6f, 8f, 0f);
    private static readonly System.Numerics.Vector3 PosB = new(-7f, 5f, 0f);
    // Drive destination D: in-arena point used by the DRIVE phase to test the Stride muscle.
    private static readonly System.Numerics.Vector3 PosD = new(4f, 11f, 0f);
    private const float DriveArrivalRadius = 2.0f;   // arrival check radius (metres)
    private const float DriveTargetSpeed   = 5.0f;   // commanded target speed (m/s)
    private const float DrivePassErrToDest = 3.0f;   // <= this distance to D → PASS
    private const float DrivePassDistMoved = 3.0f;   // >= this movement from B → PASS

    // ── Dependencies ─────────────────────────────────────────────────────────
    private readonly TestHarnessContext  _ctx;
    private readonly NetworkEntityMap    _entityMap;
    private readonly Game                _game;
    private readonly MasterSyncController _timeController;

    // ── State machine ────────────────────────────────────────────────────────
    private enum Phase
    {
        Warmup,
        Spawn,
        SettleA,
        CheckA,
        Reposition,
        SettleB,
        CheckB,
        DriveIssue,
        PausedSettle,
        CheckPaused,
        Resume,
        DrivingSettle,
        CheckDrive,
        Done,
    }

    private Phase _phase        = Phase.Warmup;
    private int   _frameInPhase = 0;
    private int   _totalFrames  = 0;
    private int   _trackFrame   = 0;

    // Verdicts and measured values.
    private bool  _initialHold;
    private float _errA;
    private System.Numerics.Vector3 _endA;

    private bool  _repos;
    private float _errB;
    private System.Numerics.Vector3 _endB;

    // Paused-freeze state.
    private bool  _pausedFreeze;
    private float _pausedDistMoved;

    // Drive-phase state.
    private bool  _drive;
    private float _driveErrToDest;
    private float _driveDistMoved;
    private System.Numerics.Vector3 _endDrive;
    private string _driveNavResult = "N/A";

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the self-test driver.
    /// </summary>
    /// <param name="ctx">The test harness context (World, ScenarioSource, log sink).</param>
    /// <param name="entityMap">The live <see cref="NetworkEntityMap"/> for netId→entity lookup.</param>
    /// <param name="game">The live Stride <see cref="Game"/> used for <c>Exit()</c>.</param>
    /// <param name="timeController">
    /// The <see cref="MasterSyncController"/> used to resume the sim after the paused-freeze check.
    /// </param>
    public StrideSelfTest(
        TestHarnessContext   ctx,
        NetworkEntityMap     entityMap,
        Game                 game,
        MasterSyncController timeController)
    {
        _ctx            = ctx            ?? throw new ArgumentNullException(nameof(ctx));
        _entityMap      = entityMap      ?? throw new ArgumentNullException(nameof(entityMap));
        _game           = game           ?? throw new ArgumentNullException(nameof(game));
        _timeController = timeController ?? throw new ArgumentNullException(nameof(timeController));
    }

    // ── Public factory ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="StrideSelfTest"/> and registers it as a continuous
    /// <see cref="TestHarnessContext.RegisterUpdate"/> hook.
    ///
    /// <para>
    /// Called from <see cref="StrideHrotGame.BootEditorSubsystem"/> when
    /// <c>STRIDE_SELFTEST=1</c> is detected, after the editor subsystem, harness,
    /// and navmesh are ready.
    /// </para>
    /// </summary>
    public static void RegisterIfEnabled(
        TestHarnessContext   ctx,
        NetworkEntityMap     entityMap,
        Game                 game,
        MasterSyncController timeController)
    {
        var selfTest = new StrideSelfTest(ctx, entityMap, game, timeController);
        ctx.RegisterUpdate(selfTest.Tick);
        Log.Info("[SELFTEST] Self-test registered (STRIDE_SELFTEST=1). Entering WARMUP.");
    }

    // ── Per-frame tick ───────────────────────────────────────────────────────

    /// <summary>
    /// Per-frame callback registered with <see cref="TestHarnessContext.RegisterUpdate"/>.
    /// Returns <c>true</c> to keep running, <c>false</c> when done (process exits inside
    /// this call via <c>game.Exit()</c> or <c>Environment.Exit(0)</c>).
    /// </summary>
    private bool Tick(float dt)
    {
        _totalFrames++;
        _frameInPhase++;

        // ── Total-run timeout guard ────────────────────────────────────────
        if (_totalFrames >= TotalTimeoutFrames && _phase != Phase.Done)
        {
            Log.Info("[SELFTEST] RESULT initialHold=FAIL repos=FAIL pausedFreeze=FAIL drive=FAIL reason=timeout");
            ExitProcess();
            return false;
        }

        return _phase switch
        {
            Phase.Warmup        => TickWarmup(),
            Phase.Spawn         => TickSpawn(),
            Phase.SettleA       => TickSettleA(),
            Phase.CheckA        => TickCheckA(),
            Phase.Reposition    => TickReposition(),
            Phase.SettleB       => TickSettleB(),
            Phase.CheckB        => TickCheckB(),
            Phase.DriveIssue    => TickDriveIssue(),
            Phase.PausedSettle  => TickPausedSettle(),
            Phase.CheckPaused   => TickCheckPaused(),
            Phase.Resume        => TickResume(),
            Phase.DrivingSettle => TickDrivingSettle(),
            Phase.CheckDrive    => TickCheckDrive(),
            Phase.Done          => false,
            _                   => false,
        };
    }

    // ── Phase handlers ────────────────────────────────────────────────────────

    private bool TickWarmup()
    {
        if (_frameInPhase >= WarmupFrames)
            TransitionTo(Phase.Spawn);
        return true;
    }

    private bool TickSpawn()
    {
        // Enqueue via the normal scenario spawn path so the entity goes through
        // CreateEntityRequestSystem → NetworkSpawningSystem → translators →
        // PhysicsBodyLifecycleSystem → BulletPhysicsBodyService.CreateBody.
        // PreAllocatedNetworkId=9001 lets us deterministically look the entity up later.
        _ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId             = Guid.NewGuid(),
            OwnerAppInstanceId    = 0,            // localNodeId=0 → WithOwned authority granted
            TkbType               = SpawnTkbType,
            PreAllocatedNetworkId = SpawnNetId,
            InitialComponents     = new List<object>
            {
                new SimTransform
                {
                    Position = PosA,
                    Rotation = System.Numerics.Quaternion.Identity,
                },
                new TkbIdentity { TkbType = SpawnTkbType },
            },
        });

        Log.Info($"[SELFTEST] SPAWN tkb={SpawnTkbType} netId={SpawnNetId} at A=({PosA.X},{PosA.Y})");
        TransitionTo(Phase.SettleA);
        return true;
    }

    private bool TickSettleA()
    {
        _trackFrame++;

        // Optionally log position every TrackLogInterval frames.
        if (_trackFrame % TrackLogInterval == 0 && TryGetPosition(out var tp))
            Log.Info($"[SELFTEST] track A frame={_totalFrames} pos=({tp.X:F2},{tp.Y:F2})");

        // Resolve-timeout: entity must appear within ResolveTimeoutFrames after spawn.
        if (_frameInPhase >= ResolveTimeoutFrames && !EntityExists())
        {
            Log.Info("[SELFTEST] RESULT initialHold=FAIL repos=FAIL drive=FAIL reason=entity-not-found");
            ExitProcess();
            return false;
        }

        if (_frameInPhase >= SettleAFrames)
            TransitionTo(Phase.CheckA);

        return true;
    }

    private bool TickCheckA()
    {
        if (!TryGetPosition(out var pos))
        {
            Log.Info("[SELFTEST] CHECK_A: entity not found — FAIL");
            _errA        = float.MaxValue;
            _endA        = System.Numerics.Vector3.Zero;
            _initialHold = false;
        }
        else
        {
            _endA = pos;
            float dx = pos.X - PosA.X;
            float dy = pos.Y - PosA.Y;
            _errA = MathF.Sqrt(dx * dx + dy * dy);
            float drift = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
            _initialHold = _errA <= HoldTolerance;
            Log.Info(
                $"[SELFTEST] CHECK_A end=({pos.X:F2},{pos.Y:F2}) " +
                $"errA={_errA:F2} driftToOrigin={drift:F2} -> {(_initialHold ? "PASS" : "FAIL")}");
        }

        TransitionTo(Phase.Reposition);
        return true;
    }

    private bool TickReposition()
    {
        if (!TryGetEntityHandle(out var entity))
        {
            // Entity gone — can't reposition; record repos=FAIL and exit.
            Log.Info("[SELFTEST] REPOSITION: entity not found — skipping, repos=FAIL");
            _repos = false;
            _errB  = float.MaxValue;
            _endB  = System.Numerics.Vector3.Zero;
            WriteSummaryAndExit();
            return false;
        }

        // Preserve the current rotation (only overwriting Position).
        var world = _ctx.World;
        var currentRot = world.HasComponent<SimTransform>(entity)
            ? world.GetComponentRO<SimTransform>(entity).Rotation
            : System.Numerics.Quaternion.Identity;

        // Write SimTransform directly — this mimics the operator drag path.
        world.SetComponent(entity, new SimTransform
        {
            Position = PosB,
            Rotation = currentRot,
        });

        Log.Info($"[SELFTEST] REPOSITION to B=({PosB.X},{PosB.Y})");
        TransitionTo(Phase.SettleB);
        return true;
    }

    private bool TickSettleB()
    {
        _trackFrame++;

        if (_trackFrame % TrackLogInterval == 0 && TryGetPosition(out var tp))
            Log.Info($"[SELFTEST] track B frame={_totalFrames} pos=({tp.X:F2},{tp.Y:F2})");

        if (_frameInPhase >= SettleBFrames)
            TransitionTo(Phase.CheckB);

        return true;
    }

    private bool TickCheckB()
    {
        if (!TryGetPosition(out var pos))
        {
            Log.Info("[SELFTEST] CHECK_B: entity not found — FAIL");
            _errB  = float.MaxValue;
            _endB  = System.Numerics.Vector3.Zero;
            _repos = false;
        }
        else
        {
            _endB = pos;
            float dx = pos.X - PosB.X;
            float dy = pos.Y - PosB.Y;
            _errB = MathF.Sqrt(dx * dx + dy * dy);
            float drift = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
            _repos = _errB <= HoldTolerance;
            Log.Info(
                $"[SELFTEST] CHECK_B end=({pos.X:F2},{pos.Y:F2}) " +
                $"errB={_errB:F2} driftToOrigin={drift:F2} -> {(_repos ? "PASS" : "FAIL")}");
        }

        TransitionTo(Phase.DriveIssue);
        return true;
    }

    private bool TickDriveIssue()
    {
        if (!TryGetEntityHandle(out var entity))
        {
            // Entity gone after reposition — record drive=FAIL and exit.
            Log.Info("[SELFTEST] DRIVE_ISSUE: entity not found — skipping drive, drive=FAIL");
            _drive        = false;
            _driveNavResult = "EntityNotFound";
            WriteSummaryAndExit();
            return false;
        }

        var world = _ctx.World;

        // Ensure NavigationStatus is present so the muscle can write feedback.
        if (world.IsComponentTypeRegistered<NavigationStatus>()
            && !world.HasComponent<NavigationStatus>(entity))
        {
            world.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });
        }

        // Issue a single direct NavigationIntent to the entity, bypassing the brain.
        // This tests whether the Stride vehicle muscle actually moves the entity
        // when given an explicit NavigationMode.DirectPoint command.
        var intent = world.HasComponent<NavigationIntent>(entity)
            ? world.GetComponent<NavigationIntent>(entity)
            : default;

        intent.Mode             = NavigationMode.DirectPoint;
        intent.FinalDestination = PosD;
        intent.TargetSpeed      = DriveTargetSpeed;
        intent.ArrivalRadius    = DriveArrivalRadius;
        intent.IntentId         = 1;
        intent.ReverseAllowed   = 0;

        if (world.HasComponent<NavigationIntent>(entity))
            world.SetComponent(entity, intent);
        else if (world.IsComponentTypeRegistered<NavigationIntent>())
            world.AddComponent(entity, intent);
        // If NavigationIntent is not registered in this composition, the drive phase
        // will still run but the vehicle will not move — FAIL captures the diagnostic.

        Log.Info($"[SELFTEST] DRIVE_ISSUE intent → D=({PosD.X},{PosD.Y}) IntentId=1");
        TransitionTo(Phase.PausedSettle);
        return true;
    }

    private bool TickPausedSettle()
    {
        _trackFrame++;

        if (_trackFrame % TrackLogInterval == 0 && TryGetPosition(out var tp))
            Log.Info($"[SELFTEST] paused frame={_totalFrames} pos=({tp.X:F2},{tp.Y:F2})");

        if (_frameInPhase >= PausedSettleFrames)
            TransitionTo(Phase.CheckPaused);

        return true;
    }

    private bool TickCheckPaused()
    {
        if (!TryGetPosition(out var pos))
        {
            Log.Info("[SELFTEST] CHECK_PAUSED: entity not found — FAIL");
            _pausedDistMoved = float.MaxValue;
            _pausedFreeze    = false;
        }
        else
        {
            float dx = pos.X - PosB.X;
            float dy = pos.Y - PosB.Y;
            _pausedDistMoved = MathF.Sqrt(dx * dx + dy * dy);
            _pausedFreeze    = _pausedDistMoved <= PausedFreezeTolerance;
            Log.Info(
                $"[SELFTEST] CHECK_PAUSED end=({pos.X:F2},{pos.Y:F2}) " +
                $"distMovedWhilePaused={_pausedDistMoved:F2} -> {(_pausedFreeze ? "PASS" : "FAIL")}");
        }

        TransitionTo(Phase.Resume);
        return true;
    }

    private bool TickResume()
    {
        _timeController.SwitchToContinuous();
        Log.Info("[SELFTEST] RESUME → SwitchToContinuous (sim time now running)");
        TransitionTo(Phase.DrivingSettle);
        return true;
    }

    private bool TickDrivingSettle()
    {
        _trackFrame++;

        if (_trackFrame % TrackLogInterval == 0)
        {
            TryGetPosition(out var tp);
            var navResult = ReadNavResult(out uint navIntentId);
            Log.Info($"[SELFTEST] drive frame={_totalFrames} pos=({tp.X:F2},{tp.Y:F2}) navResult={navResult} navIntentId={navIntentId}");
        }

        if (_frameInPhase >= DriveSettleFrames)
            TransitionTo(Phase.CheckDrive);

        return true;
    }

    private bool TickCheckDrive()
    {
        if (!TryGetPosition(out var pos))
        {
            Log.Info("[SELFTEST] CHECK_DRIVE: entity not found — FAIL");
            _driveNavResult  = "EntityNotFound";
            _driveErrToDest  = float.MaxValue;
            _driveDistMoved  = 0f;
            _endDrive        = System.Numerics.Vector3.Zero;
            _drive           = false;
        }
        else
        {
            _endDrive = pos;
            // Measure from the post-reposition start point B (XY in FDP space).
            float movedDx = pos.X - PosB.X;
            float movedDy = pos.Y - PosB.Y;
            _driveDistMoved = MathF.Sqrt(movedDx * movedDx + movedDy * movedDy);

            float destDx = pos.X - PosD.X;
            float destDy = pos.Y - PosD.Y;
            _driveErrToDest = MathF.Sqrt(destDx * destDx + destDy * destDy);

            _driveNavResult = ReadNavResult(out _).ToString();

            // PASS if arrived near D OR made real progress from B.
            _drive = _driveErrToDest <= DrivePassErrToDest || _driveDistMoved >= DrivePassDistMoved;

            Log.Info(
                $"[SELFTEST] CHECK_DRIVE end=({pos.X:F2},{pos.Y:F2}) " +
                $"distMoved={_driveDistMoved:F2} errToDest={_driveErrToDest:F2} " +
                $"navResult={_driveNavResult} -> {(_drive ? "PASS" : "FAIL")}");
        }

        WriteSummaryAndExit();
        return false;
    }

    /// <summary>
    /// Reads the <see cref="NavigationStatus.Result"/> for the test entity.
    /// Returns <see cref="NavigationResult.InProgress"/> (0) when the component is absent.
    /// </summary>
    private NavigationResult ReadNavResult(out uint intentId)
    {
        intentId = 0;
        if (!TryGetEntityHandle(out var entity)) return NavigationResult.InProgress;
        if (!_ctx.World.HasComponent<NavigationStatus>(entity)) return NavigationResult.InProgress;
        var status = _ctx.World.GetComponentRO<NavigationStatus>(entity);
        intentId = status.IntentId;
        return status.Result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void TransitionTo(Phase next)
    {
        _phase        = next;
        _frameInPhase = 0;
        _trackFrame   = 0;
    }

    private bool EntityExists() => TryGetEntityHandle(out _);

    private bool TryGetEntityHandle(out Fdp.Core.Entity entity)
    {
        entity = default;
        if (_entityMap.TryGetEntity(SpawnNetId, out var e) && _ctx.World.IsAlive(e))
        {
            entity = e;
            return true;
        }
        return false;
    }

    private bool TryGetPosition(out System.Numerics.Vector3 position)
    {
        position = System.Numerics.Vector3.Zero;
        if (!TryGetEntityHandle(out var entity)) return false;
        if (!_ctx.World.HasComponent<SimTransform>(entity)) return false;
        position = _ctx.World.GetComponentRO<SimTransform>(entity).Position;
        return true;
    }

    private void WriteSummaryAndExit()
    {
        _phase = Phase.Done;
        Log.Info(
            $"[SELFTEST] RESULT " +
            $"initialHold={(_initialHold ? "PASS" : "FAIL")} " +
            $"repos={(_repos ? "PASS" : "FAIL")} " +
            $"pausedFreeze={(_pausedFreeze ? "PASS" : "FAIL")} " +
            $"drive={(_drive ? "PASS" : "FAIL")} " +
            $"errA={_errA:F2} errB={_errB:F2} pausedDistMoved={_pausedDistMoved:F2} driveDistMoved={_driveDistMoved:F2} driveErrToDest={_driveErrToDest:F2} " +
            $"(A=({PosA.X},{PosA.Y}) endA=({_endA.X:F2},{_endA.Y:F2}) " +
            $"B=({PosB.X},{PosB.Y}) endB=({_endB.X:F2},{_endB.Y:F2}) " +
            $"D=({PosD.X},{PosD.Y}) endDrive=({_endDrive.X:F2},{_endDrive.Y:F2}))");
        ExitProcess();
    }

    private void ExitProcess()
    {
        _phase = Phase.Done;
        Log.Info("[SELFTEST] Exiting process.");

        // Flush NLog so the summary line above is written to disk before the process ends.
        NLog.LogManager.Flush(TimeSpan.FromSeconds(2));

        // Prefer Stride's Game.Exit() for a clean shutdown; fall back to Environment.Exit(0).
        try
        {
            _game.Exit();
        }
        catch
        {
            System.Environment.Exit(0);
        }
    }
}
