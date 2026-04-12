using CommandLine;
using Hrot.Common;
using Hrot.Map.Common;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Services;
using Hrot.ClusterRunner.Scenarios;
using Hrot.ClusterRunner.Systems;
using Fdp.Kernel;
using Hrot.Core.Network;
using Hrot.Network.NED.Factory;
using Hrot.BDC.Factory;
using CycloneDDS.Runtime;
using NLog;
using NLog.Config;
using NLog.Targets;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

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

        // ── Build network factory (used to inject into subsystems that accept it) ─────
        var entityMap    = new NetworkEntityMap();
        var geoTransform = HrotEnvironment.CreateGeoTransform();
        var eventBus     = new FdpEventBus();
        int factoryNodeId = ResolveAppNodeId("Runner", config.NodeId);
        INetworkFactory networkFactory = string.Equals(config.NetworkProtocol, "bdc", StringComparison.OrdinalIgnoreCase)
            ? (INetworkFactory)new BdcNetworkFactory(null, entityMap, geoTransform, eventBus, (long)factoryNodeId, NodeRole.None)
            : new NedNetworkFactory(null, entityMap, geoTransform, eventBus, factoryNodeId, NodeRole.None);

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
            .Select(t => TryCreateSubsystem(t, networkFactory))
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

        // ── Create + run orchestrator ─────────────────────────────────────────
        var orchestrator = new SubsystemOrchestrator(subsystems, options);
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

            orchestrator.Run();
        }
        finally
        {
            orchestrator.Shutdown();
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
        if (baseNodeId == 0) return 0;
        int offset = subsystemName switch
        {
            "SimHost"      => 0,
            "IG"           => 100,
            "ExCon"        => 200,
            "Orchestrator" => 300,
            "CGF"          => 400,
            "CI"           => 500,
            _              => 600,
        };
        return baseNodeId + offset;
    }

    /// <summary>
    /// Eagerly loads all statically-referenced assemblies that are not yet loaded
    /// in the current AppDomain, so that they are visible in the reflection scan.
    /// </summary>
    private static void LoadReferencedAssemblies()
    {
        var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name!), StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<System.Reflection.Assembly>(AppDomain.CurrentDomain.GetAssemblies());
        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            foreach (var refName in asm.GetReferencedAssemblies())
            {
                if (loaded.Contains(refName.Name!)) continue;
                try
                {
                    var loaded2 = System.Reflection.Assembly.Load(refName);
                    loaded.Add(refName.Name!);
                    queue.Enqueue(loaded2);
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
    /// Tries a constructor accepting <see cref="INetworkFactory"/> first, then falls back
    /// to a constructor where all parameters have default values (e.g. parameterless).
    /// Returns <c>null</c> when no suitable constructor is found.
    /// </summary>
    private static ISubsystem? TryCreateSubsystem(Type type, INetworkFactory networkFactory)
    {
        // Prefer constructor that accepts INetworkFactory.
        var factoryCtor = type.GetConstructor(new[] { typeof(INetworkFactory) });
        if (factoryCtor != null)
            return (ISubsystem)factoryCtor.Invoke(new object[] { networkFactory });

        // Fall back to a constructor where all parameters have default values.
        var ctor = type.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().All(p => p.HasDefaultValue));
        if (ctor != null)
            return (ISubsystem)ctor.Invoke(ctor.GetParameters().Select(p => p.DefaultValue).ToArray());

        return null;
    }
}

