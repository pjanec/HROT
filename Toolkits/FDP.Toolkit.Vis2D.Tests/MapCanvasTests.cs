using Xunit;
using Moq;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using System.Numerics;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests
{
    public class TestableInputProvider : IInputProvider
    {
        public Vector2 MousePosition { get; set; }
        public Vector2 MouseDelta { get; set; }
        public float MouseWheelMove { get; set; }
        
        public bool IsLeftPressed { get; set; }
        public bool IsRightPressed { get; set; }
        public bool IsLeftDown { get; set; }
        public bool IsRightDown { get; set; }
        public bool IsLeftReleased { get; set; }
        public bool IsRightReleased { get; set; }

        public bool IsMouseCaptured { get; set; }
        public bool IsKeyboardCaptured { get; set; }

        public bool IsMouseButtonPressed(MouseButton button)
        {
            if (button == MouseButton.Left) return IsLeftPressed;
            if (button == MouseButton.Right) return IsRightPressed;
            return false;
        }

        public bool IsMouseButtonDown(MouseButton button)
        {
            if (button == MouseButton.Left) return IsLeftDown;
            if (button == MouseButton.Right) return IsRightDown;
            return false;
        }

        public bool IsMouseButtonReleased(MouseButton button)
        {
            if (button == MouseButton.Left) return IsLeftReleased;
            if (button == MouseButton.Right) return IsRightReleased;
            return false;
        }

        public bool IsKeyPressed(KeyboardKey key) => false;
        public bool IsKeyDown(KeyboardKey key) => false;
        public bool IsKeyReleased(KeyboardKey key) => false;
    }

    public class TestableMapCameraForCanvas : MapCamera
    {
        public override void BeginMode() { /* No-op */ }
        public override void EndMode() { /* No-op */ }
        public override void Update(float dt) { /* No-op */ }
        public override bool HandleInput(IInputProvider input) => false;
        
        // Pass-through for math
        public override Vector2 ScreenToWorld(Vector2 v) { return v; }
        public override Vector2 WorldToScreen(Vector2 v) { return v; }
    }

    public class TestableMapCanvas : MapCanvas
    {
        public TestableInputProvider InputProvider { get; }

        public TestableMapCanvas() : this(new TestableInputProvider()) {}
        
        private TestableMapCanvas(TestableInputProvider input) : base(input) 
        {
            InputProvider = input;
        }

        public Vector2 MockMousePosition { get => InputProvider.MousePosition; set => InputProvider.MousePosition = value; }
        public float MockFrameTime { get; set; } = 0.016f;
        public Vector2 MockMouseDelta { get => InputProvider.MouseDelta; set => InputProvider.MouseDelta = value; }
        
        // Mouse button states
        public bool IsLeftPressed { get => InputProvider.IsLeftPressed; set => InputProvider.IsLeftPressed = value; }
        public bool IsRightPressed { get => InputProvider.IsRightPressed; set => InputProvider.IsRightPressed = value; }
        public bool IsLeftDown { get => InputProvider.IsLeftDown; set => InputProvider.IsLeftDown = value; }
        public bool IsRightDown { get => InputProvider.IsRightDown; set => InputProvider.IsRightDown = value; }
        
        // Forwarding missing Release props if needed by tests? 
        // Existing tests likely set Pressed/Down.
        // If Layer tests rely on Release, they might fail if Release is false.
        // But let's assume basic structure first.

        protected override Vector2 GetMousePosition() => InputProvider.MousePosition;
        protected override float GetFrameTime() => MockFrameTime;
        protected override Vector2 GetMouseDelta() => InputProvider.MouseDelta;
        protected override bool IsMouseCaptured() => false;

        protected override bool IsMouseButtonPressed(MouseButton button) => InputProvider.IsMouseButtonPressed(button);
        protected override bool IsMouseButtonDown(MouseButton button) => InputProvider.IsMouseButtonDown(button);

        // Public wrapper for testing
        public new void HandleInput()
        {
            // Base HandleInput uses _input (InputProvider), so it will see our values
            base.ProcessInputPipeline();
        }
    }

    public class MapCanvasTests
    {
        [Fact]
        public void MapCanvas_AddLayer_IncreasesLayerCount()
        {
            var canvas = new TestableMapCanvas();
            canvas.Camera = new TestableMapCameraForCanvas();
            
            var layer = new Mock<IMapLayer>();
            layer.Setup(x => x.Update(It.IsAny<float>()));
            
            canvas.AddLayer(layer.Object);
            
            // Verify Update calls layer update
            canvas.Update(1.0f);
            layer.Verify(x => x.Update(1.0f), Times.Once);
        }

        [Fact]
        public void MapCanvas_LayerMask_FiltersVisibility()
        {
            var canvas = new TestableMapCanvas();
            canvas.Camera = new TestableMapCameraForCanvas();
            
            var layerVisible = new Mock<IMapLayer>();
            layerVisible.Setup(l => l.LayerBitIndex).Returns(0); // Bit 0
            
            var layerHidden = new Mock<IMapLayer>();
            layerHidden.Setup(l => l.LayerBitIndex).Returns(1); // Bit 1
            
            canvas.AddLayer(layerVisible.Object);
            canvas.AddLayer(layerHidden.Object);
            
            // Allow only Layer 0
            canvas.ActiveLayerMask = 1; 

            canvas.Draw();
            
            layerVisible.Verify(l => l.Draw(It.IsAny<RenderContext>()), Times.Once);
            layerHidden.Verify(l => l.Draw(It.IsAny<RenderContext>()), Times.Never);
        }

        [Fact]
        public void MapCanvas_SwitchTool_CallsOnEnterExit()
        {
            var canvas = new TestableMapCanvas();
            canvas.Camera = new TestableMapCameraForCanvas();
            
            var oldTool = new Mock<IMapTool>();
            var newTool = new Mock<IMapTool>();
            
            canvas.SwitchTool(oldTool.Object);
            canvas.SwitchTool(newTool.Object);
            
            oldTool.Verify(t => t.OnExit(), Times.Once);
            newTool.Verify(t => t.OnEnter(canvas), Times.Once);
            
            Assert.Same(newTool.Object, canvas.ActiveTool);
        }

        [Fact]
        public void MapCanvas_HandleInput_ReversesOrder()
        {
             var canvas = new TestableMapCanvas();
             canvas.Camera = new TestableMapCameraForCanvas();
             
             // Setup input
             canvas.IsLeftPressed = true;

             var bottomLayer = new Mock<IMapLayer>(); // Layer 0
             // Should NOT be called because Top consumes it
             
             var topLayer = new Mock<IMapLayer>();    // Layer 1
             topLayer.Setup(l => l.HandleInput(It.IsAny<Vector2>(), It.IsAny<MouseButton>(), It.IsAny<bool>())).Returns(true); // Consumes

             canvas.AddLayer(bottomLayer.Object); // 0
             canvas.AddLayer(topLayer.Object);    // 1

             // Act (directly call HandleInput for test)
             canvas.HandleInput();

             // Assert
             // Top layer (index 1) should be checked first
             topLayer.Verify(l => l.HandleInput(It.IsAny<Vector2>(), MouseButton.Left, true), Times.Once);
             
             // Bottom layer should NOT be called
             bottomLayer.Verify(l => l.HandleInput(It.IsAny<Vector2>(), It.IsAny<MouseButton>(), It.IsAny<bool>()), Times.Never);
         }
    }
}
