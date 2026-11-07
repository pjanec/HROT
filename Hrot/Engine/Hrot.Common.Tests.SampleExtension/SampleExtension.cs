using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Infrastructure;

namespace SampleNodeExtension;

/// <summary>The extension under test: one capability, one system in the simulation group, one feature (F-2).</summary>
public sealed class SampleExtension : INodeExtension
{
    public const string CapabilityKey = "sample.extension";
    public const string FeatureId = "sample.extension.feature";

    /// <summary>Counts executions of <see cref="SampleSystem"/> (the tests read it through reflection, the assembly is loaded by path).</summary>
    public static int Executions;

    /// <summary>Counts executions of <see cref="SampleGatedSystem"/>, which the capability's MODULE registers under <c>[NodeFeature]</c>.</summary>
    public static int GatedExecutions;

    /// <summary>The last context the extension was asked with.</summary>
    public static NodeExtensionContext? LastContext;

    public string Name => "Sample";

    public INodeCapability? CreateCapability(NodeExtensionContext context)
    {
        LastContext = context;
        return new SampleCapability();
    }
}

public sealed class SampleCapability : INodeCapability
{
    public string Key => SampleExtension.CapabilityKey;
    public IReadOnlyList<string> Needs => Array.Empty<string>();
    public IReadOnlyList<FeatureDeclaration> Features { get; } = new[] { FeatureDeclaration.Switch(SampleExtension.FeatureId, "Test extension feature", FeatureChange.Live, _ => true) };

    public void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input, List<IEcsModuleSystem> simulation, List<IEcsModuleSystem> postSimulation)
        => simulation.Add(new SampleSystem());

    // A module (not a group member): the registration path on which the scheduler reads [NodeFeature] and F-13 gating holds.
    public IEnumerable<IEcsModule> ProvideModules() { yield return new SampleModule(); }
}

[TickMode(TickPolicy.EveryFrame)]   // every system declares its tick mode (CE-3293, R-3036): an extension author meets this rule too
public sealed class SampleSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime) => Interlocked.Increment(ref SampleExtension.Executions);
}

[TickMode(TickPolicy.EveryFrame)]
[NodeFeature(SampleExtension.FeatureId)]
public sealed class SampleModule : IEcsModule
{
    public string Name => "SampleNodeExtensionModule";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
    public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(new SampleGatedSystem());
    public void Tick(ISimulationView view, float deltaTime) { }
}

[TickMode(TickPolicy.EveryFrame)]
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class SampleGatedSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime) => Interlocked.Increment(ref SampleExtension.GatedExecutions);
}
