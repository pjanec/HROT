using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;

namespace Hrot.Look.Effects;

/// <summary>One round, resolved for drawing: its look, where its centre is, the way its nose points, whether it is lying, the seconds left on its fuze (0
/// without one), where it was fired from, its own seed, its speed, its entity and its munition type.</summary>
public readonly record struct RoundState(MunitionVisualDto Look, Vector3 Centre, Vector3 Nose, bool Lying, float FuzeLeft, Vector3 Muzzle, int Seed,
                                         float Speed = 0f, Entity Entity = default, long Ammo = 0);

/// <summary>
/// ⭐ CE-11004 (RR-4, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — ROUNDS IN FLIGHT: where each round is, how its nose points, when it
/// lies or sticks, its tracer streak and its smoke trail's strength. Moved verbatim from the map (<c>ProjectileLayer.Resolve</c> / <c>TracerOf</c> /
/// <c>BurnOf</c> / <c>TrailFactor</c>, P1 / P2 / LR-3 / CE-1190 / CE-1976 / CE-4647), which now calls it — so a renderer and the map place, turn and streak a
/// round the same. ⛔ No colours, no solids: those stay the map's. Sim time only.
/// </summary>
public sealed class RoundLook
{
    /// <summary>A grenade's tumble, turns per second.</summary>
    public const float TumbleTurnsPerSecond = 2.5f;

    /// <summary>⭐ CE-1190 — a throwing star's spin, turns per second (about its normal; it flies flat).</summary>
    public const float SpinTurnsPerSecond = 6f;

    /// <summary>⭐ P2 (§8 PR-F) — a tracer's visible streak: the last <see cref="TracerSeconds"/> of its flight, at most <see cref="TracerMaxLength"/> metres.</summary>
    public const float TracerSeconds = 0.03f, TracerMaxLength = 25f;

    /// <summary>Smoke-trail puffs: one every <see cref="TrailSpacing"/> metres back toward the muzzle, at most <see cref="TrailPuffs"/>.</summary>
    public const float TrailSpacing = 0.9f;

    /// <inheritdoc cref="TrailSpacing"/>
    public const int TrailPuffs = 60;

    private EntityRepository? _queryWorld;
    private EntityQuery? _query;
    private EntityQuery? _replicated;

    /// <summary>⭐ CE-11004 (RR-4) — every round with a look, into <paramref name="list"/> (cleared first): where it is, where its nose points (in flight: its
    /// velocity, a grenade / axe tumbling, a star spinning; lying: flat on the ground the way it went, or a stuck arrow along its rotation), whether it lies,
    /// its fuze, where it was fired from (a viewer's replicated round: back along its flight), its speed. <paramref name="groundHeight"/> is where a lying
    /// round rests (null = its own height). The queries are cached per world (0 B steady, the caller's list).</summary>
    public void Resolve(EntityRepository world, ITkbDatabase? tkb, Func<float, float, float>? groundHeight, List<RoundState> list)
    {
        list.Clear();
        if (!world.IsComponentTypeRegistered<SimTransform>()) return;
        bool combat = world.IsComponentTypeRegistered<BallisticProjectile>();
        bool replicated = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()
                          && world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        if (!combat && !replicated) return;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = combat ? world.Query().With<BallisticProjectile>().With<SimTransform>().Build() : null;
            // ⭐ LR-3 (§8.5, R-271) — a VIEWER's rounds: replicated, so no combat state — the look from the munition type, the nose from
            //   the replicated velocity, at rest = lying. On the muscle the same round also has BallisticProjectile ⇒ drawn once, above.
            if (replicated)
            {
                var q = world.Query().With<Fdp.Toolkit.Replication.Components.LightweightRound>()
                                     .With<Fdp.Toolkit.Replication.Components.TkbIdentity>().With<SimTransform>();
                _replicated = (combat ? q.Without<BallisticProjectile>() : q).Build();
            }
            else _replicated = null;
        }
        bool hasVelocity = world.IsComponentTypeRegistered<SimVelocity>();
        double now = Fdp.Toolkit.Time.SimClock.Of(world).TotalTime;
        if (_replicated != null)
            foreach (var e in _replicated)
            {
                long type = world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType;
                if (MunitionTkbCatalog.VisualOf(tkb, type) is not { } look) continue;
                var at = world.GetComponentRO<SimTransform>(e).Position;
                var velocity = hasVelocity && world.HasComponent<SimVelocity>(e) ? world.GetComponentRO<SimVelocity>(e).Linear : Vector3.Zero;
                int seed = e.Index * 7919 + e.Generation;
                float speed = velocity.Length();
                bool lying = speed < 0.2f;
                Vector3 nose;
                if (lying && look.Sticks)
                {
                    // ⭐ CE-1976 (BW-4, BW-10) — a STUCK arrow / bolt: drawn along its replicated rotation (+X = its flight), at its replicated place (its point already a third inside the surface) — never flat, never lifted onto the ground
                    nose = StuckNose(world.GetComponentRO<SimTransform>(e).Rotation, seed);
                }
                else if (lying)
                {
                    nose = YawDirection(seed);
                    float ground = groundHeight?.Invoke(at.X, at.Y) ?? at.Z;
                    at = new Vector3(at.X, at.Y, MathF.Max(at.Z, ground));
                }
                else
                {
                    nose = velocity / speed;
                    nose = InFlightPose(look.Shape, nose, now, seed);
                }
                // the muzzle is unknown on a viewer: a tracer / smoke trail runs back along the flight, capped by its own limits
                var muzzle = at - (lying ? Vector3.Zero : nose * (look.SmokeTrail ? TrailSpacing * TrailPuffs : TracerMaxLength));
                list.Add(new RoundState(look, at, nose, lying, 0f, muzzle, seed, lying ? 0f : speed, e, type));
            }
        if (_query == null) return;
        foreach (var e in _query)
        {
            ref readonly var round = ref world.GetComponentRO<BallisticProjectile>(e);
            if ((round.Warhead & WarheadRound.Spent) != 0) continue;
            if (MunitionTkbCatalog.VisualOf(tkb, round.Ammo) is not { } look) continue;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            int seed = e.Index * 7919 + e.Generation;
            bool lying = (round.Warhead & WarheadRound.Landed) != 0 || round.StoppedTick != 0;
            var velocity = hasVelocity && world.HasComponent<SimVelocity>(e) ? world.GetComponentRO<SimVelocity>(e).Linear : Vector3.Zero;
            var travel = at - round.PreviousPosition;
            var heading = velocity.LengthSquared() > 1e-4f ? velocity : travel.LengthSquared() > 1e-6f ? travel : Vector3.UnitX;
            Vector3 nose;
            if (lying && look.Sticks)
                nose = StuckNose(world.GetComponentRO<SimTransform>(e).Rotation, seed);   // ⭐ CE-1976 — stuck: along its rotation, as it stopped
            else if (lying)
            {
                // flat on the ground, pointing the way it was going (or a direction of its own if it came straight down)
                var flat = new Vector3(heading.X, heading.Y, 0f);
                nose = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : YawDirection(seed);
                float ground = groundHeight?.Invoke(at.X, at.Y) ?? at.Z;
                at = new Vector3(at.X, at.Y, MathF.Max(at.Z, ground));
            }
            else
            {
                nose = Vector3.Normalize(heading);
                nose = InFlightPose(look.Shape, nose, now, seed);
            }
            list.Add(new RoundState(look, at, nose, lying, (round.Warhead & WarheadRound.TimeFuze) != 0 ? round.FuzeRemaining : 0f, round.Muzzle, seed,
                               lying ? 0f : velocity.Length(), e, round.Ammo));
        }
    }

    /// <summary>⭐ P2 (§8 PR-F) — a tracer round's streak: from the round back along its flight for the last
    /// <see cref="TracerSeconds"/> (≤ <see cref="TracerMaxLength"/>, never past its muzzle). False when it is no tracer, lying, or still.</summary>
    public static bool TracerOf(in RoundState r, out Vector3 head, out Vector3 tail)
    {
        head = tail = r.Centre;
        if (!r.Look.Tracer || r.Lying || r.Speed <= 0f) return false;
        float length = MathF.Min(MathF.Min(r.Speed * TracerSeconds, TracerMaxLength), Vector3.Distance(r.Centre, r.Muzzle));
        if (length <= 0.01f) return false;
        tail = r.Centre - r.Nose * length;
        return true;
    }

    /// <summary>⭐ CE-4647 (AC-15 step 4) — how long a missile's motor burns, seconds: its TKB template's <see cref="Fdp.Toolkit.Tkb.Domain.MissileDto.BurnSeconds"/> (the air lane's contract: IR 5 s, radar 8 s); 0 = no limit (a rocket or a missile whose row says nothing trails smoke all its flight, as before).</summary>
    public static float BurnOf(ITkbDatabase? tkb, long ammo)
        => tkb != null && ammo != 0 && tkb.TryGetByType(ammo, out var t) && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.MissileDto>() is { BurnSeconds: > 0f } m ? m.BurnSeconds : 0f;

    /// <summary>⭐ CE-4647 — how long the smoke already laid lingers after burn-out (the coasting missile is seen to coast).</summary>
    public const float TrailLingerSeconds = 0.8f;

    /// <summary>⭐ CE-4647 — the smoke trail's strength 0…1 by the round's AGE against its motor's burn: full while it burns, fading over <see cref="TrailLingerSeconds"/>, then NONE while it coasts.
    /// <paramref name="burn"/> ≤ 0 (no motor data) ⇒ 1 — unchanged. No allocation.</summary>
    public static float TrailFactor(float burn, double age)
    {
        if (burn <= 0f || double.IsNaN(age) || age <= burn) return 1f;
        return (float)Math.Max(0.0, 1.0 - (age - burn) / TrailLingerSeconds);
    }

    /// <summary>⭐ CE-1190 — the pose a round's nose takes IN FLIGHT: a grenade and a thrown axe tumble end over end, a throwing star spins flat
    /// about the vertical (its nose is the direction of one arm), everything else points along its flight.</summary>
    public static Vector3 InFlightPose(MunitionShape shape, Vector3 flight, double now, int seed) => shape switch
    {
        MunitionShape.Grenade or MunitionShape.Axe => Tumble(flight, now, seed),
        MunitionShape.Star => Spin(now, seed),
        _ => flight,
    };

    /// <summary>⭐ CE-1190 — a flat spin: a horizontal direction turning about the vertical at <see cref="SpinTurnsPerSecond"/>.</summary>
    private static Vector3 Spin(double now, int seed)
    {
        float angle = (float)((now * SpinTurnsPerSecond + (seed & 1023) / 1024.0) % 1.0) * MathF.PI * 2f;
        return new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
    }

    /// <summary>A grenade's tumble: its axis turned end over end about the horizontal perpendicular to its flight.</summary>
    private static Vector3 Tumble(Vector3 flight, double now, int seed)
    {
        var side = Vector3.Cross(flight, Vector3.UnitZ);
        if (side.LengthSquared() < 1e-6f) side = Vector3.UnitY;
        float angle = (float)((now * TumbleTurnsPerSecond + (seed & 1023) / 1024.0) % 1.0) * MathF.PI * 2f;
        return Vector3.Normalize(Vector3.Transform(flight, Quaternion.CreateFromAxisAngle(Vector3.Normalize(side), angle)));
    }

    /// <summary>⭐ CE-1976 — the nose of a STUCK round: its replicated rotation carries +X onto its flight (BW-4); a degenerate rotation falls back to a direction of its own.</summary>
    public static Vector3 StuckNose(Quaternion rotation, int seed)
    {
        var n = Vector3.Transform(Vector3.UnitX, rotation);
        return n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : YawDirection(seed);
    }

    public static Vector3 YawDirection(int seed)
    {
        float a = (seed & 1023) / 1024f * MathF.PI * 2f;
        return new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
    }
}
