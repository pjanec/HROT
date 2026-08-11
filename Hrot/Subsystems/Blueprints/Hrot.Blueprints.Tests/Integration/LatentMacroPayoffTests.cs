using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Stages;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// ⭐ <b>BP-81's payoff case — the scenario macros were built for, executed.</b>
///
/// <para>
/// <b>Why this specific test and not another integration test.</b> BP-78 is on record that a macro is
/// the <b>only</b> construct that can factor out a reusable <b>latent</b> sequence: <c>BP1650</c>
/// forbids latent nodes inside a Function graph, because a Function graph compiles to a synchronous C#
/// method with no way to suspend. Batch 30 proved the splice SHAPE — clones, provenance, the mirror —
/// but never executed the result, so the one capability the feature exists to deliver was still
/// unevidenced.
/// </para>
///
/// <para>
/// ⚠ <c>CompileResult.Succeeded</c> never invokes Roslyn, and expansion assertions never invoke the
/// emitter. These tests go all the way: <b>expand → generate → compile with real Roslyn → load →
/// execute across frames → assert a value.</b>
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class LatentMacroPayoffTests
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

    // ── fixture helpers ─────────────────────────────────────────────────────

    private static Pin NewPin(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir,
        IsExec = isExec, TypeRef = new BlueprintTypeRef { TypeId = typeId },
    };

    private static Link Wire(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    /// <summary>
    /// The reusable latent sequence: <c>Entry → Delay(0.4) → SetComponent(Ammo = ammoValue) → Return</c>.
    /// "aim, wait, fire" — the <c>SetComponent</c> is the observable "fire", and it must not happen
    /// until the delay has elapsed.
    /// </summary>
    private static Graph MakeAimDelayFireMacro(string name, int ammoValue, out Node fireNode)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true);
        entry.Pins.Add(entryOut);

        var delay   = new LatentDelayNode { Id = Guid.NewGuid() };
        var dIn     = NewPin("In", "In", isExec: true);
        var dOut    = NewPin("Out", "Out", isExec: true);
        var dSecs   = NewPin("Seconds", "In", isExec: false, typeId: "System.Single");
        delay.Pins.AddRange(new[] { dIn, dOut, dSecs });
        delay.PinDefaults = new Dictionary<string, string> { ["Seconds"] = "0.4" };

        var fire = new SetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = DemoComponentFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new() { Name = "Ammo", TypeId = "System.Int32" },
            },
        };
        var fIn      = NewPin("In", "In", isExec: true);
        var fOut     = NewPin("Out", "Out", isExec: true);
        var fAmmo    = NewPin("Ammo", "In", isExec: false, typeId: "System.Int32");
        var fWritten = NewPin("Written", "Out", isExec: false, typeId: "System.Boolean");
        fire.Pins.AddRange(new[] { fIn, fOut, fAmmo, fWritten });
        fire.PinDefaults = new Dictionary<string, string> { ["Ammo"] = ammoValue.ToString() };

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = NewPin("In", "In", isExec: true);
        ret.Pins.Add(retIn);

        fireNode = fire;
        return new Graph
        {
            Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Macro,
            Nodes = { entry, delay, fire, ret },
            Links =
            {
                Wire(entry, entryOut, delay, dIn),
                Wire(delay, dOut,     fire,  fIn),
                Wire(fire,  fOut,     ret,   retIn),
            },
        };
    }

    private static MacroCallNode MakeCall(Graph macro, out Pin execIn, out Pin execOut)
    {
        var call = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        execIn  = NewPin("In",  "In",  isExec: true);
        execOut = NewPin("Out", "Out", isExec: true);
        call.Pins.AddRange(new[] { execIn, execOut });
        return call;
    }

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

    // ────────────────────────────────────────────────────────────────────────
    // 1.1 — the test whose name overclaimed, now carrying its own name
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Batch 30 shipped a test with this name whose body only called <c>Expand(...)</c> and asserted
    /// splice shape — it never touched <c>CSharpCompilation</c>. The name was fixed rather than the
    /// body being renamed to match, deliberately: <b>a test that claims more than it checks is worse
    /// than no test, because it retires the question.</b>
    ///
    /// <para>
    /// This runs the REAL incremental generator over the asset and then compiles the emitted C# with
    /// real Roslyn — the step that caught BP-117 (CS0126) and BP-112 (CS9191).
    /// </para>
    /// </summary>
    [Fact]
    public void LatentMacro_SplicedIntoATickGraph_CompilesThroughTheRealGenerator()
    {
        var macro = MakeAimDelayFireMacro("AimDelayFire", ammoValue: 42, out _);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var call     = MakeCall(macro, out var callIn, out var callOut);
        var ret      = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var tick = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, call, ret },
            Links = { Wire(entry, entryOut, call, callIn), Wire(call, callOut, ret, retIn) },
        };

        var asset  = MakeAiPrimitiveAsset("LatentMacroRoslyn", macro, tick);
        var result = AuthoringPath.Generate(asset);

        Assert.True(result.Clean,
            "A latent macro spliced into a tick graph must produce C# that really compiles.\n"
            + result.Report());

        // And the latent lowering genuinely landed in the emitted source — a macro that expanded to
        // nothing would also "compile clean".
        Assert.Contains(result.GeneratedSources, src => src.Contains("NodeStatus.Running"));
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1.2 — ⭐ the payoff: run it across frames
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The scenario the whole macro feature exists to serve, executed.</b> The macro suspends on
    /// its <c>Delay</c>, the tick returns <c>Running</c>, and the "fire" writes NOTHING yet. After time
    /// passes the next tick resumes mid-body, performs the write, and reports <c>Success</c>.
    ///
    /// <para>
    /// ⚠ The <b>value</b> assertion is what makes this more than a status check: a splice that dropped
    /// the post-delay half of the body would still return Running then Success, and only the component
    /// write proves the resumed path actually ran.
    /// </para>
    /// </summary>
    [Fact]
    public void LatentMacro_SuspendsAndResumesAcrossFrames_AndFiresOnlyAfterTheDelay()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var macro = MakeAimDelayFireMacro("AimDelayFire", ammoValue: 42, out _);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var call     = MakeCall(macro, out var callIn, out var callOut);
        var ret      = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var tick = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, call, ret },
            Links = { Wire(entry, entryOut, call, callIn), Wire(call, callOut, ret, retIn) },
        };

        var asset = MakeAiPrimitiveAsset("LatentMacroRun", macro, tick);

        fixture.CompileAndLoad(asset);   // real Roslyn, then loaded into an ALC

        fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpComponentDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Hrot.AI.Behaviors.BpComponentDemo { Ammo = 0 });

        // ── Frame 1: enters the macro body, hits the Delay, suspends ────────
        var tick1 = fixture.InvokeBTreeAction(asset, entity);
        Assert.Equal(NodeStatus.Running, tick1);
        Assert.Equal(0, ReadAmmo(fixture, entity));   // ⭐ has NOT fired yet

        // ── Frame 2, still inside the 0.4s window: still waiting ────────────
        fixture.View.AdvanceTime(0.1f);
        var tick2 = fixture.InvokeBTreeAction(asset, entity);
        Assert.Equal(NodeStatus.Running, tick2);
        Assert.Equal(0, ReadAmmo(fixture, entity));

        // ── Frame 3, past the delay: resumes mid-body and completes ─────────
        fixture.View.AdvanceTime(0.5f);
        var tick3 = fixture.InvokeBTreeAction(asset, entity);

        Assert.Equal(NodeStatus.Success, tick3);
        Assert.Equal(42, ReadAmmo(fixture, entity));  // ⭐ the resumed path really ran
    }

    // ────────────────────────────────────────────────────────────────────────
    // BP-74 / Q26-A3 — a TWO-ENTRY macro, entered from each door, executed
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The Q26-A3 payoff, executed.</b> One macro with two exec entries, each door writing a
    /// DIFFERENT value, entered from a different host path on different ticks.
    ///
    /// <para>
    /// The value assertion is what makes this meaningful: splice rule 1 became indexed this batch, and
    /// a wrong pairing — door A reaching B's body, or both doors reaching the same one — produces a
    /// graph that still compiles and still returns Success. Only the written value distinguishes a
    /// correct pairing from a plausible one.
    /// </para>
    ///
    /// <para>
    /// ⚠ Compiled through <b>real Roslyn</b> and loaded; <c>CompileResult.Succeeded</c> never invokes it.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoEntryMacro_EnteredFromEachDoor_WritesThatDoorsValue()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // ── the macro: EnterA → Ammo = 7 ; EnterB → Ammo = 99 ──────────────
        var mEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var outA   = NewPin("EnterA", "Out", isExec: true);
        var outB   = NewPin("EnterB", "Out", isExec: true);
        mEntry.Pins.AddRange(new[] { outA, outB });

        var fireA = MakeAmmoWriter(7,  out var aIn, out var aOut);
        var fireB = MakeAmmoWriter(99, out var bIn, out var bOut);

        var mRet   = new ReturnNode { Id = Guid.NewGuid() };
        var mRetIn = NewPin("In", "In", isExec: true); mRet.Pins.Add(mRetIn);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "TwoDoors", Kind = GraphKind.Macro,
            ExecInputs =
            {
                new ExecInDecl { Id = Guid.NewGuid(), Name = "EnterA" },
                new ExecInDecl { Id = Guid.NewGuid(), Name = "EnterB" },
            },
            Nodes = { mEntry, fireA, fireB, mRet },
            Links =
            {
                Wire(mEntry, outA, fireA, aIn),
                Wire(mEntry, outB, fireB, bIn),
                Wire(fireA, aOut, mRet, mRetIn),
                Wire(fireB, bOut, mRet, mRetIn),
            },
        };

        // ── host tick: Branch on Health >= 50 → door B when true, door A when false ──
        var hEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var hOut   = NewPin("Out", "Out", isExec: true); hEntry.Pins.Add(hOut);

        var getHealth = new GetComponentNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = DemoComponentFqn,
            FieldName = "Health", FieldTypeFqn = "System.Int32",
        };
        var ghOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32");
        getHealth.Pins.Add(ghOut);

        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "50" };
        var litOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32");
        lit.Pins.Add(litOut);

        var cmp = new CompareNode { Id = Guid.NewGuid(), Operator = ComparisonOperator.GreaterThanOrEqual };
        var cmpA = NewPin("A", "In", isExec: false);
        var cmpB = NewPin("B", "In", isExec: false);
        var cmpR = NewPin("Result", "Out", isExec: false, typeId: "System.Boolean");
        cmp.Pins.AddRange(new[] { cmpA, cmpB, cmpR });

        var branch = new BranchNode { Id = Guid.NewGuid() };
        var brIn   = NewPin("In",        "In",  isExec: true);
        var brT    = NewPin("True",      "Out", isExec: true);
        var brF    = NewPin("False",     "Out", isExec: true);
        var brCond = NewPin("Condition", "In",  isExec: false, typeId: "System.Boolean");
        branch.Pins.AddRange(new[] { brIn, brT, brF, brCond });

        var call = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var cA   = NewPin("EnterA", "In",  isExec: true);
        var cB   = NewPin("EnterB", "In",  isExec: true);
        var cOut = NewPin("Out",    "Out", isExec: true);
        call.Pins.AddRange(new[] { cA, cB, cOut });

        var hRet   = new ReturnNode { Id = Guid.NewGuid() };
        var hRetIn = NewPin("In", "In", isExec: true); hRet.Pins.Add(hRetIn);

        var tick = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { hEntry, getHealth, lit, cmp, branch, call, hRet },
            Links =
            {
                Wire(hEntry, hOut, branch, brIn),
                Wire(getHealth, ghOut, cmp, cmpA),
                Wire(lit, litOut, cmp, cmpB),
                Wire(cmp, cmpR, branch, brCond),
                Wire(branch, brF, call, cA),      // low health  → door A → 7
                Wire(branch, brT, call, cB),      // high health → door B → 99
                Wire(call, cOut, hRet, hRetIn),
            },
        };

        var asset = MakeAiPrimitiveAsset("TwoEntryMacroRun", macro, tick);
        fixture.CompileAndLoad(asset);            // real Roslyn

        fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpComponentDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Hrot.AI.Behaviors.BpComponentDemo { Health = 10, Ammo = 0 });

        // ── Tick 1: Health 10 < 50 → False → door A ────────────────────────
        Assert.Equal(NodeStatus.Success, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(7, ReadAmmo(fixture, entity));      // ⭐ door A's value

        // ── Tick 2: Health 100 >= 50 → True → door B ───────────────────────
        ref var demo = ref fixture.World.GetComponentRW<Hrot.AI.Behaviors.BpComponentDemo>(entity);
        demo.Health = 100;

        Assert.Equal(NodeStatus.Success, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(99, ReadAmmo(fixture, entity));     // ⭐ door B's value — the pairing is right
    }

    /// <summary>A SetComponent writing a constant into <c>BpComponentDemo.Ammo</c>.</summary>
    private static SetComponentNode MakeAmmoWriter(int value, out Pin execIn, out Pin execOut)
    {
        var n = new SetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = DemoComponentFqn,
            Fields = new List<ComponentFieldDecl> { new() { Name = "Ammo", TypeId = "System.Int32" } },
        };
        execIn  = NewPin("In",  "In",  isExec: true);
        execOut = NewPin("Out", "Out", isExec: true);
        var ammo    = NewPin("Ammo",    "In",  isExec: false, typeId: "System.Int32");
        var written = NewPin("Written", "Out", isExec: false, typeId: "System.Boolean");
        n.Pins.AddRange(new[] { execIn, execOut, ammo, written });
        n.PinDefaults = new Dictionary<string, string> { ["Ammo"] = value.ToString() };
        return n;
    }

    // ────────────────────────────────────────────────────────────────────────
    // BP-83 — debug provenance reaches the debug map
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>BP-83's payoff: one breakpoint in a macro body arms EVERY expansion site.</b>
    ///
    /// <para>
    /// The designer sets a breakpoint on a node in the MACRO graph — that is where they can see it.
    /// But every emitted <c>DebugMapEntry</c> carries a CLONE id that exists in no asset file, so
    /// before this the breakpoint matched no entry and silently never armed. Resolution is
    /// authored-node → N entries, which is why <c>OriginNodeId</c> had to reach the map and not stop
    /// at the IR.
    /// </para>
    ///
    /// <para>
    /// ⚠ <c>NodeId</c> still holds the clone id, deliberately: <b>line→node stays 1:1</b> while
    /// <b>node→line becomes one-to-many</b>. Both halves are asserted here, because a "fix" that
    /// redirected NodeId instead of adding a back-reference would satisfy the first assertion and
    /// break the debugger's line lookup.
    /// </para>
    /// </summary>
    [Fact]
    public void OneAuthoredMacroNode_MapsToAnEntryPerExpansionSite()
    {
        var macro = MakeAimDelayFireMacro("SharedBody", ammoValue: 5, out var authoredFire);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var c1       = MakeCall(macro, out var c1In, out var c1Out);
        var c2       = MakeCall(macro, out var c2In, out var c2Out);
        var ret      = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var tick = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, c1, c2, ret },
            Links =
            {
                Wire(entry, entryOut, c1, c1In),
                Wire(c1, c1Out, c2, c2In),
                Wire(c2, c2Out, ret, retIn),
            },
        };

        var asset  = MakeAiPrimitiveAsset("MacroDebugMap", macro, tick);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.NotNull(result.DebugMap);

        // ⭐ The authored "fire" node resolves to TWO entries — one per call site.
        var armed = result.DebugMap!.Entries
            .Where(e => e.OriginNodeId == authoredFire.Id)
            .ToList();

        // ⚠ Asserted as DISTINCT CLONE IDS, not as an entry count. The emitter can record more than
        // one line region per node (a latent split puts the post-Delay half in the resume block), so
        // "2 entries" would have been an assertion about emitter granularity rather than about
        // provenance. What BP-83 needs is that the authored node reaches BOTH expansion sites.
        Assert.Equal(2, armed.Select(e => e.NodeId).Distinct().Count());
        Assert.NotEmpty(armed);

        // …each pointing back into the MACRO graph, not the host. Without OriginGraphId this id would
        // be ambiguous the moment a second macro were involved.
        Assert.All(armed, e => Assert.Equal(macro.Id, e.OriginGraphId));

        // …while their own NodeIds stay clone ids, so line→node remains 1:1.
        Assert.DoesNotContain(armed, e => e.NodeId == authoredFire.Id);

        // And an ordinary authored node (the host's own) carries no provenance at all.
        Assert.Contains(result.DebugMap.Entries, e => e.OriginNodeId is null);
    }

    /// <summary>
    /// §2.3 ruling, locked: the schema is <b>1.1</b>, and a <b>1.0</b> map must still load. The two new
    /// fields are additive and omitted when null, so a macro-free asset's map is byte-identical to the
    /// 1.0 output it replaces — the bump costs no churn while keeping the version honest about shape.
    /// </summary>
    [Fact]
    public void DebugMap_RoundTripsProvenance_AndStillReadsA10Map()
    {
        var macro = MakeAimDelayFireMacro("SharedBody", ammoValue: 5, out var authoredFire);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var call     = MakeCall(macro, out var callIn, out var callOut);
        var ret      = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var tick = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, call, ret },
            Links = { Wire(entry, entryOut, call, callIn), Wire(call, callOut, ret, retIn) },
        };

        var result = new BlueprintCompiler().Compile(
            MakeAiPrimitiveAsset("MacroDebugMapRoundTrip", macro, tick), DefaultOptions());
        Assert.True(result.Succeeded);

        var json = DebugMapSerializer.Serialize(result.DebugMap!);
        Assert.Contains("\"1.1\"", json);

        var reloaded = DebugMapSerializer.Deserialize(json)!;
        var armed = reloaded.Entries.Where(e => e.OriginNodeId == authoredFire.Id).ToList();
        Assert.NotEmpty(armed);
        Assert.All(armed, e => Assert.Equal(macro.Id, e.OriginGraphId));

        // A 1.0 map — no origin fields at all — still loads, with null provenance. That is the right
        // read, not a degraded one: a map written before macros existed has no expanded nodes.
        var legacy = json.Replace("\"1.1\"", "\"1.0\"")
                         .Replace("\"originNodeId\"", "\"ignoredNodeId\"")
                         .Replace("\"originGraphId\"", "\"ignoredGraphId\"")
                         .Replace("\"OriginNodeId\"", "\"IgnoredNodeId\"")
                         .Replace("\"OriginGraphId\"", "\"IgnoredGraphId\"");
        var legacyMap = DebugMapSerializer.Deserialize(legacy)!;
        Assert.NotEmpty(legacyMap.Entries);
        Assert.All(legacyMap.Entries, e => Assert.Null(e.OriginNodeId));
    }

    /// <summary>
    /// ⚠ <b>Two call sites, one graph — where a shared resume cursor would show up.</b> Batch 30 proved
    /// two CLONES exist; it did not prove two SUSPENSIONS coexist. Each spliced body must own its
    /// resume point, or the second call would resume into the first's continuation (or skip its delay
    /// entirely).
    /// <para>
    /// The two macros write different values, so a crossed cursor is visible as a wrong VALUE and not
    /// merely as a wrong status.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoLatentCallSites_EachSuspendIndependently_AndBothBodiesRun()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var first  = MakeAimDelayFireMacro("FireFirst",  ammoValue: 7,  out _);
        var second = MakeAimDelayFireMacro("FireSecond", ammoValue: 99, out _);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = NewPin("Out", "Out", isExec: true); entry.Pins.Add(entryOut);
        var c1       = MakeCall(first,  out var c1In, out var c1Out);
        var c2       = MakeCall(second, out var c2In, out var c2Out);
        var ret      = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = NewPin("In", "In", isExec: true); ret.Pins.Add(retIn);

        var tick = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, c1, c2, ret },
            Links =
            {
                Wire(entry, entryOut, c1, c1In),
                Wire(c1, c1Out, c2, c2In),
                Wire(c2, c2Out, ret, retIn),
            },
        };

        var asset = MakeAiPrimitiveAsset("TwoLatentSites", first, second, tick);
        fixture.CompileAndLoad(asset);

        fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpComponentDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Hrot.AI.Behaviors.BpComponentDemo { Ammo = 0 });

        // First delay.
        Assert.Equal(NodeStatus.Running, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(0, ReadAmmo(fixture, entity));

        // Past the FIRST delay: the first body fires, then the second suspends on its own delay.
        fixture.View.AdvanceTime(0.5f);
        Assert.Equal(NodeStatus.Running, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(7, ReadAmmo(fixture, entity));   // ⭐ first site fired, second has not

        // Past the SECOND delay: the second body fires and the graph completes.
        fixture.View.AdvanceTime(0.5f);
        Assert.Equal(NodeStatus.Success, fixture.InvokeBTreeAction(asset, entity));
        Assert.Equal(99, ReadAmmo(fixture, entity));  // ⭐ both suspensions resolved, in order
    }
}
