using System.Diagnostics;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Fdp.Examples.ThreadedKernel;

/// <summary>
/// PROOF OF CONCEPT — §13.3. Modes:
///   --mode threaded   the kernel on a WORKER thread; the presentation pulled on the MAIN thread on a leased snapshot (the proposal)
///   --mode single     today's shape: the kernel ticked inside the main loop, the presentation reading World A directly (the baseline)
/// Options: --n 5000 · --work 200 (busy iterations per entity per tick) · --bench 10 (seconds; prints a result line, scripted drags) · --hidden
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        string mode = Arg(args, "--mode", "threaded");
        int n = int.Parse(Arg(args, "--n", "5000"));
        int work = int.Parse(Arg(args, "--work", "200"));
        double bench = double.Parse(Arg(args, "--bench", "0"), System.Globalization.CultureInfo.InvariantCulture);
        bool threaded = mode == "threaded";

        var settings = new SimSettings { TargetEntities = n, WorkPerEntity = work, WithPayload = args.Contains("--payload") };
        var stats = new WorkerStats();
        var accumulator = new EventAccumulator();
        var port = new SnapshotPort(stats, accumulator);
        var ingress = new CommandIngress();
        var ui = new UiStore();

        // ── the kernel — ONE kernel, its world A, its one simulation module (the real kernel, unchanged) ──
        var live = new EntityRepository();
        Schema.Register(live);
        live.SetSingletonUnmanaged(new GlobalTime());
        var kernel = new ModuleHostKernel(live, accumulator);
        var clock = new LoopClock();
        kernel.SetTimeController(clock);
        kernel.RegisterModule(new SimulationModule(settings, ingress, port));
        kernel.Initialize();

        // ── the presentation — an ordinary module, run by the render loop ──
        var presentation = new PulledModuleRunner(new PresentationModule(ui, ingress));

        // ── the worker (threaded mode) ──
        var running = true;
        Thread? worker = null;
        if (threaded)
        {
            worker = new Thread(() =>
            {
                var sw = Stopwatch.StartNew(); double last = 0;
                while (Volatile.Read(ref running))
                {
                    double now = sw.Elapsed.TotalSeconds; float dt = (float)Math.Min(0.1, now - last); last = now;
                    long t = Stopwatch.GetTimestamp();
                    clock.NextDelta = dt; kernel.Update(dt);
                    stats.Ticks++;
                    stats.TickMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    double spare = 1.0 / 60 - (sw.Elapsed.TotalSeconds - now);   // a light simulation is paced at 60 Hz, a heavy one runs flat out
                    if (spare > 0.001) Thread.Sleep(TimeSpan.FromSeconds(spare));
                }
            }) { IsBackground = true, Name = "kernel-worker" };
            worker.Start();
        }

        if (args.Contains("--hidden")) Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(1280, 800, "PoC — kernel on a worker, presentation pulled");
        Raylib.SetTargetFPS(0);
        rlImGui.Setup(true);

        var frameMs = new double[240]; int frameIdx = 0, frames = 0;
        var benchClock = Stopwatch.StartNew();
        double nextDrag = 1.0; int drags = 0; long dragSubmittedTick = -1; double visibleLatencySum = 0; int visibleLatencyN = 0; long dragSubmitAt = 0;
        long allocStart = 0; bool allocArmed = false; long allocFrames = 0, allocBytes = 0;
        double snapshotAgeSum = 0; int snapshotAgeN = 0; long bouncesSeen = 0, lastConsumedTick = -1;

        while (!Raylib.WindowShouldClose())
        {
            long frameStart = Stopwatch.GetTimestamp();
            float dt = Raylib.GetFrameTime();
            if (allocArmed) { allocBytes += GC.GetAllocatedBytesForCurrentThread() - allocStart; allocFrames++; }
            allocStart = GC.GetAllocatedBytesForCurrentThread(); allocArmed = benchClock.Elapsed.TotalSeconds > 2;

            // the baseline: today's shape — the simulation ticks INSIDE the frame on World A
            if (!threaded)
            {
                long t = Stopwatch.GetTimestamp();
                clock.NextDelta = Math.Min(0.1f, dt); kernel.Update(Math.Min(0.1f, dt));
                stats.Ticks++; stats.TickMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            }

            // ⭐ the pull: lease the latest snapshot (threaded) — or World A itself (baseline)
            SnapshotPort.Snapshot? leased = threaded ? port.Lease() : null;
            ISimulationView? view = threaded ? leased?.World : live;

            // a scripted drag in bench mode: every second, move the first entity to a new place and time until it is VISIBLE
            if (bench > 0 && view != null && ui.Count > 0 && benchClock.Elapsed.TotalSeconds > nextDrag && dragSubmittedTick < 0)
            {
                nextDrag += 1.0; drags++;
                ui.Selected = ui.Ids[0];
                var b = ingress.Rent(); b.SetComponent(ui.Selected, new Position { X = 500 + drags, Y = 500 }); b.SetComponent(ui.Selected, new Velocity()); ingress.Submit(b);
                dragSubmittedTick = drags; dragSubmitAt = Stopwatch.GetTimestamp();
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);
            rlImGui.Begin();
            if (view != null)
            {
                DrawMapWindow(ui);   // input for the presentation comes from ImGui (canvas-local)
                presentation.RunFrame(view, dt);
                if (dragSubmittedTick > 0 && ui.SelectedAlive && Math.Abs(ui.SelectedPos.X - (500 + drags)) < 0.01f && Math.Abs(ui.SelectedPos.Y - 500) < 0.01f)
                {
                    visibleLatencySum += Stopwatch.GetElapsedTime(dragSubmitAt).TotalMilliseconds; visibleLatencyN++; dragSubmittedTick = -1;
                }
                if (threaded && leased != null && leased.Tick != lastConsumedTick) { bouncesSeen += leased.Bounces.Count; lastConsumedTick = leased.Tick; }   // ⭐ ONCE per snapshot, however many frames re-run on it
                if (threaded && leased != null) { snapshotAgeSum += stats.Ticks - leased.Tick; snapshotAgeN++; }
            }
            DrawStatsWindow(threaded, settings, stats, port, ingress, presentation, frameMs, frames);
            rlImGui.End();
            Raylib.EndDrawing();

            if (leased != null) port.Release(leased);
            frameMs[frameIdx] = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds; frameIdx = (frameIdx + 1) % frameMs.Length; frames++;

            if (bench > 0 && benchClock.Elapsed.TotalSeconds >= bench) break;
        }

        Volatile.Write(ref running, false);
        worker?.Join(2000);

        if (bench > 0)
        {
            var window = frameMs.Take(Math.Min(frames, frameMs.Length)).OrderBy(x => x).ToArray();
            double p50 = window[window.Length / 2], p95 = window[(int)(window.Length * 0.95)], max = window[^1];
            Console.WriteLine((
                $"RESULT mode={mode} payload={settings.WithPayload} n={settings.TargetEntities} work={settings.WorkPerEntity} frames={frames} fps={frames / benchClock.Elapsed.TotalSeconds:F1} " +
                $"frame_p50_ms={p50:F2} frame_p95_ms={p95:F2} frame_max_ms={max:F2} sim_tick_ms={stats.TickMs:F2} sim_hz={stats.Ticks / benchClock.Elapsed.TotalSeconds:F1} " +
                $"publish_ms={stats.PublishMs:F2} snapshots={port.SnapshotsCreated} snapshot_age_ticks={(snapshotAgeN > 0 ? snapshotAgeSum / snapshotAgeN : 0):F2} " +
                $"cmd_apply_ms={ingress.LastApplyLatencyMs:F1} drag_visible_ms={(visibleLatencyN > 0 ? visibleLatencySum / visibleLatencyN : -1):F1} drags={drags}/{visibleLatencyN} " +
                $"refused={ingress.Refused} bounces_published={settings.BouncesPublished} bounces_seen={bouncesSeen} main_alloc_b_per_frame={(allocFrames > 0 ? allocBytes / allocFrames : -1)} " +
                $"prep_ms={presentation.Timings[0].Ms:F2} interact_ms={presentation.Timings[1].Ms:F2}"));
        }
        rlImGui.Shutdown();
        Raylib.CloseWindow();
        return 0;
    }

    private static void DrawMapWindow(UiStore ui)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(640, 660), ImGuiCond.FirstUseEver);
        ImGui.Begin("Map");
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(1000 * ui.CanvasScale, 1000 * ui.CanvasScale);
        ImGui.InvisibleButton("canvas", size);
        var io = ImGui.GetIO();
        ui.Mouse = io.MousePos - origin;
        ui.MouseInCanvas = ImGui.IsItemHovered();
        ui.Down = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        ui.Pressed = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        ui.Released = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, 0xFF202020);
        // the points prepared LAST frame (the prep system runs after this window is laid out — one frame of display lag, fine for a PoC)
        int step = Math.Max(1, ui.Count / 12000);   // ⚠ PoC artifact: one ImGui draw list holds < 65 536 vertices — draw a sample; the prep still sees every entity
        for (int i = 0; i < ui.Count; i += step)
        {
            var p = origin + new Vector2(ui.Xs[i], ui.Ys[i]) * ui.CanvasScale;
            dl.AddRectFilled(p, p + new Vector2(2, 2), ui.Ids[i] == ui.Selected ? 0xFF00FFFFu : 0xFF60C060u);
        }
        if (ui.Dragging) { var g = origin + ui.Ghost * ui.CanvasScale; dl.AddCircle(g, 6, 0xFFFFFFFFu); }
        ImGui.End();
    }

    private static void DrawStatsWindow(bool threaded, SimSettings s, WorkerStats st, SnapshotPort port, CommandIngress ing, PulledModuleRunner pr, double[] frameMs, int frames)
    {
        ImGui.SetNextWindowPos(new Vector2(660, 10), ImGuiCond.FirstUseEver);
        ImGui.Begin("Stats");
        double avg = 0; int c = Math.Min(frames, frameMs.Length); for (int i = 0; i < c; i++) avg += frameMs[i]; avg = c > 0 ? avg / c : 0;
        ImGui.Text(threaded ? "mode: THREADED (kernel on a worker)" : "mode: SINGLE (today's shape)");
        ImGui.Text($"main frame avg {avg:F2} ms");
        ImGui.Text($"sim tick {st.TickMs:F2} ms · publish {st.PublishMs:F2} ms · ticks {st.Ticks}");
        ImGui.Text($"snapshots allocated {port.SnapshotsCreated} · commands applied {ing.Applied} · last apply {ing.LastApplyLatencyMs:F1} ms");
        foreach (var (name, ms) in pr.Timings) ImGui.Text($"  {name}: {ms:F2} ms");
        int n = s.TargetEntities; if (ImGui.SliderInt("entities", ref n, 100, 50000)) s.TargetEntities = n;
        int w = s.WorkPerEntity; if (ImGui.SliderInt("brain work", ref w, 0, 2000)) s.WorkPerEntity = w;
        bool p = s.Paused; if (ImGui.Checkbox("paused", ref p)) s.Paused = p;
        ImGui.End();
    }

    private static string Arg(string[] a, string name, string dflt) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : dflt; }
}
