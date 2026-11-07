using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-A1′</c> — the ONE declaration of what an entity attribute IS.</b>
///
/// <para>📄 <c>docs/blueprints/Architect_Question_59_Attribute_Vocabulary_Single_Source.md</c> §7.2 · §9.</para>
///
/// <para>🔒 <b>User ruling, <c>2026-08-26</c>:</b> attributes are *"entity-related, network agnostic"*.
/// ⇒ ⛔ <b>no descriptor ordinal here.</b> A descriptor is a NED grouping; the join between the two worlds is
/// the ECS <b>component</b>, and <see cref="ComponentDescriptorMap"/> supplies the rest.</para>
///
/// <para>⭐⭐⭐ <b>WHAT DERIVES FROM THIS, AND WHAT HONESTLY CANNOT.</b> ⚠ Stated plainly, because the first
/// cut of this design claimed more than is true:</para>
///
/// <list type="bullet">
///   <item>⭐⭐⭐ <b>DERIVED — the edge table</b> *(<c>AttributeCompilerFactory.BuildEdgeCompiler</c>)*. It is
///   pure metadata: path → id + kind, nothing else. 📌 <b>And it is the table that actually drifted</b> —
///   <c>AX-018</c>'s missing <c>Heading</c> row. ⇒ it can no longer be missing a row, by construction.</item>
///   <item>⭐⭐ <b>DERIVED — the published schema</b> *(<c>JsonAttributeCompiler.ExportSchema</c>)*, which now
///   knows the real type of every path instead of calling everything a string.</item>
///   <item>⛔ <b>NOT DERIVED — the JSON setters and the binary handlers.</b> 📐 Measured: each carries
///   <b>per-attribute logic</b> *(parse this token, write that field, share a geo accumulator)* with two
///   different delegate signatures. ⇒ ⭐ <b>they are not redundancy, they are distinct code</b>, and folding
///   them into one record would produce a worse thing than the three tables. ⚠ They are instead
///   <b>cross-checked</b> against this list by <c>TheFourRoutingTablesAgreeTests</c>.</item>
/// </list>
///
/// <para>⛔⛔ <b>Adding an attribute means adding a row HERE FIRST.</b> The edge table and the schema then
/// follow automatically, and the rails red until the JSON setter and binary handler exist too. 📌 That is the
/// <c>UXI-30</c>/<c>AX-001</c> shape: the half-registration <c>AX-018</c> found is now unrepresentable for
/// the metadata, and loudly detected for the logic.</para>
/// </summary>
public static class AttributeVocabulary
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE VOCABULARY.</b> ⚠ ⛔ Do not add an entry without the JSON setter in
    /// <see cref="AttributeCompilerFactory.Build"/> and the handler in an installer.
    /// </summary>
    public static readonly IReadOnlyList<AttributeDefinition> All = new AttributeDefinition[]
    {
        new("Name",                  AttributeIds.Name,        AttributeValueKind.CsString,  typeof(EntityInfo)),

        // ⚠ Declared CsString, and a NUMBER is equally valid: ExCon's default enum serialisation emits `2`
        //   rather than "FORCE_OPPOSING". ⭐ AX-018 made the edge compiler honour the token, so both cross.
        new("Affiliation",           AttributeIds.Affiliation, AttributeValueKind.CsString,  typeof(EntityInfo)),

        // ⭐⭐ CE-4073 (AQ107 B's tail, R-4092) — the position is LOCAL ENU METRES, the frame the NED wire speaks, so each axis is a plain
        //   copy into SimTransform.Position: nothing to convert with, therefore NOTHING to gate on — these three exist on EVERY host.
        //   🔴 They used to be three geodetic latitude / longitude / altitude paths (ids 10–12, retired), registered only when a host had an
        //   IGeographicTransform ⇒ a host with no terrain silently dropped a position patch.
        new("Position.X",            AttributeIds.PositionX,   AttributeValueKind.CsFloat64, typeof(SimTransform)),
        new("Position.Y",            AttributeIds.PositionY,   AttributeValueKind.CsFloat64, typeof(SimTransform)),
        new("Position.Z",            AttributeIds.PositionZ,   AttributeValueKind.CsFloat64, typeof(SimTransform)),

        // ⭐⭐ Heading is a compass heading, already in the units the conversion takes. 📌 Q59-N1 renamed the id from
        //    GeoHeading, which had advertised a geodetic-prefixed heading path that does not exist.
        new("Heading",               AttributeIds.Heading,     AttributeValueKind.CsFloat64, typeof(SimTransform)),
        // ⭐ CE-3216 S1-0 (W-8) — the World Environment's clock: a new scenario start time, and its display offset.
        new("ScenarioStartUtcTicks",    AttributeIds.ScenarioStartUtcTicks,    AttributeValueKind.CsInt64, typeof(Fdp.Toolkit.Weather.ScenarioClock)),
        new("ScenarioUtcOffsetMinutes", AttributeIds.ScenarioUtcOffsetMinutes, AttributeValueKind.CsInt32, typeof(Fdp.Toolkit.Weather.ScenarioClock)),
        // ⭐ CE-4024 (AQ93 §15 K5) — a civilian's fright: a converted SUMO pedestrian is born in panic on the brain node
        new("Fear",                  AttributeIds.CivilianFear,        AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Civilians.CivilianState), BrainLocal: true),
        new("FrightFrom.X",          AttributeIds.CivilianFrightFromX, AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Civilians.CivilianState), BrainLocal: true),
        new("FrightFrom.Y",          AttributeIds.CivilianFrightFromY, AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Civilians.CivilianState), BrainLocal: true),
        new("FrightFrom.Z",          AttributeIds.CivilianFrightFromZ, AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Civilians.CivilianState), BrainLocal: true),
        // ⭐ rules V-4 (§00.10) — a rule entity's runtime control: applied on the node that owns the entity, a change-request elsewhere; the component is brain-local (no descriptor)
        new("Rule.Request",          AttributeIds.RuleRequest,         AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.ScenarioRules.RuleControl), BrainLocal: true),
        new("Rule.Seq",              AttributeIds.RuleSeq,             AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.ScenarioRules.RuleControl), BrainLocal: true),
        new("Rule.Origin",           AttributeIds.RuleOrigin,          AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.ScenarioRules.RuleControl), BrainLocal: true),
        // ⭐ rules ② (§00.11a) — a LIVE EDIT of a rule's params: the MANAGED record RuleEdit, replaced whole by each handler (R1); applied by the owner's RuleEditSystem
        new("Rule.Params",           AttributeIds.RuleParams,          AttributeValueKind.CsString,  typeof(Fdp.Toolkit.ScenarioRules.RuleEdit), BrainLocal: true),
        new("Rule.EditSeq",          AttributeIds.RuleEditSeq,         AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.ScenarioRules.RuleEdit), BrainLocal: true),
        new("Rule.EditOrigin",       AttributeIds.RuleEditOrigin,      AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.ScenarioRules.RuleEdit), BrainLocal: true),
        // ⭐ B-4 / Details forms D2 (docs/UX/DESIGN_Details_Forms.md §4.1) — the fields a form writes. Applied by the node that owns the component; a write elsewhere becomes the router's change-request.
        new("Health.Current",        AttributeIds.HealthCurrent,       AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Combat.Components.Health)),
        // ⚠ Playable / SpawnArea / WeaponState have NO descriptor (no translator republishes them): applied on the owner, not replicated — hence BrainLocal.
        new("Playable.Side",         AttributeIds.PlayableSide,        AttributeValueKind.CsString,  typeof(Hrot.Common.Playable), BrainLocal: true),
        new("Playable.SlotName",     AttributeIds.PlayableSlotName,    AttributeValueKind.CsString,  typeof(Hrot.Common.Playable), BrainLocal: true),
        new("SpawnArea.Side",        AttributeIds.SpawnAreaSide,       AttributeValueKind.CsString,  typeof(Hrot.Common.SpawnArea), BrainLocal: true),
        new("SpawnArea.FacingDeg",   AttributeIds.SpawnAreaFacingDeg,  AttributeValueKind.CsFloat64, typeof(Hrot.Common.SpawnArea), BrainLocal: true),
        // ⭐ ONE attribute for the four mounts: the mount index is the sub-index (JSON {"Ammo":{"2":5}}); the numeric segment normalises to '*'.
        new("Ammo.*",                AttributeIds.Ammo,                AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.Combat.Components.WeaponState), BrainLocal: true),
        new("Stance",                AttributeIds.Stance,              AttributeValueKind.CsInt32,   typeof(Hrot.MuscleCharacter.Animation.Components.StanceIntent)),
        // ⭐ Initial state S2 (docs/UX/DESIGN_Initial_State.md §3) — the weapon's start state; WeaponPosture is replicated (dtWeaponPosture), applied by the brain's node
        new("Weapon.Posture",        AttributeIds.WeaponPosture,       AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.Combat.Components.WeaponPosture)),
        new("Weapon.AimPoint.*",     AttributeIds.WeaponAimPoint,      AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Combat.Components.WeaponPosture)),
        new("Weapon.AimTarget",      AttributeIds.WeaponAimTarget,     AttributeValueKind.CsInt64,   typeof(Fdp.Toolkit.Combat.Components.WeaponPosture)),
        new("Weapon.HeldItem",       AttributeIds.WeaponHeldItem,      AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.Combat.Components.WeaponPosture)),
        // ⭐ Initial state S6 (docs/UX/DESIGN_Initial_State.md §3) — the AI character settings; a negative value is the reset (clears the authored bit)
        new("Proficiency",                AttributeIds.Proficiency,                AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Behavior.Components.CombatProficiency)),
        new("Temperament.RiskAcceptance", AttributeIds.TemperamentRiskAcceptance, AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Behavior.Components.CombatTemperament)),
        new("Temperament.Obedience",      AttributeIds.TemperamentObedience,      AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Behavior.Components.CombatTemperament)),
        new("Temperament.Nerve",          AttributeIds.TemperamentNerve,          AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Behavior.Components.CombatTemperament)),
        new("Temperament.Resolve",        AttributeIds.TemperamentResolve,        AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Behavior.Components.CombatTemperament)),
        // the authored scalars of the GLOBAL weather and of a weather region (one component, DESIGN_Weather_And_Time §2); the wind levels and cloud layers are lists, not scalars — not attributes (yet)
        new("Weather.GroundTemperatureC",     AttributeIds.WeatherGroundTemperatureC,     AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.LapseRateKPerKm",        AttributeIds.WeatherLapseRateKPerKm,        AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.SeaLevelPressureHpa",    AttributeIds.WeatherSeaLevelPressureHpa,    AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.RelativeHumidity",       AttributeIds.WeatherRelativeHumidity,       AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.VisibilityM",            AttributeIds.WeatherVisibilityM,            AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.FogTopM",                AttributeIds.WeatherFogTopM,                AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.FogVisibilityM",         AttributeIds.WeatherFogVisibilityM,         AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.Precipitation",          AttributeIds.WeatherPrecipitation,          AttributeValueKind.CsInt32,   typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.PrecipitationMmPerHour", AttributeIds.WeatherPrecipitationMmPerHour, AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
        new("Weather.FeatherM",               AttributeIds.WeatherFeatherM,               AttributeValueKind.CsFloat64, typeof(Fdp.Toolkit.Weather.WeatherConditions)),
    };

    /// <summary>
    /// ⭐⭐ The distinct ECS components the attribute path writes.
    ///
    /// <para>⭐⭐⭐ This is the set <see cref="ComponentDescriptorMap"/> must cover, and the reason the
    /// coverage rail can exist at all: ⛔ an attribute whose component no translator covers can be applied
    /// and will never be republished — the <c>AX-015</c> failure, generalised.</para>
    /// </summary>
    public static IEnumerable<Type> WrittenComponents => All.Select(d => d.Component).Distinct();

    /// <summary>⭐ CE-4024 — the components the attribute path writes that are REPLICATED, so a translator must cover them: every
    /// <see cref="WrittenComponents"/> entry but a <see cref="AttributeDefinition.BrainLocal"/> one.</summary>
    public static IEnumerable<Type> ReplicatedComponents => All.Where(d => !d.BrainLocal).Select(d => d.Component).Distinct();
}

/// <summary>
/// ⭐⭐ One entity attribute, network-agnostically. ⛔ No descriptor ordinal — see
/// <see cref="AttributeVocabulary"/>.
/// </summary>
/// <param name="JsonPath">
/// ⭐⭐⭐ The authoring name, and an <b>EXTERNAL CONTRACT</b> — ExCon, the debug API and authoring JSON all
/// write it. ⛔ Renaming one is a breaking change; 📌 <c>Q59-N1</c> renamed the C# <i>constant</i> instead,
/// precisely because the constant's name is source-only while this is on the wire.
/// </param>
/// <param name="AttributeId">The binary wire id. ⭐ The VALUE is the contract; the constant's name is not.</param>
/// <param name="Kind">
/// ⚠ The <b>expected</b> kind — it selects the numeric width, ⛔ it does not override the JSON token's
/// category *(<c>AX-018</c>)*.
/// </param>
/// <param name="Component">⭐⭐ The ECS component written. This is the JOIN to the descriptor world.</param>
/// <param name="BrainLocal">⭐ CE-4024 — true when <see cref="Component"/> lives only on the node that runs the entity's brain and is never
/// replicated (<c>CivilianState</c>): the attribute is applied there, and no translator republishes it — by design, not by omission.</param>
public sealed record AttributeDefinition(
    string JsonPath,
    ushort AttributeId,
    AttributeValueKind Kind,
    Type Component,
    bool BrainLocal = false);
