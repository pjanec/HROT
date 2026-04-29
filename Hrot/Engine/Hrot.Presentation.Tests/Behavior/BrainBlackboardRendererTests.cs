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

    // SC3: Renderer returns false when entity has no DoctrineState
    [Fact]
    public void RenderValue_ReturnsFalse_WhenNoDoctrineState()
    {
        var session = new MockSession(hasDoctrineState: false);
        var entity = new Entity(1, 1);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, entity, bb);

        Assert.False(result);
    }

    // Renderer returns false when DoctrineRegistry is null
    [Fact]
    public void RenderValue_ReturnsFalse_WhenRegistryNull()
    {
        BrainBlackboardRenderer.DoctrineRegistryAccessor = null;
        var session = new MockSession(hasDoctrineState: true, doctrineHash: 42);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, new Entity(1, 1), bb);

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

    // With registry but unknown doctrine hash -> false
    [Fact]
    public void RenderValue_ReturnsFalse_WhenDoctrineNotRegistered()
    {
        var registry = new DoctrineRegistry();
        BrainBlackboardRenderer.DoctrineRegistryAccessor = registry;
        var session = new MockSession(hasDoctrineState: true, doctrineHash: 999);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, new Entity(1, 1), bb);

        Assert.False(result);
    }

    // Helpers
    private sealed class MockSession : IInspectableSession
    {
        private readonly bool _hasDoctrineState;
        private readonly int _doctrineHash;
        public MockSession(bool hasDoctrineState, int doctrineHash = 0)
        {
            _hasDoctrineState = hasDoctrineState;
            _doctrineHash     = doctrineHash;
        }
        public bool IsReadOnly => true;
        public int EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasComponent(Entity e, Type t) => t == typeof(DoctrineState) && _hasDoctrineState;
        public object? GetComponent(Entity e, Type t)
            => t == typeof(DoctrineState) && _hasDoctrineState
                ? (object)new DoctrineState { ActiveDoctrineHash = _doctrineHash }
                : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => false;
    }
}
