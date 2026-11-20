using Hrot.Look.Vehicles;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐ CE-11003 (RR-3, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — spinning parts and leans as a renderer calls them: pure steps
/// over replicated speed / yaw rate / on-off state on sim time. The map's <c>MotionTracker</c> calls the same functions (its S4 / CE-1093 / CE-1353 /
/// motorcycle / flail / antenna rails stay the proof that nothing it draws changed).
/// </summary>
public sealed class MotionRulesTests
{
    /// <summary>
    /// RR-3 — a motorcycle at 10 m/s turning left at 0.5 rad/s leans LEFT (a negative turn about +x) by atan(v·ω/g) ≈ 27°; a sharper turn caps at 35°;
    /// reversing flips it; the drawn bank reaches 63 % of the target in one <see cref="MotionRules.BankSeconds"/>. 🔒 Red-proved: the sign dropped ⇒ red.
    /// </summary>
    [Fact]
    public void RR3_AMotorcycleBanksIntoItsTurn_AtanVOmegaOverG_CappedAt35Degrees()
    {
        Assert.Equal(-MathF.Atan(10f * 0.5f / 9.81f), MotionRules.BankTarget(10f, 0.5f), 5);
        Assert.Equal(-MotionRules.MaxBank, MotionRules.BankTarget(30f, 2f), 5);
        Assert.Equal(MotionRules.MaxBank, MotionRules.BankTarget(30f, -2f), 5);
        Assert.Equal(0f, MotionRules.BankTarget(0f, 1f));
        float eased = MotionRules.EaseBank(0f, -0.4f, MotionRules.BankSeconds);
        Assert.Equal(-0.4f * (1f - MathF.Exp(-1f)), eased, 5);
    }

    /// <summary>RR-3 — a helicopter's rotor runs while airborne or for 20 s after it last moved, spools 0→1 over 3 s and spins 4.3 rev/s at full rate; a flail
    /// spools over 1 s; a drone's prop is closed form; a car's wheel of a 1.5 m body rolls distance / (0.21·1.5).</summary>
    [Fact]
    public void RR3_RotorsSpoolAndSpin_OnSimTime_WheelsRollByDistance()
    {
        Assert.True(MotionRules.RotorRuns(airborne: true, hasMoved: false, 0, 100));
        Assert.True(MotionRules.RotorRuns(false, true, lastMovedAt: 90, now: 105));
        Assert.False(MotionRules.RotorRuns(false, true, lastMovedAt: 80, now: 105));

        Assert.Equal(0.5f, MotionRules.Spool(0f, on: true, dt: 1.5f, MotionRules.RotorSpoolSeconds), 5);
        Assert.Equal(1f, MotionRules.Spool(0.9f, true, 1f, MotionRules.RotorSpoolSeconds));
        Assert.Equal(0f, MotionRules.Spool(0.2f, false, 0.5f, MotionRules.FlailSpoolSeconds));
        Assert.Equal(0.25f * 2f * MathF.PI, MotionRules.Spin(0f, 1f, MotionRules.RotorRevsPerSecond, 0.25f / MotionRules.RotorRevsPerSecond), 4);

        Assert.Equal(MotionRules.DroneProp(0.0), MotionRules.DroneProp(1.0 / MotionRules.DroneRotorRevsPerSecond), 3);
        Assert.Equal(0.315f, MotionRules.CarWheelRadius(1.5f), 5);
        Assert.Equal(1f, MotionRules.Roll(0f, 0.315f, MotionRules.CarWheelRadius(1.5f)), 5);
    }
}
