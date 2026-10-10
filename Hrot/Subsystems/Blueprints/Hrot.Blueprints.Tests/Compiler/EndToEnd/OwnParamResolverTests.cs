using System;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⛔ <c>CE-445</c>: the <c>E8a</c> own-asset resolver of an action blueprint is RETIRED (<c>R-155</c>: only behaviours
/// have resolvers). What stays here: the refusals (CE-445 actions, CE-448 reusable Library resolvers) and the behaviour resolver asset
/// (<c>CE-428</c>/<c>CE-433</c>/<c>CE-443</c>). ⛔ HISTORY below.
/// <b><c>E8a</c> — an asset carries its OWN parameter resolver, so NOTHING has to name it.</b>
/// 📄 <c>DESIGN_Resolver_World_Reach.md</c> §7.2 · <c>R-149</c>.
///
/// <para>
/// ⭐⭐ <b>The measurement that makes this slice exist:</b> <c>HostedParamResolvers.Register</c> is keyed
/// by the hosted blueprint's OWN asset id, and <c>AiPrimitiveEmitter</c> already emits
/// <c>TryRun(AssetId, …)</c> in the thunk. ⇒ when the resolver is the asset's own <c>Construction</c>
/// graph, producer and consumer key on one value the asset already carries and there is <b>no binding
/// step at all</b> — which is why `E8a` needs no selection property.
/// </para>
/// </summary>
public sealed class OwnParamResolverTests
{
    // ── the emitted shape ────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Still gated.</b> An asset with neither hosting nor a resolver emits no <c>AssetId</c> and
    /// no registration — which is what keeps the other 44 goldens byte-identical.
    /// </summary>
    [Fact]
    public void NoActionBlueprint_EmitsAnActionLevelResolverRegistration()
    {
        // ⛔ CE-445: HostedParamResolvers is gone; no AiPrimitive may emit a registration into it.
        var src = Emit(GoldenCorpus.Load("ParamDemo"));

        Assert.DoesNotContain("HostedParamResolvers.Register<", src);
    }

    // ── BP1676 — there must be something to resolve ──────────────────────────

    /// <summary>
    /// ⛔ <c>CE-445</c> (<c>R-155</c>) — <b>only behaviours have resolvers.</b> A Construction graph on an ACTION
    /// blueprint is refused (<c>BP1676</c>) even when it declares parameters: an action reads its host's variable
    /// live and has nothing to resolve. 📄 <c>DESIGN_Parameter_Model.md</c> §P.4.
    /// <para>⚠ Inverse-edit red-proof: restore the <c>!ParamsOf(asset).Any()</c> condition in <c>V_ResolverPurity</c>
    /// and this passes validation.</para>
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1676")]
    public void CE445_AResolverOnAnActionBlueprint_EmitsBP1676()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ActionWithResolver")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithParameter("Speed", typeof(float))
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1676);
    }

    /// <summary>
    /// ⛔ <c>CE-448</c> (<c>R-155</c>) — <b>no reusable resolvers.</b> A Construction graph on a Library that resolves
    /// no behaviour (no <c>ResolverSubject</c>) is refused (<c>BP1676</c>), however well-shaped: a resolver is the ONE
    /// optional stage a behaviour names. ⚠ This inverts the pre-CE-448 rail <c>TwoResolversOnALibraryAsset_AreLegal</c>.
    /// <para>⚠ Inverse-edit red-proof: delete the <c>!isSubject</c> arm in <c>V_ResolverPurity</c> and this passes validation.</para>
    /// </summary>
    [Fact]
    public void CE448_AResolverOnALibraryThatResolvesNoBehaviour_EmitsBP1676()
    {
        const string Dto = "global::Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams";
        var asset = BlueprintAssetBuilder
            .Library("ResolverLib")
            .WithGraph("A", GraphKind.Construction, g =>
                { g.WithInput("D", Dto).WithOutput("R", Dto); g.Entry().Return(); })
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1676);
    }

    // ── BP1677 — the two signatures ──────────────────────────────────────────

    // ── BP1675 — the ONE purity exemption ────────────────────────────────────

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>CE-428</c> — shape ③: the shipped BEHAVIOUR resolver asset <c>T40Resolver</c> compiles to the
    /// injected signature and writes the behaviour's block through its two halves. 📄 <c>Q76</c> §12.20.
    /// <para>⚠ Read from the corpus FILE through <c>BlueprintJsonServices</c> — the generator's own read path —
    /// so a <c>ResolverSubject</c> lost in (de)serialisation reds here rather than as a build-time BP1011.</para>
    /// </summary>
    [Fact]
    public void CE428_ABehaviourResolverAsset_EmitsTheInjectedBlockSignature()
    {
        var asset = GoldenCorpus.Load("T40Resolver");
        Assert.NotNull(asset.ResolverSubject);
        Assert.Equal(new[] { "Doubled" }, asset.ResolverSubject!.StateVariables);

        var src = Emit(asset);
        // ⭐ CE-443: the authored type is the asset's OWN Params (its Parameters), parsed by ParseAuthored.
        Assert.Contains("public static void ResolveBehavior(in Params authored, ref global::Hrot.AI.Behaviors.Trees.T40_BehaviorResolverAsset_Block block,", src);
        Assert.Contains("public static void ParseAuthored(string json, out Params authored)", src);
        Assert.Contains("authored.HalfSpeed", src);      // a Parameter ⇒ read from the authored source
        Assert.Contains("block.In.Speed = ", src);       // a Variable NOT in StateVariables ⇒ the In half
        Assert.Contains("block.St.Doubled = ", src);     // a Variable in StateVariables ⇒ the St half
    }

    /// <summary>
    /// ⭐⭐ <c>CE-433</c> — <b>the whole-blackboard pair addresses the SAME block the per-field nodes do.</b>
    /// The shipped <c>T40Resolver</c>, with its <c>Get Variable</c> swapped for <c>Get All Variables</c> and its
    /// <c>Set Variable</c> for <c>Set Variables</c> (pin-less, the editor-save form, so Stage 0 derives the pins).
    /// ⭐ It must emit the identical reads/writes — <c>block.In.Speed</c> read, <c>block.St.Doubled</c> written —
    /// and ⛔ the UNWIRED <c>Speed</c> pin on <c>Set Variables</c> must write NOTHING. It must also pass
    /// <c>V_ResolverPurity</c>: a subject's Variables ARE its block. 📄 <c>Q76</c> §12.22.
    /// </summary>
    [Fact]
    public void CE433_TheWholeBlackboardPair_ReadsAndWritesTheResolversBlock()
    {
        var asset = WithWholeBlackboardPair(GoldenCorpus.Load("T40Resolver"));

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1675);
        var src = Emit(asset);
        Assert.Contains("block.St.Doubled = ", src);
        // unwired Speed pin on Set Variables ⇒ untouched: the Set Variable node stays Speed's ONLY writer
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(src, @"block\.In\.Speed = "));
    }

    /// <summary>
    /// ⛔ <c>CE-433</c> — a stored pin that names no variable (the author deleted/renamed it away) is the
    /// dangling reference <c>BP1670</c> already names, instead of an <c>Unresolved</c> reaching the emitter.
    /// </summary>
    [Fact]
    public void CE433_AStalePinName_IsBP1670()
    {
        var asset = GoldenCorpus.Load("T40Resolver");
        var graph = asset.Graphs.Single();
        var node  = new GetAllVariablesNode { Id = Guid.NewGuid() };
        node.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "Ghost", Direction = "Out",
            TypeRef = new BlueprintTypeRef { TypeId = "System.Single" },
        });
        graph.Nodes.Add(node);
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1670 && d.NodeId == node.Id);
    }

    /// <summary>
    /// ⭐ <c>CE-433</c> — the one pin-set answer excludes fixed-capacity lists (they keep the <c>List*</c>
    /// nodes, <c>BP1506</c>) and never offers a Parameter.
    /// </summary>
    [Fact]
    public void CE433_PinnedVariables_AreTheNonListVariablesOnly()
    {
        var asset = new BlueprintAsset
        {
            Parameters   = { new ParameterDecl { Id = Guid.NewGuid(), Name = "P", Type = new BlueprintTypeRef { TypeId = "System.Single" } } },
            WorkingState =
            {
                new VariableDecl { Id = Guid.NewGuid(), Name = "A",    Type = new BlueprintTypeRef { TypeId = "System.Single" } },
                new VariableDecl { Id = Guid.NewGuid(), Name = "List", Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4 } },
                new VariableDecl { Id = Guid.NewGuid(), Name = "B",    Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
        };
        Assert.Equal(new[] { "A", "B" }, GetAllVariablesNode.PinnedVariablesOf(asset).Select(v => v.Name));
    }

    /// <summary>
    /// <c>T40Resolver</c> with <c>Get Variable(Speed)</c> → <c>Get All Variables</c> and
    /// <c>Set Variable(Doubled)</c> → <c>Set Variables</c>, same node ids, pin-less, links re-keyed to the
    /// deterministic pin ids Stage 0 will mint.
    /// </summary>
    private static BlueprintAsset WithWholeBlackboardPair(BlueprintAsset asset)
    {
        // ⭐ CE-443: T40Resolver reads its AUTHORED input with Get All Parameters (no Get Variable left) and writes
        //   the block with two Set Variable nodes. Swap the one writing Doubled for Set Variables; its Speed pin
        //   stays UNWIRED, so it must write nothing (the other Set Variable is Speed's only writer).
        var graph = asset.Graphs.Single();
        var doubledId = asset.Declarations.Of(DeclarationKind.Variable).Single(d => d.Name == "Doubled").Id.ToString();
        var set = graph.Nodes.OfType<SetVariableNode>().Single(n => n.VariableId == doubledId);
        var svs = new SetVariablesNode { Id = set.Id, EditorMetadata = set.EditorMetadata };
        graph.Nodes[graph.Nodes.IndexOf(set)] = svs;

        foreach (var l in graph.Links)
            if (l.ToNodeId == set.Id && l.ToPinId == DeterministicIds.PinId(set.Id, "Value", "In"))
                l.ToPinId = DeterministicIds.PinId(set.Id, "Doubled", "In");
        return asset;
    }

    /// <summary>
    /// ⛔⛔ <c>CE-428</c> — <b>the compiler's working copy of the asset must carry EVERY public settable property.</b>
    /// <c>BlueprintCompiler.Compile</c> rebuilds the asset field by field; 📌 measured: the new
    /// <c>ResolverSubject</c> was not on that list, so a resolver asset compiled as a plain Library (BP1011) while
    /// every Stage-2-only rail stayed green. ⭐ Reflection, so the NEXT new property reds here too.
    /// </summary>
    [Fact]
    public void TheCompilersAssetCopy_CarriesEveryPublicProperty()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root, "HROT.sln")))
            root = System.IO.Path.GetDirectoryName(root)!;
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(root!,
            "Hrot", "Subsystems", "Blueprints", "Hrot.Blueprints.Compiler", "Compiler", "BlueprintCompiler.cs"));
        var copied = System.Text.RegularExpressions.Regex.Matches(src, @"^\s+(\w+)\s+=\s+asset\.", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        var missing = typeof(BlueprintAsset).GetProperties()
            .Where(p => p.CanWrite && p.SetMethod!.IsPublic && p.Name != "Graphs")
            .Where(p => p.Name is not ("Parameters" or "WorkingState" or "Variables"))   // views over the store, copied via DeclarationStore
            .Select(p => p.Name).Where(n => !copied.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "BlueprintCompiler's asset copy drops: " + string.Join(", ", missing));
    }

    private static string Emit(BlueprintAsset asset)
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded,
            "compile failed: " + string.Join(", ", result.Diagnostics.Where(d => d.IsError)
                                                         .Select(d => d.Code + ": " + d.Message)));
        return result.GeneratedSource!;
    }

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, GoldenCorpus.Options()));
        return sink.All;
    }
}

/// <summary>
/// ⭐⭐⭐ <b><c>R-149</c> — two explicit bindings for one params region THROW, never race.</b>
/// 📄 <c>DESIGN_Resolver_World_Reach.md</c> §7.2.
/// </summary>
public sealed unsafe class ResolverDuplicateBindingTests
{
    /// <summary>
    /// 🔒 <c>R-132</c>'s own sentence applied to its successor: <i>"curated wins BY DECLARATION, not by
    /// arriving first."</i> ⇒ two CURATED bindings have no tie-break, and the silent last-writer-wins
    /// this replaced would have picked one by source order.
    /// </summary>
    [Fact]
    public void TwoResolversForOneBehaviourInOneScan_Throw()
    {
        var registry = new BehaviorRegistry();
        registry.RegisterResolver("Behaviour", Noop);

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterResolver("Behaviour", Noop));

        Assert.Contains("Behaviour", ex.Message);
        Assert.Contains("one resolver", ex.Message);
    }

    /// <summary>
    /// ⭐⭐ <b>The scope is ONE registry instance, and that is load-bearing.</b> 📐 Every scan builds a
    /// FRESH staging registry and the live registry is written by <c>MergeFrom</c> — a separate
    /// overwrite path. ⇒ re-registration across a hot reload never reaches the guard.
    /// ⛔ A throw without that distinction would have broken reload, which is why this rail exists
    /// beside the one above rather than instead of it.
    /// </summary>
    [Fact]
    public void MergeFrom_StillOverwrites_SoAHotReloadIsUnaffected()
    {
        var live = new BehaviorRegistry();
        live.RegisterResolver("Behaviour", Noop);

        var rescan = new BehaviorRegistry();
        rescan.RegisterResolver("Behaviour", Noop);   // a fresh scan: legal, it is a new registry

        var ex = Record.Exception(() => live.MergeFrom(rescan));

        Assert.Null(ex);
    }

    private static unsafe void Noop(
        string json, byte* memory, int capacity, Fdp.Core.EntityRepository world, Fdp.Core.Entity self)
    { }
}
