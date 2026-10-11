using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Core.Tkb;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Map3D;
using Raylib_cs;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐⭐ CE-1042 E3 — draws the realism EFFECT entities on the map, in BOTH modes, from their TKB type (docs/DESIGN_Visual_Effects.md
/// VE-F): <b>2-D</b> a star (muzzle flash), an expanding disc (explosion), a dark blot (decal), a short streak (tracer); <b>3-D</b>
/// glowing spheres for flash and fireball (additive), a dark disc DRAPED on the ground for the decal (R-248), a glowing streak for
/// the tracer. Every look — size, colour, duration, fade — is the type's <see cref="EffectVisualDto"/>, drawn at the phase
/// <c>Age / Duration</c>. Attached by <c>MapInteractionPack.AttachMapLayers</c>, so every map host has it (R-254). No gizmos (U2).
/// <para>⚠ The muzzle (VE-E): until <c>Muzzle.Of</c> (E4, VE-K) a flash and a tracer start at the FRONT of the shooter's drawn box,
/// in its upper half — the same box in 2-D and 3-D, so the two modes agree.</para>
/// <para>⚠ Deviation, argued: ONE layer with both passes rather than <c>EffectLayer2D</c> + <c>EffectLayer3D</c> — the query, the TKB
/// lookups and the muzzle are shared, as in the gizmo layer.</para>
/// </summary>
public sealed class EffectLayer : IMapLayer
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly EntityBodyLayer3D? _bodies;
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    public EffectLayer(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, EntityBodyLayer3D? bodies = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
        _bodies = bodies;
    }

    public string Name => "Effects";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>The ground height at (x, y) — where a decal is draped in 3-D. Null ⇒ the decal's own height.</summary>
    public Func<float, float, float>? GroundHeight { get; set; }

    /// <summary>Effects drawn in the last frame (either mode) — the rail's counter.</summary>
    public int Drawn { get; private set; }

    public void Update(float dt) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <summary>One effect, resolved for drawing: what it is, where, how far through its life, how opaque — and (W3) its own seed and
    /// age in seconds, from which its particles are derived (stateless, VE-P).</summary>
    public readonly record struct Resolved(EffectVisualDto Look, Vector3 At, Vector3 Toward, float Phase, float Opacity, Color Colour,
                                           int Seed = 0, float Age = 0f);

    /// <summary>Every live effect resolved for drawing — railable without a GL context.</summary>
    public List<Resolved> Resolve()
    {
        var list = new List<Resolved>();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<Fdp.Toolkit.Time.Lifespan>() || !world.IsComponentTypeRegistered<TkbIdentity>()) return list;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<Fdp.Toolkit.Time.Lifespan>().With<TkbIdentity>().With<SimTransform>().Build();
        }
        var tkb = _tkb();
        bool anchored = world.IsComponentTypeRegistered<EffectAnchor>();
        // ⭐ W1 (VE-Q, VE-G) — the phase is the SIM clock's: paused ⇒ frozen; in a replay, the recording's own time (VE-R).
        double now = Fdp.Toolkit.Time.SimClock.Of(world).TotalTime;
        foreach (var e in _query!)
        {
            if (EffectTkbCatalog.VisualOf(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) is not { } look) continue;
            var life = world.GetComponentRO<Fdp.Toolkit.Time.Lifespan>(e);
            var at = world.GetComponentRO<SimTransform>(e).Position;
            var toward = Vector3.Zero;
            if (anchored && world.HasComponent<EffectAnchor>(e))
            {
                var a = world.GetComponentRO<EffectAnchor>(e);
                if (a.Shooter == Entity.Null || !world.IsAlive(a.Shooter)) continue;   // VE-J: the shooter gone ⇒ the effect ends
                if (a.Kind == EffectAnchorKind.Body)
                {
                    // ⭐ W3 (VE-P) — flames and smoke sit on the wreck's top and scale with its footprint (a car burns smaller than a tank).
                    at = BodyTopOf(world, a.Shooter, tkb, out float scale);
                    look = look with { Size = look.Size * scale };
                }
                else
                {
                    at = MuzzleOf(world, a.Shooter, a.WeaponIndex, tkb);
                    toward = a.Toward;
                }
            }
            if (life.IsOver(now)) continue;   // its time passed this frame; LifespanSystem removes it (or the replay already did)
            float remaining = life.Remaining(now);
            float opacity = look.FadeSeconds > 0f && remaining < look.FadeSeconds ? Math.Clamp(remaining / look.FadeSeconds, 0f, 1f) : 1f;
            var colour = ParseRgba(look.ColorHex);
            list.Add(new Resolved(look, at, toward, life.Phase(now), opacity, colour, Seed: e.Index * 7919 + e.Generation, Age: life.Age(now)));
        }
        // decals underneath, then bursts, then fire, then smoke over it, then flashes and tracers
        list.Sort((x, y) => Order(x.Look.Kind).CompareTo(Order(y.Look.Kind)));
        return list;
    }

    private static int Order(EffectKind k) => k switch
    {
        EffectKind.Decal => 0, EffectKind.Explosion => 1, EffectKind.Fire => 2, EffectKind.Smoke => 3, _ => 4,
    };

    /// <summary>The tank-sized footprint the Fire / Smoke sizes are written for (m, the longer side).</summary>
    public const float ReferenceWreckLength = 7f;

    /// <summary>⭐ W3 (VE-P) — the top centre of an entity's drawn body, and how much bigger or smaller than a tank it is (0.4–2).</summary>
    public Vector3 BodyTopOf(EntityRepository world, Entity body, ITkbDatabase? tkb, out float scale)
    {
        scale = 1f;
        if (_bodies != null && _bodies.TryGetBox(world, body, tkb, out var centre, out var rotation, out var half))
        {
            scale = Math.Clamp(2f * MathF.Max(half.X, half.Y) / ReferenceWreckLength, 0.4f, 2f);
            return centre + Vector3.Transform(new Vector3(0f, 0f, half.Z), rotation);
        }
        return world.HasComponent<SimTransform>(body) ? world.GetComponentRO<SimTransform>(body).Position + new Vector3(0, 0, 2f) : Vector3.Zero;
    }

    /// <summary>One particle of a fire or a smoke column: where, how big, what colour (alpha included).</summary>
    public readonly record struct Puff(Vector3 At, float Radius, Color Colour);

    /// <summary>The puffs fire burns with, and that smoke is drawn from.</summary>
    public const int FirePuffs = 10, SmokePuffs = 14;

    /// <summary>
    /// ⭐ W3 (VE-P) — the particles of a <see cref="EffectKind.Fire"/> or <see cref="EffectKind.Smoke"/>, derived from nothing but the
    /// effect's seed and age (no particle entities, no per-frame state): puff i runs a loop of its own period, phase-shifted by a hash
    /// of (seed, i), rising from the anchor; so a replay jump, a pause or a second host draws exactly the same puffs.
    /// <list type="bullet">
    ///   <item>Fire: short tongues (0.8 s) rising one size and shrinking, yellow → red; they die down over the last 30 % of its life.</item>
    ///   <item>Smoke: slow puffs (6 s) rising six sizes, growing, drifting downwind, thinning out; the effect's fade dims the column.</item>
    /// </list>
    /// </summary>
    public static List<Puff> Puffs(in Resolved r)
    {
        var list = new List<Puff>();
        float size = r.Look.Size;
        if (r.Look.Kind == EffectKind.Fire)
        {
            float strength = r.Phase < 0.7f ? 1f : Math.Clamp(1f - (r.Phase - 0.7f) / 0.3f, 0f, 1f);
            for (int i = 0; i < FirePuffs; i++)
            {
                float t = Frac(r.Age / 0.8f + Hash(r.Seed, i));
                float jx = (Hash(r.Seed, i + 101) - 0.5f) * size * 0.7f, jy = (Hash(r.Seed, i + 211) - 0.5f) * size * 0.7f;
                var at = r.At + new Vector3(jx * (1f - 0.5f * t), jy * (1f - 0.5f * t), t * size * strength);
                float radius = size * 0.28f * (1f - 0.6f * t) * (0.4f + 0.6f * strength);
                var colour = Lerp(new Color(255, 230, 120, 255), new Color(220, 50, 20, 255), t);
                list.Add(new Puff(at, radius, Fade(colour, (1f - t) * strength * r.Opacity)));
            }
        }
        else if (r.Look.Kind == EffectKind.Smoke)
        {
            var downwind = new Vector3(0.8f, 0.6f, 0f);   // a fixed light breeze until weather exists
            for (int i = 0; i < SmokePuffs; i++)
            {
                float t = Frac(r.Age / 6f + Hash(r.Seed, i));
                float jx = (Hash(r.Seed, i + 307) - 0.5f) * size * 0.5f, jy = (Hash(r.Seed, i + 401) - 0.5f) * size * 0.5f;
                var at = r.At + new Vector3(jx, jy, size * 0.3f + t * size * 6f) + downwind * (t * t * size * 3f);
                float radius = size * (0.35f + 1.2f * t);
                list.Add(new Puff(at, radius, Fade(r.Colour, (1f - t) * r.Opacity)));
            }
        }
        return list;
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>A stable hash of (seed, i) in [0, 1).</summary>
    private static float Hash(int seed, int i)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u ^ (uint)i * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static Color Lerp(Color a, Color b, float t)
        => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));


    /// <summary>Where a flash and a tracer start: ⭐ E4 — <see cref="Fdp.Toolkit.Combat.Muzzle.TryOf"/> (the same point the round
    /// left from and the drawn barrel tip); a type with no muzzle data: the front of the shooter's drawn box, in its upper half.</summary>
    public Vector3 MuzzleOf(EntityRepository world, Entity shooter, ITkbDatabase? tkb) => MuzzleOf(world, shooter, 0, tkb);

    /// <inheritdoc cref="MuzzleOf(EntityRepository, Entity, ITkbDatabase?)"/>
    public Vector3 MuzzleOf(EntityRepository world, Entity shooter, int weaponIndex, ITkbDatabase? tkb)
    {
        if (Fdp.Toolkit.Combat.Muzzle.TryOf(world, shooter, weaponIndex, out var barrel)) return barrel;
        if (_bodies != null && _bodies.TryGetBox(world, shooter, tkb, out var centre, out var rotation, out var half))
            return centre + Vector3.Transform(new Vector3(half.X + 0.15f, 0f, half.Z * 0.5f), rotation);
        return world.HasComponent<SimTransform>(shooter) ? world.GetComponentRO<SimTransform>(shooter).Position + new Vector3(0, 0, 1.5f) : Vector3.Zero;
    }

    // ---- 2-D --------------------------------------------------------------------------------------------

    public void Draw(RenderContext ctx)
    {
        float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
        float px = 1f / zoom;   // one screen pixel in metres
        var effects = Resolve();
        foreach (var r in effects)
        {
            var c = Fade(r.Colour, r.Opacity);
            var p = new Vector2(r.At.X, r.At.Y);
            float size = r.Look.Size;
            switch (r.Look.Kind)
            {
                case EffectKind.Decal:
                    Raylib.DrawCircleV(p, MathF.Max(size / 2f, 1.5f * px), c);
                    break;
                case EffectKind.Explosion:
                {
                    float radius = MathF.Max(size / 2f * (0.3f + 0.7f * r.Phase), 2f * px);
                    Raylib.DrawCircleV(p, radius, Fade(c, 1f - 0.6f * r.Phase));
                    Raylib.DrawRing(p, MathF.Max(0f, radius - px), radius, 0f, 360f, 32, Fade(c, 1f - r.Phase));
                    break;
                }
                case EffectKind.MuzzleFlash:
                {
                    float radius = MathF.Max(size / 2f * (1f - 0.5f * r.Phase), 3f * px);
                    var bright = Fade(c, 1f - r.Phase);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * MathF.PI / 4f;
                        var d = new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                        Raylib.DrawLineEx(p - d, p + d, MathF.Max(1.5f * px, radius * 0.25f), bright);
                    }
                    Raylib.DrawCircleV(p, radius * 0.45f, bright);
                    break;
                }
                case EffectKind.Tracer:
                    if (TracerSpan(r, out var t0, out var t1))
                        Raylib.DrawLineEx(new Vector2(t0.X, t0.Y), new Vector2(t1.X, t1.Y), MathF.Max(r.Look.Size, 1.5f * px), c);
                    break;
                case EffectKind.Fire:
                case EffectKind.Smoke:
                    // ⭐ W3 (VE-P) — seen from above: the smoke column drifts downwind as a trail of thinning puffs, the flames
                    //   flicker over the wreck; at least a few pixels each, so a far wreck still shows it is burning.
                    foreach (var puff in Puffs(r))
                        Raylib.DrawCircleV(new Vector2(puff.At.X, puff.At.Y), MathF.Max(puff.Radius, 2f * px),
                                           r.Look.Kind == EffectKind.Smoke ? Fade(puff.Colour, 0.6f) : puff.Colour);
                    break;
            }
        }
        Drawn = effects.Count;
    }

    // ---- 3-D --------------------------------------------------------------------------------------------

    public void Draw3D(RenderContext ctx)
    {
        var effects = Resolve();
        foreach (var r in effects)
        {
            var c = Fade(r.Colour, r.Opacity);
            float size = r.Look.Size;
            switch (r.Look.Kind)
            {
                case EffectKind.Decal:
                    DrawDrapedDisc(r.At, size / 2f, c);
                    break;
                case EffectKind.Explosion:
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawSphereEx(HrotToRaylib.Position(r.At + new Vector3(0, 0, size * 0.25f * r.Phase)),
                                        MathF.Max(0.05f, size / 2f * (0.3f + 0.7f * r.Phase)), 8, 12, Fade(c, 1f - 0.7f * r.Phase));
                    Raylib.EndBlendMode();
                    break;
                case EffectKind.MuzzleFlash:
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawSphereEx(HrotToRaylib.Position(r.At), MathF.Max(0.05f, size / 2f * (1f - 0.5f * r.Phase)), 6, 10, Fade(c, 1f - r.Phase));
                    Raylib.EndBlendMode();
                    break;
                case EffectKind.Tracer:
                    if (TracerSpan(r, out var t0, out var t1))
                        Raylib.DrawCylinderEx(HrotToRaylib.Position(t0), HrotToRaylib.Position(t1), size / 2f, size / 2f, 4, c);
                    break;
                case EffectKind.Fire:
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    foreach (var puff in Puffs(r))
                        Raylib.DrawSphereEx(HrotToRaylib.Position(puff.At), MathF.Max(0.05f, puff.Radius), 5, 8, puff.Colour);
                    Raylib.EndBlendMode();
                    break;
                case EffectKind.Smoke:
                    foreach (var puff in Puffs(r))
                        Raylib.DrawSphereEx(HrotToRaylib.Position(puff.At), MathF.Max(0.05f, puff.Radius), 6, 10, puff.Colour);
                    break;
            }
        }
        Drawn = effects.Count;
    }

    /// <summary>The visible streak of a tracer at its phase: a piece of the muzzle → target line, a third of it long (≤ 60 m),
    /// travelling from the muzzle to the target over the effect's life.</summary>
    public static bool TracerSpan(in Resolved r, out Vector3 from, out Vector3 to)
    {
        from = to = r.At;
        if (r.Toward == Vector3.Zero) return false;
        var path = r.Toward - r.At;
        float length = path.Length();
        if (length < 0.5f) return false;
        float streak = MathF.Min(length / 3f, 60f) / length;
        float head = Math.Clamp(r.Phase * (1f + streak), 0f, 1f);
        float tail = Math.Clamp(head - streak, 0f, 1f);
        from = r.At + path * tail;
        to = r.At + path * head;
        return head > tail;
    }

    private void DrawDrapedDisc(Vector3 centre, float radius, Color colour)
    {
        const int n = 20;
        float Ground(float x, float y) => (GroundHeight?.Invoke(x, y) ?? centre.Z) + 0.06f;
        var mid = HrotToRaylib.Position(new Vector3(centre.X, centre.Y, Ground(centre.X, centre.Y)));
        for (int i = 0; i < n; i++)
        {
            float a0 = i * MathF.PI * 2f / n, a1 = (i + 1) * MathF.PI * 2f / n;
            float x0 = centre.X + MathF.Cos(a0) * radius, y0 = centre.Y + MathF.Sin(a0) * radius;
            float x1 = centre.X + MathF.Cos(a1) * radius, y1 = centre.Y + MathF.Sin(a1) * radius;
            var p0 = HrotToRaylib.Position(new Vector3(x0, y0, Ground(x0, y0)));
            var p1 = HrotToRaylib.Position(new Vector3(x1, y1, Ground(x1, y1)));
            Raylib.DrawTriangle3D(mid, p0, p1, colour);
            Raylib.DrawTriangle3D(mid, p1, p0, colour);
        }
    }

    /// <summary><c>#RRGGBB</c> or <c>#RRGGBBAA</c> WITH its alpha (a decal is translucent); white for anything else.</summary>
    public static Color ParseRgba(string? hex)
    {
        var s = (hex ?? "").Trim().TrimStart('#');
        if ((s.Length != 6 && s.Length != 8)
            || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return Color.White;
        return s.Length == 6 ? new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)255)
                             : new Color((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private static Color Fade(Color c, float k) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * k, 0f, 255f));
}
