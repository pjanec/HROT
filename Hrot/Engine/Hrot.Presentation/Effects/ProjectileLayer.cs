using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Core.Tkb;
using Raylib_cs;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐⭐ P1 (docs/DESIGN_Visual_Effects.md §8 PR-A/PR-B) — <b>rounds in 3-D:</b> every round entity (<see cref="BallisticProjectile"/>)
/// whose MUNITION type has a look (<c>Munition.Visual</c>, <see cref="MunitionTkbCatalog.VisualOf"/>) is drawn as a body — a grenade
/// tumbling through its throw and lying on the ground until its fuze runs out, a mortar bomb nose-first down its arc, a rocket or a
/// missile trailing smoke back to where it was fired, a TRACER round burning a streak along its real flight (P2, PR-F). A round with
/// no look (an ordinary bullet) draws nothing here.
/// <para>On every map host (<c>MapInteractionPack.AttachMapLayers</c>); ⚠ a round exists only on the node that runs combat
/// (§8.1), so other hosts draw nothing until PR-D.</para>
/// <para>Bodies are drawn at least <see cref="MinPixels"/> tall so a 6 cm grenade is visible from map distances; everything is derived
/// from the round's components and the sim clock (stateless), so a pause freezes it and a replay jump is right.</para>
/// </summary>
public sealed class ProjectileLayer : IMapLayer
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly Func<MapCamera3D?>? _camera;
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    /// <summary>The smallest a round's body is drawn, in screen pixels (along its longest side).</summary>
    public const float MinPixels = 10f;

    /// <summary>A grenade's tumble, turns per second.</summary>
    public const float TumbleTurnsPerSecond = 2.5f;

    /// <summary>⭐ P2 (§8 PR-F) — a tracer's visible streak: the last <see cref="TracerSeconds"/> of its flight, at most
    /// <see cref="TracerMaxLength"/> metres, at least <see cref="TracerMinPixels"/> wide.</summary>
    public const float TracerSeconds = 0.03f, TracerMaxLength = 25f, TracerMinPixels = 2.5f;

    /// <summary>Smoke-trail puffs: one every <see cref="TrailSpacing"/> metres back toward the muzzle, at most <see cref="TrailPuffs"/>.</summary>
    public const float TrailSpacing = 0.9f;

    /// <inheritdoc cref="TrailSpacing"/>
    public const int TrailPuffs = 60;

    public ProjectileLayer(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, Func<MapCamera3D?>? camera = null)
    {
        _world = world;
        _tkb = tkb;
        _camera = camera;
    }

    public string Name => "Projectiles";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>The ground height at (x, y) — where a lying round rests. Null ⇒ its own height.</summary>
    public Func<float, float, float>? GroundHeight { get; set; }

    /// <summary>Rounds drawn in the last 3-D frame — the rail's counter.</summary>
    public int Drawn { get; private set; }

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }   // 2-D: ProjectilePresentationGizmo (PR-C) — the one 2-D projector of rounds
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <summary>One round, resolved for drawing: its look, where its centre is, the way its nose points, whether it is lying, the
    /// seconds left on its fuze (0 without one), where it was fired from, and its own seed.</summary>
    public readonly record struct Round(MunitionVisualDto Look, Vector3 Centre, Vector3 Nose, bool Lying, float FuzeLeft, Vector3 Muzzle, int Seed,
                                        float Speed = 0f);

    /// <summary>One solid of a round's body: a cylinder or a cone (<see cref="RadiusFrom"/> ≠ <see cref="RadiusTo"/>) from
    /// <see cref="From"/> to <see cref="To"/>, or a sphere (<see cref="IsSphere"/>, radius <see cref="RadiusFrom"/> at <see cref="From"/>).</summary>
    public readonly record struct Solid(Vector3 From, Vector3 To, float RadiusFrom, float RadiusTo, bool IsSphere, Color Colour);

    /// <summary>Every round with a look — railable without a GL context.</summary>
    public List<Round> Resolve()
    {
        var list = new List<Round>();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<BallisticProjectile>() || !world.IsComponentTypeRegistered<SimTransform>()) return list;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<BallisticProjectile>().With<SimTransform>().Build();
        }
        var tkb = _tkb();
        bool hasVelocity = world.IsComponentTypeRegistered<SimVelocity>();
        double now = Fdp.Toolkit.Time.SimClock.Of(world).TotalTime;
        foreach (var e in _query!)
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
            if (lying)
            {
                // flat on the ground, pointing the way it was going (or a direction of its own if it came straight down)
                var flat = new Vector3(heading.X, heading.Y, 0f);
                nose = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : YawDirection(seed);
                float ground = GroundHeight?.Invoke(at.X, at.Y) ?? at.Z;
                at = new Vector3(at.X, at.Y, MathF.Max(at.Z, ground));
            }
            else
            {
                nose = Vector3.Normalize(heading);
                if (look.Shape == MunitionShape.Grenade) nose = Tumble(nose, now, seed);
            }
            list.Add(new Round(look, at, nose, lying, (round.Warhead & WarheadRound.TimeFuze) != 0 ? round.FuzeRemaining : 0f, round.Muzzle, seed,
                               lying ? 0f : velocity.Length()));
        }
        return list;
    }

    /// <summary>The body's solids at <paramref name="scale"/> × its real size (≥ 1 when drawn far away). A lying round rests ON the
    /// ground (its centre lifted half a diameter).</summary>
    public static List<Solid> SolidsOf(in Round r, float scale = 1f)
    {
        var solids = new List<Solid>();
        float length = r.Look.Length * scale, radius = r.Look.Diameter * 0.5f * scale;
        var colour = Hrot.UI.Common.Map3D.EntityBodyLayer3D.ParseHex(r.Look.ColorHex) ?? new Color(75, 83, 32, 255);
        var dark = new Color((byte)(colour.R * 0.55f), (byte)(colour.G * 0.55f), (byte)(colour.B * 0.55f), (byte)255);
        var metal = new Color(110, 112, 114, 255);
        var centre = r.Lying ? r.Centre + new Vector3(0, 0, radius) : r.Centre;
        var n = r.Nose;
        Vector3 At(float along) => centre + n * (along * length);   // along: −0.5 = tail … +0.5 = nose
        switch (r.Look.Shape)
        {
            case MunitionShape.Grenade:
                solids.Add(new Solid(At(-0.2f), At(0.2f), radius, radius, false, colour));
                solids.Add(new Solid(At(-0.2f), default, radius, radius, true, colour));
                solids.Add(new Solid(At(0.2f), default, radius, radius, true, colour));
                solids.Add(new Solid(At(0.38f), At(0.5f), radius * 0.35f, radius * 0.35f, false, metal));   // the fuze head
                break;
            case MunitionShape.Bomb:
                solids.Add(new Solid(At(0.25f), At(0.5f), radius, radius * 0.25f, false, colour));         // nose
                solids.Add(new Solid(At(-0.15f), At(0.25f), radius, radius, false, colour));               // body
                solids.Add(new Solid(At(-0.4f), At(-0.15f), radius * 0.35f, radius, false, dark));         // boat tail
                solids.Add(new Solid(At(-0.5f), At(-0.36f), radius * 1.05f, radius * 1.05f, false, dark));  // the fin cage
                break;
            case MunitionShape.Shell:
                solids.Add(new Solid(At(0.2f), At(0.5f), radius, radius * 0.1f, false, colour));
                solids.Add(new Solid(At(-0.5f), At(0.2f), radius, radius, false, colour));
                solids.Add(new Solid(At(-0.5f), At(-0.38f), radius * 1.04f, radius * 1.04f, false, metal)); // driving band
                break;
            case MunitionShape.Rocket:
                solids.Add(new Solid(At(0.35f), At(0.5f), radius, radius * 0.1f, false, colour));
                solids.Add(new Solid(At(-0.45f), At(0.35f), radius, radius, false, colour));
                solids.Add(new Solid(At(-0.5f), At(-0.4f), radius * 1.5f, radius * 1.5f, false, dark));    // tail fins
                break;
            case MunitionShape.Bullet:
                break;   // ⭐ P2 — a bullet has no body worth drawing; a tracer one is its streak (TracerOf)
            case MunitionShape.Missile:
                solids.Add(new Solid(At(0.3f), At(0.5f), radius, radius * 0.3f, false, colour));
                solids.Add(new Solid(At(-0.45f), At(0.3f), radius, radius, false, colour));
                solids.Add(new Solid(At(-0.05f), At(0.1f), radius * 1.8f, radius * 1.8f, false, dark));    // mid wings
                solids.Add(new Solid(At(-0.5f), At(-0.4f), radius * 1.6f, radius * 1.6f, false, dark));    // tail fins
                break;
        }
        return solids;
    }

    /// <summary>The smoke trail of a rocket or a missile in flight: puffs back along the line to its muzzle, older (further back)
    /// thinner and wider. Stateless — a puff sits at a fixed distance from the muzzle.</summary>
    public static List<(Vector3 At, float Radius, Color Colour)> TrailOf(in Round r)
    {
        var puffs = new List<(Vector3, float, Color)>();
        if (!r.Look.SmokeTrail || r.Lying) return puffs;
        var back = r.Muzzle - r.Centre;
        float length = back.Length();
        if (length < TrailSpacing) return puffs;
        var dir = back / length;
        var side = Vector3.Cross(dir, Vector3.UnitZ);
        side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitY;
        for (int i = 1; i <= TrailPuffs && i * TrailSpacing < length; i++)
        {
            // ⭐ tuned on the Xvfb frame rail: evenly spaced equal puffs read as a row of beads — now denser, jittered (a hash of the
            //   puff's distance from the muzzle, so it stays put), and spreading as they age
            float d = i * TrailSpacing, k = (float)i / TrailPuffs;
            int key = (int)((length - d) / TrailSpacing) * 7919 + r.Seed;
            float jx = Jitter(key) * (0.15f + 0.8f * k), jz = Jitter(key + 31) * (0.15f + 0.8f * k);
            var at = r.Centre + dir * d + side * jx + new Vector3(0, 0, jz + 0.6f * k);
            puffs.Add((at, 0.18f + 1.3f * k, new Color((byte)205, (byte)202, (byte)196, (byte)(150f * (1f - k)))));
        }
        return puffs;
    }

    /// <summary>⭐ P2 (§8 PR-F) — a tracer round's streak: from the round back along its flight for the last
    /// <see cref="TracerSeconds"/> (≤ <see cref="TracerMaxLength"/>, never past its muzzle). False when it is no tracer, lying, or still.</summary>
    public static bool TracerOf(in Round r, out Vector3 head, out Vector3 tail)
    {
        head = tail = r.Centre;
        if (!r.Look.Tracer || r.Lying || r.Speed <= 0f) return false;
        float length = MathF.Min(MathF.Min(r.Speed * TracerSeconds, TracerMaxLength), Vector3.Distance(r.Centre, r.Muzzle));
        if (length <= 0.01f) return false;
        tail = r.Centre - r.Nose * length;
        return true;
    }

    public void Draw3D(RenderContext ctx)
    {
        var rounds = Resolve();
        var camera = _camera?.Invoke();
        foreach (var r in rounds)
        {
            float scale = 1f;
            if (camera != null)
            {
                float metresPerPixel = camera.MetresPerPixel / MathF.Max(1f, camera.Distance) * Vector3.Distance(camera.Eye, r.Centre);
                float size = MathF.Max(r.Look.Length, r.Look.Diameter);
                if (size > 0f) scale = MathF.Max(1f, MinPixels * metresPerPixel / size);
            }
            foreach (var s in SolidsOf(r, scale))
            {
                if (s.IsSphere) Raylib.DrawSphereEx(HrotToRaylib.Position(s.From), MathF.Max(0.005f, s.RadiusFrom), 6, 8, s.Colour);
                else Raylib.DrawCylinderEx(HrotToRaylib.Position(s.From), HrotToRaylib.Position(s.To), s.RadiusFrom, s.RadiusTo, 8, s.Colour);
            }
            foreach (var (at, radius, colour) in TrailOf(r))
                Raylib.DrawSphereEx(HrotToRaylib.Position(at), radius, 5, 8, colour);
            if (TracerOf(r, out var head, out var tail))
            {
                float width = 0.03f;
                if (camera != null)
                    width = MathF.Max(width, TracerMinPixels * camera.MetresPerPixel / MathF.Max(1f, camera.Distance) * Vector3.Distance(camera.Eye, r.Centre) / 2f);
                var glow = Hrot.UI.Common.Map3D.EntityBodyLayer3D.ParseHex(r.Look.TracerColorHex) ?? new Color(255, 154, 48, 255);
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawCylinderEx(HrotToRaylib.Position(tail), HrotToRaylib.Position(head), width * 0.4f, width, 6, glow);
                Raylib.EndBlendMode();
            }
        }
        Drawn = rounds.Count;
    }

    /// <summary>A grenade's tumble: its axis turned end over end about the horizontal perpendicular to its flight.</summary>
    private static Vector3 Tumble(Vector3 flight, double now, int seed)
    {
        var side = Vector3.Cross(flight, Vector3.UnitZ);
        if (side.LengthSquared() < 1e-6f) side = Vector3.UnitY;
        float angle = (float)((now * TumbleTurnsPerSecond + (seed & 1023) / 1024.0) % 1.0) * MathF.PI * 2f;
        return Vector3.Normalize(Vector3.Transform(flight, Quaternion.CreateFromAxisAngle(Vector3.Normalize(side), angle)));
    }

    private static float Jitter(int key)
    {
        unchecked { uint h = (uint)key * 0x9E3779B1u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 13; return ((h & 0xFFFF) / 65535f) - 0.5f; }
    }

    private static Vector3 YawDirection(int seed)
    {
        float a = (seed & 1023) / 1024f * MathF.PI * 2f;
        return new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
    }
}
