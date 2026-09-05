using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Xunit;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// BP-221 — an <b>AiPrimitive</b> that calls one of its own Function graphs.
///
/// <para>
/// ⭐ <b>Hand-authored, deliberately.</b> Collapse-to-Function walked into this while Batch 33 was
/// being written, but the hole is not collapse's: it is reachable by placing a second Function graph
/// and a <c>FunctionCall</c> in the editor, which is an ordinary thing to do. Proving it from a
/// hand-authored asset is what stops the fix from looking like a collapse detail — the same reason
/// Batch 31's payoff test proved <c>BP1661</c> independently of the feature that found it.
/// </para>
///
/// <para>
/// ⚠ <c>CompileResult.Succeeded</c> never invokes Roslyn, so these go through
/// <see cref="AuthoringPath.Generate"/> — the real incremental generator plus a real
/// <c>CSharpCompilation</c>.
/// </para>
/// </summary>
public sealed class AiPrimitiveFunctionHelperTests
{
    private static Pin NewPin(string name, string dir, bool isExec) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef(),
    };

    private static Link Wire(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    /// <summary>Entry → Return, nothing else. The callee.</summary>
    private static Graph EmptyFunction(string name)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var ret      = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Function,
            Nodes = { entry, ret },
            Links = { Wire(entry, entryOut, ret, retIn) },
        };
    }

    /// <summary>Entry → call(<paramref name="callee"/>) → Return.</summary>
    private static Graph CallerFunction(string name, Graph callee)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);

        var call = new FunctionCallNode
        {
            Id            = Guid.NewGuid(),
            TargetGraphId = callee.Id.ToString(),
            MethodName    = callee.Name,
        };
        var callIn  = NewPin("In",  "In",  isExec: true);
        var callOut = NewPin("Out", "Out", isExec: true);
        call.Pins.AddRange(new[] { callIn, callOut });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Function,
            Nodes = { entry, call, ret },
            Links = { Wire(entry, entryOut, call, callIn), Wire(call, callOut, ret, retIn) },
        };
    }

    private static BlueprintAsset AiPrimitiveAsset(string name, params Graph[] graphs) => new()
    {
        AssetId   = Guid.NewGuid(),
        Name      = name,
        Dispatch  = BlueprintDispatchKind.AiPrimitive,
        Primitive = new AiPrimitiveDecl
        {
            Intent   = AiPrimitiveIntent.Action,
            Hostings = new List<AiPrimitiveHosting> { AiPrimitiveHosting.BTreeAction },
        },
        Graphs = graphs.ToList(),
        Header = new Header(),
    };

    private static BlueprintAsset InstanceAsset(string name, params Graph[] graphs) => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = name,
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs   = graphs.ToList(),
        Header   = new Header(),
    };

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>BP-221.</b> <c>InstanceEmitter</c> emits a <c>Func_*</c> helper for every non-main
    /// Function graph; <c>AiPrimitiveEmitter</c> picked its main graph the same way and had no such
    /// loop, while <c>StatementEmitter</c> emitted the call regardless — so the generated class
    /// referenced a method that was never written.
    /// </summary>
    [Fact]
    public void AiPrimitive_CallingItsOwnFunctionGraph_Compiles()
    {
        var callee = EmptyFunction("Helper");
        var tick   = CallerFunction("Tick", callee);

        var asset  = AiPrimitiveAsset("AiPrimCallsOwnFunction", tick, callee);
        var result = AuthoringPath.Generate(asset);

        Assert.True(result.Clean,
            "An AiPrimitive that calls one of its own Function graphs must produce C# that really "
            + "compiles.\n" + result.Report());
    }

    /// <summary>
    /// ⭐ The helper must actually be <b>emitted</b>, not merely not-referenced: an emitter that
    /// dropped the call site instead of writing the method would also "compile clean", and would
    /// silently lose the author's function.
    /// </summary>
    [Fact]
    public void AiPrimitive_CallingItsOwnFunctionGraph_EmitsTheHelperAndCallsIt()
    {
        var callee = EmptyFunction("Helper");
        var tick   = CallerFunction("Tick", callee);

        var result = AuthoringPath.Generate(AiPrimitiveAsset("AiPrimEmitsHelper", tick, callee));

        Assert.True(result.Clean, result.Report());
        Assert.Contains(result.GeneratedSources, src => src.Contains("Func_Helper("));
        // …and referenced from the main body, not just declared.
        Assert.Contains(result.GeneratedSources,
            src => src.Split("Func_Helper(").Length >= 3);
    }

    /// <summary>
    /// The Instance path already worked — kept beside the AiPrimitive case so a future change that
    /// unifies the two emitters cannot fix one by breaking the other.
    /// </summary>
    [Fact]
    public void Instance_CallingItsOwnFunctionGraph_StillCompiles()
    {
        var callee = EmptyFunction("Helper");
        var tick   = CallerFunction("Tick", callee);

        var result = AuthoringPath.Generate(InstanceAsset("InstanceCallsOwnFunction", tick, callee));

        Assert.True(result.Clean, result.Report());
    }
}
