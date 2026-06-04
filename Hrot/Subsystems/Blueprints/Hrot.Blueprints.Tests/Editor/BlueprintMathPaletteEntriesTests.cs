using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BATCH-05B tests for <see cref="BlueprintMathPaletteEntries"/>.
/// All tests are headless (no ImGui).
/// </summary>
public sealed class BlueprintMathPaletteEntriesTests
{
    private const string MathTargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath";

    // ── helpers ───────────────────────────────────────────────────────────────

    private static NodeKindRegistry MakePaletteRegistry()
        => BlueprintEditorBootstrap.CreatePaletteRegistry();

    private static IReadOnlyList<NodeKindDescriptor> AllMathDescriptors()
        => BlueprintMathPaletteEntries.All().ToList();

    private static Pin[] DataIn(IEnumerable<Pin> pins)
        => pins.Where(p => !p.IsExec && p.Direction == "In").ToArray();

    private static Pin[] DataOut(IEnumerable<Pin> pins)
        => pins.Where(p => !p.IsExec && p.Direction == "Out").ToArray();

    private static bool HasExec(IEnumerable<Pin> pins, string name, string dir)
        => pins.Any(p => p.IsExec && p.Name == name && p.Direction == dir);

    // ── 1. Descriptor shape ───────────────────────────────────────────────────

    [Fact]
    public void All_ReturnsNonEmptyCollection()
    {
        Assert.NotEmpty(AllMathDescriptors());
    }

    [Fact]
    public void All_EachDescriptor_HasUniqueKind()
    {
        var descriptors = AllMathDescriptors();
        var kinds = descriptors.Select(d => d.Kind).ToList();
        Assert.Equal(kinds.Count, kinds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_EachDescriptor_KindStartsWithMathDot()
    {
        foreach (var d in AllMathDescriptors())
            Assert.StartsWith("Math.", d.Kind, StringComparison.Ordinal);
    }

    [Fact]
    public void All_EachDescriptor_HasNonEmptyDisplayName()
    {
        foreach (var d in AllMathDescriptors())
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName),
                $"Descriptor '{d.Kind}' has empty DisplayName.");
    }

    [Fact]
    public void All_EachDescriptor_HasMathCategory()
    {
        var validCategories = new[]
        {
            BlueprintMathPaletteEntries.Categories.Math,
            BlueprintMathPaletteEntries.Categories.MathInt,
            BlueprintMathPaletteEntries.Categories.MathCompare,
            BlueprintMathPaletteEntries.Categories.MathBool,
            BlueprintMathPaletteEntries.Categories.MathVector,
        };
        foreach (var d in AllMathDescriptors())
            Assert.Contains(d.Category, validCategories);
    }

    // ── 2. CreateInstance — correct FunctionCallNode fields ───────────────────

    [Theory]
    [InlineData("Math.AddInt",   "AddInt")]
    [InlineData("Math.Add",      "Add")]
    [InlineData("Math.Clamp",    "Clamp")]
    [InlineData("Math.Dot",      "Dot")]
    [InlineData("Math.And",      "And")]
    [InlineData("Math.Normalize","Normalize")]
    public void CreateInstance_ReturnsFunctionCallNode_WithCorrectTargetTypeAndMethod(
        string kind, string expectedMethod)
    {
        var descriptor = AllMathDescriptors().Single(d => d.Kind == kind);

        var node = descriptor.CreateInstance();

        var fc = Assert.IsType<FunctionCallNode>(node);
        Assert.Equal(MathTargetTypeId, fc.TargetTypeId);
        Assert.Equal(expectedMethod, fc.MethodName);
        Assert.True(fc.IsPure, $"Kind '{kind}': IsPure must be true.");
    }

    [Fact]
    public void CreateInstance_EachCall_ReturnsFreshGuid()
    {
        var descriptor = AllMathDescriptors().Single(d => d.Kind == "Math.AddInt");

        var node1 = descriptor.CreateInstance();
        var node2 = descriptor.CreateInstance();

        Assert.NotEqual(node1.Id, node2.Id);
        Assert.NotEqual(Guid.Empty, node1.Id);
        Assert.NotEqual(Guid.Empty, node2.Id);
    }

    [Fact]
    public void CreateInstance_AllDescriptors_ReturnsFunctionCallNode_WithCorrectTargetTypeId()
    {
        foreach (var d in AllMathDescriptors())
        {
            var node = d.CreateInstance();
            var fc   = Assert.IsType<FunctionCallNode>(node);
            Assert.Equal(MathTargetTypeId, fc.TargetTypeId);
        }
    }

    [Fact]
    public void CreateInstance_AllDescriptors_IsPureTrue()
    {
        foreach (var d in AllMathDescriptors())
        {
            var node = d.CreateInstance();
            var fc   = Assert.IsType<FunctionCallNode>(node);
            Assert.True(fc.IsPure, $"Kind '{d.Kind}': IsPure must be true.");
        }
    }

    // ── 3. Category assignments ────────────────────────────────────────────────

    [Theory]
    [InlineData("Math.Add",      "Math")]
    [InlineData("Math.Subtract", "Math")]
    [InlineData("Math.Clamp",    "Math")]
    [InlineData("Math.Sin",      "Math")]
    [InlineData("Math.AddInt",   "Math/Int")]
    [InlineData("Math.ClampInt", "Math/Int")]
    [InlineData("Math.GreaterThan",  "Math/Compare")]
    [InlineData("Math.EqualsInt",    "Math/Compare")]
    [InlineData("Math.And",      "Math/Bool")]
    [InlineData("Math.Not",      "Math/Bool")]
    [InlineData("Math.Dot",      "Math/Vector")]
    [InlineData("Math.Cross",    "Math/Vector")]
    [InlineData("Math.Normalize","Math/Vector")]
    public void Descriptor_Category_MatchesExpected(string kind, string expectedCategory)
    {
        var d = AllMathDescriptors().Single(d => d.Kind == kind);
        Assert.Equal(expectedCategory, d.Category);
    }

    // ── 4. Pin projection via NodePinSchema.GetCanonicalPins ──────────────────

    [Fact]
    public void PinProjection_AddInt_TwoIntInsOneIntOut_NoExec()
    {
        var descriptor = AllMathDescriptors().Single(d => d.Kind == "Math.AddInt");
        var node = (FunctionCallNode)descriptor.CreateInstance();

        var pins = NodePinSchema.GetCanonicalPins(node);

        // Pure: no exec pins.
        Assert.False(pins.Any(p => p.IsExec), "AddInt is pure — must have no exec pins.");

        var dataIn  = DataIn(pins);
        var dataOut = DataOut(pins);

        Assert.Equal(2, dataIn.Length);
        Assert.Equal("a",            dataIn[0].Name);
        Assert.Equal("System.Int32", dataIn[0].TypeRef?.TypeId);
        Assert.Equal("b",            dataIn[1].Name);
        Assert.Equal("System.Int32", dataIn[1].TypeRef?.TypeId);

        var ret = Assert.Single(dataOut);
        Assert.Equal("Return",       ret.Name);
        Assert.Equal("System.Int32", ret.TypeRef?.TypeId);
    }

    [Fact]
    public void PinProjection_Add_Float_TwoFloatInsOneFloatOut_NoExec()
    {
        var descriptor = AllMathDescriptors().Single(d => d.Kind == "Math.Add");
        var node = (FunctionCallNode)descriptor.CreateInstance();

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.False(pins.Any(p => p.IsExec), "Add (float) is pure — must have no exec pins.");

        var dataIn  = DataIn(pins);
        var dataOut = DataOut(pins);

        Assert.Equal(2, dataIn.Length);
        Assert.Equal("a",             dataIn[0].Name);
        Assert.Equal("System.Single", dataIn[0].TypeRef?.TypeId);
        Assert.Equal("b",             dataIn[1].Name);
        Assert.Equal("System.Single", dataIn[1].TypeRef?.TypeId);

        var ret = Assert.Single(dataOut);
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Single", ret.TypeRef?.TypeId);
    }

    [Fact]
    public void PinProjection_Clamp_ThreeFloatInsOneFloatOut_NoExec()
    {
        var descriptor = AllMathDescriptors().Single(d => d.Kind == "Math.Clamp");
        var node = (FunctionCallNode)descriptor.CreateInstance();

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.False(pins.Any(p => p.IsExec), "Clamp is pure — must have no exec pins.");

        var dataIn  = DataIn(pins);
        var dataOut = DataOut(pins);

        // float Clamp(float value, float min, float max)
        Assert.Equal(3, dataIn.Length);
        Assert.Equal("value", dataIn[0].Name);
        Assert.Equal("System.Single", dataIn[0].TypeRef?.TypeId);
        Assert.Equal("min",   dataIn[1].Name);
        Assert.Equal("System.Single", dataIn[1].TypeRef?.TypeId);
        Assert.Equal("max",   dataIn[2].Name);
        Assert.Equal("System.Single", dataIn[2].TypeRef?.TypeId);

        var ret = Assert.Single(dataOut);
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Single", ret.TypeRef?.TypeId);
    }

    [Fact]
    public void PinProjection_Dot_TwoVector3InsOneFloatOut_NoExec()
    {
        var descriptor = AllMathDescriptors().Single(d => d.Kind == "Math.Dot");
        var node = (FunctionCallNode)descriptor.CreateInstance();

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.False(pins.Any(p => p.IsExec), "Dot is pure — must have no exec pins.");

        var dataIn  = DataIn(pins);
        var dataOut = DataOut(pins);

        // float Dot(Vector3 a, Vector3 b)
        Assert.Equal(2, dataIn.Length);
        Assert.Equal("a", dataIn[0].Name);
        Assert.Contains("Vector3", dataIn[0].TypeRef?.TypeId ?? "");
        Assert.Equal("b", dataIn[1].Name);
        Assert.Contains("Vector3", dataIn[1].TypeRef?.TypeId ?? "");

        var ret = Assert.Single(dataOut);
        Assert.Equal("Return",        ret.Name);
        Assert.Equal("System.Single", ret.TypeRef?.TypeId);
    }

    // ── 5. Palette registry contains math kinds ────────────────────────────────

    [Fact]
    public void PaletteRegistry_ContainsMathAddInt()
    {
        var registry = MakePaletteRegistry();
        Assert.NotNull(registry.TryGet("Math.AddInt"));
    }

    [Fact]
    public void PaletteRegistry_ContainsAllMathDescriptors()
    {
        var registry    = MakePaletteRegistry();
        var descriptors = AllMathDescriptors();

        foreach (var d in descriptors)
        {
            var found = registry.TryGet(d.Kind);
            Assert.NotNull(found);
            Assert.Equal(d.Kind, found!.Kind);
        }
    }

    [Fact]
    public void PaletteRegistry_MathKinds_AreOrderedDeterministically()
    {
        // Enumerate twice; math kinds must appear in the same order both times.
        var run1 = BlueprintMathPaletteEntries.All().Select(d => d.Kind).ToList();
        var run2 = BlueprintMathPaletteEntries.All().Select(d => d.Kind).ToList();
        Assert.Equal(run1, run2);
    }

    [Fact]
    public void PaletteRegistry_MathKinds_DoNotOverwriteBuiltinKinds()
    {
        var registry = MakePaletteRegistry();

        // Core built-in kinds that must still be present.
        foreach (var builtinKind in new[] { "Branch", "Sequence", "GetVariable", "FunctionCall" })
            Assert.NotNull(registry.TryGet(builtinKind));
    }

    // ── 6. BlueprintNodeCatalog includes math kinds ───────────────────────────

    [Fact]
    public void NodeCatalog_All_ContainsMathAddInt()
    {
        var registry = MakePaletteRegistry();
        var catalog  = new BlueprintNodeCatalog(registry);

        Assert.Contains(catalog.All, e => e.Kind.Id == "Math.AddInt");
    }

    [Fact]
    public void NodeCatalog_All_ContainsAllMathKinds()
    {
        var registry    = MakePaletteRegistry();
        var catalog     = new BlueprintNodeCatalog(registry);
        var catalogKinds = catalog.All.Select(e => e.Kind.Id).ToHashSet();

        foreach (var d in AllMathDescriptors())
            Assert.Contains(d.Kind, catalogKinds);
    }

    [Fact]
    public void NodeCatalog_Count_IncreasedByMathEntries()
    {
        // A registry WITHOUT math entries.
        var registryWithout = new NodeKindRegistry();
        registryWithout.Register(BlueprintNodePaletteEntries.All().First()); // at least 1 non-math entry

        // A registry WITH math entries (full bootstrap).
        var registryWith = MakePaletteRegistry();

        var catalogWithout = new BlueprintNodeCatalog(registryWithout);
        var catalogWith    = new BlueprintNodeCatalog(registryWith);

        Assert.True(catalogWith.All.Count > catalogWithout.All.Count,
            "Full bootstrap catalog should have more entries than a catalog with no math kinds.");
    }
}
