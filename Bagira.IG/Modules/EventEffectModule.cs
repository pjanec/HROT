using Bagira.IG.Systems;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Modules;

/// <summary>
/// Module wrapper that registers both <see cref="EventToEffectSystem"/> and
/// <see cref="VisualEffectCleanupSystem"/> in the <see cref="ModuleHostKernel"/>
/// scheduler.
///
/// Both systems are registered so the kernel ticks them in phase order:
/// <list type="number">
///   <item>
///     <see cref="EventToEffectSystem"/> — <c>Simulation</c> phase — spawns effect
///     entities from <see cref="FireInteractionEvent"/> events.
///   </item>
///   <item>
///     <see cref="VisualEffectCleanupSystem"/> — <c>PostSimulation</c> phase —
///     advances <c>ElapsedTime</c> and destroys expired effects.
///   </item>
/// </list>
/// </summary>
public class EventEffectModule : IEcsModule
{
    public string          Name   => "EventEffect";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly EventToEffectSystem       _spawnSystem   = new();
    private readonly VisualEffectCleanupSystem _cleanupSystem = new();

    /// <inheritdoc/>
    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(_spawnSystem);
        registry.RegisterSystem(_cleanupSystem);
    }

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float dt) { }
}
