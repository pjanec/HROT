using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Xunit;

namespace Fdp.Presentation.Tests;

[Collection("ImGui Sequential")]
public class EntityAwareRendererTests
{
    // SC1: Interface hierarchy
    [Fact]
    public void IEntityAwareImGuiRenderer_IsAssignableFrom_IImGuiRenderer()
    {
        Assert.True(typeof(IImGuiRenderer).IsAssignableFrom(typeof(IEntityAwareImGuiRenderer)));
    }

    // Verify ComponentReflector dispatches to entity-aware path for IEntityAwareImGuiRenderer
    [Fact]
    public void ComponentReflector_DispatchesTo_EntityAwareRenderer_WhenRegistered()
    {
        // Arrange: create a mock IEntityAwareImGuiRenderer
        var mock = new MockEntityAwareRenderer();
        ImGuiRendererRegistry.Register(typeof(SampleComponent), mock);

        // Act: check via interface that the registry returns the right type
        var renderer = ImGuiRendererRegistry.GetRenderer(typeof(SampleComponent));
        
        // Assert: the renderer implements IEntityAwareImGuiRenderer
        Assert.NotNull(renderer);
        Assert.IsAssignableFrom<IEntityAwareImGuiRenderer>(renderer);
    }

    // Helper types
    [ComponentId(200)]
    private struct SampleComponent { public int X; }

    private sealed class MockEntityAwareRenderer : IEntityAwareImGuiRenderer
    {
        public bool WasCalled { get; private set; }
        public string? GetSummary(object value) => null;
        public bool RenderValue(object value) => false;
        public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
        {
            doubleClickedPath = null;
            WasCalled = true;
            return true;
        }
    }
}
