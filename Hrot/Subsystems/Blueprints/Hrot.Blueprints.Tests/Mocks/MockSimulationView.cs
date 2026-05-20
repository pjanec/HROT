using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Blueprints.Tests.Mocks;

/// <summary>
/// Read-only projection of EntityRepository for test scenarios.
/// Implements ISimulationView by forwarding all reads to the underlying repo.
/// Per Patch 1 (TH-006): ReadEvents&lt;T&gt;() delegates directly to _repo.Bus.Read&lt;T&gt;().
/// No _eventStreamsByType field. No BeginTick method.
/// </summary>
public sealed class MockSimulationView : ISimulationView
{
    private readonly EntityRepository _repo;
    private readonly IEntityCommandBuffer _ecb;

    private float _time;
    private float _deltaTime;
    private uint _tick;

    public MockSimulationView(EntityRepository repo, IEntityCommandBuffer ecb)
    {
        _repo = repo;
        _ecb = ecb;
    }

    // -- Time state (advanced by BlueprintTestFixture.TickFrame) --

    public float Time => _time;
    public float DeltaTime => _deltaTime;
    public uint Tick => _tick;

    internal void AdvanceTime(float dt)
    {
        _time += dt;
        _deltaTime = dt;
        _tick++;
    }

    // -- ISimulationView: entity liveness and components --

    public bool IsAlive(Entity e) => _repo.IsAlive(e);

    public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
        => ref _repo.GetComponentRO<T>(e);

    public T GetManagedComponentRO<T>(Entity e) where T : class
        => ((ISimulationView)_repo).GetManagedComponentRO<T>(e);

    public bool HasComponent<T>(Entity e) where T : unmanaged
        => _repo.HasComponent<T>(e);

    public bool HasManagedComponent<T>(Entity e) where T : class
        => _repo.HasManagedComponent<T>(e);

    // -- ISimulationView: events (Patch 1: direct bus delegation, no intermediate dict) --

    public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged
        => _repo.Bus.Read<T>();

    public IReadOnlyList<T> ReadManagedEvents<T>()
        => _repo.Bus.ReadManaged<T>();

    // -- ISimulationView: queries and command buffer --

    public QueryBuilder Query() => _repo.Query();

    public IEntityCommandBuffer GetCommandBuffer() => _ecb;
}
