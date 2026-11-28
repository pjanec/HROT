using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Look.Effects;

/// <summary>⭐ CE-11004 (RR-4) — what a <see cref="FlameSpec"/> is.</summary>
public enum FlameKind : byte
{
    /// <summary>A burning spot of a holder (a vehicle, a wagon, a building, a cell): flames of <see cref="FlameSpec.Size"/> at <see cref="FlameSpec.At"/>, a small smoke when <see cref="FlameSpec.Smoke"/>.</summary>
    Spot = 0,
    /// <summary>A lit flame jet: from the muzzle <see cref="FlameSpec.At"/> to <see cref="FlameSpec.To"/>, a cone of <see cref="FlameSpec.HalfAngle"/>.</summary>
    Jet,
    /// <summary>A burning person: one body-sized fire at his feet (no smoke).</summary>
    BurningPerson,
    /// <summary>Scorched ground: a dark rectangle <see cref="FlameSpec.Rect"/> (length × width) along <see cref="FlameSpec.Heading"/>, centred at <see cref="FlameSpec.At"/> (z 0: lay it on the ground).</summary>
    Scorch,
}

/// <summary>⭐ CE-11004 (RR-4) — one fire the map draws from STATE (not from an effect entity): its kind, where, how big, how far through its burn, how strong,
/// its seed (the flicker's), and per kind the jet's end and cone or the scorch's rectangle.</summary>
public struct FlameSpec
{
    public FlameKind Kind;
    /// <summary>The entity the fire belongs to (the holder, the jet's shooter, the burning man, the marks' record).</summary>
    public Entity Holder;
    public Vector3 At;
    /// <summary>A jet's end (<see cref="FlameKind.Jet"/>).</summary>
    public Vector3 To;
    /// <summary>The flames' size, metres (<see cref="FlameKind.Spot"/> / <see cref="FlameKind.BurningPerson"/>).</summary>
    public float Size;
    /// <summary>How far through its burn, 0…1 (a spot); 0 otherwise.</summary>
    public float Phase;
    /// <summary>Seconds since it started (sim time).</summary>
    public float Age;
    /// <summary>0…1.</summary>
    public float Intensity;
    public int Seed;
    /// <summary>A jet's cone half-angle, radians.</summary>
    public float HalfAngle;
    /// <summary>A scorch's length (X) and width (Y), metres.</summary>
    public Vector2 Rect;
    /// <summary>A scorch's heading, radians.</summary>
    public float Heading;
    /// <summary>A spot sends up a small smoke (<see cref="FlameLook.SpotSmoke"/>).</summary>
    public bool Smoke;
}

/// <summary>
/// ⭐ CE-11004 (RR-4, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — THE FLAME LOOK from replicated state: every burning
/// <c>FireState</c> spot, every lit flame jet (<see cref="FlameJets.TryGet"/>), every burning person (<see cref="Hrot.Look.People.PersonLook.IsBurning"/>) and every
/// <c>BurnedArea</c> mark. Moved from the map (<c>EffectLayer.ResolveStateInto</c>, CE-4635 / CE-4637, flame weapons §8 U-1…U-4), which now draws these specs
/// (adding its own colours and looks) — so a renderer and the map burn the same places, the same size, on the same clock. Stateless (sim time and the
/// replicated state; a replay and a second node agree); the queries are cached per world; 0 B steady (the caller's list).
/// </summary>
public sealed class FlameLook
{
    /// <summary>The smoke a burning spot sends up, as a fraction of its flame size.</summary>
    public const float SpotSmokeShare = 0.3f;

    /// <summary>The slot a burning MAN's one fire takes in its seed (a holder's FireState has slots 0…Capacity−1).</summary>
    public const int ManFireSlot = 7;

    /// <summary>How big a spot's flames are from its radius: <see cref="SpotSizePerRadius"/> of it, within 0.3–8 m.</summary>
    public const float SpotSizePerRadius = 0.7f;

    /// <inheritdoc cref="SpotSizePerRadius"/>
    public static float SpotSize(float radius) => Math.Clamp(radius * SpotSizePerRadius, 0.3f, 8f);

    /// <summary>Where a jet starts: the shooter's muzzle on mount <paramref name="mount"/> and the way it points. A host that draws bodies passes its own (the
    /// map: the TKB barrel, else the rifle it drew); null ⇒ <see cref="DefaultMuzzle"/>.</summary>
    public delegate Vector3 MuzzleOf(EntityRepository world, Entity shooter, int mount, out Vector3 forward);

    /// <summary>A burning person's feet and height as the host draws him; false ⇒ his position and 1.8 m.</summary>
    public delegate bool BodyOf(EntityRepository world, Entity person, out Vector3 feet, out float height);

    private EntityRepository? _world;
    private EntityQuery? _fireQuery, _jetQuery, _burnQuery, _markQuery;

    /// <summary>The engine's muzzle (<c>Muzzle.TryOf</c>), else 1.5 m above the shooter with no direction.</summary>
    public static Vector3 DefaultMuzzle(EntityRepository world, Entity shooter, int mount, out Vector3 forward)
    {
        if (Muzzle.TryOf(world, shooter, mount, out var barrel, out forward)) return barrel;
        forward = Vector3.Zero;
        return world.HasComponent<SimTransform>(shooter) ? world.GetComponentRO<SimTransform>(shooter).Position + new Vector3(0, 0, 1.5f) : Vector3.Zero;
    }

    /// <summary>A holder's point <c>(x, y, z)</c> in the world: a vehicle's spots are in ITS frame; a building's or a cell's record has an identity rotation.</summary>
    public static Vector3 HolderPoint(EntityRepository world, Entity holder, float x, float y, float z)
    {
        var p = new Vector3(x, y, z);
        if (!world.HasComponent<SimTransform>(holder)) return p;
        ref readonly var t = ref world.GetComponentRO<SimTransform>(holder);
        Quaternion q = t.Rotation;
        if (q.LengthSquared() < 0.5f) q = Quaternion.Identity;
        return t.Position + Vector3.Transform(p, q);
    }

    /// <summary>A spot's small smoke: its size, opacity (fading over the last 30 % of the burn) and seed.</summary>
    public static void SpotSmoke(in FlameSpec spot, out float size, out float opacity, out int seed)
    {
        float fadeOut = spot.Phase < 0.7f ? 1f : Math.Clamp(1f - (spot.Phase - 0.7f) / 0.3f, 0f, 1f);
        size = MathF.Max(spot.Size * SpotSmokeShare, 0.25f);
        opacity = fadeOut * spot.Intensity * 0.7f;
        seed = spot.Seed ^ 0x5BD1E995;
    }

    /// <summary>
    /// Every fire of the world at sim time <paramref name="now"/>, APPENDED to <paramref name="into"/> in the map's order: spots, jets, burning people, scorch.
    /// <paramref name="kinds"/> masks what is wanted (1 &lt;&lt; (int)<see cref="FlameKind"/>; the map asks only for the kinds whose TKB look exists).
    /// </summary>
    public void Resolve(EntityRepository world, double now, List<FlameSpec> into, MuzzleOf? muzzleOf = null, BodyOf? bodyOf = null, int kinds = 0xF)
    {
        if (!ReferenceEquals(world, _world))
        {
            _world = world;
            _fireQuery = world.IsComponentTypeRegistered<FireState>() ? world.Query().With<FireState>().Build() : null;
            _jetQuery = world.IsComponentTypeRegistered<WeaponPosture>() && world.IsComponentTypeRegistered<WeaponAmmo>()
                ? world.Query().With<WeaponPosture>().With<WeaponAmmo>().With<SimTransform>().Build() : null;
            _burnQuery = world.IsComponentTypeRegistered<EffectState>() ? world.Query().With<EffectState>().With<SimTransform>().Build() : null;
            _markQuery = world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.GroundHeatMarks>()
                ? world.Query().With<Fdp.Toolkit.Terrain.GroundHeatMarks>().Build() : null;
        }

        // ---- U-2 — every burning spot of every holder ----
        if (_fireQuery != null && (kinds & (1 << (int)FlameKind.Spot)) != 0)
            foreach (var e in _fireQuery)
            {
                ref readonly var fs = ref world.GetComponentRO<FireState>(e);
                int n = Math.Min((int)fs.Count, FireState.Capacity);
                for (int i = 0; i < n; i++)
                {
                    var spot = fs.Get(i);
                    if (!spot.IsBurning(now)) continue;
                    into.Add(new FlameSpec
                    {
                        Kind = FlameKind.Spot, Holder = e, At = HolderPoint(world, e, spot.X, spot.Y, spot.Z), Size = SpotSize(spot.Radius),
                        Phase = (float)((now - spot.Since.Seconds) / spot.Seconds), Age = (float)(now - spot.Since.Seconds),
                        Intensity = spot.Intensity > 0f ? MathF.Min(spot.Intensity, 1f) : 1f, Seed = e.Index * 8 + i, Smoke = true,
                    });
                }
            }

        // ---- U-1 — every lit flame jet ----
        if (_jetQuery != null && (kinds & (1 << (int)FlameKind.Jet)) != 0)
            foreach (var e in _jetQuery)
            {
                if (!FlameJets.TryGet(world, e, now, out var jet)) continue;
                var muzzle = muzzleOf != null ? muzzleOf(world, e, jet.Mount, out var forward) : DefaultMuzzle(world, e, jet.Mount, out forward);
                Vector3 aim = jet.AimPoint;
                var to = aim - muzzle;
                float dist = to.Length();
                Vector3 dir = dist > 0.5f ? to / dist : (forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : Vector3.UnitX);
                float length = dist > 0.5f ? MathF.Min(dist, jet.RangeM) : jet.RangeM;   // ⚠ v1: no wall clipping — the simulation shortens the jet (FL-1)
                into.Add(new FlameSpec
                {
                    Kind = FlameKind.Jet, Holder = e, At = muzzle, To = muzzle + dir * length, Intensity = jet.Intensity, Seed = e.Index * 8,
                    Age = (float)MathF.Max(0f, (float)(now - jet.BurstStartedAt)), HalfAngle = jet.HalfAngleRad,
                });
            }

        // ---- U-3 — every burning man: one body-sized fire ----
        if (_burnQuery != null && (kinds & (1 << (int)FlameKind.BurningPerson)) != 0)
            foreach (var e in _burnQuery)
            {
                if (!Hrot.Look.People.PersonLook.IsBurning(world, e, now)) continue;
                if (bodyOf == null || !bodyOf(world, e, out var feet, out float height))
                {
                    feet = world.GetComponentRO<SimTransform>(e).Position;
                    height = 1.8f;
                }
                into.Add(new FlameSpec
                {
                    Kind = FlameKind.BurningPerson, Holder = e, At = feet, Size = Math.Clamp(height * 0.8f, 0.6f, 2.4f), Age = (float)(now % 3600.0),
                    Intensity = 1f, Seed = e.Index * 8 + ManFireSlot,
                });
            }

        // ---- U-4 — scorched ground ----
        if (_markQuery != null && (kinds & (1 << (int)FlameKind.Scorch)) != 0)
            foreach (var e in _markQuery)
            {
                ref readonly var marks = ref world.GetComponentRO<Fdp.Toolkit.Terrain.GroundHeatMarks>(e);
                int n = Math.Min((int)marks.Count, Fdp.Toolkit.Terrain.GroundHeatMarks.Capacity);
                for (int i = 0; i < n; i++)
                {
                    var m = marks.Get(i);
                    if (m.Kind != Fdp.Toolkit.Terrain.HeatMarkKind.BurnedArea || m.Length <= 0f || m.Width <= 0f) continue;
                    into.Add(new FlameSpec
                    {
                        Kind = FlameKind.Scorch, Holder = e, At = new Vector3(m.At.X, m.At.Y, 0f), Intensity = 1f, Seed = e.Index * 16 + i,
                        Rect = new Vector2(m.Length, m.Width), Heading = m.Heading,
                    });
                }
            }
    }
}
