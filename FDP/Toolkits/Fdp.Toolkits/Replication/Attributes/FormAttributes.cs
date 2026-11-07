using System;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Weather;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Stance;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐ B-4 / Details forms D2 (<c>docs/UX/DESIGN_Details_Forms.md</c> §4.1) — the VALUE logic of the form attributes, in ONE place: the JSON setters
/// (<see cref="AttributeCompilerFactory.Build"/>) and the binary handlers (<see cref="FormAttributeInstaller"/>) both call it, so the two routes cannot
/// disagree on a clamp, a token or a rejection.
/// </summary>
public static class FormAttributes
{
    // ── reading a value, whatever its kind ───────────────────────────────────────────────────────────────

    /// <summary>The numeric value of a record, whatever numeric kind it was sent as.</summary>
    public static double AsDouble(in AttributeValue v) => v.Kind switch
    {
        AttributeValueKind.CsInt32 => v.IntValue,
        AttributeValueKind.CsInt64 => v.LongValue,
        AttributeValueKind.CsFloat32 => v.FloatValue,
        AttributeValueKind.Bool => v.BoolValue ? 1 : 0,
        AttributeValueKind.CsString => double.TryParse(v.StringValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0d,
        _ => v.DoubleValue,
    };

    /// <summary>A force token — the shapes <c>Affiliation</c> takes: a number (the ordinal) or <c>FORCE_*</c> text.</summary>
    public static ForceId Force(in AttributeValue v)
        => v.Kind == AttributeValueKind.CsString
            ? AttributeCompilerFactory.MapAffiliationString(v.StringValue)
            : AttributeCompilerFactory.MapAffiliationInt((int)AsDouble(v));

    /// <summary>The same token read from JSON.</summary>
    public static ForceId Force(ref Utf8JsonReader r)
        => r.TokenType == JsonTokenType.Number ? AttributeCompilerFactory.MapAffiliationInt(r.GetInt32()) : AttributeCompilerFactory.MapAffiliationString(r.GetString());

    // ── Health ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Strength: clamped to [0, <see cref="Health.Max"/>].</summary>
    public static void SetHealth(ref Health h, double value)
        => h.Current = (float)Math.Clamp(value, 0d, Math.Max(0f, h.Max));

    // ── Ammunition ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One mount's rounds: clamped to [0, <see cref="WeaponState.MaxAmmo"/>].</summary>
    public static void SetAmmo(ref WeaponState w, double value)
        => w.Ammo = (int)Math.Clamp(Math.Round(value), 0d, Math.Max(0, w.MaxAmmo));

    /// <summary>The mount child of <paramref name="unit"/> with <c>WeaponMountInfo.MountIndex == mount</c> (mounts 1–3), the way
    /// <c>WeaponAmmoMirrorSystem</c> finds them: a <c>WeaponMountInfo</c> + <c>PartMetadata</c> child whose parent is the unit. <c>false</c> when there is none.</summary>
    public static bool TryFindMount(EntityRepository repo, Entity unit, int mount, out Entity child)
    {
        child = default;
        if (mount <= 0 || !repo.IsComponentTypeRegistered<WeaponMountInfo>() || !repo.IsComponentTypeRegistered<PartMetadata>()
            || !repo.IsComponentTypeRegistered<WeaponState>()) return false;
        foreach (var e in repo.Query().With<WeaponMountInfo>().With<PartMetadata>().With<WeaponState>().Build())
        {
            if (repo.GetComponentRO<PartMetadata>(e).ParentEntity != unit) continue;
            if (repo.GetComponentRO<WeaponMountInfo>(e).MountIndex != mount) continue;
            child = e;
            return true;
        }
        return false;
    }

    // ── Weather ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One authored scalar of the weather (global or a region's). Humidity is a 0..1 ratio, the precipitation kind a 0..5 code, the distances and rates are not negative.</summary>
    public static void SetWeather(ref WeatherConditions c, ushort attributeId, double v)
    {
        float f = (float)v;
        switch (attributeId)
        {
            case AttributeIds.WeatherGroundTemperatureC:     c.GroundTemperatureC = f; break;
            case AttributeIds.WeatherLapseRateKPerKm:        c.LapseRateKPerKm = f; break;
            case AttributeIds.WeatherSeaLevelPressureHpa:    c.SeaLevelPressureHpa = Math.Max(0f, f); break;
            case AttributeIds.WeatherRelativeHumidity:       c.RelativeHumidity = Math.Clamp(f, 0f, 1f); break;
            case AttributeIds.WeatherVisibilityM:            c.VisibilityM = Math.Max(0f, f); break;
            case AttributeIds.WeatherFogTopM:                c.FogTopM = Math.Max(0f, f); break;
            case AttributeIds.WeatherFogVisibilityM:         c.FogVisibilityM = Math.Max(0f, f); break;
            case AttributeIds.WeatherPrecipitation:          c.Precipitation = (byte)Math.Clamp((int)Math.Round(v), 0, (int)PrecipitationKind.Hail); break;
            case AttributeIds.WeatherPrecipitationMmPerHour: c.PrecipitationMmPerHour = Math.Max(0f, f); break;
            case AttributeIds.WeatherFeatherM:               c.FeatherM = Math.Max(0f, f); break;
            default: throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "not a weather attribute");
        }
    }

    // ── Character (Initial state S6) ─────────────────────────────────────────────────────────────────────

    /// <summary>Which <see cref="Fdp.Toolkit.Behavior.Components.CombatTemperament"/> field.</summary>
    public enum TemperamentField : byte { RiskAcceptance, Obedience, Nerve, Resolve }

    /// <summary>
    /// ⭐ S6 — the proficiency a person authored: 0..1 into <c>Authored</c> with its set bit (a slider that only moved the raw value would leave the unit unchanged —
    /// the reader honours the bit); a NEGATIVE value is the reset (the bit cleared ⇒ the unit's, else the type's value). The one logic, shared by the JSON setter,
    /// the binary handler and the Brain tab's local path.
    /// </summary>
    public static void SetProficiency(ref Fdp.Toolkit.Behavior.Components.CombatProficiency p, double value)
    {
        if (value < 0d) { p.Set &= unchecked((byte)~Fdp.Toolkit.Behavior.Components.CombatProficiency.AuthoredSet); return; }
        p.Authored = (float)Math.Clamp(value, 0d, 1d);
        p.Set |= Fdp.Toolkit.Behavior.Components.CombatProficiency.AuthoredSet;
    }

    /// <summary>⭐ S6 — one temperament field a person authored (0..1, sets its bit); a NEGATIVE value clears the bit ⇒ the type's value (with its spread) again.</summary>
    public static void SetTemperament(ref Fdp.Toolkit.Behavior.Components.CombatTemperament t, TemperamentField field, double value)
    {
        byte bit = field switch
        {
            TemperamentField.RiskAcceptance => Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet,
            TemperamentField.Obedience => Fdp.Toolkit.Behavior.Components.CombatTemperament.ObedienceSet,
            TemperamentField.Nerve => Fdp.Toolkit.Behavior.Components.CombatTemperament.NerveSet,
            _ => Fdp.Toolkit.Behavior.Components.CombatTemperament.ResolveSet,
        };
        if (value < 0d) { t.Set &= unchecked((byte)~bit); return; }
        float v = (float)Math.Clamp(value, 0d, 1d);
        switch (field)
        {
            case TemperamentField.RiskAcceptance: t.RiskAcceptance = v; break;
            case TemperamentField.Obedience: t.Obedience = v; break;
            case TemperamentField.Nerve: t.Nerve = v; break;
            default: t.Resolve = v; break;
        }
        t.Set |= bit;
    }

    // ── Damage (Initial state S5) ────────────────────────────────────────────────────────────────────────

    /// <summary>Is <paramref name="part"/> a part id a <see cref="Fdp.Toolkit.Physics.BodyParts.PartDamage"/> holds (0..<c>MaxParts</c>-1)?</summary>
    public static bool IsPartId(int part) => part >= 0 && part < Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts;

    /// <summary>
    /// ⭐ S5 — one part's damage level: <paramref name="value"/> clamped to 0..255 and rounded. The one logic of the JSON setter, the binary handler and the
    /// mobility switch. A part id out of range changes nothing. <c>true</c> when the level went DOWN (the caller then gives capabilities back).
    /// </summary>
    public static bool SetPartLevel(ref Fdp.Toolkit.Physics.BodyParts.PartDamage d, int part, double value)
    {
        if (!IsPartId(part)) return false;
        byte level = (byte)Math.Clamp(Math.Round(value), 0d, 255d);
        bool lowered = level < d.LevelOf((byte)part);
        d.Set((byte)part, level);
        return lowered;
    }

    /// <summary>Does <paramref name="body"/> have a zone whose wound effect is Mobility (running gear, engine)?</summary>
    public static bool HasMobilityZone(Fdp.Toolkit.Physics.BodyParts.ResolvedBody? body)
    {
        if (body == null) return false;
        foreach (var z in body.Zones) if (z.Effect == WoundEffect.Mobility) return true;
        return false;
    }

    /// <summary>⭐ S5 — the mobility kill as a switch over <see cref="SetPartLevel"/>: set raises every part of every Mobility zone to at least its wounded
    /// threshold, clear lowers them to 0. <c>true</c> when any level went down.</summary>
    public static bool SetMobilityKill(ref Fdp.Toolkit.Physics.BodyParts.PartDamage d, Fdp.Toolkit.Physics.BodyParts.ResolvedBody body, bool kill)
    {
        bool lowered = false;
        foreach (var z in body.Zones)
        {
            if (z.Effect != WoundEffect.Mobility) continue;
            for (int p = 0; p < Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts; p++)
            {
                if (!z.Has((byte)p)) continue;
                double target = kill ? Math.Max(d.LevelOf((byte)p), Math.Clamp(z.WoundedAt, 0, 255)) : 0d;
                lowered |= SetPartLevel(ref d, p, target);
            }
        }
        return lowered;
    }

    /// <summary>
    /// ⭐ S5 — the binary handlers' ONE body for <see cref="AttributeIds.DamagePart"/> / <see cref="AttributeIds.DamageMobilityKill"/>. Refuses (touches nothing, so
    /// the router reports it unapplied) a part out of range, a unit with no body, a mobility switch on a type with no Mobility zone, and a node that does not own
    /// the component. A unit with no <c>PartDamage</c> gets one (the body's plan, as <c>HealthApplicationSystem</c> adds it) on the node that applies its Health.
    /// After the write the unit's capabilities follow at once: a LOWERED damage first gives back what only the damage took
    /// (<see cref="Fdp.Toolkit.Combat.CombatLife.RestoreHitCapabilities"/>), then the hit rule strips what the damage now takes — also in a paused world.
    /// </summary>
    public static void WriteDamage(BinaryPatchContext ctx, ushort attribute, int part, double value)
    {
        var repo = ctx.Repo;
        bool mobility = attribute == AttributeIds.DamageMobilityKill;
        if (!mobility && !IsPartId(part)) return;
        if (repo == null)   // creation path: the request carries the level; the body is judged where the unit lives
        {
            if (mobility || !ctx.PatchContext.CanWrite<Fdp.Toolkit.Physics.BodyParts.PartDamage>()) return;
            SetPartLevel(ref ctx.PatchContext.GetUnmanagedComponent<Fdp.Toolkit.Physics.BodyParts.PartDamage>(), part, value);
            return;
        }
        var entity = ctx.Entity;
        if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.BodyParts.PartDamage>()) return;
        var body = Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(repo, entity);
        if (body == null || (mobility && !HasMobilityZone(body))) return;
        if (repo.HasComponent<Fdp.Toolkit.Physics.BodyParts.PartDamage>(entity))
        {
            if (!ctx.PatchContext.CanWrite<Fdp.Toolkit.Physics.BodyParts.PartDamage>()) return;
        }
        else
        {
            if (!Fdp.Toolkit.Combat.HealthOwnership.AppliesHere(repo, entity)) return;   // PartDamage is owned with Health (Brain group)
            repo.AddComponent(entity, new Fdp.Toolkit.Physics.BodyParts.PartDamage { Plan = body.Plan });
            repo.SetAuthority<Fdp.Toolkit.Physics.BodyParts.PartDamage>(entity, true);
        }
        bool lowered;
        {
            ref var d = ref ctx.PatchContext.GetUnmanagedComponent<Fdp.Toolkit.Physics.BodyParts.PartDamage>();
            if (d.Plan == Fdp.Toolkit.Physics.BodyParts.BodyPlan.None) d.Plan = body.Plan;   // added empty by the edit framework
            lowered = mobility ? SetMobilityKill(ref d, body, value >= 0.5d) : SetPartLevel(ref d, part, value);
        }
        if (repo.IsComponentTypeRegistered<Health>() && repo.HasComponent<Health>(entity))
        {
            var health = repo.GetComponentRO<Health>(entity);
            if (lowered) Fdp.Toolkit.Combat.CombatLife.RestoreHitCapabilities(repo, entity, in health);
            Fdp.Toolkit.Combat.CombatLife.ApplyHitCapabilities(repo, entity, in health);
        }
    }

    // ── Weapon posture (Initial state S2) ────────────────────────────────────────────────────────────────

    /// <summary>A token of enum <typeparamref name="T"/>: a whole number or its name; <c>false</c> for anything the enum does not define.</summary>
    public static bool TryEnum<T>(in AttributeValue v, out T value) where T : struct, Enum
    {
        if (v.Kind == AttributeValueKind.CsString && !double.TryParse(v.StringValue, out _))
            return Enum.TryParse(v.StringValue, ignoreCase: true, out value) && Enum.IsDefined(value);
        double d = AsDouble(v);
        value = (T)Enum.ToObject(typeof(T), (byte)Math.Clamp((int)d, 0, 255));
        return d == Math.Floor(d) && d >= 0 && Enum.IsDefined(value);
    }

    /// <summary>The same token read from JSON.</summary>
    public static bool TryEnum<T>(ref Utf8JsonReader r, out T value) where T : struct, Enum
        => TryEnum(r.TokenType == JsonTokenType.Number ? AttributeValue.FromDouble(r.GetDouble()) : AttributeValue.FromString(r.GetString()), out value);


    // ── Stance ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A stance token: a number (0 Standing · 1 Crouched · 2 Prone) or its name. <c>false</c> for anything that is not a <see cref="StanceId"/>.</summary>
    public static bool TryStance(in AttributeValue v, out StanceId stance)
    {
        if (v.Kind == AttributeValueKind.CsString && !double.TryParse(v.StringValue, out _))
            return Enum.TryParse(v.StringValue, ignoreCase: true, out stance) && Enum.IsDefined(stance);
        double d = AsDouble(v);
        stance = (StanceId)(byte)Math.Clamp((int)d, 0, 255);
        return d == Math.Floor(d) && Enum.IsDefined(stance);
    }

    /// <summary>The same token read from JSON.</summary>
    public static bool TryStance(ref Utf8JsonReader r, out StanceId stance)
        => TryStance(r.TokenType == JsonTokenType.Number ? AttributeValue.FromDouble(r.GetDouble()) : AttributeValue.FromString(r.GetString()), out stance);

    /// <summary>Does <paramref name="entity"/>'s TYPE declare <paramref name="stance"/>? From the TKB's animation definition (<c>SupportedStances</c>). A world
    /// with no TKB (or a type with no animation definition) cannot say ⇒ <c>true</c> for any defined stance.</summary>
    public static bool Declares(EntityRepository repo, Entity entity, StanceId stance)
    {
        if (!Combat.CombatTkb.TryGetTemplate(repo, entity, out var template)) return true;
        var anim = template.GetDescriptor<CharacterAnimationDefDto>();
        return anim == null || anim.SupportedStances.Contains(stance);
    }

    /// <summary>Is time advancing in <paramref name="repo"/>? Exactly the scheduler's test for a <c>[TickMode(WhenTimeAdvances)]</c> system (a frame delta &gt; 0, <c>SystemScheduler.ExecuteSystem</c>).
    /// A world with no <see cref="GlobalTime"/> is not advancing.</summary>
    public static bool TimeAdvances(EntityRepository repo)
        => repo.HasSingleton<GlobalTime>() && repo.GetSingleton<GlobalTime>().DeltaTime > 0f;

    /// <summary>
    /// Sets the stance of <paramref name="entity"/>. ⭐ Q7 of the Details forms design: in a world whose time is NOT advancing (paused / Edit mode, where
    /// <c>StanceTransitionSystem</c> does not run) the intent AND the status change at once, so the body shows it and nothing plays; while time advances it is an
    /// ordinary <see cref="StanceRequest"/> with the intent's blend. The status is only written where this node owns it (the body's node).
    /// </summary>
    public static void ApplyStance(EntityRepository repo, Entity entity, StanceId stance)
    {
        if (!TimeAdvances(repo))
        {
            ref var intent = ref repo.GetComponentRW<StanceIntent>(entity);
            if (intent.TargetStance != stance) { intent.TargetStance = stance; intent.Version++; }
            intent.BlendTime = 0f;
            if (repo.IsComponentTypeRegistered<StanceStatus>() && repo.HasComponent<StanceStatus>(entity) && repo.HasAuthority<StanceStatus>(entity))
                SeedStatus(repo, entity);
            return;
        }
        float blend = repo.GetComponentRO<StanceIntent>(entity).BlendTime;
        StanceRequest.Set(repo, entity, stance, blend > 0f ? blend : 0.3f);
    }

    /// <summary>The body starts IN the intent's stance: status = intent, acknowledged, no transition (used at load and by the paused set).</summary>
    public static void SeedStatus(EntityRepository repo, Entity entity)
    {
        if (!repo.IsComponentTypeRegistered<StanceIntent>() || !repo.IsComponentTypeRegistered<StanceStatus>()
            || !repo.HasComponent<StanceIntent>(entity) || !repo.HasComponent<StanceStatus>(entity)) return;
        var intent = repo.GetComponentRO<StanceIntent>(entity);
        ref var status = ref repo.GetComponentRW<StanceStatus>(entity);
        status.CurrentStance = intent.TargetStance;
        status.AckVersion = intent.Version;
        status.Phase = StanceTransitionPhase.Idle;
        status.TransitionProgress = 0f;
    }
}
