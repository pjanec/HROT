using System.Numerics;
using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Layers;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using Fdp.Kernel.Collections;
using ModuleHost.Core.Abstractions;
using Moq;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests.Layers
{
    public class EntityRenderLayerTests
    {
        [Fact]
        public void EntityRenderLayer_LayerMaskFilter_HidesNonMatching()
        {
            // Setup
            var world = new EntityRepository();
            world.RegisterComponent<MapDisplayComponent>();
            
            var adapter = new Mock<IVisualizerAdapter>();
            var selection = new Mock<ISelectionState>();
            
            // Create query for all entities (empty filter)
            var query = world.Query().Build();
            
            // Layer 0 is default
            var layer = new EntityRenderLayer("TestLayer", 0, world, query, adapter.Object, selection.Object);

            // Create entities
            var e1 = world.CreateEntity();
            world.SetComponent(e1, new MapDisplayComponent { LayerMask = 1 }); // Matches layer 0 (bit 0)
            
            var e2 = world.CreateEntity();
            world.SetComponent(e2, new MapDisplayComponent { LayerMask = 2 }); // Layer 1 (bit 1) -> Should be hidden
            
            var e3 = world.CreateEntity(); // No component -> Default is Layer 0 -> Should be visible?
            // Wait, logic in Layer:
            // "If entity doesn't have MapDisplayComponent, assume it's on Layer 0? Or hidden?"
            // Usually hidden or default?
            // "If entity doesn't have MapDisplayComponent, assume it's on Layer 0" -> In my code I assumed Layer 0 (mask=1).
            
            // Setup adapter to return valid position
            adapter.Setup(a => a.GetPosition(It.IsAny<ISimulationView>(), It.IsAny<Entity>())).Returns(Vector2.Zero);
            
            // Render Context: VisibleLayersMask allows Layer 0 (bit 0)
            // VisibleLayersMask = 1
            var ctx = new RenderContext { VisibleLayersMask = 1 }; // Bit 0 enabled

            // Act
            layer.Draw(ctx);

            // Assert
            // e1 should be rendered
            adapter.Verify(a => a.Render(It.IsAny<ISimulationView>(), e1, It.IsAny<Vector2>(), It.IsAny<RenderContext>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
            
            // e2 should NOT be rendered (layer mismatch)
            adapter.Verify(a => a.Render(It.IsAny<ISimulationView>(), e2, It.IsAny<Vector2>(), It.IsAny<RenderContext>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
            
            // e3 should be rendered (default to layer 0)
            adapter.Verify(a => a.Render(It.IsAny<ISimulationView>(), e3, It.IsAny<Vector2>(), It.IsAny<RenderContext>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public void EntityRenderLayer_HitTest_FindsClosest()
        {
            var world = new EntityRepository();
            var adapter = new Mock<IVisualizerAdapter>();
            var selection = new Mock<ISelectionState>();
            
            var query = world.Query().Build();
            var layer = new EntityRenderLayer("TestLayer", 0, world, query, adapter.Object, selection.Object);

            var e1 = world.CreateEntity(); // At (10, 10)
            var e2 = world.CreateEntity(); // At (20, 20) close to click
            
            adapter.Setup(a => a.GetPosition(world, e1)).Returns(new Vector2(10, 10));
            adapter.Setup(a => a.GetPosition(world, e2)).Returns(new Vector2(20, 20));
            
            adapter.Setup(a => a.GetHitRadius(world, It.IsAny<Entity>())).Returns(5.0f);
            
            // Act - Click at (21, 21) -> Should hit e2 (dist sqrt(2) approx 1.4 < 5)
            // e1 is far (dist sqrt(11^2 + 11^2) approx 15 > 5)
            
            bool consumed = layer.HandleInput(new Vector2(21, 21), MouseButton.Left, true);
            
            // Assert
            Assert.True(consumed);
            selection.VerifySet(s => s.PrimarySelected = e2);
        }
    }
}
