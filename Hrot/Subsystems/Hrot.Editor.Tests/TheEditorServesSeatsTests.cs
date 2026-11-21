using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Runner;
using Hrot.Common;
using Hrot.Common.Orders;
using Hrot.Editor.DebugApi;
using Hrot.Gameplay;
using Hrot.Map.Common.Components;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>The seat rails start real editors (a real scenario load, the go-live) and read process-wide state: they run alone, after the parallel collections — measured: in parallel they made <c>AiHotReloadCoordinatorTests.CE3468</c> (it picks the last loaded <c>Hrot.AI.Behaviors</c> copy of the process) red.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TheEditorServesSeatsCollection { public const string Name = "The editor serves seats"; }

/// <summary>
/// ⭐⭐ <c>CE-11027</c> S12-4 (Q114 B / K; <c>docs/DESIGN_Player_Join.md</c> §12) — the EDITOR is the all-in-one server: it composes <see cref="GameplayServerPack"/> (no election, its own <c>EntityCreation</c> as the spawner) over the node's
/// in-process <see cref="LocalGameplayHub"/>, and a seat (a further station of the node — the gateway makes one per seat) is a <see cref="DeployFlowSystem"/> on a client end of that same hub. These rails run on the CONSTRUCTED editor node.
/// <list type="bullet">
///   <item><b>composition</b> — the editor's capabilities carry the server pack, serving over the server end of <c>LocalGameplayHub.Of(world)</c>: the hub the server uses IS the one a node extension asks for;</item>
///   <item><b>the hub survives a scenario load</b> — the node's world is the SAME repository after a load (<c>HrotNodeContext.World</c> is init-only; the boundary clears it in place), which is why the registry keys by the world;</item>
///   <item><b>the first editor run</b> — a seat at index 1 asks for a role, the man spawns through the server, the seat goes Choosing → Requesting → TakingControl → Playing, <c>GET /deploy?station=</c> reads that seat, <c>Leave()</c> frees it;</item>
///   <item><b>K (measured)</b> — the edit gate does not refuse a non-zero-index station's possess / release, in edit mode and live.</item>
/// </list>
/// 🔴 Mutations (each compiles): the editor composes no server · <c>LocalGameplayHub.Of</c> returns a new hub per call.
/// </summary>
[Collection(TheEditorServesSeatsCollection.Name)]
public sealed class TheEditorServesSeatsTests
{
    private const int RoleNetId = 7001, AreaNetId = 7002;

    private static GameplayServerPack ServerPack(EditorSubsystem editor)
        => editor.CapabilitiesForTest.OfType<GameplayServerPack>().Single();

    /// <summary>The editor's two authored things a role needs: a big Friend spawn area and a role of the Rifleman type (TKB 200).</summary>
    private static void AuthorRole(EditorSubsystem editor)
    {
        var node = editor.NodeForTest!; var w = node.World;
        var area = w.CreateEntity();
        w.AddComponent(area, new NetworkIdentity { Value = AreaNetId });
        w.AddComponent(area, new SimTransform { Position = new Vector3(0f, 0f, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(area, new SpawnArea { Side = ForceId.Friend, FacingDeg = 0f });
        w.SetManagedComponent(area, new EditablePolyline { Points = new[] { new Vector2(-30, -30), new Vector2(30, -30), new Vector2(30, 30), new Vector2(-30, 30) } });
        node.EntityMap.Register(AreaNetId, area);
        var role = w.CreateEntity();
        w.AddComponent(role, new NetworkIdentity { Value = RoleNetId });
        w.AddComponent(role, new DeployRole { Side = ForceId.Friend, PersonTkbType = (Fdp.Core.TkbTypeId)(200), Name = new FixedString32("Rifleman"), MaxCount = 4 });
        node.EntityMap.Register(RoleNetId, role);
    }

    [Fact(Timeout = 60_000)]
    public void CE11027_TheEditorComposesTheDeployServer_OverTheNodesOneHub()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var pack = ServerPack(editor);                                   // red: the editor composes none
            var end = Assert.IsType<LocalGameplayEnd>(pack.WireEnd);          // the server speaks over the hub's server end, not DDS
            Assert.Null(pack.Wire);
            Assert.True(end.IsOpen);
            Assert.Same(LocalGameplayHub.Of(editor.NodeForTest!.World), end.Hub);   // 🔴 Of returning another hub ⇒ the gateway's seats would talk to nobody
            Assert.NotNull(pack.Processor);
            Assert.True(pack.Processor!.IsServer || true);                    // (the election is null ⇒ it always serves; read after a frame below)
            editor.Update(0.016f); editor.Update(0.016f);
            Assert.True(pack.Processor.IsServer, "no election: the editor serves alone");
        }
        finally { editor.Shutdown(); }
    }

    /// <summary>
    /// ⭐ S12-4 item 2 (the measurement behind <c>LocalGameplayHub.Of(world)</c>) + K LIVE — on a REAL scenario load (<c>test-move</c>) and the real go-live (the world is cleared again), the node's world is the SAME repository (so the registry may key
    /// by it), the hub and the server's end are the same objects, and the server — which forgot its tables at each <c>WorldEpoch</c> move — serves a seat again: in OPERATING EDIT after the load and in LIVE after the go-live, the same seat at
    /// index 1 deploys into a role and the edit gate does not refuse its possess (the journal says Mode Edit, then Live, never Refused). 📐 Measured in the probe: epoch 1 → 2 (load) → 4 (live), <c>mode=Live</c> once the branch stands.
    /// </summary>
    [Fact(Timeout = 300_000)]
    public void CE11027_ARealLoadAndTheGoLive_KeepTheNodesWorldAndHub_AndTheSameSeatDeploysInEditAndInLive()
    {
        string sourceDir = Fdp.Testing.TestRepo.ScenarioDirectory("test-move");
        string name = "s124_" + Guid.NewGuid().ToString("N");
        string dir = System.IO.Path.Combine(EditorBootstrap.ScenariosRoot, name);
        System.IO.Directory.CreateDirectory(dir);
        foreach (var f in System.IO.Directory.GetFiles(sourceDir)) System.IO.File.Copy(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)));
        var editor = new EditorSubsystem();
        new SubsystemOrchestrator(new ISubsystem[] { editor }, new RunnerOptions { Headless = true }).Initialize();
        try
        {
            var node = editor.NodeForTest!; var world = node.World;
            var hub = LocalGameplayHub.Of(world); var end = ServerPack(editor).WireEnd; var processor = ServerPack(editor).Processor;
            var master = editor.ClusterMasterForTest!;
            void FramesUntil(Func<bool> done, string what, int seconds = 90)
            {
                long until = Environment.TickCount64 + seconds * 1000L;
                while (!done()) { Assert.True(Environment.TickCount64 < until, "timed out: " + what); editor.Update(0.016f); System.Threading.Thread.Sleep(10); }
            }
            FramesUntil(() => node.ClusterSlave.View.GateOpen(HostGatePolicy.Operator), "the run gate", 30);
            editor.EditorLogic.LoadScenarioByName(name);
            FramesUntil(() => (int)master.CurrentClusterState == (int)ClusterState.OperatingEdit && world.EntityCount > 0, "the scenario loaded");
            Assert.True(WorldEpochOf(world) >= 2, "a real load cleared the world (epoch " + WorldEpochOf(world) + ")");
            Assert.Same(world, editor.NodeForTest!.World);                       // the measurement: the load clears the SAME repository
            Assert.Same(hub, LocalGameplayHub.Of(editor.NodeForTest.World));
            Assert.Same(end, ServerPack(editor).WireEnd); Assert.Same(processor, ServerPack(editor).Processor);

            // OPERATING EDIT after the load: the seat deploys, the gate lets it in.
            // 📐 Finding: a loaded editor is PAUSED, and the possession system ticks only while time advances (<c>TickPolicy.WhenTimeAdvances</c>) — a seat's possess stays Pending ("taking control timed out" after 10 s) until the clock runs, exactly as the editor user's own does.
            editor.TimeControllerForTest!.SwitchToContinuous();
            AuthorRole(editor);
            var seat = NewSeat(editor, 1);
            Pump(editor, seat, () => seat.Flow.State == DeployFlowState.Choosing && seat.Flow.Offers.Any(o => o.IsRole && o.UnitNetId == RoleNetId), "the role offered after the load");
            Assert.True(seat.Flow.Request(ForceId.Friend, RoleNetId, 0, AreaNetId));
            Pump(editor, seat, () => seat.Flow.State == DeployFlowState.Playing, "Playing after the load");
            Assert.True(node.EntityMap.TryGetEntity(seat.Flow.UnitNetId, out var man1));
            Assert.Equal(seat.Station.Value, PossessionSystem.HolderOf(world, man1));
            Assert.True(seat.Flow.Leave());
            Pump(editor, seat, () => PossessionSystem.HolderOf(world, man1) is null && seat.Flow.State == DeployFlowState.Choosing, "the seat left");
            var editTakes = JournalOf(world, "Take control");
            Assert.NotEmpty(editTakes); Assert.All(editTakes, e => { Assert.NotEqual(Fdp.Toolkit.Replication.Edits.EditStatus.Refused, e.Status); Assert.Equal("Edit", e.Mode); });

            // the go-live: the world is cleared again, the mode becomes Live
            long epochBefore = WorldEpochOf(world);
            editor.OrchestrationBusForTest!.PublishManaged(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.OperatingLive, ScenarioId = name, TimeMode = "Deterministic" });
            FramesUntil(() => world.GetSingletonManaged<Hrot.Common.Editing.StationMode>()!.Current == Hrot.Common.Editing.EditingMode.Live && world.EntityCount > 0 && WorldEpochOf(world) > epochBefore, "the editor Live");
            Assert.Same(world, editor.NodeForTest!.World); Assert.Same(hub, LocalGameplayHub.Of(world)); Assert.Same(end, ServerPack(editor).WireEnd);

            editor.TimeControllerForTest!.SwitchToContinuous();
            AuthorRole(editor);   // the clear wiped the authored role and area
            Pump(editor, seat, () => seat.Flow.State == DeployFlowState.Choosing && seat.Flow.Offers.Any(o => o.IsRole && o.UnitNetId == RoleNetId), "the role offered in Live");
            Assert.True(seat.Flow.Request(ForceId.Friend, RoleNetId, 0, AreaNetId));
            Pump(editor, seat, () => seat.Flow.State == DeployFlowState.Playing, "Playing in Live");
            Assert.True(node.EntityMap.TryGetEntity(seat.Flow.UnitNetId, out var man2));
            Assert.Equal(seat.Station.Value, PossessionSystem.HolderOf(world, man2));
            // K live: its possess went through the gate in Live mode and was not refused
            var liveTakes = JournalOf(world, "Take control").Where(e => e.Mode == "Live").ToArray();
            Assert.NotEmpty(liveTakes); Assert.All(liveTakes, e => Assert.NotEqual(Fdp.Toolkit.Replication.Edits.EditStatus.Refused, e.Status));
            Assert.True(seat.Flow.Leave());
            Pump(editor, seat, () => PossessionSystem.HolderOf(world, man2) is null, "the seat left in Live");
        }
        finally { editor.Shutdown(); if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
    }

    private static int WorldEpochOf(EntityRepository w) => w.HasSingletonManaged<Fdp.Toolkit.Replication.Services.WorldEpoch>() ? w.GetSingletonManaged<Fdp.Toolkit.Replication.Services.WorldEpoch>()!.Value : 0;

    // ── the first editor run: a seat at index 1 ─────────────────────────────────────────────

    private sealed class Seat
    {
        public StationId Station; public LocalGameplayEnd End = null!; public PlayerSlotReaderSystem Slots = null!; public DeployFlowSystem Flow = null!; public EntityOrders Orders = null!;
        public readonly System.Collections.Generic.List<DeployFlowState> Visited = new();
    }

    /// <summary>A gateway seat as the gateway will make it: a client end of the node's hub and a <c>register: false</c> flow, ticked by the caller, posting its possess through ITS OWN <c>EntityOrders</c> (station = the seat's).</summary>
    private static Seat NewSeat(EditorSubsystem editor, int index)
    {
        var node = editor.NodeForTest!; var world = node.World;
        var seat = new Seat { Station = StationId.Of(node.NodeId, index) };
        seat.End = LocalGameplayHub.Of(world).ClientEnd();
        seat.Orders = new EntityOrders(world, node.EntityMap, node.GeoTransform) { Station = seat.Station.Value };
        var env = new DeployFlowEnvironment(() => true, () => true, () => true, unit => { var r = seat.Orders.Possess(unit); return (r.Accepted, r.Detail); });
        seat.Flow = new DeployFlowSystem(seat.End, world, node.EntityMap, seat.Station.Value, env, register: false);
        seat.Slots = new PlayerSlotReaderSystem(seat.End, () => seat.Flow.ServerExists);
        seat.Flow.Slots = seat.Slots.TryGetSlot;
        return seat;
    }

    private static void Pump(EditorSubsystem editor, Seat seat, Func<bool> done, string what, int timeoutMs = 30_000)
    {
        var world = editor.NodeForTest!.World;
        long end = Environment.TickCount64 + timeoutMs;
        while (!done())
        {
            Assert.True(Environment.TickCount64 < end, $"timed out: {what} (state {seat.Flow.State}, refusal '{seat.Flow.LastRefusal}', reason '{seat.Flow.Reason}'; the possess journal: "
                + string.Join("; ", JournalOf(world, "Take control").Select(e => $"{e.Status} '{e.Reason}' in {e.Mode}")) + ")");
            editor.Update(0.016f);
            seat.Slots.Execute(world, 0.016f);
            seat.Flow.Execute(world, 0.016f);
            if (seat.Visited.Count == 0 || seat.Visited[^1] != seat.Flow.State) seat.Visited.Add(seat.Flow.State);
            System.Threading.Thread.Sleep(5);
        }
    }

    private static DebugApiService Api(EditorSubsystem editor)
    {
        var world = editor.NodeForTest!.World;
        return new DebugApiService(new PerspectiveScopedDispatcher(new ISubsystemDebugProvider[] { new SubsystemDebugProvider("Editor", "Editor", world: () => world) }, currentPerspective: () => "Editor", acksPending: null));
    }

    private static Fdp.Toolkit.Replication.Edits.EditJournalEntry[] JournalOf(EntityRepository w, string label)
    {
        var j = Fdp.Toolkit.Replication.Edits.EditJournal.Of(w);
        return Enumerable.Range(0, j.Count).Select(i => j[i]).Where(e => e.Label == label).ToArray();
    }

    /// <summary>
    /// ⭐ S12-4 item 6 + K (edit mode) — the FIRST run of the path in a real editor: a seat at station index 1 asks for the role; the editor's server spawns the man through its own <c>EntityCreation</c>; the flow goes
    /// Choosing → Requesting → TakingControl → Playing; the man is held by THAT seat (index 1), not by seat 0; <c>GET /deploy?station=</c> reads the seat; <c>Leave()</c> frees it. The editor is in EDIT mode throughout
    /// (the cluster is Idle): the possess and the release went through <c>Edits.Submit</c>'s gate and were NOT refused — K's answer for edit mode. 🔴 Mutation: the editor composes no server ⇒ no offer, never Choosing with one ⇒ timeout.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void CE11027_ASeatAtIndex1_DeploysIntoARoleInTheEditor_ReadByHttp_FreedByLeave_AndTheEditGateDoesNotRefuseIt()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var node = editor.NodeForTest!; var world = node.World;
            editor.TimeControllerForTest!.SwitchToContinuous();
            Assert.Equal(Hrot.Common.Editing.EditingMode.Edit, world.GetSingletonManaged<Hrot.Common.Editing.StationMode>()!.Current);   // K: this is EDIT mode
            AuthorRole(editor);
            var seat = NewSeat(editor, 1);
            Assert.Same(seat.Flow, DeployFlowSystem.Of(world, seat.Station.Value));   // found by station …
            Assert.Null(DeployFlowSystem.Of(world));                                   // … and never the world's own (register: false)

            Pump(editor, seat, () => seat.Flow.State == DeployFlowState.Choosing && seat.Flow.Offers.Any(o => o.IsRole && o.UnitNetId == RoleNetId), "the seat sees the role offered");
            Assert.True(seat.Flow.Request(ForceId.Friend, RoleNetId, 0, AreaNetId));
            Pump(editor, seat, () => seat.Flow.State == DeployFlowState.Playing, "Playing");

            Assert.Equal(new[] { DeployFlowState.Choosing, DeployFlowState.Requesting, DeployFlowState.TakingControl, DeployFlowState.Playing },
                         seat.Visited.SkipWhile(s => s != DeployFlowState.Choosing).ToArray());
            int manNet = seat.Flow.UnitNetId;
            Assert.NotEqual(RoleNetId, manNet);
            Assert.True(node.EntityMap.TryGetEntity(manNet, out var man) && world.IsAlive(man));
            Assert.Equal(seat.Station.Value, PossessionSystem.HolderOf(world, man));             // held by seat 1 — the node's seat 0 (index 0) is NOT the holder
            Assert.Equal(1, world.GetComponentRO<PossessedBy>(man).StationNodeId.Index);

            // K: the possess went through the gate in Edit mode and was not refused
            var take = JournalOf(world, "Take control");
            Assert.NotEmpty(take);
            Assert.All(take, e => { Assert.NotEqual(Fdp.Toolkit.Replication.Edits.EditStatus.Refused, e.Status); Assert.Equal("Edit", e.Mode); });

            // HTTP /deploy?station= reads THAT seat
            var (doc, err, _, status) = Api(editor).Deploy(null, post: false, station: $"{node.NodeId.Value}/1");
            Assert.Null(err); Assert.Equal(200, status);
            var j = (JsonObject)doc!;
            Assert.Equal("Playing", (string?)j["state"]); Assert.Equal(seat.Station.Value, (int?)j["station"]);
            Assert.Equal(seat.Station.Value, Api(editor).Deploy(null, false, seat.Station.Value.ToString()).Result!["station"]!.GetValue<int>());   // the whole id spells the same seat
            Assert.Equal(404, Api(editor).Deploy(null, post: false, station: $"{node.NodeId.Value}/2").Status);   // another seat: none
            Assert.Equal(400, Api(editor).Deploy(null, post: false, station: "seven").Status);
            Assert.Equal(404, Api(editor).Deploy(null, post: false).Status);                                       // no station ⇒ the world's own flow: this editor has none
            // POST carries the station in its BODY: the seat's flow is found (it is Playing, so an observe is refused 409), another seat is 404
            Assert.Equal(409, Api(editor).Deploy(new JsonObject { ["side"] = "Friend", ["observe"] = true, ["station"] = $"{node.NodeId.Value}/1" }, post: true).Status);
            Assert.Equal(404, Api(editor).Deploy(new JsonObject { ["side"] = "Friend", ["observe"] = true, ["station"] = $"{node.NodeId.Value}/2" }, post: true).Status);

            // Leave frees the seat: the man is released (also a K row: the release went through the gate)
            Assert.True(seat.Flow.Leave());
            Pump(editor, seat, () => PossessionSystem.HolderOf(world, man) is null && seat.Flow.State == DeployFlowState.Choosing, "the seat left and its man is free");
            Assert.False(world.HasComponent<PossessedBy>(man) && world.GetComponentRO<PossessedBy>(man).StationNodeId == seat.Station);
        }
        finally { editor.Shutdown(); }
    }
}
