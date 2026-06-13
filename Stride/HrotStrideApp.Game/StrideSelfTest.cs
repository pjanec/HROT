#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
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
    private const int TrackLogInterval     = 30;
    private const int ResolveTimeoutFrames = 120;   // after SPAWN: give up if not found
    private const int TotalTimeoutFrames   = 1200;  // whole-run hard limit

    // ── Test parameters ──────────────────────────────────────────────────────
    private const long  SpawnTkbType   = 100L;  // Tank_M1Abrams → OrientedBox Bullet body
    private const long  SpawnNetId     = 9001L;
    private const float HoldTolerance  = 5.0f;  // metres (FDP X,Y)

    // IN-ARENA coords (arena ≈ FDP X∈[-10,10], Y∈[0,15]) so the infinite-plane walls do
    // not eject the body — isolates position/reposition-honoring from the small-arena ejection.
    private static readonly System.Numerics.Vector3 PosA = new(6f, 8f, 0f);
    private static readonly System.Numerics.Vector3 PosB = new(-7f, 5f, 0f);

    // ── Dependencies ─────────────────────────────────────────────────────────
    private readonly TestHarnessContext _ctx;
    private readonly NetworkEntityMap   _entityMap;
    private readonly Game  _game;

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

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the self-test driver.
    /// </summary>
    /// <param name="ctx">The test harness context (World, ScenarioSource, log sink).</param>
    /// <param name="entityMap">The live <see cref="NetworkEntityMap"/> for netId→entity lookup.</param>
    /// <param name="game">The live Stride <see cref="Game"/> used for <c>Exit()</c>.</param>
    public StrideSelfTest(
        TestHarnessContext ctx,
        NetworkEntityMap   entityMap,
        Game  game)
    {
        _ctx       = ctx       ?? throw new ArgumentNullException(nameof(ctx));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        _game      = game      ?? throw new ArgumentNullException(nameof(game));
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
        TestHarnessContext ctx,
        NetworkEntityMap   entityMap,
        Game  game)
    {
        var selfTest = new StrideSelfTest(ctx, entityMap, game);
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
            Log.Info("[SELFTEST] RESULT initialHold=FAIL repos=FAIL reason=timeout");
            ExitProcess();
            return false;
        }

        return _phase switch
        {
            Phase.Warmup     => TickWarmup(),
            Phase.Spawn      => TickSpawn(),
            Phase.SettleA    => TickSettleA(),
            Phase.CheckA     => TickCheckA(),
            Phase.Reposition => TickReposition(),
            Phase.SettleB    => TickSettleB(),
            Phase.CheckB     => TickCheckB(),
            Phase.Done       => false,
            _                => false,
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
            Log.Info("[SELFTEST] RESULT initialHold=FAIL repos=FAIL reason=entity-not-found");
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

        WriteSummaryAndExit();
        return false;
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
            $"errA={_errA:F2} errB={_errB:F2} " +
            $"(A=({PosA.X},{PosA.Y}) endA=({_endA.X:F2},{_endA.Y:F2}) " +
            $"B=({PosB.X},{PosB.Y}) endB=({_endB.X:F2},{_endB.Y:F2}))");
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
