using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.AI.Behaviors.Brains;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Golden;
using Hrot.Map.Definitions.Behavior.Intents;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐ <c>CE-472</c> — Send Intent / To JSON / From JSON. 📄 <c>docs/blueprints/DESIGN_Typed_Intent_And_Json_Nodes.md</c>.
/// ⭐ The load-bearing rails RUN the generated code: a blueprint behaviour sends the intent through the real brain tick,
/// and the JSON it put on the bus is parsed by the real <c>HullDownAttackRun</c> resolver — the receiver it reaches.
/// </summary>
public class CE472_IntentAndJsonNodeTests
{
    private static readonly string DtoFqn = typeof(HullDownAttackIntentDto).FullName!;

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    private static List<StructFieldDecl> Members(Type? dto = null) => ReflectionIntentContractProvider
        .PinnableMembers(dto ?? typeof(HullDownAttackIntentDto))
        .Select(m => new StructFieldDecl { Name = m.Name, TypeId = m.TypeId }).ToList();

    private static LiteralNode Literal(string typeId, string json, out Pin outPin)
    {
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = typeId, ValueJson = json };
        outPin = DataPin("Value", "Out", typeId);
        lit.Pins.Add(outPin);
        return lit;
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A behaviour whose Tick sends <c>HullDownAttack</c> to self with SlotX = 12.5 and TargetNetworkId = 4242 (the
    /// rest unwired ⇒ the contract's defaults), then succeeds.
    /// </summary>
    private static BlueprintAsset BuildSender(string name = "Ce472Send")
    {
        var send = new SendIntentNode { Id = Guid.NewGuid(), IntentId = "HullDownAttack", DtoTypeFqn = DtoFqn, Fields = Members() };
        var sIn = ExecPin("In", "In");
        var sOut = ExecPin("Out", "Out");
        send.Pins.AddRange(new[] { sIn, sOut, DataPin("Target", "In", "Fdp.Core.Entity") });
        foreach (var f in send.Fields) send.Pins.Add(DataPin(f.Name, "In", f.TypeId));
        var slot = Literal("System.Single", "12.5", out var slotOut);
        var target = Literal("Fdp.Toolkit.Replication.EntityRef", "4242", out var targetOut);   // a reference literal (DESIGN_Entity_Reference)

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);
        var g = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function, Nodes = { entry, slot, target, send, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,  FromPinId = entryOut.Id,  ToNodeId = send.Id, ToPinId = sIn.Id },
                new Link { FromNodeId = send.Id,   FromPinId = sOut.Id,      ToNodeId = ret.Id,  ToPinId = retIn.Id },
                new Link { FromNodeId = slot.Id,   FromPinId = slotOut.Id,   ToNodeId = send.Id, ToPinId = send.Pins.Single(p => p.Name == "SlotX").Id },
                new Link { FromNodeId = target.Id, FromPinId = targetOut.Id, ToNodeId = send.Id, ToPinId = send.Pins.Single(p => p.Name == "TargetNetworkId").Id },
            },
        };
        var asset = Builders.BlueprintAssetBuilder.Behavior(name).Build();
        asset.Graphs.Add(g);
        return asset;
    }

    /// <summary>
    /// A Library function <paramref name="name"/>(slotX) = FromJson(ToJson(SlotX = slotX)).<paramref name="member"/> —
    /// or, with <paramref name="badJson"/>, FromJson(literal) with no ToJson. ⚠ A string can never be a function
    /// input/output (the Library ABI marshals I/O through bytes) — the JSON lives inside the graph, decision B.
    /// </summary>
    private static Graph RoundTrip(string name, string member, string memberType, string? badJson = null,
                                   Type? dto = null, string inMember = "SlotX")
    {
        string dtoFqn = (dto ?? typeof(HullDownAttackIntentDto)).FullName!;
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        var slotIn = DataPin("slotX", "Out", "System.Single");
        entry.Pins.AddRange(new[] { entryOut, slotIn });

        var fromJson = new FromJsonNode { Id = Guid.NewGuid(), DtoTypeFqn = dtoFqn, Fields = Members(dto) };
        var jsonIn = DataPin("Json", "In", "System.String");
        fromJson.Pins.Add(jsonIn);
        foreach (var f in fromJson.Fields) fromJson.Pins.Add(DataPin(f.Name, "Out", f.TypeId));
        fromJson.Pins.Add(DataPin("Ok", "Out", "System.Boolean"));
        var picked = fromJson.Pins.Single(p => p.Name == member && p.Direction == "Out");

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        var retValue = DataPin("V", "In", memberType);
        ret.Pins.AddRange(new[] { retIn, retValue });

        var g = new Graph { Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Function, Nodes = { entry, fromJson, ret } };
        g.Inputs.Add(new ParameterDecl { Name = "slotX", Type = new BlueprintTypeRef { TypeId = "System.Single" } });
        g.Outputs.Add(new ParameterDecl { Name = "V", Type = new BlueprintTypeRef { TypeId = memberType } });
        g.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id });
        g.Links.Add(new Link { FromNodeId = fromJson.Id, FromPinId = picked.Id, ToNodeId = ret.Id, ToPinId = retValue.Id });

        if (badJson is not null)
        {
            var lit = Literal("System.String", System.Text.Json.JsonSerializer.Serialize(badJson), out var litOut);   // ValueJson is JSON
            g.Nodes.Add(lit);
            g.Links.Add(new Link { FromNodeId = lit.Id, FromPinId = litOut.Id, ToNodeId = fromJson.Id, ToPinId = jsonIn.Id });
            return g;
        }

        var toJson = new ToJsonNode { Id = Guid.NewGuid(), DtoTypeFqn = dtoFqn, Fields = Members(dto) };
        foreach (var f in toJson.Fields) toJson.Pins.Add(DataPin(f.Name, "In", f.TypeId));
        var jsonOut = DataPin("Json", "Out", "System.String");
        toJson.Pins.Add(jsonOut);
        g.Nodes.Add(toJson);
        g.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = slotIn.Id, ToNodeId = toJson.Id, ToPinId = toJson.Pins.Single(p => p.Name == inMember).Id });
        g.Links.Add(new Link { FromNodeId = toJson.Id, FromPinId = jsonOut.Id, ToNodeId = fromJson.Id, ToPinId = jsonIn.Id });
        return g;
    }

    private static BlueprintAsset RoundTripLibrary() => new()
    {
        AssetId = Guid.NewGuid(), Name = "Ce472RoundTrip", Dispatch = BlueprintDispatchKind.Library,
        Graphs =
        {
            RoundTrip("SlotX", "SlotX", "System.Single"),
            RoundTrip("Speed", "ApproachSpeed", "System.Single"),
            RoundTrip("Ok", "Ok", "System.Boolean"),
            RoundTrip("BadOk", "Ok", "System.Boolean", badJson: "not json"),
            RoundTrip("BadSlotX", "SlotX", "System.Single", badJson: "not json"),
            RoundTrip("EmptyOk", "Ok", "System.Boolean", badJson: ""),
        },
    };

    /// <summary>⭐ CE-2023 ③ ("S8n") — the same round trip over a STRUCT contract (MoveToLocation's).</summary>
    private static BlueprintAsset StructRoundTripLibrary()
    {
        var dto = typeof(Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto);
        return new()
        {
            AssetId = Guid.NewGuid(), Name = "Ce2023StructRoundTrip", Dispatch = BlueprintDispatchKind.Library,
            Graphs =
            {
                RoundTrip("X", "X", "System.Single", dto: dto, inMember: "X"),
                RoundTrip("Ok", "Ok", "System.Boolean", dto: dto, inMember: "X"),
                RoundTrip("BadOk", "Ok", "System.Boolean", badJson: "not json", dto: dto, inMember: "X"),
                RoundTrip("BadX", "X", "System.Single", badJson: "not json", dto: dto, inMember: "X"),
            },
        };
    }

    // ── coverage fixtures for NodeCoverageTests (each node kind must appear in a compiling asset) ──
    internal static BlueprintAsset CoverageRoundTrip() => RoundTripLibrary();
    internal static BlueprintAsset CoverageSender() => BuildSender("Ce472SendCoverage");

    private static string Diags(CompileResult r) => string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private static MethodInfo Method(Assembly asm, string name)
        => asm.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)).First(m => m.Name == name);

    // ── rails ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 The end-to-end claim: a blueprint BEHAVIOUR ticks, its Send Intent publishes <c>AssignTacticalIntentEvent</c>
    /// for self, and the REAL <c>HullDownAttackRun</c> resolver parses its JSON — wired members carried, unwired ones at
    /// the contract's defaults (the constants the retired C# helper baked).
    /// </summary>
    [Fact]
    public unsafe void SendIntent_InARunningBehaviour_PublishesJsonTheRealReceiverParses()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(BuildSender(), GoldenCorpus.Options());

        var world = fixture.World;
        var e = fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = "Ce472Send", JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(fixture.BehaviorRegistry).Execute(world, 0.016f);
        world.Bus.SwapBuffers();
        new BrainTickSystem(fixture.BehaviorRegistry).Execute(world, 0.016f);
        world.Bus.SwapBuffers();

        var sent = Assert.Single(world.Bus.ReadManaged<AssignTacticalIntentEvent>());
        Assert.Equal(e, sent.Entity);                    // Target unwired ⇒ self
        Assert.Equal("HullDownAttack", sent.IntentId);

        HullDownAttackParams p;
        HillAttackTankNodes.ParseHullDownAttackParams(sent.JsonParams, (byte*)&p, Marshal.SizeOf<HullDownAttackParams>());
        Assert.Equal(12.5f, p.SlotX);
        Assert.Equal(4242L, p.TargetNetworkId);
        Assert.Equal(15f, p.ApproachSpeed);
        Assert.Equal(5f, p.CreepSpeed);
        Assert.Equal(1, p.MaxRounds);

        // ⭐ parity with the live C# commander's own payload (HillAttackCommanderNodes, the HullDownAttack dispatch —
        //   the retired HullDownIntentJson helper built the same DTO): same inputs ⇒ the receiver sees the same struct
        var oracle = System.Text.Json.JsonSerializer.Serialize(
            new HullDownAttackParams
            {
                SlotX = 12.5f, TargetNetworkId = 4242L, ApproachSpeed = 15f, CreepSpeed = 5f,
                MaxRounds = 1, RoundsFired = 0, LastObservedAmmo = -1,
            },
            Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        HullDownAttackParams h;
        HillAttackTankNodes.ParseHullDownAttackParams(oracle, (byte*)&h, Marshal.SizeOf<HullDownAttackParams>());
        Assert.Equal(h, p);
    }

    [Fact]
    public void ToJson_ThenFromJson_RoundTrips_AndFromJsonNeverThrowsOnBadInput()
    {
        var asset = RoundTripLibrary();
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));
        Assert.Contains("FdpJsonOptionsRegistry.DefaultRelaxed", result.GeneratedSource!);

        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        object Call(string fn) => Method(asm, fn).Invoke(null, new object[] { 3.5f })!;

        Assert.Equal(3.5f, Call("SlotX"));
        Assert.Equal(15f, Call("Speed"));      // unwired on To JSON ⇒ the contract default survives the trip
        Assert.Equal(true, Call("Ok"));
        Assert.Equal(false, Call("BadOk"));    // decision E: a flag, not a throw
        Assert.Equal(0f, Call("BadSlotX"));    // members at the DTO's defaults
        Assert.Equal(false, Call("EmptyOk"));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2023</c> ③ ("S8n") — To JSON / From JSON over a STRUCT contract compiles and round-trips. 🔴 The From JSON
    /// emission was <c>T x = null!; … x ??= new T();</c>, which does not compile for a struct.
    /// </summary>
    [Fact]
    public void ToJson_ThenFromJson_OverAStructContract_RoundTrips()
    {
        var asset = StructRoundTripLibrary();
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));

        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asm = fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        object Call(string fn) => Method(asm, fn).Invoke(null, new object[] { 3.5f })!;

        Assert.Equal(3.5f, Call("X"));
        Assert.Equal(true, Call("Ok"));
        Assert.Equal(false, Call("BadOk"));
        Assert.Equal(0f, Call("BadX"));
    }

    [Theory]
    [CoversDiagnosticCode("BP1680")]
    [InlineData(true)]
    [InlineData(false)]
    public void ANodeWithNoDtoOrNoIntentId_IsRefusedByTheCompilerNotByRoslyn(bool missingDto)
    {
        var asset = BuildSender();
        var send = asset.Graphs.Single().Nodes.OfType<SendIntentNode>().Single();
        if (missingDto) send.DtoTypeFqn = ""; else send.IntentId = "";
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Diagnostics.Any(d => d.Code == "BP1680"), Diags(result));
    }

    /// <summary>The palette discovers the contracts and bakes only the members a pin can carry.</summary>
    [Fact]
    public void Palette_OffersEachContract_WithPinnableMembersOnly()
    {
        var contracts = ReflectionIntentContractProvider.Compute(new[] { typeof(HullDownAttackIntentDto).Assembly });
        var hull = Assert.Single(contracts, c => c.Id == "HullDownAttack");
        Assert.Equal(DtoFqn, hull.DtoTypeFqn);
        Assert.Contains(hull.Members, m => m.Name == "TargetNetworkId" && m.TypeId == "Fdp.Toolkit.Replication.EntityRef");

        var move = Assert.Single(contracts, c => c.Id == "MoveToLocation");
        Assert.DoesNotContain(move.Members, m => m.Name == "PickableLocation");   // [JsonIgnore] + not pinnable
        Assert.Contains(move.Members, m => m.Name == "X" && m.TypeId == "System.Single");

        var kinds = IntentContractPaletteEntries.Entries(new FixedProvider(contracts)).Select(d => d.Kind).ToList();
        Assert.Contains("Intent.Send.HullDownAttack", kinds);
        Assert.Contains($"Json.To.{DtoFqn}", kinds);
        Assert.Contains($"Json.From.{DtoFqn}", kinds);
    }

    private sealed class FixedProvider(IReadOnlyList<IntentContract> contracts) : IIntentContractProvider
    {
        public IReadOnlyList<IntentContract> GetContracts() => contracts;
    }
}
