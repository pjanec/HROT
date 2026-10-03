using System;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-446</c> — a behaviour implemented by a blueprint, end to end.</b> 📄 <c>Architect_Question_77</c> §5.6.
///
/// <para>
/// ⭐ A <see cref="BlueprintDispatchKind.Behavior"/> asset is the Instance body (full node set, latent cursor) whose Tick
/// returns a status. These rails drive the REAL chain: compile → load (the generated registrar registers a
/// <c>BehaviorDefinition</c> on the blueprint brain tier) → <c>AssignBehaviorEvent</c> through <c>BehaviorIngressSystem</c>
/// → <c>BrainTickSystem</c> → finish → the clear (<c>CE-449</c>).
/// </para>
/// </summary>
public sealed unsafe class BlueprintBehaviourTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    /// <summary>⭐ S2 — a Variable's byte offset in a blueprint behaviour's block <c>{ Params In; Vars St }</c>.</summary>
    internal static int VarOffset(BehaviorDefinition def, string name)
    {
        var block = def.BlackboardLayoutType!;
        var st = block.GetField("St")!;
        return (int)System.Runtime.InteropServices.Marshal.OffsetOf(block, "St")
             + (int)System.Runtime.InteropServices.Marshal.OffsetOf(st.FieldType, name);
    }

    /// <summary>Tick: wait one second (latent), then Return(Success).</summary>
    private static BlueprintAsset WaitThenSucceed(string name) => BlueprintAssetBuilder
        .Behavior(name)
        .WithGraph("Tick", g => g.Entry().Delay(1f).Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success))
        .Build();

    // ── emission ────────────────────────────────────────────────────────────

    [Fact]
    public void CE446_ABlueprintBehaviour_EmitsAStatusTick_AndRegistersOnTheBlueprintBrainTier()
    {
        var result = new BlueprintCompiler().Compile(WaitThenSucceed("WaitThenSucceed"), GoldenCorpus.Options());
        Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Code + ": " + d.Message)));
        var src = result.GeneratedSource!;

        Assert.Contains("public static global::Fbt.NodeStatus Tick(", src);
        Assert.Contains("return global::Fbt.NodeStatus.Success;", src);     // the Return node finishes it
        Assert.Contains("return global::Fbt.NodeStatus.Running;", src);     // suspended on the delay
        Assert.Contains("BrainTier = global::Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBlueprint", src);
        Assert.Contains("BlueprintTick = ", src);
        // ⛔ never staged as an attachable Instance
        Assert.DoesNotContain("BlueprintDispatchKind.Instance", src);
        Assert.DoesNotContain("TickThunk", src);
    }

    [Fact]
    public void CE446_ATickThatFallsOffTheEnd_IsRunning_NotFinished()
    {
        var src = new BlueprintCompiler().Compile(BlueprintAssetBuilder
            .Behavior("NeverEnds")
            .WithGraph("Tick", g => g.Entry())
            .Build(), GoldenCorpus.Options()).GeneratedSource!;

        Assert.Contains("return global::Fbt.NodeStatus.Running;", src);
        Assert.DoesNotContain("NodeStatus.Success", src);
    }

    /// <summary>
    /// ⭐⭐ <b>The SHIPPED path</b> — the corpus asset <c>BlueprintBehaviourDemo.bp.json</c> is compiled by the real source
    /// generator into <c>Hrot.AI.Behaviors</c>, and the production registrar scan registers it as a behaviour on the blueprint
    /// brain tier (and NOT as an attachable Instance).
    /// </summary>
    [Fact]
    public void CE446_TheShippedDemo_IsRegisteredByTheProductionScan_AsABlueprintBehaviour()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        var staging = new Fdp.Toolkit.Blueprints.BlueprintRegistryStaging();
        var beh     = new BehaviorRegistry();
        Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner.Scan(
            typeof(Hrot.AI.Behaviors.BpComponentDemo).Assembly, staging, beh, skipOnUnknownParam: true);

        Assert.True(beh.TryGetId("BlueprintBehaviourDemo", out int id), "the generated registrar must register the shipped demo");
        Assert.True(beh.TryGetDefinition(id, out var def));
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, def!.BrainTier);
        Assert.NotNull(def.BlueprintTick);
        Assert.NotNull(def.ParseParams);
    }

    /// <summary>
    /// ⭐⭐ <b>An EVENT GRAPH runs inside a blueprint behaviour</b> — <c>BehaviorTick</c> dispatches this frame's events to
    /// the Event graphs (the Instance dispatch over the handler table) before the Tick. The handler writes the behaviour's
    /// own block. Tick: empty ⇒ Running; Event graph <c>WhenTestHitEvent</c>: <c>WasHit = true</c>.
    /// </summary>
    [Fact]
    public unsafe void CE446_AnEventGraph_ReceivesItsEvent_InABlueprintBehaviour()
    {
        const string Name = "CE446EventGraph";
        var varId = Guid.NewGuid();
        var asset = BlueprintAssetBuilder.Behavior(Name).WithGraph("Tick", g => g.Entry()).Build();
        asset.Variables.Add(new VariableDecl
            { Id = varId, Name = "WasHit", Type = new BlueprintTypeRef { TypeId = "bool" }, DefaultValueJson = "false" });

        var entry  = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = typeof(Runtime.WhenTestHitEvent).FullName! };
        var eOut   = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(eOut);
        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "bool", ValueJson = "true" };
        var litOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        lit.Pins.Add(litOut);
        var set    = new SetVariableNode { Id = Guid.NewGuid(), VariableId = varId.ToString() };
        var sIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var sOut   = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        var sVal   = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", TypeRef = new BlueprintTypeRef { TypeId = "bool" } };
        set.Pins.Add(sIn); set.Pins.Add(sOut); set.Pins.Add(sVal);
        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "OnHit", Kind = GraphKind.Event,
            Nodes = { entry, lit, set },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = eOut.Id,   ToNodeId = set.Id, ToPinId = sIn.Id },
                new Link { FromNodeId = lit.Id,   FromPinId = litOut.Id, ToNodeId = set.Id, ToPinId = sVal.Id },
            },
        });

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Name, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));

        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);
        var brain = new BrainTickSystem(_fixture.BehaviorRegistry);

        bool WasHit()
        {
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root));
            return *(bool*)(root + (int)VarOffset(def!, "WasHit"));
        }

        world.Bus.SwapBuffers();
        brain.Execute(world, 0.016f);
        Assert.False(WasHit());

        world.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 5f });
        world.Bus.SwapBuffers();
        brain.Execute(world, 0.016f);
        Assert.True(WasHit());
    }

    // ── the behaviour's OWN resolver (Q77 §3 B) ─────────────────────────────

    /// <summary>
    /// A behaviour with Parameter <c>Speed</c> (default 2), Variable <c>Mirrored</c>, and a resolver graph
    /// <c>Mirrored = Speed</c> (Get Parameter → Set Variable). ⭐ The resolver reads the AUTHORED input and writes STATE.
    /// </summary>
    private static BlueprintAsset BehaviourWithResolver(string name, Action<Graph>? tweakResolver = null)
    {
        var asset = BlueprintAssetBuilder.Behavior(name)
            .WithParameter("Speed", typeof(float), "2")
            .WithVariable("Mirrored", typeof(float))
            .WithGraph("Tick", g => g.Entry())
            .Build();
        var speed = asset.Parameters.Single();
        var mirrored = asset.Variables.Single();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var eOut  = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(eOut);
        var get   = new GetParameterNode { Id = Guid.NewGuid(), ParameterId = speed.Id.ToString() };
        var gOut  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        get.Pins.Add(gOut);
        var set   = new SetVariableNode { Id = Guid.NewGuid(), VariableId = mirrored.Id.ToString() };
        var sIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var sOut  = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        var sVal  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        set.Pins.Add(sIn); set.Pins.Add(sOut); set.Pins.Add(sVal);
        var resolver = new Graph
        {
            Id = Guid.NewGuid(), Name = "Resolve", Kind = GraphKind.Construction,
            Nodes = { entry, get, set },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = eOut.Id, ToNodeId = set.Id, ToPinId = sIn.Id },
                new Link { FromNodeId = get.Id,   FromPinId = gOut.Id, ToNodeId = set.Id, ToPinId = sVal.Id },
            },
        };
        tweakResolver?.Invoke(resolver);
        asset.Graphs.Add(resolver);
        return asset;
    }

    private unsafe float AssignAndReadMirrored(string name, string json)
    {
        _fixture.CompileAndLoad(BehaviourWithResolver(name), GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(name, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = name, JsonParams = json });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);
        Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root));
        return *(float*)(root + (int)VarOffset(def!, "Mirrored"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The own resolver runs at ASSIGN</b>, inside the ingress shadow, AFTER the JSON is parsed onto the Parameters:
    /// <c>{"Speed": 7}</c> ⇒ <c>Mirrored == 7</c> before the first tick.
    /// <para>⚠ Inverse-edit red-proof: drop the <c>Resolve_…</c> call from <c>BehaviorParseParams</c> and this reads 0.</para>
    /// </summary>
    [Fact]
    public void CE446_TheOwnResolver_RunsAtAssign_FromTheParsedParameters()
        => Assert.Equal(7f, AssignAndReadMirrored("CE446ResolverJson", "{\"Speed\": 7}"));

    /// <summary>⭐ With no JSON the Parameter keeps its authored default, and the resolver sees it.</summary>
    [Fact]
    public void CE446_TheOwnResolver_SeesTheParameterDefault_WhenTheJsonOmitsIt()
        => Assert.Equal(2f, AssignAndReadMirrored("CE446ResolverDefault", string.Empty));

    private static System.Collections.Generic.IReadOnlyList<Hrot.Blueprints.Core.Compiler.Diagnostics.Diagnostic> Diagnose(BlueprintAsset a)
        => new BlueprintCompiler().Compile(a, GoldenCorpus.Options()).Diagnostics;

    /// <summary>⛔ The authored input is read-only: a resolver that writes a Parameter is BP1675.</summary>
    [Fact]
    public void CE446_AResolverWritingAParameter_IsBP1675()
    {
        var a = BehaviourWithResolver("CE446WritesParam");
        var set = a.Graphs.Single(g => g.Kind == GraphKind.Construction).Nodes.OfType<SetVariableNode>().Single();
        set.VariableId = a.Parameters.Single().Id.ToString();
        Assert.Contains(Diagnose(a), d => d.Code == "BP1675");
    }

    /// <summary>⛔ A behaviour has exactly ONE resolver (R-152): two Construction graphs are BP1676.</summary>
    [Fact]
    public void CE446_TwoResolvers_AreBP1676()
    {
        var a = BehaviourWithResolver("CE446TwoResolvers");
        a.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Resolve2", Kind = GraphKind.Construction });
        Assert.Contains(Diagnose(a), d => d.Code == "BP1676");
    }

    /// <summary>⛔ The block is injected: a resolver graph that declares an input is BP1677.</summary>
    [Fact]
    public void CE446_AResolverDeclaringAnInput_IsBP1677()
    {
        var a = BehaviourWithResolver("CE446ResolverInput", g => g.Inputs.Add(new ParameterDecl
            { Id = Guid.NewGuid(), Name = "X", Type = new BlueprintTypeRef { TypeId = "System.Single" } }));
        Assert.Contains(Diagnose(a), d => d.Code == "BP1677");
    }

    // ── runtime, through the real ingress and brain tick ───────────────────

    /// <summary>
    /// ⭐⭐⭐ assign → it runs (Running while the latent delay waits) → the delay elapses → Return(Success) →
    /// <c>BehaviorFinishedEvent(Success)</c> exactly once → cleared (<c>BrainTier = 0</c>, <c>CE-449</c>).
    /// </summary>
    [Fact]
    public void CE446_ABlueprintBehaviour_WaitsOnItsLatentNode_ThenFinishesAndIsCleared()
    {
        const string Name = "CE446WaitThenSucceed";
        _fixture.CompileAndLoad(WaitThenSucceed(Name));
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Name, out int id), "the generated registrar must register the behaviour by name");
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, def!.BrainTier);

        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, world.GetComponent<BehaviorState>(e).BrainTier);

        var brain = new BrainTickSystem(_fixture.BehaviorRegistry);
        int finished = 0;
        void Frame(float t)
        {
            world.SetSimulationTime(t);
            brain.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index && evt.Result == Fbt.NodeStatus.Success) finished++;
        }

        Frame(10.0f);   // enters the delay (until 11.0)
        Frame(10.5f);   // still waiting
        Assert.Equal(0, finished);
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, world.GetComponent<BehaviorState>(e).BrainTier);

        Frame(11.5f);   // the delay elapsed ⇒ Return(Success)
        Frame(12.0f);   // nothing ticks a finished behaviour
        Assert.Equal(1, finished);
        Assert.Equal(0, world.GetComponent<BehaviorState>(e).BrainTier);
        Assert.False(RootParamsAccess.TryGetRootBytes(world, e, out _), "the block is freed by the clear");
    }

    /// <summary>
    /// ⭐⭐ <b>CE-2018 — THREE waits in one graph resume in order.</b> Tick: Delay(1) → Delay(1) → Delay(1) → Return(Success).
    /// 🔴 Measured 2026-10-02: the resume dispatch chain sent <c>ResumeAt == 3</c> to the SECOND wait's check (each chain
    /// link's else went to <c>check[k+1]</c>, not <c>chain[k+1]</c>), so the third wait re-entered itself forever and the
    /// behaviour never finished. Two waits — every golden — were unaffected.
    /// <para>✅ Red-proof: the old else target ⇒ <c>finished</c> stays 0.</para>
    /// </summary>
    [Fact]
    public void CE2018_ThreeWaitsInOneGraph_ResumeInOrder_AndFinish()
    {
        const string Name = "CE2018ThreeWaits";
        var (e, brain) = AssignBehaviour(BlueprintAssetBuilder.Behavior(Name)
            .WithGraph("Tick", g => g.Entry().Delay(1f).Delay(1f).Delay(1f).Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success))
            .Build());
        var world = _fixture.World;
        int finished = 0;
        void Frame(float t)
        {
            world.SetSimulationTime(t);
            brain.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index && evt.Result == Fbt.NodeStatus.Success) finished++;
        }

        Frame(10.0f);   // wait 1 (until 11)
        Frame(11.5f);   // wait 2 (until 12.5)
        Frame(13.0f);   // wait 3 (until 14)
        Assert.Equal(0, finished);
        Frame(14.5f);   // done ⇒ Return(Success)
        Frame(16.0f);
        Assert.Equal(1, finished);
    }

    // ── S1 (DESIGN_Unified_Behaviour_Run §2 I11–I12): latent correctness, measured before fibers ──

    private const string LocomotionChannelFqn = "LocomotionChannel";   // a wait target is named by its short type name (BP1402)

    private (Fdp.Core.Entity Entity, BrainTickSystem Brain) AssignBehaviour(BlueprintAsset asset)
    {
        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = asset.Name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);
        return (e, new BrainTickSystem(_fixture.BehaviorRegistry));
    }

    /// <summary>
    /// ⭐⭐ <b>S1 / I12 — after a <c>WaitForChannel</c> SUCCEEDS, the next pass starts the graph from the TOP.</b>
    /// Tick: publish <see cref="Runtime.PingDemoEvent"/> (BEFORE the wait) → wait on the locomotion channel → publish
    /// <see cref="Runtime.WhenTestHitEvent"/> (AFTER) → fall off the end (Running, so the Tick graph runs again next frame).
    /// With the channel held at Success, every pass is "before, suspend, after": the two counts stay in step.
    /// <para>🔴 Measured 2026-10-02: the success path resumed without clearing <c>ResumeAt</c>
    /// (<c>WaitLowering_Instance.cs</c>), so every later frame re-entered the resume check and re-ran ONLY the code
    /// after the wait — 5 "after" against 1 "before" in 5 frames. The Failure and Delay paths already cleared it.</para>
    /// </summary>
    [Fact]
    public void S1_AfterAChannelWaitSucceeds_TheNextPassStartsFromTheTop()
    {
        var asset = BlueprintAssetBuilder.Behavior("S1ChannelOnce")
            .WithGraph("Tick", g => g.Entry()
                .PublishCustomEvent(typeof(Runtime.PingDemoEvent).FullName!, targetFieldName: "Target")
                .WaitForChannel(LocomotionChannelFqn)
                .PublishCustomEvent(typeof(Runtime.WhenTestHitEvent).FullName!, targetFieldName: "Target"))
            .Build();
        var (e, brain) = AssignBehaviour(asset);
        var world = _fixture.World;
        world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.LocomotionChannel { Status = Fbt.NodeStatus.Success });

        int before = 0, after = 0;
        for (int i = 0; i < 6; i++)
        {
            brain.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            foreach (var evt in world.Bus.Read<Runtime.PingDemoEvent>())  if (evt.Target.Index == e.Index) before++;
            foreach (var evt in world.Bus.Read<Runtime.WhenTestHitEvent>()) if (evt.Target.Index == e.Index) after++;
        }

        Assert.True(after > 0, "the wait never completed");
        Assert.Equal(before, after);
    }

    /// <summary>
    /// ⭐⭐ <b>S1 / I11 — a latent node in an EVENT graph.</b> Until fibers (S6a) give each graph its own cursor, it must be
    /// refused by a blueprint diagnostic naming the node — never a C# compile error in generated code.
    /// <para>🔴 Measured suspicion: the Event method has no <c>instanceVersion</c> parameter while its latent lowering uses one.</para>
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1658")]
    public void S1_ALatentNodeInAnEventGraph_IsABlueprintDiagnostic_NotAGeneratedCodeError()
    {
        // ⭐ S6a: a BEHAVIOUR's Event graph may now wait (its own fiber); the rule binds an Instance only.
        var asset = BlueprintAssetBuilder.Instance("S1EventDelay")
            .WithGraph("Tick", g => g.Entry())
            .WithEventGraph("OnHit", g => g.Entry(typeof(Runtime.WhenTestHitEvent).FullName!).Delay(1f))
            .Build();

        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Exception? roslyn = null;
        if (result.Succeeded)
            roslyn = Record.Exception(() => _fixture.CompileAndLoad(asset, GoldenCorpus.Options()));

        Assert.True(roslyn == null, "generated code failed to compile: " + roslyn?.Message);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Code == "BP1658");
    }

    // ── S2 (DESIGN_Unified_Behaviour_Run U-1): the block is the blackboard; the brain state is its own slot ──

    private static bool HasCursor(Type t, int depth = 0)
        => depth < 4 && t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
               .Any(f => f.FieldType == typeof(Fdp.Toolkit.Blueprints.BlueprintLatentCursor)
                      || (f.FieldType.IsValueType && !f.FieldType.IsPrimitive && HasCursor(f.FieldType, depth + 1)));

    /// <summary>
    /// ⭐⭐ <b>S2 — the block holds the blackboard only; the cursor and When memory are the brain state.</b>
    /// A latent behaviour with a Parameter, a Variable and a resolver: its block (<c>BlackboardLayoutType</c>) is
    /// <c>{ In; St }</c> with the Parameters at offset 0 and NO cursor anywhere in it; its brain state
    /// (<c>BrainStateLayoutType</c>) starts with the cursor and its width is <c>BrainStateBytes</c>; its resolver takes the
    /// block only. After assign the root STATE slot exists at exactly that width.
    /// ✅ Red-proof: before S2 the block was <c>[Cursor][Params][State]</c> (cursor at 0, no brain-state slot).
    /// </summary>
    [Fact]
    public void S2_TheBlockIsTheBlackboard_AndTheCursorIsTheBrainState()
    {
        const string Name = "S2Split";
        var asset = BehaviourWithResolver(Name);
        var withDelay = BlueprintAssetBuilder.Behavior(Name)
            .WithGraph("Tick", g => g.Entry().Delay(1f).Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success)).Build();
        asset.Graphs.RemoveAll(g => g.Name == "Tick");
        asset.Graphs.AddRange(withDelay.Graphs);

        var src = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options()).GeneratedSource!;
        Assert.Contains("private static void Resolve_Resolve(ref Block __bb, ", src);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Name, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));

        var block = def!.BlackboardLayoutType!;
        Assert.False(HasCursor(block), "the blackboard block must not contain the latent cursor");
        Assert.Equal(0, (int)System.Runtime.InteropServices.Marshal.OffsetOf(block, "In"));
        var exec = def.BrainStateLayoutType!;
        Assert.Equal(typeof(Fdp.Toolkit.Blueprints.BlueprintLatentCursor), exec.GetField("Cursor")!.FieldType);
        Assert.Equal(0, (int)System.Runtime.InteropServices.Marshal.OffsetOf(exec, "Cursor"));
        Assert.True(def.BrainStateBytes >= 16);

        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = "{\"Speed\": 7}" });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);

        Assert.True(RootStateAccess.TryGetRootBytes(world, e, out byte* brain, out int brainBytes));
        Assert.Equal(def.BrainStateBytes, brainBytes);
        Assert.Equal(7f, *(float*)(RootParamsAccessRoot(world, e) + VarOffset(def, "Mirrored")));
    }

    /// <summary>
    /// ⭐⭐ <b>S3</b> (<c>DESIGN_Unified_Behaviour_Run</c> §4) — a blueprint behaviour publishes the SAME two descriptions a
    /// BTree/HSM registrar does: <c>JsonParamsDtoType</c> = the block's <c>In</c> struct, and an Input manifest with one
    /// entry per Parameter at its offset. ⇒ <c>InputBytes</c> is the Params region, not the whole block.
    /// ✅ Red-proof: drop the two registrar lines (<c>CSharpEmitter.EmitBehaviorRegistration</c>) and both are null.
    /// </summary>
    [Fact]
    public void S3_ABlueprintBehaviour_PublishesItsParamsContract_AndItsInputManifest()
    {
        const string Name = "S3Manifest";
        _fixture.CompileAndLoad(BehaviourWithResolver(Name), GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Name, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));

        Assert.NotNull(def!.JsonParamsDtoType);
        Assert.Same(def.BlackboardLayoutType!.GetField("In")!.FieldType, def.JsonParamsDtoType);
        var manifest = Assert.Single(def.ManagedBlackboardVariables!);
        Assert.Equal("Speed", manifest.Name);
        Assert.Equal(typeof(float), manifest.Type);
        Assert.Equal(0, manifest.ByteOffset);
        Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf(def.JsonParamsDtoType!), RootParamsAccess.InputBytes(def));
        Assert.True(RootParamsAccess.InputBytes(def) < System.Runtime.InteropServices.Marshal.SizeOf(def.BlackboardLayoutType),
            "the Input region is the Params half, not the whole block");
    }

    /// <summary>⭐ S3 — a parameterless blueprint behaviour declares an EMPTY manifest (0 Input bytes) and no JSON contract.</summary>
    [Fact]
    public void S3_AParameterlessBlueprintBehaviour_DeclaresAnEmptyManifest()
    {
        const string Name = "S3NoParams";
        var asset = BlueprintAssetBuilder.Behavior(Name)
            .WithVariable("Count", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success)).Build();
        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Name, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));

        Assert.Null(def!.JsonParamsDtoType);
        Assert.NotNull(def.ManagedBlackboardVariables);
        Assert.Empty(def.ManagedBlackboardVariables!);
        Assert.Equal(0, RootParamsAccess.InputBytes(def));
    }

    // ── S5d (DESIGN_Unified_Behaviour_Run §4): a blueprint behaviour HOSTS a behaviour ──────────────────────

    private static int _childTicks;
    private static Fbt.NodeStatus _childEnds = Fbt.NodeStatus.Success;

    private static Fbt.NodeStatus ChildCounts(ref byte bb, ref Fbt.BehaviorTreeState st, ref BTreeContext ctx, int p)
        => ++_childTicks >= 3 ? _childEnds : Fbt.NodeStatus.Running;

    /// <summary>⭐ S6b — ticks counted PER OCCURRENCE (the child's slot key rides its context), so two copies of a child
    /// each need their own three ticks: a rail can tell two slots from one shared slot.</summary>
    private static readonly System.Collections.Generic.Dictionary<int, int> _ticksByOccurrence = new();

    private static Fbt.NodeStatus ChildCountsPerOccurrence(ref byte bb, ref Fbt.BehaviorTreeState st, ref BTreeContext ctx, int p)
    {
        _ticksByOccurrence.TryGetValue(ctx.OccurrenceKey, out int n);
        if (++n >= 3) { _ticksByOccurrence.Remove(ctx.OccurrenceKey); return Fbt.NodeStatus.Success; }   // a fresh run counts again
        _ticksByOccurrence[ctx.OccurrenceKey] = n;
        return Fbt.NodeStatus.Running;
    }

    /// <summary>Registers a BTree child that runs three ticks, then ends with <see cref="_childEnds"/>.</summary>
    private void RegisterCountingChild(string name, bool perOccurrence = false)
    {
        var b = new Fbt.Compiler.BTreeBuilder<byte, BTreeContext>()
            .Sequence(seq => seq.Action(perOccurrence ? ChildCountsPerOccurrence : ChildCounts));
        _fixture.BehaviorRegistry.Register(name, new BehaviorDefinition
        {
            Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Fbt.Runtime.Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
        });
    }

    private (Fdp.Core.Entity e, Func<Fbt.NodeStatus?> frame) AssignAndFramer(string name)
    {
        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);
        var brain = new BrainTickSystem(_fixture.BehaviorRegistry);
        return (e, () =>
        {
            brain.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index) return evt.Result;
            return null;
        });
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S5d — a compiled blueprint behaviour RUNS ANOTHER BEHAVIOUR and waits for it.</b> Tick: Run Behaviour(child) →
    /// Return(Success). The child (a BTree) runs in the host's own site slot — provisioned by ingress from the registrar's
    /// declaration — for its three ticks; on its Success the host continues on Out and finishes.
    /// <para>✅ Red-proof: drop the registrar's <c>HostedChildren.Register</c> line and the first child tick throws (no binding).</para>
    /// </summary>
    [Fact]
    public void S5d_ABlueprintBehaviour_RunsAChildBehaviour_AndContinuesOnItsSuccess()
    {
        const string Host = "S5dHost", Child = "S5dChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = BlueprintAssetBuilder.Behavior(Host)
            .WithGraph("Tick", g => g.Entry().RunBehavior(Child).Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success)).Build();
        var src = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options()).GeneratedSource!;
        Assert.Contains("HostedSubtree.TickFromBlueprint(", src);
        Assert.Contains("HostedChildren.Register(beh, ", src);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (_, frame) = AssignAndFramer(Host);

        Assert.Null(frame());          // reaches the node and suspends
        Assert.Null(frame());          // child tick 1
        Assert.Null(frame());          // child tick 2
        Assert.Equal(Fbt.NodeStatus.Success, frame());   // child tick 3 ⇒ Success ⇒ Out ⇒ Return(Success)
        Assert.Equal(3, _childTicks);
    }

    /// <summary>⭐⭐ S5d — the child's Failure takes OnFailure; unwired, the host's Tick fails (Q#13, as a channel wait).</summary>
    [Fact]
    public void S5d_AChildsFailure_FailsAHostWithNoOnFailure()
    {
        const string Host = "S5dHostF", Child = "S5dChildF";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Failure;
        RegisterCountingChild(Child);
        _fixture.CompileAndLoad(BlueprintAssetBuilder.Behavior(Host)
            .WithGraph("Tick", g => g.Entry().RunBehavior(Child).Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success)).Build(),
            GoldenCorpus.Options());
        var (_, frame) = AssignAndFramer(Host);

        Fbt.NodeStatus? result = null;
        for (int f = 0; f < 6 && result is null; f++) result = frame();
        Assert.Equal(Fbt.NodeStatus.Failure, result);
    }

    /// <summary>⭐ S5d — Run Behaviour outside a behaviour is BP1659: only a behaviour has a brain to run a child under.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1659")]
    public void S5d_RunBehaviourInAnInstance_IsBP1659()
    {
        var asset = BlueprintAssetBuilder.Instance("S5dInInstance")
            .WithGraph("Tick", g => g.Entry().RunBehavior("Anything")).Build();
        Assert.Contains(Diagnose(asset), d => d.Code == "BP1659");
    }

    // ── S6a (DESIGN_Unified_Behaviour_Run §4a): a behaviour's Event graph is a fiber of its own ─────────────────────

    /// <summary>
    /// A behaviour whose Tick never ends and whose Event graph (<see cref="Runtime.WhenTestHitEvent"/>) runs the counting
    /// child, then writes the event's <c>Damage</c> into Variable <c>Got</c>. The read is AFTER the wait, so it only works if
    /// the fiber kept the event it started on.
    /// </summary>
    private static BlueprintAsset WaitingEventHandler(string name, string child,
        EventFiberPolicy policy = EventFiberPolicy.Parallel, int capacity = 0)
    {
        var asset = BlueprintAssetBuilder.Behavior(name)
            .WithVariable("Got", typeof(float))
            .WithGraph("Tick", g => g.Entry())
            .WithEventGraph("OnHit", g => g.WithInput("Damage", "System.Single")
                .Entry(typeof(Runtime.WhenTestHitEvent).FullName!).RunBehavior(child).SetVariable("Got", ""))
            .Build();
        var graph = asset.Graphs.Single(gr => gr.Kind == GraphKind.Event);
        var entry = graph.Nodes.OfType<EventEntryNode>().Single();
        entry.Policy = policy; entry.Capacity = capacity;
        var set   = graph.Nodes.OfType<SetVariableNode>().Single();
        set.VariableId = asset.Variables.Single().Id.ToString();
        var dOut = new Pin { Id = Guid.NewGuid(), Name = "Damage", Direction = "Out", TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var vIn  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        entry.Pins.Add(dOut);
        set.Pins.Add(vIn);
        graph.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = dOut.Id, ToNodeId = set.Id, ToPinId = vIn.Id });
        return asset;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S6a — an Event graph WAITS across frames and keeps its event.</b> The hit arrives once (frame 1); the handler
    /// reaches Run Behaviour and suspends on its OWN cursor while the Tick keeps running; the child ticks on frames 2–4; on
    /// its Success the handler resumes with the saved input and writes <c>Got = 7</c>.
    /// <para>✅ Red-proof: resume the fiber with <c>default</c> inputs instead of the saved ones and <c>Got</c> stays 0.</para>
    /// </summary>
    [Fact]
    public unsafe void S6a_AnEventGraph_WaitsAcrossFrames_AndKeepsItsEvent()
    {
        const string Host = "S6aWaitHost", Child = "S6aWaitChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = WaitingEventHandler(Host, Child);
        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var (e, frame) = AssignAndFramer(Host);
        float Got() => *(float*)(RootParamsAccessRoot(_fixture.World, e) + (int)VarOffset(def!, "Got"));

        _fixture.World.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 7f });
        _fixture.World.Bus.SwapBuffers();           // readable on the next tick (the framer swaps after it)
        Assert.Null(frame());                       // f1: the handler reaches Run Behaviour and suspends
        Assert.Null(frame());                       // f2: child tick 1
        Assert.Null(frame());                       // f3: child tick 2
        Assert.Equal(0f, Got());
        Assert.Null(frame());                       // f4: child tick 3 ⇒ Success ⇒ the handler resumes and writes
        Assert.Equal(3, _childTicks);
        Assert.Equal(7f, Got());
    }

    /// <summary>
    /// ⭐⭐ <b>S6a — Parallel(1): an event arriving while its handler still waits is a FAULT, never a silent drop</b> (U-6).
    /// <para>✅ Red-proof: drop the busy check and the second hit restarts the handler silently — no fault.</para>
    /// </summary>
    [Fact]
    public void S6a_AnEventArrivingWhileItsHandlerWaits_FaultsTheRun()
    {
        const string Host = "S6aOverflowHost", Child = "S6aOverflowChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        _fixture.CompileAndLoad(WaitingEventHandler(Host, Child), GoldenCorpus.Options());
        var world = _fixture.World;
        var (e, _) = AssignAndFramer(Host);
        var brain = new BrainTickSystem(_fixture.BehaviorRegistry);

        world.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 1f });
        world.Bus.SwapBuffers(); brain.Execute(world, 0.016f);          // the handler starts and waits
        world.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 2f });
        world.Bus.SwapBuffers(); brain.Execute(world, 0.016f);          // arrives while it waits
        world.Bus.SwapBuffers();

        var finished = world.Bus.Read<BehaviorFinishedEvent>().ToArray().Where(f => f.Entity.Index == e.Index).ToList();
        Assert.Single(finished);
        Assert.Equal(BehaviorFaultCode.EventOverflow, finished[0].FaultCode);
    }

    /// <summary>⭐ S6a — the Tick graph keeps the shared <c>Cursor</c>; an Event fiber's cursor is its own field.</summary>
    [Fact]
    public void S6a_AWaitingEventGraph_GetsItsOwnCursor_TheTickKeepsTheSharedOne()
    {
        var src = new BlueprintCompiler().Compile(WaitingEventHandler("S6aEmit", "AnyChild"), GoldenCorpus.Options()).GeneratedSource!;
        Assert.Contains("__ex.__fib_OnHit_0.Cursor.ResumeAt", src);
        Assert.Contains("__ex.__fib_OnHit_0.In_Damage", src);
        Assert.Contains("BehaviorFaultCode.EventOverflow", src);
    }

    // ── S6b (DESIGN_Unified_Behaviour_Run U-6): the event policies ─────────────────────────────────────────────────

    /// <summary>Frames one hit per call (published, made readable, ticked); returns Got after the tick.</summary>
    private unsafe Func<float?, float> HitFramer(string host, Fdp.Core.Entity e, BehaviorDefinition def, Func<Fbt.NodeStatus?> frame)
        => damage =>
        {
            if (damage is { } d) _fixture.World.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = d });
            _fixture.World.Bus.SwapBuffers();
            Assert.Null(frame());
            return *(float*)(RootParamsAccessRoot(_fixture.World, e) + (int)VarOffset(def, "Got"));
        };

    /// <summary>
    /// ⭐⭐⭐ <b>S6b — Parallel(2): two hits run two handlers at once, each with its own event and its own child.</b> Hit 7
    /// starts copy 0, hit 9 (one frame later) copy 1; each waits on its OWN child, which counts its own three ticks. So copy
    /// 0 finishes first (7) and copy 1 one frame later (9) — each copy kept its event and its child, and nothing faulted.
    /// <para>✅ Red-proofs: one copy only ⇒ the second hit faults; both copies keyed to ONE child slot ⇒ they drive one
    /// child, copy 1 finishes it first and the writes come out in the wrong order.</para>
    /// </summary>
    [Fact]
    public void S6b_Parallel2_RunsTwoHandlersAtOnce_EachWithItsOwnEvent()
    {
        const string Host = "S6bParHost", Child = "S6bParChild";
        _ticksByOccurrence.Clear();
        RegisterCountingChild(Child, perOccurrence: true);
        _fixture.CompileAndLoad(WaitingEventHandler(Host, Child, EventFiberPolicy.Parallel, 2), GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var (e, frame) = AssignAndFramer(Host);
        var hit = HitFramer(Host, e, def!, frame);

        Assert.Equal(0f, hit(7f));     // copy 0 starts and waits
        Assert.Equal(0f, hit(9f));     // copy 0: its child's tick 1 · copy 1 starts and waits
        Assert.Equal(0f, hit(null));   // copy 0: tick 2 · copy 1: its child's tick 1
        Assert.Equal(7f, hit(null));   // copy 0: tick 3 ⇒ Success ⇒ Got = 7 · copy 1: tick 2
        Assert.Equal(9f, hit(null));   // copy 1: tick 3 ⇒ Success ⇒ Got = 9
    }

    /// <summary>
    /// ⭐⭐ <b>S6b — Restart: the newest event wins.</b> Hit 7 starts the handler; hit 9 arrives while it waits, so the
    /// handler (and the child it hosts) is abandoned and started over on 9. Only 9 is ever written, and nothing faulted.
    /// <para>✅ Red-proof: Parallel(1) instead and the second hit faults the run.</para>
    /// </summary>
    [Fact]
    public void S6b_Restart_TheNewestEventWins_AndTheOldHandlerNeverWrites()
    {
        const string Host = "S6bRstHost", Child = "S6bRstChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        _fixture.CompileAndLoad(WaitingEventHandler(Host, Child, EventFiberPolicy.Restart), GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var (e, frame) = AssignAndFramer(Host);
        var hit = HitFramer(Host, e, def!, frame);

        Assert.Equal(0f, hit(7f));     // starts on 7 and waits
        Assert.Equal(0f, hit(9f));     // resume: child tick 1 · then 9 arrives ⇒ restart on 9
        float got = 0f;
        for (int f = 0; f < 4 && got == 0f; f++) got = hit(null);
        Assert.Equal(9f, got);
    }

    // ── CE-2013 (DESIGN_Typed_Event_Nodes E2): several typed event nodes in one Event graph ─────────────────────────

    /// <summary>One Event graph: Hit and Ping (no payload) both enter ONE tail, Run Behaviour(child) → Count = Count + 1.</summary>
    private static BlueprintAsset SharedRunBehaviourTail(string name, string child,
        EventFiberPolicy hitPolicy = EventFiberPolicy.Parallel, int hitCapacity = 0)
    {
        var asset = BlueprintAssetBuilder.Behavior(name)
            .WithVariable("Count", typeof(int))
            .WithGraph("Tick", g => g.Entry())
            .Build();
        var t = new Runtime.TypedEventGraph();
        var hit  = t.Event(typeof(Runtime.WhenTestHitEvent).FullName!);
        var ping = t.Event(typeof(Runtime.PingDemoEvent).FullName!);
        hit.Policy = hitPolicy; hit.Capacity = hitCapacity;
        var run = t.RunBehavior(child);
        var inc = t.Increment(asset.Variables.Single());
        t.Then(hit, run).Then(ping, run).Then(run, inc);
        asset.Graphs.Add(t.Graph);
        return asset;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>CE-2013 (T-3) — a Run Behaviour in a SHARED tail keeps separate state per handler.</b> Hit and Ping arrive
    /// together; each handler runs the tail's Run Behaviour in its OWN site slot, so the child runs twice (one occurrence
    /// each, three ticks each) and both handlers count: <c>Count = 2</c>.
    /// <para>✅ Red-proof: give the second handler the same node ids (no clone) and the generated class declares one site
    /// field twice — it does not compile.</para>
    /// </summary>
    [Fact]
    public void E2_ARunBehaviourInASharedTail_RunsInItsOwnSlotPerHandler()
    {
        const string Host = "E2SharedHost", Child = "E2SharedChild";
        _ticksByOccurrence.Clear();
        RegisterCountingChild(Child, perOccurrence: true);
        var asset = SharedRunBehaviourTail(Host, Child);
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            compiled.GeneratedSource!, @"public static readonly int __RunSite_\w+ =").Count);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var (e, frame) = AssignAndFramer(Host);
        int Count() => *(int*)(RootParamsAccessRoot(_fixture.World, e) + VarOffset(def!, "Count"));

        _fixture.World.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 1f });
        _fixture.World.Bus.Publish(new Runtime.PingDemoEvent { Value = 1 });
        _fixture.World.Bus.SwapBuffers();
        Assert.Null(frame());                      // f1: both handlers reach Run Behaviour and wait, each in its slot
        Assert.Null(frame());                      // f2: each child's first tick
        Assert.Equal(2, _ticksByOccurrence.Count); // two occurrences of the child — one per handler
        Assert.Null(frame());                      // f3: second ticks
        Assert.Equal(0, Count());
        Assert.Null(frame());                      // f4: both children reach Success ⇒ both handlers count
        Assert.Equal(2, Count());
    }

    /// <summary>
    /// ⭐⭐ <b>CE-2013 — each handler keeps its OWN policy.</b> Hit is Queue(2), Ping the default Parallel(1): only Hit's
    /// handler has a queue, and Ping's has its own fiber copy.
    /// </summary>
    [Fact]
    public void E2_EachHandlerKeepsItsOwnPolicy()
    {
        var compiled = new BlueprintCompiler()
            .Compile(SharedRunBehaviourTail("E2Policies", "AnyChild", EventFiberPolicy.Queue, 2), GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        var src = compiled.GeneratedSource!;
        Assert.Contains("__fib_OnEvents_qCount", src);
        Assert.Contains("__fib_OnEvents1_0", src);      // the second handler's own fiber (the name is sanitized)
        Assert.DoesNotContain("__fib_OnEvents1_qCount", src);
    }

    /// <summary>
    /// ⭐⭐ <b>CE-2014 (T-6) — the WHOLE event survives a wait.</b> Hit's whole-event pin feeds Break Struct AFTER the handler
    /// waits on its child, so the event struct rides the fiber's saved inputs (a project struct of unknown size: the runtime
    /// layout path) and is read on resume: <c>Got = 7</c>.
    /// <para>✅ Red-proof: resume with <c>default</c> saved inputs and <c>Got</c> stays 0 (as S6a's own red-proof).</para>
    /// </summary>
    [Fact]
    public unsafe void E3_TheWholeEvent_IsKeptAcrossAWait_AndSplitOnResume()
    {
        const string Host = "E3WholeHost", Child = "E3WholeChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = BlueprintAssetBuilder.Behavior(Host)
            .WithVariable("Got", typeof(float))
            .WithGraph("Tick", g => g.Entry())
            .Build();
        string hitFqn = typeof(Runtime.WhenTestHitEvent).FullName!;
        var t = new Runtime.TypedEventGraph();
        var hit = t.Event(hitFqn);
        var run = t.RunBehavior(Child);
        var brk = t.BreakStructOf(hitFqn, "System.Single", "Damage");
        var set = t.Set(asset.Variables.Single());
        t.Then(hit, run).Then(run, set).Data(hit, "Event", brk, "Value").Data(brk, "Damage", set, "Value");
        asset.Graphs.Add(t.Graph);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var (e, frame) = AssignAndFramer(Host);
        float Got() => *(float*)(RootParamsAccessRoot(_fixture.World, e) + VarOffset(def!, "Got"));

        _fixture.World.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 7f });
        _fixture.World.Bus.SwapBuffers();
        Assert.Null(frame());                       // f1: waits on the child, holding the event
        Assert.Null(frame());
        Assert.Null(frame());
        Assert.Equal(0f, Got());
        Assert.Null(frame());                       // f4: the child succeeds ⇒ resume ⇒ Break Struct ⇒ Got = 7
        Assert.Equal(7f, Got());
    }

    // ── S7a (DESIGN_Unified_Behaviour_Run "S7 design"): the Behaviour Task node — While Running · Abort ⇒ Failed ─────────

    /// <summary>
    /// A behaviour whose Event graph runs a Behaviour Task on <see cref="Runtime.WhenTestHitEvent"/>; <paramref name="wire"/>
    /// wires the task's outputs. Variables <c>Ticks</c> · <c>Done</c> · <c>Failed</c> are the counters the rails read.
    /// </summary>
    private static BlueprintAsset TaskHost(string name, string child,
        Action<Runtime.TypedEventGraph, RunBehaviorNode, Func<string, VariableDecl>> wire)
    {
        var asset = BlueprintAssetBuilder.Behavior(name)
            .WithVariable("Ticks", typeof(int)).WithVariable("Done", typeof(int)).WithVariable("Failed", typeof(int))
            .WithGraph("Tick", g => g.Entry())
            .Build();
        var t = new Runtime.TypedEventGraph();
        var hit = t.Event(typeof(Runtime.WhenTestHitEvent).FullName!);
        var task = t.RunBehavior(child);
        t.Then(hit, task);
        wire(t, task, n => asset.Variables.Single(v => v.Name == n));
        asset.Graphs.Add(t.Graph);
        return asset;
    }

    private unsafe Func<string, int> IntReader(string host, Fdp.Core.Entity e)
    {
        Assert.True(_fixture.BehaviorRegistry.TryGetId(host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        return n => *(int*)(RootParamsAccessRoot(_fixture.World, e) + VarOffset(def!, n));
    }

    private void Hit()
    {
        _fixture.World.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = 1f });
        _fixture.World.Bus.SwapBuffers();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S7a (D3) — While Running runs on every frame the child is still running, then Succeeded.</b> The child runs
    /// three ticks: frames 2 and 3 return Running (While Running counts each), frame 4 succeeds (Succeeded counts once).
    /// <para>✅ Red-proof: lower the Running arm to <c>ret_void</c> (drop <c>WhileRunningBlock</c>) and <c>Ticks</c> stays 0.</para>
    /// </summary>
    [Fact]
    public void S7a_WhileRunning_RunsEachFrameTheChildRuns_ThenSucceeded()
    {
        const string Host = "S7aWrHost", Child = "S7aWrChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = TaskHost(Host, Child, (t, task, v) =>
        {
            t.Then(task, t.Increment(v("Ticks")), RunBehaviorNode.WhileRunningPin);
            t.Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
        });
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);

        Hit();
        Assert.Null(frame());                               // f1: reaches the task and suspends
        Assert.Equal((0, 0), (read("Ticks"), read("Done")));
        Assert.Null(frame());                               // f2: child Running ⇒ While Running
        Assert.Equal((1, 0), (read("Ticks"), read("Done")));
        Assert.Null(frame());                               // f3: child Running ⇒ While Running
        Assert.Equal((2, 0), (read("Ticks"), read("Done")));
        Assert.Null(frame());                               // f4: child Success ⇒ Succeeded (While Running does not run)
        Assert.Equal((2, 1), (read("Ticks"), read("Done")));
        Assert.Equal(3, _childTicks);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S7a (D4/D5) — an Abort from the task's own While Running stops the child and continues on Failed.</b> The
    /// child would succeed on its third tick; While Running aborts on the first, so the child ticks ONCE, its hosted site
    /// is reset, and the next frame takes Failed — never Succeeded.
    /// <para>✅ Red-proof: drop the aborted arm from the dispatch chain (<c>WaitLowering_Instance</c>) and the next frame
    /// resumes the child instead — it ticks again and Failed stays 0.</para>
    /// </summary>
    [Fact]
    public void S7a_AbortFromWhileRunning_StopsTheChild_AndContinuesOnFailed()
    {
        const string Host = "S7aAbortHost", Child = "S7aAbortChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = TaskHost(Host, Child, (t, task, v) =>
        {
            var tick = t.Increment(v("Ticks"));
            t.Then(task, tick, RunBehaviorNode.WhileRunningPin).ThenInto(tick, "Out", task, RunBehaviorNode.AbortPin);
            t.Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
            t.Then(task, t.Increment(v("Failed")), RunBehaviorNode.FailedPin);
        });
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.Contains("HostedSubtree.Reset(", compiled.GeneratedSource!);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);

        Hit();
        Assert.Null(frame());                               // f1: suspends
        Assert.Null(frame());                               // f2: child Running ⇒ While Running ⇒ Abort
        Assert.Equal(1, read("Ticks"));
        Assert.Null(frame());                               // f3: the aborted arm ⇒ Failed
        Assert.Equal((0, 1), (read("Done"), read("Failed")));
        for (int f = 0; f < 3; f++) Assert.Null(frame());   // nothing more runs: the child is gone
        Assert.Equal(1, _childTicks);
        Assert.Equal((1, 0, 1), (read("Ticks"), read("Done"), read("Failed")));
    }

    /// <summary>
    /// ⭐⭐ <b>S7a — a Behaviour Task in a Tick graph aborts the same way</b> (the AiPrimitive-free instance path, no fiber):
    /// Tick: Task(child) — While Running → Abort, Failed → Return(Success). The behaviour finishes Success on frame 3.
    /// </summary>
    [Fact]
    public void S7a_ATaskInATickGraph_AbortsToFailed()
    {
        const string Host = "S7aTickHost", Child = "S7aTickChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = BlueprintAssetBuilder.Behavior(Host)
            .WithGraph("Tick", g => g.Entry().RunBehaviorWithFailure(Child, f => f.Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success))
                                    .Return(Hrot.Blueprints.Core.Assets.NodeStatus.Failure))
            .Build();
        var tick = asset.Graphs.Single(gr => gr.Name == "Tick");
        var task = tick.Nodes.OfType<RunBehaviorNode>().Single();
        var wr = new Pin { Id = Guid.NewGuid(), Name = RunBehaviorNode.WhileRunningPin, Direction = "Out", IsExec = true, TypeRef = new() };
        var abort = new Pin { Id = Guid.NewGuid(), Name = RunBehaviorNode.AbortPin, Direction = "In", IsExec = true, TypeRef = new() };
        task.Pins.Add(wr); task.Pins.Add(abort);
        tick.Links.Add(new Link { FromNodeId = task.Id, FromPinId = wr.Id, ToNodeId = task.Id, ToPinId = abort.Id });
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (_, frame) = AssignAndFramer(Host);
        Assert.Null(frame());                                  // f1: suspends
        Assert.Null(frame());                                  // f2: Running ⇒ While Running ⇒ Abort
        Assert.Equal(Fbt.NodeStatus.Success, frame());         // f3: aborted ⇒ Failed ⇒ Return(Success)
        Assert.Equal(1, _childTicks);
    }

    /// <summary>⛔ S7a (D3) — While Running is a per-frame chain: a latent node in it is BP1684.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1684")]
    public void S7a_ALatentNodeInWhileRunning_IsBP1684()
        => Assert.Contains(Diagnose(TaskHost("S7aWrLatent", "AnyChild", (t, task, _) =>
               t.Then(task, t.Delay(), RunBehaviorNode.WhileRunningPin))), d => d.Code == "BP1684");

    /// <summary>⛔ S7a (D5) — until S7b, Abort is reachable only from the task's OWN While Running: from Succeeded it is BP1685.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1685")]
    public void S7a_AnAbortFromOutsideItsWhileRunning_IsBP1685()
        => Assert.Contains(Diagnose(TaskHost("S7aAbortOutside", "AnyChild", (t, task, v) =>
           {
               var done = t.Increment(v("Done"));
               t.Then(task, done, RunBehaviorNode.SucceededPin).ThenInto(done, "Out", task, RunBehaviorNode.AbortPin);
           })), d => d.Code == "BP1685");

    /// <summary>
    /// ⭐⭐ <b>S7a (D1) — an S5d asset still loads.</b> The retired kind <c>"RunBehavior"</c> reads as a Behaviour Task, and
    /// its <c>In</c>/<c>Out</c>/<c>OnFailure</c> pins (and the links that name them by deterministic id) become
    /// <c>Start</c>/<c>Succeeded</c>/<c>Failed</c>.
    /// </summary>
    [Fact]
    public void S7a_AnS5dRunBehaviourAsset_MigratesToABehaviourTask()
    {
        var asset = BlueprintAssetBuilder.Behavior("S7aMigrate").WithGraph("Tick", g => g.Entry()).Build();
        var graph = asset.Graphs.Single();
        var entry = graph.Nodes.Single();
        var node = new RunBehaviorNode { Id = Guid.NewGuid(), BehaviorName = "Child" };
        graph.Nodes.Add(node);
        var entryOut = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        Guid Old(string n, string d) => Hrot.Blueprints.Core.Compiler.DeterministicIds.PinId(node.Id, n, d);
        graph.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = node.Id, ToPinId = Old("In", "In") });
        graph.Links.Add(new Link { FromNodeId = node.Id, FromPinId = Old("OnFailure", "Out"), ToNodeId = entry.Id, ToPinId = Guid.NewGuid() });

        var json = Hrot.Blueprints.Core.BlueprintJsonServices.Serialize(asset);
        Assert.Contains("\"BehaviorTask\"", json);
        var legacy = json.Replace("\"BehaviorTask\"", "\"RunBehavior\"");

        var loaded = Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(legacy)!;
        var task = loaded.Graphs.Single().Nodes.OfType<RunBehaviorNode>().Single();
        Assert.Equal("Child", task.BehaviorName);
        var links = loaded.Graphs.Single().Links;
        Assert.Contains(links, l => l.ToNodeId == task.Id && l.ToPinId == Old(RunBehaviorNode.StartPin, "In"));
        Assert.Contains(links, l => l.FromNodeId == task.Id && l.FromPinId == Old(RunBehaviorNode.FailedPin, "Out"));
        Assert.DoesNotContain(links, l => l.ToPinId == Old("In", "In") || l.FromPinId == Old("OnFailure", "Out"));
    }

    // ── S7b (DESIGN_Unified_Behaviour_Run "S7b design"): Started — a task that runs ALONGSIDE ──────────────────────────

    private static int _aCalls, _bTicks;

    private static Fbt.NodeStatus ChildStepA(ref byte bb, ref Fbt.BehaviorTreeState st, ref BTreeContext ctx, int p)
    { _aCalls++; _bTicks = 0; return Fbt.NodeStatus.Success; }   // a (re)started run counts B afresh

    private static Fbt.NodeStatus ChildStepB(ref byte bb, ref Fbt.BehaviorTreeState st, ref BTreeContext ctx, int p)
        => ++_bTicks >= 3 ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Running;

    /// <summary>A BTree child: Sequence(A, B) — A succeeds at once (counted), B runs until its third tick. A reset child
    /// starts over at A, so <see cref="_aCalls"/> counts the (re)starts.</summary>
    private void RegisterTwoStepChild(string name)
    {
        var b = new Fbt.Compiler.BTreeBuilder<byte, BTreeContext>()
            .Sequence(seq => seq.Action(ChildStepA).Action(ChildStepB));
        _fixture.BehaviorRegistry.Register(name, new BehaviorDefinition
        {
            Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Fbt.Runtime.Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
        });
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S7b (B1) — a Started task runs ALONGSIDE: the graph continues at once, the task finishes later.</b> The hit
    /// starts the task and its Started chain counts in the SAME frame (the handler does not wait — it is not even a fiber);
    /// the task runs in its own task fiber, ticking the child on frames 2–4, and its Succeeded chain counts on frame 4.
    /// <para>✅ Red-proof: skip the lift (Stage 2.6) and the Started chain never runs (the task is run-and-wait again).</para>
    /// </summary>
    [Fact]
    public void S7b_AStartedTask_RunsAlongside_TheGraphContinuesAtOnce()
    {
        const string Host = "S7bAlongHost", Child = "S7bAlongChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = TaskHost(Host, Child, (t, task, v) =>
        {
            t.Then(task, t.Increment(v("Ticks")), RunBehaviorNode.StartedPin);
            t.Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
        });
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        var src = compiled.GeneratedSource!;
        Assert.Contains("__fib_OnEventsTask0", src);                // the task fiber (its name sanitized)
        Assert.DoesNotContain("__fib_OnEvents_0", src);             // the handler itself never waits
        Assert.DoesNotContain("__EvtId_OnEvents_Task_0", src);      // a task fiber has no event (B5's dead edge)

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);

        Hit();
        Assert.Null(frame());                                       // f1: start ⇒ Started chain runs at once
        Assert.Equal((1, 0), (read("Ticks"), read("Done")));
        Assert.Null(frame());                                       // f2: child tick 1
        Assert.Null(frame());                                       // f3: child tick 2
        Assert.Equal(0, read("Done"));
        Assert.Null(frame());                                       // f4: child tick 3 ⇒ Succeeded (in the task fiber)
        Assert.Equal((1, 1), (read("Ticks"), read("Done")));
        Assert.Equal(3, _childTicks);
    }

    /// <summary>
    /// ⭐⭐ <b>S7b (B2) — Start while it runs RESTARTS the task.</b> A second hit while the child is mid-way resets the child
    /// (it starts over at its first step) and the task finishes once, on the second start's schedule.
    /// <para>✅ Red-proof: drop the reset from the shared Restart helper and the child continues instead (A runs once).</para>
    /// </summary>
    [Fact]
    public void S7b_StartWhileRunning_RestartsTheTask()
    {
        const string Host = "S7bRestartHost", Child = "S7bRestartChild";
        _aCalls = 0; _bTicks = 0;
        RegisterTwoStepChild(Child);
        var asset = TaskHost(Host, Child, (t, task, v) =>
        {
            t.Then(task, t.Increment(v("Ticks")), RunBehaviorNode.StartedPin);
            t.Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
        });
        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);

        Hit();
        Assert.Null(frame());                                       // f1: start
        Assert.Null(frame());                                       // f2: A ✓, B tick 1
        Assert.Equal(1, _aCalls);
        Hit();
        Assert.Null(frame());                                       // f3: B tick 2 · then the second hit ⇒ RESTART
        Assert.Equal(2, read("Ticks"));
        Assert.Null(frame());                                       // f4: the reset child starts over: A again
        Assert.Equal(2, _aCalls);
        Assert.Equal(0, read("Done"));
        int done = 0;
        for (int f = 0; f < 4 && done == 0; f++) { Assert.Null(frame()); done = read("Done"); }
        Assert.Equal(1, done);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S7b (B3) — the timeout shape: Started → Delay → Abort stops the task and takes Failed.</b> The handler waits
    /// on a zero Delay after starting the task; when it fires it aborts the task, whose fiber continues on Failed next
    /// frame — never Succeeded, and the child is not ticked again.
    /// <para>✅ Red-proof: emit the abort op as a no-op and the task runs on to Succeeded.</para>
    /// </summary>
    [Fact]
    public void S7b_AbortFromTheStartedChain_StopsTheTask_AndTakesFailed()
    {
        const string Host = "S7bTimeoutHost", Child = "S7bTimeoutChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        var asset = TaskHost(Host, Child, (t, task, v) =>
        {
            var wait = t.Delay(0f);
            t.Then(task, wait, RunBehaviorNode.StartedPin).ThenInto(wait, "Out", task, RunBehaviorNode.AbortPin);
            t.Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
            t.Then(task, t.Increment(v("Failed")), RunBehaviorNode.FailedPin);
        });
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);

        Hit();
        int failed = 0;
        for (int f = 0; f < 6 && failed == 0; f++) { Assert.Null(frame()); failed = read("Failed"); }
        Assert.Equal(1, failed);
        for (int f = 0; f < 4; f++) Assert.Null(frame());
        Assert.Equal((0, 1), (read("Done"), read("Failed")));
        Assert.True(_childTicks < 3, $"the child ran to its end ({_childTicks} ticks) — the abort did not stop it");
    }

    /// <summary>⛔ S7b (B4) — a Started task's chains run in their own fiber: reading the EVENT that started it is BP1687.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1687")]
    public void S7b_AStartedTaskReadingTheStartingEvent_IsBP1687()
    {
        string hitFqn = typeof(Runtime.WhenTestHitEvent).FullName!;
        var asset = BlueprintAssetBuilder.Behavior("S7bReadsEvent")
            .WithVariable("Got", typeof(float)).WithGraph("Tick", g => g.Entry()).Build();
        var t = new Runtime.TypedEventGraph();
        var hit = t.Event(hitFqn);
        var task = t.RunBehavior("AnyChild");
        var brk = t.BreakStructOf(hitFqn, "System.Single", "Damage");
        var set = t.Set(asset.Variables.Single());
        t.Then(hit, task).Then(task, t.Increment(asset.Variables.Single()), RunBehaviorNode.StartedPin)
         .Then(task, set, RunBehaviorNode.SucceededPin)
         .Data(hit, "Event", brk, "Value").Data(brk, "Damage", set, "Value");
        asset.Graphs.Add(t.Graph);
        Assert.Contains(Diagnose(asset), d => d.Code == "BP1687");
    }

    /// <summary>⛔ S7b (B3) — with Started wired, an Abort from the Succeeded chain (the task has ended) is still BP1685.</summary>
    [Fact]
    public void S7b_AnAbortFromTheSucceededChain_IsStillBP1685()
        => Assert.Contains(Diagnose(TaskHost("S7bAbortAfterEnd", "AnyChild", (t, task, v) =>
           {
               t.Then(task, t.Increment(v("Ticks")), RunBehaviorNode.StartedPin);
               var done = t.Increment(v("Done"));
               t.Then(task, done, RunBehaviorNode.SucceededPin).ThenInto(done, "Out", task, RunBehaviorNode.AbortPin);
           })), d => d.Code == "BP1685");

    // ── S8 (DESIGN_Unified_Behaviour_Run "S8 design"): a Behaviour Task's Params ──────────────────────────────────────

    private static readonly System.Collections.Generic.List<float> _childSaw = new();
    private static int _childTicksLeft;

    /// <summary>The child records each NEW Value its block holds (what it was seeded with; a restart re-seeds), then runs
    /// three ticks per value.</summary>
    private static Fbt.NodeStatus ChildReadsParams(ref byte bb, ref Fbt.BehaviorTreeState st, ref BTreeContext ctx, int p)
    {
        float v = System.Runtime.CompilerServices.Unsafe.As<byte, Runtime.S8TaskParams>(ref bb).Value;
        if (_childSaw.Count == 0 || _childSaw[^1] != v) { _childSaw.Add(v); _childTicksLeft = 3; }
        return --_childTicksLeft <= 0 ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Running;
    }

    /// <summary>A BTree child whose block IS <see cref="Runtime.S8TaskParams"/> (no manifest, no resolver) ⇒ a host binds
    /// exactly that struct (<c>BehaviorRegistry.TryGetHostedInputType</c>).</summary>
    /// <param name="generated">⭐ S8b — register the shape a GENERATED BTree/HSM registrar emits instead: a manifest and
    /// its Inputs struct as <c>JsonParamsDtoType</c> (<paramref name="noInputs"/>: an empty manifest and no contract).</param>
    private void RegisterParamsReadingChild(string name, bool generated = false, bool noInputs = false)
    {
        _childSaw.Clear(); _childTicksLeft = 0;
        var b = new Fbt.Compiler.BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(ChildReadsParams));
        var def = new BehaviorDefinition
        {
            Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter = new Fbt.Runtime.Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
        };
        if (!generated) def.BlackboardLayoutType = typeof(Runtime.S8TaskParams);
        _fixture.BehaviorRegistry.Register(name, !generated ? def : new BehaviorDefinition
        {
            Name = name, BrainTier = BehaviorConstants.BrainTierBTree, BTreeInterpreter = def.BTreeInterpreter,
            ManagedBlackboardVariables = noInputs
                ? Array.Empty<ManagedBlackboardVariable>()
                : new[] { new ManagedBlackboardVariable("Value", typeof(float), 0) },
            JsonParamsDtoType = noInputs ? null : typeof(Runtime.S8TaskParams),
            BlackboardLayoutType = typeof(Runtime.S8TaskParams),   // its block (here: just the Inputs half)
        });
    }

    /// <summary>OnHit: Task(child) with Params = Make S8TaskParams { Value = hit.Damage }; <paramref name="alongside"/> wires
    /// Started (→ Ticks + 1), else the task is waited for; Succeeded → Done + 1.</summary>
    private static BlueprintAsset ParamsHost(string name, string child, bool alongside, string? paramsTypeId = null)
    {
        string hitFqn = typeof(Runtime.WhenTestHitEvent).FullName!, paramsFqn = paramsTypeId ?? typeof(Runtime.S8TaskParams).FullName!;
        return TaskHostWith(name, (t, v) =>
        {
            var hit  = t.Event(hitFqn);
            hit.Policy = EventFiberPolicy.Parallel; hit.Capacity = 4;
            var task = t.RunBehaviorWithParams(child, paramsFqn);
            var brk  = t.BreakStructOf(hitFqn, "System.Single", "Damage");
            var make = t.MakeStruct(paramsFqn, "System.Single", "Value");
            t.Then(hit, task).Data(hit, "Event", brk, "Value").Data(brk, "Damage", make, "Value")
             .Data(make, "Value", task, RunBehaviorNode.ParamsPin)
             .Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
            if (alongside) t.Then(task, t.Increment(v("Ticks")), RunBehaviorNode.StartedPin);
        });
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-2026</c> (S8c) — a <c>Params</c> pin bound to a GENERATED child keeps its host's exact layout, in the real
    /// generators.</b>
    ///
    /// <para>
    /// 🔴 RED before: the hidden host variable is typed as the child's generated Inputs struct — a type the BTree generator emits
    /// in the SAME run, which the blueprint generator cannot see. Its size oracle was Roslyn-only ⇒ the AN2 4-byte GUESS,
    /// <c>SizeReliable = false</c> ⇒ <c>CSharpEmitter.LayoutFromRuntime</c> dropped the WHOLE host block (<c>Vars</c>/<c>Block</c>) to Sequential with
    /// runtime offsets (and the debug map's state layout with it). ⭐ It still compiled — the measured effect is the layout, not a
    /// build break. ⭐ S8b-2 made the editor offer exactly that type. Now <c>GeneratedTypeCatalog</c> sizes it from the sibling
    /// <c>*.btree.json</c> and the host stays Explicit.
    /// </para>
    /// </summary>
    [Fact]
    public void S8c_AParamsPinBoundToAGeneratedChild_KeepsTheHostsExactLayout_InTheRealGenerators()
    {
        var child = new Hrot.AiEditor.Persistence.BTree.BehaviorTreeAssetDto { AssetId = Guid.NewGuid(), Name = "S8cChild" };
        child.Blackboard.Managed = true;
        child.Blackboard.Variables.Add(new Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto
        { Name = "Value", Type = new Hrot.AiEditor.Persistence.BTree.BlackboardTypeRefDto { TypeId = "System.Single" } });
        string childInputs = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.InputsStructTypeId(child)!;

        var result = Integration.AuthoringPath.Generate(
            new[] { TickParamsHost("S8cHost", "S8cChild", childInputs) },
            new[] { ("/authoring/S8cChild.btree.json", Hrot.AiEditor.Persistence.BTree.BTreeJsonServices.Serialize(child)) });

        Assert.True(result.Clean, result.Report());
        string host = result.GeneratedSources.Single(src => src.Contains("class S8cHost"));
        Assert.Contains("public global::" + childInputs + " __TaskParams_", host);   // the hidden variable IS the child's struct

        // CONTROL: the same host over a params type Roslyn CAN see is Explicit — so a Sequential host below is the sibling type's doing.
        var control = Integration.AuthoringPath.Generate(TickParamsHost("S8cControl", "S8cChild", typeof(Runtime.S8TaskParams).FullName!));
        Assert.True(control.Clean, control.Report());
        Assert.Contains("LayoutKind.Explicit", VarsLayout(control.GeneratedSources.Single(src => src.Contains("class S8cControl"))));

        // ⭐ the hidden variable's size is EXACT (the sibling's own packing), so the host keeps its declared layout
        Assert.Contains("LayoutKind.Explicit", VarsLayout(host));

        static string VarsLayout(string source)
        {
            string[] lines = source.Split('\n');
            int vars = Array.FindIndex(lines, l => l.Trim() == "public struct Vars");
            Assert.True(vars > 0, "the host declares its Vars struct");
            return lines[vars - 1];
        }
    }

    /// <summary>
    /// <c>Tick</c>: Task(child) with Params = Make {paramsTypeId} (its <c>Value</c> left at default); Succeeded → Done + 1. ⚠ Driven
    /// from the Tick graph, not an event: an event fiber keeps its payload in a slot whose size is never oracle-checked (pin
    /// types), which alone drops a host to runtime layout — so only a Tick host shows what the PARAMS variable does to it.
    /// </summary>
    private static BlueprintAsset TickParamsHost(string name, string child, string paramsTypeId)
    {
        var asset = BlueprintAssetBuilder.Behavior(name).WithVariable("Done", typeof(int)).Build();
        var t = new Runtime.TypedEventGraph("Tick", GraphKind.Function);
        var entry = t.Entry();
        var task = t.RunBehaviorWithParams(child, paramsTypeId);
        var make = t.MakeStruct(paramsTypeId, "System.Single", "Value");
        t.Then(entry, task).Data(make, "Value", task, RunBehaviorNode.ParamsPin)
         .Then(task, t.Increment(asset.Variables.Single(v => v.Name == "Done")), RunBehaviorNode.SucceededPin);
        asset.Graphs.Add(t.Graph);
        return asset;
    }

    private static BlueprintAsset TaskHostWith(string name, Action<Runtime.TypedEventGraph, Func<string, VariableDecl>> build)
    {
        var asset = BlueprintAssetBuilder.Behavior(name)
            .WithVariable("Ticks", typeof(int)).WithVariable("Done", typeof(int)).WithVariable("Failed", typeof(int))
            .WithGraph("Tick", g => g.Entry())
            .Build();
        var t = new Runtime.TypedEventGraph();
        build(t, n => asset.Variables.Single(v => v.Name == n));
        asset.Graphs.Add(t.Graph);
        return asset;
    }

    private void HitWith(float damage)
    {
        _fixture.World.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = damage });
        _fixture.World.Bus.SwapBuffers();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S8 (P1/P2) — the Params pin seeds the child at its start.</b> The hit's Damage (7) goes through Make Struct
    /// into the task's Params; the child's FIRST tick reads 7 from its own block — copied from the hidden host variable
    /// the Start path wrote (CE-431's binding, now passed by the blueprint host).
    /// <para>✅ Red-proof: emit the tick without the binding argument and the child reads its default 0.</para>
    /// </summary>
    [Fact]
    public void S8_TheParamsPin_SeedsTheChild_AtItsStart()
    {
        const string Host = "S8ParamsHost", Child = "S8ParamsChild";
        RegisterParamsReadingChild(Child);
        var asset = ParamsHost(Host, Child, alongside: false);
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.Contains("__RunBind_", compiled.GeneratedSource!);
        Assert.Contains("__TaskParams_", compiled.GeneratedSource!);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);
        HitWith(7f);
        for (int f = 0; f < 6 && read("Done") == 0; f++) Assert.Null(frame());
        Assert.Equal(new[] { 7f }, _childSaw);
        Assert.Equal(1, read("Done"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S8 (P4) — a STARTED task takes its Params from the starting event, and a restart takes the new ones.</b>
    /// The Set Variable stays in the starting graph (the event is there), so the task fiber's child reads 7; a second hit
    /// (9) while it runs restarts it, and the restarted child reads 9.
    /// </summary>
    [Fact]
    public void S8_AStartedTask_TakesItsParamsFromTheStartingEvent_AndARestartTakesTheNewOnes()
    {
        const string Host = "S8AlongHost", Child = "S8AlongChild";
        RegisterParamsReadingChild(Child);
        var asset = ParamsHost(Host, Child, alongside: true);
        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);

        HitWith(7f);
        Assert.Null(frame());                     // f1: Set Params = 7, start the task fiber, Started ⇒ Ticks 1
        Assert.Equal(1, read("Ticks"));
        Assert.Null(frame());                     // f2: the child's first tick reads 7
        Assert.Equal(new[] { 7f }, _childSaw);
        HitWith(9f);
        Assert.Null(frame());                     // f3: the child ticks · the second hit ⇒ Params = 9, RESTART
        Assert.Equal(2, read("Ticks"));
        for (int f = 0; f < 6 && read("Done") == 0; f++) Assert.Null(frame());
        Assert.Equal(new[] { 7f, 9f }, _childSaw);
        Assert.Equal(1, read("Done"));
    }

    /// <summary>⭐ S8 — an unwired Params pin changes nothing: no binding, and the child starts from its defaults (0).</summary>
    [Fact]
    public void S8_AnUnwiredParamsPin_BindsNothing()
    {
        const string Host = "S8UnwiredHost", Child = "S8UnwiredChild";
        RegisterParamsReadingChild(Child);
        var asset = TaskHostWith(Host, (t, v) =>
        {
            var hit  = t.Event(typeof(Runtime.WhenTestHitEvent).FullName!);
            var task = t.RunBehaviorWithParams(Child, typeof(Runtime.S8TaskParams).FullName!);
            t.Then(hit, task).Then(task, t.Increment(v("Done")), RunBehaviorNode.SucceededPin);
        });
        var compiled = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.DoesNotContain("__RunBind_", compiled.GeneratedSource!);

        _fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);
        HitWith(7f);
        for (int f = 0; f < 6 && read("Done") == 0; f++) Assert.Null(frame());
        Assert.Equal(new[] { 0f }, _childSaw);
    }

    /// <summary>
    /// ⭐⭐ S8 (P5) — <c>BehaviorRegistry.TryGetHostedInputType</c>, the one answer the drawer asks: a child with a block
    /// layout and no manifest binds its layout; a blueprint behaviour binds its generated <c>Params</c>; one with no
    /// parameters binds nothing.
    /// </summary>
    [Fact]
    public void S8_TheHostedInputType_IsTheChildsAuthoredInput()
    {
        RegisterParamsReadingChild("S8TypeLayoutChild");
        _fixture.CompileAndLoadMany(new[]
        {
            BlueprintAssetBuilder.Behavior("S8TypeBpWithParams").WithParameter("Speed", typeof(float))
                .WithGraph("Tick", g => g.Entry()).Build(),
            BlueprintAssetBuilder.Behavior("S8TypeBpNoParams").WithGraph("Tick", g => g.Entry()).Build(),
        }, GoldenCorpus.Options());
        var reg = _fixture.BehaviorRegistry;

        Assert.True(reg.TryGetHostedInputType("S8TypeLayoutChild", out var layout));
        Assert.Equal(typeof(Runtime.S8TaskParams), layout);
        Assert.True(reg.TryGetHostedInputType("S8TypeBpWithParams", out var bp));
        Assert.Equal("Params", bp.Name);
        Assert.False(reg.TryGetHostedInputType("S8TypeBpNoParams", out _));
        // ⭐ S8b-2: the editor's spelling of this answer is ChildInputTypes (Hrot.Editor.Tests, ChildInputTypesTests).
    }

    /// <summary>
    /// ⭐⭐ <b>S8b-1 (CE-2024) — a GENERATED child (it has a manifest: BTree, HSM) answers with its Inputs struct</b>, its
    /// <c>JsonParamsDtoType</c>, and a Behaviour Task seeds it. 🔴 S8 read <c>JsonParamsDtoType</c> for blueprints only, so a
    /// BTree/HSM child had no Params pin. One with a manifest but no Inputs struct answers nothing.
    /// <para>✅ Red-proof: restore S8's blueprint-only branch and the first assert fails.</para>
    /// </summary>
    [Fact]
    public void S8b_AGeneratedChild_AnswersWithItsInputsStruct_AndIsSeeded()
    {
        const string Host = "S8bHost", Child = "S8bGeneratedChild", NoInputs = "S8bNoInputsChild";
        RegisterParamsReadingChild(NoInputs, generated: true, noInputs: true);
        RegisterParamsReadingChild(Child, generated: true);

        Assert.True(_fixture.BehaviorRegistry.TryGetHostedInputType(Child, out var type));
        Assert.Equal(typeof(Runtime.S8TaskParams), type);
        Assert.False(_fixture.BehaviorRegistry.TryGetHostedInputType(NoInputs, out _));

        _fixture.CompileAndLoad(ParamsHost(Host, Child, alongside: false), GoldenCorpus.Options());
        var (e, frame) = AssignAndFramer(Host);
        var read = IntReader(Host, e);
        HitWith(5f);
        for (int f = 0; f < 6 && read("Done") == 0; f++) Assert.Null(frame());
        Assert.Equal(new[] { 5f }, _childSaw);
    }

    // ── §7 demos (DESIGN_Unified_Behaviour_Run §7): the SHIPPED demo assets, run through the real BrainTickSystem ────────

    /// <summary>A frame that also advances the world's simulation time by the frame's 16 ms — what a Delay reads
    /// (<c>BlueprintRunner</c> passes <c>repo.SimulationTime</c>); the plain framer leaves it at 0.</summary>
    private Func<Fbt.NodeStatus?> Timed(Func<Fbt.NodeStatus?> frame)
    {
        float t = 0f;
        return () => { t += 0.016f; _fixture.World.SetSimulationTime(t); return frame(); };
    }

    /// <summary>Loads the shipped Demo_TaskChain and its three step children (each: Delay → Success), and assigns it.</summary>
    private (Fdp.Core.Entity e, Func<Fbt.NodeStatus?> frame, Func<string, int> readInt, Action<string> setTrue) LoadTaskChain()
    {
        const string Host = "Demo_TaskChain";
        var assets = new[] { "Demo_Advance", "Demo_Engage", "Demo_Retreat", Host }.Select(GoldenCorpus.Load).ToList();
        _fixture.CompileAndLoadMany(assets, GoldenCorpus.Options());
        var (e, step) = AssignAndFramer(Host);
        var frame = Timed(step);
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        unsafe
        {
            // ⚠ A finished behaviour's block is cleared: a read after the end returns the last value seen.
            var last = new System.Collections.Generic.Dictionary<string, int>();
            int ReadInt(string n)
            {
                if (RootParamsAccess.TryGetRootBytes(_fixture.World, e, out byte* root))
                    last[n] = *(int*)(root + VarOffset(def!, n));
                return last.TryGetValue(n, out int v) ? v : 0;
            }
            void SetTrue(string n) => *(bool*)(RootParamsAccessRoot(_fixture.World, e) + VarOffset(def!, n)) = true;
            return (e, frame, ReadInt, SetTrue);
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Demo_TaskChain (§7) — each Behaviour Task runs in order and the chain finishes.</b> Advance (1 s) ─Succeeded→
    /// Stage = 1 → Engage (2 s) ─Succeeded→ Stage = 2 → Return Success. ⚠ The last write and the end share a frame (the
    /// block is then cleared), so the end is pinned by its RESULT and its TIMING: Success ~2 s after Stage 1.
    /// </summary>
    [Fact]
    public void Demo_TaskChain_RunsEachTaskInOrder_AndSucceeds()
    {
        var (_, frame, readInt, _) = LoadTaskChain();
        int stage1At = -1, endAt = -1;
        Fbt.NodeStatus? result = null;
        for (int f = 1; f <= 400 && result is null; f++)
        {
            result = frame();
            if (stage1At < 0 && readInt("Stage") == 1) stage1At = f;
            if (result is not null) endAt = f;
        }
        Assert.Equal(Fbt.NodeStatus.Success, result);
        Assert.InRange(stage1At, 55, 75);                   // ~1 s of 16 ms frames
        Assert.InRange(endAt - stage1At, 115, 140);         // Engage's ~2 s, then Stage 2 ⇒ Success
        Assert.Equal(0, readInt("Retreated") & 0xFF);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Demo_TaskChain (§7) — Abort stops a running task and the chain retreats.</b> Once Engage runs (Stage 1),
    /// setting <c>CallOff</c> makes Engage's While Running abort it ⇒ Failed ⇒ Retreat (0.5 s) ⇒ Return Failure; Stage
    /// never reaches 2.
    /// </summary>
    [Fact]
    public unsafe void Demo_TaskChain_CallOff_AbortsTheRunningTask_AndRetreats()
    {
        var (e, frame, readInt, setTrue) = LoadTaskChain();
        Fbt.NodeStatus? result = null;
        int f = 0;
        for (; f < 200 && readInt("Stage") != 1; f++) Assert.Null(frame());
        Assert.Equal(1, readInt("Stage"));
        for (int k = 0; k < 10; k++) Assert.Null(frame());   // Engage is running
        setTrue("CallOff");
        int abortedAt = f + 10;
        for (; f < 600 && result is null; f++) result = frame();
        Assert.Equal(Fbt.NodeStatus.Failure, result);
        Assert.Equal(1, readInt("Stage"));                    // Stage 2 never written
        Assert.InRange(f - abortedAt, 25, 45);                // the 0.5 s Retreat (an unwired failure would end at once)
    }

    /// <summary>Loads the shipped Demo_MissionPlan and its step children, and assigns it.</summary>
    private (Fdp.Core.Entity e, Func<Fbt.NodeStatus?> frame, Func<string, int> readInt) LoadMissionPlan()
    {
        const string Host = "Demo_MissionPlan";
        var assets = new[] { "Demo_Advance", "Demo_Engage", "Demo_Retreat", "Demo_TakeCover", Host }
            .Select(GoldenCorpus.Load).ToList();
        _fixture.CompileAndLoadMany(assets, GoldenCorpus.Options());
        var (e, step) = AssignAndFramer(Host);
        var frame = Timed(step);
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        unsafe
        {
            // ⚠ A finished behaviour's block is cleared: a read after the end returns the last value seen.
            var last = new System.Collections.Generic.Dictionary<string, int>();
            int ReadInt(string n)
            {
                if (RootParamsAccess.TryGetRootBytes(_fixture.World, e, out byte* root))
                    last[n] = *(int*)(root + VarOffset(def!, n));
                return last.TryGetValue(n, out int v) ? v : 0;
            }
            return (e, frame, ReadInt);
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Demo_MissionPlan (§7, the concept capstone) — untouched, the mission runs its three legs.</b> Advance (1 s) →
    /// Phase 1 → Defend (2 s) → Phase 2 → Return (1 s) → Phase 3 → Success; Health stays 100, no cover taken.
    /// </summary>
    [Fact]
    public void Demo_MissionPlan_Untouched_RunsItsThreeLegs_AndSucceeds()
    {
        var (_, frame, readInt) = LoadMissionPlan();
        var phases = new System.Collections.Generic.List<int>();
        Fbt.NodeStatus? result = null;
        int f = 0;
        for (; f < 600 && result is null; f++)
        {
            result = frame();
            int p = readInt("Phase");
            readInt("Health"); readInt("CoverTaken");       // seen while it runs (the block is cleared at the end)
            if (phases.Count == 0 || phases[^1] != p) phases.Add(p);
        }
        Assert.Equal(Fbt.NodeStatus.Success, result);
        Assert.Equal(new[] { 0, 1, 2 }, phases);            // Phase 3 and the end share the last frame
        Assert.InRange(f, 235, 265);                        // 1 s + 2 s + 1 s
        Assert.Equal((100, 0), (readInt("Health"), readInt("CoverTaken")));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Demo_MissionPlan — hits take cover ALONGSIDE, and low health aborts the defence.</b> During Defend, eight
    /// hits on self arrive one per frame: each costs 10 Health and starts Take Cover RUNNING ALONGSIDE (its Started chain
    /// counts at once — the handler never waits; a later hit restarts the cover). At Health 20 Defend's While Running
    /// aborts it ⇒ Failed ⇒ Retreat ⇒ Phase −1 ⇒ Failure — the Return leg never runs.
    /// </summary>
    [Fact]
    public void Demo_MissionPlan_Hits_TakeCoverAlongside_AndLowHealthAbortsTheDefence()
    {
        var (e, frame, readInt) = LoadMissionPlan();
        int f = 0;
        for (; f < 200 && readInt("Phase") != 1; f++) Assert.Null(frame());
        Assert.Equal(1, readInt("Phase"));
        for (int k = 0; k < 8; k++)
        {
            _fixture.World.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.HitEvent { HitEntity = e });
            _fixture.World.Bus.SwapBuffers();
            Assert.Null(frame());
            Assert.Equal(k + 1, readInt("CoverTaken"));       // the Started chain ran in the hit's own frame
        }
        Assert.Equal(20, readInt("Health"));
        Fbt.NodeStatus? result = null;
        int retreatFrames = 0;
        for (; retreatFrames < 120 && result is null; retreatFrames++) result = frame();
        Assert.Equal(Fbt.NodeStatus.Failure, result);
        Assert.Equal(1, readInt("Phase"));                  // the Return leg (Phase 2) never ran
        Assert.InRange(retreatFrames, 25, 45);              // Retreat's 0.5 s after the abort
    }

    /// <summary>⭐ The shipped demos are registered by the PRODUCTION scan as blueprint behaviours (the generator compiled them).</summary>
    [Theory]
    [InlineData("Demo_TaskChain")]
    [InlineData("Demo_MissionPlan")]
    [InlineData("Demo_Advance")]
    public void TheShippedDemos_AreRegisteredByTheProductionScan(string name)
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        var staging = new Fdp.Toolkit.Blueprints.BlueprintRegistryStaging();
        var beh     = new BehaviorRegistry();
        Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner.Scan(
            typeof(Hrot.AI.Behaviors.BpComponentDemo).Assembly, staging, beh, skipOnUnknownParam: true);
        Assert.True(beh.TryGetId(name, out int id), $"the generated registrar must register '{name}'");
        Assert.True(beh.TryGetDefinition(id, out var def));
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, def!.BrainTier);
    }

    /// <summary>⭐ S6b — the policy limits are a blueprint diagnostic (a fixed layout needs a bounded compile-time N).</summary>
    [Theory]
    [CoversDiagnosticCode("BP1681")]
    [InlineData(EventFiberPolicy.Parallel, 17)]
    [InlineData(EventFiberPolicy.Restart, 2)]
    [InlineData(EventFiberPolicy.Queue, 17)]
    public void S6b_APolicyOutOfRange_IsBP1681(EventFiberPolicy policy, int capacity)
        => Assert.Contains(Diagnose(WaitingEventHandler("S6bBad", "AnyChild", policy, capacity)), d => d.Code == "BP1681");

    /// <summary>
    /// ⭐⭐⭐ <b>S6 — a WAITING handler is saved to the recording and restored on replay</b> (user, 2026-10-02: "the blueprint
    /// state [must be] correctly saved to the recordings and restored on replay"). Through the real Flight Recorder: hit 7
    /// starts the handler, one child tick later a KEYFRAME is recorded; the run then carries on and writes <c>Got = 7</c>.
    /// Seeking back to the keyframe restores the mid-wait state — <c>Got</c> is 0 again and the handler is still waiting,
    /// holding its event — and two more ticks finish it on the restored copy, writing 7 again.
    /// <para>⚠ The child's tick COUNTER is test scaffolding (a static), not behaviour state, so it is put back by hand.</para>
    /// <para>✅ Red-proof: mark the blackboard tier components <c>NoReplay</c> and the seek restores nothing (Got stays 7).</para>
    /// </summary>
    [Fact]
    public unsafe void S6_AWaitingHandler_IsSavedToTheRecording_AndResumesAfterReplay()
    {
        const string Host = "S6RecHost", Child = "S6RecChild";
        _childTicks = 0; _childEnds = Fbt.NodeStatus.Success;
        RegisterCountingChild(Child);
        _fixture.CompileAndLoad(WaitingEventHandler(Host, Child), GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var world = _fixture.World;
        var (e, frame) = AssignAndFramer(Host);
        var hit = HitFramer(Host, e, def!, frame);

        Assert.Equal(0f, hit(7f));     // the handler starts and waits
        Assert.Equal(0f, hit(null));   // child tick 1 — still waiting

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"S6Rec_{Guid.NewGuid():N}.fdp");
        try
        {
            using (var rec = new Fdp.Core.FlightRecorder.AsyncRecorder(path))
                rec.CaptureKeyframe(world, DateTime.UtcNow.Ticks, blocking: true, eventBus: world.Bus);
            int ticksAtKeyframe = _childTicks;

            Assert.Equal(0f, hit(null));   // child tick 2
            Assert.Equal(7f, hit(null));   // child tick 3 ⇒ Success ⇒ the handler writes Got = 7 and ends

            using (var playback = new Fdp.Core.FlightRecorder.PlaybackController(path))
                playback.SeekToFrame(world, 0);
            _childTicks = ticksAtKeyframe;

            float Got() => *(float*)(RootParamsAccessRoot(world, e) + (int)VarOffset(def!, "Got"));
            Assert.Equal(0f, Got());       // restored mid-wait: nothing written yet
            Assert.Equal(0f, hit(null));   // the restored handler resumes: child tick 2
            Assert.Equal(7f, hit(null));   // child tick 3 ⇒ it writes the event it was holding
        }
        finally
        {
            try { System.IO.File.Delete(path); System.IO.File.Delete(path + ".meta.json"); } catch { }
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>S6b-2 — Queue(2): arrivals wait in line and run one after another, in order — and the line survives a
    /// record + replay.</b> Hits 7, 9, 11 on three consecutive frames: 7 runs (3 child ticks), 9 and 11 wait; each runs when
    /// the one before finishes, so Got goes 7 → 9 → 11. A keyframe recorded while 9 and 11 wait, sought back to after the
    /// run, replays the same 7 → 9 → 11.
    /// <para>✅ Red-proofs: drain the queue newest-first and the order reads 7 → 11 → 9; a capacity of 1 faults on the third hit.</para>
    /// </summary>
    [Fact]
    public unsafe void S6b_Queue2_RunsArrivalsInOrder_AndTheLineSurvivesReplay()
    {
        const string Host = "S6bQHost", Child = "S6bQChild";
        _ticksByOccurrence.Clear();
        RegisterCountingChild(Child, perOccurrence: true);
        _fixture.CompileAndLoad(WaitingEventHandler(Host, Child, EventFiberPolicy.Queue, 2), GoldenCorpus.Options());
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Host, out int id));
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        var world = _fixture.World;
        var (e, frame) = AssignAndFramer(Host);
        var hit = HitFramer(Host, e, def!, frame);

        Assert.Equal(0f, hit(7f));     // 7 starts and waits
        Assert.Equal(0f, hit(9f));     // child tick 1 · 9 waits in line
        Assert.Equal(0f, hit(11f));    // child tick 2 · 11 waits in line (the line is full)

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"S6bQ_{Guid.NewGuid():N}.fdp");
        try
        {
            using (var rec = new Fdp.Core.FlightRecorder.AsyncRecorder(path))
                rec.CaptureKeyframe(world, DateTime.UtcNow.Ticks, blocking: true, eventBus: world.Bus);
            var countsAtKeyframe = new System.Collections.Generic.Dictionary<int, int>(_ticksByOccurrence);

            float[] Run()
            {
                var seen = new System.Collections.Generic.List<float>();
                for (int f = 0; f < 8; f++) { float g = hit(null); if (seen.Count == 0 || seen[^1] != g) seen.Add(g); }
                return seen.ToArray();
            }

            Assert.Equal(new[] { 7f, 9f, 11f }, Run());

            using (var playback = new Fdp.Core.FlightRecorder.PlaybackController(path))
                playback.SeekToFrame(world, 0);
            _ticksByOccurrence.Clear();
            foreach (var kv in countsAtKeyframe) _ticksByOccurrence[kv.Key] = kv.Value;   // test scaffolding, not state

            Assert.Equal(new[] { 0f, 7f, 9f, 11f }, new[] { 0f }.Concat(Run().Where(v => v != 0f)).ToArray());
        }
        finally
        {
            try { System.IO.File.Delete(path); System.IO.File.Delete(path + ".meta.json"); } catch { }
        }
    }

    /// <summary>⭐⭐ S6b-2 — a full line is a FAULT, never a silent drop (U-6).</summary>
    [Fact]
    public void S6b_AFullQueue_FaultsTheRun()
    {
        const string Host = "S6bQFHost", Child = "S6bQFChild";
        _ticksByOccurrence.Clear();
        RegisterCountingChild(Child, perOccurrence: true);
        _fixture.CompileAndLoad(WaitingEventHandler(Host, Child, EventFiberPolicy.Queue, 1), GoldenCorpus.Options());
        var world = _fixture.World;
        var (e, _) = AssignAndFramer(Host);
        var brain = new BrainTickSystem(_fixture.BehaviorRegistry);

        foreach (var d in new[] { 1f, 2f, 3f })
        {
            world.Bus.Publish(new Runtime.WhenTestHitEvent { Damage = d });
            world.Bus.SwapBuffers(); brain.Execute(world, 0.016f);      // 1 runs, 2 waits, 3 finds the line full
        }
        world.Bus.SwapBuffers();
        var finished = world.Bus.Read<BehaviorFinishedEvent>().ToArray().Where(f => f.Entity.Index == e.Index).ToList();
        Assert.Single(finished);
        Assert.Equal(BehaviorFaultCode.EventOverflow, finished[0].FaultCode);
    }

    private static byte* RootParamsAccessRoot(Fdp.Core.EntityRepository world, Fdp.Core.Entity e)
    {
        Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root));
        return root;
    }
}
