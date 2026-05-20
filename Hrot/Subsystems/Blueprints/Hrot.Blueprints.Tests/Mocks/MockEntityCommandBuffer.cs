using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;

namespace Hrot.Blueprints.Tests.Mocks;

/// <summary>
/// Abstract base for deferred ECS operations recorded by MockEntityCommandBuffer.
/// Insertion order equals playback order; no sorting or deduplication.
/// </summary>
public abstract class EcbOp
{
    public abstract void Apply(EntityRepository repo);
}

/// <summary>
/// Records that an entity was already eagerly created. Apply is a no-op.
/// </summary>
public sealed class EcbOp_CreateEntityRecord : EcbOp
{
    public override void Apply(EntityRepository repo) { /* entity was created eagerly */ }
}

public sealed class EcbOp_DestroyEntity : EcbOp
{
    private readonly Entity _entity;
    public EcbOp_DestroyEntity(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity))
            repo.DestroyEntity(_entity);
    }
}

public sealed class EcbOp_AddComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    private readonly T _value;
    public EcbOp_AddComponentUnmanaged(Entity entity, T value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity))
            repo.AddComponent(_entity, _value);
    }
}

public sealed class EcbOp_AddEmptyComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    public EcbOp_AddEmptyComponentUnmanaged(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity))
            repo.AddComponent(_entity, default(T));
    }
}

public sealed class EcbOp_RemoveComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    public EcbOp_RemoveComponentUnmanaged(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity) && repo.HasComponent<T>(_entity))
            repo.RemoveComponent<T>(_entity);
    }
}

public sealed class EcbOp_SetComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    private readonly T _value;
    public EcbOp_SetComponentUnmanaged(Entity entity, T value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity) && repo.HasComponent<T>(_entity))
            repo.SetComponent(_entity, _value);
    }
}

public sealed class EcbOp_AddComponentManaged<T> : EcbOp where T : class
{
    private readonly Entity _entity;
    private readonly T? _value;
    public EcbOp_AddComponentManaged(Entity entity, T? value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity))
            repo.AddComponent(_entity, _value);
    }
}

public sealed class EcbOp_RemoveComponentManaged<T> : EcbOp where T : class
{
    private readonly Entity _entity;
    public EcbOp_RemoveComponentManaged(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity) && repo.HasManagedComponent<T>(_entity))
            repo.RemoveComponent<T>(_entity);
    }
}

public sealed class EcbOp_SetManagedComponent<T> : EcbOp where T : class
{
    private readonly Entity _entity;
    private readonly T? _value;
    public EcbOp_SetManagedComponent(Entity entity, T? value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity))
            repo.SetManagedComponent(_entity, _value!);
    }
}

public sealed class EcbOp_PublishEventUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly T _evt;
    public EcbOp_PublishEventUnmanaged(T evt) { _evt = evt; }
    public override void Apply(EntityRepository repo)
    {
        repo.Bus.Publish(_evt);
    }
}

public sealed class EcbOp_SetLifecycleState : EcbOp
{
    private readonly Entity _entity;
    private readonly EntityLifecycle _state;
    public EcbOp_SetLifecycleState(Entity entity, EntityLifecycle state) { _entity = entity; _state = state; }
    public override void Apply(EntityRepository repo)
    {
        if (repo.IsAlive(_entity))
            repo.SetLifecycleState(_entity, _state);
    }
}

/// <summary>
/// Mock implementation of IEntityCommandBuffer for Blueprint test scenarios.
/// CreateEntity() is EAGER: the entity is created immediately in the repository.
/// All other mutations are recorded and applied during Playback(repo).
/// Insertion order equals playback order exactly (no sorting or deduplication).
/// </summary>
public sealed class MockEntityCommandBuffer : IEntityCommandBuffer
{
    private readonly EntityRepository _repo;
    private readonly List<EcbOp> _ops = new();

    public MockEntityCommandBuffer(EntityRepository repo)
    {
        _repo = repo;
    }

    // -- Op inspection (for tests) --

    internal IReadOnlyList<EcbOp> OpsForInspection => _ops;
    internal int OpCount => _ops.Count;

    // -- Playback --

    internal void Playback(EntityRepository repo)
    {
        foreach (var op in _ops)
            op.Apply(repo);
        _ops.Clear();
    }

    // -- IEntityCommandBuffer --

    public Entity CreateEntity()
    {
        // Eager: real entity exists immediately so tests can act on it before Playback.
        var entity = _repo.CreateEntity();
        _ops.Add(new EcbOp_CreateEntityRecord());
        return entity;
    }

    public void DestroyEntity(Entity entity)
        => _ops.Add(new EcbOp_DestroyEntity(entity));

    public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        => _ops.Add(new EcbOp_AddComponentUnmanaged<T>(entity, component));

    public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
        => _ops.Add(new EcbOp_SetComponentUnmanaged<T>(entity, component));

    public void RemoveComponent<T>(Entity entity) where T : unmanaged
        => _ops.Add(new EcbOp_RemoveComponentUnmanaged<T>(entity));

    public void AddManagedComponent<T>(Entity entity, T? component) where T : class
        => _ops.Add(new EcbOp_AddComponentManaged<T>(entity, component));

    public void SetManagedComponent<T>(Entity entity, T? component) where T : class
        => _ops.Add(new EcbOp_SetManagedComponent<T>(entity, component));

    public void RemoveManagedComponent<T>(Entity entity) where T : class
        => _ops.Add(new EcbOp_RemoveComponentManaged<T>(entity));

    public void PublishEvent<T>(in T evt) where T : unmanaged
        => _ops.Add(new EcbOp_PublishEventUnmanaged<T>(evt));

    public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size)
        => throw new NotSupportedException("SetComponentRaw is not supported in MockEntityCommandBuffer.");

    public void SetManagedComponentRaw(Entity entity, int typeId, object obj)
        => throw new NotSupportedException("SetManagedComponentRaw is not supported in MockEntityCommandBuffer.");

    public void SetLifecycleState(Entity entity, EntityLifecycle state)
        => _ops.Add(new EcbOp_SetLifecycleState(entity, state));

    // -- Test-only: AddEmptyComponent (not on IEntityCommandBuffer interface) --

    public void AddEmptyComponent<T>(Entity entity) where T : unmanaged
        => _ops.Add(new EcbOp_AddEmptyComponentUnmanaged<T>(entity));
}
