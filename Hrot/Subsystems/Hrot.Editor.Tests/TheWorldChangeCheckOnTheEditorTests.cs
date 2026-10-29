using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <c>CE-4093</c> (AQ108 D20) — THE NOISE TEST, on a real scenario in a real (headless) <see cref="EditorSubsystem"/>: the node's
/// unsaved-changes check answers "unchanged" for a world nobody edited — however much the camera, the selection and the panels move — "changed"
/// for an edit, and "unchanged" again after the editor's own save. The baseline in every case is the one the PRODUCTION hooks took (the load chain's,
/// the save wrapper's) — nothing here takes one by hand.
/// <para>📐 <b>Measured (2026-10-28, Debug)</b>: <c>uc-urban-battle</c> (on demo-city, 682 entities) loads headless in the real editor; two
/// churns were found and each is fixed at its cause — see <c>WorldChangeCheck</c>'s remarks (the keyframe's per-entity <c>LastChangeTick</c>) and
/// <c>CullingState</c>'s own flag (the camera's culling).</para>
/// 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md D20.
/// </summary>
public sealed class TheWorldChangeCheckOnTheEditorTests
{
    private readonly ITestOutputHelper _out;
    public TheWorldChangeCheckOnTheEditorTests(ITestOutputHelper output) => _out = output;

    /// <summary>The scenario's files, copied under a unique name into the shared scenarios root the editor loads from.</summary>
    private static string CopyScenario(string source, out string dir)
    {
        string repoRoot = AppContext.BaseDirectory;
        while (repoRoot != null && !Directory.Exists(Path.Combine(repoRoot, "scenarios", source))) repoRoot = Path.GetDirectoryName(repoRoot)!;
        if (repoRoot == null) throw new DirectoryNotFoundException("scenarios/" + source);
        string name = "ce4093_" + Guid.NewGuid().ToString("N");
        dir = Path.Combine(EditorBootstrap.ScenariosRoot, name);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "scenarios", source)))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        return name;
    }

    /// <summary>Frames until the load has RESOLVED on the node: the scenario's units stand in the editor's world and the load chain's hook has taken the
    /// node's baseline (a real wall-clock wait: the load works off the frame thread). ⚠ Neither the master's state nor the entity count says it —
    /// 📐 measured: the editor's master reports OperatingEdit ~8 frames after the request, 630 of the 682 units arrive ~15 s later, and the chain
    /// resolves one frame after the last.</summary>
    private static void RunUntilLoaded(EditorSubsystem editor, int seconds)
    {
        var master = editor.ClusterMasterForTest!;
        var world = editor.NodeForTest!.World;
        var trace = new System.Text.StringBuilder();
        var until = DateTime.UtcNow.AddSeconds(seconds);
        int last = -1;
        while (DateTime.UtcNow < until)
        {
            editor.Update(0.016f);
            System.Threading.Thread.Sleep(10);
            int st = (int)master.CurrentClusterState;
            if (st != last) { trace.Append($" {master.CurrentClusterState}(entities={world.EntityCount})"); last = st; }
            if (st == (int)ClusterState.OperatingEdit && world.EntityCount > 0 && Answer(editor) != WorldChangeCheckHandler.NoBaseline) return;
        }
        var handler = editor.NodeForTest!.ClusterSlave.RegisteredHandlers.OfType<WorldChangeCheckHandler>().Single();
        var check = (WorldChangeCheck)typeof(WorldChangeCheckHandler).GetField("_check", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(handler)!;
        Assert.Fail($"the load did not finish in {seconds}s; state trace:{trace}; now: state={master.CurrentClusterState} entities={world.EntityCount} " +
                    $"hasBaseline={check.HasBaseline} settling={check.IsSettling} settleFrames={check.SettledAfterFrames}");
    }

    private static string Answer(EditorSubsystem editor)
    {
        var handler = editor.NodeForTest!.ClusterSlave.RegisteredHandlers.OfType<WorldChangeCheckHandler>().Single();
        var intent = new ExecuteNodeOpIntent { Operation = Fdp.Toolkit.Orchestration.NodeOpType.CheckUnsaved, TransactionId = Guid.NewGuid() };
        return (string)handler.PrepareAsync(intent, default).GetAwaiter().GetResult()!;
    }

    private static void Frames(EditorSubsystem editor, int n, int sleepMs = 0)
    {
        for (int i = 0; i < n; i++) { editor.Update(0.016f); if (sleepMs > 0) System.Threading.Thread.Sleep(sleepMs); }
    }

    /// <summary>One content hash per recordable component type — the types whose hash moved between two points are the churners of that stretch
    /// (the failure message names them: "diff two captures by type").</summary>
    private static Dictionary<int, ulong> PerType(EntityRepository world)
    {
        var map = new Dictionary<int, ulong>();
        foreach (int id in ComponentTypeRegistry.GetRecordableTypeIds().ToArray())
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                new RecorderSystem { ComponentTypeFilter = t => t == id, RecordSingletonTables = false }.RecordKeyframe(world, w, 0, null);
                w.Flush();
            }
            map[id] = WorldChangeCheck.HashOf(WorldKeyframe.FromRecordedFrame(ms.ToArray()));
        }
        return map;
    }

    private static string Churners(Dictionary<int, ulong> a, Dictionary<int, ulong> b)
        => string.Join(", ", a.Where(kv => b.TryGetValue(kv.Key, out var h) && h != kv.Value).Select(kv => ComponentTypeRegistry.GetType(kv.Key)?.FullName ?? kv.Key.ToString()));

    /// <summary>One real load of <paramref name="scenario"/> into the headless editor, run to the moment the node's baseline stands; the stages work on it
    /// and the editor and the scenario copy are cleaned up after.</summary>
    private void WithTheScenarioLoaded(string scenario, Action<EditorSubsystem, EntityRepository> stages, int httpPort = 0)
    {
        string name = CopyScenario(scenario, out var dir);
        var editor = new EditorSubsystem();
        // ⭐ CE-4093 (master side) — the editor's debug API starts when this variable names a port AT INITIALIZE: the master-side rails below talk HTTP to it
        string? before = Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT");
        if (httpPort > 0) Environment.SetEnvironmentVariable("HROT_DEBUG_API_PORT", httpPort.ToString());
        // ⭐ CE-3340 ③ — started through the ONE host start (SubsystemOrchestrator.Initialize), as every host is: it seeds the NAS (terrain, scenarios) before the
        // editor reads it. ⛔ A bare editor.Initialize(...) is not a host start and no longer seeds.
        try { new SubsystemOrchestrator(new ISubsystem[] { editor }, new RunnerOptions { Headless = true }).Initialize(); }
        finally { if (httpPort > 0) Environment.SetEnvironmentVariable("HROT_DEBUG_API_PORT", before); }
        try
        {
            // ⭐ CE-1442 — the run gate is closed until the master's state (and the in-sync set) has reached the editor's node; the guarded open refuses before that, exactly as a user's click would
            var gateUntil = DateTime.UtcNow.AddSeconds(30);
            while (!editor.NodeForTest!.ClusterSlave.View.GateOpen(Fdp.Toolkit.Orchestration.HostGatePolicy.Operator) && DateTime.UtcNow < gateUntil) { editor.Update(0.016f); System.Threading.Thread.Sleep(10); }
            Assert.True(editor.NodeForTest!.ClusterSlave.View.GateOpen(Fdp.Toolkit.Orchestration.HostGatePolicy.Operator), editor.NodeForTest!.ClusterSlave.View.NotOperableReason);
            editor.EditorLogic.LoadScenarioByName(name);
            RunUntilLoaded(editor, 120);
            stages(editor, editor.NodeForTest!.World);
        }
        finally { editor.Shutdown(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>One HTTP call to the editor's debug API, with the editor's frames pumped meanwhile (the routes run on the main thread, in <c>Update</c>).</summary>
    private static (System.Net.HttpStatusCode Status, System.Text.Json.Nodes.JsonNode Body, double Ms) Call(EditorSubsystem editor, System.Net.Http.HttpClient client, int port, string method, string path, string json = "{}")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var task = method == "GET"
            ? client.GetAsync($"http://localhost:{port}{path}")
            : client.PostAsync($"http://localhost:{port}{path}", new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var until = DateTime.UtcNow.AddSeconds(90);
        while (!task.IsCompleted && DateTime.UtcNow < until) { editor.Update(0.016f); System.Threading.Thread.Sleep(1); }
        Assert.True(task.IsCompleted, $"{method} {path} did not answer in 90 s");
        var response = task.GetAwaiter().GetResult();
        double ms = sw.Elapsed.TotalMilliseconds;
        return (response.StatusCode, System.Text.Json.Nodes.JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())!, ms);
    }

    /// <summary>Asserts the node's answer; a wrong one names the component types whose content moved since <paramref name="reference"/>.</summary>
    private static void Expect(EditorSubsystem editor, EntityRepository world, Dictionary<int, ulong> reference, string expected, string stage)
    {
        string answer = Answer(editor);
        Assert.True(answer == expected,
            $"[{stage}] the node answered '{answer}', expected '{expected}'; types whose content moved since the load: {Churners(reference, PerType(world))}");
    }

    /// <summary>A unit with a name and a network id — the one every edit below targets.</summary>
    private static (Entity Unit, long NetworkId) AUnit(EntityRepository world)
    {
        foreach (var e in world.Query().With<EntityInfo>().With<Fdp.Toolkit.Replication.Components.NetworkIdentity>().Build())
            return (e, world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value);
        throw new InvalidOperationException("the scenario loaded no named unit");
    }

    /// <summary>The rename dialog's own path (EntityRenameModal → IEditorLogic.CommitPropertyEdit → UpdateEntityCommand, applied by the spawning pack). ⚠ A raw
    /// write of a unit's <c>SimTransform</c> is put back by the next frame — it is no edit.</summary>
    private static void Rename(EditorSubsystem editor, EntityRepository world, (Entity Unit, long NetworkId) unit, string to)
    {
        var info = world.GetComponent<EntityInfo>(unit.Unit);
        info.Name = new FixedString64(to);
        editor.EditorLogic.CommitPropertyEdit(unit.NetworkId, new object[] { info });
        Frames(editor, 3);
    }

    /// <summary>The map camera's culling viewport, which the editor's frame hands to <c>MapCullingSystem</c> (a headless editor has no screen to project one from).</summary>
    private static void SetViewport(EditorSubsystem editor, float minX, float maxX, float minY, float maxY, float zoom)
    {
        var viewport = typeof(EditorSubsystem).GetField("_cameraViewport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(editor)!;
        var t = viewport.GetType();
        t.GetProperty("WorldMinX")!.SetValue(viewport, minX); t.GetProperty("WorldMaxX")!.SetValue(viewport, maxX);
        t.GetProperty("WorldMinY")!.SetValue(viewport, minY); t.GetProperty("WorldMaxY")!.SetValue(viewport, maxY);
        t.GetProperty("Zoom")!.SetValue(viewport, zoom);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4093</c> success conditions 1, 6(a), 7 — <b>THE NOISE TEST.</b> <c>uc-urban-battle</c> (on demo-city, 682 units) is loaded into editing in a real
    /// headless editor. The baseline is the one the LOAD CHAIN's hook took (nothing here takes one) — "unchanged" the moment the load has resolved. Then, touching
    /// nothing else with the clock stopped: 200 frames pass; the map camera pans and zooms (the culling viewport — it rewrites <c>CullingState</c> of every unit); a
    /// unit is selected and deselected (it adds <c>SelectionState</c>); the windows are registered — after each stretch the node still answers "unchanged". An edit
    /// (a rename through the authoring path) then reads "changed". Last, what one check costs (printed).
    /// 🔴 Mutations (each compiles, each reddens this): the load chain's <c>BeginSettling</c> removed ⇒ stage 1 times out at "no-baseline"; <c>SettleStep</c>
    /// baselining at its first frame ⇒ "changed" at once (the first frame's initialisation moved nine types); <c>CullingState</c> without <c>NoReplay</c> ⇒ the camera
    /// stretch reads "changed", naming <c>CullingState</c>; <c>HashOf</c> not skipping <c>LastChangeTick</c> ⇒ the selection stretch reads "changed";
    /// <c>HasChanged</c> always false ⇒ the edit stage fails.
    /// </summary>
    [Fact(Timeout = 400_000)]
    public void TheNoiseTest_AQuietEditorOnARealScenario_ReadsUnchanged_AnEditReadsChanged()
    {
        WithTheScenarioLoaded("uc-urban-battle", (editor, world) =>
        {
            var reference = PerType(world);
            double t0 = editor.TimeControllerForTest!.GetCurrentState().TotalTime;
            void Quiet(string stage) => Expect(editor, world, reference, "unchanged", stage);

            Quiet("the moment the load resolved (the hook's baseline)");
            Frames(editor, 200, sleepMs: 2);                                          Quiet("200 quiet frames");
            SetViewport(editor, -500, 150, -500, 790, 1f);   Frames(editor, 5);       Quiet("the camera over the west half");
            SetViewport(editor, 150, 800, -500, 790, 0.1f);  Frames(editor, 5);       Quiet("the camera over the east half, zoomed out");
            SetViewport(editor, 0, 0, 0, 0, 1f);             Frames(editor, 5);       Quiet("the camera unset again");

            var unit = AUnit(world);
            editor.SetSelection2D(unit.Unit);  Frames(editor, 5);                     Quiet("a unit selected");
            editor.SetSelection2D(null);       Frames(editor, 5);                     Quiet("the selection cleared");
            editor.RegisterWindows(new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 16f, 16f)));
            Frames(editor, 10);                                                       Quiet("the windows registered");
            Assert.Equal(t0, editor.TimeControllerForTest!.GetCurrentState().TotalTime);   // the clock stayed stopped throughout

            Rename(editor, world, unit, "ce4093-renamed");
            Expect(editor, world, reference, "changed", "a unit renamed through the authoring path");
            Assert.Contains("Fdp.Core.EntityInfo", Churners(reference, PerType(world)));

            // what one check costs: capture + hash, the median of 10 after 2 warm-up rounds
            var capture = new List<double>(); var hash = new List<double>(); var whole = new List<double>(); int bytes = 0;
            for (int i = 0; i < 12; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var kf = WorldKeyframes.Capture(world, WorldChangeCheck.DefaultOptions);
                double c = sw.Elapsed.TotalMilliseconds; sw.Restart();
                WorldChangeCheck.HashOf(kf);
                double h = sw.Elapsed.TotalMilliseconds; sw.Restart();
                Answer(editor);
                double a = sw.Elapsed.TotalMilliseconds;
                bytes = kf.SizeBytes;
                if (i >= 2) { capture.Add(c); hash.Add(h); whole.Add(a); }
            }
            double Median(List<double> v) { var sorted = v.OrderBy(x => x).ToList(); return (sorted[4] + sorted[5]) / 2; }
            _out.WriteLine($"uc-urban-battle: {world.EntityCount} entities, keyframe {bytes / 1024.0 / 1024.0:F2} MiB; median of 10 — capture {Median(capture):F1} ms, hash {Median(hash):F1} ms, one whole answer (capture + hash) {Median(whole):F1} ms");
            Assert.True(bytes > 100_000, "a real world was captured");
            Assert.True(Median(whole) < 2_000, $"a check round on one node took {Median(whole):F0} ms");
        });
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4093</c> success conditions 2 + 6(b) — an edit reads "changed"; the editor's OWN save — the real storage round (master → the node's
    /// <c>SerializeLocal</c> through the composition's save wrapper → merge) — makes the node read "unchanged" again; the next edit reads "changed" again.
    /// 📐 <c>pc-defend-house</c>: saving a freshly loaded <c>uc-urban-battle</c> in this headless editor throws in <c>ScenarioSerializer</c> (<c>SaveResolver: entity
    /// Entity.Null is not in the save map</c>, with no edit at all — pre-existing at base <c>1a5bd73a1</c>, see the report), so the save stage runs on a scenario that saves.
    /// 🔴 Mutations: the save wrapper's <c>Commit</c> not taking the baseline ⇒ the post-save wait times out at "changed"; <c>HasChanged</c> always false ⇒ the edit
    /// stage fails.
    /// </summary>
    [Fact(Timeout = 400_000)]
    public void AnEditReadsChanged_TheEditorsOwnSaveReadsUnchangedAgain_TheNextEditReadsChanged()
    {
        WithTheScenarioLoaded("pc-defend-house", (editor, world) =>
        {
            var reference = PerType(world);
            Expect(editor, world, reference, "unchanged", "the moment the load resolved (the hook's baseline)");

            var unit = AUnit(world);
            Rename(editor, world, unit, "ce4093-first");
            Expect(editor, world, reference, "changed", "a unit renamed through the authoring path");

            editor.EditorLogic.SaveCurrentScenario();                                 // the real round
            string answer = "";
            var until = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < until && (answer = Answer(editor)) != "unchanged") Frames(editor, 1, sleepMs: 10);
            Assert.True(answer == "unchanged", $"[after the editor's own save] the node answered '{answer}' — its part of the completed save did not re-baseline");

            reference = PerType(world);
            Rename(editor, world, unit, "ce4093-second");
            Expect(editor, world, reference, "changed", "a unit renamed again after the save");
        });
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4093</c> (master side) success condition 5 — <b>THE CHECK ROUND END TO END</b>, over the editor's real debug API on a real scenario
    /// (<c>pc-defend-house</c>): the master asks the editor's node (its production handler, its production baseline hooks), aggregates and answers over HTTP.
    /// Loaded ⇒ <c>{changed:false}</c> and <c>/status.scenarioDirty:false</c>; a unit renamed through the authoring path ⇒ <c>{changed:true}</c> naming the node,
    /// <c>scenarioDirty:true</c> and <c>POST /scenario/load/live</c> answers 409 <i>"the scenario has unsaved changes"</i> with the cluster still in editing; the
    /// editor's own save (the real storage round, pull + merge) ⇒ <c>{changed:false}</c> again, <c>scenarioDirty:false</c>.
    /// 🔴 Mutations (each compiles): the editor's <c>unsavedCheck</c> not wired ⇒ the first call times out / 504; <c>UnsavedCheckAggregator</c> not registered in
    /// <c>OrchestratorCore</c> ⇒ "changed" at once (an unaggregated round reads as changed); the gate not called from <c>HandleScenarioLoad</c> ⇒ the 409 cell red;
    /// the master's save-failure fact never cleared ⇒ the post-save cell stays changed.
    /// </summary>
    [Fact(Timeout = 400_000)]
    public void TheCheckRoundOverHttp_UnchangedAfterTheLoad_ChangedAfterAnEdit_RefusesTheLiveLoad_UnchangedAfterTheSave()
    {
        int port = FreePort();
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        WithTheScenarioLoaded("pc-defend-house", (editor, world) =>
        {
            var checkUnsaved = () => Call(editor, client, port, "POST", "/scenario/check-unsaved");
            bool? Dirty() => Call(editor, client, port, "GET", "/status").Body["data"]!["scenarioDirty"]?.GetValue<bool>();

            Assert.Null(Dirty());                                                      // no round yet ⇒ unknown, and asking did not run one
            var (status, body, _) = checkUnsaved();
            Assert.Equal(System.Net.HttpStatusCode.OK, status);
            Assert.False(body["data"]!["changed"]!.GetValue<bool>(), $"[loaded] {body}");
            Assert.Empty(body["data"]!["changedNodes"]!.AsArray());
            Assert.False(Dirty());

            var unit = AUnit(world);
            Rename(editor, world, unit, "ce4093-http");
            (status, body, _) = checkUnsaved();
            Assert.Equal(System.Net.HttpStatusCode.OK, status);
            Assert.True(body["data"]!["changed"]!.GetValue<bool>(), $"[renamed] {body}");
            Assert.NotEmpty(body["data"]!["changedNodes"]!.AsArray());                 // the editor's node is named
            Assert.True(Dirty());

            string other = "ce4093_other";
            (status, body, _) = Call(editor, client, port, "POST", "/scenario/load/live", $"{{\"name\":\"{other}\"}}");
            Assert.Equal(System.Net.HttpStatusCode.Conflict, status);
            Assert.Contains("the scenario has unsaved changes", body["error"]!.GetValue<string>());
            Assert.Equal((int)ClusterState.OperatingEdit, (int)editor.ClusterMasterForTest!.CurrentClusterState);   // nothing was unloaded

            editor.EditorLogic.SaveCurrentScenario();                                  // the real round
            bool? changed = null;
            var until = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < until)
            {
                (status, body, _) = checkUnsaved();
                Assert.Equal(System.Net.HttpStatusCode.OK, status);
                changed = body["data"]!["changed"]!.GetValue<bool>();
                if (changed == false) break;
                Frames(editor, 5, sleepMs: 10);
            }
            Assert.True(changed == false, $"[after the editor's own save] the master still said changed: {body}");
            Assert.False(Dirty());
        }, httpPort: port);
    }

    /// <summary>
    /// ⭐ <c>CE-4093</c> (master side) success condition 6 — what ONE check round costs end to end on the editor with <c>uc-urban-battle</c> loaded (682 units):
    /// <c>POST /scenario/check-unsaved</c> from request to answer — master fan-out, the node's capture + hash, aggregation, the read model, the route's polling — the
    /// median of 5 after a warm-up round (printed). The editor's frames are pumped back to back while it waits, so the figure is compute, not frame pacing.
    /// </summary>
    [Fact(Timeout = 400_000)]
    public void TheCheckRound_OnTheBigScenario_TimedOverHttp()
    {
        int port = FreePort();
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        WithTheScenarioLoaded("uc-urban-battle", (editor, world) =>
        {
            var warm = Call(editor, client, port, "POST", "/scenario/check-unsaved");
            Assert.False(warm.Body["data"]!["changed"]!.GetValue<bool>(), $"[loaded] {warm.Body}");
            var times = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var (status, body, ms) = Call(editor, client, port, "POST", "/scenario/check-unsaved");
                Assert.Equal(System.Net.HttpStatusCode.OK, status);
                Assert.False(body["data"]!["changed"]!.GetValue<bool>(), $"[round {i}] {body}");
                times.Add(ms);
            }
            double median = times.OrderBy(x => x).ElementAt(2);
            _out.WriteLine($"uc-urban-battle: {world.EntityCount} entities; POST /scenario/check-unsaved, 5 rounds (ms): {string.Join(", ", times.Select(t => t.ToString("F0")))}; median {median:F0} ms");
            Assert.True(median < 5_000, $"one check round took {median:F0} ms");
        }, httpPort: port);
    }
}
