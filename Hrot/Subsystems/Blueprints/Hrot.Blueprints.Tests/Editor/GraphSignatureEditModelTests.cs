using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BATCH-03D2 — headless tests for <see cref="GraphSignatureEditModel"/>.
///
/// Each test verifies real behaviour: the exact list state after a mutation
/// and that <c>onChanged</c> fires exactly once per operation.
/// </summary>
public sealed class GraphSignatureEditModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Graph MakeFunctionGraph() => new()
    {
        Id   = Guid.NewGuid(),
        Name = "TestFunc",
        Kind = GraphKind.Function,
    };

    private static (Graph graph, GraphSignatureEditModel model, List<int> spy)
        MakeInputsModel()
    {
        var graph = MakeFunctionGraph();
        var spy   = new List<int>();
        var model = new GraphSignatureEditModel(graph, isOutputs: false, () => spy.Add(1));
        return (graph, model, spy);
    }

    private static (Graph graph, GraphSignatureEditModel model, List<int> spy)
        MakeOutputsModel()
    {
        var graph = MakeFunctionGraph();
        var spy   = new List<int>();
        var model = new GraphSignatureEditModel(graph, isOutputs: true, () => spy.Add(1));
        return (graph, model, spy);
    }

    // ── AddParameter — Inputs ────────────────────────────────────────────────

    [Fact]
    public void Add_ToInputs_AppendsParameterDecl_WithCorrectNameAndTypeId()
    {
        var (graph, model, spy) = MakeInputsModel();

        model.AddParameter("damage", "System.Single");

        Assert.Single(graph.Inputs);
        Assert.Equal("damage",        graph.Inputs[0].Name);
        Assert.Equal("System.Single", graph.Inputs[0].Type.TypeId);
        Assert.Empty(graph.Outputs); // Outputs untouched
    }

    [Fact]
    public void Add_ToInputs_FiresOnChangedExactlyOnce()
    {
        var (_, model, spy) = MakeInputsModel();

        model.AddParameter("x", "System.Int32");

        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Add_ToInputs_AssignsNonEmptyGuid()
    {
        var (graph, model, _) = MakeInputsModel();

        model.AddParameter("value", "System.Boolean");

        Assert.NotEqual(Guid.Empty, graph.Inputs[0].Id);
    }

    // ── AddParameter — Outputs ───────────────────────────────────────────────

    [Fact]
    public void Add_ToOutputs_AppendsParameterDecl_WithCorrectNameAndTypeId()
    {
        var (graph, model, spy) = MakeOutputsModel();

        model.AddParameter("result", "System.Int32");

        Assert.Single(graph.Outputs);
        Assert.Equal("result",      graph.Outputs[0].Name);
        Assert.Equal("System.Int32", graph.Outputs[0].Type.TypeId);
        Assert.Empty(graph.Inputs); // Inputs untouched
    }

    [Fact]
    public void Add_ToOutputs_FiresOnChangedExactlyOnce()
    {
        var (_, model, spy) = MakeOutputsModel();

        model.AddParameter("r", "System.Single");

        Assert.Equal(1, spy.Count);
    }

    // ── RemoveParameter ──────────────────────────────────────────────────────

    [Fact]
    public void Remove_FromInputs_RemovesMatchingParam_AndFiresOnce()
    {
        var (graph, model, spy) = MakeInputsModel();
        model.AddParameter("a", "System.Int32");
        model.AddParameter("b", "System.Single");
        spy.Clear(); // reset spy after adds

        model.RemoveParameter("a");

        Assert.Single(graph.Inputs);
        Assert.Equal("b", graph.Inputs[0].Name);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Remove_FromOutputs_RemovesMatchingParam_AndFiresOnce()
    {
        var (graph, model, spy) = MakeOutputsModel();
        model.AddParameter("r", "System.Boolean");
        spy.Clear();

        model.RemoveParameter("r");

        Assert.Empty(graph.Outputs);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Remove_NameNotFound_DoesNotFireOnChanged()
    {
        var (_, model, spy) = MakeInputsModel();
        model.AddParameter("x", "System.Int32");
        spy.Clear();

        model.RemoveParameter("nonexistent");

        Assert.Equal(0, spy.Count);
    }

    // ── RenameParameter ──────────────────────────────────────────────────────

    [Fact]
    public void Rename_Inputs_ChangesName_AndFiresOnce()
    {
        var (graph, model, spy) = MakeInputsModel();
        model.AddParameter("oldName", "System.Single");
        spy.Clear();

        model.RenameParameter("oldName", "newName");

        Assert.Equal("newName", graph.Inputs[0].Name);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Rename_Outputs_ChangesName_AndFiresOnce()
    {
        var (graph, model, spy) = MakeOutputsModel();
        model.AddParameter("result", "System.Int32");
        spy.Clear();

        model.RenameParameter("result", "returnValue");

        Assert.Equal("returnValue", graph.Outputs[0].Name);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Rename_NameNotFound_DoesNotFireOnChanged()
    {
        var (_, model, spy) = MakeInputsModel();
        spy.Clear();

        model.RenameParameter("ghost", "newName");

        Assert.Equal(0, spy.Count);
    }

    // ── RetypeParameter ──────────────────────────────────────────────────────

    [Fact]
    public void Retype_Inputs_ChangesTypeId_AndFiresOnce()
    {
        var (graph, model, spy) = MakeInputsModel();
        model.AddParameter("speed", "System.Single");
        spy.Clear();

        model.RetypeParameter("speed", "System.Double");

        Assert.Equal("System.Double", graph.Inputs[0].Type.TypeId);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Retype_Outputs_ChangesTypeId_AndFiresOnce()
    {
        var (graph, model, spy) = MakeOutputsModel();
        model.AddParameter("out", "System.Boolean");
        spy.Clear();

        model.RetypeParameter("out", "System.Int32");

        Assert.Equal("System.Int32", graph.Outputs[0].Type.TypeId);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void Retype_NameNotFound_DoesNotFireOnChanged()
    {
        var (_, model, spy) = MakeInputsModel();
        spy.Clear();

        model.RetypeParameter("ghost", "System.Int32");

        Assert.Equal(0, spy.Count);
    }

    // ── MoveParameter ────────────────────────────────────────────────────────

    [Fact]
    public void Move_Inputs_ReordersParams_AndFiresOnce()
    {
        var (graph, model, spy) = MakeInputsModel();
        model.AddParameter("first",  "System.Int32");
        model.AddParameter("second", "System.Single");
        spy.Clear();

        model.MoveParameter(0, 1); // move "first" to position 1

        Assert.Equal("second", graph.Inputs[0].Name);
        Assert.Equal("first",  graph.Inputs[1].Name);
        Assert.Equal(1, spy.Count);
    }

    // ── Round-trip: AddParameter → NodePinSchema projects a matching data-OUT ─

    /// <summary>
    /// After <see cref="GraphSignatureEditModel.AddParameter"/> on Graph.Inputs,
    /// calling <see cref="NodePinSchema.GetCanonicalPins"/> on an
    /// <see cref="EventEntryNode"/> in that Function graph yields a data-OUT pin
    /// with matching Name and TypeId — proving that signature edits drive pins
    /// (BATCH-03C contract).
    /// </summary>
    [Fact]
    public void AddInputParameter_ThenNodePinSchema_ProjectsMatchingDataOutPin()
    {
        var graph = MakeFunctionGraph();
        var spy   = new List<int>();
        var model = new GraphSignatureEditModel(graph, isOutputs: false, () => spy.Add(1));

        model.AddParameter("damage", "System.Single");

        var entryNode = new EventEntryNode();
        var pins = NodePinSchema.GetCanonicalPins(entryNode, containingGraph: graph);

        // exec-Out + one data-Out per Graph.Inputs entry
        var dataOuts = pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        Assert.Single(dataOuts);
        Assert.Equal("damage",        dataOuts[0].Name);
        Assert.Equal("System.Single", dataOuts[0].TypeRef.TypeId);
    }
}
