namespace Hrot.Look.Vehicles;

/// <summary>
/// ⭐ CE-11003 (RR-3, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — SPINNING PARTS AND LEANS as rules over replicated state on SIM
/// time: rotor / drone prop / radar antenna / flail drum rates and their spool, the motorcycle's bank into a turn, wheels rolling by the distance driven.
/// Moved verbatim from the map (<c>MotionTracker</c>, CE-1033 S4, CE-1093, CE-1086, CE-4618, CE-4624, CE-4621), which now calls these, so the map and an
/// external renderer spin and lean the same. ⭐ Each is a pure STEP <c>next = f(prev, inputs, dt)</c>: the per-entity state (the angle, the spooled rate,
/// the eased bank) belongs to the caller — two renderers never share one. Pure, 0 B.
/// </summary>
public static class MotionRules
{
    /// <summary>The main rotor's speed (UH-60: about 258 rpm), rev/s.</summary>
    public const float RotorRevsPerSecond = 4.3f;

    /// <summary>⭐ CE-1093 — how long a helicopter's rotor keeps running after it last moved (s), how long it takes to spool up or down (s), and the
    /// smallest movement between two frames that counts (m).</summary>
    public const float RotorRunOnSeconds = 20f, RotorSpoolSeconds = 3f, MovedMetres = 0.02f;

    /// <summary>⭐ CE-1086 — a drone's drawn prop speed (not the real ~150 rev/s, which would alias into a standstill at frame rate), rev/s.</summary>
    public const float DroneRotorRevsPerSecond = 7.3f;

    /// <summary>⭐ CE-4618 — a search radar's antenna, rev/s while the radar radiates, spooling over <see cref="RotorSpoolSeconds"/>.</summary>
    public const float AntennaRevsPerSecond = 0.6f;

    /// <summary>⭐ CE-4624 — a mine clearer's flail drum, rev/s while the flail is on, spooling over <see cref="FlailSpoolSeconds"/> (s).</summary>
    public const float FlailRevsPerSecond = 4f, FlailSpoolSeconds = 1f;

    /// <summary>⭐ CE-4621 — a motorcycle's lean into a turn: <c>atan(v · yawRate / g)</c>, at most <see cref="MaxBank"/> (rad, ~35°), eased with
    /// <see cref="BankSeconds"/> (s). A drawing only.</summary>
    public const float MaxBank = 0.61f, BankSeconds = 0.15f, Gravity = 9.81f;

    /// <summary>Wheel radii: a car's or utility's as a fraction of its body height; a motorcycle's; the air-defence truck's (m).</summary>
    public const float WheelRadiusOfHeight = 0.21f, MotoWheelRadius = 0.31f, SamWheelRadius = 0.6f;

    /// <summary>A spooled rate 0..1 after <paramref name="dt"/> seconds: up toward 1 while <paramref name="on"/>, down toward 0 when not, linearly over
    /// <paramref name="spoolSeconds"/>.</summary>
    public static float Spool(float rate, bool on, float dt, float spoolSeconds)
    {
        float step = dt / spoolSeconds;
        return on ? MathF.Min(1f, rate + step) : MathF.Max(0f, rate - step);
    }

    /// <summary>An angle (radians, wrapped) advanced by a part spinning at <paramref name="rate"/> × <paramref name="revsPerSecond"/> for <paramref name="dt"/> seconds.</summary>
    public static float Spin(float angle, float rate, float revsPerSecond, float dt) => Wrap(angle + rate * revsPerSecond * MathF.PI * 2f * dt);

    /// <summary>⭐ CE-1093 — does a helicopter's rotor run: while airborne, or within <see cref="RotorRunOnSeconds"/> of its last movement (taking off,
    /// landing, taxiing).</summary>
    public static bool RotorRuns(bool airborne, bool hasMoved, double lastMovedAt, double now)
        => airborne || (hasMoved && now - lastMovedAt <= RotorRunOnSeconds);

    /// <summary>⭐ CE-1086 — a live drone's prop angle at sim time <paramref name="now"/> (closed form: no state).</summary>
    public static float DroneProp(double now) => (float)((now * DroneRotorRevsPerSecond % 1.0) * MathF.PI * 2.0);

    /// <summary>⭐ CE-4621 — the bank a rider needs at <paramref name="speed"/> m/s (along the bike's forward, negative reversing) turning at
    /// <paramref name="yawRate"/> rad/s (counter-clockwise positive): <c>−atan(v·ω/g)</c> — a left turn tips the top to the left, a NEGATIVE turn about
    /// the body's +x — clamped to ±<see cref="MaxBank"/>.</summary>
    public static float BankTarget(float speed, float yawRate) => Math.Clamp(-MathF.Atan(speed * yawRate / Gravity), -MaxBank, MaxBank);

    /// <summary>The drawn bank after <paramref name="dt"/> seconds easing toward <paramref name="target"/> (time constant <see cref="BankSeconds"/>).</summary>
    public static float EaseBank(float bank, float target, float dt) => bank + (target - bank) * (1f - MathF.Exp(-dt / BankSeconds));

    /// <summary>A wheel's roll angle (radians, wrapped) after the body moved <paramref name="along"/> metres along its forward (negative reversing) on
    /// wheels of <paramref name="radius"/> metres.</summary>
    public static float Roll(float roll, float along, float radius) => Wrap(roll + along / radius);

    /// <summary>A car's or utility vehicle's wheel radius for a body <paramref name="height"/> metres tall (never under 5 cm).</summary>
    public static float CarWheelRadius(float height) => MathF.Max(0.05f, WheelRadiusOfHeight * height);

    private static float Wrap(float a) => a % (MathF.PI * 2f);
}
