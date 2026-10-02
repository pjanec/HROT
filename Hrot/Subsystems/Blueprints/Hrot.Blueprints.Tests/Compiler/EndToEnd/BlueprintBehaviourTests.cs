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
        var asset = BlueprintAssetBuilder.Behavior("S1EventDelay")
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

    private static byte* RootParamsAccessRoot(Fdp.Core.EntityRepository world, Fdp.Core.Entity e)
    {
        Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root));
        return root;
    }
}
