using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Radar;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis3D;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>What the fighter HUD says this frame, as data (the draw and the rails read the same thing).</summary>
public enum MissileCue : byte { None = 0, Search, InZone, Lock, LockInZone }

/// <summary>The radar warning: nothing, a radar TRACKS this jet, a missile has been LAUNCHED at it.</summary>
public enum WarningCue : byte { None = 0, Tracked, Launch }

/// <summary>The g read-out's colour: white below 6, amber from 6, red within 1 g of the limit.</summary>
public enum GColour : byte { Normal = 0, High, Limit }

/// <param name="Active">the possessed jet carries air-to-air weapons (a fighter): else nothing is drawn.</param>
/// <param name="TargetAt">the nearest hostile aircraft inside the cone of the look, <paramref name="HasTarget"/>.</param>
/// <param name="PipperAt">where a gun burst fired now meets him (<see cref="TargetLead.Point"/>), only <paramref name="HasPipper"/>: gun selected, inside <see cref="FighterHud.GunRangeM"/>.</param>
public readonly record struct FighterHudModel(bool Active, bool GunSelected, bool HasTarget, Vector3 TargetAt, float RangeM, float ClosureMps,
                                              bool HasPipper, Vector3 PipperAt, MissileCue Missile, float GLoad, GColour G,
                                              WarningCue Warning, float WarningBearingDeg, string WarningText, string RangeText, string GText);

/// <summary>
/// ⭐ CE-4647 (AC-15 step 7; docs/designs/air-attack/DESIGN_Air_Combat.md §4d; mockup <c>air-combat-hud.svg</c>) — the FIGHTER HUD of a possessed F-16 / MiG (the manual mouse-aim pilot: mode 8 is AI only), over the existing flyer HUD
/// (<c>PossessionStation.Hud</c>, Entity Control §8f — unchanged): a gun PIPPER at the led point, the missile cue (SEARCH / IN ZONE / LOCK), the radar warning (a tick at the emitter's bearing, the text on a LAUNCH) and the g.
/// <para>📐 The pipper is <see cref="TargetLead.Point"/> — the ONE predictor of rounds, missiles and gun runs; the warning reads the EXISTING <see cref="RadarWarning"/> and <see cref="MissileWarning"/>.
/// ✅ The zone is the air lane's: <see cref="EmploymentZone.Inside"/> (AC-8 — the missile's motor model against the target's present speed and aspect), the ONE zone the fighter's brain reads too. IN ZONE ⇔ it says so.</para>
/// ⭐ HOT PATH: the target scan is one cached query; every text is a cached string per value — a steady frame allocates nothing.
/// </summary>
public sealed class FighterHud
{
    /// <summary>The gun's reach for the pipper (m, DESIGN §AC-8: "fires ≤ 500 m"), its default muzzle speed (m/s) where the mount says none.</summary>
    public const float GunRangeM = 500f, DefaultMuzzleSpeed = 1050f;
    /// <summary>The seeker locks inside <see cref="LockConeDeg"/> of the look; a target is "found" inside <see cref="SearchConeDeg"/> and <see cref="MaxSearchRangeM"/> (a radar missile's zone reaches 20 km).</summary>
    public const float LockConeDeg = 15f, SearchConeDeg = 30f, MaxSearchRangeM = 30000f;
    /// <summary>g from which the read-out turns amber (also the vortex trails' threshold) and red.</summary>
    public const float HighG = 6f, LimitG = 8f;

    private EntityRepository? _queryWorld;
    private EntityQuery? _query;
    private readonly Dictionary<long, bool> _aircraft = new();
    private readonly Dictionary<(int Kind, int A, int B), string> _texts = new();

    private const int KindLaunchIr = 1, KindLaunchRadar = 2, KindRange = 3, KindG = 4;

    /// <summary>The text of (kind, a, b), built ONCE per distinct value: a steady frame allocates nothing (no lambda, no concatenation per frame). Culture-invariant (R-4221).</summary>
    private string Text(int kind, int a, int b = 0)
    {
        if (_texts.TryGetValue((kind, a, b), out var s)) return s;
        var inv = CultureInfo.InvariantCulture;
        s = kind switch
        {
            KindLaunchIr => "LAUNCH IR  " + (a / 10f).ToString("0.0", inv) + " s",
            KindLaunchRadar => "LAUNCH RADAR  " + (a / 10f).ToString("0.0", inv) + " s",
            KindRange => (a / 10f).ToString("0.0", inv) + " km  " + b.ToString(inv) + " m/s",
            _ => "G  " + (a / 10f).ToString("0.0", inv),
        };
        return _texts[(kind, a, b)] = s;
    }

    /// <summary>A fighter: the possessed type is a jet family and has an air-to-air missile mount (an F-16 before the air lane armed it has none — no HUD).</summary>
    public static bool Applies(EntityRepository world, Entity unit, ITkbDatabase? tkb)
        => world.HasComponent<TkbIdentity>(unit) && tkb != null && tkb.TryGetByType(world.GetComponentRO<TkbIdentity>(unit).TkbType, out var t)
           && JetStores.Carries(VisualFamilies.Of(t)) && t.GetDescriptor<WeaponSuiteDto>() is { } suite && HasMissileMount(suite);

    private static bool HasMissileMount(WeaponSuiteDto suite)
    {
        for (int i = 0; i < suite.Mounts.Count; i++) if (JetStores.IsIr(suite.Mounts[i].AmmoGuid) || JetStores.IsRadar(suite.Mounts[i].AmmoGuid)) return true;
        return false;
    }

    private bool IsAircraft(ITkbDatabase? tkb, long type)
    {
        if (_aircraft.TryGetValue(type, out bool air)) return air;
        var family = EntityBodyLayer3D.Resolve(tkb != null && tkb.TryGetByType(type, out var t) ? t : null).Family;
        return _aircraft[type] = family is VisualFamily.Jet or VisualFamily.TwinEngineJet or VisualFamily.AttackJet or VisualFamily.Helicopter
                                    or VisualFamily.AttackHelicopter or VisualFamily.CargoPlane or VisualFamily.Drone;
    }

    /// <summary>The HUD of <paramref name="unit"/> looking along <paramref name="look"/> with mount <paramref name="mount"/> selected, at sim time <paramref name="now"/>. Pure over the world: no drawing.</summary>
    public FighterHudModel Build(EntityRepository world, Entity unit, Vector3 look, int mount, ITkbDatabase? tkb, double now)
    {
        if (!Applies(world, unit, tkb) || !world.HasComponent<SimTransform>(unit)) return default;
        var tf = world.GetComponentRO<SimTransform>(unit);
        var own = tf.Position;
        var vel = world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(unit) ? world.GetComponentRO<SimVelocity>(unit).Linear : Vector3.Zero;
        var suite = tkb!.GetByType(world.GetComponentRO<TkbIdentity>(unit).TkbType).GetDescriptor<WeaponSuiteDto>()!;
        WeaponMountDto? m = mount >= 0 && mount < suite.Mounts.Count ? suite.Mounts[mount] : null;
        bool gun = m is { } g && g.Category is WeaponCategory.Autocannon or WeaponCategory.MachineGun or WeaponCategory.SmallArms;
        bool missile = m is { } mm && (JetStores.IsIr(mm.AmmoGuid) || JetStores.IsRadar(mm.AmmoGuid));

        // ── the target: the nearest hostile aircraft inside the cone of the look
        bool has = false; Vector3 at = default, tvel = default; float best = float.MaxValue;
        var fwd = look.LengthSquared() > 1e-6f ? Vector3.Normalize(look) : Vector3.UnitX;
        var side = world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(unit) ? world.GetComponentRO<EntityInfo>(unit).ForceId : ForceId.Neutral;
        if (!ReferenceEquals(world, _queryWorld)) { _queryWorld = world; _query = world.Query().With<SimTransform>().With<TkbIdentity>().Build(); }
        float cosCone = MathF.Cos(SearchConeDeg * MathF.PI / 180f);
        foreach (var e in _query!)
        {
            if (e == unit || !IsAircraft(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) || CombatLife.IsDestroyed(world, e)) continue;
            var theirs = world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(e) ? world.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;
            if (theirs == side || theirs == ForceId.Neutral || side == ForceId.Neutral) continue;   // hostile = another side's
            var p = world.GetComponentRO<SimTransform>(e).Position;
            var d = p - own; float range = d.Length();
            if (range < 1f || range > MaxSearchRangeM) continue;
            float cos = Vector3.Dot(d / range, fwd);
            if (cos < cosCone || -cos >= best) continue;   // the one nearest the look
            best = -cos; has = true; at = p;
            tvel = world.IsComponentTypeRegistered<SimVelocity>() && world.HasComponent<SimVelocity>(e) ? world.GetComponentRO<SimVelocity>(e).Linear : Vector3.Zero;
        }
        float rangeM = has ? Vector3.Distance(own, at) : 0f;
        float closure = has ? -Vector3.Dot(tvel - vel, Vector3.Normalize(at - own)) : 0f;

        // ── the gun's pipper and the missile's cue
        bool pipper = gun && has && rangeM <= GunRangeM;
        var pip = pipper ? TargetLead.Point(own, vel, m!.MuzzleVelocity > 0f ? m.MuzzleVelocity : DefaultMuzzleSpeed, at, tvel) : default;
        var cue = MissileCue.None;
        if (missile)
        {
            if (!has) cue = MissileCue.Search;
            else
            {
                // the air lane's employment zone, for THIS missile (its MissileDto from the mount's ammunition type) against the target's present speed and aspect
                bool inZone = tkb!.TryGetByType((long)m!.AmmoGuid, out var round) && round.GetDescriptor<MissileDto>() is { } dto && EmploymentZone.Inside(dto, own, vel, at, tvel, 1f, false);
                bool locked = Vector3.Dot((at - own) / rangeM, fwd) >= MathF.Cos(LockConeDeg * MathF.PI / 180f);
                cue = locked ? (inZone ? MissileCue.LockInZone : MissileCue.Lock) : (inZone ? MissileCue.InZone : MissileCue.Search);
            }
        }

        // ── g and the warnings
        float gLoad = FighterState.Read(world, unit) is { } st ? st.GLoad : 1f;
        var gc = gLoad >= LimitG ? GColour.Limit : gLoad >= HighG ? GColour.High : GColour.Normal;
        var warning = WarningCue.None; float bearing = 0f; string wtext = "";
        float heading = 90f - Fdp.Toolkit.Motion.RotorcraftModel.YawOf(tf.Rotation) * 180f / MathF.PI;
        if (world.IsComponentTypeRegistered<MissileWarning>() && world.HasComponent<MissileWarning>(unit))
        {
            var mw = world.GetComponentRO<MissileWarning>(unit);
            float left = mw.TimeToImpactS - (float)(now - (double)mw.At);
            if (left > 0f)
            {
                warning = WarningCue.Launch; bearing = mw.BearingDeg - heading;
                int tenths = (int)MathF.Round(left * 10f);
                wtext = Text(mw.Seeker == GuidanceKind.Radar ? KindLaunchRadar : KindLaunchIr, tenths);
            }
        }
        if (warning == WarningCue.None && world.IsComponentTypeRegistered<RadarWarning>() && world.HasComponent<RadarWarning>(unit)
            && world.GetComponentRO<RadarWarning>(unit) is { Level: RadarWarningLevel.Tracked } rw) { warning = WarningCue.Tracked; bearing = rw.BearingDeg - heading; }

        string rt = has ? Text(KindRange, (int)MathF.Round(rangeM / 100f), (int)MathF.Round(closure)) : "";
        return new FighterHudModel(true, gun, has, at, rangeM, closure, pipper, pip, cue, gLoad, gc, warning, bearing, wtext, rt,
                                   Text(KindG, (int)MathF.Round(gLoad * 10f)));
    }

    // ── the picture (the mockup's anchors) ──────────────────────────────────────────────────────────────────────────────────────────

    public static readonly Color Green = new(124, 245, 138, 255), Amber = new(255, 200, 60, 255), Red = new(255, 90, 80, 255), Grey = new(160, 160, 170, 255), White = new(215, 215, 215, 255);
    public const int RwrRadius = 46;

    /// <summary>The radar warning ring's centre: 90 px from the open map's left, 59 % down (the mockup's anchor).</summary>
    public static Vector2 RwrCentre(Vector2 viewport) => new(MathF.Max(8f, Fdp.Presentation.WindowManager.OpenMapArea.Corner(viewport, left: true, top: true, 0, 0, 16f).X) + 74f, viewport.Y * 0.59f);
    /// <summary>The IN ZONE tag's centre (bottom-centre, 70 px up) and the g tag's rectangle (bottom-right).</summary>
    public static Vector2 ZoneCentre(Vector2 viewport, Vector2 viewCentre) => new(viewCentre.X, viewport.Y - 70f);
    public static Rectangle GRect(Vector2 viewport) => new(viewport.X - 106f, viewport.Y - 52f, 90f, 28f);

    public static Color ColourOf(GColour g) => g switch { GColour.Limit => Red, GColour.High => Amber, _ => White };

    private static bool OnScreen(MapCamera3D camera, Vector3 world, out Vector2 screen)
    {
        screen = default;
        if (Vector3.Dot(world - camera.Eye, camera.Forward) <= 0.1f) return false;
        screen = camera.WorldToScreen(world);
        return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
    }

    /// <summary>Draws the HUD (the station's overlay pass, after the flyer's own lines). Nothing when the model is inactive.</summary>
    public static void Draw(in FighterHudModel m, MapCamera3D camera, Font? font)
    {
        if (!m.Active) return;
        var size = camera.Viewport; var vc = camera.ViewCentre;
        // the lock diamond on the target, with the range and closure
        if (m.HasTarget && m.Missile is MissileCue.Lock or MissileCue.LockInZone && OnScreen(camera, m.TargetAt, out var t))
        {
            var col = m.Missile == MissileCue.LockInZone ? Green : Amber;
            Raylib.DrawLineEx(new Vector2(t.X, t.Y - 34), new Vector2(t.X + 34, t.Y), 2f, col); Raylib.DrawLineEx(new Vector2(t.X + 34, t.Y), new Vector2(t.X, t.Y + 34), 2f, col);
            Raylib.DrawLineEx(new Vector2(t.X, t.Y + 34), new Vector2(t.X - 34, t.Y), 2f, col); Raylib.DrawLineEx(new Vector2(t.X - 34, t.Y), new Vector2(t.X, t.Y - 34), 2f, col);
            HudText.Draw(font, "LOCK", (int)t.X + 40, (int)t.Y - 18, 14, col); HudText.Draw(font, m.RangeText, (int)t.X + 40, (int)t.Y, 13, col);
        }
        // the gun pipper at the led point, a dashed lead line from the target
        if (m.HasPipper && OnScreen(camera, m.PipperAt, out var p))
        {
            Raylib.DrawCircleLines((int)p.X, (int)p.Y, 19f, Amber); Raylib.DrawCircleLines((int)p.X, (int)p.Y, 18f, Amber); Raylib.DrawCircle((int)p.X, (int)p.Y, 2.4f, Amber);
            if (OnScreen(camera, m.TargetAt, out var tt)) Raylib.DrawLineEx(tt, p, 1.5f, Amber);
        }
        // IN ZONE (bottom-centre) / SEARCH (grey)
        if (m.Missile != MissileCue.None)
        {
            var z = ZoneCentre(size, vc);
            bool inz = m.Missile is MissileCue.InZone or MissileCue.LockInZone;
            string tag = inz ? "IN ZONE" : m.Missile == MissileCue.Lock ? "LOCK" : "SEARCH";
            var col = inz ? Green : m.Missile == MissileCue.Lock ? Amber : Grey;
            int tw = (int)HudText.Measure(font, tag, 20);
            Raylib.DrawRectangleRounded(new Rectangle(z.X - 80, z.Y - 22, 160, 36), 0.1f, 4, new Color(16, 19, 26, 140));
            HudText.Draw(font, tag, (int)(z.X - tw / 2f), (int)z.Y - 17, 20, col);
        }
        // the radar warning ring: a tick at the bearing (0 = the nose, up), the text beside it on a LAUNCH
        if (m.Warning != WarningCue.None)
        {
            var c = RwrCentre(size); var col = m.Warning == WarningCue.Launch ? Red : Amber;
            Raylib.DrawCircle((int)c.X, (int)c.Y, RwrRadius, new Color(16, 19, 26, 140)); Raylib.DrawCircleLines((int)c.X, (int)c.Y, RwrRadius, Grey);
            float a = m.WarningBearingDeg * MathF.PI / 180f;
            var tip = c + new Vector2(MathF.Sin(a), -MathF.Cos(a)) * (RwrRadius - 6f);
            Raylib.DrawPoly(tip, 3, 8f, m.WarningBearingDeg - 90f + 90f, col);
            if (m.Warning == WarningCue.Launch) HudText.Draw(font, m.WarningText, (int)c.X + RwrRadius + 12, (int)c.Y - 9, 16, col);
        }
        // g
        var r = GRect(size);
        Raylib.DrawRectangleRounded(r, 0.2f, 4, new Color(16, 19, 26, 140));
        int gw = (int)HudText.Measure(font, m.GText, 18);
        HudText.Draw(font, m.GText, (int)(r.X + r.Width / 2f - gw / 2f), (int)r.Y + 4, 18, ColourOf(m.G));
    }
}
