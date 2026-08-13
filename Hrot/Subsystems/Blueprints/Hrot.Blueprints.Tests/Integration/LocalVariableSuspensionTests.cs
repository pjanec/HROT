using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// BP-57 / ⭐ <b>Q27-A3</b> — a local must survive a suspension.
///
/// <para>
/// ⚠⚠ Batch 37 shipped locals as plain C# locals (A1). A suspension is <c>return NodeStatus.Running</c>
/// — the C# frame dies, and a stack local with it — so a value written before a <c>Delay</c> read back
/// as its <b>default</b> after the resume, silently and with no diagnostic. A3 rules that a suspendable
/// graph's locals are blackboard-allocated instead, reset in the ENTRY block rather than at the top of
/// the method.
/// </para>
///
/// <para>
/// ⭐ These execute across real frames through real Roslyn. <c>CompileResult.Succeeded</c> never invokes
/// Roslyn and never runs anything, so nothing short of this could have caught the defect — and Batch
/// 37's own per-invocation test called the graph twice <b>from the top</b>, never suspending mid-graph.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class LocalVariableSuspensionTests
{
    private const string DemoComponentFqn = "Hrot.AI.Behaviors.BpComponentDemo";

    private static BlueprintTestFixtureOptions NoAlcCheck { get; } =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin NewPin(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir,
        IsExec = isExec, TypeRef = new BlueprintTypeRef { TypeId = typeId },
    };

    private static Link Wire(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    private static VariableDecl Decl(string name, string typeId, string? defaultJson = null) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Type = new BlueprintTypeRef { TypeId = typeId },
        DefaultValueJson = defaultJson ?? "",
    };

    private static BlueprintAsset MakeAiPrimitiveAsset(string name, params Graph[] graphs) => new()
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

    private static int ReadAmmo(BlueprintTestFixture fixture, Fdp.Core.Entity e)
        => fixture.World.GetComponentRO<Hrot.AI.Behaviors.BpComponentDemo>(e).Ammo;

    /// <summary>
    /// <c>Entry → Set(local = 7) → Delay(0.4) → SetComponent(Ammo = Get(local)) → Return</c>.
    ///
    /// <para>
    /// ⭐ The write is <b>before</b> the suspension and the read <b>after</b> it, which is the only
    /// shape that distinguishes storage that survives a resume from storage that does not. The
    /// component write is the observable: the local's value has to cross the frame boundary to reach
    /// it.
    /// </para>
    /// </summary>
    private static Graph MakeSetDelayReadGraph(
        VariableDecl local, float seconds = 0.4f, string graphName = "Tick")
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);

        var lit  = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "7" };
        var lOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32"); lit.Pins.Add(lOut);

        var set  = new SetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        var setIn  = NewPin("In",  "In",  isExec: true);
        var setOut = NewPin("Out", "Out", isExec: true);
        var setVal = NewPin("Value", "In", isExec: false, typeId: "System.Int32");
        set.Pins.AddRange(new[] { setIn, setOut, setVal });

        var delay = new LatentDelayNode { Id = Guid.NewGuid() };
        var dIn   = NewPin("In",  "In",  isExec: true);
        var dOut  = NewPin("Out", "Out", isExec: true);
        var dSecs = NewPin("Seconds", "In", isExec: false, typeId: "System.Single");
        delay.Pins.AddRange(new[] { dIn, dOut, dSecs });
        delay.PinDefaults = new Dictionary<string, string>
        {
            ["Seconds"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var get  = new GetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        var gOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32"); get.Pins.Add(gOut);

        var fire = new SetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = DemoComponentFqn,
            Fields = new List<ComponentFieldDecl> { new() { Name = "Ammo", TypeId = "System.Int32" } },
        };
        var fIn      = NewPin("In",  "In",  isExec: true);
        var fOut     = NewPin("Out", "Out", isExec: true);
        var fAmmo    = NewPin("Ammo", "In", isExec: false, typeId: "System.Int32");
        var fWritten = NewPin("Written", "Out", isExec: false, typeId: "System.Boolean");
        fire.Pins.AddRange(new[] { fIn, fOut, fAmmo, fWritten });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = graphName, Kind = GraphKind.Function,
            Nodes = { entry, lit, set, delay, get, fire, ret },
            Links =
            {
                Wire(entry, entryOut, set,  setIn),
                Wire(lit,   lOut,     set,  setVal),
                Wire(set,   setOut,   delay, dIn),
                Wire(delay, dOut,     fire, fIn),
                Wire(get,   gOut,     fire, fAmmo),
                Wire(fire,  fOut,     ret,  retIn),
            },
        };
        graph.LocalVariables.Add(local);
        return graph;
    }

    /// <summary>
    /// <c>Entry → SetComponent(Ammo = Get(local)) → Set(local = 7) → Delay → Return</c>.
    ///
    /// <para>
    /// ⭐ The read happens <b>first</b>, so the component records what the local held on ENTRY. Run it
    /// twice and the second invocation's entry value distinguishes "reset once per invocation" (0)
    /// from "persisted like a State field" (7) — the wart Q27 exists to avoid, which a blackboard slot
    /// would otherwise reintroduce.
    /// </para>
    /// </summary>
    private static Graph MakeReadThenSetGraph(VariableDecl local)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);

        var get  = new GetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        var gOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32"); get.Pins.Add(gOut);

        var record = new SetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = DemoComponentFqn,
            Fields = new List<ComponentFieldDecl> { new() { Name = "Ammo", TypeId = "System.Int32" } },
        };
        var rIn      = NewPin("In",  "In",  isExec: true);
        var rOut     = NewPin("Out", "Out", isExec: true);
        var rAmmo    = NewPin("Ammo", "In", isExec: false, typeId: "System.Int32");
        var rWritten = NewPin("Written", "Out", isExec: false, typeId: "System.Boolean");
        record.Pins.AddRange(new[] { rIn, rOut, rAmmo, rWritten });

        var lit  = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "7" };
        var lOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32"); lit.Pins.Add(lOut);

        var set    = new SetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        var setIn  = NewPin("In",  "In",  isExec: true);
        var setOut = NewPin("Out", "Out", isExec: true);
        var setVal = NewPin("Value", "In", isExec: false, typeId: "System.Int32");
        set.Pins.AddRange(new[] { setIn, setOut, setVal });

        var delay = new LatentDelayNode { Id = Guid.NewGuid() };
        var dIn   = NewPin("In",  "In",  isExec: true);
        var dOut  = NewPin("Out", "Out", isExec: true);
        var dSecs = NewPin("Seconds", "In", isExec: false, typeId: "System.Single");
        delay.Pins.AddRange(new[] { dIn, dOut, dSecs });
        delay.PinDefaults = new Dictionary<string, string> { ["Seconds"] = "0.4" };

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, get, record, lit, set, delay, ret },
            Links =
            {
                Wire(entry,  entryOut, record, rIn),
                Wire(get,    gOut,     record, rAmmo),
                Wire(record, rOut,     set,    setIn),
                Wire(lit,    lOut,     set,    setVal),
                Wire(set,    setOut,   delay,  dIn),
                Wire(delay,  dOut,     ret,    retIn),
            },
        };
        graph.LocalVariables.Add(local);
        return graph;
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐⭐ The defect: a local written before a suspension
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The value has to cross the frame boundary.</b> Set to 7, suspend on a <c>Delay</c>, and
    /// read it back after the resume into a component field.
    ///
    /// <para>
    /// ⚠ Before Q27-A3 this read <b>0</b> — the local was a C# local, the suspension returned out of
    /// the method, and the frame took the value with it. It compiled clean and produced a wrong number:
    /// no diagnostic, no crash, nothing to notice.
    /// </para>
    /// </summary>
    [Fact]
    public void ALocalWrittenBeforeASuspension_StillHoldsItsValueAfterTheResume()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var local = Decl("Carry", "System.Int32", "0");
        var asset = MakeAiPrimitiveAsset("SuspendLocalSurvives", MakeSetDelayReadGraph(local));

        fixture.CompileAndLoad(asset);   // real Roslyn — .Succeeded never invokes it
        fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpComponentDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Hrot.AI.Behaviors.BpComponentDemo { Ammo = -1 });

        Assert.Equal(NodeStatus.Running, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(-1, ReadAmmo(fixture, entity));   // has not reached the write yet

        fixture.View.AdvanceTime(0.5f);
        Assert.Equal(NodeStatus.Success, fixture.InvokeBTreeAction(asset, entity));

        // ⭐ 7, not 0. 0 is the defect this item exists to fix.
        Assert.Equal(7, ReadAmmo(fixture, entity));
    }

    /// <summary>
    /// ⭐⭐ <b>The other half, and the one a single call would pass on.</b> Surviving a suspension must
    /// not turn a local into a persistent field: the NEXT invocation has to see the declared default
    /// again.
    ///
    /// <para>
    /// ⚠ "Per-invocation" is not "per-frame" — that conflation is what Q27-A1 got wrong. The reset
    /// lives in the entry block, which is reached only when <c>__phase == 0</c>, so it fires once per
    /// logical invocation and survives every suspension inside it.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>This one stays green under a revert, and that is correct rather than a gap</b> — the revert
    /// removes the storage class it guards, and a stack local trivially resets. It is the OTHER half of
    /// a pair: put the reset back at the top of the method (per-frame) and
    /// <c>ALocalWrittenBeforeASuspension_StillHoldsItsValueAfterTheResume</c> goes red instead. Neither
    /// misplacement survives both.
    /// </para>
    /// </summary>
    [Fact]
    public void ANewInvocation_SeesTheDeclaredDefaultAgain()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var local = Decl("Carry", "System.Int32", "0");
        var asset = MakeAiPrimitiveAsset("SuspendLocalResets", MakeReadThenSetGraph(local));

        fixture.CompileAndLoad(asset);
        fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpComponentDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Hrot.AI.Behaviors.BpComponentDemo { Ammo = -1 });

        // ── Invocation 1: records the entry value (0), sets 7, suspends ─────
        Assert.Equal(NodeStatus.Running, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(0, ReadAmmo(fixture, entity));

        fixture.View.AdvanceTime(0.5f);
        Assert.Equal(NodeStatus.Success, fixture.InvokeBTreeAction(asset, entity));

        // ── Invocation 2: enters again and records the entry value ─────────
        Assert.Equal(NodeStatus.Running, fixture.InvokeBTreeAction(asset, entity));

        // ⭐ 0, not 7. A slot that kept invocation 1's value would be a State field wearing a local's
        // name — exactly the wart Q27-A1 was written to avoid, reintroduced by the fix for A3.
        Assert.Equal(0, ReadAmmo(fixture, entity));
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ The other storage class must be untouched
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>A graph that cannot suspend still gets a plain C# local.</b> The blackboard slot is the
    /// price of surviving a resume; a graph that never resumes must not pay it, or every scratch value
    /// in the asset grows the blackboard.
    /// </summary>
    [Fact]
    public void ANonSuspendingGraph_StillEmitsAPlainCSharpLocal()
    {
        var local = Decl("Carry", "System.Int32", "0");

        // The same fixture WITHOUT the Delay: entry → Set → Return.
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var lit  = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "7" };
        var lOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32"); lit.Pins.Add(lOut);
        var set    = new SetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        var setIn  = NewPin("In",  "In",  isExec: true);
        var setOut = NewPin("Out", "Out", isExec: true);
        var setVal = NewPin("Value", "In", isExec: false, typeId: "System.Int32");
        set.Pins.AddRange(new[] { setIn, setOut, setVal });
        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, lit, set, ret },
            Links = { Wire(entry, entryOut, set, setIn), Wire(lit, lOut, set, setVal), Wire(set, setOut, ret, retIn) },
        };
        graph.LocalVariables.Add(local);

        var result = new BlueprintCompiler().Compile(MakeAiPrimitiveAsset("NoSuspend", graph), DefaultOptions());
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));

        var src = result.GeneratedSource ?? "";
        Assert.Contains("int __loc_Carry = 0;", src);   // declared at the top of the body
        Assert.DoesNotContain("ws.__loc_", src);        // ⛔ no blackboard slot
        Assert.DoesNotContain("__loc_Carry;", src.Replace("int __loc_Carry = 0;", ""));
    }

    /// <summary>
    /// ⭐⭐ <b>The slot must stay out of <c>FindVariableIndex</c>/<c>VarFieldName</c>'s positional
    /// space.</b> Those three lists already disagree about what their integer means (<c>BP-226</c>);
    /// an asset carrying BOTH a real <c>WorkingState</c> variable and a suspending graph's local is the
    /// shape where a slot appended to that list would shift a real field's index.
    /// </summary>
    [Fact]
    public void ASlotDoesNotShiftARealWorkingStateFieldsIndex()
    {
        var stateVar = Decl("Rounds", "System.Int32", "5");
        var local    = Decl("Carry",  "System.Int32", "0");

        var asset = MakeAiPrimitiveAsset("SlotAndVariable", MakeSetDelayReadGraph(local));
        asset.WorkingState.Add(stateVar);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));

        var src = result.GeneratedSource ?? "";
        Assert.Contains("public int Rounds;", src);              // the real field, unshifted
        Assert.Contains("public int __loc_Tick_Carry;", src);    // the slot, alongside it
        Assert.DoesNotContain("__var_", src);                    // ⛔ nothing fell through to the union
    }

    /// <summary>
    /// ⭐ <b>Two graphs may each declare a local named <c>Scratch</c>.</b> Once they share one struct,
    /// <c>__loc_</c> alone no longer separates them — the names must be graph-qualified, or one graph's
    /// scratch value silently becomes the other's.
    /// </summary>
    [Fact]
    public void TwoGraphsWithSameNamedLocals_GetDistinctSlots()
    {
        var first  = Decl("Scratch", "System.Int32", "0");
        var second = Decl("Scratch", "System.Int32", "0");

        var g1 = MakeSetDelayReadGraph(first);
        var g2 = MakeSetDelayReadGraph(second, graphName: "Other");

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "TwoScratches",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs = { g1, g2 }, Header = new Header(),
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Code)));

        var src = result.GeneratedSource ?? "";
        Assert.Contains("__loc_Tick_Scratch", src);
        Assert.Contains("__loc_Other_Scratch", src);
    }
}
