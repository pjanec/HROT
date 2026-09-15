using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <c>BP1670</c> — a Get/SetVariable that targets nothing.
///
/// <para>
/// ⚠⚠ <b>The failure this replaces was silent at the blueprint level and loud in the wrong place.</b>
/// Both index lookups missed, returned -1, and the emitter wrote <c>s.__var_-1 = __t0;</c> — not a C#
/// identifier. The blueprint compile reported <b>no</b> diagnostic; the SOLUTION build then failed with
/// a <c>CS</c> error pointing at a generated file, naming neither the graph nor the node.
/// </para>
/// </summary>
public sealed class DanglingVariableReferenceTests
{
    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin P(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef { TypeId = typeId },
    };

    private static Link W(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    /// <summary>Entry → Set(<paramref name="variableId"/>, 7) → Return.</summary>
    private static Graph GraphWriting(string variableId)
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        var eOut  = P("Out", "Out", true); entry.Pins.Add(eOut);

        var lit  = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "7" };
        var lOut = P("Value", "Out", false, "System.Int32"); lit.Pins.Add(lOut);

        var set  = new SetVariableNode { Id = Guid.NewGuid(), VariableId = variableId };
        var sIn  = P("In",  "In",  true);
        var sOut = P("Out", "Out", true);
        var sVal = P("Value", "In", false, "System.Int32");
        set.Pins.AddRange(new[] { sIn, sOut, sVal });

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var rIn = P("In", "In", true); ret.Pins.Add(rIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, lit, set, ret },
            Links = { W(entry, eOut, set, sIn), W(lit, lOut, set, sVal), W(set, sOut, ret, rIn) },
        };
    }

    private static BlueprintAsset Instance(string name, params Graph[] graphs) => new()
    {
        AssetId  = Guid.NewGuid(), Name = name,
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs   = graphs.ToList(), Header = new Header(),
    };

    private static CompileResult Compile(BlueprintAsset asset)
        => new BlueprintCompiler().Compile(asset, DefaultOptions());

    /// <summary>
    /// ⭐ A well-formed GUID matching nothing — the shape a deleted variable leaves behind.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1670")]
    public void AGetSetTargetingNothing_IsRefused()
    {
        var result = Compile(Instance("Dangling", GraphWriting(Guid.NewGuid().ToString())));

        Assert.Contains(DiagnosticCodes.BP1670, result.Diagnostics.Select(d => d.Code));
        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// ⭐⭐ <b>The assertion that matters more than the diagnostic's presence: the invalid identifier
    /// is gone.</b> A rail that fired but still emitted <c>__var_-1</c> would leave the solution build
    /// broken for anyone who compiled past the error.
    /// </summary>
    [Fact]
    public void ADanglingReference_NoLongerEmitsAnInvalidIdentifier()
    {
        var result = Compile(Instance("DanglingEmit", GraphWriting(Guid.NewGuid().ToString())));
        Assert.DoesNotContain("__var_-1", result.GeneratedSource ?? "");
    }

    /// <summary>
    /// ⭐ <b>A local IS a resolution target.</b> The rail mirrors Stage 5's order — this graph's locals
    /// by id first — so a node aimed at a local must not be refused as dangling.
    /// </summary>
    [Fact]
    public void AReferenceToThisGraphsLocal_IsNotRefused()
    {
        var local = new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Scratch",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
            DefaultValueJson = "0",
        };
        var graph = GraphWriting(local.Id.ToString());
        graph.LocalVariables.Add(local);

        var result = Compile(Instance("LocalTarget", graph));

        Assert.DoesNotContain(DiagnosticCodes.BP1670, result.Diagnostics.Select(d => d.Code));
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
    }

    /// <summary>
    /// ⭐⭐ <b>The false-positive guard, and it is not hypothetical.</b> A <c>VariableId</c> is not
    /// always a GUID — <c>Stage5.FindVariableIndex</c> resolves a bare NAME against the asset's lists,
    /// and Batch 37's shadowing guard depends on that path. A rail that accepted only GUIDs would
    /// refuse graphs that compile correctly today.
    /// </summary>
    [Fact]
    public void AReferenceByNameToAnAssetVariable_IsNotRefused()
    {
        var assetVar = new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Ammo",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };
        var asset = Instance("NameRef", GraphWriting("Ammo"));
        asset.Variables.Add(assetVar);

        var result = Compile(asset);

        Assert.DoesNotContain(DiagnosticCodes.BP1670, result.Diagnostics.Select(d => d.Code));
    }

    /// <summary>
    /// ⭐⭐ <b>The scope guard — the one that would have failed six shipped assets.</b>
    /// <c>GetShared</c>/<c>SetShared</c> also carry a <c>VariableId</c>, but it is a name-keyed
    /// shared-state slot resolved at runtime, never through <c>FindVariableIndex</c>. The shipped
    /// corpus holds 61 such references (the literals <c>"state"</c> and <c>"rally"</c>), and a rail
    /// generalised to "any node with a VariableId" would reject every one of them.
    /// </summary>
    [Fact]
    public void ASharedStateReference_IsNotTouchedByThisRail()
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        var eOut  = P("Out", "Out", true); entry.Pins.Add(eOut);

        var lit  = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "7" };
        var lOut = P("Value", "Out", false, "System.Int32"); lit.Pins.Add(lOut);

        var set = new SetSharedNode
        {
            Id = Guid.NewGuid(), VariableId = "state", SharedTypeId = "System.Int32",
        };
        var sIn  = P("In",  "In",  true);
        var sOut = P("Out", "Out", true);
        var sVal = P("Value", "In", false, "System.Int32");
        set.Pins.AddRange(new[] { sIn, sOut, sVal });

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var rIn = P("In", "In", true); ret.Pins.Add(rIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, lit, set, ret },
            Links = { W(entry, eOut, set, sIn), W(lit, lOut, set, sVal), W(set, sOut, ret, rIn) },
        };

        var result = Compile(Instance("SharedRef", graph));

        Assert.DoesNotContain(DiagnosticCodes.BP1670, result.Diagnostics.Select(d => d.Code));
    }
}
