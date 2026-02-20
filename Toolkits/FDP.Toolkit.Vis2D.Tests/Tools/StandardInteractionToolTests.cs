using Xunit;
using Moq;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Tools;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Tests.Input;
using ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests.Tools
{
    public class StandardInteractionToolTests
    {
        [Fact]
        public void HandleClick_InvokesOnWorldClick()
        {
            // Arrange
            var view = new Mock<ISimulationView>();
            var repo = new EntityRepository();
            var query = repo.Query().Build();
            var adapter = new Mock<IVisualizerAdapter>();
            
            var tool = new StandardInteractionTool(view.Object, query, adapter.Object);
            
            var canvas = new MapCanvas(new MockInputProvider());
            tool.OnEnter(canvas);
            
            bool clicked = false;
            tool.OnWorldClick += (pos, btn, s, c, e) => {
                if (pos == new Vector2(100, 100)) clicked = true;
            };

            // Act
            tool.HandleClick(new Vector2(100, 100), MouseButton.Left);
            
            // Assert
            Assert.True(clicked);
        }
        
        [Fact]
        public void ShiftClick_Detected()
        {
             // Arrange
            var repo = new EntityRepository();
            var tool = new StandardInteractionTool(new Mock<ISimulationView>().Object, repo.Query().Build(), new Mock<IVisualizerAdapter>().Object);
            
            var input = new MockInputProvider();
            input.IsShiftDown = true;
            
            var canvas = new MapCanvas(input);
            tool.OnEnter(canvas);
            
            bool wasShift = false;
            tool.OnWorldClick += (pos, btn, s, c, e) => {
                wasShift = s;
            };

            // Act
            tool.HandleClick(new Vector2(100, 100), MouseButton.Left);
            
            // Assert
            Assert.True(wasShift, "Shift modifier should be detected");
        }
        
        [Fact]
        public void FindEntity_SelectsClosest()
        {
            // Arrange
            var repo = new EntityRepository();
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            
            var adapter = new Mock<IVisualizerAdapter>();
            var view = new Mock<ISimulationView>();
            
            adapter.Setup(a => a.GetPosition(view.Object, e1)).Returns(new Vector2(10, 10));
            adapter.Setup(a => a.GetHitRadius(view.Object, e1)).Returns(5f);
            
            adapter.Setup(a => a.GetPosition(view.Object, e2)).Returns(new Vector2(20, 20));
            adapter.Setup(a => a.GetHitRadius(view.Object, e2)).Returns(5f);
            
            var tool = new StandardInteractionTool(view.Object, repo.Query().Build(), adapter.Object);
            var canvas = new MapCanvas();
            tool.OnEnter(canvas);
            
            Entity hitEntity = Entity.Null;
            tool.OnWorldClick += (pos, btn, s, c, e) => {
                hitEntity = e;
            };
            
            // Act: Click at (21, 21) -> Should hit e2
            tool.HandleClick(new Vector2(21,21), MouseButton.Left);
            
            // Assert
            Assert.Equal(e2, hitEntity);
        }
    }
}
