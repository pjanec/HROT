using Fdp.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-018</c> — THE ATTRIBUTE VOCABULARY IS DECLARED IN FOUR PLACES, AND THEY DISAGREED.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §17 · owning design
/// <c>docs/designs/attribs2/ATTR2-DESIGN.md</c> §3.2, whose stated intent is that the JSON→ECS and
/// JSON→binary schemas *"stay in perfect sync"*.</para>
///
/// <para>🔴🔴 <b>Measured <c>2026-08-26</c> — four tables, not the two I first reported:</b></para>
///
/// <list type="number">
///   <item>⭐ <c>AttributeCompilerFactory.Build()</c> — JSON path → ECS setter *(**has** <c>Heading</c>)*</item>
///   <item>⭐ <c>AttributeCompilerFactory.BuildEdgeCompiler()</c> — JSON path → record *(⛔ **no**
///   <c>Heading</c>; ⚠ **test-only caller**)*</item>
///   <item>🔴 <c>IgApplication._edgeCompiler</c> — the **PRODUCTION** JSON→record table, **hand-copied**
///   from ② with a comment saying it must stay in sync *(⛔ ruling 9: a duplicate implementation kept in
///   step by a comment — the same disease as the <c>eForceIdentifier</c> triple)*</item>
///   <item>⭐ <c>AttributeCompilerFactory.BuildBinaryInterpreter()</c> — record → ECS *(**has**
///   <c>Heading</c>)*</item>
/// </list>
///
/// <para>⇒ 🔴 <b>the binary interpreter can APPLY a heading that no edge table can ever EMIT</b>, so a
/// heading sent through the JSON→binary route is silently dropped. ⭐ And <c>Affiliation</c> is declared
/// <c>CsString</c> at the edge while the ECS arm deliberately accepts a NUMBER too
/// *(<c>MapAffiliationInt</c> exists precisely because ExCon serialises the enum as <c>2</c>)*.</para>
///
/// <para>⚠ <b>None of this is a network-separation question.</b> 📐 All four tables now live in
/// <c>Fdp.Toolkits</c> after <c>AX-017</c>; the disagreement is entirely FDP-internal. ⇒ ⭐ fixing it needs
/// no DDS type and moves nothing across the boundary — <c>R-134</c> is untouched.</para>
/// </summary>
public class TheFourRoutingTablesAgreeTests
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE DECLARED VOCABULARY — every logical attribute, and the JSON that expresses it.</b>
    ///
    /// <para>⛔ Adding a row here is a design act. ⭐ <see cref="TheDeclaredVocabularyCoversEveryJsonRoute"/>
    /// pins it against the JSON compiler's own <c>RegisteredPaths</c>, so it cannot go stale silently.</para>
    /// </summary>
    public static readonly (string Path, ushort Id, string Json)[] Vocabulary =
    {
        ("Name",                  AttributeIds.Name,       "{\"Name\":\"Charlie\"}"),
        ("Affiliation",           AttributeIds.Affiliation, "{\"Affiliation\":\"FORCE_OPPOSING\"}"),
        ("Position.X",             AttributeIds.PositionX,  "{\"Position\":{\"X\":32.0}}"),
        ("Position.Y",             AttributeIds.PositionY,  "{\"Position\":{\"Y\":34.0}}"),
        ("Position.Z",             AttributeIds.PositionZ,  "{\"Position\":{\"Z\":100.0}}"),
        ("Heading",               AttributeIds.Heading, "{\"Heading\":90.0}"),
        // ⭐ CE-3216 S1-0 (W-8) — the World Environment's clock: a new scenario start time and its display offset
        ("ScenarioStartUtcTicks",    AttributeIds.ScenarioStartUtcTicks,    "{\"ScenarioStartUtcTicks\":638450000000000000}"),
        ("ScenarioUtcOffsetMinutes", AttributeIds.ScenarioUtcOffsetMinutes, "{\"ScenarioUtcOffsetMinutes\":120}"),
        // ⭐ CE-4024 (AQ93 §15 K5) — a civilian's fright: a converted SUMO pedestrian is born in panic
        ("Fear",         AttributeIds.CivilianFear,        "{\"Fear\":1.0}"),
        ("FrightFrom.X", AttributeIds.CivilianFrightFromX, "{\"FrightFrom\":{\"X\":10.0}}"),
        ("FrightFrom.Y", AttributeIds.CivilianFrightFromY, "{\"FrightFrom\":{\"Y\":20.0}}"),
        ("FrightFrom.Z", AttributeIds.CivilianFrightFromZ, "{\"FrightFrom\":{\"Z\":1.0}}"),
        // ⭐ rules V-4 (§00.10) — a rule entity's runtime control
        ("Rule.Request", AttributeIds.RuleRequest,         "{\"Rule\":{\"Request\":2}}"),
        ("Rule.Seq",     AttributeIds.RuleSeq,             "{\"Rule\":{\"Seq\":1}}"),
        ("Rule.Origin",  AttributeIds.RuleOrigin,          "{\"Rule\":{\"Origin\":1}}"),
        // ⭐ rules ② (§00.11a) — a live edit of a rule's params: the managed RuleEdit, replaced whole by each path
        ("Rule.Params",     AttributeIds.RuleParams,       "{\"Rule\":{\"Params\":\"{}\"}}"),
        ("Rule.EditSeq",    AttributeIds.RuleEditSeq,      "{\"Rule\":{\"EditSeq\":1}}"),
        ("Rule.EditOrigin", AttributeIds.RuleEditOrigin,   "{\"Rule\":{\"EditOrigin\":3}}"),
        // ⭐ B-4 / Details forms D2 — the fields a form writes
        ("Health.Current",      AttributeIds.HealthCurrent,      "{\"Health\":{\"Current\":50.0}}"),
        ("Playable.Side",      AttributeIds.PlayableSide,       "{\"Playable\":{\"Side\":\"FORCE_OPPOSING\"}}"),
        ("Playable.SlotName",  AttributeIds.PlayableSlotName,   "{\"Playable\":{\"SlotName\":\"Alpha\"}}"),
        ("SpawnArea.Side",     AttributeIds.SpawnAreaSide,      "{\"SpawnArea\":{\"Side\":\"FORCE_FRIENDLY\"}}"),
        ("SpawnArea.FacingDeg",AttributeIds.SpawnAreaFacingDeg, "{\"SpawnArea\":{\"FacingDeg\":90.0}}"),
        ("Ammo.*",             AttributeIds.Ammo,               "{\"Ammo\":{\"2\":5}}"),
        ("Stance",             AttributeIds.Stance,             "{\"Stance\":1}"),
        // ⭐ Initial state S2 — the weapon's start state
        ("Weapon.Posture",     AttributeIds.WeaponPosture,      "{\"Weapon\":{\"Posture\":2}}"),
        ("Weapon.AimPoint.*",  AttributeIds.WeaponAimPoint,     "{\"Weapon\":{\"AimPoint\":{\"0\":12.5}}}"),
        ("Weapon.AimTarget",   AttributeIds.WeaponAimTarget,    "{\"Weapon\":{\"AimTarget\":1}}"),
        ("Weapon.HeldItem",    AttributeIds.WeaponHeldItem,     "{\"Weapon\":{\"HeldItem\":1}}"),
        // ⭐ Initial state S5 — the damage per part and the mobility switch
        ("Damage.Part.*",       AttributeIds.DamagePart,         "{\"Damage\":{\"Part\":{\"3\":128}}}"),
        ("Damage.MobilityKill", AttributeIds.DamageMobilityKill, "{\"Damage\":{\"MobilityKill\":1}}"),
        // ⭐ Initial state S6 — the AI character settings
        ("Proficiency",                AttributeIds.Proficiency,                "{\"Proficiency\":0.8}"),
        ("Temperament.RiskAcceptance", AttributeIds.TemperamentRiskAcceptance, "{\"Temperament\":{\"RiskAcceptance\":0.7}}"),
        ("Temperament.Obedience",      AttributeIds.TemperamentObedience,      "{\"Temperament\":{\"Obedience\":0.6}}"),
        ("Temperament.Nerve",          AttributeIds.TemperamentNerve,          "{\"Temperament\":{\"Nerve\":0.5}}"),
        ("Temperament.Resolve",        AttributeIds.TemperamentResolve,        "{\"Temperament\":{\"Resolve\":0.4}}"),
        ("Weather.GroundTemperatureC",     AttributeIds.WeatherGroundTemperatureC,     "{\"Weather\":{\"GroundTemperatureC\":21.5}}"),
        ("Weather.LapseRateKPerKm",        AttributeIds.WeatherLapseRateKPerKm,        "{\"Weather\":{\"LapseRateKPerKm\":6.0}}"),
        ("Weather.SeaLevelPressureHpa",    AttributeIds.WeatherSeaLevelPressureHpa,    "{\"Weather\":{\"SeaLevelPressureHpa\":1008.0}}"),
        ("Weather.RelativeHumidity",       AttributeIds.WeatherRelativeHumidity,       "{\"Weather\":{\"RelativeHumidity\":0.4}}"),
        ("Weather.VisibilityM",            AttributeIds.WeatherVisibilityM,            "{\"Weather\":{\"VisibilityM\":9000.0}}"),
        ("Weather.FogTopM",                AttributeIds.WeatherFogTopM,                "{\"Weather\":{\"FogTopM\":300.0}}"),
        ("Weather.FogVisibilityM",         AttributeIds.WeatherFogVisibilityM,         "{\"Weather\":{\"FogVisibilityM\":150.0}}"),
        ("Weather.Precipitation",          AttributeIds.WeatherPrecipitation,          "{\"Weather\":{\"Precipitation\":2}}"),
        ("Weather.PrecipitationMmPerHour", AttributeIds.WeatherPrecipitationMmPerHour, "{\"Weather\":{\"PrecipitationMmPerHour\":4.0}}"),
        ("Weather.FeatherM",               AttributeIds.WeatherFeatherM,               "{\"Weather\":{\"FeatherM\":500.0}}"),
    };

    public static IEnumerable<object[]> VocabularyCases()
        => Vocabulary.Select(v => new object[] { v.Path, v.Id, v.Json });

    // ══ ① the edge table must emit EVERY attribute the interpreter can apply ══════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT CAUGHT IT: every declared attribute produces a record with the right id.</b>
    ///
    /// <para>🔴 <c>Heading</c> reddened this — the edge table never registered it, so
    /// <c>{"Heading":90.0}</c> emitted <b>zero</b> records while <c>BuildBinaryInterpreter</c> stood ready
    /// to apply <c>Heading</c>. ⚠ Silent: no exception, no log, the rotation simply never left the edge.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(VocabularyCases))]
    public void EveryDeclaredAttributeIsEmittedByTheEdgeTable(string path, ushort id, string json)
    {
        var emitter = Emit(AttributeCompilerFactory.BuildEdgeCompiler(), json);

        Assert.Single(emitter.Records);
        Assert.Equal(id, emitter.Records[0].AttributeId);
    }

    /// <summary>
    /// ⭐⭐ <b>And the whole vocabulary in ONE patch emits one record each — no path shadows another.</b>
    ///
    /// <para>📌 Worth its own rail because the FNV path hashes are the routing key: two paths that collided
    /// would each pass ① in isolation *(the survivor wins)* and lose a record here.</para>
    /// </summary>
    [Fact]
    public void TheWholeVocabularyInOnePatchEmitsOneRecordEach()
    {
        const string all = "{\"Name\":\"Charlie\",\"Affiliation\":\"FORCE_OPPOSING\",\"Heading\":90.0," +
                           "\"Position\":{\"X\":32.0,\"Y\":34.0,\"Z\":100.0}," +
                           "\"ScenarioStartUtcTicks\":638450000000000000,\"ScenarioUtcOffsetMinutes\":120," +
                           "\"Fear\":1.0,\"FrightFrom\":{\"X\":10.0,\"Y\":20.0,\"Z\":1.0},\"Rule\":{\"Request\":2,\"Seq\":1,\"Origin\":1,\"Params\":\"{}\",\"EditSeq\":1,\"EditOrigin\":3}," +
                           "\"Health\":{\"Current\":50.0},\"Playable\":{\"Side\":\"FORCE_OPPOSING\",\"SlotName\":\"Alpha\"},\"SpawnArea\":{\"Side\":\"FORCE_FRIENDLY\",\"FacingDeg\":90.0},\"Ammo\":{\"2\":5},\"Stance\":1," +
                           "\"Weapon\":{\"Posture\":2,\"AimPoint\":{\"0\":12.5},\"AimTarget\":1,\"HeldItem\":1}," +
                           "\"Damage\":{\"Part\":{\"3\":128},\"MobilityKill\":1}," +
                           "\"Proficiency\":0.8,\"Temperament\":{\"RiskAcceptance\":0.7,\"Obedience\":0.6,\"Nerve\":0.5,\"Resolve\":0.4}," +
                           "\"Weather\":{\"GroundTemperatureC\":21.5,\"LapseRateKPerKm\":6.0,\"SeaLevelPressureHpa\":1008.0,\"RelativeHumidity\":0.4,\"VisibilityM\":9000.0,\"FogTopM\":300.0,\"FogVisibilityM\":150.0,\"Precipitation\":2,\"PrecipitationMmPerHour\":4.0,\"FeatherM\":500.0}}";

        var emitter = Emit(AttributeCompilerFactory.BuildEdgeCompiler(), all);

        Assert.Equal(
            Vocabulary.Select(v => v.Id).OrderBy(i => i).ToArray(),
            emitter.Records.Select(r => r.AttributeId).OrderBy(i => i).ToArray());
    }

    // ══ ② the declared vocabulary is not allowed to drift from the JSON compiler ══

    /// <summary>
    /// ⭐⭐⭐ <b>The declared list above must be exactly the JSON compiler's registered paths.</b>
    ///
    /// <para>⭐ This is what stops <see cref="Vocabulary"/> becoming a second stale table: a path added to
    /// <c>Build()</c> and nowhere else reddens HERE, and then ① reddens for the edge table. ⇒ ⛔ **a new
    /// attribute cannot be half-registered.**</para>
    /// </summary>
    [Fact]
    public void TheDeclaredVocabularyCoversEveryJsonRoute()
    {
        var registered = AttributeCompilerFactory.Build()
            .RegisteredPaths.OrderBy(p => p, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            Vocabulary.Select(v => v.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            registered);
    }

    // ══ ③ Affiliation-as-a-number: the ECS arm handles it, so the edge must too ══

    /// <summary>
    /// ⭐⭐⭐ <b><c>{"Affiliation":2}</c> must cross the edge, because the ECS arm goes out of its way to
    /// accept it.</b>
    ///
    /// <para>📐 <c>AttributeCompilerFactory.MapAffiliationInt</c> exists with the comment *"handles the
    /// ExCon default JSON serialisation which emits enums as their underlying integer value"*, and
    /// <c>EntityDataAttributeInstaller.HandleAffiliation</c> already branches on
    /// <c>record.Value.Kind == CsInt32</c>. ⇒ ⭐ **both ends were ready; only the edge refused.**
    /// 🔴 It was declared <c>CsString</c>, so <c>EmitRecord</c> called <c>reader.GetString()</c> on a
    /// Number token.</para>
    /// </summary>
    [Fact]
    public void AnIntegerAffiliationCrossesTheEdgeAsAnIntRecord()
    {
        var emitter = Emit(AttributeCompilerFactory.BuildEdgeCompiler(), "{\"Affiliation\":2}");

        Assert.Single(emitter.Records);
        Assert.Equal(AttributeIds.Affiliation, emitter.Records[0].AttributeId);
        Assert.Equal(AttributeValueKind.CsInt32, emitter.Records[0].Value.Kind);
        Assert.Equal(2, emitter.Records[0].Value.IntValue);
    }

    /// <summary>
    /// ⭐⭐ <b>And end-to-end: an integer affiliation through edge → interpreter → ECS lands as Hostile,</b>
    /// the same value the JSON→ECS arm produces for <c>"FORCE_OPPOSING"</c>.
    ///
    /// <para>⭐ ③ proves the record is emitted; this proves it MEANS the right thing once applied. ⚠ Two
    /// claims, deliberately not merged — an emitted record with the wrong kind would satisfy neither.</para>
    /// </summary>
    [Fact]
    public void AnIntegerAffiliationAppliesTheSameForceIdAsTheStringForm()
    {
        Assert.Equal(ForceId.Hostile, ApplyThroughEdgeAndBinary("{\"Affiliation\":2}").ForceId);
        Assert.Equal(ForceId.Hostile, ApplyThroughEdgeAndBinary("{\"Affiliation\":\"FORCE_OPPOSING\"}").ForceId);
    }

    // ══ ④ the two routes must agree end to end ════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE STRONGEST CLAIM: JSON→ECS and JSON→edge→binary→ECS produce the same ECS state.</b>
    ///
    /// <para>⭐⭐ ① compares REGISTRATIONS; this compares OUTCOMES, which is what a caller actually cares
    /// about. ⚠ A wrong <c>AttributeId</c> in the edge table would pass ① *(a record IS emitted)* and fail
    /// here.</para>
    /// </summary>
    /// <summary>⭐ CE-4024 (AQ93 §15 K5) — a creation request's fright gives the new entity a terrified mind that knows where it came from
    /// (the request carries no <c>CivilianState</c>: it is brain-local, never on the wire).</summary>
    [Fact]
    public void AFrightOnACreationRequest_BearsAMindInPanic()
    {
        var ctx = new ListPatchContext(new List<object>());
        AttributeCompilerFactory.Build().Compile("{\"Fear\":1.0,\"FrightFrom\":{\"X\":10.0,\"Y\":20.0,\"Z\":1.0}}", ctx);

        var mind = Assert.IsType<Fdp.Toolkit.Civilians.CivilianState>(Assert.Single(ctx.FlushComponents()));
        Assert.Equal(1f, mind.Fear);
        Assert.Equal(new System.Numerics.Vector3(10f, 20f, 1f), mind.FrightFrom);
        Assert.Equal(1, mind.HasFrightFrom);
    }

    [Fact]
    public void BothRoutesProduceTheSameEntityState()
    {
        const string patch = "{\"Name\":\"Charlie\",\"Affiliation\":\"FORCE_OPPOSING\",\"Heading\":90.0}";

        // ── route A: JSON straight to ECS ──
        var (repoA, eA) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build();
        compiler.Compile(patch, compiler.CreatePatchContext(repoA, eA));

        // ── route B: JSON → records → binary interpreter → ECS ──
        var infoB   = ApplyThroughEdgeAndBinary(patch, out var repoB, out var eB);
        var infoA   = repoA.GetComponent<Fdp.Core.EntityInfo>(eA);

        Assert.Equal(infoA.Name,    infoB.Name);
        Assert.Equal(infoA.ForceId, infoB.ForceId);

        var rotA = repoA.GetComponent<SimTransform>(eA).Rotation;
        var rotB = repoB.GetComponent<SimTransform>(eB).Rotation;
        Assert.Equal(rotA.Z, rotB.Z, 5);
        Assert.Equal(rotA.W, rotB.W, 5);

        // ⭐ And AX-017's claim ②: the same descriptors are dirty on both routes.
        Assert.Equal(Dirty(repoA, eA), Dirty(repoB, eB));
    }

    // ══ ④b Q59-A1′ — the vocabulary IS the declaration, and the rest is checked against it ══

    /// <summary>
    /// ⭐⭐⭐ <b>The local <see cref="Vocabulary"/> list and <c>AttributeVocabulary.All</c> agree.</b>
    ///
    /// <para>⭐⭐ <c>Q59-A1′</c> moved the declaration into production as
    /// <c>Fdp.Toolkit.Replication.Attributes.AttributeVocabulary</c>, and the edge table is now DERIVED from
    /// it — so <c>AX-018</c>'s missing row is unrepresentable. ⚠ This test's own list stays as an
    /// <b>independent</b> statement of the same facts *(double-entry)*: ⛔ if the two ever disagree, one of
    /// them was edited without thinking about the other.</para>
    /// </summary>
    [Fact]
    public void TheProductionVocabularyMatchesThisTestsIndependentList()
    {
        Assert.Equal(
            Vocabulary.Select(v => (v.Path, v.Id)).OrderBy(t => t.Path, StringComparer.Ordinal).ToArray(),
            AttributeVocabulary.All.Select(d => (d.JsonPath, d.AttributeId))
                .OrderBy(t => t.JsonPath, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL FOR WHAT COULD NOT BE DERIVED: every declared attribute has a BINARY HANDLER.</b>
    ///
    /// <para>⭐⭐ The JSON setters and binary handlers carry per-attribute logic, so they cannot be generated
    /// from the vocabulary *(<see cref="AttributeVocabulary"/>'s remarks say so plainly)*. ⇒ ⛔ the guard has
    /// to be a rail, and this is it: a row added to the vocabulary with no installer handler reddens here.</para>
    ///
    /// <para>⚠ Asserted by APPLYING each attribute and requiring the interpreter to have consumed it —
    /// ⛔ not by reflecting over the handler dictionary, which is private and would pin an implementation
    /// detail rather than the behaviour.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(VocabularyCases))]
    public void EveryDeclaredAttributeHasABinaryHandler(string path, ushort id, string json)
    {
        var (repo, e) = OwnedEntity(tank: id == AttributeIds.DamageMobilityKill);   // ⭐ S5 — the mobility switch needs a type with a Mobility zone
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();
        var patchCtx    = EcsPatchContext.Create(repo, e);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = e;

        interpreter.Apply(ctx, new[]
        {
            new EntityAttributeChange { AttributeId = id, Value = AttributeValue.FromDouble(1.0) }
        });

        Assert.True(patchCtx.HasAppliedAny,
            $"no binary handler consumed AttributeId {id} ('{path}'). Every AttributeVocabulary row needs " +
            "a handler in one of the installers — the vocabulary cannot generate one.");
    }

    /// <summary>
    /// ⭐⭐ <b>And every declared attribute has a JSON SETTER</b> — the other half that cannot be derived.
    /// </summary>
    [Theory]
    [MemberData(nameof(VocabularyCases))]
    public void EveryDeclaredAttributeHasAJsonSetter(string path, ushort id, string json)
    {
        var compiler = AttributeCompilerFactory.Build();

        Assert.Contains(path, compiler.RegisteredPaths);
    }

    // ══ ④c Q59-N3 — the PUBLISHED schema describes the real contract ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The published schema has one property per FULL path, with the RIGHT type.</b>
    ///
    /// <para>🔴🔴 <b>Measured before the fix:</b> <c>ExportSchema</c> keyed each property on the path's root
    /// segment, so the three dotted position paths (then geodetic) collapsed to their shared root and it appeared
    /// <b>three times in one JSON object</b>; and every type was <c>"string"</c>, including four
    /// <c>Float64</c> paths. ⇒ a consumer saw <b>4 properties instead of 6</b>, all mistyped.</para>
    ///
    /// <para>⭐ Asserted by PARSING — a duplicate key silently collapses in a parsed object, which is exactly
    /// how the defect stayed invisible. ⇒ counting parsed properties is what catches it.</para>
    /// </summary>
    [Fact]
    public void ThePublishedSchemaDescribesEveryPathWithItsRealType()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(
            AttributeCompilerFactory.Build().ExportSchema())!;

        var props = doc["properties"]!.AsObject();

        // ⭐ One property per declared path — not per root segment.
        Assert.Equal(
            AttributeVocabulary.All.Select(d => d.JsonPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            props.Select(kv => kv.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray());

        // ⭐⭐ And the types are truthful: the position paths and Heading are numbers, the clock's two are integers (CE-3216), the rest strings.
        foreach (var d in AttributeVocabulary.All)
        {
            string expected = d.Kind switch
            {
                AttributeValueKind.CsFloat64 => "number",
                AttributeValueKind.CsInt32 or AttributeValueKind.CsInt64 => "integer",
                _ => "string",
            };
            Assert.Equal(expected, props[d.JsonPath]!["type"]!.GetValue<string>());
        }
    }

    // ══ ⑤ ruling 9 — ONE edge table, not two ══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The production edge table is the factory's, not a hand-copy.</b>
    ///
    /// <para>🔴 <c>IgApplication</c> built its own <c>JsonToRecordCompilerBuilder</c> with the five
    /// registrations spelled out again and a comment saying they must stay in sync with
    /// <c>BuildEdgeCompiler()</c>. ⛔ **That comment is the whole enforcement** — and it had already failed:
    /// <c>Heading</c> was added to two tables and neither edge table got it.</para>
    ///
    /// <para>⭐ Railed as a SOURCE scan, because *"IG calls the factory"* is not something reflection can
    /// see — the call happens once at construction and leaves no signature behind. ⚠ Same reasoning as
    /// <c>StrictNetworkSeparationTests</c>' source rail *(a folded constant is invisible)*.</para>
    /// </summary>
    [Fact]
    public void NoOneOutsideTheFactoryBuildsAnEdgeTable()
    {
        // ⭐ The ONLY places allowed to name the builder: the factory (the one home), the builder's own
        //   file, and tests (which legitimately build ad-hoc tables).
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "AttributeCompilerFactory.cs",
            "JsonToRecordCompilerBuilder.cs",
            "JsonToRecordCompilerTests.cs",
            "TheFourRoutingTablesAgreeTests.cs",
        };

        var offenders = new List<string>();
        int scanned = 0;

        foreach (var root in new[] { "Hrot", "FDP" })
        {
            var dir = Path.Combine(TestRepo.Root, root);
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    continue;

                scanned++;
                if (!File.ReadAllText(file).Contains("new JsonToRecordCompilerBuilder()", StringComparison.Ordinal))
                    continue;
                if (allowed.Contains(Path.GetFileName(file)))
                    continue;

                offenders.Add(Path.GetFileName(file));
            }
        }

        // ⚠ The rail's own red-proof: a scan that found no sources would report green for ever.
        Assert.True(scanned > 100, $"the source scan only saw {scanned} files — it is not scanning the repo");

        Assert.Empty(offenders);
    }

    // ══ ⑥ R-1 (AR-3) — the FIFTH table: the READER. Every row has one, and set-then-read agrees ═════════════

    private enum Want { Number, Text, Long, Unreadable }

    /// <summary>
    /// ⭐ <b>What each row reads back after its JSON setter ran on a fresh entity.</b> (path, sub-index, JSON, the kind of answer, the expected value.)
    /// ⚠ Three rows' JSON setters CONSUME and IGNORE the value on a creation request (no world to judge in): <c>Weapon.AimTarget</c> (a live unit is needed — reads 0 = none),
    /// <c>Damage.MobilityKill</c> (needs the unit's TYPE — a body with no Mobility zone has no such switch ⇒ unreadable) and the mounts 1–3 of <c>Ammo.*</c>
    /// (child entities — so the sample sets mount 0). ⛔ Not a hole: the binary handlers apply them, the rail names the difference.
    /// </summary>
    private static readonly (string Path, int Sub, string Json, Want Want, double Number, string Text)[] ReadBack =
    {
        ("Name", 0, "{\"Name\":\"Charlie\"}", Want.Text, 0, "Charlie"),
        ("Affiliation", 0, "{\"Affiliation\":\"FORCE_OPPOSING\"}", Want.Text, 0, "FORCE_OPPOSING"),
        ("Position.X", 0, "{\"Position\":{\"X\":32.0}}", Want.Number, 32.0, ""),
        ("Position.Y", 0, "{\"Position\":{\"Y\":34.0}}", Want.Number, 34.0, ""),
        ("Position.Z", 0, "{\"Position\":{\"Z\":100.0}}", Want.Number, 100.0, ""),
        ("Heading", 0, "{\"Heading\":90.0}", Want.Number, 90.0, ""),
        ("ScenarioStartUtcTicks", 0, "{\"ScenarioStartUtcTicks\":638450000000000000}", Want.Long, 638450000000000000d, ""),
        ("ScenarioUtcOffsetMinutes", 0, "{\"ScenarioUtcOffsetMinutes\":120}", Want.Number, 120, ""),
        ("Fear", 0, "{\"Fear\":1.0}", Want.Number, 1.0, ""),
        ("FrightFrom.X", 0, "{\"FrightFrom\":{\"X\":10.0}}", Want.Number, 10.0, ""),
        ("FrightFrom.Y", 0, "{\"FrightFrom\":{\"Y\":20.0}}", Want.Number, 20.0, ""),
        ("FrightFrom.Z", 0, "{\"FrightFrom\":{\"Z\":1.0}}", Want.Number, 1.0, ""),
        ("Rule.Request", 0, "{\"Rule\":{\"Request\":2}}", Want.Number, 2, ""),
        ("Rule.Seq", 0, "{\"Rule\":{\"Seq\":1}}", Want.Number, 1, ""),
        ("Rule.Origin", 0, "{\"Rule\":{\"Origin\":1}}", Want.Number, 1, ""),
        ("Rule.Params", 0, "{\"Rule\":{\"Params\":\"{}\"}}", Want.Text, 0, "{}"),
        ("Rule.EditSeq", 0, "{\"Rule\":{\"EditSeq\":1}}", Want.Number, 1, ""),
        ("Rule.EditOrigin", 0, "{\"Rule\":{\"EditOrigin\":3}}", Want.Number, 3, ""),
        ("Health.Current", 0, "{\"Health\":{\"Current\":50.0}}", Want.Number, 50.0, ""),
        ("Playable.Side", 0, "{\"Playable\":{\"Side\":\"FORCE_OPPOSING\"}}", Want.Text, 0, "FORCE_OPPOSING"),
        ("Playable.SlotName", 0, "{\"Playable\":{\"SlotName\":\"Alpha\"}}", Want.Text, 0, "Alpha"),
        ("SpawnArea.Side", 0, "{\"SpawnArea\":{\"Side\":\"FORCE_FRIENDLY\"}}", Want.Text, 0, "FORCE_FRIENDLY"),
        ("SpawnArea.FacingDeg", 0, "{\"SpawnArea\":{\"FacingDeg\":90.0}}", Want.Number, 90.0, ""),
        ("Ammo.*", 0, "{\"Ammo\":{\"0\":5}}", Want.Number, 5, ""),
        ("Stance", 0, "{\"Stance\":1}", Want.Number, 1, ""),
        ("Weapon.Posture", 0, "{\"Weapon\":{\"Posture\":2}}", Want.Number, 2, ""),
        ("Weapon.AimPoint.*", 0, "{\"Weapon\":{\"AimPoint\":{\"0\":12.5}}}", Want.Number, 12.5, ""),
        ("Weapon.AimTarget", 0, "{\"Weapon\":{\"AimTarget\":1}}", Want.Long, 0, ""),
        ("Weapon.HeldItem", 0, "{\"Weapon\":{\"HeldItem\":1}}", Want.Number, 1, ""),
        ("Damage.Part.*", 3, "{\"Damage\":{\"Part\":{\"3\":128}}}", Want.Number, 128, ""),
        ("Damage.MobilityKill", 0, "{\"Damage\":{\"MobilityKill\":1}}", Want.Unreadable, 0, ""),
        ("Proficiency", 0, "{\"Proficiency\":0.8}", Want.Number, 0.8, ""),
        ("Temperament.RiskAcceptance", 0, "{\"Temperament\":{\"RiskAcceptance\":0.7}}", Want.Number, 0.7, ""),
        ("Temperament.Obedience", 0, "{\"Temperament\":{\"Obedience\":0.6}}", Want.Number, 0.6, ""),
        ("Temperament.Nerve", 0, "{\"Temperament\":{\"Nerve\":0.5}}", Want.Number, 0.5, ""),
        ("Temperament.Resolve", 0, "{\"Temperament\":{\"Resolve\":0.4}}", Want.Number, 0.4, ""),
        ("Weather.GroundTemperatureC", 0, "{\"Weather\":{\"GroundTemperatureC\":21.5}}", Want.Number, 21.5, ""),
        ("Weather.LapseRateKPerKm", 0, "{\"Weather\":{\"LapseRateKPerKm\":6.0}}", Want.Number, 6.0, ""),
        ("Weather.SeaLevelPressureHpa", 0, "{\"Weather\":{\"SeaLevelPressureHpa\":1008.0}}", Want.Number, 1008.0, ""),
        ("Weather.RelativeHumidity", 0, "{\"Weather\":{\"RelativeHumidity\":0.4}}", Want.Number, 0.4, ""),
        ("Weather.VisibilityM", 0, "{\"Weather\":{\"VisibilityM\":9000.0}}", Want.Number, 9000.0, ""),
        ("Weather.FogTopM", 0, "{\"Weather\":{\"FogTopM\":300.0}}", Want.Number, 300.0, ""),
        ("Weather.FogVisibilityM", 0, "{\"Weather\":{\"FogVisibilityM\":150.0}}", Want.Number, 150.0, ""),
        ("Weather.Precipitation", 0, "{\"Weather\":{\"Precipitation\":2}}", Want.Number, 2, ""),
        ("Weather.PrecipitationMmPerHour", 0, "{\"Weather\":{\"PrecipitationMmPerHour\":4.0}}", Want.Number, 4.0, ""),
        ("Weather.FeatherM", 0, "{\"Weather\":{\"FeatherM\":500.0}}", Want.Number, 500.0, ""),
    };

    public static IEnumerable<object[]> ReadBackCases() => ReadBack.Select(r => new object[] { r.Path });

    /// <summary>⭐ A fresh owned entity whose caps let the clamping setters keep the sample value (<c>Health.Max</c> 100, <c>WeaponState.MaxAmmo</c> 10).</summary>
    private static (EntityRepository, Entity) EntityForReadBack()
    {
        var (repo, e) = OwnedEntity();
        repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(e).Max = 100f;
        repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(e).MaxAmmo = 10;
        return (repo, e);
    }

    /// <summary>⭐⭐⭐ <b>AR-3 (a) — EVERY vocabulary row has a reader</b>, and the rail's own table names every row (a row added to the vocabulary reddens here until it has both).</summary>
    [Fact]
    public void EveryVocabularyRowHasAReader()
    {
        var missing = AttributeVocabulary.All.Where(d => !AttributeReaders.HasReader(d.AttributeId)).Select(d => d.JsonPath).ToArray();
        Assert.True(missing.Length == 0, "no AttributeReaders entry for: " + string.Join(", ", missing));

        Assert.Equal(
            AttributeVocabulary.All.Select(d => d.JsonPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            ReadBack.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>AR-3 (b) — SET THEN READ AGREES, for every row.</b> The row's JSON goes through the JSON compiler onto a fresh entity, the value is read back through
    /// <see cref="AttributeReaders"/> and compared. 📌 The setter and the reader are two halves of one meaning: a setter that writes the wrong field, or a reader that reads
    /// another, reddens HERE.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReadBackCases))]
    public void WhatTheJsonSetterWritesTheReaderReadsBack(string path)
    {
        var row = ReadBack.Single(r => r.Path == path);
        var (repo, e) = EntityForReadBack();
        var compiler = AttributeCompilerFactory.Build();
        compiler.Compile(row.Json, compiler.CreatePatchContext(repo, e));

        var def = AttributeVocabulary.All.Single(d => d.JsonPath == path);
        bool ok = AttributeReaders.TryRead(repo, e, def.AttributeId, row.Sub, out var v);
        if (row.Want == Want.Unreadable) { Assert.False(ok, $"'{path}' should not be readable on this entity"); return; }
        Assert.True(ok, $"'{path}' read nothing after its setter ran");

        switch (row.Want)
        {
            case Want.Text:
                Assert.Equal(AttributeValueKind.CsString, v.Kind);
                Assert.Equal(row.Text, path == "Rule.Params" ? v.StringValue : v.Text.ToString());
                break;
            case Want.Long:
                Assert.Equal(AttributeValueKind.CsInt64, v.Kind);
                Assert.Equal((long)row.Number, v.LongValue);
                break;
            default:
                Assert.Equal(row.Number, FormAttributes.AsDouble(v), 3);
                break;
        }

        // the same read, by the path as the world spells it
        string spelled = path.EndsWith(".*", StringComparison.Ordinal) ? path[..^1] + row.Sub : path;
        Assert.True(AttributeReaders.TryRead(repo, e, spelled, out var again), spelled);
        Assert.Equal(v.Kind, again.Kind);
    }

    /// <summary>⭐ A path resolves to its id and sub-index: <c>Ammo.2</c> → (Ammo, 2), the wildcard's own spelling → sub-index 0, an unknown path → false.</summary>
    [Fact]
    public void APathResolvesToItsIdAndSubIndex()
    {
        Assert.True(AttributeReaders.TryResolve("Health.Current", out var id, out var sub));
        Assert.Equal((AttributeIds.HealthCurrent, 0), (id, sub));
        Assert.True(AttributeReaders.TryResolve("Ammo.2", out id, out sub));
        Assert.Equal((AttributeIds.Ammo, 2), (id, sub));
        Assert.True(AttributeReaders.TryResolve("Damage.Part.13", out id, out sub));
        Assert.Equal((AttributeIds.DamagePart, 13), (id, sub));
        Assert.True(AttributeReaders.TryResolve("Weapon.AimPoint.*", out id, out sub));
        Assert.Equal((AttributeIds.WeaponAimPoint, 0), (id, sub));
        Assert.False(AttributeReaders.TryResolve("Health.Nope", out _, out _));
        Assert.False(AttributeReaders.TryResolve("Health.Current.4", out _, out _));
    }

    /// <summary>⭐ A wildcard lists the sub-indices the entity has; an entity without the component reads false everywhere.</summary>
    [Fact]
    public void AWildcardListsItsSubIndicesAndAMissingComponentReadsNothing()
    {
        var (repo, e) = EntityForReadBack();
        var subs = new List<int>();
        AttributeReaders.SubIndicesOf(repo, e, AttributeIds.Ammo, subs);
        Assert.Equal(new[] { 0 }, subs);
        subs.Clear();
        AttributeReaders.SubIndicesOf(repo, e, AttributeIds.WeaponAimPoint, subs);
        Assert.Equal(new[] { 0, 1, 2 }, subs);
        subs.Clear();
        AttributeReaders.SubIndicesOf(repo, e, AttributeIds.HealthCurrent, subs);
        Assert.Equal(new[] { 0 }, subs);

        var bare = repo.CreateEntity();
        foreach (var d in AttributeVocabulary.All)
            Assert.False(AttributeReaders.TryRead(repo, bare, d.AttributeId, 0, out _), d.JsonPath);
        Assert.False(AttributeReaders.TryRead(repo, Entity.Null, AttributeIds.HealthCurrent, 0, out _));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>AR-3 (c) — 0 B.</b> A form reads through the reader every frame (R-2084): reading every row — numbers, the text rows (<c>Name</c>, <c>SlotName</c>, a force token)
    /// and the managed <c>Rule.Params</c> — in a loop allocates nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReadBackCases))]
    public void ReadingARowAllocatesNothing(string path)
    {
        var row = ReadBack.Single(r => r.Path == path);
        var (repo, e) = EntityForReadBack();
        var compiler = AttributeCompilerFactory.Build();
        compiler.Compile(row.Json, compiler.CreatePatchContext(repo, e));
        ushort id = AttributeVocabulary.All.Single(d => d.JsonPath == path).AttributeId;

        for (int i = 0; i < 50; i++) AttributeReaders.TryRead(repo, e, id, row.Sub, out _);   // warm: JIT, statics
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) AttributeReaders.TryRead(repo, e, id, row.Sub, out _);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(after - before == 0, $"reading '{path}' allocated {after - before} B over 1000 reads (hot path: 0)");
    }

    /// <summary>⭐ <b>AR-7 — every row declares its unit</b>, or is on the short list of values that have none (a name, a code, a switch).</summary>
    [Fact]
    public void EveryRowDeclaresItsUnitOrIsOnTheListOfUnitless()
    {
        var unitless = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name", "Affiliation", "Rule.Request", "Rule.Seq", "Rule.Origin", "Rule.Params", "Rule.EditSeq", "Rule.EditOrigin", "Health.Current",
            "Playable.Side", "Playable.SlotName", "SpawnArea.Side", "Stance", "Weapon.Posture", "Weapon.AimTarget", "Weapon.HeldItem", "Damage.MobilityKill", "Weather.Precipitation",
        };
        foreach (var d in AttributeVocabulary.All)
            Assert.True(unitless.Contains(d.JsonPath) == (d.Unit == null), $"'{d.JsonPath}': Unit = {d.Unit ?? "null"}");
        Assert.Equal("m", AttributeVocabulary.All.Single(d => d.JsonPath == "Position.X").Unit);
        Assert.Equal("°", AttributeVocabulary.All.Single(d => d.JsonPath == "SpawnArea.FacingDeg").Unit);
        Assert.Equal("hPa", AttributeVocabulary.All.Single(d => d.JsonPath == "Weather.SeaLevelPressureHpa").Unit);
    }

    // ══ helpers ══════════════════════════════════════════════════════════════════

    private static ListEmitter Emit(JsonToRecordCompiler compiler, string json)
    {
        var emitter = new ListEmitter();
        compiler.Compile(Encoding.UTF8.GetBytes(json), emitter);
        return emitter;
    }

    private static Fdp.Core.EntityInfo ApplyThroughEdgeAndBinary(string json)
        => ApplyThroughEdgeAndBinary(json, out _, out _);

    private static Fdp.Core.EntityInfo ApplyThroughEdgeAndBinary(
        string json, out EntityRepository repo, out Entity entity)
    {
        var records = Emit(AttributeCompilerFactory.BuildEdgeCompiler(), json).Records.ToArray();

        (repo, entity) = OwnedEntity();
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();
        var patchCtx    = EcsPatchContext.Create(repo, entity);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = entity;
        interpreter.Apply(ctx, records);

        return repo.GetComponent<Fdp.Core.EntityInfo>(entity);
    }

    private static (EntityRepository, Entity) OwnedEntity(bool tank = false)
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<Fdp.Core.EntityInfo>();
        repo.RegisterComponent<EgressPublicationState>();

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(Fdp.Core.EntityInfo));
        repo.AddComponent(e, default(SimTransform));
        repo.AddComponent(e, default(Fdp.Toolkit.Weather.ScenarioClock));   // ⭐ CE-3216 — the clock's two attributes
        if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.CivilianState>()) repo.RegisterComponent<Fdp.Toolkit.Civilians.CivilianState>();
        repo.AddComponent(e, default(Fdp.Toolkit.Civilians.CivilianState));   // ⭐ CE-4024 — the fright's four attributes
        repo.SetAuthority<Fdp.Core.EntityInfo>(e, true);
        repo.SetAuthority<SimTransform>(e, true);
        repo.SetAuthority<Fdp.Toolkit.Weather.ScenarioClock>(e, true);
        repo.SetAuthority<Fdp.Toolkit.Civilians.CivilianState>(e, true);
        repo.AddComponent(e, default(Fdp.Toolkit.ScenarioRules.RuleControl));   // ⭐ rules V-4 — the control's three attributes
        repo.SetAuthority<Fdp.Toolkit.ScenarioRules.RuleControl>(e, true);
        // ⭐ B-4 / Details forms D2 — the components the form attributes write (owned here, so each handler applies)
        AddOwned(repo, e, default(Fdp.Toolkit.Combat.Components.Health));
        AddOwned(repo, e, default(Hrot.Common.Playable));
        AddOwned(repo, e, default(Hrot.Common.SpawnArea));
        AddOwned(repo, e, default(Fdp.Toolkit.Combat.Components.WeaponState));
        AddOwned(repo, e, default(Hrot.MuscleCharacter.Animation.Components.StanceIntent));
        AddOwned(repo, e, default(Fdp.Toolkit.Weather.WeatherConditions));
        AddOwned(repo, e, default(Fdp.Toolkit.Combat.Components.WeaponPosture));   // ⭐ Initial state S2 — the weapon's start state
        AddOwned(repo, e, default(Fdp.Toolkit.Physics.BodyParts.PartDamage));   // ⭐ Initial state S5 — the damage per part
        if (tank)   // a typed body with a Mobility zone (the person body has none)
        {
            if (!repo.IsComponentTypeRegistered<TkbIdentity>()) repo.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var template = new Fdp.Interfaces.TkbTemplate("T6", 6) { DisType = new Fdp.Core.DISEntityType { Kind = 1, Domain = 1, Category = 1 } };
            template.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 500f, ArmorSide = 80f, ArmorRear = 40f, ArmorTop = 30f });
            db.Register(template);
            repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            repo.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)6 });
        }
        AddOwned(repo, e, default(Fdp.Toolkit.Behavior.Components.CombatProficiency));   // ⭐ Initial state S6 — the AI character settings
        AddOwned(repo, e, default(Fdp.Toolkit.Behavior.Components.CombatTemperament));
        if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var aimedAt = repo.CreateEntity();   // ⭐ S2 — a unit with network id 1, so Weapon.AimTarget = 1 resolves
        repo.AddComponent(aimedAt, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1 });
        repo.SetManagedComponent(e, new Fdp.Toolkit.ScenarioRules.RuleEdit());   // ⭐ rules ② — a live edit's three attributes (managed, replaced whole)
        repo.SetAuthority(e, ManagedComponentType<Fdp.Toolkit.ScenarioRules.RuleEdit>.ID, true);
        return (repo, e);
    }

    private static void AddOwned<T>(EntityRepository repo, Entity e, T value) where T : unmanaged
    {
        if (!repo.IsComponentTypeRegistered<T>()) repo.RegisterComponent<T>();
        repo.AddComponent(e, value);
        repo.SetAuthority<T>(e, true);
    }

    private static long[] Dirty(EntityRepository repo, Entity e)
        => repo.HasManagedComponent<EgressPublicationState>(e)
            ? repo.GetComponent<EgressPublicationState>(e).DirtyDescriptors.OrderBy(o => o).ToArray()
            : Array.Empty<long>();

    /// <summary>⭐ Collects everything the edge compiler emits, kind included.</summary>
    private sealed class ListEmitter : IAttributeRecordEmitter
    {
        public readonly List<EntityAttributeChange> Records = new();

        private void Add(ushort id, AttributeValue v) =>
            Records.Add(new EntityAttributeChange { AttributeId = id, Value = v });

        public void EmitInt32(ushort id, int v, short s1 = 0, short s2 = 0)      => Add(id, AttributeValue.FromInt(v));
        public void EmitInt64(ushort id, long v, short s1 = 0, short s2 = 0)     => Add(id, AttributeValue.FromInt((int)v));
        public void EmitFloat32(ushort id, float v, short s1 = 0, short s2 = 0)  => Add(id, AttributeValue.FromDouble(v));
        public void EmitFloat64(ushort id, double v, short s1 = 0, short s2 = 0) => Add(id, AttributeValue.FromDouble(v));
        public void EmitBool(ushort id, bool v, short s1 = 0, short s2 = 0)      => Add(id, AttributeValue.FromInt(v ? 1 : 0));
        public void EmitString(ushort id, string? v, short s1 = 0, short s2 = 0) => Add(id, AttributeValue.FromString(v ?? string.Empty));
    }

}
