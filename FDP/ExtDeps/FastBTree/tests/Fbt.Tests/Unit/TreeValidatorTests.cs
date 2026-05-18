using Xunit;
using Fbt.Serialization;

namespace Fbt.Tests.Unit
{
    public class TreeValidatorTests
    {
        [Fact]
        public void TreeValidator_ValidTree_NoErrors()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] 
                { 
                    new NodeDefinition { Type = NodeType.Action, SubtreeOffset = 1, PayloadIndex = 0 } 
                },
                MethodNames = new[] { "A" }
            };

            var result = TreeValidator.Validate(blob);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void TreeValidator_InvalidSubtreeOffset_ReportsError()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] 
                { 
                    new NodeDefinition { Type = NodeType.Action, SubtreeOffset = 0 } // Zero offset is invalid
                }
            };

            var result = TreeValidator.Validate(blob);
            Assert.False(result.IsValid);
            Assert.Contains("SubtreeOffset is zero", result.Errors[0]);
        }

        [Fact]
        public void TreeValidator_OffsetOutOfBounds_ReportsError()
        {
             var blob = new BehaviorTreeBlob
            {
                Nodes = new[] 
                { 
                    new NodeDefinition { Type = NodeType.Action, SubtreeOffset = 5 } // Length is 1
                }
            };

            var result = TreeValidator.Validate(blob);
            Assert.False(result.IsValid);
            Assert.Contains("exceeds tree bounds", result.Errors[0]);
        }

        [Fact]
        public void TreeValidator_InvalidPayloadIndex_ReportsError()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] 
                { 
                    new NodeDefinition { Type = NodeType.Action, SubtreeOffset = 1, PayloadIndex = 99 } // No methods
                },
                MethodNames = new string[0]
            };

            var result = TreeValidator.Validate(blob);
            Assert.False(result.IsValid);
            Assert.Contains("Invalid method PayloadIndex", result.Errors[0]);
        }

        [Fact]
        public void Validate_NestedParallel_ReportsWarning()
        {
            // Build a blob directly (without CompileFromJson which now throws for nested Parallel).
            // Layout: Parallel(0) -> Sequence -> Parallel(0) -> Action
            // 0: Parallel, subtree=4, children=1, PayloadIndex=0
            // 1: Sequence, subtree=3, children=1, PayloadIndex=-1
            // 2: Parallel, subtree=2, children=1, PayloadIndex=0
            // 3: Action,   subtree=1, children=0, PayloadIndex=0
            var blob = new BehaviorTreeBlob
            {
                TreeName = "NestedParallel",
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Parallel,  ChildCount = 1, SubtreeOffset = 4, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Sequence,  ChildCount = 1, SubtreeOffset = 3, PayloadIndex = -1 },
                    new NodeDefinition { Type = NodeType.Parallel,  ChildCount = 1, SubtreeOffset = 2, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action,    ChildCount = 0, SubtreeOffset = 1, PayloadIndex = 0 },
                },
                IntParams = new[] { 0 },
                MethodNames = new[] { "A" }
            };

            var result = TreeValidator.Validate(blob);

            Assert.True(result.IsValid); // No errors
            Assert.True(result.HasWarnings); // But has warnings
            Assert.Contains("Nested Parallel", result.Warnings[0]);
        }

        [Fact]
        public void Validate_NestedRepeater_ReportsWarning()
        {
            // Build a blob directly (without CompileFromJson which now throws for nested Repeater).
            // Layout: Repeater(2) -> Sequence -> Repeater(2) -> Action
            // 0: Repeater, subtree=4, children=1, PayloadIndex=0
            // 1: Sequence, subtree=3, children=1, PayloadIndex=-1
            // 2: Repeater, subtree=2, children=1, PayloadIndex=0
            // 3: Action,   subtree=1, children=0, PayloadIndex=0
            var blob = new BehaviorTreeBlob
            {
                TreeName = "NestedRepeater",
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Repeater, ChildCount = 1, SubtreeOffset = 4, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 1, SubtreeOffset = 3, PayloadIndex = -1 },
                    new NodeDefinition { Type = NodeType.Repeater, ChildCount = 1, SubtreeOffset = 2, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, PayloadIndex = 0 },
                },
                IntParams = new[] { 2 },
                MethodNames = new[] { "A" }
            };

            var result = TreeValidator.Validate(blob);

            Assert.True(result.IsValid);
            Assert.True(result.HasWarnings);
            Assert.Contains("Nested Repeater", result.Warnings[0]);
        }

        [Fact]
        public void Validate_ParallelTooManyChildren_ReportsWarning()
        {
            // Create Parallel with 17 children
             var children = new System.Collections.Generic.List<string>();
             for(int i=0; i<17; i++) 
                children.Add(@"{ ""Type"": ""Action"", ""Action"": ""A"" }");

            string childrenJson = string.Join(",", children);

            string json = $@"{{
                ""TreeName"": ""BigParallel"",
                ""Root"": {{
                    ""Type"": ""Parallel"",
                    ""Policy"": 0,
                    ""Children"": [ {childrenJson} ]
                }}
            }}";
            
            var blob = TreeCompiler.CompileFromJson(json);
            var result = TreeValidator.Validate(blob);
            
            Assert.True(result.IsValid);
            Assert.True(result.HasWarnings);
            Assert.Contains("Parallel has 17 children", result.Warnings[0]);
        }
    }
}
