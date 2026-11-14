using System;
using System.IO;
using System.Text.Json;

namespace Fdp.Core.Jobs
{
    /// <summary>
    /// ⭐⭐ U-2 (CE-4531, R-4224) — the user's performance choices, ONE file every HROT process on the machine reads at start (the editor, the
    /// player, the server, a separate node), so they agree by construction (R-4215). 🔒 User, 2026-11-13: "all optimization ON hardcoded by
    /// default … switchable ON/OFF easily … in the View -> Settings …, savable to user folder".
    /// <para>Precedence (<see cref="Resolve"/>): an explicit <c>HROT_*</c> environment variable (scripts, tests) &gt; the <c>-m all</c> rule (the
    /// AI-debug network mode runs SERIAL whatever the file says) &gt; the user file &gt; ON. The product entry point resolves and applies it;
    /// library defaults stay as they are, so a test harness keeps choosing explicitly.</para>
    /// </summary>
    public sealed record PerformanceSettings
    {
        /// <summary>The file's name in the user folder (beside the layout files).</summary>
        public const string FileName = "performance.json";

        /// <summary>The threaded node on a UI host (the sim on its worker, the UI on the staging: <c>HROT_THREADED_KERNEL=3</c>). Applies on restart.</summary>
        public bool ThreadedEditor { get; init; } = true;

        /// <summary>The per-entity parallel phases (<c>HROT_PARALLEL_SIM=1</c>), on every node.</summary>
        public bool ParallelSimulation { get; init; } = true;

        /// <summary>The worker pool's thread budget (<c>HROT_SIM_THREADS</c>); 0 = auto (cores − 2, at least 1).</summary>
        public int WorkerThreads { get; init; }

        /// <summary>The defaults: everything ON, the threads automatic.</summary>
        public static PerformanceSettings Default { get; } = new();

        /// <summary>The file at <paramref name="path"/>; the defaults when it is missing or unreadable (never throws — a bad file must not stop a node).</summary>
        public static PerformanceSettings Load(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Default;
            try { return JsonSerializer.Deserialize<PerformanceSettings>(File.ReadAllText(path), Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) ?? Default; }
            catch (Exception) { return Default; }
        }

        /// <summary>Writes the file (the menu's choice); the folder is created.</summary>
        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Serialization.FdpJsonOptionsRegistry.Indented));
        }

        /// <summary>
        /// What runs: <paramref name="env"/> (an explicit <c>HROT_THREADED_KERNEL</c> / <c>HROT_PARALLEL_SIM</c> / <c>HROT_SIM_THREADS</c>) wins;
        /// else <paramref name="serialMode"/> (<c>-m all</c>) turns both phases off; else the file. <paramref name="uiHost"/>: the threaded node is a UI
        /// host's (editor, player) — a headless node never runs it.
        /// </summary>
        public static ResolvedPerformance Resolve(PerformanceSettings file, Func<string, string?> env, bool serialMode, bool uiHost)
        {
            string? kernel = env("HROT_THREADED_KERNEL"), parallel = env("HROT_PARALLEL_SIM"), threads = env("HROT_SIM_THREADS");
            int threadedMode = !string.IsNullOrEmpty(kernel) ? (int.TryParse(kernel, out int k) ? k : 0)
                             : serialMode || !uiHost || !file.ThreadedEditor ? 0 : 3;
            bool par = !string.IsNullOrEmpty(parallel) ? parallel == "1" : !serialMode && file.ParallelSimulation;
            int n = int.TryParse(threads, out int t) && t > 0 ? t : Math.Max(0, file.WorkerThreads);
            string source = !string.IsNullOrEmpty(kernel) || !string.IsNullOrEmpty(parallel) || !string.IsNullOrEmpty(threads) ? "environment"
                          : serialMode ? "-m all (serial)" : "settings";
            return new ResolvedPerformance(threadedMode, par, n, source);
        }

        /// <summary>The line every process logs once at start (language-neutral).</summary>
        public static string Describe(in ResolvedPerformance r)
            => $"[Performance] threaded node {(r.ThreadedKernelMode == 3 ? "ON" : r.ThreadedKernelMode == 0 ? "off" : "mode " + r.ThreadedKernelMode)} · parallel simulation {(r.ParallelSimulation ? "ON" : "off")} · worker threads {(r.WorkerThreads > 0 ? r.WorkerThreads.ToString(System.Globalization.CultureInfo.InvariantCulture) : "auto")} ({r.Source})";
    }

    /// <summary>⭐ U-2 — what a process runs with, after the precedence (<see cref="PerformanceSettings.Resolve"/>).</summary>
    public readonly record struct ResolvedPerformance(int ThreadedKernelMode, bool ParallelSimulation, int WorkerThreads, string Source);
}

namespace Fdp.Core.Jobs
{
    /// <summary>⭐ CE-4585 (B-5) — WHERE each switch's value came from: <c>environment</c> (an explicit <c>HROT_*</c>), <c>-m all</c> (serial),
    /// <c>not a UI host</c> (the threaded node is a UI host's), <c>file</c> (the user's saved choice), <c>default</c> (no file: ON).</summary>
    public readonly record struct PerformanceSources(string ThreadedNode, string ParallelSimulation, string WorkerThreads);

    /// <summary>
    /// ⭐⭐ CE-4585 (B-5; Threaded Presentation §0a.1, R-4224) — what THIS process runs with, published ONCE at start by the product entry point
    /// (<c>PerformanceStartup.Apply</c>) so any host in the process — the debug API's <c>GET /performance</c> and <c>/status</c> — reads the one
    /// source. ⭐ The switches are read once, at start: a test chooses them by STARTING the process with the environment variables, and this
    /// confirms what it got. Nothing here changes them (a live toggle would give a mixed state — the brain / squad phases latch at type load).
    /// </summary>
    public static class PerformanceStatus
    {
        /// <summary>What runs (null: no product entry point published — an in-process test host).</summary>
        public static ResolvedPerformance? Running { get; private set; }

        /// <summary>Where each switch came from.</summary>
        public static PerformanceSources Sources { get; private set; }

        /// <summary>The user file's path (null: no user folder).</summary>
        public static string? UserFile { get; private set; }

        private static Func<ResolvedPerformance>? s_saved;

        /// <summary>The saved choice resolved as this process would at its next start (null when not published).</summary>
        public static ResolvedPerformance? SavedForNextStart() => s_saved?.Invoke();

        /// <summary>Published by the entry point, once.</summary>
        public static void Publish(ResolvedPerformance running, PerformanceSources sources, string? userFile, Func<ResolvedPerformance> savedForNextStart)
        {
            Running = running; Sources = sources; UserFile = userFile; s_saved = savedForNextStart;
        }

        /// <summary>Where each switch came from, for these inputs (pure — the rails' view).</summary>
        public static PerformanceSources SourcesOf(Func<string, string?> env, bool serialMode, bool uiHost, bool fileExists)
        {
            string saved = fileExists ? "file" : "default";
            string kernel = !string.IsNullOrEmpty(env("HROT_THREADED_KERNEL")) ? "environment" : serialMode ? "-m all" : !uiHost ? "not a UI host" : saved;
            string parallel = !string.IsNullOrEmpty(env("HROT_PARALLEL_SIM")) ? "environment" : serialMode ? "-m all" : saved;
            string threads = int.TryParse(env("HROT_SIM_THREADS"), out int t) && t > 0 ? "environment" : saved;
            return new PerformanceSources(kernel, parallel, threads);
        }

        /// <summary>The report <c>GET /performance</c> answers (language-neutral: integers and booleans only).</summary>
        public static System.Text.Json.Nodes.JsonObject Report()
        {
            var o = new System.Text.Json.Nodes.JsonObject();
            if (Running is not { } r)
            {
                o["running"] = null;
                o["note"] = "no product entry point published the performance switches in this process (an in-process host); the library defaults apply";
            }
            else
            {
                o["running"] = Describe(r, Sources);
                if (SavedForNextStart() is { } next) o["savedForNextStart"] = Describe(next, null);
                o["userFile"] = UserFile;
            }
            o["workerThreadsNow"] = FdpWorkerPool.Budget();
            // ⭐ CE-4586 — every declared variable this process was STARTED with (the one table, Fdp.Core.EnvVars); GET /help lists them all
            var envNow = new System.Text.Json.Nodes.JsonObject();
            foreach (var (v, value) in EnvVars.Received()) envNow[v.Name] = value;
            o["environment"] = envNow;
            o["howToChange"] = "the switches are read ONCE, at process start: start the process with HROT_PARALLEL_SIM=0|1, HROT_THREADED_KERNEL=0|3, "
                             + "HROT_SIM_THREADS=n (they win over the saved file), then GET /performance confirms. The saved file is the menu's (View > Settings > Performance) — never changed over HTTP.";
            return o;
        }

        private static System.Text.Json.Nodes.JsonObject Describe(ResolvedPerformance r, PerformanceSources? sources)
        {
            var o = new System.Text.Json.Nodes.JsonObject
            {
                ["threadedNode"] = r.ThreadedKernelMode == 3,
                ["threadedKernelMode"] = r.ThreadedKernelMode,
                ["parallelSimulation"] = r.ParallelSimulation,
                ["workerThreads"] = r.WorkerThreads,   // 0 = auto
                ["source"] = r.Source,
            };
            if (sources is { } s)
                o["sources"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["threadedNode"] = s.ThreadedNode, ["parallelSimulation"] = s.ParallelSimulation, ["workerThreads"] = s.WorkerThreads,
                };
            return o;
        }
    }
}
