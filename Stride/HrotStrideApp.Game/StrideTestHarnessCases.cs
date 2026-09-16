#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using Hrot.Stride.Core.TestHarness;
using SNum = System.Numerics;

namespace HrotStrideApp;

/// <summary>
/// The initial P0–P3 <see cref="VisualTestCase"/>s seeded into the in-app test harness
/// (BATCH-12, STR-TEST-1). All cases are visible/meaningful in the current app — physics is
/// still NoOp, so none depend on Bullet.
///
/// <para>
/// <b>Registration pattern for future phases</b> (P4 animation, P5 gizmos, P6 networking):
/// add a single line per case in your phase wiring —
/// <code>
/// registry.Register(new VisualTestCase("Label", "Description", ctx => { ... }));
/// </code>
/// </para>
/// </summary>
public static class StrideTestHarnessCases
{
    // ── UrbanCombat TKB type constants (match UrbanCombatNewScenario) ──────
    private const long TkbMilitaryApc     = 2001L; // → procedural/model box visual
    private const long TkbInfantrySoldier = 2002L; // → mannequin model visual (capsule shape)

    // Incrementing spawn cursor so successive spawns appear at distinct positions in view.
    // FDP coords: X=East, Y=North, Z=Up. Camera looks toward Stride +Z (FDP +Y).
    private static int s_spawnCursor;

    /// <summary>
    /// Registers the four initial cases into <paramref name="registry"/>, in the order the
    /// harness assigns keyboard shortcuts D1–D4. Returns the same registry for chaining.
    /// </summary>
    public static TestHarnessRegistry RegisterInitialCases(TestHarnessRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        // D1 — Spawn Infantry (2002): a new mannequin appears at an incrementing position.
        registry.Register(new VisualTestCase(
            "Spawn Infantry",
            "Enqueue an InfantrySoldier (TKB 2002); a mannequin appears at the next slot.",
            ctx =>
            {
                var pos = NextSpawnPosition();
                EnqueueSpawn(ctx.ScenarioSource, TkbInfantrySoldier, pos);
                ctx.Log($"Spawn Infantry @ FDP {Fmt(pos)} (mannequin)");
            }));

        // D2 — Spawn Vehicle (2001): a box appears.
        registry.Register(new VisualTestCase(
            "Spawn Vehicle",
            "Enqueue a MilitaryAPC (TKB 2001); a box appears at the next slot.",
            ctx =>
            {
                var pos = NextSpawnPosition();
                EnqueueSpawn(ctx.ScenarioSource, TkbMilitaryApc, pos);
                ctx.Log($"Spawn Vehicle @ FDP {Fmt(pos)} (box)");
            }));

        // D3 — Clear All: destroy every live FDP entity; visuals disappear next tick.
        // Validates §7 death/teardown reconciliation LIVE (Pass-A teardown in
        // SplitAuthorityStrideSyncScript / StrideVisualBindingSystem.SyncExistenceOnly).
        registry.Register(new VisualTestCase(
            "Clear All",
            "Destroy all live FDP entities + stop continuous hooks; visuals vanish next tick.",
            ctx =>
            {
                // Stop any continuous cases (e.g. the orbiting ghost) so they don't try to
                // touch a destroyed entity next frame.
                ctx.ClearUpdates();

                int destroyed = 0;
                // Snapshot the entity list first — destroying mutates the query source.
                var toDestroy = new List<Entity>();
                foreach (var e in ctx.World.Query().With<SimTransform>().Build())
                    toDestroy.Add(e);

                foreach (var e in toDestroy)
                {
                    if (ctx.World.IsAlive(e))
                    {
                        ctx.World.DestroyEntity(e);
                        destroyed++;
                    }
                }

                s_spawnCursor = 0; // reset layout so the next spawn starts fresh
                ctx.Log($"Clear All: destroyed {destroyed} entit{(destroyed == 1 ? "y" : "ies")}; " +
                        "visuals reconcile away next tick.");
            }));

        // D4 — Spawn Orbiting Ghost: a NON-OWNED entity whose SimTransform we move in a
        // circle each frame. Because it is non-owned for SimTransform, Pass-B of
        // SplitAuthorityStrideSyncScript (.WithoutOwned<SimTransform>()) forward-syncs its
        // visual from SimTransform — so the mannequin visibly orbits. This validates the
        // forward-sync → visual path LIVE (the one bit of motion possible without Bullet).
        registry.Register(new VisualTestCase(
            "Spawn Orbiting Ghost",
            "Create a non-owned mannequin and orbit its SimTransform; Pass-B forward-sync moves the visual.",
            ctx => SpawnOrbitingGhost(ctx)));

        return registry;
    }

    // ── Orbiting ghost (non-owned, forward-sync demo) ─────────────────────

    private static void SpawnOrbitingGhost(TestHarnessContext ctx)
    {
        // Create the entity DIRECTLY in the world (not via the spawn/authority path) so it is
        // NON-OWNED for SimTransform: AddComponent does not set the authority bit, and we
        // never call SetAuthority. Pass-B's .WithoutOwned<SimTransform>() therefore matches
        // it and drives its visual from SimTransform each frame.
        //
        // NOTE: it needs TkbIdentity (TKB 2002) so StrideVisualBindingSystem Pass-A resolves a
        // mannequin visual for it, and SimTransform so Pass-B can forward-sync the pose.
        var center = new SNum.Vector3(0f, 8f, 1.0f); // FDP: in front of camera, slightly up
        const float radius = 2.5f;

        var ghost = ctx.World.CreateEntity();
        ctx.World.AddComponent(ghost, new SimTransform
        {
            Position = center + new SNum.Vector3(radius, 0f, 0f),
            Rotation = SNum.Quaternion.Identity,
        });
        ctx.World.AddComponent(ghost, new TkbIdentity { TkbType = TkbInfantrySoldier });

        ctx.Log($"Orbiting Ghost spawned (non-owned, TKB 2002) at FDP {Fmt(center)} r={radius:F1}; " +
                "visual orbits via Pass-B forward-sync.");

        // Continuous hook: move the ghost's SimTransform in a circle each frame.
        // Returns true to keep running; returns false (stops) once the entity is gone
        // (e.g. after Clear All, or if a future case destroys it).
        float angle = 0f;
        const float angularSpeed = 1.2f; // rad/s

        ctx.RegisterUpdate(dt =>
        {
            if (!ctx.World.IsAlive(ghost))
                return false; // entity gone → stop the hook

            angle += angularSpeed * dt;

            // Move in the FDP X/Y (East/North) plane — i.e. the horizontal ground plane.
            ref var tf = ref ctx.World.GetComponentRW<SimTransform>(ghost);
            tf.Position = center + new SNum.Vector3(
                radius * MathF.Cos(angle),
                radius * MathF.Sin(angle),
                0f);

            return true;
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SNum.Vector3 NextSpawnPosition()
    {
        // Lay spawns out left-to-right along FDP X (East), at FDP Y=5 (in front of camera),
        // FDP Z=0 (ground). Wrap to a second row every 6 spawns.
        int col = s_spawnCursor % 6;
        int row = s_spawnCursor / 6;
        s_spawnCursor++;

        float x = -5f + col * 2f;       // -5, -3, -1, 1, 3, 5
        float y = 5f + row * 2f;        // first row at FDP Y=5, next rows further north
        return new SNum.Vector3(x, y, 0f);
    }

    private static void EnqueueSpawn(
        ScenarioEntityCreationRequestSource source, long tkbType, SNum.Vector3 fdpPos)
    {
        source.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0, // localNodeId=0 → authority granted immediately (owned)
            TkbType            = tkbType,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = fdpPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType = tkbType },
            },
        });
    }

    private static string Fmt(SNum.Vector3 v) => $"({v.X:F1},{v.Y:F1},{v.Z:F1})";
}
