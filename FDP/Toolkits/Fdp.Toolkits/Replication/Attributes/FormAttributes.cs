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
