using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Look.People;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐ CE-11002 (RR-2, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — the aimed upper body called the way an external renderer calls
/// it: <see cref="UpperBody.Targets"/> per person from replicated state, eased by its own <see cref="PostureBlend"/>. The map forwards to the same functions
/// (its WL-6 / CE-1204 / CE-4621 rails stay the proof that nothing it draws changed).
/// </summary>
public sealed class UpperBodyTests
{
    private static (EntityRepository World, Entity Man, Entity Target) World()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<WeaponPosture>();
        var man = world.CreateEntity();
        world.AddComponent(man, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        var target = world.CreateEntity();
        world.AddComponent(target, new SimTransform { Position = new Vector3(20f, 20f, -10f), Rotation = Quaternion.Identity });
        return (world, man, target);
    }

    /// <summary>
    /// RR-2 — an armed man aimed at a target 45° to his left and below: the torso turns 45° and pitches down, the head (no look of its own) turns back
    /// against it within the neck; at ease: nothing; behind him: clamped at <see cref="UpperBody.MaxAimTwist"/>; unarmed: no torso turn at all.
    /// 🔒 Red-proved: <c>Targets</c> ignoring <c>armed</c> ⇒ red (the unarmed man turns).
    /// </summary>
    [Fact]
    public void RR2_AnArmedManAimed_TurnsAndPitchesToHisTarget_Clamped_AnUnarmedOneDoesNot()
    {
        var (world, man, target) = World();
        world.AddComponent(man, new WeaponPosture { Kind = WeaponPostureKind.Ready, Target = target });
        UpperBody.Targets(world, man, 1.8f, armed: true, out var t);
        Assert.False(t.Aiming);
        Assert.Equal((0f, 0f), (t.Twist, t.Pitch));

        world.SetComponent(man, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = target });
        UpperBody.Targets(world, man, 1.8f, armed: true, out t);
        Assert.True(t.Aiming);
        Assert.Equal(MathF.PI / 4f, t.Twist, 2);
        Assert.True(t.Pitch < -0.2f, $"pitched down at the lower target: {t.Pitch}");
        Assert.True(MathF.Abs(t.Head) <= MathF.PI / 2f);

        world.SetComponent(target, new SimTransform { Position = new Vector3(-10f, 1f, 1f), Rotation = Quaternion.Identity });
        UpperBody.Targets(world, man, 1.8f, armed: true, out t);
        Assert.Equal(UpperBody.MaxAimTwist, t.Twist, 3);

        UpperBody.Targets(world, man, 1.8f, armed: false, out t);
        Assert.False(t.Aiming);
        Assert.Equal(0f, t.Twist);
    }

    /// <summary>RR-2 — a rider aims from his seat's frame up to ~160° round; the easing reaches 95 % of a turn in three time constants and a long gap
    /// snaps (sim time only).</summary>
    [Fact]
    public void RR2_ARiderTurnsFurther_AndTheEasingFollowsSimTime()
    {
        Assert.True(UpperBody.AimInSeat(new Vector3(-10f, 0.001f, UpperBody.SeatShoulder), out float rear, out _));
        Assert.Equal(UpperBody.MaxSeatTwist, rear, 3);
        Assert.False(UpperBody.AimInSeat(new Vector3(0.02f, 0.02f, 0.5f), out _, out _));

        var blend = new PostureBlend();
        var e = new Entity(1, 1);
        Assert.Equal((0f, 0f), blend.Aim(e, 0f, 0f, 10.0));
        var (eased, _) = blend.Aim(e, 1f, 0f, 10.0 + 3 * PostureBlend.AimSeconds);
        Assert.InRange(eased, 0.94f, 0.96f);
        Assert.Equal(-1f, blend.Aim(e, -1f, 0f, 20.0).Twist);   // a gap over a second: snaps
    }
}
