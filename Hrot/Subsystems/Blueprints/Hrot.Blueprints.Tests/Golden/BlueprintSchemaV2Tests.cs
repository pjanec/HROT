using System.Text.Json.Nodes;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// <b>U-10 (D2) — the <c>1 ⇄ 2</c> transform pair, held to the strongest gate the plan asked for.</b>
///
/// <para>
/// ⭐⭐ <b><c>v1 → v2 → v1</c> byte identity, on all 58 shipped assets.</b> ⛔ The plan recorded this
/// gate as unwritable — <b>0 of 58</b> files survived even <c>Deserialize → Serialize</c>, because
/// they were hand-authored against a serializer that wrote something else. <c>U-15</c> made it
/// writable; this is it, run.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Batch 55: the pair IS wired now — and the corpus is v2, so this file's inputs inverted.</b>
/// The reader (Batch 54), the migration registry (step 2) and the writer (step 3) all use it, and the
/// 58 shipped assets are v2 on disk. ⇒ <see cref="LoadAsV1"/> produces the v1 form these v1-oriented
/// assertions need, which exercises the DOWN-migrator on all 58 as a side effect.
/// ⚠ <b>Registry-path coverage lives in <c>BlueprintMigrationRegistryTests</c></b> — this file still
/// proves the transform called directly, and the two are deliberately not the same test.
/// </para>
/// </summary>
public sealed class BlueprintSchemaV2Tests
{
    private static JsonObject Load(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    /// <summary>
    /// ⭐⭐ <b>Batch 55 step 3 — the shipped files are v2 now, so the v1 form has to be produced.</b>
    /// ⚠ Every v1-oriented assertion below used to read the file directly; since the bump that would
    /// hand <c>Up</c> a v2 document, which it refuses. ⛔ Down-migrating first is not a workaround —
    /// it is the honest statement that v1 is now a derived form, and it exercises the down-migrator
    /// on every one of the 58 as a side effect.
    /// </summary>
    private static JsonObject LoadAsV1(string file) => BlueprintSchemaV2.Down(Load(file));

    private static IEnumerable<string> Files() => CorpusCanonicalisationTests.AllManagedFiles();

    /// <summary>
    /// ⭐⭐ <b>The gate.</b> Round-tripping through v2 and back reproduces the file byte for byte.
    /// ⚠ Compared as serialized text, not as a DOM: property ORDER is part of what canonical means,
    /// and a DOM comparison would pass while the bytes moved.
    /// </summary>
    [Fact]
    public void V1ToV2ToV1IsTheIdentity_ForEveryShippedAsset()
    {
        var broken = new List<string>();

        foreach (var file in Files())
        {
            var v1       = LoadAsV1(file);
            var original = v1.ToJsonString(Indented);
            var back     = BlueprintSchemaV2.Down(BlueprintSchemaV2.Up(v1)).ToJsonString(Indented);

            if (!string.Equals(original.TrimEnd('\n'), back.TrimEnd('\n'), StringComparison.Ordinal))
                broken.Add(Path.GetFileName(file) + FirstDifference(original, back));
        }

        Assert.True(broken.Count == 0,
            "v1 -> v2 -> v1 is not the identity for:\n  " + string.Join("\n  ", broken));
    }

    /// <summary>
    /// ⭐⭐ <b>The direction the tree now has: <c>v2 → v1 → v2</c>, byte-identical on all 58.</b>
    ///
    /// <para>
    /// ⚠ <b>Not a restatement of the gate above — it starts from the shipped BYTES.</b>
    /// <c>V1ToV2ToV1</c> begins at a v1 form this test suite computed, so a systematic error in
    /// <c>Down</c> would cancel out of it. This one begins at what is actually on disk, so it is the
    /// half that would catch the corpus and the transform disagreeing.
    /// </para>
    /// </summary>
    [Fact]
    public void V2ToV1ToV2IsTheIdentity_ForEveryShippedAsset()
    {
        var broken = new List<string>();

        foreach (var file in Files())
        {
            var onDisk = Load(file);
            var back   = BlueprintSchemaV2.Up(BlueprintSchemaV2.Down(onDisk));

            if (!string.Equals(onDisk.ToJsonString(Indented), back.ToJsonString(Indented),
                               StringComparison.Ordinal))
                broken.Add(Path.GetFileName(file));
        }

        Assert.True(broken.Count == 0,
            "v2 -> v1 -> v2 is not the identity for:\n  " + string.Join("\n  ", broken));
    }

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string FirstDifference(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return $" (first difference at char {i}: expected '{Excerpt(a, i)}', got '{Excerpt(b, i)}')";
    }

    private static string Excerpt(string s, int at)
        => at >= s.Length ? "<end>" : s.Substring(at, Math.Min(40, s.Length - at)).Replace("\n", "\\n");

    /// <summary>
    /// ⭐ <b>The transform is not vacuous</b> — v2 really does carry every declaration, tagged, and
    /// really does drop the three lists. ⛔ Without this, a <c>Up</c> that returned its input would
    /// pass the identity gate perfectly.
    /// </summary>
    [Fact]
    public void UpProducesOneTaggedArrayCarryingEveryDeclaration()
    {
        var touched = 0;

        foreach (var file in Files())
        {
            var v1 = LoadAsV1(file);
            var expected = new[] { "Parameters", "WorkingState", "Variables" }
                .Sum(p => v1[p] is JsonArray a ? a.Count : 0);

            var v2 = BlueprintSchemaV2.Up(v1);

            Assert.False(v2.ContainsKey("Parameters"));
            Assert.False(v2.ContainsKey("WorkingState"));
            Assert.False(v2.ContainsKey("Variables"));
            Assert.True(BlueprintSchemaV2.IsV2(v2));

            var declarations = Assert.IsType<JsonArray>(v2[BlueprintSchemaV2.DeclarationsProperty]);
            Assert.Equal(expected, declarations.Count);
            foreach (var d in declarations)
                Assert.NotNull(d!.AsObject()[BlueprintSchemaV2.KindProperty]);

            if (expected > 0) touched++;
        }

        // ⚠ If every shipped asset happened to declare nothing, the sweep above would be green and
        //   meaningless. It is not: assert that it isn't.
        Assert.True(touched > 0, "no shipped asset declares a parameter, working-state entry or variable");
    }

    /// <summary>
    /// ⭐ Concatenation order is storage order — Parameter, WorkingState, Variable — which is
    /// <c>DeclarationList.KindOrder</c> and the struct layout order. ⛔ Any other order would put a
    /// union index out of step with the layout the blackboard is addressed by.
    /// </summary>
    [Fact]
    public void TheTaggedArrayIsInStorageOrder()
    {
        var v1 = new JsonObject
        {
            ["Name"]         = "OrderProbe",
            ["Parameters"]   = new JsonArray(new JsonObject { ["Name"] = "P" }),
            ["WorkingState"] = new JsonArray(new JsonObject { ["Name"] = "W" }),
            ["Variables"]    = new JsonArray(new JsonObject { ["Name"] = "V" }),
        };

        var declarations = BlueprintSchemaV2.Up(v1)[BlueprintSchemaV2.DeclarationsProperty]!.AsArray();

        Assert.Equal(
            new[] { "Parameter", "WorkingState", "Variable" },
            declarations.Select(d => d!.AsObject()[BlueprintSchemaV2.KindProperty]!.GetValue<string>()));
        Assert.Equal(
            new[] { "P", "W", "V" },
            declarations.Select(d => d!.AsObject()["Name"]!.GetValue<string>()));

        // ⭐⭐ Batch 86 — RESTATED. R-01 retires WorkingState as a KIND but NOT as an on-disk TAG: a v1
        //   document still has three lists, and Up must keep tagging all three or the middle one is a
        //   variable lost on load. ⇒ the two sequences can no longer be equal, so the claim becomes
        //   "the tags AGREE with KindOrder on the kinds both carry, in order" — which is the half that
        //   decides which struct a declaration is laid out in.
        var tagged = declarations
            .Select(d => d!.AsObject()[BlueprintSchemaV2.KindProperty]!.GetValue<string>())
            .ToArray();
        var kindOrder = DeclarationList.KindOrder.Select(k => k.ToString()).ToArray();
        Assert.Equal(kindOrder, tagged.Where(kindOrder.Contains).ToArray());

        // ⛔ And the retired tag leads the run it was absorbed into — R-24: reordering it moves every
        //    following field's offset.
        Assert.True(Array.IndexOf(tagged, "WorkingState")
                  < Array.IndexOf(tagged, nameof(DeclarationKind.Variable)));
    }

    /// <summary>
    /// ⛔⛔ <b>An untagged declaration THROWS rather than being dropped.</b> A dropped declaration is a
    /// field missing from the emitted struct, which shifts every later offset — the blackboard wipe
    /// this task's Pass 3 exists to prevent. ⭐ A migrator that silently skips what it cannot classify
    /// is how that happens quietly.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Local")]
    [InlineData("")]
    public void DownRefusesADeclarationItCannotClassify(string? tag)
    {
        var decl = new JsonObject { ["Name"] = "Orphan" };
        if (tag is not null) decl[BlueprintSchemaV2.KindProperty] = tag;

        var v2 = new JsonObject
        {
            ["Name"] = "RefusalProbe",
            [BlueprintSchemaV2.DeclarationsProperty] = new JsonArray(decl),
        };

        var ex = Assert.Throws<InvalidDataException>(() => BlueprintSchemaV2.Down(v2));
        Assert.Contains("silently delete", ex.Message);
    }

    /// <summary>
    /// ⭐ <b>The round trip preserves the MODEL, not only the bytes.</b> ⚠ Byte identity is the
    /// stronger claim, but it is stated over the canonical form; this says the same thing in the terms
    /// the compiler actually consumes, so a future formatting change cannot quietly weaken the gate to
    /// nothing.
    /// </summary>
    [Fact]
    public void TheRoundTripPreservesEveryDeclarationInTheModel()
    {
        foreach (var file in Files())
        {
            var before = BlueprintJsonServices.Deserialize(File.ReadAllText(file))!;
            var after  = BlueprintJsonServices.Deserialize(
                BlueprintSchemaV2.Down(BlueprintSchemaV2.Up(LoadAsV1(file))).ToJsonString(Indented))!;

            Assert.Equal(
                before.Declarations.Select(d => $"{d.Kind}:{d.Id}:{d.Name}:{d.Type.TypeId}"),
                after.Declarations.Select(d => $"{d.Kind}:{d.Id}:{d.Name}:{d.Type.TypeId}"));
        }
    }

    /// <summary>Up refuses a v2 document and Down refuses a v1 one — neither guesses.</summary>
    [Fact]
    public void EachDirectionRefusesTheWrongShape()
    {
        // ⚠ Batch 54: canonical now, and deliberately. This fixture used to be `{ "Parameters": [] }`
        // — one of the three lists — which `Up` refuses since it started requiring canonical v1 in.
        // ⭐ The test is about DIRECTION (v1 vs v2), not canonicality; that is
        // SchemaV2AdversarialTests' subject, so the fixture is corrected rather than the rule relaxed.
        var v1 = new JsonObject
        {
            ["Parameters"]   = new JsonArray(),
            ["WorkingState"] = new JsonArray(),
            ["Variables"]    = new JsonArray(),
        };
        var v2 = BlueprintSchemaV2.Up(v1);

        Assert.Throws<InvalidOperationException>(() => BlueprintSchemaV2.Up(v2));
        Assert.Throws<InvalidOperationException>(() => BlueprintSchemaV2.Down(v1));
    }
}
