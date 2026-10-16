using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// CE-1006 — a Set Variable node's unwired <c>Value</c> input gets an inline editor whenever the variable's type has a registered
/// pin editor: the canvas draws one for every input pin whose model carries a Default (<c>NodeRenderer</c>), and
/// <see cref="BlueprintPinModel"/> exposes one exactly when the registry knows the type. The framework registry lacked every numeric
/// width but int and float; <see cref="WidenedPinEditor"/> now covers them. Still without one: <c>Entity</c>, structs, and a
/// variable id that does not resolve (the pin falls back to <c>System.Object</c>).
/// </summary>
public sealed class SetVariableValueEditorTests
{
    private static IPinModel ValueIn(string typeId, string? variableId = null)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "A" };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "EventGraph", Kind = GraphKind.Event };
        asset.Graphs.Add(graph);
        var varId = Guid.NewGuid();
        asset.Variables.Add(new VariableDecl { Id = varId, Name = "V", Type = new BlueprintTypeRef { TypeId = typeId } });
        graph.Nodes.Add(new SetVariableNode { Id = Guid.NewGuid(), VariableId = variableId ?? $"var:{varId}" });
        // the PRODUCTION registry (BlueprintDocumentFactory's own builder), not the framework built-ins alone
        var model = new BlueprintGraphModel(asset, graph, new NodeKindRegistry(),
            editorRegistry: BlueprintDocumentFactory.CreatePinEditorRegistry(new BlueprintEnumValueProvider()));
        return model.Nodes.Single().Pins.Single(p => p.Kind == PinKind.Data && p.Direction == PinDirection.Input);
    }

    [Theory]
    [InlineData("System.Single")]
    [InlineData("System.Int32")]
    [InlineData("System.Boolean")]
    [InlineData("System.Numerics.Vector3")]
    [InlineData("System.Double")]
    [InlineData("System.Int64")]
    [InlineData("System.Int16")]
    [InlineData("System.SByte")]
    [InlineData("System.Byte")]
    [InlineData("System.UInt16")]
    [InlineData("System.UInt32")]
    [InlineData("System.UInt64")]
    public void CE1006_ARegisteredType_GetsAnInlineEditor(string typeId)
    {
        var pin = ValueIn(typeId);
        Assert.Equal(typeId, pin.Type?.Id);
        Assert.NotNull(pin.Default);
    }

    [Fact]
    public void CE1006_AnUnresolvedVariable_FallsBackToObject_WithNoEditor()
    {
        var pin = ValueIn("System.Single", variableId: "NotAGuid");
        Assert.Equal("System.Object", pin.Type?.Id);
        Assert.Null(pin.Default);
    }

    [Theory]
    [InlineData("System.Double", "2.5", typeof(double))]
    [InlineData("System.Int64", "9000000000", typeof(long))]
    [InlineData("System.Int16", "-7", typeof(short))]
    [InlineData("System.UInt64", "12", typeof(ulong))]
    public void CE1006_AWidenedDefault_KeepsItsOwnWidth_AndRoundTrips(string typeId, string raw, Type clr)
    {
        var parsed = BlueprintPinDefaultValue.ParseValue(typeId, raw);
        Assert.IsType(clr, parsed);
        Assert.Equal(raw, BlueprintPinDefaultValue.FormatValue(parsed));
    }
}
