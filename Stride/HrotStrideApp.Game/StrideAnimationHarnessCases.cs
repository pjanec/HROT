#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using Hrot.Stride.Animation;
using Hrot.Stride.Core.TestHarness;
using SNum = System.Numerics;

namespace HrotStrideApp;

/// <summary>
/// Phase-4 animation <see cref="VisualTestCase"/>s for the in-app Stride test harness
/// (BATCH-14, STR-P4-T3/T4). These are the human-visible Walk / Run / Jump demonstrations:
/// they spawn a mannequin and then, because physics is still NoOp, <b>drive its
/// <see cref="SimVelocity"/> and advance its <see cref="SimTransform"/> directly each frame</b>
/// (via <see cref="TestHarnessContext.RegisterUpdate"/>) so the locomotion bridge
/// (<see cref="StrideAnimationBridge"/>) feeds that velocity into
/// <c>UpdateLocomotionInputs</c> and the backend blends to walk / run while the mannequin
/// moves. The "Trigger Jump" case fires the off-mesh-link montage path.
///
/// <para>
/// <b>Controls</b> (assigned by the harness in registration order; these are the cases that
/// follow the four BATCH-12 cases, so they get the next D-keys):
/// <list type="bullet">
///   <item><b>Walk Mannequin</b> — spawn a mannequin and drive it forward at walk speed
///     (~1.5 m/s) for a few seconds; the backend's locomotion blend favors <c>Walk</c>.</item>
///   <item><b>Run Mannequin</b> — same at run speed (~4 m/s); blend favors <c>Run</c>.</item>
///   <item><b>Trigger Jump</b> — fire an off-mesh-link Jump traversal on the most-recently
///     spawned animated mannequin; the Jump_Start→Loop→End montage chain plays on its slot.</item>
/// </list>
/// </para>
///
/// <para>
/// Each case logs (via NLog through <see cref="TestHarnessContext.Log"/>) what it triggered
/// and the live backend blend weights / slot state it is producing, so a human watching the
/// log can confirm the bridge logic ran even before the skeletal playback is GPU-verified.
/// </para>
/// </summary>
public static class StrideAnimationHarnessCases
{
    private const long TkbInfantrySoldier = 2002L; // animated mannequin (has CharacterAnimationDefDto)

    // Walk/run target speeds (m/s) chosen to land squarely in the LocomotionBlend bands:
    //   WalkSpeed = 1.5 → pure Walk; RunSpeed = 4.0 → pure Run (see LocomotionBlend).
    private const float WalkSpeedMps = 1.5f;
    private const float RunSpeedMps = 4.0f;
    private const float DriveSeconds = 6.0f; // how long each locomotion demo drives the mannequin

    // Spawn the locomotion-demo mannequins along a fresh row so they don't overlap the
    // BATCH-12 demo spawns.
    private static float s_animRowY = 9f;

    /// <summary>
    /// Register the Walk / Run / Jump cases into <paramref name="registry"/>. The animation
    /// <paramref name="backend"/> and <paramref name="bridge"/> are captured so the cases can
    /// read live blend/slot state for logging and trigger jumps directly.
    /// </summary>
    public static TestHarnessRegistry RegisterAnimationCases(
        TestHarnessRegistry registry,
        StrideAnimationBackend backend,
        StrideAnimationBridge bridge)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (backend == null) throw new ArgumentNullException(nameof(backend));
        if (bridge == null) throw new ArgumentNullException(nameof(bridge));

        registry.Register(new VisualTestCase(
            "Walk Mannequin",
            "Spawn a mannequin and drive it forward at walk speed (~1.5 m/s); locomotion blend → Walk.",
            ctx => SpawnAndDrive(ctx, backend, bridge, WalkSpeedMps, "Walk")));

        registry.Register(new VisualTestCase(
            "Run Mannequin",
            "Spawn a mannequin and drive it forward at run speed (~4 m/s); locomotion blend → Run.",
            ctx => SpawnAndDrive(ctx, backend, bridge, RunSpeedMps, "Run")));

        registry.Register(new VisualTestCase(
            "Trigger Jump",
            "Fire an off-mesh-link Jump on the latest mannequin; Jump_Start→Loop→End plays on its slot.",
            ctx => TriggerJump(ctx, backend, bridge)));

        return registry;
    }

    // ── Walk / Run: spawn + drive SimVelocity/SimTransform each frame ───────

    private static void SpawnAndDrive(
        TestHarnessContext ctx,
        StrideAnimationBackend backend,
        StrideAnimationBridge bridge,
        float speedMps,
        string label)
    {
        // Spawn a mannequin via the Brain spawn path so it is owned and the bridge registers
        // it with the backend on the next bridge tick.
        var startPos = new SNum.Vector3(-6f, s_animRowY, 0f);
        s_animRowY += 2f; // next demo on a fresh row

        var request = new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0, // localNodeId 0 → owned immediately
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new SimVelocity  { Linear = SNum.Vector3.Zero, Angular = SNum.Vector3.Zero },
                new TkbIdentity  { TkbType = TkbInfantrySoldier },
            },
        };
        ctx.ScenarioSource.Enqueue(request);

        ctx.Log($"{label} Mannequin: spawned @ FDP ({startPos.X:F1},{startPos.Y:F1},{startPos.Z:F1}); " +
                $"driving forward at {speedMps:F1} m/s for {DriveSeconds:F0}s.");

        // The spawned entity is not known to us yet (the spawn pipeline runs on the next
        // kernel tick). Resolve it lazily on the first hook tick by finding the newest
        // animated entity near startPos that has zero velocity, then drive that one.
        Entity target = default;
        bool resolved = false;
        float elapsed = 0f;
        // Drive direction: FDP +Y (north), the forward facing used by the demo spawns.
        var dir = new SNum.Vector3(0f, 1f, 0f);

        ctx.RegisterUpdate(dt =>
        {
            if (!resolved)
            {
                if (TryResolveSpawned(ctx.World, startPos, out target))
                {
                    resolved = true;
                    // Set facing along the drive direction (yaw toward +Y/north).
                    SetForwardVelocity(ctx.World, target, dir, speedMps);
                }
                return true; // keep waiting until the spawn materializes
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log($"{label} Mannequin: entity gone — stopping drive.");
                return false;
            }

            elapsed += dt;

            // Each frame: keep SimVelocity at the target speed and advance SimTransform by
            // velocity*dt (physics is NoOp, so we integrate the pose ourselves so the visual
            // visibly moves and the bridge keeps reading a nonzero SimVelocity).
            SetForwardVelocity(ctx.World, target, dir, speedMps);
            ref var tf = ref ctx.World.GetComponentRW<SimTransform>(target);
            tf.Position += dir * speedMps * dt;

            // Log the live locomotion blend the bridge is producing (proof the speed→blend
            // path ran), once we have a backend handle for the entity.
            if (bridge.TryGetHandle(target, out var handle))
            {
                var loco = backend.QueryLocomotion(handle);
                // Log roughly once per second to avoid spamming.
                if ((int)(elapsed - dt) != (int)elapsed)
                    ctx.Log($"{label} Mannequin: speed={speedMps:F1} → blend Idle={loco.Idle:F2} " +
                            $"Walk={loco.Walk:F2} Run={loco.Run:F2}");
            }

            if (elapsed >= DriveSeconds)
            {
                // Stop: zero the velocity so the blend returns to Idle and the visual halts.
                if (ctx.World.HasComponent<SimVelocity>(target))
                {
                    ref var v = ref ctx.World.GetComponentRW<SimVelocity>(target);
                    v.Linear = SNum.Vector3.Zero;
                }
                ctx.Log($"{label} Mannequin: drive complete; velocity zeroed → blend returns to Idle.");
                return false;
            }

            return true;
        });
    }

    // ── Jump: fire the off-mesh-link montage path ──────────────────────────

    private static void TriggerJump(
        TestHarnessContext ctx,
        StrideAnimationBackend backend,
        StrideAnimationBridge bridge)
    {
        // Pick the most-recently-spawned animated mannequin (highest entity index with a
        // backend handle). Falls back to publishing the event to the bus for the first
        // animated entity found.
        Entity target = default;
        bool found = false;
        foreach (var e in ctx.World.Query().With<SimTransform>().With<TkbIdentity>().Build())
        {
            if (ctx.World.GetComponentRO<TkbIdentity>(e).TkbType != TkbInfantrySoldier)
                continue;
            if (!found || e.Index > target.Index)
            {
                target = e;
                found = true;
            }
        }

        if (!found)
        {
            ctx.Log("Trigger Jump: no mannequin present — spawn one first (Walk/Run Mannequin or Spawn Infantry).");
            return;
        }

        // Two equivalent paths, both exercised here:
        //  1. Publish the OffMeshTraversalStartedEvent — the real OffMeshLinkDetectionSystem
        //     seam (the bridge reads it next tick via World.ReadEvents).
        //  2. Call bridge.TriggerJump directly so the montage starts this frame even without
        //     the nav stack wired (the bridge is registered once the entity is known to it).
        ctx.World.Bus.Publish(new OffMeshTraversalStartedEvent
        {
            Target        = target,
            LinkWorldPos  = ctx.World.GetComponentRO<SimTransform>(target).Position,
            TraversalKind = TraversalKind.Jump,
        });
        bridge.TriggerJump(target);

        string slotState = bridge.TryGetHandle(target, out var handle)
            ? (backend.IsAnySlotActive(handle) ? "slot active (Jump_Start playing)" : "slot not yet active")
            : "(entity not yet registered with backend — montage starts once the bridge registers it)";
        ctx.Log($"Trigger Jump: fired off-mesh Jump on mannequin #{target.Index}; {slotState}.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Find the spawned mannequin nearest <paramref name="near"/> that is animated and has a
    /// (near-)zero velocity (i.e. freshly spawned, not yet driven). Returns the best match.
    /// </summary>
    private static bool TryResolveSpawned(EntityRepository world, SNum.Vector3 near, out Entity result)
    {
        result = default;
        bool found = false;
        float bestDistSq = float.MaxValue;

        foreach (var e in world.Query().With<SimTransform>().With<TkbIdentity>().Build())
        {
            if (world.GetComponentRO<TkbIdentity>(e).TkbType != TkbInfantrySoldier)
                continue;

            var pos = world.GetComponentRO<SimTransform>(e).Position;
            float d = (pos - near).LengthSquared();
            if (d < bestDistSq)
            {
                bestDistSq = d;
                result = e;
                found = true;
            }
        }

        // Only accept if reasonably close to the spawn point (the spawn just appeared there).
        return found && bestDistSq < 1.0f;
    }

    private static void SetForwardVelocity(EntityRepository world, Entity e, SNum.Vector3 dir, float speed)
    {
        if (!world.HasComponent<SimVelocity>(e))
            world.AddComponent(e, new SimVelocity());
        ref var v = ref world.GetComponentRW<SimVelocity>(e);
        v.Linear = dir * speed;
    }
}
