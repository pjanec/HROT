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
    private EntityRepository? _cloudWorld;
    private EntityQuery? _cloudQuery;

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

    /// <summary>⭐ CE-1165 — gas / smoke clouds drawn in the last frame (either mode) — the rail's counter.</summary>
    public int CloudsDrawn { get; private set; }

    public void Update(float dt) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <summary>One effect, resolved for drawing: what it is, where, how far through its life, how opaque — and (W3) its own seed and
    /// age in seconds, from which its particles are derived (stateless, VE-P).</summary>
    public readonly record struct Resolved(EffectVisualDto Look, Vector3 At, Vector3 Toward, float Phase, float Opacity, Color Colour,
                                           int Seed = 0, float Age = 0f, Vector3 Forward = default, EffectFlight? Flight = null)
    {
        /// <summary>⭐ CE-1129 (VE §10 IM-6) — the surface a decal lies on: its rotation's +Z (up for a ground mark and a burst's scorch).</summary>
        public Vector3 Normal { get; init; } = Vector3.UnitZ;
    }

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
        bool flying = world.IsComponentTypeRegistered<EffectFlight>();   // ⭐ CE-1115
        // ⭐ W1 (VE-Q, VE-G) — the phase is the SIM clock's: paused ⇒ frozen; in a replay, the recording's own time (VE-R).
        double now = Fdp.Toolkit.Time.SimClock.Of(world).TotalTime;
        foreach (var e in _query!)
        {
            if (EffectTkbCatalog.VisualOf(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) is not { } look) continue;
            var life = world.GetComponentRO<Fdp.Toolkit.Time.Lifespan>(e);
            var at = world.GetComponentRO<SimTransform>(e).Position;
            var toward = Vector3.Zero;
            var forward = Vector3.Zero;   // ⭐ CE-1070 — the weapon's pointing direction at the muzzle, for a flash
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
                    at = MuzzleOf(world, a.Shooter, a.WeaponIndex, tkb, out forward);
                    toward = a.Toward;
                }
            }
            if (life.IsOver(now)) continue;   // its time passed this frame; LifespanSystem removes it (or the replay already did)
            float remaining = life.Remaining(now);
            float opacity = look.FadeSeconds > 0f && remaining < look.FadeSeconds ? Math.Clamp(remaining / look.FadeSeconds, 0f, 1f) : 1f;
            var colour = ParseRgba(look.ColorHex);
            EffectFlight? flight = flying && world.HasComponent<EffectFlight>(e) ? world.GetComponentRO<EffectFlight>(e) : null;
            list.Add(new Resolved(look, at, toward, life.Phase(now), opacity, colour, Seed: e.Index * 7919 + e.Generation, Age: life.Age(now),
                                  Forward: forward, Flight: flight)
                     { Normal = Vector3.Transform(Vector3.UnitZ, world.GetComponentRO<SimTransform>(e).Rotation) });
        }
        // decals underneath, then bursts, then fire, then smoke over it, then flashes and tracers
        list.Sort((x, y) => Order(x.Look.Kind).CompareTo(Order(y.Look.Kind)));
        return list;
    }

    private static int Order(EffectKind k) => k switch
    {
        EffectKind.Decal => 0, EffectKind.Explosion => 1, EffectKind.Fire => 2, EffectKind.Smoke => 3, _ => 4,
    };

    /// <summary>
    /// ⭐ CE-1165 (civilian U-4, docs/DESIGN_Civilians_And_Crowd_Control.md §8.8 U4-e) — one gas or smoke cloud, resolved for drawing: the
    /// cylinder (<see cref="Radius"/> × <see cref="Height"/> standing on <see cref="Centre"/>), what it is, how dense it looks now
    /// (<c>1 − TransmittanceAt(now)</c>: dense for two thirds of its life, then thinning to nothing), its seed and age (for the puffs).
    /// </summary>
    public readonly record struct CloudLook(Vector3 Centre, float Radius, float Height, Fdp.Toolkit.Combat.CloudKind Kind, float Density,
                                            int Seed, float Age);

    /// <summary>⭐ CE-1165 (U4-a′) — every live cloud resolved for drawing; read every frame (its owner drifts it on the wind, CE-3219), and
    /// faded on <c>Fraction(now)</c> — a cloud has no <c>Lifespan</c>. Railable without a GL context.</summary>
    public List<CloudLook> ResolveClouds()
    {
        var list = new List<CloudLook>();
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.AreaEffectCloud>()) return list;
        if (!ReferenceEquals(world, _cloudWorld))
        {
            _cloudWorld = world;
            _cloudQuery = world.Query().With<Fdp.Toolkit.Combat.AreaEffectCloud>().With<SimTransform>().Build();
        }
        double now = Fdp.Toolkit.Time.SimClock.Of(world).TotalTime;
        foreach (var e in _cloudQuery!)
        {
            var c = world.GetComponentRO<Fdp.Toolkit.Combat.AreaEffectCloud>(e);
            if (c.Radius <= 0f || c.IsOver(now)) continue;
            float density = Math.Clamp(1f - c.TransmittanceAt(now), 0f, 1f);
            if (density <= 0.01f) continue;
            list.Add(new CloudLook(world.GetComponentRO<SimTransform>(e).Position, c.Radius, MathF.Max(c.Height, 0.5f), c.Kind, density,
                                   e.Index * 7919 + e.Generation, (float)Math.Max(0.0, now - c.BornAtSimSeconds)));
        }
        return list;
    }

    /// <summary>⭐ CE-1165 (U4-e) — a cloud's colour: tear gas off-white with a yellow cast, smoke grey.</summary>
    public static Color CloudColour(Fdp.Toolkit.Combat.CloudKind kind)
        => kind == Fdp.Toolkit.Combat.CloudKind.TearGas ? new Color(236, 232, 196, 255) : new Color(168, 168, 166, 255);

    /// <summary>⭐ CE-1165 — the most puffs one cloud draws in 3-D (a big cloud, not a field of spheres).</summary>
    public const int MaxCloudPuffs = 36;

    /// <summary>
    /// ⭐ CE-1165 (U4-e) — the 3-D puffs of a cloud, the chaff cloud's precedent: hashed per entity (stateless, the same on every node),
    /// spread evenly over its disc and between a fifth and four fifths of its height, each bobbing slowly; their number grows with the
    /// cloud's size; each faint, so their overlap is the density. Every puff lies INSIDE the cylinder.
    /// </summary>
    public static List<Puff> CloudPuffs(in CloudLook c)
    {
        int n = Math.Clamp((int)MathF.Round(c.Radius * c.Radius * 0.4f), 8, MaxCloudPuffs);
        var colour = CloudColour(c.Kind);
        var list = new List<Puff>(n);
        float puff = MathF.Min(MathF.Max(c.Radius * 0.42f, 0.8f), c.Height * 0.5f);
        for (int i = 0; i < n; i++)
        {
            float r = MathF.Sqrt(Hash(c.Seed, i)) * MathF.Max(c.Radius - puff, 0f);
            float a = Hash(c.Seed, i + 131) * MathF.Tau;
            float bob = MathF.Sin(c.Age * 0.35f + i * 1.7f) * 0.08f;
            float z = puff + (Hash(c.Seed, i + 263) + bob) * MathF.Max(c.Height - 2f * puff, 0f);
            float size = puff * (0.8f + 0.2f * Hash(c.Seed, i + 397));   // never more than the inset ⇒ inside the cylinder
            var at = c.Centre + new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, Math.Clamp(z, puff, MathF.Max(c.Height - puff, puff)));
            list.Add(new Puff(at, size, Fade(colour, 0.32f * c.Density)));
        }
        return list;
    }

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
    public const int FirePuffs = 16, SmokePuffs = 14;

    /// <summary>
    /// ⭐ W3 (VE-P) — the particles of a <see cref="EffectKind.Fire"/> or <see cref="EffectKind.Smoke"/>, derived from nothing but the
    /// effect's seed and age (no particle entities, no per-frame state): puff i runs a loop of its own period, phase-shifted by a hash
    /// of (seed, i), rising from the anchor; so a replay jump, a pause or a second host draws exactly the same puffs.
    /// <list type="bullet">
    ///   <item>Fire: short tongues (0.9 s) spread over the hull, rising 1.3 sizes and shrinking, yellow → orange → soot; they die down over
    ///   the last 30 % of its life.</item>
    ///   <item>Smoke: slow puffs (6 s) rising six sizes, growing, drifting downwind, thinning out; the effect's fade dims the column.</item>
    /// </list>
    /// </summary>
    public static List<Puff> Puffs(in Resolved r)
    {
        var list = new List<Puff>();
        float size = r.Look.Size;
        if (r.Look.Kind == EffectKind.Fire)
        {
            // ⭐ tuned on the Xvfb frame rail (2026-10-11): ALPHA-blended orange → red → soot tongues spread over the hull — additive
            //   yellow puffs saturated to a white ball and read as a lamp, not a fire.
            float strength = r.Phase < 0.7f ? 1f : Math.Clamp(1f - (r.Phase - 0.7f) / 0.3f, 0f, 1f);
            for (int i = 0; i < FirePuffs; i++)
            {
                float t = Frac(r.Age / 0.9f + Hash(r.Seed, i));
                float jx = (Hash(r.Seed, i + 101) - 0.5f) * size * 0.9f, jy = (Hash(r.Seed, i + 211) - 0.5f) * size * 0.6f;
                var at = r.At + new Vector3(jx * (1f - 0.6f * t), jy * (1f - 0.6f * t), t * size * 1.3f * strength);
                float radius = size * 0.20f * (1f - 0.5f * t) * (0.4f + 0.6f * strength);
                var colour = t < 0.5f ? Lerp(new Color(255, 200, 70, 255), new Color(240, 90, 20, 255), t * 2f)
                                      : Lerp(new Color(240, 90, 20, 255), new Color(80, 36, 24, 255), (t - 0.5f) * 2f);
                list.Add(new Puff(at, radius, Fade(colour, 0.9f * MathF.Pow(1f - t, 0.6f) * strength * r.Opacity)));
            }
        }
        else if (r.Look.Kind == EffectKind.Flare && r.Flight is { } f)
        {
            // ⭐ CE-1115 (§9 DV-4) — the smoke the flare leaves: grey puffs at where it WAS along its own path, older ones bigger and fainter
            for (int i = 1; i <= FlareTrailPuffs; i++)
            {
                float back = r.Age - i * FlareTrailStep;
                if (back < 0f) break;
                var at = Fdp.Toolkit.Combat.DecoyFlight.At(f.Origin, f.Velocity, f.SinkMps, back);
                float k = i / (float)FlareTrailPuffs;
                list.Add(new Puff(at, size * (0.15f + 0.45f * k), Fade(new Color(150, 146, 138, 255), 0.75f * (1f - k) * r.Opacity)));
            }
        }
        else if (r.Look.Kind == EffectKind.Chaff)
        {
            // ⭐ CE-1115 (§9 DV-4) — the cloud blooms over its first 1.5 s and thins as it sinks
            float bloom = Math.Clamp(r.Age / 1.5f, 0.15f, 1f);
            for (int i = 0; i < ChaffPuffs; i++)
            {
                var off = new Vector3(Hash(r.Seed, i) - 0.5f, Hash(r.Seed, i + 51) - 0.5f, (Hash(r.Seed, i + 97) - 0.5f) * 0.6f) * size * bloom;
                list.Add(new Puff(r.At + off, size * 0.22f * bloom, Fade(r.Colour, 0.75f * r.Opacity)));
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

    /// <summary>⭐ CE-1115 — a flare's smoke trail: this many puffs, one every <see cref="FlareTrailStep"/> s back along its path.</summary>
    public const int FlareTrailPuffs = 8;
    public const float FlareTrailStep = 0.12f;
    /// <summary>⭐ CE-1115 — the puffs of a chaff cloud.</summary>
    public const int ChaffPuffs = 10;

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
        => MuzzleOf(world, shooter, weaponIndex, tkb, out _);

    /// <summary>
    /// ⭐ CE-1070 (docs/DESIGN_Visual_Effects.md §6c) — the muzzle and the way the weapon points there, in this order: ① the TKB muzzle
    /// (<see cref="Fdp.Toolkit.Combat.Muzzle.TryOf(EntityRepository, Entity, int, out Vector3, out Vector3)"/> — a tank's barrel, posed);
    /// ② the end of the rifle the 3-D figure was DRAWN holding (<see cref="EntityBodyLayer3D.TryGetWeaponTip"/> — posture, stance, lean
    /// and aim included); ③ the front of the drawn box, in its upper half; ④ 1.5 m above the shooter.
    /// ⛔ ① then ③ was the old order: a rifleman has no TKB muzzle, so his flash sat 0.55 m in front of his PICK box at 1.35 m — above
    /// a crouched man's head, nowhere near the rifle he is drawn holding.
    /// </summary>
    public Vector3 MuzzleOf(EntityRepository world, Entity shooter, int weaponIndex, ITkbDatabase? tkb, out Vector3 forward)
    {
        if (Fdp.Toolkit.Combat.Muzzle.TryOf(world, shooter, weaponIndex, out var barrel, out forward)) return barrel;
        if (_bodies != null && _bodies.TryGetWeaponTip(shooter, out var tip, out forward)) return tip;
        if (_bodies != null && _bodies.TryGetBox(world, shooter, tkb, out var centre, out var rotation, out var half))
        {
            forward = Vector3.Transform(Vector3.UnitX, rotation);
            return centre + Vector3.Transform(new Vector3(half.X + 0.15f, 0f, half.Z * 0.5f), rotation);
        }
        forward = Vector3.Zero;
        return world.HasComponent<SimTransform>(shooter) ? world.GetComponentRO<SimTransform>(shooter).Position + new Vector3(0, 0, 1.5f) : Vector3.Zero;
    }

    /// <summary>⭐ CE-1070 — how far forward of the muzzle a flash's centre sits, as a fraction of its radius: the fireball blooms OUT of
    /// the barrel (its back just behind the muzzle), instead of being centred on the tip — a 4 m tank flash centred there swallowed the
    /// barrel and the turret front and stood 2 m above the gun.</summary>
    public const float FlashForwardOfMuzzle = 0.8f;

    /// <summary>⭐ CE-1070 — where a flash's fireball is centred this frame: its radius × <see cref="FlashForwardOfMuzzle"/> along the
    /// weapon, from the muzzle (at the muzzle when the direction is unknown).</summary>
    public static Vector3 FlashCentre(in Resolved r, float radius)
        => r.Forward == Vector3.Zero ? r.At : r.At + Vector3.Normalize(r.Forward) * (radius * FlashForwardOfMuzzle);

    /// <summary>⭐ CE-1231 (docs/DESIGN_Visual_Effects.md §11 CH-1) — a burst this big and up (the huge one: an IED, a vest, a car bomb, a
    /// heavy shell, a bomb) is an orange CORE inside a grey-brown CLOUD of dust and smoke that outlasts it.</summary>
    public const float DustFromSizeM = 15f;

    /// <summary>The core's share of the burst's size when it raises dust (the cloud is the full size).</summary>
    public const float CoreShare = 0.6f;

    public static readonly Color DustColour = new(122, 106, 88, 255);

    /// <summary>⭐ CE-1231 — does this explosion look raise a dust cloud?</summary>
    public static bool RaisesDust(EffectVisualDto look) => look.Kind == EffectKind.Explosion && look.Size >= DustFromSizeM;

    /// <summary>⭐ CE-1231 — the dust cloud of a burst of <paramref name="size"/> at <paramref name="phase"/> (0..1): it is up at once and
    /// keeps spreading, thick for the first half, fading after. Railable without a GL context.</summary>
    public static (float Radius, float Alpha) Dust(float size, float phase)
        => (size / 2f * (0.55f + 0.45f * phase), 0.75f * (phase < 0.5f ? 1f : 1f - (phase - 0.5f) * 2f));

    // ---- 2-D --------------------------------------------------------------------------------------------

    public void Draw(RenderContext ctx)
    {
        float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
        float px = 1f / zoom;   // one screen pixel in metres
        // ⭐ CE-1165 (U4-e) — clouds first, under every other effect: a filled disc of their radius and a thin rim, as dense as they look
        var clouds = ResolveClouds();
        foreach (var cl in clouds)
        {
            var colour = CloudColour(cl.Kind);
            var p = new Vector2(cl.Centre.X, cl.Centre.Y);
            Raylib.DrawCircleV(p, cl.Radius, Fade(colour, 0.55f * cl.Density));
            Raylib.DrawRing(p, MathF.Max(0f, cl.Radius - 1.5f * px), cl.Radius, 0f, 360f, 48, Fade(colour, 0.85f * cl.Density));
        }
        CloudsDrawn = clouds.Count;
        var effects = Resolve();
        // ⭐ W3 — seen from above the smoke would hide the flames, so in 2-D the flames are drawn LAST (on top).
        effects.Sort((x, y) => (x.Look.Kind == EffectKind.Fire).CompareTo(y.Look.Kind == EffectKind.Fire));
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
                    if (RaisesDust(r.Look))   // ⭐ CE-1231 — the dust cloud first, under the core
                    {
                        var (dustRadius, dustAlpha) = Dust(size, r.Phase);
                        Raylib.DrawCircleV(p, dustRadius, Fade(DustColour, dustAlpha * r.Opacity));
                        size *= CoreShare;
                    }
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
                case EffectKind.Flare:
                    // ⭐ CE-1115 — a bright dot (a few pixels at least) with its trail
                    foreach (var puff in Puffs(r))
                        Raylib.DrawCircleV(new Vector2(puff.At.X, puff.At.Y), MathF.Max(puff.Radius, 1.5f * px), Fade(puff.Colour, 0.6f));
                    Raylib.DrawCircleV(p, MathF.Max(size / 2f, 3f * px), c);
                    break;
                case EffectKind.Chaff:
                    Raylib.DrawCircleV(p, MathF.Max(size / 2f * Math.Clamp(r.Age / 1.5f, 0.15f, 1f), 3f * px), Fade(c, 0.45f));
                    break;
                case EffectKind.Fire:
                case EffectKind.Smoke:
                    // ⭐ W3 (VE-P) — seen from above: the smoke column drifts downwind as a trail of thinning puffs, the flames
                    //   flicker over the wreck; at least a few pixels each, so a far wreck still shows it is burning.
                    foreach (var puff in Puffs(r))
                        Raylib.DrawCircleV(new Vector2(puff.At.X, puff.At.Y),
                                           MathF.Max(puff.Radius, (r.Look.Kind == EffectKind.Fire ? 3f : 2f) * px),
                                           r.Look.Kind == EffectKind.Smoke ? Fade(puff.Colour, 0.6f) : puff.Colour);
                    break;
            }
        }
        Drawn = effects.Count;
    }

    // ---- 3-D --------------------------------------------------------------------------------------------

    public void Draw3D(RenderContext ctx)
    {
        // ⭐ CE-1165 (U4-e) — each cloud as faint puffs filling its cylinder
        var clouds = ResolveClouds();
        foreach (var cl in clouds)
            foreach (var puff in CloudPuffs(cl))
                Raylib.DrawSphereEx(HrotToRaylib.Position(puff.At), MathF.Max(0.1f, puff.Radius), 6, 10, puff.Colour);
        CloudsDrawn = clouds.Count;
        var effects = Resolve();
        foreach (var r in effects)
        {
            var c = Fade(r.Colour, r.Opacity);
            float size = r.Look.Size;
            switch (r.Look.Kind)
            {
                case EffectKind.Decal:
                    // ⭐ CE-1129 (VE §10 IM-6) — on the ground: draped; on a wall or a roof: a disc in the face's plane, a hair off it
                    if (IsOnGround(r)) DrawDrapedDisc(r.At, size / 2f, c);
                    else DrawSurfaceDisc(r.At, r.Normal, size / 2f, c);
                    break;
                case EffectKind.Explosion:
                    if (RaisesDust(r.Look))   // ⭐ CE-1231 — the grey-brown cloud round the orange core, rising with it
                    {
                        var (dustRadius, dustAlpha) = Dust(size, r.Phase);
                        Raylib.DrawSphereEx(HrotToRaylib.Position(r.At + new Vector3(0, 0, dustRadius * 0.6f)), dustRadius, 8, 12,
                                            Fade(DustColour, dustAlpha * r.Opacity));
                        size *= CoreShare;
                    }
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawSphereEx(HrotToRaylib.Position(r.At + new Vector3(0, 0, size * 0.25f * r.Phase)),
                                        MathF.Max(0.05f, size / 2f * (0.3f + 0.7f * r.Phase)), 8, 12, Fade(c, 1f - 0.7f * r.Phase));
                    Raylib.EndBlendMode();
                    break;
                case EffectKind.MuzzleFlash:
                {
                    float radius = MathF.Max(0.05f, size / 2f * (1f - 0.5f * r.Phase));
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawSphereEx(HrotToRaylib.Position(FlashCentre(r, radius)), radius, 6, 10, Fade(c, 1f - r.Phase));   // ⭐ CE-1070
                    Raylib.EndBlendMode();
                    break;
                }
                case EffectKind.Tracer:
                    if (TracerSpan(r, out var t0, out var t1))
                        Raylib.DrawCylinderEx(HrotToRaylib.Position(t0), HrotToRaylib.Position(t1), size / 2f, size / 2f, 4, c);
                    break;
                case EffectKind.Fire:
                    foreach (var puff in Puffs(r))
                        Raylib.DrawSphereEx(HrotToRaylib.Position(puff.At), MathF.Max(0.05f, puff.Radius), 5, 8, puff.Colour);
                    break;
                case EffectKind.Smoke:
                case EffectKind.Chaff:
                    foreach (var puff in Puffs(r))
                        Raylib.DrawSphereEx(HrotToRaylib.Position(puff.At), MathF.Max(0.05f, puff.Radius), 6, 10, puff.Colour);
                    break;
                case EffectKind.Flare:
                {
                    // ⭐ CE-1115 (§9 DV-4) — the trail first, then the burning star (additive, flickering), a hot core inside it
                    foreach (var puff in Puffs(r))
                        Raylib.DrawSphereEx(HrotToRaylib.Position(puff.At), MathF.Max(0.05f, puff.Radius), 5, 8, puff.Colour);
                    float flicker = 0.85f + 0.15f * MathF.Sin(r.Age * 37f + r.Seed);
                    Raylib.BeginBlendMode(BlendMode.Additive);
                    Raylib.DrawSphereEx(HrotToRaylib.Position(r.At), MathF.Max(0.05f, size / 2f * flicker), 6, 10, c);
                    Raylib.DrawSphereEx(HrotToRaylib.Position(r.At), MathF.Max(0.03f, size / 5f), 5, 8, Fade(Color.White, r.Opacity));
                    Raylib.EndBlendMode();
                    break;
                }
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

    /// <summary>⭐ CE-1129 — a mark lies on the GROUND (draped) when it faces up and sits within <see cref="GroundMarkBand"/> of it; a roof
    /// faces up too, but stands above the ground.</summary>
    public bool IsOnGround(in Resolved r)
        => r.Normal.Z > 0.9f && MathF.Abs(r.At.Z - (GroundHeight?.Invoke(r.At.X, r.At.Y) ?? r.At.Z)) <= GroundMarkBand;

    /// <summary>⭐ CE-1129 — how far off the ground (m) a mark facing up still counts as on it.</summary>
    public const float GroundMarkBand = 0.3f;

    /// <summary>⭐ CE-1129 — how far a mark on a wall or a roof stands off its face (m), so it does not flicker into it.</summary>
    public const float SurfaceMarkLift = 0.02f;

    /// <summary>⭐ CE-1129 (VE §10 IM-6) — the disc a mark on a wall or a roof is: in the plane of <paramref name="normal"/>, lifted
    /// <see cref="SurfaceMarkLift"/> off it, both sides drawn.</summary>
    public static (Vector3 Centre, Vector3 U, Vector3 V) SurfaceDiscFrame(Vector3 at, Vector3 normal)
    {
        var n = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        var u = Vector3.Cross(n, MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
        u = Vector3.Normalize(u);
        return (at + n * SurfaceMarkLift, u, Vector3.Cross(n, u));
    }

    private static void DrawSurfaceDisc(Vector3 at, Vector3 normal, float radius, Color colour)
    {
        const int n = 16;
        var (centre, u, v) = SurfaceDiscFrame(at, normal);
        var mid = HrotToRaylib.Position(centre);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * MathF.PI * 2f / n, a1 = (i + 1) * MathF.PI * 2f / n;
            var p0 = HrotToRaylib.Position(centre + (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius);
            var p1 = HrotToRaylib.Position(centre + (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius);
            Raylib.DrawTriangle3D(mid, p0, p1, colour);
            Raylib.DrawTriangle3D(mid, p1, p0, colour);
        }
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
