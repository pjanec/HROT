using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐ <c>CE-470</c> — <i>Get Sim Time</i> / <i>Get Delta Time</i> (<see cref="GetTimeNode"/>). 📄
/// <c>docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md</c> §8 (the scope table these rails pin).
/// </summary>
public class CE470_GetTimeNodeTests
{
    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>
    /// One graph reading the clock into a sink that each dispatch ACCEPTS — so a refusal can only be <c>BP1679</c>,
    /// never a fixture defect: an Instance/Behavior <b>variable</b>, an AiPrimitive <b>working-state field</b> (with the
    /// required <c>primitive</c> block), a Library function's <b>return value</b> (Library declares no asset state).
    /// </summary>
    internal static BlueprintAsset Build(TimeKind kind, BlueprintDispatchKind dispatch = BlueprintDispatchKind.Instance,
                                         GraphKind graphKind = GraphKind.Function)
    {
        var get = new GetTimeNode { Id = Guid.NewGuid(), Kind = kind };
        var valuePin = DataPin("Value", "Out", "System.Single");
        get.Pins.Add(valuePin);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = graphKind == GraphKind.Function ? "Main" : "OnPing", Kind = graphKind,
            Nodes = { entry, get, ret },
        };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = $"GetTime{kind}{dispatch}", Dispatch = dispatch, Graphs = { graph } };

        if (dispatch == BlueprintDispatchKind.Library)
        {
            graph.Outputs.Add(new ParameterDecl { Name = "T", Type = new BlueprintTypeRef { TypeId = "System.Single" } });
            var retValue = DataPin("T", "In", "System.Single");
            ret.Pins.Add(retValue);
            graph.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id });
            graph.Links.Add(new Link { FromNodeId = get.Id, FromPinId = valuePin.Id, ToNodeId = ret.Id, ToPinId = retValue.Id });
            return asset;
        }

        var sinkId = Guid.NewGuid();
        var sink = new VariableDecl { Id = sinkId, Name = "Out", Type = new BlueprintTypeRef { TypeId = "System.Single" } };
        if (dispatch == BlueprintDispatchKind.AiPrimitive)
        {
            asset.Primitive = new AiPrimitiveDecl { Intent = AiPrimitiveIntent.Action, Hostings = new List<AiPrimitiveHosting> { AiPrimitiveHosting.BlueprintCall } };
            asset.WorkingState.Add(sink);
        }
        else
        {
            asset.Variables.Add(sink);
        }

        var setIn = ExecPin("ExecIn", "In");
        var setOut = ExecPin("ExecOut", "Out");
        var setValue = DataPin("Value", "In", "System.Single");
        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = sinkId.ToString() };
        set.Pins.AddRange(new[] { setIn, setOut, setValue });
        graph.Nodes.Add(set);
        graph.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = set.Id, ToPinId = setIn.Id });
        graph.Links.Add(new Link { FromNodeId = set.Id,   FromPinId = setOut.Id,   ToNodeId = ret.Id, ToPinId = retIn.Id });
        graph.Links.Add(new Link { FromNodeId = get.Id,   FromPinId = valuePin.Id, ToNodeId = set.Id, ToPinId = setValue.Id });
        return asset;
    }

    private static string Diags(CompileResult r) => string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    [Theory]
    [InlineData(TimeKind.SimTime,   "= time;")]
    [InlineData(TimeKind.DeltaTime, "= deltaTime;")]
    public void GetTime_InAnInstanceFunctionGraph_ReadsTheClock_AndSurvivesTheRealCSharpCompiler(TimeKind kind, string emitted)
    {
        var asset = Build(kind);
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));
        Assert.Contains(emitted, result.GeneratedSource!);

        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset, GoldenCorpus.Options());
    }

    [Fact]
    public void GetSimTime_InAnAiPrimitive_IsAllowed()
    {
        var result = new BlueprintCompiler().Compile(Build(TimeKind.SimTime, BlueprintDispatchKind.AiPrimitive), GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));   // ⚠ a fixture defect must not pass as "no BP1679"
        Assert.Contains("= time;", result.GeneratedSource!);
    }

    /// <summary>The Q78 §8 scope table: every cell where the clock is NOT a parameter of the emitted method.</summary>
    [Theory]
    [CoversDiagnosticCode("BP1679")]
    [InlineData(TimeKind.DeltaTime, BlueprintDispatchKind.AiPrimitive, GraphKind.Function)]
    [InlineData(TimeKind.DeltaTime, BlueprintDispatchKind.Instance,    GraphKind.Event)]
    [InlineData(TimeKind.DeltaTime, BlueprintDispatchKind.Library,     GraphKind.Function)]
    [InlineData(TimeKind.SimTime,   BlueprintDispatchKind.Library,     GraphKind.Function)]
    public void GetTime_WhereTheClockIsNotInScope_IsRefusedByTheCompilerNotByRoslyn(
        TimeKind kind, BlueprintDispatchKind dispatch, GraphKind graphKind)
    {
        var result = new BlueprintCompiler().Compile(Build(kind, dispatch, graphKind), GoldenCorpus.Options());
        Assert.True(result.Diagnostics.Any(d => d.Code == "BP1679"), Diags(result));
        // ⭐ the fixture itself is valid: BP1679 is the ONLY error, so it cannot pass for a different reason.
        Assert.All(result.Diagnostics.Where(d => d.IsError), d => Assert.Equal("BP1679", d.Code));
    }
}
