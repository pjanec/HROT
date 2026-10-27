using System;
﻿using System.Linq;
using System.Text.Json;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// Builds the application-wide <see cref="JsonAttributeCompiler"/> singleton used by
/// <see cref="Systems.CreateEntityRequestSystem"/> and
/// <see cref="Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem"/>.
///
/// <para>
/// Registers the following JSON attribute paths:
/// <list type="bullet">
///   <item><c>"Name"</c> → <see cref="Fdp.Core.EntityInfo.Name"/> (ordinal: <c>dtEntityInfo</c>)</item>
///   <item><c>"Affiliation"</c> → <see cref="Fdp.Core.EntityInfo.ForceId"/> (ordinal: <c>dtEntityInfo</c>)</item>
///   <item><c>"Position.X"</c>, <c>"Position.Y"</c>, <c>"Position.Z"</c> (⭐ <c>CE-4073</c> — local ENU metres)
///         → <see cref="Fdp.Toolkit.Replication.Components.SimTransform.Position"/>, one axis each, a plain copy
///         (<see cref="SimTransformPosition.SetAxis"/>). ⭐ Registered on EVERY host — there is no geodetic frame to wait for.</item>
/// </list>
/// </para>
/// </summary>
public static class AttributeCompilerFactory
{

    // ── Public factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs an immutable <see cref="JsonAttributeCompiler"/> with the standard SimHost
    /// attribute routing table.
    /// </summary>
    /// <remarks>⭐ <c>CE-4073</c> — there is no <c>IGeographicTransform</c> parameter: the position route is local ENU metres, so it
    /// cannot be gated on one (it used to be, and a host with no terrain silently dropped position patches).</remarks>
    public static JsonAttributeCompiler Build()
    {
        var builder = new AttributeCompilerBuilder()

            // ── IgEntityData — unmanaged struct paths ───────────────────────────────────
            .RegisterValuePath<Fdp.Core.EntityInfo>(
                "Name",
                ( ref Fdp.Core.EntityInfo c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r ) =>
                    c.Name = r.GetString() ?? string.Empty,
                kind: KindOf("Name") )

            .RegisterValuePath<Fdp.Core.EntityInfo>(
                "Affiliation",
                ( ref Fdp.Core.EntityInfo c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r ) =>
                    c.ForceId = r.TokenType == JsonTokenType.Number
                        ? MapAffiliationInt( r.GetInt32())
                        : MapAffiliationString( r.GetString()),
                kind: KindOf("Affiliation") );

        // ── SimTransform — Position.X/Y/Z: local ENU metres, a plain copy per axis, on EVERY host (CE-4073) ──────────────────
        builder
            .RegisterValuePath<SimTransform>(
                "Position.X",
                (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => SimTransformPosition.SetAxis(ref st, 0, r.GetDouble()),
                kind: KindOf("Position.X"))
            .RegisterValuePath<SimTransform>(
                "Position.Y",
                (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => SimTransformPosition.SetAxis(ref st, 1, r.GetDouble()),
                kind: KindOf("Position.Y"))
            .RegisterValuePath<SimTransform>(
                "Position.Z",
                (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => SimTransformPosition.SetAxis(ref st, 2, r.GetDouble()),
                kind: KindOf("Position.Z"));

        // ── SimTransform — Heading (compass degrees, always registered) ───────
        builder.RegisterValuePath<SimTransform>(
            "Heading",
            (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            {
                // ⭐⭐ Q59-F5 — CALL the shared conversion; do not re-derive it. This used to inline
                //    `(90 - h) * π/180` + `CreateFromAxisAngle(UnitZ, …)`, which is numerically identical
                //    to the bridge and was therefore NOT a defect — ⛔ but it was the third copy of one
                //    formula, and F3 is what the third copy of a formula eventually becomes (the
                //    DescriptorMapper copy drifted to the wrong axis entirely).
                // 📌 AttributeIds.Heading's own doc already claimed "no new conversion math was
                //    written; the installer reuses the bridge" — true of the installer, false here until now.
                // ⭐ STEER P3 row 7 — a heading-only patch keeps the body's pitch and roll (never levels a banked aircraft)
                st.Rotation = Fdp.Modules.Geographic.Systems.SimTransformBridgeSystem
                    .WithHeadingDeg(st.Rotation, (float)r.GetDouble());
            },
            kind: KindOf("Heading"));

        // ── ScenarioClock — ⭐ CE-3216 S1-0 (W-8): a runtime edit sets a new start time, as sent; never re-anchored ───────────
        builder
            .RegisterValuePath<Fdp.Toolkit.Weather.ScenarioClock>(
                "ScenarioStartUtcTicks",
                (ref Fdp.Toolkit.Weather.ScenarioClock c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => c.StartUtcTicks = r.GetInt64(),
                kind: KindOf("ScenarioStartUtcTicks"))
            .RegisterValuePath<Fdp.Toolkit.Weather.ScenarioClock>(
                "ScenarioUtcOffsetMinutes",
                (ref Fdp.Toolkit.Weather.ScenarioClock c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => c.DisplayUtcOffsetMinutes = (short)r.GetInt32(),
                kind: KindOf("ScenarioUtcOffsetMinutes"));

        // ── CivilianState — ⭐ CE-4024 (AQ93 §15 K5): a fright, so a converted SUMO pedestrian is born in panic ───────────
        builder
            .RegisterValuePath<Fdp.Toolkit.Civilians.CivilianState>(
                "Fear",
                (ref Fdp.Toolkit.Civilians.CivilianState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => CivilianFright.SetFear(ref c, r.GetDouble()),
                kind: KindOf("Fear"))
            .RegisterValuePath<Fdp.Toolkit.Civilians.CivilianState>(
                "FrightFrom.X",
                (ref Fdp.Toolkit.Civilians.CivilianState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => CivilianFright.SetFrom(ref c, 0, r.GetDouble()),
                kind: KindOf("FrightFrom.X"))
            .RegisterValuePath<Fdp.Toolkit.Civilians.CivilianState>(
                "FrightFrom.Y",
                (ref Fdp.Toolkit.Civilians.CivilianState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => CivilianFright.SetFrom(ref c, 1, r.GetDouble()),
                kind: KindOf("FrightFrom.Y"))
            .RegisterValuePath<Fdp.Toolkit.Civilians.CivilianState>(
                "FrightFrom.Z",
                (ref Fdp.Toolkit.Civilians.CivilianState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) => CivilianFright.SetFrom(ref c, 2, r.GetDouble()),
                kind: KindOf("FrightFrom.Z"));

        return builder.Build();
    }

    /// <summary>
    /// Constructs an immutable <see cref="JsonToRecordCompiler"/> with the standard SimHost
    /// binary attribute routing schema.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐⭐ Registers the same paths as <see cref="Build"/> so that the JSON→ECS and JSON→Binary
    /// pipelines stay in perfect sync — 📄 the owning design's own words
    /// *(<c>docs/designs/attribs2/ATTR2-DESIGN.md</c> §3.2)*.</para>
    ///
    /// <para>🔴🔴 <b><c>AX-018</c> — that promise had FAILED, and nothing detected it.</b> 📐 Measured
    /// <c>2026-08-26</c>: <c>Heading</c> was added to <see cref="Build"/> and to
    /// <see cref="BuildBinaryInterpreter"/> *(Axis-B item ②)* and to <b>neither edge table</b>. ⇒ the
    /// interpreter stood ready to apply <c>Heading</c> that no edge table could ever emit, so a heading
    /// crossing the JSON→binary route emitted <b>zero records</b> — ⛔ silently: no exception, no log, the
    /// rotation simply never left the edge.</para>
    ///
    /// <para>⭐⭐ <b>This is now the SOLE edge table.</b> ⚠ <c>IgApplication</c> used to hand-copy these
    /// registrations with a comment saying they must stay in sync — ⛔ ruling 9, and the comment WAS the
    /// enforcement. ⭐ Railed by <c>TheFourRoutingTablesAgreeTests</c>: the vocabulary is pinned against
    /// <see cref="Build"/>'s own <c>RegisteredPaths</c>, and a second builder anywhere in the tree is a
    /// source-scan red.</para>
    /// </remarks>
    public static JsonToRecordCompiler BuildEdgeCompiler()
    {
        // ⭐⭐⭐ Q59-A1′ — DERIVED, not spelled out. This table used to re-list every path by hand, which is
        //    how AX-018's `Heading` row came to be missing from it while Build() and BuildBinaryInterpreter()
        //    both had one — silently, for months.
        // ⇒ ⭐⭐ a row can no longer be missing HERE by construction; the only place to add an attribute is
        //    AttributeVocabulary.All.
        // ⚠ Every definition is registered: the edge only TRANSCRIBES JSON into records.
        var builder = new JsonToRecordCompilerBuilder();

        foreach (var d in AttributeVocabulary.All)
            builder.Register(d.JsonPath, d.AttributeId, d.Kind);

        return builder.Build();
    }

    /// <summary>
    /// Constructs a <see cref="BinaryInterpreter"/> configured with the standard SimHost
    /// domain installers.
    /// </summary>
    /// <remarks>⭐ <c>CE-4073</c> — no <c>IGeographicTransform</c> parameter: <see cref="SimTransformAttributeInstaller"/> copies local
    /// ENU metres, so it is added on every host (it used to be added only when a transform was passed).</remarks>
    public static BinaryInterpreter<EntityAttributeChange> BuildBinaryInterpreter()
    {
        var builder = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
            .AddInstaller(new EntityDataAttributeInstaller())
            // ⭐⭐⭐ CE-4073 — the position (local ENU metres, a copy per axis), UNCONDITIONALLY. ⛔ It used to be added only
            //    `if (geoTransform != null)` — the gate that made a host with no terrain drop position patches silently.
            .AddInstaller(new SimTransformAttributeInstaller())
            // ⭐⭐ Axis-B item ② — heading, also unconditional: a compass heading is already in the units
            //    SimTransformBridgeSystem.HeadingDegToRotation takes.
            .AddInstaller(new SimTransformHeadingInstaller())
            .AddInstaller(new ScenarioClockAttributeInstaller())    // ⭐ CE-3216 S1-0 — the scenario start time (no geo transform needed)
            .AddInstaller(new CivilianFrightAttributeInstaller());  // ⭐ CE-4024 — a civilian's fright (AQ93 §15 K5)

        return builder.Build();
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <c>Q59-A1′</c> — the declared kind for a path, so the JSON routing table and the edge table read
    /// the SAME value. ⛔ Throws rather than defaulting: a path registered here but absent from
    /// <see cref="AttributeVocabulary"/> is a half-registration, which is exactly what this design ends.
    /// </summary>
    private static AttributeValueKind KindOf(string jsonPath)
        => AttributeVocabulary.All.SingleOrDefault(d => d.JsonPath == jsonPath)?.Kind
           ?? throw new InvalidOperationException(
               $"JSON path '{jsonPath}' is registered in Build() but not declared in " +
               "AttributeVocabulary.All. Add the definition there first — it is the single declaration.");


    /// <summary>
    /// Maps a JSON affiliation string (e.g. <c>"FORCE_FRIENDLY"</c>) to a <see cref="ForceId"/>.
    /// Unrecognised values map to <see cref="ForceId.Neutral"/>.
    /// </summary>
    internal static ForceId MapAffiliationString(string? value) =>
        value switch
        {
            "FORCE_FRIENDLY" => ForceId.Friend,
            "FORCE_OPPOSING" => ForceId.Hostile,
            "FORCE_NEUTRAL"  => ForceId.Neutral,
            _                => ForceId.Neutral,
        };

    /// <summary>
    /// Maps a JSON affiliation integer (the raw <see cref="ForceIdentifier"/> ordinal) to a
    /// <see cref="ForceId"/>. Handles the ExCon default JSON serialisation which emits enums
    /// as their underlying integer value (e.g. <c>2</c> for <c>FORCE_OPPOSING</c>).
    /// </summary>
    internal static ForceId MapAffiliationInt(int value) =>
        (ForceIdentifier)value switch
        {
            ForceIdentifier.Friendly => ForceId.Friend,
            ForceIdentifier.Opposing => ForceId.Hostile,
            ForceIdentifier.Neutral  => ForceId.Neutral,
            _                        => ForceId.Neutral,
        };
}
