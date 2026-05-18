using Xunit;
using Fbt.Serialization;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using System.IO;

namespace Fbt.Tests.Integration
{
    public class TreeExecutionTests
    {
        [Fact]
        public void IntegrationTest_SimpleSequence_ExecutesCorrectly()
        {
            // JSON
            string json = @"{
                ""TreeName"": ""TestTree"",
                ""Version"": 1,
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" },
                        { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" }
                    ]
                }
            }";
            
            // Compile
            var blob = TreeCompiler.CompileFromJson(json);
            var validation = TreeValidator.Validate(blob);
            Assert.True(validation.IsValid, "Tree should be valid");
            
            // Execute
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("IncrementCounter", TestActions.IncrementCounter);
            
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(2, bb.Counter); // Both actions executed
        }

        [Fact]
        public void IntegrationTest_SaveLoadBinary_ExecutesSame()
        {
            string json = @"{
                ""TreeName"": ""BinaryTest"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" }
                    ]
                }
            }";
            
            var originalBlob = TreeCompiler.CompileFromJson(json);
            string path = "exec_test.bin";
            
            try
            {
                BinaryTreeSerializer.Save(originalBlob, path);
                var loadedBlob = BinaryTreeSerializer.Load(path);
                
                var registry = new ActionRegistry<TestBlackboard, MockContext>();
                registry.Register("IncrementCounter", TestActions.IncrementCounter);
                
                var interpreter = new Interpreter<TestBlackboard, MockContext>(loadedBlob, registry);
                var bb = new TestBlackboard();
                var state = new BehaviorTreeState();
                var ctx = new MockContext();
                
                var result = interpreter.Tick(ref bb, ref state, ref ctx);
                Assert.Equal(NodeStatus.Success, result);
                Assert.Equal(1, bb.Counter);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
        
        [Fact]
        public void IntegrationTest_ComplexTree_RunningLogic()
        {
            // Root(Seq) -> [Success, RunningOnce]
            string json = @"{
                ""TreeName"": ""RunningTest"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""AlwaysSuccess"" },
                        { ""Type"": ""Action"", ""Action"": ""ReturnRunningOnce"" }
                    ]
                }
            }";
            
            var blob = TreeCompiler.CompileFromJson(json);
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("AlwaysSuccess", TestActions.AlwaysSuccess);
            registry.Register("ReturnRunningOnce", TestActions.ReturnRunningOnce);
            
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
             var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            // First Tick
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, result1);
            Assert.Equal(1, ctx.CallCount); // AlwaysSuccess called
            
            // Second Tick (Resume)
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result2);
            // AlwaysSuccess should NOT be called again (CallCount stays 1)
            // Wait, does interpreter skip?
            // Tick 1: AlwaysSuccess(S), ReturnRun(R). State index = 2 (RunningOnce).
            // Tick 2: Root(Seq) -> Start at 2?
            // Interpreter logic:
            // Seq loop:
            //  Child 0 (Success). Offset=1. Start=1. End=1.
            //  RunningIndex=2. 2 > 1. Skip!
            //  Child 1 (ReturnRun). Execute. Success.
            
            Assert.Equal(1, ctx.CallCount); 
            
            unsafe { Assert.Equal(2, state.LocalRegisters[0]); } // Running Once called twice
        }
    }
}
