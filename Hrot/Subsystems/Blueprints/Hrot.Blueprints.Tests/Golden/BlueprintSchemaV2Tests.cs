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
/// ⚠ <b>The pair is not wired into anything.</b> Nothing writes v2 and nothing reads it — see the
/// batch 49 report for why that sequencing changed after <c>U-9</c> landed inverse. What is proved
/// here is the part that does not depend on the sequencing: that the translation itself loses
/// nothing, measured against real data rather than fixtures.
/// </para>
/// </summary>
public sealed class BlueprintSchemaV2Tests
{
    private static JsonObject Load(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

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
            var original = File.ReadAllText(file).Replace("\r\n", "\n");
            var back     = BlueprintSchemaV2.Down(BlueprintSchemaV2.Up(Load(file)))
                                            .ToJsonString(Indented);

            if (!string.Equals(original.TrimEnd('\n'), back.TrimEnd('\n'), StringComparison.Ordinal))
                broken.Add(Path.GetFileName(file) + FirstDifference(original, back));
        }

        Assert.True(broken.Count == 0,
            "v1 -> v2 -> v1 is not the identity for:\n  " + string.Join("\n  ", broken));
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
            var v1 = Load(file);
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

        // And it matches the in-memory view's order rather than restating it.
        Assert.Equal(
            DeclarationList.KindOrder.Select(k => k.ToString()),
            declarations.Select(d => d!.AsObject()[BlueprintSchemaV2.KindProperty]!.GetValue<string>()));
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
                BlueprintSchemaV2.Down(BlueprintSchemaV2.Up(Load(file))).ToJsonString(Indented))!;

            Assert.Equal(
                before.Declarations.Select(d => $"{d.Kind}:{d.Id}:{d.Name}:{d.Type.TypeId}"),
                after.Declarations.Select(d => $"{d.Kind}:{d.Id}:{d.Name}:{d.Type.TypeId}"));
        }
    }

    /// <summary>Up refuses a v2 document and Down refuses a v1 one — neither guesses.</summary>
    [Fact]
    public void EachDirectionRefusesTheWrongShape()
    {
        var v1 = new JsonObject { ["Parameters"] = new JsonArray() };
        var v2 = BlueprintSchemaV2.Up(v1);

        Assert.Throws<InvalidOperationException>(() => BlueprintSchemaV2.Up(v2));
        Assert.Throws<InvalidOperationException>(() => BlueprintSchemaV2.Down(v1));
    }
}
