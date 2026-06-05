using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Headless tests for the Blueprint inline pin-default-value editor wiring (BF-BATCH-07).
/// No ImGui context required — tests cover:
///   (a) <see cref="BlueprintPinDefaultValue"/> parse/format round-trips,
///   (b) <see cref="BlueprintPinModel.Default"/> returns non-null for persisted defaults,
///   (c) <see cref="BlueprintCommandSink.Apply"/> SetPinDefault writes back to the model and asset.
/// NOTE: Visual/ImGui rendering requires running-editor verification; headless gates cannot
/// exercise the ImGui widget loop.
/// </summary>
public sealed class BlueprintPinDefaultValueTests
{
    // ── BlueprintPinDefaultValue parse / format round-trips ───────────────────

    [Theory]
    [InlineData("System.Boolean", "true",  true)]
    [InlineData("System.Boolean", "false", false)]
    [InlineData("System.Int32",   "42",    42)]
    [InlineData("System.Single",  "3.14",  3.14f)]
    [InlineData("System.Double",  "1.5",   1.5)]
    [InlineData("System.Byte",    "255",   (byte)255)]
    [InlineData("System.UInt32",  "100",   (uint)100)]
    [InlineData("System.String",  "hello", "hello")]
    public void ParseValue_Returns_CorrectType(string typeId, string raw, object expected)
    {
        var result = BlueprintPinDefaultValue.ParseValue(typeId, raw);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseValue_Bool_False_WhenBadInput()
    {
        var result = BlueprintPinDefaultValue.ParseValue("System.Boolean", "notabool");
        Assert.Equal(false, result);
    }

    [Fact]
    public void ParseValue_Int_Zero_WhenBadInput()
    {
        var result = BlueprintPinDefaultValue.ParseValue("System.Int32", "notanint");
        Assert.Equal(0, result);
    }

    [Fact]
    public void ParseValue_Float_Zero_WhenBadInput()
    {
        var result = BlueprintPinDefaultValue.ParseValue("System.Single", "notafloat");
        Assert.Equal(0f, result);
    }

    [Fact]
    public void ParseValue_UnknownType_ReturnsRawString()
    {
        var result = BlueprintPinDefaultValue.ParseValue("Some.Unknown.Type", "rawval");
        Assert.Equal("rawval", result);
    }

    [Theory]
    [InlineData(true,   "true")]
    [InlineData(false,  "false")]
    [InlineData(42,     "42")]
    [InlineData(3.14f,  "3.14")]
    [InlineData("hi",   "hi")]
    public void FormatValue_Returns_CorrectString(object value, string expected)
    {
        var result = BlueprintPinDefaultValue.FormatValue(value);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatValue_Null_ReturnsNull()
    {
        Assert.Null(BlueprintPinDefaultValue.FormatValue(null));
    }

    // ── BlueprintPinModel.Default ─────────────────────────────────────────────

    [Fact]
    public void PinModel_Default_IsNull_WhenNoDefaultValue()
    {
        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "count",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Int32" },
            // DefaultValue not set → null
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId);

        Assert.Null(model.Default);
    }

    [Fact]
    public void PinModel_Default_IsNonNull_WhenDefaultValueIsSet()
    {
        var pin = new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "count",
            Direction    = "In",
            IsExec       = false,
            TypeRef      = new BlueprintTypeRef { TypeId = "System.Int32" },
            DefaultValue = "7",
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId);

        Assert.NotNull(model.Default);
        Assert.Equal(7, model.Default!.Value);
    }

    [Fact]
    public void PinModel_Default_IsNull_ForOutputPin()
    {
        var pin = new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "result",
            Direction    = "Out",
            IsExec       = false,
            TypeRef      = new BlueprintTypeRef { TypeId = "System.Int32" },
            DefaultValue = "7",
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId);

        // Output pins must not expose a default editor widget.
        Assert.Null(model.Default);
    }

    [Fact]
    public void PinModel_Default_IsNull_ForExecPin()
    {
        var pin = new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "exec",
            Direction    = "In",
            IsExec       = true,
            TypeRef      = new BlueprintTypeRef { TypeId = "" },
            DefaultValue = "whatever",
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId);

        Assert.Null(model.Default);
    }

    // ── SetPinDefault round-trip via BlueprintCommandSink ─────────────────────

    [Fact]
    public void SetPinDefault_WritesValue_To_NodePinDefaults()
    {
        // Arrange: build a minimal asset with one FunctionCallNode that has a typed input pin.
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "TestAsset",
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Event };
        asset.Graphs.Add(graph);

        var node = new FunctionCallNode
        {
            Id           = Guid.NewGuid(),
            MethodName   = "Dummy",
            TargetTypeId = "Foo",
        };
        graph.Nodes.Add(node);

        // Give the node a persisted pin so fast-path is used (pins.Count > 0).
        var inputPin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "speed",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Single" },
        };
        node.Pins.Add(inputPin);

        var typeSystem   = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model        = new BlueprintGraphModel(asset, graph);
        var catalog      = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator    = new BlueprintLinkValidator(model, typeSystem);
        var history      = new CommandHistory();
        var dirtyLog     = new List<BlueprintAsset>();
        var editService  = new EditService
        {
            Context = new EditServiceContext(history, a => dirtyLog.Add(a))
        };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history,
            editService, markDirty: a => dirtyLog.Add(a));

        // Locate the resolved PinId from the rebuilt model.
        var pinId = model.FindPin(new PinId(inputPin.Id))?.Id
                    ?? throw new InvalidOperationException("Pin not found in model.");

        // Act: apply SetPinDefault.
        var cmd    = new GraphCommand.SetPinDefault(pinId, 42.5f);
        var result = sink.Apply(cmd);

        // Assert: success, node.PinDefaults contains the formatted value.
        Assert.True(result.Success, result.Message);
        Assert.NotNull(node.PinDefaults);
        Assert.True(node.PinDefaults!.TryGetValue("speed", out var stored));
        Assert.Equal("42.5", stored);

        // After rebuild the model pin's Default reflects the new value.
        var updatedPin = model.FindPin(new PinId(inputPin.Id));
        // After rebuild, pin ID may change (deterministic GUID). Find by name.
        var allPins = model.Nodes
            .SelectMany(n => n.Pins)
            .FirstOrDefault(p => p.Label == "speed");
        Assert.NotNull(allPins?.Default);
        Assert.Equal(42.5f, allPins!.Default!.Value);

        // Also verify the document was marked dirty.
        Assert.NotEmpty(dirtyLog);
    }

    [Fact]
    public void SetPinDefault_ClearsValue_WhenNewValueIsNull()
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "ClearTest" };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Event };
        asset.Graphs.Add(graph);

        var node = new FunctionCallNode
        {
            Id           = Guid.NewGuid(),
            MethodName   = "Dummy",
            TargetTypeId = "Foo",
            PinDefaults  = new Dictionary<string, string> { ["count"] = "5" },
        };
        graph.Nodes.Add(node);

        var inputPin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "count",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Int32" },
        };
        node.Pins.Add(inputPin);

        var typeSystem  = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model       = new BlueprintGraphModel(asset, graph);
        var catalog     = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator   = new BlueprintLinkValidator(model, typeSystem);
        var history     = new CommandHistory();
        var editService = new EditService
        {
            Context = new EditServiceContext(history, a => { })
        };
        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history,
            editService, markDirty: _ => { });

        var pinId = new PinId(inputPin.Id);
        var cmd   = new GraphCommand.SetPinDefault(pinId, null);
        var result = sink.Apply(cmd);

        Assert.True(result.Success, result.Message);
        // PinDefaults should be null or empty after clearing the only entry.
        Assert.True(node.PinDefaults == null || node.PinDefaults.Count == 0);
    }
}
