using CommandLine;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Hrot.BDC.Factory;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Scenarios;
using Hrot.ClusterRunner.Services;
using Hrot.ClusterRunner.Systems;
using Hrot.Common;
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
    static int Main(string[] args)
    {
        // Enable NLog Console Output globally for FdpLog<T>
        var logConfig = new LoggingConfiguration();
        var logConsole = new ColoredConsoleTarget("logConsole")
        {
            Layout = "${time} | ${level:uppercase=true:padding=-5} | ${logger:shortName=true} | ${message}${exception:format=tostring}"
        };
        logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, logConsole);
        LogManager.Configuration = logConfig;

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

        var options = new RunnerOptions
        {
            Headless       = config.Headless,
            DomainId       = config.DomainId,
            NodeId         = config.NodeId,
            NodeIdResolver = ResolveAppNodeId,
        };

        if (!config.Headless)
        {
            Raylib_cs.Raylib.SetConfigFlags(Raylib_cs.ConfigFlags.ResizableWindow | Raylib_cs.ConfigFlags.Msaa4xHint);
            Raylib_cs.Raylib.InitWindow(options.WindowWidth, options.WindowHeight, "HROT Cluster Runner");
            Raylib_cs.Raylib.SetTargetFPS(options.TargetFps);
            rlImGui_cs.rlImGui.Setup(true); 
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        }
        
        // ── Create + run orchestrator ─────────────────────────────────────────
        var orchestrator = new SubsystemOrchestrator(subsystems, options);
        Raylib_cs.Texture2D atlasTexture = default;
        Fdp.Presentation.WindowManager.WindowManager windowManager = null;
        try
        {
            orchestrator.Initialize();

            // WM-S703: Wire up PerspectiveCoordinatorSystem now that the orchestrator exists.
            // Maps perspective names to subsystem names used by SwitchMapOwner.
            var perspectiveMap = new Dictionary<string, string>
            {
                ["IG"]      = "IG",
                ["SimHost"] = "SimHost",
                ["ExCon"]   = "ExCon",
                ["CGF"]     = "CGF",
            };
            var coordinator = new PerspectiveCoordinatorSystem(orchestrator, perspectiveMap);
            perspSubsystem.Coordinator = coordinator;


            if (!config.Headless)
            {
                // 1. Load the embedded UI icon atlas to the GPU
                byte[] pngBytes = Fdp.Presentation.Icons.EmbeddedAtlasResources.GetSilkAtlasPngBytes();
                var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", pngBytes);
                atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(img);
                Raylib_cs.Raylib.UnloadImage(img); // Clean up CPU-side image buffer

                // 2. Inject the atlas into the Window Manager
                var atlas = new Fdp.Presentation.Icons.IconAtlas((nint)atlasTexture.Id, atlasTexture.Width, atlasTexture.Height, 16f);
                windowManager = new Fdp.Presentation.WindowManager.WindowManager(atlas);

                // 3. Register all GUI panels to the Window Manager
                foreach (var sub in subsystems)
                {
                    if (sub is IWindowRegistrar registrar)
                        registrar.RegisterWindows(windowManager);
                }

                windowManager.OnPerspectiveChanged += (oldPersp, newPersp) =>
                {
                    coordinator.Enqueue(new TogglePerspectiveEvent(oldPersp, newPersp));
                    Console.WriteLine($"[Runner] Perspective changed: {oldPersp} → {newPersp}");
                };

                // WM-S603: Reference status bar section — shows system state to operators.
                windowManager.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
                {
                    ImGuiNET.ImGui.Text("System OK");
                });

                // Load persisted settings and get the last active perspective.
                string? persistedPerspective = windowManager.LoadSettings();

                // Identify the first user-facing subsystem (skip PerspectiveUpdateSubsystem).
                var firstUserSubsystem = subsystems
                    .Skip(1)  // skip PerspectiveUpdateSubsystem which is always index 0
                    .FirstOrDefault();
                string defaultPerspective = firstUserSubsystem?.Name ?? "Default";

                // Apply the valid persisted perspective or fall back to the first available one.
                bool isValidPersisted = !string.IsNullOrEmpty(persistedPerspective)
                    && subsystems.Any(s => s.Name == persistedPerspective);

                windowManager.SwitchPerspective(isValidPersisted ? persistedPerspective! : defaultPerspective);


                // 4. The proper non-headless Render Loop
                while (!Raylib_cs.Raylib.WindowShouldClose())
                {
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
                    float statusBarHeight = windowManager?.StatusBar.Height ?? 0f;
                    var dockspaceSize = statusBarHeight > 0f
                        ? new System.Numerics.Vector2(viewport.WorkSize.X, viewport.WorkSize.Y - statusBarHeight)
                        : System.Numerics.Vector2.Zero;

                    ImGuiNET.ImGui.DockSpace(ImGuiNET.ImGui.GetID("MainDockSpace"), dockspaceSize, ImGuiNET.ImGuiDockNodeFlags.PassthruCentralNode);
                    ImGuiNET.ImGui.End();
                    // --------------------------------

                    windowManager.Render();
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

            if (!config.Headless)
            {
                // ADD THIS: Persist window layouts and the active perspective before tearing down ImGui
                windowManager?.SaveSettings();

                rlImGui_cs.rlImGui.Shutdown();
                
                // Clean up the GPU texture we allocated for the IconAtlas
                if (atlasTexture.Id != 0)
                {
                    Raylib_cs.Raylib.UnloadTexture(atlasTexture);
                }

                Raylib_cs.Raylib.CloseWindow();
            }
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

