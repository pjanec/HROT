using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-57 / Q27 — function-local variables, the compiler half.
///
/// <para>
/// ⭐ <b>Q27-A1 is the whole design: a local is NOT a <c>State</c> field.</b> It compiles to a plain
/// C# local, reset from its default on entry, so the instance's state struct does not grow by one
/// field per scratch value and call N+1 cannot see call N's value.
/// </para>
///
/// <para>
/// ⚠ <b>The shadowing test is the one that would catch the real defect.</b>
/// <c>Stage5.FindVariableIndex</c> falls back to a NAME match across the asset's
/// Variables/WorkingState/Parameters when an id misses; if local lookup inherited that habit, a local
/// reference would silently resolve to the asset variable of the same name — <b>the wrong storage,
/// not merely the wrong value</b>. A test that only checked the local's value would pass on that
/// defect, so these assert the asset variable is <b>unchanged</b>.
/// </para>
/// </summary>
public sealed class LocalVariableTests
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

    private static VariableDecl Decl(string name, string typeId, string? defaultJson = null) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Type = new BlueprintTypeRef { TypeId = typeId },
        DefaultValueJson = defaultJson ?? "",
    };

    /// <summary>Entry → Set(target, literal) → Return, in a Function graph named Tick.</summary>
    private static Graph GraphWritingTo(VariableDecl target, string name = "Tick")
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        var eOut  = P("Out", "Out", true); entry.Pins.Add(eOut);

        var lit = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "7" };
        var lOut = P("Value", "Out", false, "System.Int32"); lit.Pins.Add(lOut);

        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = target.Id.ToString() };
        var sIn  = P("In", "In", true);
        var sOut = P("Out", "Out", true);
        var sVal = P("Value", "In", false, "System.Int32");
        set.Pins.AddRange(new[] { sIn, sOut, sVal });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var rIn   = P("In", "In", true); ret.Pins.Add(rIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Function,
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

    private static string[] Codes(BlueprintAsset asset)
        => Compile(asset).Diagnostics.Select(d => d.Code).ToArray();

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ A local is not a State field
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The State struct must not grow.</b> A local that quietly became a field is exactly the
    /// wart Q27 exists to avoid — it would behave identically on the first call and differently on the
    /// second, and would cost every instance a field per scratch value.
    /// </summary>
    [Fact]
    public void AGraphWithALocal_EmitsNoExtraStateField()
    {
        var local = Decl("Scratch", "System.Int32", "3");

        var ammo = Decl("Ammo", "System.Int32");
        var withoutLocal = Instance("NoLocal", GraphWritingTo(ammo));
        withoutLocal.Variables.Add(ammo);

        var graph = GraphWritingTo(local);
        graph.LocalVariables.Add(local);
        var withLocal = Instance("WithLocal", graph);

        var before = Compile(withoutLocal);
        var after  = Compile(withLocal);

        Assert.True(before.Succeeded, string.Join("; ", before.Diagnostics.Select(d => d.Code)));
        Assert.True(after.Succeeded,  string.Join("; ", after.Diagnostics.Select(d => d.Code)));

        // ⭐ The local's name appears in the generated source as a LOCAL, never as a struct field.
        var src = after.GeneratedSource ?? "";
        Assert.Contains("__loc_Scratch", src);
        Assert.DoesNotContain("public int Scratch;", src);
        Assert.DoesNotContain("s.Scratch", src);
    }

    /// <summary>⭐ Q27-E: the local is re-initialised from its declared default at the top of the body.</summary>
    [Fact]
    public void ALocal_IsDeclaredAndInitialisedAtTheTopOfTheBody()
    {
        var local = Decl("Scratch", "System.Int32", "3");
        var graph = GraphWritingTo(local);
        graph.LocalVariables.Add(local);

        var result = Compile(Instance("InitLocal", graph));

        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Contains("__loc_Scratch = 3", result.GeneratedSource ?? "");
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐ Shadowing — the trap
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>A local and an asset variable sharing a NAME.</b> Writing the local must not touch the
    /// asset variable's field. ⚠ Asserted by the emitted access, because that is where the confusion
    /// would show: <c>s.Scratch = …</c> is the asset field, <c>__loc_Scratch = …</c> is the local.
    /// A test that only checked "the local got written" would pass on the defect.
    /// </summary>
    [Fact]
    public void ALocalShadowingAnAssetVariableName_WritesTheLocal_NotTheField()
    {
        var local = Decl("Scratch", "System.Int32", "0");
        var graph = GraphWritingTo(local);
        graph.LocalVariables.Add(local);

        var asset = Instance("Shadowed", graph);
        // ⚠ A DIFFERENT decl with the SAME name — this is what the name fallback would find.
        asset.Variables.Add(Decl("Scratch", "System.Int32", "99"));

        var result = Compile(asset);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));

        var src = result.GeneratedSource ?? "";
        // ⭐ A GRAPH write always assigns a temp (`X = __tN;`); `s.Scratch = 99;` also appears, but
        // that is InitDefault seeding the asset variable's declared default, which is correct and
        // unrelated. Asserting on the temp form is what distinguishes the two.
        Assert.Contains("__loc_Scratch = __t", src);
        // ⛔ The asset field must not be written BY THIS GRAPH.
        Assert.DoesNotContain("s.Scratch = __t", src);
    }

    /// <summary>
    /// ⭐⭐ <b>The guard that makes the "no name fallback" rule bite, and it took a failed
    /// revert-goes-red to find.</b>
    ///
    /// <para>
    /// The other shadowing test targets the local <b>by id</b>, so it passes whether or not local
    /// lookup also matches on name — adding the fallback back left it green. The hazard runs the other
    /// way: <c>VariableId</c> is not always a GUID (<c>Stage5.FindVariableIndex</c> has a name fallback
    /// precisely because a node may carry a NAME), and a node naming an <b>asset variable</b> must not
    /// be captured by a local that happens to share that name.
    /// </para>
    ///
    /// <para>
    /// ⛔ With a name fallback in local lookup this writes the per-call local instead of the persistent
    /// field — the value silently stops persisting, which no status or shape assertion would notice.
    /// </para>
    /// </summary>
    [Fact]
    public void ANodeNamingAnAssetVariable_IsNotCapturedByASameNamedLocal()
    {
        var assetVar = Decl("Scratch", "System.Int32");
        var graph    = GraphWritingTo(assetVar);
        // ⚠ The node carries the NAME, not the id — the form the fallback exists for.
        graph.Nodes.OfType<SetVariableNode>().Single().VariableId = "Scratch";
        graph.LocalVariables.Add(Decl("Scratch", "System.Int32", "0"));

        var asset = Instance("NameTargeted", graph);
        asset.Variables.Add(assetVar);

        var result = Compile(asset);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));

        var src = result.GeneratedSource ?? "";
        Assert.Contains("s.Scratch = __t", src);            // ⭐ the asset field, as named
        Assert.DoesNotContain("__loc_Scratch = __t", src);  // ⛔ not the local
    }

    /// <summary>
    /// The mirror: a Get/Set naming an ASSET variable still resolves to the field, so local resolution
    /// has not swallowed the ordinary case.
    /// </summary>
    [Fact]
    public void AGraphWithLocals_StillResolvesAssetVariablesToFields()
    {
        var assetVar = Decl("Ammo", "System.Int32");
        var graph    = GraphWritingTo(assetVar);
        graph.LocalVariables.Add(Decl("Scratch", "System.Int32", "1"));

        var asset = Instance("Mixed", graph);
        asset.Variables.Add(assetVar);

        var result = Compile(asset);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Contains("Ammo =", result.GeneratedSource ?? "");
    }

    // ────────────────────────────────────────────────────────────────────────
    // The two rails
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>BP1664</c>, reserved and unbuildable for six batches because <c>Graph</c> had no
    /// <c>LocalVariables</c> at all. Q27-B: a macro is spliced, so a macro-local has nothing to be
    /// scoped to.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1664")]
    public void AMacroDeclaringALocal_ReportsBP1664()
    {
        var macro = new Graph { Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro };
        macro.LocalVariables.Add(Decl("Scratch", "System.Int32"));

        var ammo  = Decl("Ammo", "System.Int32");
        var asset = Instance("MacroLocal", GraphWritingTo(ammo), macro);
        asset.Variables.Add(ammo);

        Assert.Contains(DiagnosticCodes.BP1664, Codes(asset));
    }

    /// <summary>
    /// <c>BP1669</c> — the rail nobody had named. A macro body referencing a local would resolve
    /// against whichever host it was spliced into, so the same macro expands cleanly in one graph and
    /// breaks in another.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1669")]
    public void AMacroBodyReferencingAHostLocal_ReportsBP1669()
    {
        var local    = Decl("Scratch", "System.Int32");
        var assetVar = Decl("Ammo", "System.Int32");

        var host = GraphWritingTo(assetVar);
        host.LocalVariables.Add(local);

        // The macro reads the HOST's local — legal-looking, and wrong in every other host.
        // ⚠ Given a well-formed body (entry → return) so the asset validates far enough for the rail
        // to be the thing under test rather than a malformed-graph diagnostic.
        var macro   = new Graph { Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro };
        var mEntry  = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        var mOut    = P("Out", "Out", true); mEntry.Pins.Add(mOut);
        var mRet    = new ReturnNode { Id = Guid.NewGuid() };
        var mIn     = P("In", "In", true); mRet.Pins.Add(mIn);
        var get = new GetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        macro.Nodes.AddRange(new Node[] { mEntry, get, mRet });
        macro.Links.Add(W(mEntry, mOut, mRet, mIn));

        var asset = Instance("MacroReadsLocal", host, macro);
        asset.Variables.Add(assetVar);

        var all   = Compile(asset).Diagnostics;
        var diags = all.Where(d => d.Code == DiagnosticCodes.BP1669).ToList();

        var reported = Assert.Single(diags);
        // ⭐ It names the macro and the local, so the message is actionable without opening anything.
        Assert.Contains("AimFire", reported.Message);
        Assert.Contains("Scratch", reported.Message);
    }

    /// <summary>⚠ The rails must not fire on an asset that has no locals at all.</summary>
    [Fact]
    public void AnAssetWithNoLocals_ReportsNeitherRail()
    {
        var assetVar = Decl("Ammo", "System.Int32");
        var asset = Instance("NoLocals",
            GraphWritingTo(assetVar),
            new Graph { Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro });
        asset.Variables.Add(assetVar);

        var codes = Codes(asset);
        Assert.DoesNotContain(DiagnosticCodes.BP1664, codes);
        Assert.DoesNotContain(DiagnosticCodes.BP1669, codes);
    }
}
