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
    private EntityQuery? _replicated;

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
                                        float Speed = 0f, Entity Entity = default);

    /// <summary>⭐ CE-1091 (docs/DESIGN_Map_3D_Mode.md §6n N-6) — a round's REMEMBERED path: where it was (newest last) and when (sim
    /// seconds), one point every <see cref="PathSpacing"/> metres, at most <see cref="PathPoints"/>. Presentation memory on each host — a
    /// replay jump or a round first seen mid-flight simply starts a short path.</summary>
    public sealed class RoundPath
    {
        public readonly List<(Vector3 At, double T)> Points = new();

        /// <summary>The round is at <paramref name="at"/> at <paramref name="t"/>: a new point once it moved <see cref="PathSpacing"/>
        /// from the last one (the newest point always follows it).</summary>
        public void Add(Vector3 at, double t)
        {
            if (Points.Count >= 2 && Vector3.Distance(Points[^2].At, at) < PathSpacing) { Points[^1] = (at, t); return; }
            Points.Add((at, t));
            if (Points.Count > PathPoints) Points.RemoveAt(0);
        }
    }

    /// <summary>⭐ CE-1091 — a path point every <see cref="PathSpacing"/> m, at most <see cref="PathPoints"/> of them (a minute of a mortar
    /// bomb's arc at its slowest).</summary>
    public const float PathSpacing = 0.9f;

    /// <inheritdoc cref="PathSpacing"/>
    public const int PathPoints = 600;

    /// <summary>⭐ CE-1091 — a shell's or a bomb's faint streak: its path over the last <see cref="StreakSeconds"/>.</summary>
    public const double StreakSeconds = 2.0;

    private readonly Dictionary<Entity, RoundPath> _paths = new();
    private readonly HashSet<Entity> _pathsNow = new();
    private readonly List<Entity> _pathsGone = new();

    /// <summary>The remembered path of round <paramref name="e"/> (null before its first frame).</summary>
    public RoundPath? PathOf(Entity e) => _paths.TryGetValue(e, out var p) ? p : null;

    /// <summary>One solid of a round's body: a cylinder or a cone (<see cref="RadiusFrom"/> ≠ <see cref="RadiusTo"/>) from
    /// <see cref="From"/> to <see cref="To"/>, or a sphere (<see cref="IsSphere"/>, radius <see cref="RadiusFrom"/> at <see cref="From"/>).</summary>
    public readonly record struct Solid(Vector3 From, Vector3 To, float RadiusFrom, float RadiusTo, bool IsSphere, Color Colour);

    /// <summary>Every round with a look — railable without a GL context.</summary>
    public List<Round> Resolve()
    {
        var list = new List<Round>();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<SimTransform>()) return list;
        bool combat = world.IsComponentTypeRegistered<BallisticProjectile>();
        bool replicated = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()
                          && world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        if (!combat && !replicated) return list;
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
        var tkb = _tkb();
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
                if (lying)
                {
                    nose = YawDirection(seed);
                    float ground = GroundHeight?.Invoke(at.X, at.Y) ?? at.Z;
                    at = new Vector3(at.X, at.Y, MathF.Max(at.Z, ground));
                }
                else
                {
                    nose = velocity / speed;
                    if (look.Shape == MunitionShape.Grenade) nose = Tumble(nose, now, seed);
                }
                // the muzzle is unknown on a viewer: a tracer / smoke trail runs back along the flight, capped by its own limits
                var muzzle = at - (lying ? Vector3.Zero : nose * (look.SmokeTrail ? TrailSpacing * TrailPuffs : TracerMaxLength));
                list.Add(new Round(look, at, nose, lying, 0f, muzzle, seed, lying ? 0f : speed, e));
            }
        if (_query == null) { Remember(list, now); return list; }
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
                               lying ? 0f : velocity.Length(), e));
        }
        Remember(list, now);
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

    /// <summary>⭐ CE-1091 — every flying round's path grows by where it is now; rounds gone are forgotten.</summary>
    private void Remember(List<Round> rounds, double now)
    {
        _pathsNow.Clear();
        foreach (var r in rounds)
        {
            if (r.Entity.IsNull || r.Lying) continue;
            _pathsNow.Add(r.Entity);
            if (!_paths.TryGetValue(r.Entity, out var path)) _paths[r.Entity] = path = new RoundPath();
            path.Add(r.Centre, now);
        }
        _pathsGone.Clear();
        foreach (var e in _paths.Keys) if (!_pathsNow.Contains(e)) _pathsGone.Add(e);
        foreach (var e in _pathsGone) _paths.Remove(e);
    }

    /// <summary>
    /// ⭐ CE-1091 (N-6) — the smoke trail along the round's REMEMBERED path (it curves as the round did: a Javelin's climb and dive), puffs
    /// every <see cref="TrailSpacing"/> m back from the round, older thinner and wider; where there is no path yet (a rail, the first frame),
    /// the stateless straight trail back to its muzzle.
    /// </summary>
    public static List<(Vector3 At, float Radius, Color Colour)> TrailOf(in Round r, RoundPath? path)
    {
        if (path == null || path.Points.Count < 2 || !r.Look.SmokeTrail || r.Lying) return TrailOf(r);
        var puffs = new List<(Vector3, float, Color)>();
        // walk the polyline back from the round: the round itself, then the remembered points newest first
        var at = r.Centre;
        float walked = 0f, next = TrailSpacing;
        int i = 1;
        for (int k = path.Points.Count - 1; k >= 0 && i <= TrailPuffs; k--)
        {
            var to = path.Points[k].At;
            float seg = Vector3.Distance(at, to);
            while (seg > 1e-4f && walked + seg >= next && i <= TrailPuffs)
            {
                var p = Vector3.Lerp(at, to, (next - walked) / seg);
                float kk = (float)i / TrailPuffs;
                int key = (int)(next / TrailSpacing) * 7919 + r.Seed;
                float jx = Jitter(key) * (0.15f + 0.8f * kk), jz = Jitter(key + 31) * (0.15f + 0.8f * kk);
                puffs.Add((p + new Vector3(jx, 0f, jz + 0.6f * kk), 0.18f + 1.3f * kk, new Color((byte)205, (byte)202, (byte)196, (byte)(150f * (1f - kk)))));
                next += TrailSpacing;
                i++;
            }
            walked += seg;
            at = to;
        }
        return puffs;
    }

    /// <summary>⭐ CE-1091 (N-6) — a SHELL's or a BOMB's faint streak: its remembered path over the last <see cref="StreakSeconds"/>, so a
    /// high arc reads while it is watched. Empty for any other round, a lying one, or one with no path yet.</summary>
    public static List<(Vector3 From, Vector3 To)> StreakOf(in Round r, RoundPath? path, double now)
    {
        var lines = new List<(Vector3, Vector3)>();
        if (path == null || r.Lying || r.Look.Shape is not (MunitionShape.Shell or MunitionShape.Bomb)) return lines;
        var at = r.Centre;
        for (int k = path.Points.Count - 1; k >= 0; k--)
        {
            var (to, t) = path.Points[k];
            if (now - t > StreakSeconds) break;
            if (Vector3.DistanceSquared(at, to) > 1e-6f) lines.Add((at, to));
            at = to;
        }
        return lines;
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

    /// <summary>⭐ CE-1091 — the streak's colour: a faint warm grey.</summary>
    public static readonly Color StreakColour = new(220, 214, 200, 110);

    public void Draw3D(RenderContext ctx)
    {
        var rounds = Resolve();
        var w = _world();
        double now = w != null ? Fdp.Toolkit.Time.SimClock.Of(w).TotalTime : 0d;
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
            foreach (var (at, radius, colour) in TrailOf(r, PathOf(r.Entity)))
                Raylib.DrawSphereEx(HrotToRaylib.Position(at), radius, 5, 8, colour);
            // ⭐ CE-1091 — a shell's faint streak along its last two seconds
            foreach (var (from, to) in StreakOf(r, PathOf(r.Entity), now))
                Raylib.DrawLine3D(HrotToRaylib.Position(from), HrotToRaylib.Position(to), StreakColour);
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
