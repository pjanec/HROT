using Xunit;
using Moq;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Tools;
using FDP.Toolkit.Vis2D.Tests.Input;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests.Tools
{
    public class EntityDragToolTests
    {
        [Fact]
        public void HandleDrag_UpdatesPosition_WhenMouseIsDown()
        {
            // Arrange
            var repo = new EntityRepository();
            var entity = repo.CreateEntity();
            
            bool moved = false;
            Vector2 lastPos = Vector2.Zero;
            
            var tool = new EntityDragTool(entity, Vector2.Zero, () => {});
            tool.OnEntityMoved += (e, pos) => {
                moved = true;
                lastPos = pos;
            };
            
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            tool.OnEnter(canvas);
            
            // Set input state: Mouse DOWN
            input.IsLeftDown = true;
            
            // Act
            bool handled = tool.HandleDrag(new Vector2(50, 50), new Vector2(50, 50));
            
            // Assert
            Assert.True(handled);
            Assert.True(moved);
            Assert.Equal(new Vector2(50, 50), lastPos);
        }

        [Fact]
        public void HandleDrag_DoesNothing_WhenMouseUp()
        {
            // Arrange
            var repo = new EntityRepository();
            var entity = repo.CreateEntity();

            var tool = new EntityDragTool(entity, Vector2.Zero, () => {});
            
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            tool.OnEnter(canvas);
            
            // Mouse UP
            input.IsLeftDown = false;
            
            // Act
            bool handled = tool.HandleDrag(new Vector2(50, 50), new Vector2(50, 50));
            
            // Assert
            Assert.False(handled);
        }
        
         [Fact]
        public void Update_CompletesDrag_WhenMouseReleased()
        {
             // Arrange
            var repo = new EntityRepository();
            var entity = repo.CreateEntity();

            bool completed = false;
            var tool = new EntityDragTool(entity, Vector2.Zero, () => { completed = true; });
            
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            tool.OnEnter(canvas);
            
            // Simulate release
            input.IsLeftReleased = true;
            
            // Act
            tool.Update(0.1f);
            
            // Assert
            Assert.True(completed);
        }
    }
}
