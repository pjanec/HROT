#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using Hrot.Stride.Core.TestHarness;
using SNum = System.Numerics;

namespace HrotStrideApp;

/// <summary>
/// Phase-5 (BATCH-15) <see cref="VisualTestCase"/>s for the in-app Stride test harness:
/// <b>3-D gizmos</b> (STR-P5-T1) and <b>record/replay</b> (STR-P5-T4, design §9).
///
/// <para>
/// <b>Controls</b> (assigned by the harness in registration order, after the BATCH-12/14 cases):
/// <list type="bullet">
///   <item><b>Draw Test Gizmo</b> (BATCH-21 upgraded) — writes a rich set of
///     <c>DebugPrimitive</c> shapes (R/G/B axis triad, red + white spheres, colored line
///     segments) into the <see cref="EditorStrideSubsystem.ProducerBuffer"/>. The
///     <see cref="EditorStrideSubsystem.GizmoRenderer3D"/> resolves+swizzles them and emits to
///     the <see cref="Hrot.Stride.Core.PooledEntityDebugDrawSink3D"/> so they <b>actually
///     render</b> as emissive Stride entities in the scene (STR-D16 resolved). Persists 8 s.
///     Fly the camera to Stride Z≈6 to see the axis triad and spheres.</item>
///   <item><b>Record 3s / Replay</b> — spawns a <b>dedicated</b> non-owned ghost that the case
///     fully owns, drives it in a circle for ~3 s while recording (the case writes its
///     <c>SimTransform</c> every frame from within its own phase-machine hook), finalizes, then
///     prepares a replay: the <see cref="EditorStrideSubsystem.ReplayLoadHandler"/> severs the
///     reverse-sync group and <c>PlaybackTickSystem</c> drives the recorded <c>SimTransform</c>
///     back. At replay start the case SNAPS the ghost to the origin and STOPS live-driving it, so
///     PlaybackTickSystem re-tracing the recorded circle is visibly unmistakable; the ghost's
///     position is logged each replay frame (throttled) to prove playback is moving it. A dedicated
///     ghost (rather than the shared BATCH-12 orbiting ghost, which a separate live hook keeps
///     moving during replay) is what makes the replay's effect observable. The whole sequence is
///     driven by a per-frame hook so the kernel keeps ticking (async module installs need a
///     subsequent kernel Update to go live).</item>
/// </list>
/// </para>
/// </summary>
public static class StrideGizmoReplayHarnessCases
{
    private const long TkbInfantrySoldier = 2002L;

    /// <summary>
    /// Re-entrancy guard for the "Record 3s / Replay" case. The case drives a multi-second
    /// record→finalize→replay state machine via a per-frame hook. Triggering it again while a
    /// sequence is still in flight (a second key press, or the button <c>Click</c> and the
    /// keyboard <c>D9</c> both firing) would start a SECOND concurrent sequence: two
    /// <c>Recording_&lt;guid&gt;</c> modules and two reverse-sync severings racing on the shared
    /// <c>RecordReplayController</c> + the reverse-sync group, which crashes (IOException / topology
    /// churn). This flag ensures only one sequence runs at a time. It is set true when a sequence
    /// starts and cleared on EVERY terminal path (normal completion AND every early-return/failure
    /// branch) so a faulted sequence can never leave it stuck true. The case is static, so a single
    /// static field suffices; the harness drives all triggers on one thread (button Click +
    /// keyboard poll both run on the Stride game-update thread), so no locking is required.
    /// </summary>
    private static bool s_recordReplayInProgress;

    /// <summary>
    /// Registers the BATCH-15 cases into <paramref name="registry"/>. The
    /// <paramref name="subsystem"/> is captured so the cases can reach the gizmo ProducerBuffer
    /// and the record/replay controller + handler directly.
    /// </summary>
    public static TestHarnessRegistry RegisterGizmoReplayCases(
        TestHarnessRegistry registry,
        EditorStrideSubsystem subsystem)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (subsystem == null) throw new ArgumentNullException(nameof(subsystem));

        registry.Register(new VisualTestCase(
            "Draw Test Gizmo",
            "Write a known line+sphere DebugPrimitive into the ProducerBuffer; the 3D renderer resolves+swizzles it.",
            ctx => DrawTestGizmo(ctx, subsystem)));

        registry.Register(new VisualTestCase(
            "Record 3s / Replay",
            "Record the orbiting ghost for ~3s, then replay it (reverse-sync severed; PlaybackTickSystem drives SimTransform).",
            ctx => RecordThenReplay(ctx, subsystem)));

        return registry;
    }

    // ── Draw Test Gizmo ───────────────────────────────────────────────────

    /// <summary>
    /// D8 — Draw Test Gizmo (upgraded BATCH-21, STR-D16 resolution).
    ///
    /// <para>
    /// Emits a rich set of 3-D debug shapes at a known FDP location so the user can confirm
    /// GPU rendering by pressing D8 and looking at the Stride window:
    /// <list type="bullet">
    ///   <item><b>Coordinate axis triad</b> at the origin: red=+X(East), green=+Y(North), blue=+Z(Up), 2 m long.</item>
    ///   <item><b>Red sphere</b> of radius 0.75 m floating 2 m above the origin.</item>
    ///   <item><b>White box</b> 2 × 0.5 × 1 m oriented at the origin at ground level.</item>
    ///   <item><b>Diagonal colored segments</b>: cyan, magenta, yellow — 2 m each, forming a
    ///     small star pattern visible from above.</item>
    /// </list>
    /// All shapes persist 8 s so there is plenty of time to look around.
    /// </para>
    ///
    /// <para>
    /// <b>What the user should see (with PooledEntityDebugDrawSink3D wired):</b>
    /// Colored emissive shapes appear in the Stride window at the arena origin (FDP 0,6,0 →
    /// Stride 0,0,6): three bright axis sticks, a sphere, a box, and three diagonal line
    /// segments. Everything is unlit (emissive) and fully visible regardless of the directional
    /// light. The log prints the emitted count (should be 9: 3 axis + 1 sphere + 1 box + 3 diag +
    /// 1 extra vertical).
    /// </para>
    /// </summary>
    private static void DrawTestGizmo(TestHarnessContext ctx, EditorStrideSubsystem subsystem)
    {
        // FDP world position at the arena center, ground level.
        // FDP: X=East, Y=North, Z=Up.  Stride: X=East, Y=Up, Z=North.
        // Origin in FDP space (0, 6, 0) maps to Stride (0, 0, 6) — visible from the overview camera.
        var origin = new SNum.Vector3(0f, 6f, 0f);
        const float persist = 8f;

        // Known Rgba32 colors: Red(255,0,0), Green(0,255,0), Yellow(255,255,0), White(255,255,255).
        // Blue/Cyan/Magenta are not predefined — construct them inline.
        var colorBlue    = new Rgba32(  0,   0, 255, 255);
        var colorCyan    = new Rgba32(  0, 255, 255, 255);
        var colorMagenta = new Rgba32(255,   0, 255, 255);

        // ── Coordinate axis triad (2 m each, primary colors) ────────────
        // +X (East)  = red
        EmitLine(subsystem, origin, origin + new SNum.Vector3(2f, 0f, 0f), Rgba32.Red, persist);
        // +Y (North) = green
        EmitLine(subsystem, origin, origin + new SNum.Vector3(0f, 2f, 0f), Rgba32.Green, persist);
        // +Z (Up)    = blue
        EmitLine(subsystem, origin, origin + new SNum.Vector3(0f, 0f, 2f), colorBlue, persist);

        // ── Red sphere 0.75 m radius at 2 m altitude ────────────────────
        var sphere = DebugPrimitive.MakeSphere(
            center:   origin + new SNum.Vector3(0f, 0f, 2f),
            radius:   0.75f,
            color:    Rgba32.Red,
            sizeMode: SizeMode.WorldMeters,
            target:   PipelineTarget.All);
        sphere.Space           = CoordinateSpace.World;
        sphere.LifetimeSeconds = persist;
        subsystem.ProducerBuffer.EmitRaw(sphere);

        // ── White sphere at 1 m east + 1 m north as a second visible landmark ──
        var sphere2 = DebugPrimitive.MakeSphere(
            center:   origin + new SNum.Vector3(1f, 1f, 0.5f),
            radius:   0.4f,
            color:    Rgba32.White,
            sizeMode: SizeMode.WorldMeters,
            target:   PipelineTarget.All);
        sphere2.Space           = CoordinateSpace.World;
        sphere2.LifetimeSeconds = persist;
        subsystem.ProducerBuffer.EmitRaw(sphere2);

        // ── Diagonal colored segments (2 m each, cross pattern) ─────────
        // Cyan: NE diagonal
        EmitLine(subsystem, origin, origin + new SNum.Vector3( 1.4f, 1.4f, 0f), colorCyan, persist);
        // Magenta: NW diagonal
        EmitLine(subsystem, origin, origin + new SNum.Vector3(-1.4f, 1.4f, 0f), colorMagenta, persist);
        // Yellow: vertical to 1 m height (redundant with Z-axis but in yellow for contrast)
        EmitLine(subsystem, origin + new SNum.Vector3(0f, 0f, 1f),
                            origin + new SNum.Vector3(0f, 0f, 3f), Rgba32.Yellow, persist);

        // Render once immediately so the log count is printed this tick.
        subsystem.GizmoRenderer3D.Sink.BeginFrame();
        int emitted = subsystem.GizmoRenderer3D.Render(subsystem.ProducerBuffer.GetFrame());
        subsystem.GizmoRenderer3D.Sink.EndFrame();

        bool gpuActive = subsystem.GizmoRenderer3D.Sink is Hrot.Stride.Core.PooledEntityDebugDrawSink3D;
        ctx.Log(
            $"[D8 Draw Test Gizmo] Emitted {emitted} debug shape(s). " +
            $"GPU sink active: {gpuActive}. " +
            $"Origin FDP={Fmt(origin)} → Stride (0,0,6). " +
            "Shapes: R/G/B axis triad 2 m, red sphere r=0.75 @ +2 m up, white sphere r=0.4 @ (+1,+1,+0.5), " +
            "cyan/magenta diagonals, yellow vertical segment. " +
            "All persist 8 s. Fly camera to arena center (Stride Z≈6) to confirm.");
    }

    /// <summary>Emits a world-space FDP line into the ProducerBuffer with a given color and lifetime.</summary>
    private static void EmitLine(
        EditorStrideSubsystem subsystem,
        SNum.Vector3 from, SNum.Vector3 to,
        Rgba32 color, float lifetimeSeconds)
    {
        var line = DebugPrimitive.MakeLine(
            from:      from,
            to:        to,
            color:     color,
            thickness: 2f,
            sizeMode:  SizeMode.WorldMeters,
            target:    PipelineTarget.All);
        line.Space           = CoordinateSpace.World;
        line.LifetimeSeconds = lifetimeSeconds;
        subsystem.ProducerBuffer.EmitRaw(line);
    }

    // ── Record 3s / Replay ─────────────────────────────────────────────────

    private static void RecordThenReplay(TestHarnessContext ctx, EditorStrideSubsystem subsystem)
    {
        // Re-entrancy guard: only one record/replay sequence may run at a time. A second trigger
        // while one is in flight (double key press, or button Click + keyboard D9 in the same
        // frame) would start a concurrent sequence and crash the shared RecordReplayController +
        // reverse-sync group. Ignore the re-entrant trigger; the in-flight sequence continues.
        if (s_recordReplayInProgress)
        {
            ctx.Log("Record/Replay already in progress — ignored.");
            return;
        }
        s_recordReplayInProgress = true;

        // Create a DEDICATED, non-owned ghost that THIS case fully owns and drives. We deliberately
        // do NOT reuse the shared BATCH-12 orbiting ghost: that one is moved every frame by a
        // SEPARATE live orbit hook which keeps running during replay, so playback (which restores
        // the same recorded motion) would be indistinguishable from the continuing live motion —
        // the replay would have no visible effect. Owning the ghost here lets the case decide
        // EXACTLY when live-driving stops (record phase drives it; replay phase does not), so
        // PlaybackTickSystem's effect is unmistakable. Modeled on EnsureOrbitingGhost: a bare
        // SimTransform + TkbIdentity{TkbType=2002} entity → non-owned (Mode-1 ghost), gets a
        // mannequin visual, and is matched by Pass-B's .WithoutOwned<SimTransform>() selector.
        var center = new SNum.Vector3(0f, 8f, 1.0f);
        const float radius = 2.5f;
        var ghost = ctx.World.CreateEntity();
        var startPose = center + new SNum.Vector3(radius, 0f, 0f);
        ctx.World.AddComponent(ghost, new SimTransform
        {
            Position = startPose,
            Rotation = SNum.Quaternion.Identity,
        });
        ctx.World.AddComponent(ghost, new TkbIdentity { TkbType = TkbInfantrySoldier });

        var exerciseId = Guid.NewGuid();
        const float recordSeconds = 3f;
        const float angularSpeed = 1.2f;

        // Phase state machine driven by the per-frame hook. Each phase kicks an async op
        // (module install / file flush need subsequent kernel ticks to complete) and waits for
        // its Task before advancing — meanwhile the hook returning true keeps the kernel ticking.
        // The hook ALSO owns the ghost's live driving: during RECORD it writes SimTransform every
        // frame (orbit); during REPLAY it STOPS writing so PlaybackTickSystem is the only driver.
        int phase = 0;           // 0=start-record, 1=recording, 2=start-replay, 3=replaying, 4=done
        float elapsed = 0f;
        float angle = 0f;
        Task? pending = null;

        // Replay-progress probe: detect whether PlaybackTickSystem is actually moving the ghost.
        SNum.Vector3 replayFirstPos = default;
        SNum.Vector3 replayLastLoggedPos = default;
        bool replayMovedAtAll = false;
        float replayLogAccum = 0f;
        const float replayLogInterval = 0.3f; // ~3/sec position log during playback

        ctx.Log($"Record 3s / Replay: starting (exerciseId={exerciseId:N}). " +
                $"Spawned a DEDICATED ghost (entity={ghost}) at {Fmt(startPose)}; " +
                $"the case will drive it in a circle for {recordSeconds:F0}s while recording, then STOP " +
                "live-driving and let PlaybackTickSystem re-trace it.");

        // Helper: drive the ghost's SimTransform along the orbit (used only during RECORD).
        void DriveGhostLive(float dt)
        {
            if (!ctx.World.IsAlive(ghost)) return;
            angle += angularSpeed * dt;
            ref var tf = ref ctx.World.GetComponentRW<SimTransform>(ghost);
            tf.Position = center + new SNum.Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), 0f);
        }

        ctx.RegisterUpdate(dt =>
        {
            try
            {
            switch (phase)
            {
                case 0: // kick off recording
                    pending = subsystem.RecordReplayController.PrepareRecordingAsync(
                        exerciseId, subsystem.RecordReplayStorageDirectory);
                    phase = 1;
                    elapsed = 0f;
                    return true;

                case 1: // wait for install, then RECORD for recordSeconds while live-driving the ghost
                    if (pending is { IsCompleted: false })
                    {
                        // Keep orbiting even while the recorder install completes so motion is
                        // continuous; the recording proper begins once installed.
                        DriveGhostLive(dt);
                        return true;
                    }
                    if (pending is { IsFaulted: true })
                    {
                        ctx.Log($"Record/Replay: recording install FAILED: {pending!.Exception?.GetBaseException().Message}");
                        FailCleanup();
                        return false;
                    }
                    DriveGhostLive(dt); // RECORD phase: the case drives the ghost live (captured)
                    elapsed += dt;
                    if (elapsed < recordSeconds) return true;

                    // Finalize the recording (flushes LZ4 + writes .meta.json).
                    pending = subsystem.RecordReplayController.FinalizeRecordingAsync();
                    phase = 2;
                    ctx.Log($"Record/Replay: recorded ~{elapsed:F1}s of live orbit; ghost now at " +
                            $"{Fmt(ctx.World.GetComponentRO<SimTransform>(ghost).Position)}; finalizing recording.");
                    return true;

                case 2: // wait for finalize, then start replay
                    if (pending is { IsCompleted: false }) return true;
                    if (pending is { IsFaulted: true })
                    {
                        ctx.Log($"Record/Replay: finalize FAILED: {pending!.Exception?.GetBaseException().Message}");
                        FailCleanup();
                        return false;
                    }
                    // PrepareReplay via the handler: PrepareAsync installs the ReplayModule, then
                    // Commit severs the reverse-sync group (Enabled=false) so PlaybackTickSystem
                    // drives SimTransform from the recording.
                    pending = StartReplayAsync(subsystem, exerciseId);
                    phase = 3;
                    elapsed = 0f;
                    return true;

                case 3: // wait for replay to be live, then run playback WITHOUT live-driving
                    if (pending is { IsCompleted: false }) return true;
                    if (pending is { IsFaulted: true })
                    {
                        ctx.Log($"Record/Replay: replay prepare FAILED: {pending!.Exception?.GetBaseException().Message}");
                        FailCleanup();
                        return false;
                    }
                    if (elapsed == 0f)
                    {
                        // SNAP the ghost to an obviously different pose (origin) at replay start so
                        // playback re-tracing the recorded circle is unmistakable. From here on the
                        // case does NOT write SimTransform — only PlaybackTickSystem should move it.
                        if (ctx.World.IsAlive(ghost))
                        {
                            ref var snap = ref ctx.World.GetComponentRW<SimTransform>(ghost);
                            snap.Position = SNum.Vector3.Zero;
                            snap.Rotation = SNum.Quaternion.Identity;
                        }
                        replayFirstPos = ctx.World.IsAlive(ghost)
                            ? ctx.World.GetComponentRO<SimTransform>(ghost).Position
                            : default;
                        replayLastLoggedPos = replayFirstPos;
                        replayMovedAtAll = false;
                        replayLogAccum = replayLogInterval; // log immediately on the first replay frame
                        ctx.Log($"Record/Replay: replay live — reverse-sync severed (group Enabled={subsystem.ReverseSyncGroup?.Enabled}); " +
                                $"SNAPPED ghost to {Fmt(replayFirstPos)}. The case is NO LONGER driving it — " +
                                "PlaybackTickSystem alone should now re-trace the recorded circle.");
                    }
                    elapsed += dt;

                    // Per-frame replay probe (throttled ~3/sec): log the ghost's position so a human
                    // can confirm playback is actually MOVING it. If it never changes from the snap,
                    // playback is NOT driving SimTransform (silent failure) — call that out.
                    if (ctx.World.IsAlive(ghost))
                    {
                        var pos = ctx.World.GetComponentRO<SimTransform>(ghost).Position;
                        if ((pos - replayFirstPos).Length() > 1e-4f) replayMovedAtAll = true;
                        replayLogAccum += dt;
                        if (replayLogAccum >= replayLogInterval)
                        {
                            replayLogAccum = 0f;
                            float step = (pos - replayLastLoggedPos).Length();
                            ctx.Log($"Record/Replay: [playback t={elapsed:F1}s] ghost SimTransform={Fmt(pos)} " +
                                    $"(moved {step:F3}m since last sample){(step < 1e-4f ? " — NOT MOVING; playback may not be driving SimTransform!" : string.Empty)}");
                            replayLastLoggedPos = pos;
                        }
                    }

                    if (elapsed < recordSeconds) return true;

                    if (!replayMovedAtAll)
                        ctx.Log("Record/Replay: WARNING — ghost SimTransform NEVER changed during the whole " +
                                "replay window. PlaybackTickSystem did not drive it (possible silent-failure; " +
                                "check the '[PlaybackController] Recording has no SchemaManifest' warning).");
                    else
                        ctx.Log("Record/Replay: confirmed playback drove the ghost (SimTransform changed during replay).");

                    // Finalize replay → restores reverse-sync authority.
                    pending = FinalizeReplayAsync(subsystem, exerciseId);
                    phase = 4;
                    return true;

                case 4: // wait for finalize-replay, then done
                    if (pending is { IsCompleted: false }) return true;
                    // Restore: destroy the dedicated ghost so the scene returns to its prior state.
                    if (ctx.World.IsAlive(ghost)) ctx.World.DestroyEntity(ghost);
                    ctx.Log($"Record/Replay: complete — reverse-sync restored (group Enabled={subsystem.ReverseSyncGroup?.Enabled}); " +
                            "dedicated ghost destroyed.");
                    s_recordReplayInProgress = false; // sequence finished; allow a future trigger
                    return false; // stop the hook

                default:
                    FailCleanup();
                    return false;
            }
            }
            catch (Exception ex)
            {
                // Robustness: any unexpected fault inside the hook (e.g. topology churn) must not
                // leave the guard stuck true, or the case could never be triggered again.
                ctx.Log($"Record/Replay: hook faulted — clearing in-progress guard: {ex.Message}");
                FailCleanup();
                return false;
            }

            // Local: clear the guard and tear down the dedicated ghost on any failure/terminal path.
            void FailCleanup()
            {
                try { if (ctx.World.IsAlive(ghost)) ctx.World.DestroyEntity(ghost); } catch { /* best effort */ }
                s_recordReplayInProgress = false;
            }
        });
    }

    private static Task StartReplayAsync(EditorStrideSubsystem subsystem, Guid exerciseId)
    {
        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.PrepareReplay,
            DomainPayload = exerciseId,
        };
        return RunHandlerAsync(subsystem, intent);
    }

    private static Task FinalizeReplayAsync(EditorStrideSubsystem subsystem, Guid exerciseId)
    {
        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.FinalizeReplay,
            DomainPayload = exerciseId,
        };
        return RunHandlerAsync(subsystem, intent);
    }

    private static async Task RunHandlerAsync(EditorStrideSubsystem subsystem, ExecuteNodeOpIntent intent)
    {
        // Mirror the orchestration two-phase path: PrepareAsync (installs/uninstalls the kernel
        // module), then Commit (flips the reverse-sync group's Enabled flag). Run on the harness
        // thread; the async install completes as the kernel keeps ticking from the per-frame hook.
        await subsystem.ReplayLoadHandler.PrepareAsync(intent, System.Threading.CancellationToken.None)
            .ConfigureAwait(false);
        subsystem.ReplayLoadHandler.Commit(intent, subsystem.World);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string Fmt(SNum.Vector3 v) => $"({v.X:F1},{v.Y:F1},{v.Z:F1})";
}
