using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Tests.Input;
using Fdp.Toolkit.Vis2D.Tools;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Tools
{
    /// <summary>
    /// Tests for <see cref="BoxSelectionTool"/> layer-visibility enforcement (BUG2-V001).
    /// </summary>
    public class BoxSelectionToolTests
    {
        private static (EntityRepository, EntityQuery) CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<MapDisplayComponent>();
            var query = world.Query().Build();
            return (world, query);
        }

        [Fact]
        public void FinishSelection_HiddenLayerEntities_NotIncluded()
        {
            var (world, query) = CreateWorld();

            // Entity on layer 0 (mask=1), but canvas hides layer 0 (active mask = 0x2).
            var e1 = world.CreateEntity();
            world.SetComponent(e1, new MapDisplayComponent { LayerMask = 0x1u }); // layer 0 only

            // Entity on layer 1 (mask=2), which is visible.
            var e2 = world.CreateEntity();
            world.SetComponent(e2, new MapDisplayComponent { LayerMask = 0x2u });

            List<Entity>? result = null;
            var tool = new BoxSelectionTool(
                new Vector2(0f, 0f), world, query, _ => new Vector2(50f, 50f),
                selected => result = selected,
                () => { });

            var input   = new MockInputProvider();
            var canvas  = new MapCanvas(input);
            canvas.ActiveLayerMask = 0x2u; // only layer 1 visible

            tool.OnEnter(canvas);
            tool.HandleDrag(new Vector2(100f, 100f), new Vector2(100f, 100f)); // expand selection rect

            // Release left button to trigger FinishSelection via Update.
            input.IsLeftReleased = true;
            tool.Update(0f);

            Assert.NotNull(result);
            Assert.DoesNotContain(e1, result!); // layer 0 is hidden
            Assert.Contains(e2, result!);       // layer 1 is visible
        }

        [Fact]
        public void FinishSelection_VisibleLayerEntities_Included()
        {
            var (world, query) = CreateWorld();

            // Entity on layer 0, which is visible (active mask = 0x1).
            var e1 = world.CreateEntity();
            world.SetComponent(e1, new MapDisplayComponent { LayerMask = 0x1u });

            List<Entity>? result = null;
            var tool = new BoxSelectionTool(
                new Vector2(0f, 0f), world, query, _ => new Vector2(50f, 50f),
                selected => result = selected,
                () => { });

            var input  = new MockInputProvider();
            var canvas = new MapCanvas(input);
            canvas.ActiveLayerMask = 0x1u; // layer 0 visible

            tool.OnEnter(canvas);
            tool.HandleDrag(new Vector2(100f, 100f), new Vector2(100f, 100f));

            input.IsLeftReleased = true;
            tool.Update(0f);

            Assert.NotNull(result);
            Assert.Contains(e1, result!);
        }
    }
}
