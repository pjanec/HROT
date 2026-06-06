using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using NodeEditor.UI.MiniEditors;
using Xunit;
using DiagnosticSeverity = Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSeverity;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Headless tests for Fdp.Core.FixedString32 / FixedString64 blueprint pin type support
/// (BF-BATCH-FIXEDSTRING).
/// Coverage:
///   (a) StaticTypeRegistry resolves both FixedString TypeIds → IsUnmanaged=true, correct SizeBytes.
///   (b) PinDefaultValueEditorRegistry returns a non-null editor for both FixedString TypeKeys
///       when registered host-side (simulating BlueprintDocumentFactory.Build wiring).
///   (c) BlueprintPinDefaultValue.ParseValue round-trips for both TypeIds (null → "").
///   (d) The EditorTypesDemo recipe deserializes, the FixedString32 pin model exposes a non-null
///       Default when the editor registry is supplied, and the recipe compiles without errors.
/// </summary>
public sealed class FixedStringPinTests
{
    // ── (a) StaticTypeRegistry ────────────────────────────────────────────────

    [Theory]
    [InlineData("Fdp.Core.FixedString32", 32)]
    [InlineData("Fdp.Core.FixedString64", 64)]
    public void StaticTypeRegistry_Resolves_FixedStringTypes(string typeId, int expectedSize)
    {
        var typeRef = new Hrot.Blueprints.Core.Assets.BlueprintTypeRef { TypeId = typeId };
        var resolved = StaticTypeRegistry.Instance.TryResolve(typeRef, out var irType);

        Assert.True(resolved, $"StaticTypeRegistry should resolve '{typeId}'");
        Assert.True(irType.IsUnmanaged, $"'{typeId}' should be unmanaged");
        Assert.Equal(expectedSize, irType.SizeBytes);
    }

    // ── (b) Editor registry wiring (mirrors BlueprintDocumentFactory.Build) ───

    [Theory]
    [InlineData("Fdp.Core.FixedString32")]
    [InlineData("Fdp.Core.FixedString64")]
    public void EditorRegistry_ReturnsNonNullEditor_ForFixedStringTypes(string typeId)
    {
        // Simulate the host-side wiring done in BlueprintDocumentFactory.Build.
        var registry = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        registry.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
        registry.Register(new TypeKey(BlueprintTypeSystem.FixedString64), new StringPinEditor());

        var editor = registry.GetEditor(new TypeKey(typeId));

        Assert.NotNull(editor);
        Assert.IsType<StringPinEditor>(editor);
    }

    // ── (c) BlueprintPinDefaultValue.ParseValue round-trips ──────────────────

    [Theory]
    [InlineData("Fdp.Core.FixedString32")]
    [InlineData("Fdp.Core.FixedString64")]
    public void ParseValue_NullOrEmpty_ReturnsEmptyString(string typeId)
    {
        var resultNull  = BlueprintPinDefaultValue.ParseValue(typeId, null);
        var resultEmpty = BlueprintPinDefaultValue.ParseValue(typeId, "");

        Assert.Equal("", resultNull);
        Assert.Equal("", resultEmpty);
    }

    [Theory]
    [InlineData("Fdp.Core.FixedString32", "hello")]
    [InlineData("Fdp.Core.FixedString64", "a longer label")]
    public void ParseValue_NonEmpty_ReturnsRawString(string typeId, string raw)
    {
        var result = BlueprintPinDefaultValue.ParseValue(typeId, raw);
        Assert.Equal(raw, result);
    }

    // ── (d) EditorTypesDemo recipe: deserialize + FixedString pin model + compile ──

    [Fact]
    public void EditorTypesDemo_DeserializesAndContainsFixedString32Pin()
    {
        var asset = LoadEditorTypesDemo();

        // The recipe must contain a node with a FixedString32 In-data pin.
        var allPins = asset.Graphs
            .SelectMany(g => g.Nodes)
            .SelectMany(n => n.Pins)
            .ToList();

        var fixedStringPin = allPins.FirstOrDefault(p =>
            p.TypeRef?.TypeId == "Fdp.Core.FixedString32" && p.Direction == "In" && !p.IsExec);

        Assert.NotNull(fixedStringPin);
    }

    [Fact]
    public void EditorTypesDemo_FixedStringPin_DefaultIsNonNull_WhenRegistrySupplied()
    {
        var asset = LoadEditorTypesDemo();

        // Simulate host-side wiring.
        var registry = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        registry.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
        registry.Register(new TypeKey(BlueprintTypeSystem.FixedString64), new StringPinEditor());

        // Find the FixedString32 pin in the asset.
        var graph = asset.Graphs.First();
        var fixedStringNode = graph.Nodes
            .FirstOrDefault(n => n.Pins.Any(p =>
                p.TypeRef?.TypeId == "Fdp.Core.FixedString32" &&
                p.Direction == "In" && !p.IsExec));
        Assert.NotNull(fixedStringNode);

        var rawPin = fixedStringNode!.Pins.First(p =>
            p.TypeRef?.TypeId == "Fdp.Core.FixedString32" &&
            p.Direction == "In" && !p.IsExec);

        var pinModel = new BlueprintPinModel(rawPin, new NodeId(fixedStringNode.Id), registry);

        // With the registry wired, Default must be non-null (shows type-zero editor).
        Assert.NotNull(pinModel.Default);
        Assert.Equal("", pinModel.Default!.Value);
    }

    [Fact]
    public void EditorTypesDemo_CompilesWithNoErrors()
    {
        var asset = LoadEditorTypesDemo();
        var opts  = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var result = new BlueprintCompiler().Compile(asset, opts);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintAsset LoadEditorTypesDemo()
    {
        // Try production location first (Hrot.AI.Behaviors assembly output).
        var aiBehaviorsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");

        string path;
        if (aiBehaviorsAssembly != null)
        {
            var dir = Path.GetDirectoryName(aiBehaviorsAssembly.Location)
                ?? throw new InvalidOperationException("Cannot determine Hrot.AI.Behaviors location.");
            path = Path.Combine(dir, "Blueprints", "Recipes", "EditorTypesDemo.bp.json");
        }
        else
        {
            // Fallback: TestAssets location.
            var dir = TestData.ResolveTestAssetsDir();
            path = Path.Combine(dir, "Recipes", "EditorTypesDemo.bp.json");
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"EditorTypesDemo.bp.json not found at: {path}");

        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException("Deserialized null from EditorTypesDemo.bp.json");
    }
}
