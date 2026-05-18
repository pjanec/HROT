using System;
using Xunit;
using Fhsm.Compiler.IO;
using Fhsm.Compiler.Graph;

namespace Fhsm.Tests.Compiler
{
    public class JsonParserTests
    {
        [Fact]
        public void Parse_Simple_StateMachine()
        {
            var json = @"
            {
                ""name"": ""TestMachine"",
                ""states"": [
                    { ""name"": ""Idle"" },
                    { ""name"": ""Active"" }
                ],
                ""transitions"": [
                    { ""source"": ""Idle"", ""target"": ""Active"", ""event"": 1 }
                ]
            }";
            
            var parser = new JsonStateMachineParser();
            var graph = parser.Parse(json);
            
            Assert.Equal("TestMachine", graph.Name);
            // Root has __Root and its children. The parser adds states to graph.
            // If parser adds directly via AddState(name, parent), parent is null -> child of RootState.
            Assert.Equal(2, graph.RootState.Children.Count);
            
            var idle = graph.FindStateByName("Idle");
            Assert.NotNull(idle);
            Assert.Single(idle.Transitions);
        }

        [Fact]
        public void Parse_Nested_States()
        {
            var json = @"
            {
                ""name"": ""TestMachine"",
                ""states"": [
                    { 
                        ""name"": ""Parent"",
                        ""children"": [
                            { ""name"": ""Child1"" },
                            { ""name"": ""Child2"" }
                        ]
                    }
                ]
            }";
            
            var parser = new JsonStateMachineParser();
            var graph = parser.Parse(json);
            
            var parent = graph.FindStateByName("Parent");
            Assert.NotNull(parent);
            Assert.Equal(2, parent.Children.Count);
        }
    }
}
