using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Blueprints.Tests.MockSystems;

/// <summary>
/// Base class for test-only dispatcher systems that read channel commands
/// authored by Blueprints and stub out the Status field.
/// Used to write linear end-to-end tests for AiPrimitive Blueprints that
/// issue commands (e.g., MoveToAndFire).
/// Casts ISimulationView to EntityRepository for writable ref access (per Q-12.4 resolution).
/// </summary>
public abstract class MockDispatcherSystem<TChannel> : IEcsModuleSystem, IProfiledSystem
    where TChannel : unmanaged
{
    public string ProfileName => $"Mock{typeof(TChannel).Name}Dispatcher";

    protected EntityRepository? Repo { get; private set; }
    private EntityQuery? _query;

    public void Execute(ISimulationView view, float deltaTime)
    {
        Repo = (EntityRepository)view;
        _query ??= Repo.Query().With<TChannel>().Build();

        foreach (var entity in _query)
        {
            ref var channel = ref Repo.GetComponentRW<TChannel>(entity);
            HandleChannel(ref channel, entity, view);
        }
    }

    /// <summary>
    /// Subclasses implement the test-specific dispatcher behavior -- typically
    /// reading the ActiveAction field, deciding the new Status, and writing it back.
    /// </summary>
    protected abstract void HandleChannel(ref TChannel channel, Entity entity, ISimulationView view);
}
