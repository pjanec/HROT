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
        ("GeoPosition.Latitude",  AttributeIds.GeoLat,     "{\"GeoPosition\":{\"Latitude\":32.0}}"),
        ("GeoPosition.Longitude", AttributeIds.GeoLon,     "{\"GeoPosition\":{\"Longitude\":34.0}}"),
        ("GeoPosition.Altitude",  AttributeIds.GeoAlt,     "{\"GeoPosition\":{\"Altitude\":100.0}}"),
        ("Heading",               AttributeIds.Heading, "{\"Heading\":90.0}"),
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
                           "\"GeoPosition\":{\"Latitude\":32.0,\"Longitude\":34.0,\"Altitude\":100.0}}";

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
        var registered = AttributeCompilerFactory.Build(new VocabularyGeoTransform())
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
    [Fact]
    public void BothRoutesProduceTheSameEntityState()
    {
        const string patch = "{\"Name\":\"Charlie\",\"Affiliation\":\"FORCE_OPPOSING\",\"Heading\":90.0}";

        // ── route A: JSON straight to ECS ──
        var (repoA, eA) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
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
            var dir = Path.Combine(RepoRoot(), root);
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
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var patchCtx    = EcsPatchContext.Create(repo, entity);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = entity;
        interpreter.Apply(ctx, records);

        return repo.GetComponent<Fdp.Core.EntityInfo>(entity);
    }

    private static (EntityRepository, Entity) OwnedEntity()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<Fdp.Core.EntityInfo>();
        repo.RegisterComponent<EgressPublicationState>();

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(Fdp.Core.EntityInfo));
        repo.AddComponent(e, default(SimTransform));
        repo.SetAuthority<Fdp.Core.EntityInfo>(e, true);
        repo.SetAuthority<SimTransform>(e, true);
        return (repo, e);
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

    /// <summary>⭐ A geo transform so <see cref="AttributeCompilerFactory.Build"/> registers the geo paths.</summary>
    private sealed class VocabularyGeoTransform : Fdp.Modules.Geographic.IGeographicTransform
    {
        public void SetOrigin(double lat, double lon, double alt) { }

        public System.Numerics.Vector3 ToCartesian(double lat, double lon, double alt)
            => new System.Numerics.Vector3((float)lon, (float)lat, (float)alt);

        public (double lat, double lon, double alt) ToGeodetic(System.Numerics.Vector3 p)
            => (p.Y, p.X, p.Z);
    }

    /// <summary>⭐ Walks up to the repo root. ⛔ Fails loudly rather than skipping.</summary>
    private static string RepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var probe = start;
            while (!string.IsNullOrEmpty(probe))
            {
                if (Directory.Exists(Path.Combine(probe, "Hrot")) &&
                    Directory.Exists(Path.Combine(probe, "FDP")))
                    return probe;
                probe = Path.GetDirectoryName(probe);
            }
        }

        Assert.Fail("Could not locate the repo root; this rail scans source and cannot run without it.");
        return string.Empty;
    }
}
