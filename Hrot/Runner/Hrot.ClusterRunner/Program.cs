using CommandLine;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Presentation.Windows;
using Fdp.Presentation.WindowManager;
using Hrot.BDC.Factory;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Scenarios;
using Hrot.ClusterRunner.Services;
using Hrot.ClusterRunner.Systems;
using Hrot.Common;
using Hrot.Common.Scenario.Migrations;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using ImGuiNET;
using NLog;
using NLog.Config;
using NLog.Targets;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.Runner;

/// <summary>
/// Entry point for the Hrot Runner process.
///
/// <para>The Runner can host one or more subsystems in a single process
/// (<c>--mode all</c> or <c>--mode simhost,ig</c>) or launch a single
/// subsystem in standalone mode (<c>--mode simhost</c>).
/// When running in standalone mode, the Waiting Room protocol
/// synchronises startup with peer processes unless <c>--no-wait</c> is supplied.
/// </para>
///
/// <para><b>CLI arguments:</b>
/// <list type="table">
///   <item><term>-m / --mode</term><description>all | simhost | ig | ios | comma-separated</description></item>
///   <item><term>-d / --domain</term><description>DDS domain ID (default: 0)</description></item>
///   <item><term>--headless</term><description>Run without UI</description></item>
///   <item><term>--no-wait</term><description>Skip waiting room synchronisation</description></item>
///   <item><term>--wait-for</term><description>Comma-separated peers to wait for</description></item>
///   <item><term>-c / --config</term><description>JSON config file path</description></item>
/// </list>
/// </para>
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        FdpConfig.EnforceExplicitComponentIds = true;
        FdpConfig.EnforceExplicitEventRegistration = true;

        // Enable NLog Console Output globally for FdpLog<T>.
        // Also register the UI target so the Message Log window captures all output.
        var logConfig = new LoggingConfiguration();
        var logConsole = new ColoredConsoleTarget("logConsole")
        {
            Layout = "${time} | ${level:uppercase=true:padding=-5} | ${logger:shortName=true} | ${message}${exception:format=tostring}"
        };
        logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, logConsole);
        logConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, NLogMessageLogTarget.SharedInstance);

        // Parse CLI args
        HrotRunnerConfiguration? config = null;
        var parseResult = Parser.Default.ParseArguments<HrotRunnerConfiguration>(args);

        parseResult.WithParsed(c => config = c)
                   .WithNotParsed(_ => Environment.Exit(1));

        if (config is null)
            return 1;

        // Merge optional JSON config file
        if (!string.IsNullOrEmpty(config.ConfigFile))
        {
            try
            {
                config.MergeFromJsonFile(config.ConfigFile);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"[Runner] ERROR: {ex.Message}");
                return 1;
            }
        }

        // Validate (parses mode/wait-for strings)
        try
        {
            config.Validate();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[Runner] Configuration error: {ex.Message}");
            return 1;
        }

        // ── Dump-api mode: print the debug-API manifest and exit ────────────────────────────────
        //
        // ⭐⭐⭐ HN-030: this is what `tools/ai-debug-mcp` generates its tool catalog from, so the catalog
        //    stops being a hand-maintained mirror of these routes. 📄 MCP_Integration.md § Follow-up.
        //
        // ⭐ Boots NOTHING — no DDS, no window, no world. DebugApiHost.EnumerateRouteTemplates only builds
        //   closures, so the whole API can describe itself in-process in milliseconds. ⇒ `npm run gen:skill`
        //   can regenerate from source without an editor running.
        // ⛔ Ask a LIVE GET /capabilities for what a given host can DO; a dump answers only what the API IS.
        if (config.RequestedSubsystems.Contains("dump-api"))
        {
            Console.Out.Write(Hrot.Editor.DebugApi.DebugApiHost.DumpManifestJson());
            return 0;
        }

        Console.WriteLine($"[Runner] Starting – mode={string.Join(",", config.RequestedSubsystems)}, domain={config.DomainId}, headless={config.Headless}");

        // Add NLog file target when LogDirectory is configured
        string resolvedLogDir = string.IsNullOrWhiteSpace(config.LogDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : Path.GetFullPath(config.LogDirectory);
        Directory.CreateDirectory(resolvedLogDir);
        NLog.ScopeContext.PushProperty("nodeId", config.NodeId.ToString());

        string subsystemTag = config.RequestedSubsystems.Count > 0
            ? string.Join("_", config.RequestedSubsystems)
            : "Hrot";
        var fileTarget = new FileTarget("logFile")
        {
            Layout           = "[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] [Node-${scopeproperty:nodeId}] ${message} ${exception:format=tostring}",
            FileName         = Path.Combine(resolvedLogDir, $"{subsystemTag}_{config.NodeId}.log"),
            ArchiveFileName  = Path.Combine(resolvedLogDir, $"{subsystemTag}_{config.NodeId}.{{#}}.log"),
            ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Rolling,
            MaxArchiveFiles  = 10,
            ArchiveAboveSize = 50 * 1024 * 1024,
            KeepFileOpen     = true,
            ConcurrentWrites = false,
        };
        logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
        // Route AI behavior logs to the dedicated UI tab (also captured by file/console rules above).
        logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
        LogManager.Configuration = logConfig;

        // ── CI mode: headless deterministic scenario run ──────────────────
        if (config.RequestedSubsystems.Contains("ci"))
        {
            if (string.IsNullOrWhiteSpace(config.ScenarioName))
            {
                Console.Error.WriteLine("[Runner] --scenario is required for --mode ci.");
                return 1;
            }

            Console.WriteLine($"[Runner] CI mode – scenario={config.ScenarioName}");

            var ciSub       = new CiSubsystem(config.ScenarioName);
            var ciOptions   = new RunnerOptions
            {
                Headless          = true,
                Deterministic     = true,
                FixedDeltaSeconds = 1.0f / 60.0f,
                DomainId          = config.DomainId
            };
            var ciOrchestrator = new SubsystemOrchestrator(new[] { (ISubsystem)ciSub }, ciOptions);
            ciSub.AttachOrchestrator(ciOrchestrator);

            ciOrchestrator.Initialize();
            ciOrchestrator.Run();
            ciOrchestrator.Shutdown();

            // ScenarioSubsystem calls Environment.Exit(code) before reaching here.
            // This return is a safety fallback.
            return 0;
        }

        // ── Migrate mode: run $meta envelope migration on all known JSON files ──────────────────
        if (config.RequestedSubsystems.Contains("migrate"))
        {
            Console.WriteLine("[Runner] Migrate mode -- constructing migration services...");
            var migrationServices = HrotMigrationBootstrap.BuildClusterRunnerMigrate();

            var runner = new Hrot.ClusterRunner.Migration.MigrateMode(
                migrationServices,
                config.InputDirectory,
                config.TargetVersion,
                config.DryRun);

            return await runner.RunAsync();
        }

        // ── Build subsystems from mode ────────────────────────────────────────
        // WM-S703: PerspectiveUpdateSubsystem must be the first subsystem so that
        // perspective transitions enqueued during Render are processed before any
        // other subsystem's Update runs in the next frame.
        var perspSubsystem = new PerspectiveUpdateSubsystem();
        LoadReferencedAssemblies();
        // Discover all non-abstract ISubsystem implementations (runner-internal
        // ones are excluded; see ScanForSubsystems).
        var discovered = ScanForSubsystems()
            .Select(type => 
            {
                // 1. Create isolated memory spaces per subsystem
                var entityMap    = new NetworkEntityMap();
                var geoTransform = HrotEnvironment.CreateGeoTransform();
                var eventBus     = new FdpEventBus();
        
                // 2. Extract a rough name for NodeId resolution (e.g., "SimHostSubsystem" -> "SimHost")
                string subName = type.Name.Replace("Subsystem", "");
                int subNodeId = ResolveAppNodeId(subName, config.NodeId);

                // 3. Create an isolated DDS Participant with a unique Instance ID
                var participant = HrotEnvironment.CreateParticipant(config.DomainId);
                participant?.EnableSenderTracking(new SenderIdentityConfig
                {
                    AppDomainId   = config.DomainId,
                    AppInstanceId = subNodeId
                });

                // 4. Create the dedicated Network Factory
                INetworkFactory networkFactory = string.Equals(config.NetworkProtocol, "bdc", StringComparison.OrdinalIgnoreCase)
                    ? (INetworkFactory)new BdcNetworkFactory(participant, entityMap, geoTransform, eventBus, (long)subNodeId, NodeRole.None)
                    : new NedNetworkFactory(participant, entityMap, geoTransform, eventBus, subNodeId, NodeRole.None);

                // 5. Inject the isolated factory into the subsystem
                return TryCreateSubsystem(type, networkFactory);
            })
            .Where(s => s != null)
            .ToDictionary(s => s!.Name, s => s!, StringComparer.OrdinalIgnoreCase);

        var subsystems = new List<ISubsystem> { perspSubsystem };
        foreach (var name in config.RequestedSubsystems)
        {
            if (!discovered.TryGetValue(name, out var sub))
            {
                Console.Error.WriteLine($"[Runner] Unknown subsystem name: '{name}'. Available: {string.Join(", ", discovered.Keys)}");
                return 1;
            }
            subsystems.Add(sub);
        }

        // ⭐⭐⭐ RULING 67 — the configured authoring root, applied ONCE for EVERY host.
        //    🔒 "we need a config file provided asset path for the CGF as well as the Editor (same
        //       shared code), with fallback to the repo source as of now." (user, 2026-08-14)
        //    ⇒ ⭐ ONE call, before any Initialize(): AssetRoots is the codebase's stated single authority
        //      for roots, so configuring it here reaches every host that asks it a question — ⛔ rather
        //      than each subsystem growing its own notion of where the assets live, which is the third
        //      competing path authority ruling 67 warns against.
        //    ⚠ A configured-but-missing directory THROWS here, at startup, by design.
        Hrot.Editor.AiShared.AssetRoots.Configure(config.AssetRoot);

        // Propagate configurable AI project path to the editor subsystem before Initialize().
        foreach (var sub in subsystems.OfType<Hrot.Editor.EditorSubsystem>())
            sub.AiBehaviorsProjectPath = config.AiBehaviorsProjectPath;

        // ⭐⭐ cgf==editor SLICE 2 (CE-013) — CGF indexes the SAME authoring assets, so it reads the SAME
        //    configured project path. ⛔ Not a second notion of where the assets live: one config value,
        //    two hosts. 📌 A production caller that HAS a dependency must pass it — the loop above already
        //    held it, and CGF was not in it.
        foreach (var sub in subsystems.OfType<Hrot.CGF.CgfSubsystem>())
            sub.AiBehaviorsProjectPath = config.AiBehaviorsProjectPath;

        var options = new RunnerOptions
        {
            Headless       = config.Headless,
            DomainId       = config.DomainId,
            NodeId         = config.NodeId,
            // ⭐ Batch 103 (103a) — the layout reset is a RUNNER option, so the window controller reads
            //   it from the same place it reads the window size. ⛔ Not a static or an env var.
            // ⚠ `?? true` because the option is `bool?` — see HrotRunnerConfiguration.ResetLayout for
            //   why. ⭐ The default lives in ONE place (the [Option] attribute); this is only the
            //   unwrap, and it agrees with it.
            ResetLayoutOnRun = config.ResetLayout ?? true,
            NodeIdResolver = ResolveAppNodeId,
        };

        // ── Create + run orchestrator ─────────────────────────────────────────
        var orchestrator = new SubsystemOrchestrator(subsystems, options);
        Hrot.ClusterRunner.Presentation.LocalWindowController? windowCtrl = null;
        // ⭐ Declared out here so the `finally` below can dispose the host — see the wiring block inside.
        Hrot.Editor.DebugApi.MainThreadJobQueue? clusterApiQueue = null;
        Hrot.Editor.DebugApi.DebugApiHost?       clusterApiHost  = null;
        Hrot.Editor.DebugApi.DebugApiService?    clusterApiService = null;
        try
        {
            orchestrator.Initialize();

            // WM-S703: Wire up PerspectiveCoordinatorSystem now that the orchestrator exists.
            // Maps perspective names to subsystem names used by SwitchMapOwner.
            //
            // ⭐⭐⭐ A9 — CGF's perspective is "Scenario", so THE KEY MOVED AND THE VALUE DID NOT.
            // 📄 DESIGN_Perspective_Unification.md §1b (charter D1: cgf and the editor share one
            //    perspective vocabulary) · §1e (exactly ONE new entry — BTree/HSM/Blueprint own a GRAPH
            //    canvas, not a map, so they are deliberately absent here, exactly as the editor's three
            //    already are). ⛔ There is no "CGF" perspective any more.
            // ⭐ This is also the FIRST entry whose key and value differ — 📌 §1b's one constraint:
            //    a perspective maps to exactly ONE subsystem, so two CO-RUNNING subsystems must never
            //    claim the same name. ⚠ Safe here because the runner refuses to combine editor and cgf.
            //
            // ⭐ A10 — the ["StrideMock"] entry is GONE. 📌 A one-line courtesy to the parallel
            //    StrideMock-cleanup batch: it was the only StrideMock line in this file and that batch is
            //    forbidden to touch this literal, so deleting it here is what keeps the two conflict-free.
            var perspectiveMap = new Dictionary<string, string>
            {
                ["IG"]        = "IG",
                ["SimHost"]   = "SimHost",
                ["ExCon"]     = "ExCon",
                ["Scenario"]  = "CGF",
            };

            // ⭐⭐⭐ GZH-014 — KEYED BY PERSPECTIVE, which is what it is looked up by.
            //
            // 🔴🔴 A9 FINDING, measured 2026-08-23. This dictionary's field doc says "map from perspective
            //    name to IGizmoControllable subsystem" and PerspectiveCoordinatorSystem looks it up with
            //    evt.OldPerspective / evt.NewPerspective — ⛔ but it was BUILT from ISubsystem.Name. That
            //    was invisible only because every perspective happened to be spelled like its subsystem.
            //    ⇒ renaming CGF's perspective to "Scenario" would have left the gizmo listener transfer
            //    silently dead (SwitchMapOwner would still work, since it takes the mapped VALUE), i.e.
            //    exactly the kind of green-but-broken the naming unification exists to remove.
            // ⭐ Derived from perspectiveMap so the two can no longer drift: one map declares the
            //    perspective→subsystem relation, and this resolves it to the instance.
            // 📐 Nothing is lost: the old subsystem-name keys were only ever reachable INSIDE
            //    PerspectiveCoordinatorSystem's `perspectiveMap.TryGetValue(NewPerspective)` guard, so a
            //    key that is not a mapped perspective could never be hit.
            var gizmoByPerspective = new Dictionary<string, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (perspective, subsystemName) in perspectiveMap)
            {
                var owner = subsystems
                    .OfType<Hrot.Common.Diagnostics.Gizmos.IGizmoControllable>()
                    .FirstOrDefault(s => (ISubsystem)s != perspSubsystem
                                      && string.Equals(((ISubsystem)s).Name, subsystemName,
                                                       StringComparison.OrdinalIgnoreCase));
                // ⭐ Absent is normal — the mode simply does not run that subsystem.
                if (owner != null) gizmoByPerspective[perspective] = owner;
            }
            var gizmoControllables = gizmoByPerspective;
            var coordinator = new PerspectiveCoordinatorSystem(orchestrator, perspectiveMap, gizmoControllables);
            perspSubsystem.Coordinator = coordinator;

            var shell = new Hrot.ClusterRunner.Presentation.RaylibPresentationShell();
            windowCtrl = new Hrot.ClusterRunner.Presentation.LocalWindowController(
                shell, subsystems, options, coordinator);

            // ══ THE LIFTED DEBUG API — item ② of the conformance harness ═══════════════════════════════
            //
            // ⭐⭐⭐ 📄 Architect_Question_54 (RESOLVED) · DESIGN_Headless_Testability.md §6a's four wiring
            //    points. 🔒 User: "MCP in 'mode all' should work from the context of the currently selected
            //    perspective to be closest to how the user would control it."
            //
            // ⛔⛔ GATED TWICE, and both guards matter:
            //    ① HROT_DEBUG_API_PORT — costs nothing in a normal run, exactly like the editor's;
            //    ② the EDITOR SUBSYSTEM MUST BE ABSENT — it wires its own host on the same port, so doing
            //       it here too would fight for the listener. ⭐ `--mode editor` keeps its existing path
            //       untouched; this is the CLUSTER path only.
            var clusterApiPort = Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT");
            bool hasEditorSubsystem = subsystems.OfType<Hrot.Editor.EditorSubsystem>().Any();

            if (!string.IsNullOrWhiteSpace(clusterApiPort)
                && int.TryParse(clusterApiPort, out var clusterPort)
                && !hasEditorSubsystem)
            {
                // ① Capture must be ON before any panel draws, or every dump is empty.
                Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.CaptureEnabled = true;

                // ⭐⭐⭐ ①b SEED THE CURATED SCENARIOS INTO THE WORKING NAS (HN-029).
                //
                // 📐 Measured: `POST /scenario/load/live hill-attack` in `--mode all` published its intent, the
                //    master accepted it and fanned out to 5 nodes — then AssetPrefetchProcessManager failed with
                //    "NAS source directory '<staging>/shared/scenarios/hill-attack' does not exist". ⇒ the load
                //    machinery was fine; the scenario simply was not on the NAS.
                //
                // ⭐⭐ REUSE, not a new mechanism: CuratedScenarios is the editor's own start-up seed, and its
                //    own doc already says "the logic is host-agnostic (the working root is a parameter), so CGF
                //    or any other host can call the same helper." 📌 A textbook under-adopted seam.
                //
                // ⛔⛔ GATED ON THE DEBUG PORT ON PURPOSE — not done in a normal cluster run. The seed
                //    force-overwrites the curated NAMES in the operator's working NAS folder, and a developer
                //    running the cluster alongside their own edited scenarios must not have them replaced
                //    underneath. ⚠ A no-op in a deployed build anyway (no source tree to copy from).
                var seeded = Hrot.ScenarioEditor.Services.CuratedScenarios.SeedIntoWorking(
                    System.IO.Path.Combine(
                        Hrot.Orchestrator.ClusterConfiguration.Default.NasBasePath,
                        Fdp.Toolkit.Orchestration.OrchestrationConstants.ScenariosDirectoryName));
                FdpLog<Program>.Info("[Runner] Curated scenarios seeded to the working NAS: [{0}].",
                    string.Join(", ", seeded));

                // ⭐⭐ The providers — one per subsystem that contributes a read+drive surface (Q54-2).
                //    ⚠ Built AFTER orchestrator.Initialize(), because a provider carries the subsystem's
                //      world and its cluster time adapter, and neither exists before Initialize.
                var debugProviders = subsystems
                    .OfType<Hrot.Presentation.DebugApi.IProvidesDebugSurface>()
                    .Select(p => p.CreateDebugProvider())
                    .Where(p => p != null)
                    .Select(p => p!)
                    .ToList();

                // ⭐⭐⭐ THE ACK-GATE, NOW CONFIRMABLE CLUSTER-WIDE (HN-028 — was the conformance batch's one
                //    cross-lane blocker). The gate's truth is MasterSyncController.IsAwaitingStepAcks, and the
                //    only instance is private to OrchestratorSubsystem; it now exposes exactly that one fact as
                //    `bool?` — null meaning "no master on this node".
                //
                // ⚠ Read through a LAMBDA, not a captured value: the master is built in Initialize() and
                //   disposed in Shutdown(), so a latched copy would answer for a controller that no longer
                //   exists. 📌 This is deviation ③ of the conformance batch (a value-captured provider LIES);
                //   the same mistake was already paid for once with time.drive.
                //
                // ⭐ `--mode all` includes the orchestrator, so this resolves; a mode without it passes null and
                //   GET /capabilities honestly reports hasMaster:false.
                var orchestratorSubsystem = subsystems
                    .OfType<Hrot.Orchestrator.OrchestratorSubsystem>()
                    .FirstOrDefault();

                var dispatcher = new Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher(
                    debugProviders,
                    currentPerspective: () => windowCtrl?.WindowManager?.CurrentPerspective ?? string.Empty,
                    acksPending: orchestratorSubsystem is null
                        ? null
                        : () => orchestratorSubsystem.IsAwaitingStepAcks);

                // ② construct + attach + start.
                clusterApiQueue = new Hrot.Editor.DebugApi.MainThreadJobQueue();
                clusterApiHost  = new Hrot.Editor.DebugApi.DebugApiHost(
                    clusterPort, clusterApiQueue, () => orchestrator.Stop(), mode: config.ModeString);
                clusterApiHost.AttachDispatcher(dispatcher);
                // ⭐⭐⭐ MD-001 — the SimHost-node gap. 📄 DESIGN_Mcp_Diagnostics_Federation §2.1.
                // ⛔⛔ This line built the service with NO log sinks, so `GET /logs` answered `[]` on every
                //    cluster-limited node — a SimHost node could not report its own logs, which is the
                //    whole point of each node hosting its own MCP endpoint (§1).
                // ⚠ A Func: the window manager and its MessageLogRegistry may not exist yet (headless
                //   nodes never build one at all), and the helper still answers with the process-wide
                //   NLog targets that Program.Main installs for EVERY mode.
                // ⭐⭐⭐ CE-169 — the SECOND silent default at this exact call site (logSinks was the
                //    first, see the note above). ⛔⛔ With no registry, `GET /behaviors` answered
                //    "Behavior registry not available." and `GET /entities/{id}/state` omitted the
                //    behaviour NAME on every cluster node — while that same node was resolving the
                //    hash to RUN the behaviour. An instrument that cannot tell "absent" from
                //    "unwired" reads as evidence of absence, which is exactly how it misled a
                //    diagnosis. 📌 The rule this breaks: a production caller that HAS a dependency
                //    must PASS it — and this one does have it, via `subsystems`.
                // ⚠ A Func, for the same reason logSinks is one: CGF builds its registry in the
                //   `behavior-registry` boot step, and `orchestrator.Run()` is ~240 lines BELOW this
                //   point. A captured value would be null forever.
                // ⭐ A node with no CGF genuinely has no registry; the Func returns null and the route
                //   says so truthfully rather than fabricating an empty one (the CE-110 shape).
                var behaviorRegistryGetter =
                    () => subsystems.OfType<Hrot.CGF.CgfSubsystem>()
                                    .Select(s => s.BehaviorRegistry)
                                    .FirstOrDefault(r => r is not null);

                clusterApiService = new Hrot.Editor.DebugApi.DebugApiService(
                    dispatcher,
                    logSinks: () => Fdp.Core.Logging.MessageLogSinks.ForDiagnostics(
                        windowCtrl?.WindowManager?.MessageLogRegistry),
                    behaviorRegistry: behaviorRegistryGetter);
                clusterApiHost.AttachService(clusterApiService);
                clusterApiHost.Start();

                FdpLog<Program>.Info(
                    "[Runner] Debug API listening on {0} — mode={1}, providers=[{2}], perspectives=[{3}].",
                    clusterPort, config.ModeString,
                    string.Join(", ", debugProviders.Select(p => p.SubsystemName)),
                    string.Join(", ", dispatcher.RoutablePerspectives));
            }
            else if (!string.IsNullOrWhiteSpace(clusterApiPort) && hasEditorSubsystem)
            {
                // ⭐ Not a warning: the editor OWNS the API in its own mode. Said out loud so nobody hunts
                //   for a second host that deliberately does not exist.
                FdpLog<Program>.Info("[Runner] Debug API left to EditorSubsystem (mode includes the editor).");
            }

            if (!config.Headless)
                windowCtrl.OpenLocalWindow();

            // ⭐⭐⭐ PERSPECTIVE ACCESS — attached HERE because this is the first moment the WindowManager
            //    exists. 📄 DESIGN_Regression_Net.md §7 N0's as-built said the same thing about the editor:
            //    "the dependency arrives late, and that is forced".
            // ⭐ Reusing the EXISTING WindowManagerPerspectiveSwitcher (Hrot.Editor.AiShared) — ⛔ not a
            //   second switcher: it already wraps GetPerspectives/CurrentPerspective/SwitchPerspective, and
            //   a cluster copy would be the duplicate ruling 9 forbids.
            // ⚠ Without this, GET /perspectives answers 503 "not wired" — which is honest, but it would make
            //   the conformance suite unable to reach 3 of 4 perspectives' panels.
            if (clusterApiService is not null && windowCtrl.WindowManager is not null)
            {
                clusterApiService.AttachPerspectives(
                    new Hrot.Editor.AiShared.Documents.WindowManagerPerspectiveSwitcher(windowCtrl.WindowManager));
                FdpLog<Program>.Info("[Runner] Debug API perspective access attached.");

                // ⭐⭐⭐ cgf==editor SLICE 2 (CE-014) — the AI-ASSET shell, attached on the next line for
                //    the same reason the perspectives are: this is the first moment both halves exist.
                // ⛔⛔ It has to happen HERE and nowhere else: Hrot.CGF cannot reference Hrot.Editor
                //    (DebugApiService's home) because Hrot.Editor already references Hrot.CGF ⇒ this
                //    composition root is the ONLY place that can see both. 📌 The same reference-wall
                //    argument EditorSubsystem makes for the HSM details view.
                // ⚠ Absent on a mode without CGF, and absent on a headless one — GET /assets then
                //   answers 503 with the wiring explanation, ⛔ not an empty list that would read as
                //   "this host has no assets".
                var cgfShell = subsystems.OfType<Hrot.CGF.CgfSubsystem>().FirstOrDefault();
                if (cgfShell?.AssetShellCatalog is { } shellCatalog
                 && cgfShell.AssetShellDocuments is { } shellDocs
                 && cgfShell.AssetShellWindows   is { } shellWindows)
                {
                    clusterApiService.AttachAssetShell(shellCatalog, shellDocs, shellWindows);
                    FdpLog<Program>.Info(
                        "[Runner] Debug API asset shell attached — {0} asset(s) indexed on CGF.",
                        shellCatalog.All.Count);

                    // ⭐⭐ cgf==editor SLICE 3 (CE-021) — the save/reload actions, on the next line and
                    //    for the same reference-wall reason as the shell above.
                    if (cgfShell.AssetShellSave is { } save && cgfShell.AssetShellReload is { } reload)
                    {
                        clusterApiService.AttachAssetEditing(save, reload);
                        FdpLog<Program>.Info("[Runner] Debug API asset save/reload attached.");
                    }

                    // ⭐⭐⭐ AQ57 / MA-019 — CREATE, attached here for the third time for the same
                    //    reference-wall reason: this is the only composition root that sees both halves.
                    // ⛔ Not "CGF gets its own create": it is the SAME per-kind INewAssetService contract
                    //   the editor's New-Asset dialog runs, composed at CGF's root (Q57-A1).
                    if (cgfShell.AssetShellCreate is { } create)
                    {
                        clusterApiService.AttachAssetAuthoring(
                            (kind, name, relPath, recipe) => create(kind, name, relPath, recipe));
                        FdpLog<Program>.Info("[Runner] Debug API asset creation attached.");
                    }

                    // ⭐⭐ MA-020 — recipe discovery reads the SAME registry create does. ⚠ If one is
                    //    attached and the other is not, GET /assets/recipes would list templates that
                    //    POST /assets could not build — so they are wired from the one source, together.
                    if (cgfShell.AssetShellNewAssetServices is { } newAssetServices)
                    {
                        clusterApiService.AttachRecipes(
                            newAssetServices,
                            Hrot.Blueprints.Editor.RecipeMetadataAdapter.DescribeRecipe,
                            Hrot.Blueprints.Editor.RecipeMetadataAdapter.RecipeCategory);
                        FdpLog<Program>.Info(
                            "[Runner] Debug API recipe discovery attached — kinds [{0}].",
                            string.Join(", ", newAssetServices.Keys));
                    }

                    // ⭐⭐⭐ MD-008 — ⛔ NO `AttachEditorCommands` CALL HERE, and that is DELIBERATE.
                    // 📐 Measured `2026-08-26`: a CGF node already answers `GET /editor/commands` with 68
                    //    commands. `DebugApiService.ResolveEditorCommands` falls back to
                    //    `_documents.Active -> ContextOf(...).Commands`, and `_documents` is attached by
                    //    `AttachAssetShell` two lines above. ⇒ ⭐ the explicit attach would compute the
                    //    SAME expression from the SAME object — a duplicate wiring, not a missing one.
                    // ⚠ Proven by `The_editor_command_bus_answers_on_a_non_editor_node`, which is green
                    //   on this host with nothing attached. ⛔ Do not "fix" this by adding the call.

                    // ⭐ MA-022 — the action-schema exporter, so get_node_kind_schema reports real DTO
                    //   fields here instead of `paramsSource: none:no-exporter-wired`.
                    if (cgfShell.AssetShellSchemaExporter is { } schemaExporter)
                    {
                        clusterApiService.AttachSchemaExporter(schemaExporter);
                        FdpLog<Program>.Info("[Runner] Debug API action-schema exporter attached.");
                    }
                }
                else
                {
                    FdpLog<Program>.Info(
                        "[Runner] No CGF asset shell in this mode — /assets and /documents answer 503.");
                }
            }

            using var consoleSvc = new ConsoleCommandService();
            consoleSvc.RegisterCommand("open",  "Open the local Raylib window",
                _ => windowCtrl.OpenLocalWindow());
            consoleSvc.RegisterCommand("close", "Close the local Raylib window",
                _ => windowCtrl.CloseLocalWindow());
            consoleSvc.OnCommandDispatched += orchestrator.EnqueueConsoleAction;
            consoleSvc.Start();

            if (windowCtrl.IsLocalWindowOpen)
            {
                // 4. The proper non-headless Render Loop.
                // App-exit guards (e.g. the editor's unsaved-changes prompt) may DEFER a window-close.
                var exitGuards = subsystems
                    .OfType<Fdp.Toolkit.Runner.IAppExitGuard>()
                    .ToList();
                bool exiting = false;

                // Remote-desktop clicks (TeamViewer / Parsec / RDP) inject WM_*BUTTONDOWN and
                // WM_*BUTTONUP microseconds apart, so both land in one glfwPollEvents() drain and
                // the polled button state ends where it started -- the press is never observed and
                // the click is silently lost. The latch watches the raw messages and replays a lost
                // click held across frames, so raylib sees an ordinary slow click. Inert for local
                // input; kill switch: HROT_DISABLE_CLICK_LATCH=1.
                // Windows-only compensation; a no-op elsewhere (Linux needs nothing here).
                using var clickLatch = Fdp.Presentation.Input.ClickLatch.Create();

                while (!exiting)
                {
                    // ③ + ④ — THE FRAME BOUNDARY. 🔴 ORDER MATTERS (HN-007): the API's jobs drain FIRST,
                    //    serving the PREVIOUS frame's complete captures, and only then is the captured set
                    //    cleared for this frame. ⛔ Clearing first is the defect that made every out-of-band
                    //    panel read return an empty set in the editor, and it would do the same here.
                    if (clusterApiQueue is not null)
                    {
                        clusterApiQueue.DrainAll();
                        Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.ClearCaptured();
                    }

                    // Before input is polled: replay anything the previous frame dropped.
                    clickLatch.Tick(
                        Raylib_cs.Raylib.IsMouseButtonDown(Raylib_cs.MouseButton.Left),
                        Raylib_cs.Raylib.IsMouseButtonDown(Raylib_cs.MouseButton.Right),
                        Raylib_cs.Raylib.IsMouseButtonDown(Raylib_cs.MouseButton.Middle));

                    orchestrator.DrainConsoleActions();

                    // A subsystem (or a console action) asked the orchestrator to stop — e.g. the
                    // editor's POST /shutdown. This loop is not orchestrator.Run(), so it has to
                    // honour that request itself; breaking here still reaches the finally below,
                    // which shuts every subsystem down in order.
                    if (!orchestrator.IsRunning)
                        break;

                    float dt = Raylib_cs.Raylib.GetFrameTime();

                    // WindowShouldClose() is edge-triggered (it reads AND resets the GLFW close flag),
                    // so this fires once on the frame the window [X] is clicked. Ask every guard: if any
                    // DEFERS (unsaved work → it opens its own modal), keep running; otherwise exit now.
                    if (Raylib_cs.Raylib.WindowShouldClose())
                    {
                        bool deferred = false;
                        foreach (var guard in exitGuards)
                            if (guard.OnExitRequested() == Fdp.Toolkit.Runner.ExitDisposition.Deferred)
                                deferred = true;
                        if (!deferred)
                            break; // no unsaved work → exit immediately (skip this frame's render)
                    }

                    orchestrator.Update(dt);

                    // Apply any queued font/DPI rebake at the frame boundary (atlas rebuild
                    // must happen outside rlImGui.Begin/End).
                    shell.FontService.ApplyPendingRebuild();

                    Raylib_cs.Raylib.BeginDrawing();
                    // Restore the black background
                    Raylib_cs.Raylib.ClearBackground(Raylib_cs.Color.Black);

                    orchestrator.DrawWorldAll();

                    rlImGui_cs.rlImGui.Begin();

                    // --- DOCKSPACE SETUP (§4.1.2: inset bottom by status bar) ---
                    // BATCH-25: Toolbar now lives inside the main menu bar (which ImGui already
                    // excludes from the viewport work area), so the toolbar inset is 0.
                    var viewport = ImGuiNET.ImGui.GetMainViewport();
                    float toolbarHeight = 0f;
                    float statusBarHeight = windowCtrl.WindowManager?.StatusBar.Height ?? 0f;

                    ImGuiNET.ImGui.SetNextWindowPos(DockspaceLayout.CentralPos(viewport.WorkPos, toolbarHeight));
                    ImGuiNET.ImGui.SetNextWindowSize(DockspaceLayout.CentralSize(
                        viewport.WorkSize.X, viewport.WorkSize.Y, toolbarHeight, statusBarHeight));
                    ImGuiNET.ImGui.SetNextWindowViewport(viewport.ID);
                    ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowRounding, 0f);
                    ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowBorderSize, 0f);
                    ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.WindowBg, System.Numerics.Vector4.Zero);

                    var dockspaceFlags = ImGuiNET.ImGuiWindowFlags.NoDocking
                        | ImGuiNET.ImGuiWindowFlags.NoTitleBar | ImGuiNET.ImGuiWindowFlags.NoCollapse
                        | ImGuiNET.ImGuiWindowFlags.NoResize | ImGuiNET.ImGuiWindowFlags.NoMove
                        | ImGuiNET.ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiNET.ImGuiWindowFlags.NoNavFocus
                        | ImGuiNET.ImGuiWindowFlags.NoBackground
                        // Canonical dockspace-host flags: without these the mouse-wheel (used to zoom the
                        // graph canvas) scrolls this passthrough host by its content overflow (~the main
                        // menu-bar height), making the whole content area jump on every zoom.
                        | ImGuiNET.ImGuiWindowFlags.NoScrollbar | ImGuiNET.ImGuiWindowFlags.NoScrollWithMouse;

                    ImGuiNET.ImGui.Begin("##DockSpace", dockspaceFlags);
                    ImGuiNET.ImGui.PopStyleColor();
                    ImGuiNET.ImGui.PopStyleVar(2);

                    ImGuiNET.ImGui.DockSpace(ImGuiNET.ImGui.GetID("MainDockSpace"),
                        DockspaceLayout.CentralSize(viewport.WorkSize.X, viewport.WorkSize.Y, toolbarHeight, statusBarHeight),
                        ImGuiNET.ImGuiDockNodeFlags.PassthruCentralNode);
                    ImGuiNET.ImGui.End();
                    // --------------------------------

                    windowCtrl.WindowManager!.Render();
                    orchestrator.DrawUIAll();
                    rlImGui_cs.rlImGui.End();

                    Raylib_cs.Raylib.EndDrawing();

                    // A guard's modal approved exit this frame (Save All & Exit / Discard & Exit).
                    foreach (var guard in exitGuards)
                        if (guard.ExitApproved)
                            exiting = true;
                }

            }
            else
            {
                // Fallback to the internal headless simulation loop for CI/background tasks
                orchestrator.Run();
            }
        }
        finally
        {
            clusterApiHost?.Dispose();
            orchestrator.Shutdown();
            if (windowCtrl?.IsLocalWindowOpen == true)
                windowCtrl.CloseLocalWindow();
        }

        Console.WriteLine("[Runner] Shutdown complete.");
        return 0;
    }

    /// <summary>
    /// Application-layer node-ID resolver.  Maps each concrete subsystem name to a
    /// deterministic, pairwise-unique offset added to the base node ID so every
    /// subsystem in a multi-process cluster receives a distinct ID.
    /// Returns 0 (legacy fallback) when <paramref name="baseNodeId"/> is 0.
    /// </summary>
    private static int ResolveAppNodeId(string subsystemName, int baseNodeId)
    {
        int offset = subsystemName.ToUpper() switch
        {
            "SIMHOST"      => 1,
            "IG"           => 100,
            "EXCON"        => 200,
            "ORCHESTRATOR" => 300,
            "CGF"          => 400,
            "CI"           => 500,
            // ST-015: the "STRIDEMOCK" => 700 arm went with the subsystem. Not in the dispatch's
            // table, which recorded Program.cs:256 as the only StrideMock line in this file.
            _              => 600,
        };
        return baseNodeId + offset;
    }

    /// <summary>
    /// Eagerly loads all Hrot.* and Fdp.* assemblies found in the deployment directory so
    /// that plugin subsystems not statically referenced by any compiled code are still
    /// visible to the reflection-based <see cref="ScanForSubsystems"/> pass.
    /// </summary>
    private static void LoadReferencedAssemblies()
    {
        // Scan the physical deployment directory instead of walking IL metadata.
        // The C# compiler drops purely-dynamic <ProjectReference> links from the IL,
        // so Assembly.GetReferencedAssemblies() misses assemblies like Hrot.CGF.dll
        // when no type from them is statically used in ClusterRunner source.
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dllFiles = System.IO.Directory.GetFiles(basePath, "*.dll");

        var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name!), StringComparer.OrdinalIgnoreCase);

        foreach (var file in dllFiles)
        {
            var assemblyName = System.IO.Path.GetFileNameWithoutExtension(file);

            // Filter to our own domain boundaries to avoid eagerly loading
            // hundreds of system/third-party DLLs.
            if (!assemblyName.StartsWith("Hrot.") && !assemblyName.StartsWith("Fdp."))
                continue;

            // ARCHITECTURE: Do not lock the AI behaviors assembly in the Default ALC.
            // FbtAssemblyHotReloader loads Hrot.AI.Behaviors exclusively into a
            // collectible ALC so it can be unloaded and reloaded at runtime.
            if (assemblyName.Equals("Hrot.AI.Behaviors", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!loaded.Contains(assemblyName))
            {
                try
                {
                    // Use Load(AssemblyName) rather than LoadFrom to ensure the
                    // plugin is loaded into the default AssemblyLoadContext.
                    System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(assemblyName));
                    loaded.Add(assemblyName);
                }
                catch { /* ignore assemblies that cannot be loaded */ }
            }
        }
    }

    /// <summary>
    /// Scans all loaded assemblies for non-abstract ISubsystem implementations
    /// (excluding PerspectiveUpdateSubsystem, EyesAndMuscleSubsystem, and CiSubsystem
    /// which are runner-internal or handled separately).
    /// </summary>
    private static IEnumerable<Type> ScanForSubsystems()
    {
        var subsystemType = typeof(ISubsystem);
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => t.IsClass && !t.IsAbstract
                     && subsystemType.IsAssignableFrom(t)
                     && t != typeof(PerspectiveUpdateSubsystem)
                     && t != typeof(EyesAndMuscleSubsystem)
                     && t != typeof(CiSubsystem));
    }

    /// <summary>
    /// Attempts to instantiate an <see cref="ISubsystem"/> from its <see cref="Type"/>.
    /// Requires a constructor accepting <see cref="INetworkFactory"/>.
    /// Returns <c>null</c> when no suitable constructor is found.
    /// </summary>
    private static ISubsystem? TryCreateSubsystem(Type type, INetworkFactory networkFactory)
    {
        // Prefer constructor that accepts INetworkFactory.
        var factoryCtor = type.GetConstructor(new[] { typeof(INetworkFactory) });
        if (factoryCtor != null)
            return (ISubsystem)factoryCtor.Invoke(new object[] { networkFactory });


        return null;
    }
}

