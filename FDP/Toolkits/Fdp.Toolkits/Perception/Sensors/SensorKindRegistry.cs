using System;
using System.Collections.Generic;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B0 (R-213) — the SHAPE of a sensor's answer. Every sensor shares one FORM (child entity, kind,
    /// settings, lifecycle, solver routing, <see cref="UnitSensors.Of"/>); the RESULT type differs per family, and a reader of
    /// one family never reads another's result as its own (no narrowing cast). 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.10.
    /// </summary>
    public enum SensorResultFamily : byte
    {
        /// <summary>A scored, ranked list of entities / points — <see cref="EqsCognitiveBuffer"/> (perception, EQS queries).</summary>
        Ranked = 0,
        /// <summary>Areas along a route — <see cref="DangerAreaCognitiveBuffer"/> (the danger-area sensor).</summary>
        Area = 1,
    }

    /// <summary>⭐ <c>CE-3072</c> B0 — what reading a unit's sensor of a kind found. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.4.</summary>
    public enum SensorReadStatus : byte
    {
        /// <summary>An answer is there.</summary>
        Ok = 0,
        /// <summary>The unit has no sensor of the kind.</summary>
        NoSensor = 1,
        /// <summary>It has one, with no answer yet.</summary>
        NoAnswerYet = 2,
        /// <summary>
        /// ⛔ It has one, but of ANOTHER result family than the reader expects (a ranked reader pointed at a danger-area
        /// sensor). A reader must FAIL loudly on this — never wait for ever (R-133).
        /// </summary>
        WrongFamily = 3,
    }

    /// <summary>⭐ <c>CE-3072</c> B0 (Q3, behaviors lane) — how a <c>When SensorResult(kind)</c> trigger is decided.</summary>
    public enum SensorTriggerShape : byte
    {
        /// <summary>From the answer's header only (ready, stamp, age) — every family.</summary>
        Header = 0,
        /// <summary>Entry 0's <see cref="SensorTrigger.ElementField"/> changed since the last answer.</summary>
        FieldChanged = 1,
        /// <summary>Entry 0's <see cref="SensorTrigger.ElementField"/> crossed the node's threshold (either way).</summary>
        FieldCrossed = 2,
    }

    /// <summary>⭐ <c>CE-3072</c> B0 (Q3) — one trigger of a kind, as DATA, so N4 lowers every family the same way.</summary>
    /// <param name="Name">What the node offers (<c>"ThreatCrossed"</c>).</param>
    /// <param name="ElementField">The element field it watches (<c>"ThreatRating"</c>); null for a header trigger.</param>
    /// <param name="Shape">How it is decided.</param>
    public sealed record SensorTrigger(string Name, string? ElementField, SensorTriggerShape Shape);

    /// <summary>
    /// ⭐ <c>CE-3072</c> B0 — one sensor kind's types: what configures it, what it answers, and the triggers on it.
    /// <para>⭐ (Q1) Every <see cref="ResultComponent"/> exposes the same five members — <c>int Count</c>, <c>bool IsReady</c>,
    /// <c>uint LastUpdateTick</c>, <c>float LastUpdateTimeSeconds</c> and <c>ReadOnlySpan&lt;ElementType&gt; GetSpanRO()</c>; the
    /// generated blueprint read (N2) and the header triggers (<c>BecomesStale</c> reads <c>LastUpdateTimeSeconds</c> — Q4, behaviors
    /// lane H6) use exactly these. A new family's result component must too.</para>
    /// </summary>
    /// <param name="Kind">The kind (<c>(SensorModality)0</c> = an EQS query sensor, found by template rather than kind).</param>
    /// <param name="Family">Its result family.</param>
    /// <param name="SettingsType">What configures it — the in-pins of a blueprint <c>SpawnSensor(kind)</c> (N3).</param>
    /// <param name="ResultComponent">The component holding its answer, on the sensor child.</param>
    /// <param name="ElementType">One entry of the answer — the out-pins of <c>ReadSensorResult(kind, i)</c> (N2).</param>
    /// <param name="Triggers">The <c>When SensorResult(kind)</c> triggers (N4): the header ones every family has, then its own.</param>
    /// <param name="EnsureMethod">(Q2) The FQN of a static <c>Entity Ensure(EntityRepository, Entity owner, int site, in TSettings,
    /// long key)</c> that a behaviour calls to create its own sensor of this kind (N3 lowers <c>SpawnSensor(kind)</c> to it);
    /// null when a behaviour does not create one this way (a TKB perception kind; an EQS query, spawned by template).</param>
    public sealed record SensorKindInfo(
        SensorModality Kind,
        SensorResultFamily Family,
        Type SettingsType,
        Type ResultComponent,
        Type ElementType,
        IReadOnlyList<SensorTrigger> Triggers,
        string? EnsureMethod);

    /// <summary>
    /// ⭐⭐ <c>CE-3072</c> B0 / <c>CE-3078</c> N1 (R-213) — sensor kind → its types. The editor reflects it and the blueprint
    /// compiler bakes from it (as <c>GetComponent</c> does for a component's fields), so the sensor nodes never name a result
    /// type: a new kind is a registry entry and its types, never a new node. 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.10.
    /// <para>⚠ The ranked settings type is <see cref="EqsSensor"/>; which of its fields a blueprint shows as pins is the
    /// node's choice (today's <c>SpawnEqsSensor</c> pins), not this table's.</para>
    /// </summary>
    public static class SensorKindRegistry
    {
        /// <summary>The kind of an EQS query sensor — it carries no <see cref="SensorModality"/> (found by template).</summary>
        public const SensorModality EqsQuery = 0;

        /// <summary>Header triggers every family has.</summary>
        public static readonly IReadOnlyList<SensorTrigger> HeaderTriggers = new[]
        {
            new SensorTrigger("FirstReady", null, SensorTriggerShape.Header),
            new SensorTrigger("Changed", null, SensorTriggerShape.Header),
            new SensorTrigger("BecomesStale", null, SensorTriggerShape.Header),
        };

        private static readonly Dictionary<SensorModality, SensorKindInfo> _kinds = new();
        private static readonly object _lock = new();

        static SensorKindRegistry()
        {
            var ranked = new List<SensorTrigger>(HeaderTriggers)
            {
                new("TopChanged", nameof(EqsResult.EntityId), SensorTriggerShape.FieldChanged),
                new("ScoreCrossed", nameof(EqsResult.Score), SensorTriggerShape.FieldCrossed),
            };
            foreach (var k in new[] { EqsQuery, SensorModality.Visual, SensorModality.Radar, SensorModality.Thermal, SensorModality.Acoustic })
                Register(new SensorKindInfo(k, SensorResultFamily.Ranked, typeof(EqsSensor), typeof(EqsCognitiveBuffer), typeof(EqsResult), ranked, null));
            var area = new List<SensorTrigger>(HeaderTriggers)
            {
                new("NextAreaChanged", nameof(DangerAreaDescriptor.FeatureId), SensorTriggerShape.FieldChanged),
                new("ThreatCrossed", nameof(DangerAreaDescriptor.ThreatRating), SensorTriggerShape.FieldCrossed),
            };
            Register(new SensorKindInfo(SensorModality.DangerArea, SensorResultFamily.Area, typeof(DangerAreaSettings),
                typeof(DangerAreaCognitiveBuffer), typeof(DangerAreaDescriptor), area,
                "Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure"));
        }

        /// <summary>Add or replace a kind (a toolkit adding its own sensor kind).</summary>
        public static void Register(SensorKindInfo info)
        {
            lock (_lock) _kinds[info.Kind] = info;
        }

        /// <summary>The kind's types, if it is registered.</summary>
        public static bool TryGet(SensorModality kind, out SensorKindInfo info)
        {
            lock (_lock) return _kinds.TryGetValue(kind, out info!);
        }

        /// <summary>The kind's result family — <see cref="SensorResultFamily.Ranked"/> for an unregistered kind (today's default).</summary>
        public static SensorResultFamily FamilyOf(SensorModality kind) => TryGet(kind, out var i) ? i.Family : SensorResultFamily.Ranked;

        /// <summary>Every registered kind, ordered by kind.</summary>
        public static IReadOnlyList<SensorKindInfo> All
        {
            get
            {
                lock (_lock)
                {
                    var list = new List<SensorKindInfo>(_kinds.Values);
                    list.Sort((a, b) => a.Kind.CompareTo(b.Kind));
                    return list;
                }
            }
        }
    }
}
