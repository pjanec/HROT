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

        // Propagate configurable AI project path to the editor subsystem before Initialize().
        foreach (var sub in subsystems.OfType<Hrot.Editor.EditorSubsystem>())
            sub.AiBehaviorsProjectPath = config.AiBehaviorsProjectPath;

        var options = new RunnerOptions
        {
            Headless       = config.Headless,
            DomainId       = config.DomainId,
            NodeId         = config.NodeId,
            NodeIdResolver = ResolveAppNodeId,
        };

        // ── Create + run orchestrator ─────────────────────────────────────────
        var orchestrator = new SubsystemOrchestrator(subsystems, options);
        Hrot.ClusterRunner.Presentation.LocalWindowController? windowCtrl = null;
        try
        {
            orchestrator.Initialize();

            // WM-S703: Wire up PerspectiveCoordinatorSystem now that the orchestrator exists.
            // Maps perspective names to subsystem names used by SwitchMapOwner.
            var perspectiveMap = new Dictionary<string, string>
            {
                ["IG"]        = "IG",
                ["SimHost"]   = "SimHost",
                ["ExCon"]     = "ExCon",
                ["CGF"]       = "CGF",
                ["StrideMock"] = "StrideMock",
            };
            // GZH-014: build gizmo-controllable map keyed by perspective name.
            var gizmoControllables = subsystems
                .OfType<Hrot.Common.Diagnostics.Gizmos.IGizmoControllable>()
                .Where(s => (ISubsystem)s != perspSubsystem)
                .ToDictionary(
                    s => ((ISubsystem)s).Name,
                    s => s,
                    StringComparer.OrdinalIgnoreCase);
            var coordinator = new PerspectiveCoordinatorSystem(orchestrator, perspectiveMap, gizmoControllables);
            perspSubsystem.Coordinator = coordinator;

            var shell = new Hrot.ClusterRunner.Presentation.RaylibPresentationShell();
            windowCtrl = new Hrot.ClusterRunner.Presentation.LocalWindowController(
                shell, subsystems, options, coordinator);

            if (!config.Headless)
                windowCtrl.OpenLocalWindow();

            using var consoleSvc = new ConsoleCommandService();
            consoleSvc.RegisterCommand("open",  "Open the local Raylib window",
                _ => windowCtrl.OpenLocalWindow());
            consoleSvc.RegisterCommand("close", "Close the local Raylib window",
                _ => windowCtrl.CloseLocalWindow());
            consoleSvc.OnCommandDispatched += orchestrator.EnqueueConsoleAction;
            consoleSvc.Start();

            if (windowCtrl.IsLocalWindowOpen)
            {
                // 4. The proper non-headless Render Loop
                while (!Raylib_cs.Raylib.WindowShouldClose())
                {
                    orchestrator.DrainConsoleActions();
                    float dt = Raylib_cs.Raylib.GetFrameTime();

                    orchestrator.Update(dt);

                    Raylib_cs.Raylib.BeginDrawing();
                    // Restore the black background
                    Raylib_cs.Raylib.ClearBackground(Raylib_cs.Color.Black);

                    orchestrator.DrawWorldAll();

                    rlImGui_cs.rlImGui.Begin();

                    // --- RESTORED DOCKSPACE SETUP ---
                    var viewport = ImGuiNET.ImGui.GetMainViewport();
                    ImGuiNET.ImGui.SetNextWindowPos(viewport.WorkPos);
                    ImGuiNET.ImGui.SetNextWindowSize(viewport.WorkSize);
                    ImGuiNET.ImGui.SetNextWindowViewport(viewport.ID);
                    ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowRounding, 0f);
                    ImGuiNET.ImGui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowBorderSize, 0f);
                    ImGuiNET.ImGui.PushStyleColor(ImGuiNET.ImGuiCol.WindowBg, System.Numerics.Vector4.Zero);

                    var dockspaceFlags = ImGuiNET.ImGuiWindowFlags.NoDocking
                        | ImGuiNET.ImGuiWindowFlags.NoTitleBar | ImGuiNET.ImGuiWindowFlags.NoCollapse
                        | ImGuiNET.ImGuiWindowFlags.NoResize | ImGuiNET.ImGuiWindowFlags.NoMove
                        | ImGuiNET.ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiNET.ImGuiWindowFlags.NoNavFocus
                        | ImGuiNET.ImGuiWindowFlags.NoBackground;

                    ImGuiNET.ImGui.Begin("##DockSpace", dockspaceFlags);
                    ImGuiNET.ImGui.PopStyleColor();
                    ImGuiNET.ImGui.PopStyleVar(2);

                    // Reduce dockspace height to leave room for the status bar
                    float statusBarHeight = windowCtrl.WindowManager?.StatusBar.Height ?? 0f;
                    var dockspaceSize = statusBarHeight > 0f
                        ? new System.Numerics.Vector2(viewport.WorkSize.X, viewport.WorkSize.Y - statusBarHeight)
                        : System.Numerics.Vector2.Zero;

                    ImGuiNET.ImGui.DockSpace(ImGuiNET.ImGui.GetID("MainDockSpace"), dockspaceSize, ImGuiNET.ImGuiDockNodeFlags.PassthruCentralNode);
                    ImGuiNET.ImGui.End();
                    // --------------------------------

                    windowCtrl.WindowManager!.Render();
                    orchestrator.DrawUIAll();
                    rlImGui_cs.rlImGui.End();

                    Raylib_cs.Raylib.EndDrawing();
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
            "STRIDEMOCK"   => 700,
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

