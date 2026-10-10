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

    /// <summary>One effect, resolved for drawing: what it is, where, how far through its life, how opaque.</summary>
    public readonly record struct Resolved(EffectVisualDto Look, Vector3 At, Vector3 Toward, float Phase, float Opacity, Color Colour);

    /// <summary>Every live effect resolved for drawing — railable without a GL context.</summary>
    public List<Resolved> Resolve()
    {
        var list = new List<Resolved>();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<EffectLifetime>() || !world.IsComponentTypeRegistered<TkbIdentity>()) return list;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<EffectLifetime>().With<TkbIdentity>().With<SimTransform>().Build();
        }
        var tkb = _tkb();
        bool anchored = world.IsComponentTypeRegistered<EffectAnchor>();
        foreach (var e in _query!)
        {
            if (EffectTkbCatalog.VisualOf(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) is not { } look) continue;
            var life = world.GetComponentRO<EffectLifetime>(e);
            var at = world.GetComponentRO<SimTransform>(e).Position;
            var toward = Vector3.Zero;
            if (anchored && world.HasComponent<EffectAnchor>(e))
            {
                var a = world.GetComponentRO<EffectAnchor>(e);
                if (a.Shooter == Entity.Null || !world.IsAlive(a.Shooter)) continue;   // VE-J: the shooter gone ⇒ the effect ends
                at = MuzzleOf(world, a.Shooter, tkb);
                toward = a.Toward;
            }
            float remaining = life.Duration - life.Age;
            float opacity = look.FadeSeconds > 0f && remaining < look.FadeSeconds ? Math.Clamp(remaining / look.FadeSeconds, 0f, 1f) : 1f;
            var colour = ParseRgba(look.ColorHex);
            list.Add(new Resolved(look, at, toward, life.Phase, opacity, colour));
        }
        // decals underneath, then bursts, then flashes and tracers
        list.Sort((x, y) => Order(x.Look.Kind).CompareTo(Order(y.Look.Kind)));
        return list;
    }

    private static int Order(EffectKind k) => k switch { EffectKind.Decal => 0, EffectKind.Explosion => 1, _ => 2 };

    /// <summary>⚠ Interim muzzle (until E4's <c>Muzzle.Of</c>): the front of the shooter's drawn box, in its upper half.</summary>
    public Vector3 MuzzleOf(EntityRepository world, Entity shooter, ITkbDatabase? tkb)
    {
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
