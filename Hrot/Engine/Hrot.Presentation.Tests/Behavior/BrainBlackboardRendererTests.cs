using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Presentation.Renderers;
using Xunit;

namespace Hrot.Presentation.Tests;

[Collection("ImGui Sequential")]
public class BrainBlackboardRendererTests
{
    private static readonly BrainBlackboardRenderer _renderer = new BrainBlackboardRenderer();

    // SC3: Renderer returns false when entity has no BehaviorState
    [Fact]
    public void RenderValue_ReturnsFalse_WhenNoBehaviorState()
    {
        var session = new MockSession(hasBehaviorState: false);
        var entity = new Entity(1, 1);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, entity, bb, out _);

        Assert.False(result);
    }

    // Renderer returns false when BehaviorRegistry is null
    [Fact]
    public void RenderValue_ReturnsFalse_WhenRegistryNull()
    {
        BrainBlackboardRenderer.BehaviorRegistryAccessor = null;
        var session = new MockSession(hasBehaviorState: true, behaviorHash: 42);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, new Entity(1, 1), bb, out _);

        Assert.False(result);
    }

    // GetSummary returns non-null
    [Fact]
    public void GetSummary_ReturnsNonNull()
    {
        var bb = new BrainBlackboard();
        var result = _renderer.GetSummary(bb);
        Assert.NotNull(result);
    }

    // Non-entity-aware path always returns false
    [Fact]
    public void RenderValue_Object_ReturnsFalse()
    {
        bool result = _renderer.RenderValue(new BrainBlackboard());
        Assert.False(result);
    }

    // With registry but unknown behavior hash -> false
    [Fact]
    public void RenderValue_ReturnsFalse_WhenBehaviorNotRegistered()
    {
        var registry = new BehaviorRegistry();
        BrainBlackboardRenderer.BehaviorRegistryAccessor = registry;
        var session = new MockSession(hasBehaviorState: true, behaviorHash: 999);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, new Entity(1, 1), bb, out _);

        Assert.False(result);
    }

    // Task 2a: BehaviorDefinition stores ManagedBlackboardVariables and round-trips correctly
    [Fact]
    public void BehaviorDefinition_StoreManagedBlackboardVariables_RoundTrips()
    {
        var vars = new[]
        {
            new ManagedBlackboardVariable("counter", typeof(int), 0),
            new ManagedBlackboardVariable("accum",   typeof(int), 8),
        };
        var def = new BehaviorDefinition
        {
            Name = "T10_MultiAction",
            BrainTier = BehaviorConstants.BrainTierBTree,
            ManagedBlackboardVariables = vars,
        };
        Assert.Equal(2, def.ManagedBlackboardVariables!.Count);
        Assert.Equal("counter", def.ManagedBlackboardVariables[0].Name);
        Assert.Equal(0, def.ManagedBlackboardVariables[0].ByteOffset);
        Assert.Equal("accum", def.ManagedBlackboardVariables[1].Name);
        Assert.Equal(8, def.ManagedBlackboardVariables[1].ByteOffset);
    }

    // Helpers
    private sealed class MockSession : IInspectableSession
    {
        private readonly bool _hasBehaviorState;
        private readonly int _behaviorHash;
        public MockSession(bool hasBehaviorState, int behaviorHash = 0)
        {
            _hasBehaviorState = hasBehaviorState;
            _behaviorHash     = behaviorHash;
        }
        public bool IsReadOnly => true;
        public int EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasComponent(Entity e, Type t) => t == typeof(BehaviorState) && _hasBehaviorState;
        public object? GetComponent(Entity e, Type t)
            => t == typeof(BehaviorState) && _hasBehaviorState
                ? (object)new BehaviorState { ActiveBehaviorHash = _behaviorHash }
                : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => false;
    }
}
