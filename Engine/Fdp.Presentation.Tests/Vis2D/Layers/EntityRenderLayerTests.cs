using System.Numerics;
using Xunit;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Moq;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
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
            
            // Act - Pick at (21, 21) -> Should hit e2 (dist sqrt(2) approx 1.4 < 5)
            // e1 is far (dist sqrt(11^2 + 11^2) approx 15 > 5)
            // Selection state is managed by map tools via PickEntity(), not HandleInput().
            
            Entity? hit = layer.PickEntity(new Vector2(21, 21));
            
            // Assert
            Assert.Equal(e2, hit);
        }

        // ── BUG2-V001 — Catch-all mode filters hidden entities ──────────────────

        /// <summary>
        /// When <see cref="EntityRenderLayer"/> is created with <c>layerBitIndex = -1</c>
        /// (catch-all mode), entities whose <see cref="MapDisplayComponent.LayerMask"/> has
        /// NO bits in common with <see cref="RenderContext.VisibleLayersMask"/> must be skipped.
        /// </summary>
        [Fact]
        public void Draw_CatchAllMode_HiddenEntities_Skipped()
        {
            var world = new EntityRepository();
            world.RegisterComponent<MapDisplayComponent>();

            var adapter   = new Mock<IVisualizerAdapter>();
            var selection = new Mock<ISelectionState>();
            var query     = world.Query().Build();

            // Catch-all layer (layerBitIndex = -1).
            var layer = new EntityRenderLayer("All", layerBitIndex: -1, world, query, adapter.Object, selection.Object);

            // Entity with LayerMask=0x1 (layer 0) should be hidden when VisibleLayersMask=0x2.
            var hidden = world.CreateEntity();
            world.SetComponent(hidden, new MapDisplayComponent { LayerMask = 0x1u });

            // Entity with LayerMask=0x2 (layer 1) should be rendered when VisibleLayersMask=0x2.
            var visible = world.CreateEntity();
            world.SetComponent(visible, new MapDisplayComponent { LayerMask = 0x2u });

            adapter.Setup(a => a.GetPosition(It.IsAny<ISimulationView>(), It.IsAny<Entity>()))
                   .Returns(Vector2.Zero);

            // VisibleLayersMask = 0x2 → only layer 1 visible.
            var ctx = new RenderContext { VisibleLayersMask = 0x2u };
            layer.Draw(ctx);

            // Hidden entity: Render must NOT be called.
            adapter.Verify(
                a => a.Render(It.IsAny<ISimulationView>(), hidden, It.IsAny<Vector2>(),
                              It.IsAny<RenderContext>(), It.IsAny<bool>(), It.IsAny<bool>()),
                Times.Never);

            // Visible entity: Render must be called exactly once.
            adapter.Verify(
                a => a.Render(It.IsAny<ISimulationView>(), visible, It.IsAny<Vector2>(),
                              It.IsAny<RenderContext>(), It.IsAny<bool>(), It.IsAny<bool>()),
                Times.Once);
        }
    }
}
