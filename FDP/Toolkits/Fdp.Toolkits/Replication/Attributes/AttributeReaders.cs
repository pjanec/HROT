using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Common;
using Hrot.MuscleCharacter.Animation.Components;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b>R-1 (AR-1…AR-3) — the attribute vocabulary's READ side.</b>
///
/// <para>📄 <c>docs/UX/DESIGN_Review_Perspective.md</c> §6.13a · <c>docs/UX/DESIGN_Details_Forms.md</c> §2.4 (<i>"a row reads only the world — components, the attribute
/// vocabulary's READ side"</i>). The vocabulary (<see cref="AttributeVocabulary"/>) declared what an attribute IS and had a JSON setter and a binary handler per row,
/// and NO reader; this is the reader — ONE entry per row, keyed by <see cref="AttributeDefinition.AttributeId"/>, beside the setters.</para>
///
/// <para>⭐ <b>Semantics = what the attribute MEANS when read.</b> Where a Details form reads an attribute (<c>HrotForms</c>) the reader returns the same thing
/// (the EFFECTIVE proficiency and temperament, the network id a weapon is aimed at, the part's level); otherwise it returns the field the setter writes.
/// ⭐ A force is read back as the token the setter takes (<c>FORCE_FRIENDLY</c> / <c>FORCE_OPPOSING</c> / <c>FORCE_NEUTRAL</c>).</para>
///
/// <para>⛔⛔ <b>HOT PATH (R-2084): a form reads through here every frame.</b> Every read allocates 0 B — numbers are plain struct values, text is a
/// <see cref="FixedString64"/> (<see cref="AttributeValue.Text"/>), no LINQ, no closure per call. ⚠ The two exceptions are the mounts 1–3 of <c>Ammo.*</c> and a
/// wildcard's sub-index list on a unit with a body, which walk a query / a list — user-action paths, not per-frame ones. ⭐ Railed by
/// <c>TheFourRoutingTablesAgreeTests</c> (every row has a reader · set-then-read agrees · 0 B).</para>
///
/// <para>⭐ <b>False</b> = the entity does not have the value (no such component, a dead entity, a sub-index the attribute does not have).</para>
/// </summary>
public static class AttributeReaders
{
    /// <summary>One attribute's reader: <c>false</c> when the entity does not have it.</summary>
    public delegate bool Reader(EntityRepository repo, Entity entity, int subIndex, out AttributeValue value);

    private delegate bool Project<T>(EntityRepository repo, Entity entity, in T component, int subIndex, out AttributeValue value) where T : unmanaged;

    // ⭐ before the table: static fields initialise in textual order
    private static readonly FixedString64 ForceFriendly = new("FORCE_FRIENDLY");
    private static readonly FixedString64 ForceOpposing = new("FORCE_OPPOSING");
    private static readonly FixedString64 ForceNeutral = new("FORCE_NEUTRAL");

    private static readonly Dictionary<ushort, Reader> s_byId = BuildTable();
    private static readonly Dictionary<string, ushort> s_idByPath = BuildPathIndex();
    private static readonly Dictionary<ushort, AttributeDefinition> s_definitionById = BuildDefinitionIndex();

    // ── the public face ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Is there a reader for <paramref name="attributeId"/>? ⭐ Every <see cref="AttributeVocabulary.All"/> row has one (the rail says so).</summary>
    public static bool HasReader(ushort attributeId) => s_byId.ContainsKey(attributeId);

    /// <summary>The vocabulary row of <paramref name="attributeId"/>, or null.</summary>
    public static AttributeDefinition? DefinitionOf(ushort attributeId) => s_definitionById.TryGetValue(attributeId, out var d) ? d : null;

    /// <summary>
    /// ⭐ Reads attribute <paramref name="attributeId"/> of <paramref name="entity"/>. <paramref name="subIndex"/> is the wildcard's index (<c>Ammo.*</c>: the mount ·
    /// <c>Damage.Part.*</c>: the part id · <c>Weapon.AimPoint.*</c>: the axis 0 x · 1 y · 2 z) and 0 for every other attribute. 0 B.
    /// </summary>
    public static bool TryRead(EntityRepository repo, Entity entity, ushort attributeId, int subIndex, out AttributeValue value)
    {
        if (repo != null && !entity.IsNull && repo.IsAlive(entity) && s_byId.TryGetValue(attributeId, out var reader))
            return reader(repo, entity, subIndex, out value);
        value = default;
        return false;
    }

    /// <summary>
    /// ⭐ The same, by the vocabulary path — <c>Health.Current</c>, or a wildcard with its sub-index as the last segment (<c>Ammo.2</c>, <c>Damage.Part.3</c>,
    /// <c>Weapon.AimPoint.1</c>). ⚠ Allocates for a wildcard path (it splits the string): for a per-frame read resolve once with <see cref="TryResolve"/>.
    /// </summary>
    public static bool TryRead(EntityRepository repo, Entity entity, string jsonPath, out AttributeValue value)
    {
        if (TryResolve(jsonPath, out ushort id, out int sub)) return TryRead(repo, entity, id, sub, out value);
        value = default;
        return false;
    }

    /// <summary>
    /// ⭐ A path as the world reads it → the attribute id and the sub-index. <c>Health.Current</c> → (50, 0) · <c>Ammo.2</c> → (<c>Ammo</c>, 2) · the wildcard's own
    /// spelling (<c>Ammo.*</c>) → (<c>Ammo</c>, 0). <c>false</c> for a path the vocabulary does not have.
    /// </summary>
    public static bool TryResolve(string? jsonPath, out ushort attributeId, out int subIndex)
    {
        attributeId = 0; subIndex = 0;
        if (string.IsNullOrEmpty(jsonPath)) return false;
        if (s_idByPath.TryGetValue(jsonPath, out attributeId)) return true;
        int dot = jsonPath.LastIndexOf('.');
        if (dot > 0 && dot < jsonPath.Length - 1 && int.TryParse(jsonPath.AsSpan(dot + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out subIndex)
            && s_idByPath.TryGetValue(string.Concat(jsonPath.AsSpan(0, dot), ".*"), out attributeId))
            return true;
        attributeId = 0; subIndex = 0;
        return false;
    }

    /// <summary>
    /// ⭐ The sub-indices <paramref name="attributeId"/> has on <paramref name="entity"/>: the mounts present (<c>Ammo.*</c>), the parts of its body (<c>Damage.Part.*</c>), the axes
    /// (<c>Weapon.AimPoint.*</c>: 0..2) — appended to <paramref name="into"/> (never cleared). A non-wildcard attribute adds <c>0</c> when the entity has it; nothing when it has not.
    /// </summary>
    public static void SubIndicesOf(EntityRepository repo, Entity entity, ushort attributeId, List<int> into)
    {
        if (!s_definitionById.TryGetValue(attributeId, out var def)) return;
        if (!def.JsonPath.EndsWith(".*", StringComparison.Ordinal))
        {
            if (TryRead(repo, entity, attributeId, 0, out _)) into.Add(0);
            return;
        }
        switch (attributeId)
        {
            case AttributeIds.Ammo:
                if (TryRead(repo, entity, attributeId, 0, out _)) into.Add(0);
                for (int m = 1; m <= MaxMount; m++) if (FormAttributes.TryFindMount(repo, entity, m, out _)) into.Add(m);
                break;
            case AttributeIds.WeaponAimPoint:
                if (TryRead(repo, entity, attributeId, 0, out _)) { into.Add(0); into.Add(1); into.Add(2); }
                break;
            case AttributeIds.DamagePart:
            {
                var zones = BodyZonesOf(repo, entity);
                bool has = Has<Fdp.Toolkit.Physics.BodyParts.PartDamage>(repo, entity);
                for (int p = 0; p < Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts; p++)
                {
                    bool inBody = zones != null && ZonesHavePart(zones, p);
                    if (inBody || (has && repo.GetComponentRO<Fdp.Toolkit.Physics.BodyParts.PartDamage>(entity).LevelOf((byte)p) > 0)) into.Add(p);
                }
                break;
            }
        }
    }

    /// <summary>The highest mount index a unit has (<c>WeaponMountInfo.MountIndex</c> 1–3; mount 0 is the unit's own weapon).</summary>
    public const int MaxMount = 3;

    // ── the table ──────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<ushort, AttributeDefinition> BuildDefinitionIndex()
    {
        var d = new Dictionary<ushort, AttributeDefinition>();
        foreach (var row in AttributeVocabulary.All) d[row.AttributeId] = row;
        return d;
    }

    private static Dictionary<string, ushort> BuildPathIndex()
    {
        var d = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var row in AttributeVocabulary.All) d[row.JsonPath] = row.AttributeId;
        return d;
    }

    private static bool Has<T>(EntityRepository r, Entity e) where T : unmanaged => r.IsComponentTypeRegistered<T>() && r.HasComponent<T>(e);

    private static Dictionary<ushort, Reader> BuildTable()
    {
        var t = new Dictionary<ushort, Reader>();

        void Of<T>(ushort id, Project<T> project) where T : unmanaged
            => t[id] = (EntityRepository r, Entity e, int sub, out AttributeValue v) =>
            {
                if (!Has<T>(r, e)) { v = default; return false; }
                return project(r, e, in r.GetComponentRO<T>(e), sub, out v);
            };

        // a number: the delegate only picks the value
        void D<T>(ushort id, Func<T, double> get) where T : unmanaged
            => Of<T>(id, (EntityRepository _, Entity _, in T c, int _, out AttributeValue v) => { v = AttributeValue.FromDouble(get(c)); return true; });
        void I<T>(ushort id, Func<T, int> get) where T : unmanaged
            => Of<T>(id, (EntityRepository _, Entity _, in T c, int _, out AttributeValue v) => { v = AttributeValue.FromInt(get(c)); return true; });

        // ── EntityInfo ──
        Of<EntityInfo>(AttributeIds.Name, (EntityRepository _, Entity _, in EntityInfo c, int _, out AttributeValue v) => { v = AttributeValue.FromText(c.Name); return true; });
        Of<EntityInfo>(AttributeIds.Affiliation, (EntityRepository _, Entity _, in EntityInfo c, int _, out AttributeValue v) => { v = AttributeValue.FromText(ForceToken(c.ForceId)); return true; });

        // ── SimTransform — local ENU metres, compass heading (the setter's conversions, inverted) ──
        D<SimTransform>(AttributeIds.PositionX, static c => c.Position.X);
        D<SimTransform>(AttributeIds.PositionY, static c => c.Position.Y);
        D<SimTransform>(AttributeIds.PositionZ, static c => c.Position.Z);
        D<SimTransform>(AttributeIds.Heading, static c => SimTransformBridgeSystem.RotationToHeadingDeg(c.Rotation));

        // ── the World Environment's clock ──
        Of<Fdp.Toolkit.Weather.ScenarioClock>(AttributeIds.ScenarioStartUtcTicks, (EntityRepository _, Entity _, in Fdp.Toolkit.Weather.ScenarioClock c, int _, out AttributeValue v) => { v = AttributeValue.FromLong(c.StartUtcTicks); return true; });
        I<Fdp.Toolkit.Weather.ScenarioClock>(AttributeIds.ScenarioUtcOffsetMinutes, static c => c.DisplayUtcOffsetMinutes);

        // ── a civilian's fright ──
        D<Fdp.Toolkit.Civilians.CivilianState>(AttributeIds.CivilianFear, static c => c.Fear);
        D<Fdp.Toolkit.Civilians.CivilianState>(AttributeIds.CivilianFrightFromX, static c => c.FrightFrom.X);
        D<Fdp.Toolkit.Civilians.CivilianState>(AttributeIds.CivilianFrightFromY, static c => c.FrightFrom.Y);
        D<Fdp.Toolkit.Civilians.CivilianState>(AttributeIds.CivilianFrightFromZ, static c => c.FrightFrom.Z);

        // ── a rule entity's control (unmanaged) and live edit (a managed record) ──
        I<Fdp.Toolkit.ScenarioRules.RuleControl>(AttributeIds.RuleRequest, static c => c.Request);
        I<Fdp.Toolkit.ScenarioRules.RuleControl>(AttributeIds.RuleSeq, static c => c.Seq);
        I<Fdp.Toolkit.ScenarioRules.RuleControl>(AttributeIds.RuleOrigin, static c => c.Origin);
        void Edit(ushort id, Func<Fdp.Toolkit.ScenarioRules.RuleEdit, AttributeValue> get)
            => t[id] = (EntityRepository r, Entity e, int _, out AttributeValue v) =>
            {
                var view = (Fdp.ModuleHost.Abstractions.ISimulationView)r;
                if (!view.HasManagedComponent<Fdp.Toolkit.ScenarioRules.RuleEdit>(e)) { v = default; return false; }
                v = get(view.GetManagedComponentRO<Fdp.Toolkit.ScenarioRules.RuleEdit>(e));
                return true;
            };
        // the whole JSON as the string it is (no copy); Text carries its first 64 bytes for a 0 B glance
        Edit(AttributeIds.RuleParams, static c => new AttributeValue { Kind = AttributeValueKind.CsString, StringValue = c.Json, Text = new FixedString64(c.Json.AsSpan(0, Math.Min(c.Json.Length, 40))) });
        Edit(AttributeIds.RuleEditSeq, static c => AttributeValue.FromInt(c.Seq));
        Edit(AttributeIds.RuleEditOrigin, static c => AttributeValue.FromInt(c.Origin));

        // ── the form attributes (B-4 / Details forms D2) ──
        D<Health>(AttributeIds.HealthCurrent, static c => c.Current);
        Of<Playable>(AttributeIds.PlayableSide, (EntityRepository _, Entity _, in Playable c, int _, out AttributeValue v) => { v = AttributeValue.FromText(ForceToken(c.Side)); return true; });
        Of<Playable>(AttributeIds.PlayableSlotName, (EntityRepository _, Entity _, in Playable c, int _, out AttributeValue v) => { v = AttributeValue.FromText(Widen(c.SlotName)); return true; });
        Of<SpawnArea>(AttributeIds.SpawnAreaSide, (EntityRepository _, Entity _, in SpawnArea c, int _, out AttributeValue v) => { v = AttributeValue.FromText(ForceToken(c.Side)); return true; });
        D<SpawnArea>(AttributeIds.SpawnAreaFacingDeg, static c => c.FacingDeg);
        // Ammo.<mount>: mount 0 is the unit's own WeaponState; mounts 1–3 are CHILD entities (FormAttributes.TryFindMount — a query, so not a per-frame path)
        t[AttributeIds.Ammo] = (EntityRepository r, Entity e, int mount, out AttributeValue v) =>
        {
            if (mount <= 0)
            {
                if (!Has<WeaponState>(r, e)) { v = default; return false; }
                v = AttributeValue.FromInt(r.GetComponentRO<WeaponState>(e).Ammo);
                return true;
            }
            if (!FormAttributes.TryFindMount(r, e, mount, out var child)) { v = default; return false; }
            v = AttributeValue.FromInt(r.GetComponentRO<WeaponState>(child).Ammo);
            return true;
        };
        I<StanceIntent>(AttributeIds.Stance, static c => (int)c.TargetStance);   // the forms read the INTENT

        // ── the weapon's start state ──
        I<WeaponPosture>(AttributeIds.WeaponPosture, static c => (int)c.Kind);
        Of<WeaponPosture>(AttributeIds.WeaponAimPoint, (EntityRepository _, Entity _, in WeaponPosture c, int axis, out AttributeValue v) =>
        {
            v = default;
            if (axis < 0 || axis > 2) return false;
            v = AttributeValue.FromDouble(axis == 0 ? c.Point.X : axis == 1 ? c.Point.Y : c.Point.Z);
            return true;
        });
        // the target's NETWORK id (what the attribute is written with), 0 = none / gone
        Of<WeaponPosture>(AttributeIds.WeaponAimTarget, (EntityRepository r, Entity _, in WeaponPosture c, int _, out AttributeValue v) =>
        {
            var target = c.Target;
            v = AttributeValue.FromLong(target.IsNull || !r.IsAlive(target) ? 0 : NetworkIdResolver.RuntimeNetworkIdOf(r, target));
            return true;
        });
        I<WeaponPosture>(AttributeIds.WeaponHeldItem, static c => (int)c.HeldItem);

        // ── the damage per part, and the mobility kill as one switch ──
        t[AttributeIds.DamagePart] = (EntityRepository r, Entity e, int part, out AttributeValue v) =>
        {
            v = default;
            if (!FormAttributes.IsPartId(part)) return false;
            if (Has<Fdp.Toolkit.Physics.BodyParts.PartDamage>(r, e))
            {
                v = AttributeValue.FromDouble(r.GetComponentRO<Fdp.Toolkit.Physics.BodyParts.PartDamage>(e).LevelOf((byte)part));
                return true;
            }
            // no PartDamage yet (the owner adds it on the first write): a unit with a body that has this part is undamaged there — the forms show 0
            var zones = BodyZonesOf(r, e);
            if (zones == null || !ZonesHavePart(zones, part)) return false;
            v = AttributeValue.FromDouble(0);
            return true;
        };
        t[AttributeIds.DamageMobilityKill] = (EntityRepository r, Entity e, int _, out AttributeValue v) =>
        {
            v = default;
            var zones = BodyZonesOf(r, e);
            if (zones == null) return false;
            bool any = false, killed = true;
            bool has = Has<Fdp.Toolkit.Physics.BodyParts.PartDamage>(r, e);
            for (int k = 0; k < zones.Count; k++)
            {
                var z = zones[k];
                if (z.Effect != WoundEffect.Mobility) continue;
                for (int p = 0; p < Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts; p++)
                {
                    if (!z.Has((byte)p)) continue;
                    any = true;
                    int level = has ? r.GetComponentRO<Fdp.Toolkit.Physics.BodyParts.PartDamage>(e).LevelOf((byte)p) : 0;
                    if (level < Math.Clamp(z.WoundedAt, 0, 255)) killed = false;
                }
            }
            if (!any) return false;   // no Mobility zone: the switch does not exist for this unit
            v = AttributeValue.FromInt(killed ? 1 : 0);
            return true;
        };

        // ── the AI character settings: the EFFECTIVE value (authored, else the unit's, else the type's, else the default) — what the forms show ──
        Of<CombatProficiency>(AttributeIds.Proficiency, (EntityRepository r, Entity e, in CombatProficiency _, int _, out AttributeValue v) => { v = AttributeValue.FromDouble(ProficiencyOf.Value(r, e)); return true; });
        void Trait(ushort id, Func<Fdp.ModuleHost.Abstractions.ISimulationView, Entity, float> effective)
            => Of<CombatTemperament>(id, (EntityRepository r, Entity e, in CombatTemperament _, int _, out AttributeValue v) => { v = AttributeValue.FromDouble(effective(r, e)); return true; });
        Trait(AttributeIds.TemperamentRiskAcceptance, TemperamentOf.RiskAcceptance);
        Trait(AttributeIds.TemperamentObedience, TemperamentOf.Obedience);
        Trait(AttributeIds.TemperamentNerve, TemperamentOf.Nerve);
        Trait(AttributeIds.TemperamentResolve, TemperamentOf.Resolve);

        // ── the weather scalars ──
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherGroundTemperatureC, static c => c.GroundTemperatureC);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherLapseRateKPerKm, static c => c.LapseRateKPerKm);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherSeaLevelPressureHpa, static c => c.SeaLevelPressureHpa);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherRelativeHumidity, static c => c.RelativeHumidity);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherVisibilityM, static c => c.VisibilityM);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherFogTopM, static c => c.FogTopM);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherFogVisibilityM, static c => c.FogVisibilityM);
        I<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherPrecipitation, static c => c.Precipitation);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherPrecipitationMmPerHour, static c => c.PrecipitationMmPerHour);
        D<Fdp.Toolkit.Weather.WeatherConditions>(AttributeIds.WeatherFeatherM, static c => c.FeatherM);

        return t;
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The token the <c>Affiliation</c> / <c>Side</c> setters take for <paramref name="f"/> (a constant fixed string: 0 B).</summary>
    private static FixedString64 ForceToken(ForceId f) => f switch { ForceId.Friend => ForceFriendly, ForceId.Hostile => ForceOpposing, _ => ForceNeutral };

    /// <summary>A 32-byte fixed string as a 64-byte one — both are null-terminated UTF-8 buffers, so a copy of the 32 bytes (0 B).</summary>
    private static FixedString64 Widen(in FixedString32 s)
    {
        FixedString64 r = default;
        System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(ref System.Runtime.CompilerServices.Unsafe.As<FixedString64, byte>(ref r),
            ref System.Runtime.CompilerServices.Unsafe.As<FixedString32, byte>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in s)), 32);
        return r;
    }

    /// <summary>The zones of the unit's body (a unit that can be damaged: PartDamage, Health and a resolved body), or null.</summary>
    private static IReadOnlyList<Fdp.Toolkit.Physics.BodyParts.ResolvedZone>? BodyZonesOf(EntityRepository r, Entity e)
    {
        if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Physics.BodyParts.PartDamage>() || !r.IsComponentTypeRegistered<Health>() || !r.HasComponent<Health>(e)) return null;
        return Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(r, e)?.Zones;
    }

    private static bool ZonesHavePart(IReadOnlyList<Fdp.Toolkit.Physics.BodyParts.ResolvedZone> zones, int part)
    {
        for (int k = 0; k < zones.Count; k++) if (zones[k].Has((byte)part)) return true;
        return false;
    }
}
