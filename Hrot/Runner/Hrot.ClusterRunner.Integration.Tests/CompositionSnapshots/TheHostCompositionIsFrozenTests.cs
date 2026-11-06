using System.Runtime.CompilerServices;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Hrot.Common;
using Hrot.Map.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Editor;
using Hrot.Network.NED.Factory;
using Hrot.NodeComposition;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.CompositionSnapshots;

/// <summary>
/// ⭐⭐⭐ <b>S0 — the composition snapshot rail</b> (docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Multiplayer_Server_And_Full_Cycle.md, item ①, safety rule S0).
/// Before the host-bootstrap refactor ("a host is data, not a class") freezes WHAT every ECS host composes today — modules, systems with phase / group / order,
/// translators with direction and topic, resolved capabilities, resource providers, singletons, registered component types, the time role and the ClusterSlave count —
/// as committed goldens under <c>CompositionSnapshots/</c> (one <c>&lt;host&gt;.json</c>). Every later refactor step must reproduce them BYTE-IDENTICAL; a step that
/// moves one is a different change and needs its own approval.
/// <para>⭐ Capture: <c>HROT_COMPOSITION_CAPTURE=1</c> (re)writes the goldens; otherwise each host is compared and a mismatch fails with a readable diff naming the host, the section and the row.</para>
/// <para>⭐ Hosts: the editor (offline), the Stride node bootstrapper (the net8.0 part of the Stride host — the Windows-only app layer, <c>HrotStrideApp.Game</c>, is
/// <c>net8.0-windows</c> and is not booted here), and the cluster nodes the runner builds for <c>--mode all</c> — each with its OWN factory and DDS participant exactly as
/// <c>Program.cs</c> builds them. A cluster node composes the same standalone or in <c>all</c>/<c>server</c>, so <c>simhost</c>, <c>ig</c>, <c>cgf</c>, <c>traffic</c> are the
/// per-host goldens and <c>all.json</c> records the roster plus counts.</para>
/// </summary>
[Collection("EqsIntegrationTests")]   // one DDS participant at a time, as the other constructed-host rails
public sealed class TheHostCompositionIsFrozenTests
{
    private const string CaptureVariable = "HROT_COMPOSITION_CAPTURE";
    private static int _domain = 230;

    private static string GoldenDir([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    private static bool Capturing => Environment.GetEnvironmentVariable(CaptureVariable) == "1";

    /// <summary>Writes (capture) or compares one golden. Returns the failure text, or "".</summary>
    private static string Check(string host, string actual)
    {
        var path = Path.Combine(GoldenDir(), host + ".json");
        if (Capturing) { File.WriteAllText(path, actual); return ""; }
        if (!File.Exists(path)) return $"[{host}] no golden at {path} — capture with {CaptureVariable}=1\n";
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        return CompositionSnapshot.Diff(host, expected, actual);
    }

    private static void AssertNoDifferences(string failures)
        => Assert.True(failures.Length == 0, "the host composition moved (a refactor must reproduce the goldens byte-identical; re-capture only for a reviewed, intended change):\n" + failures);

    [Fact(Timeout = 120_000)]
    public void TheEditorComposesWhatItComposedBefore()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var bootstrapper = CompositionSnapshot.FindField<SharedApplicationBootstrapper>(editor);
            AssertNoDifferences(Check("editor", CompositionSnapshot.Describe("editor", editor.NodeForTest!, bootstrapper)));
        }
        finally { editor.Shutdown(); }
    }

    [Fact(Timeout = 120_000)]
    public void TheStrideNodeComposesWhatItComposedBefore()
    {
        var bootstrapper = new StrideNodeBootstrapper();
        try
        {
            bootstrapper.BootstrapNode(new HrotNodeConfig
            {
                Headless = true, SkipAllocatorRouting = true, SubsystemName = "Stride", NodeId = (Fdp.Core.NodeId)1,
                LocalTempRoot = Path.Combine(Path.GetTempPath(), "hrot-composition-stride"),
            }, StrideNodeBootstrapper.Role, new OfflineNetworkFactory());
            AssertNoDifferences(Check("stride", CompositionSnapshot.Describe("stride", bootstrapper.Context, bootstrapper)));
        }
        finally { bootstrapper.Dispose(); }
    }

    /// <summary>The nodes the runner builds for <c>--mode all</c>, in its order, each on its own factory and participant (Program.cs).</summary>
    [Fact(Timeout = 180_000)]
    public void TheClusterNodesComposeWhatTheyComposedBefore()
    {
        int domain = Interlocked.Increment(ref _domain);
        var types = new[]
        {
            typeof(Hrot.Orchestrator.OrchestratorSubsystem), typeof(Hrot.SimHost.SimHostSubsystem), typeof(Hrot.Player.PlayerSubsystem),
            typeof(Hrot.CGF.CgfSubsystem), typeof(Hrot.TrafficNode.TrafficSubsystem),
        };
        var participants = new List<DdsParticipant>();
        var subsystems = new List<ISubsystem>();
        foreach (var type in types)
        {
            var participant = HrotEnvironment.CreateParticipant(domain);
            participants.Add(participant);
            INetworkFactory factory = new NedNetworkFactory(participant, new NetworkEntityMap(), HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
                new FdpEventBus(), 0, NodeRole.None);
            subsystems.Add(SubsystemFactory.TryCreate(type, factory)!);
        }
        var orchestrator = new SubsystemOrchestrator(subsystems, new RunnerOptions { Headless = true, DomainId = domain });
        try
        {
            orchestrator.Initialize();

            var failures = new System.Text.StringBuilder();
            var counts = new System.Text.Json.Nodes.JsonObject();
            foreach (var sub in subsystems)
            {
                var (node, bootstrapper) = NodeOf(sub);
                if (node is null) continue;   // the orchestrator is the cluster master's host, not an ECS node
                var host = sub.Name.ToLowerInvariant();
                var json = CompositionSnapshot.Describe(host, node, bootstrapper);
                failures.Append(Check(host, json));
                var c = System.Text.Json.Nodes.JsonNode.Parse(json)!["counts"]!.AsObject();
                counts[host] = System.Text.Json.Nodes.JsonNode.Parse(c.ToJsonString());
            }
            var all = new System.Text.Json.Nodes.JsonObject
            {
                ["host"] = "all",
                ["roster"] = new System.Text.Json.Nodes.JsonArray(subsystems.Select(s => (System.Text.Json.Nodes.JsonNode)s.Name).ToArray()),
                ["nodeCounts"] = counts,
            };
            failures.Append(Check("all", all.ToJsonString(CompositionSnapshot.Json).Replace("\r\n", "\n") + "\n"));
            AssertNoDifferences(failures.ToString());
        }
        finally
        {
            try { orchestrator.Shutdown(); } catch { /* best effort, as the harness */ }
            foreach (var p in participants) try { p.Dispose(); } catch { /* as the harness */ }
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4270</c> B7c (D4, R-3077) — <c>-m server</c>, the FUSED server: the orchestrator and ONE cgf node booted with
    /// <see cref="Hrot.CGF.CgfSubsystem.FusedServerRole"/> (as Program.cs sets it), each on its own factory and participant. Golden <c>server-fused.json</c>.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public void TheFusedServerComposesWhatItComposedBefore()
    {
        int domain = Interlocked.Increment(ref _domain);
        var types = new[] { typeof(Hrot.Orchestrator.OrchestratorSubsystem), typeof(Hrot.CGF.CgfSubsystem) };
        var participants = new List<DdsParticipant>();
        var subsystems = new List<ISubsystem>();
        foreach (var type in types)
        {
            var participant = HrotEnvironment.CreateParticipant(domain);
            participants.Add(participant);
            INetworkFactory factory = new NedNetworkFactory(participant, new NetworkEntityMap(), HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
                new FdpEventBus(), 0, NodeRole.None);
            var sub = SubsystemFactory.TryCreate(type, factory)!;
            if (sub is Hrot.CGF.CgfSubsystem cgf) cgf.Role = Hrot.CGF.CgfSubsystem.FusedServerRole;
            subsystems.Add(sub);
        }
        var orchestrator = new SubsystemOrchestrator(subsystems, new RunnerOptions { Headless = true, DomainId = domain });
        try
        {
            orchestrator.Initialize();
            var cgfNode = subsystems.OfType<Hrot.CGF.CgfSubsystem>().Single();
            var (node, bootstrapper) = NodeOf(cgfNode);
            Assert.NotNull(node);
            AssertNoDifferences(Check("server-fused", CompositionSnapshot.Describe("server-fused", node!, bootstrapper)));
        }
        finally
        {
            try { orchestrator.Shutdown(); } catch { /* best effort, as the harness */ }
            foreach (var p in participants) try { p.Dispose(); } catch { /* as the harness */ }
        }
    }

    private static (HrotNodeContext? Node, SharedApplicationBootstrapper? Bootstrapper) NodeOf(ISubsystem sub)
    {
        switch (sub)
        {
            case Hrot.TrafficNode.TrafficSubsystem t: return (t.Bootstrapper?.Context, t.Bootstrapper);
            case Hrot.CGF.CgfSubsystem c: return (c.NodeForTest, CompositionSnapshot.FindField<SharedApplicationBootstrapper>(c));
            default:
                var app = CompositionSnapshot.Field(sub, "_app");
                if (app is null) return (null, null);
                return (CompositionSnapshot.FindField<HrotNodeContext>(app), CompositionSnapshot.FindField<SharedApplicationBootstrapper>(app));
        }
    }
}
