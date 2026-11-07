using System;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Well-known attribute ID constants for the ATTR2 binary wire schema.
/// Each constant maps a human-readable attribute name to the <c>ushort</c> ID
/// transmitted in <c>AttributeRecord.AttributeId</c> over DDS.
///
/// <para><b>Reserved Numeric Range Strategy:</b></para>
/// <list type="table">
///   <listheader><term>Range</term><description>Domain</description></listheader>
///   <item><term>1 – 99</term><description>Core entity data (identity, affiliation, status).</description></item>
///   <item><term>100 – 199</term><description>Geo-spatial / positional attributes (WGS-84 geodetic, orientation).</description></item>
///   <item><term>200 – 499</term><description>Domain-specific core extensions (weapons, sensors, comms).</description></item>
///   <item><term>500+</term><description>Application/project domain extensions — add in companion files (see below).</description></item>
/// </list>
///
/// <para><b>Extension Pattern:</b><br/>
/// Domain projects that need additional IDs should declare a companion static class in their
/// own assembly rather than modifying this file. Example:
/// <code>
/// // In MyDomain project:
/// namespace MyDomain.DdsSchema;
///
/// // Extends the schema without touching FDP core.
/// public static class MyDomainAttributeIds
/// {
///     public const ushort WeaponAmmo = 500;
///     public const ushort RadioFrequency = 501;
/// }
/// </code>
/// </para>
/// </summary>
public static class AttributeIds
{
    // ── Range 1–99: Core entity data ──────────────────────────────────────────────

    /// <summary>
    /// Display name of the entity (<c>IgEntityData.Name</c>).
    /// Value type: <c>AttributeValueType.String</c>.
    /// </summary>
    public const ushort Name = 1;

    /// <summary>
    /// Force/side affiliation (<c>IgEntityData.ForceId</c>).
    /// Value type: <c>AttributeValueType.Int32</c> (maps to the ForceId enum).
    /// </summary>
    public const ushort Affiliation = 2;

    // ── Geo-spatial / positional ──────────────────────────────────────────────────

    /// <summary>
    /// ⛔ RESERVED — was <c>GeoLat</c> (WGS-84 latitude, degrees), retired by <c>CE-4073</c> (AQ107 B's tail, R-4092): every position the
    /// attribute route carries is now LOCAL ENU metres (<see cref="PositionX"/>/<see cref="PositionY"/>/<see cref="PositionZ"/>), the
    /// frame the NED wire already speaks. ⚠ Do NOT reuse 10 — a stale sender would be read as the wrong quantity.
    /// </summary>
    public const ushort RetiredGeoLat = 10;

    /// <summary>⛔ RESERVED — was <c>GeoLon</c> (WGS-84 longitude, degrees), retired by <c>CE-4073</c>. ⚠ Do NOT reuse 11.</summary>
    public const ushort RetiredGeoLon = 11;

    /// <summary>⛔ RESERVED — was <c>GeoAlt</c> (WGS-84 altitude, metres), retired by <c>CE-4073</c>. ⚠ Do NOT reuse 12.</summary>
    public const ushort RetiredGeoAlt = 12;

    /// <summary>
    /// ⭐⭐ <c>CE-4073</c> — local ENU <b>east</b> metres: <c>SimTransform.Position.X</c>. Value type: <c>Float64</c>.
    /// The three axes are independent plain copies — a patch carrying only <c>Position.X</c> moves the entity east and leaves its
    /// north and altitude alone — and they exist on EVERY host (nothing to convert with, so nothing to gate on).
    /// </summary>
    public const ushort PositionX = 14;

    /// <summary>⭐⭐ <c>CE-4073</c> — local ENU <b>north</b> metres: <c>SimTransform.Position.Y</c>. <c>Float64</c>. See <see cref="PositionX"/>.</summary>
    public const ushort PositionY = 15;

    /// <summary>⭐⭐ <c>CE-4073</c> — local ENU <b>up</b> metres: <c>SimTransform.Position.Z</c>. <c>Float64</c>. See <see cref="PositionX"/>.</summary>
    public const ushort PositionZ = 16;

    /// <summary>
    /// ⭐⭐⭐ <b>Compass heading in degrees — <c>0 = North, 90 = East, clockwise</b></c> — applied to
    /// <c>SimTransform.Rotation</c>. Value type: <c>AttributeValueType.Float64</c>.
    ///
    /// <para>⭐⭐ <b>This constant is the ONLY new thing heading needed.</b> 📐 Measured
    /// <c>2026-08-25</c>: the convention, the conversion and the wire field all already exist —
    /// <c>SimTransformBridgeSystem.HeadingDegToRotation</c> / <c>RotationToHeadingDeg</c>
    /// *(documented with this exact convention)*, <c>EulerOri.Heading</c>, and
    /// <c>GeoSpatialEgressTranslator</c>'s yaw→compass step. ⇒ ⛔ **no new conversion math was written**;
    /// the installer reuses the bridge. 📄 <c>DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §6 ②.</para>
    ///
    /// <para>⚠⚠ <b>That claim was true of the INSTALLER and false of two other callers — corrected by
    /// <c>Q59-C1</c>/<c>F5</c>.</b> 📐 Measured <c>2026-08-26</c>: the JSON <c>"Heading"</c> handler INLINED
    /// the same formula *(harmless — numerically identical)*, and <c>DescriptorMapper</c> had drifted to
    /// <b>a yaw about Y with no compass offset</b>, which disagreed at every heading and pointed straight UP
    /// at <c>h=90</c>. ⭐ All three now CALL <c>HeadingDegToRotation</c>.</para>
    ///
    /// <para>⭐⭐ <b>RENAMED <c>GeoHeading</c> → <c>Heading</c> by <c>Q59-N1</c>.</b> 📐 The old name
    /// advertised a JSON path that does not exist: the route is <c>"Heading"</c>, and
    /// <c>{"Position":{"Heading":…}}</c> applies <b>nothing, silently</b>. ⚠ The rename is
    /// <b>source-only</b> — the wire carries the <c>ushort</c> <c>13</c> and no <c>.idl</c> names the
    /// constant — whereas renaming the PATH would be a breaking external contract change. 📄 <c>Q59</c> §8.2.</para>
    ///
    /// <para>⚠ <b>Degrees, not radians, and compass, not math-yaw</b> — deliberately the same units the
    /// wire and the DebugApi already use *(<c>headingDeg</c>)*, so nothing on the path has to convert
    /// twice. ⛔ A radians-or-math-yaw id would be a second convention for one concept.</para>
    ///
    /// <para>⚠ <b>Numbering:</b> the class doc reserves 100–199 for geo-spatial, but the shipped
    /// position ids sit at 10–16 — a pre-existing doc/value mismatch. ⭐ This follows the VALUES and
    /// takes <c>13</c>, keeping the family contiguous; ⛔ these ids are a WIRE schema, so renumbering
    /// them to match the prose is not a free edit and is not attempted. <c>CE-4073</c> retired 10–12
    /// and added <see cref="PositionX"/>–<see cref="PositionZ"/> at 14–16.</para>
    /// </summary>
    public const ushort Heading = 13;

    // ── CE-3216 S1-0 — the scenario clock (R-3012, W-8; docs/designs/weather/DESIGN_Weather_And_Time.md) ─────────────────
    // ⚠ In the 1–99 "core" range, beside the entity data: a scenario-level attribute of the World Environment, not a domain extension.

    /// <summary>
    /// ⭐ The scenario START TIME — <c>ScenarioClock.StartUtcTicks</c> (UTC ticks at sim time 0). Value type: <c>Int64</c>. A runtime
    /// edit simply sets a new start time, applied as sent by the clock's owner (W-8).
    /// </summary>
    public const ushort ScenarioStartUtcTicks = 20;

    /// <summary>⭐ The clock's display offset from UTC in minutes — <c>ScenarioClock.DisplayUtcOffsetMinutes</c>. Value type: <c>Int32</c>.</summary>
    public const ushort ScenarioUtcOffsetMinutes = 21;

    // ── CE-4024 — a civilian's fright (AQ93 §15 K5, CV-12; docs/DESIGN_Civilians_And_Crowd_Control.md) ──────────────────────
    // ⭐ How a SUMO pedestrian converted on the traffic node reaches the brain node already in panic: CivilianState is brain-local
    //   and never replicated, so the creation request carries it as attributes. Also an operator's way to frighten a civilian.

    /// <summary>⭐ <c>CivilianState.Fear</c>, 0 (calm) … 1 (terrified). Value type: <c>Float64</c>.</summary>
    public const ushort CivilianFear = 30;

    /// <summary>⭐ <c>CivilianState.FrightFrom.X</c> — where the fright came from (world metres); sets <c>HasFrightFrom</c>. <c>Float64</c>.</summary>
    public const ushort CivilianFrightFromX = 31;

    /// <summary>⭐ <c>CivilianState.FrightFrom.Y</c>. See <see cref="CivilianFrightFromX"/>.</summary>
    public const ushort CivilianFrightFromY = 32;

    /// <summary>⭐ <c>CivilianState.FrightFrom.Z</c>. See <see cref="CivilianFrightFromX"/>.</summary>
    public const ushort CivilianFrightFromZ = 33;

    // ── rules V-4 (docs/DESIGN_Scenario_Rules.md §00.10) — runtime control of a rule entity (an instructor / an agent: fire · disarm · rearm · reset · activate · deactivate) ──
    // ⭐ Brain-local, never replicated: the request is APPLIED on the node that owns the rule entity (a write elsewhere becomes the router's change-request).

    /// <summary>⭐ <c>RuleControl.Request</c> — a <c>RuleRequest</c> value. Value type: <c>Int32</c>.</summary>
    public const ushort RuleRequest = 40;

    /// <summary>⭐ <c>RuleControl.Seq</c> — the request's sequence number; the owner acts ONCE per new value. <c>Int32</c>.</summary>
    public const ushort RuleSeq = 41;

    /// <summary>⭐ <c>RuleControl.Origin</c> — who asked (<c>RuleOrigin</c>: 1 debug API, 2 instructor), for the journal. <c>Int32</c>.</summary>
    public const ushort RuleOrigin = 42;

    // ── rules ② (docs/DESIGN_Scenario_Rules.md §00.11a) — a LIVE EDIT of a rule's params that keeps its state: RuleEdit (433), a managed record the handlers REPLACE whole ──

    /// <summary>⭐ <c>RuleEdit.Json</c> — the rule's new params (a JSON object, the same shape a load takes). <c>String</c>.</summary>
    public const ushort RuleParams = 43;

    /// <summary>⭐ <c>RuleEdit.Seq</c> — the edit's sequence number; the owner applies ONCE per new value. <c>Int32</c>.</summary>
    public const ushort RuleEditSeq = 44;

    /// <summary>⭐ <c>RuleEdit.Origin</c> — who asked (<c>BehaviorOrigin</c> value), for the journal. <c>Int32</c>.</summary>
    public const ushort RuleEditOrigin = 45;

    // ── B-4 / Details forms D2 (docs/UX/DESIGN_Details_Forms.md §4.1) — the fields a form writes. Next free ids after the rules block; 46–49 stay unallocated. ──

    /// <summary>⭐ <c>Health.Current</c> (clamped to [0, <c>Max</c>]). <c>Float64</c>.</summary>
    public const ushort HealthCurrent = 50;

    /// <summary>⭐ <c>Playable.Side</c> — a force token, the shapes <see cref="Affiliation"/> takes. <c>String</c> (a number is accepted).</summary>
    public const ushort PlayableSide = 51;

    /// <summary>⭐ <c>Playable.SlotName</c>. <c>String</c>.</summary>
    public const ushort PlayableSlotName = 52;

    /// <summary>⭐ <c>SpawnArea.Side</c> — a force token, as <see cref="Affiliation"/>. <c>String</c> (a number is accepted).</summary>
    public const ushort SpawnAreaSide = 53;

    /// <summary>⭐ <c>SpawnArea.FacingDeg</c>. <c>Float64</c>.</summary>
    public const ushort SpawnAreaFacingDeg = 54;

    /// <summary>⭐ <c>Ammo.&lt;mount&gt;</c> — that mount's <c>WeaponState.Ammo</c> (clamped to its <c>MaxAmmo</c>); the MOUNT index travels as <c>SubIndex1</c>
    /// (<c>{"Ammo":{"2":5}}</c>): mount 0 is the unit itself, mounts 1–3 its mount children. <c>Int32</c>.</summary>
    public const ushort Ammo = 55;

    /// <summary>⭐ <c>StanceIntent.TargetStance</c> — a <c>StanceId</c> (0 Standing · 1 Crouched · 2 Prone) as a number or its name. <c>Int32</c>.</summary>
    public const ushort Stance = 56;

    /// <summary>⭐ <c>WeatherConditions.GroundTemperatureC</c>. <c>Float64</c>.</summary>
    public const ushort WeatherGroundTemperatureC = 57;

    /// <summary>⭐ <c>WeatherConditions.LapseRateKPerKm</c>. <c>Float64</c>.</summary>
    public const ushort WeatherLapseRateKPerKm = 58;

    /// <summary>⭐ <c>WeatherConditions.SeaLevelPressureHpa</c>. <c>Float64</c>.</summary>
    public const ushort WeatherSeaLevelPressureHpa = 59;

    /// <summary>⭐ <c>WeatherConditions.RelativeHumidity</c> (0..1). <c>Float64</c>.</summary>
    public const ushort WeatherRelativeHumidity = 60;

    /// <summary>⭐ <c>WeatherConditions.VisibilityM</c>. <c>Float64</c>.</summary>
    public const ushort WeatherVisibilityM = 61;

    /// <summary>⭐ <c>WeatherConditions.FogTopM</c>. <c>Float64</c>.</summary>
    public const ushort WeatherFogTopM = 62;

    /// <summary>⭐ <c>WeatherConditions.FogVisibilityM</c>. <c>Float64</c>.</summary>
    public const ushort WeatherFogVisibilityM = 63;

    /// <summary>⭐ <c>WeatherConditions.Precipitation</c> (a <c>PrecipitationKind</c>, 0..5). <c>Int32</c>.</summary>
    public const ushort WeatherPrecipitation = 64;

    /// <summary>⭐ <c>WeatherConditions.PrecipitationMmPerHour</c>. <c>Float64</c>.</summary>
    public const ushort WeatherPrecipitationMmPerHour = 65;

    /// <summary>⭐ <c>WeatherConditions.FeatherM</c> (the blend distance at a region's edge). <c>Float64</c>.</summary>
    public const ushort WeatherFeatherM = 66;

    // ── ⭐ Initial state S2 (docs/UX/DESIGN_Initial_State.md §3, R-4137) — the weapon's start state, on WeaponPosture (applied by the brain's node) ──

    /// <summary>⭐ <c>WeaponPosture.Kind</c> — a <c>WeaponPostureKind</c> (0 AtEase · 1 Ready · 2 Aimed) as a number or its name. <c>Int32</c>.</summary>
    public const ushort WeaponPosture = 70;

    /// <summary>⭐ <c>WeaponPosture.Point</c>, one axis per record — the axis (0 x · 1 y · 2 z) travels as <c>SubIndex1</c> (<c>{"Weapon.AimPoint":{"0":12.5}}</c>);
    /// aims at a point, not a unit. World metres. <c>Float64</c>.</summary>
    public const ushort WeaponAimPoint = 71;

    /// <summary>⭐ <c>WeaponPosture.Target</c> — the target unit's NETWORK id (0 clears); aims at it, not at a point. Resolved on the applying node (binary only). <c>Int64</c>.</summary>
    public const ushort WeaponAimTarget = 72;

    /// <summary>⭐ <c>WeaponPosture.HeldItem</c> — a <c>HeldItemKind</c> (0 None · 1 Weapon · 2 Throwable · 3 Phone · 4 Binoculars) as a number or its name; a thing he does not
    /// have is refused (<c>HeldItems.Has</c>). <c>Int32</c>.</summary>
    public const ushort WeaponHeldItem = 73;
}
