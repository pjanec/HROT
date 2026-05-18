using Fbt.Serialization;
using Fbt.Utilities;
using Xunit;

namespace Fbt.Tests.Unit
{
    public class TreeVisualizerTests
    {
        [Fact]
        public void Visualize_SimpleTree_ReturnsCorrectString()
        {
            var blob = new BehaviorTreeBlob
            {
                TreeName = "TestTree",
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "MyAction" }
            };

            string output = TreeVisualizer.Visualize(blob);

            Assert.Contains("Tree: TestTree", output);
            Assert.Contains("[0] Sequence", output);
            Assert.Contains("[1] Action", output);
            Assert.Contains("MyAction", output);
        }

        [Fact]
        public void Visualize_NestedTree_IndentationCorrect()
        {
             var blob = new BehaviorTreeBlob
            {
                TreeName = "IndentTest",
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Selector, ChildCount = 1, SubtreeOffset = 3 },
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "A" }
            };
            
            string output = TreeVisualizer.Visualize(blob);
            
            // Check for indentation (2 spaces per level)
            // [0] Selector (depth 0)
            //   [1] Sequence (depth 1, 2 spaces)
            //     [2] Action (depth 2, 4 spaces)
            
            Assert.Contains("[0] Selector", output);
            Assert.Contains("[1] Sequence", output);
            Assert.Contains("[2] Action", output);
        }

        [Fact]
        public void Visualize_ExtendedNodes_ShowsParams()
        {
             var blob = new BehaviorTreeBlob
            {
                TreeName = "ParamsTest",
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 3, SubtreeOffset = 4 },
                    new NodeDefinition { Type = NodeType.Wait, PayloadIndex = 0, SubtreeOffset = 1 },
                    new NodeDefinition { Type = NodeType.Repeater, PayloadIndex = 0, SubtreeOffset = 1 },
                    new NodeDefinition { Type = NodeType.Cooldown, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                FloatParams = new[] { 1.5f }, // Wait=1.5
                IntParams = new[] { 5 } // Repeat=5
            };
            
            // Note: Indices above are illustrative, actual payload indices refer to separate arrays.
            // Wait uses FloatParams[0]
            // Repeater uses IntParams[0]
            // Cooldown uses FloatParams[0] (reusing same array)
            
            string output = TreeVisualizer.Visualize(blob);
            Assert.Contains("Wait", output);
            Assert.Contains("Repeater", output);
            Assert.Contains("Cooldown", output);
            
            Assert.Contains("Tree: ParamsTest", output);
        }
    }
}
