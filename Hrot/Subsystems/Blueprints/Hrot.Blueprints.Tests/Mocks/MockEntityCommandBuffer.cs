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
    public abstract void Apply(EntityRepository repo, EcbRemap remap);
}

/// <summary>
/// Placeholder → real entity map built during one <see cref="MockEntityCommandBuffer"/> playback —
/// the same scheme as <c>Fdp.Core.EntityCommandBuffer.Playback</c> (a negative index is a placeholder).
/// </summary>
public sealed class EcbRemap
{
    private readonly Dictionary<int, Entity> _map = new();
    internal void Add(int placeholderIndex, Entity real) => _map[placeholderIndex] = real;
    public Entity Resolve(Entity e) => e.Index < 0 && _map.TryGetValue(e.Index, out var real) ? real : e;
}

/// <summary>
/// ⭐ A DEFERRED create, like the real ECB: the entity is created at playback and the placeholder handed out by
/// <see cref="MockEntityCommandBuffer.CreateEntity"/> is remapped to it.
/// </summary>
public sealed class EcbOp_CreateEntity : EcbOp
{
    private readonly int _placeholderIndex;
    public EcbOp_CreateEntity(int placeholderIndex) { _placeholderIndex = placeholderIndex; }
    public override void Apply(EntityRepository repo, EcbRemap remap) => remap.Add(_placeholderIndex, repo.CreateEntity());
}

public sealed class EcbOp_DestroyEntity : EcbOp
{
    private readonly Entity _entity;
    public EcbOp_DestroyEntity(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity))
            repo.DestroyEntity(_entity);
    }
}

public sealed class EcbOp_AddComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    private readonly T _value;
    public EcbOp_AddComponentUnmanaged(Entity entity, T value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity))
            repo.AddComponent(_entity, _value);
    }
}

public sealed class EcbOp_AddEmptyComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    public EcbOp_AddEmptyComponentUnmanaged(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity))
            repo.AddComponent(_entity, default(T));
    }
}

public sealed class EcbOp_RemoveComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    public EcbOp_RemoveComponentUnmanaged(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity) && repo.HasComponent<T>(_entity))
            repo.RemoveComponent<T>(_entity);
    }
}

public sealed class EcbOp_SetComponentUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly Entity _entity;
    private readonly T _value;
    public EcbOp_SetComponentUnmanaged(Entity entity, T value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity) && repo.HasComponent<T>(_entity))
            repo.SetComponent(_entity, _value);
    }
}

public sealed class EcbOp_AddComponentManaged<T> : EcbOp where T : class
{
    private readonly Entity _entity;
    private readonly T? _value;
    public EcbOp_AddComponentManaged(Entity entity, T? value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity))
            repo.AddComponent(_entity, _value);
    }
}

public sealed class EcbOp_RemoveComponentManaged<T> : EcbOp where T : class
{
    private readonly Entity _entity;
    public EcbOp_RemoveComponentManaged(Entity entity) { _entity = entity; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity) && repo.HasManagedComponent<T>(_entity))
            repo.RemoveComponent<T>(_entity);
    }
}

public sealed class EcbOp_SetManagedComponent<T> : EcbOp where T : class
{
    private readonly Entity _entity;
    private readonly T? _value;
    public EcbOp_SetManagedComponent(Entity entity, T? value) { _entity = entity; _value = value; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity))
            repo.SetManagedComponent(_entity, _value!);
    }
}

public sealed class EcbOp_PublishEventUnmanaged<T> : EcbOp where T : unmanaged
{
    private readonly T _evt;
    public EcbOp_PublishEventUnmanaged(T evt) { _evt = evt; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        repo.Bus.Publish(_evt);
    }
}

public sealed class EcbOp_SetLifecycleState : EcbOp
{
    private readonly Entity _entity;
    private readonly EntityLifecycle _state;
    public EcbOp_SetLifecycleState(Entity entity, EntityLifecycle state) { _entity = entity; _state = state; }
    public override void Apply(EntityRepository repo, EcbRemap remap)
    {
        var _entity = remap.Resolve(this._entity);
        if (repo.IsAlive(_entity))
            repo.SetLifecycleState(_entity, _state);
    }
}

/// <summary>
/// Mock implementation of IEntityCommandBuffer for Blueprint test scenarios.
/// ⭐ It DEFERS everything, like <c>Fdp.Core.EntityCommandBuffer</c>: CreateEntity() returns a placeholder
/// (negative index) that becomes a real entity only at Playback(repo). It used to create eagerly, which let a
/// blueprint use a freshly-spawned handle in the same frame — something production cannot do — and hid
/// CE-479 (a spawn handle that was never valid). All mutations are applied during Playback(repo).
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
        var remap = new EcbRemap();
        foreach (var op in _ops)
            op.Apply(repo, remap);
        _ops.Clear();
        _createCounter = 0;
        LastPlayback = remap;
    }

    /// <summary>Test-only: the placeholder map of the most recent <see cref="Playback"/> — resolve a handle
    /// <see cref="CreateEntity"/> returned to the entity it became.</summary>
    public EcbRemap LastPlayback { get; private set; } = new();

    private int _createCounter;

    // -- IEntityCommandBuffer --

    public Entity CreateEntity()
    {
        // Deferred, as in production: a placeholder with a negative index, remapped at Playback.
        var placeholder = new Entity(-(++_createCounter), 0);
        _ops.Add(new EcbOp_CreateEntity(placeholder.Index));
        return placeholder;
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
