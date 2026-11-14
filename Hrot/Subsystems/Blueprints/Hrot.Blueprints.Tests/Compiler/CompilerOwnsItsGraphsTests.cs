using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>U-2 / <c>BP-229</c> — compiling an asset must not EDIT it.</b>
///
/// <para>
/// ⛔ <b>The defect.</b> <c>BlueprintCompiler.Compile</c> took a shallow copy that owned a new
/// <c>Graphs</c> list but the <b>same <see cref="Graph"/> objects</b>, and
/// <c>Stage2_5_ExpandMacros</c> then edited them in place: it removed the caller's
/// <c>MacroCallNode</c> from <c>host.Nodes</c> and rewired host <see cref="Link"/> objects. ⇒ after a
/// compile, the designer's macro call node was <b>gone from the graph they were looking at</b> and the
/// macro body was spliced into it.
/// </para>
///
/// <para>
/// 📌 <b>Not reachable in production today</b> — the only path that hands <c>Compile</c> a live
/// document is <c>QuickReloadService.TriggerAsync</c>, which has no production caller. A loaded gun,
/// not a live defect; ⚠ and Batch 43 made <c>Graph.LocalVariables</c> designer-editable, so a graph
/// object now carries authored state a write-through could corrupt.
/// </para>
/// </summary>
public sealed class CompilerOwnsItsGraphsTests
{
    /// <summary>
    /// ⭐⭐ <b>Pass 1 — the caller's graph is untouched by a compile that expands a macro.</b>
    /// ⚠ Asserted on the <b>node the splice removes</b>, not merely on counts: a copy that preserved
    /// the count while swapping the node would pass a count-only test.
    /// </summary>
    [Fact]
    public void CompilingAnAssetWithAMacroCall_LeavesTheCallersGraphIntact()
    {
        var asset = MacroCallFixture();
        var host  = asset.Graphs.Single(g => g.Kind != GraphKind.Macro);

        var callNodeIds = host.Nodes.OfType<MacroCallNode>().Select(n => n.Id).ToList();
        Assert.NotEmpty(callNodeIds);
        var nodesBefore = host.Nodes.Count;
        var linksBefore = host.Links.Count;
        var linkTargets = host.Links.Select(l => (l.FromNodeId, l.FromPinId, l.ToNodeId, l.ToPinId)).ToList();

        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded,
            "fixture must compile: " + string.Join(",", result.Diagnostics.Where(d => d.IsError).Select(d => d.Code)));

        Assert.Equal(nodesBefore, host.Nodes.Count);
        Assert.Equal(linksBefore, host.Links.Count);
        foreach (var id in callNodeIds)
            Assert.Contains(host.Nodes, n => n.Id == id);      // ⭐ the node the splice deletes

        // ⭐ And the links were not REWIRED — MacroExpander assigns ToNodeId/ToPinId in place.
        Assert.Equal(
            linkTargets,
            host.Links.Select(l => (l.FromNodeId, l.FromPinId, l.ToNodeId, l.ToPinId)).ToList());
    }

    /// <summary>
    /// ⭐ <b>The macro really did expand</b> — otherwise Pass 1 could be satisfied by a compiler that
    /// silently skipped the splice, which is the failure mode a "nothing changed" test invites.
    /// </summary>
    [Fact]
    public void TheMacroStillExpandsInTheCompilersOwnCopy()
    {
        var asset  = MacroCallFixture();
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());

        Assert.True(result.Succeeded);
        var canonicalHost = result.CanonicalAsset!.Graphs.Single(g => g.Kind != GraphKind.Macro);
        Assert.DoesNotContain(canonicalHost.Nodes, n => n is MacroCallNode);   // spliced away, in the copy
    }

    /// <summary>
    /// 📌 <b>Designer-authored graph state survives a compile</b> — <c>Graph.LocalVariables</c>
    /// (BP-57), the newest thing riding on a <see cref="Graph"/>. ⚠ It passes because no stage mutates
    /// the list, and this locks that in: a future stage that normalises locals would have to notice.
    /// </summary>
    [Fact]
    public void CompilingDoesNotDisturbTheCallersLocalVariables()
    {
        var asset = MacroCallFixture();
        var host  = asset.Graphs.Single(g => g.Kind != GraphKind.Macro);
        host.LocalVariables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Scratch",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "",
        });
        var before = host.LocalVariables.Select(v => (v.Id, v.Name)).ToList();

        new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());

        Assert.Equal(before, host.LocalVariables.Select(v => (v.Id, v.Name)).ToList());
    }

    /// <summary>
    /// ⚠ <b>Stage 0's rehydration stays visible, and that is why the copy sits where it does.</b>
    /// <c>Compile</c>'s own comment calls the pin mutation <i>"intentional rehydration"</i>; a copy
    /// taken before Stage 0 would hide it and silently change documented behaviour. ⛔ This is the
    /// assertion that stops a later "make the copy deeper" change from doing that.
    /// </summary>
    [Fact]
    public void Stage0PinRehydrationIsStillVisibleToTheCaller()
    {
        var asset = MacroCallFixture();
        var pinless = asset.Graphs
            .SelectMany(g => g.Nodes)
            .FirstOrDefault(n => n.Pins.Count == 0);
        Assert.NotNull(pinless);   // the fixture is authored projection-only, like a saved asset

        new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());

        Assert.NotEmpty(pinless!.Pins);
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal Instance asset whose Event graph calls a one-node macro. Authored pin-less, as a
    /// saved asset is, so <see cref="Stage0PinRehydrationIsStillVisibleToTheCaller"/> has something to
    /// observe.
    /// </summary>
    private static Pin ExecIn()  => new() { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true };
    private static Pin ExecOut() => new() { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true };

    private static Link Wire(Node from, Pin fromPin, Node to, Pin toPin) => new()
    {
        FromNodeId = from.Id, FromPinId = fromPin.Id, ToNodeId = to.Id, ToPinId = toPin.Id,
    };

    private static BlueprintAsset MacroCallFixture()
    {
        // The macro: Entry → PrintString → Return. Mirrors MacroExpansionTests' own fixture shape.
        var mEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var mEntryOut = ExecOut(); mEntry.Pins.Add(mEntryOut);
        var mBody = new PrintStringNode { Id = Guid.NewGuid() };
        var mBodyIn = ExecIn(); var mBodyOut = ExecOut();
        mBody.Pins.AddRange(new[] { mBodyIn, mBodyOut });
        var mRet = new ReturnNode { Id = Guid.NewGuid() };
        var mRetIn = ExecIn(); mRet.Pins.Add(mRetIn);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "Bump", Kind = GraphKind.Macro,
            Nodes = { mEntry, mBody, mRet },
            Links = { Wire(mEntry, mEntryOut, mBody, mBodyIn), Wire(mBody, mBodyOut, mRet, mRetIn) },
        };

        // The host: Entry → MacroCall → Return, plus ⭐ ONE PIN-LESS node so Stage 0 has something to
        // rehydrate — a saved asset is authored projection-only, and that rehydration is the mutation
        // the copy is deliberately placed AFTER.
        var hEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var hEntryOut = ExecOut(); hEntry.Pins.Add(hEntryOut);
        var call = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var callIn = ExecIn(); var callOut = ExecOut();
        call.Pins.AddRange(new[] { callIn, callOut });
        var hRet = new ReturnNode { Id = Guid.NewGuid() };
        var hRetIn = ExecIn(); hRet.Pins.Add(hRetIn);
        var pinless = new PrintStringNode { Id = Guid.NewGuid() };   // no pins on purpose

        var host = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { hEntry, call, hRet, pinless },
            Links = { Wire(hEntry, hEntryOut, call, callIn), Wire(call, callOut, hRet, hRetIn) },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "MacroOwnershipFixture",
            Dispatch = BlueprintDispatchKind.Instance,
            Header   = new Header(),
            Graphs   = { macro, host },
        };
    }
}
