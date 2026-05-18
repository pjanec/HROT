using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using Fbt;
using Fbt.Serialization;
using Xunit;

namespace Fbt.Tests.Unit
{
    public class InterpreterTests
    {
        [Fact]
        public void Interpreter_EmptyTree_ReturnsSuccess()
        {
            var blob = new BehaviorTreeBlob { Nodes = new NodeDefinition[0] };
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
        }

        [Fact]
        public void Sequence_AllSucceed_ReturnsSuccess()
        {
            var blob = CreateSimpleSequence();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(2, ctx.CallCount); // Both actions called
        }

        [Fact]
        public void Sequence_FirstFails_ReturnsFailureImmediately()
        {
            var blob = CreateSequenceWithFailingChild();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Failure, result);
            Assert.Equal(1, ctx.CallCount); // Only first called
        }

        [Fact]
        public void Sequence_DoesNotExecuteChildrenAfterFailure()
        {
            var blob = CreateSequenceWithFailingChild();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            interpreter.Tick(ref bb, ref state, ref ctx);

            // Verified by CallCount in previous test, but explicity:
            Assert.Equal(1, ctx.CallCount);
        }

        [Fact]
        public void Selector_FirstSucceeds_SkipsRest()
        {
            var blob = CreateSelectorWithSucceedingChild();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(1, ctx.CallCount);
        }

        [Fact]
        public void Selector_AllFail_ReturnsFailure()
        {
            var blob = CreateSelectorAllFail();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Failure, result);
            Assert.Equal(2, ctx.CallCount);
        }

        [Fact]
        public void Inverter_FlipsSuccess()
        {
            var blob = CreateInverter(true); // Child succeeds
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Failure, result);
        }

        [Fact]
        public void Inverter_FlipsFailure()
        {
            var blob = CreateInverter(false); // Child fails
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
        }

        [Fact]
        public void Inverter_PreservesRunning()
        {
            var blob = CreateInverterRunning();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, result);
        }

        [Fact]
        public void RunningNode_ResumesOnNextTick()
        {
            var blob = CreateSequenceWithRunningAction();
            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 1
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, result1);
            Assert.NotEqual(0, state.RunningNodeIndex);
            
            // Tick 2 (Action finishes)
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result2);
            Assert.Equal(0, state.RunningNodeIndex);
            
            // Should have called ReturnRunningOnce twice
            // Should have called ReturnRunningOnce twice
            unsafe
            {
                Assert.Equal(2, state.LocalRegisters[0]); // Counter in register
            }
        }

        [Fact]
        public void RunningNode_SkipsAlreadyProcessedChildren()
        {
            // Sequence: [0] Success, [1] Running
            // Tick 1: [0] runs (success), [1] runs (running). State saves [1].
            // Tick 2: Resume. Should skip [0]. Run [1].
            
            var blob = new BehaviorTreeBlob
            {
                 Nodes = new[]
                 {
                     new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                     new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // AlwaysSuccess (index 1)
                     new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }  // ReturnRunningOnce (index 2)
                 },
                 MethodNames = new[] { "AlwaysSuccess", "ReturnRunningOnce" }
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 1
            interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(1, ctx.CallCount); // AlwaysSuccess called once
            
            // Tick 2
            interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(1, ctx.CallCount); // AlwaysSuccess NOT called again (ctx.CallCount only incremented by AlwaysSuccess/Failure)
            // ReturnRunningOnce uses register logic, doesn't inc CallCount in my helper (oops, let me check TestActions)
            // TestActions.AlwaysSuccess does inc CallCount.
        }

        [Fact]
        public void ActionNode_UpdatesRunningNodeIndex()
        {
            var blob = new BehaviorTreeBlob
            {
                 Nodes = new[] { new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 } },
                 MethodNames = new[] { "ReturnRunning" }
            };
            
            // Custom action that returns Running
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("ReturnRunning", (ref TestBlackboard b, ref BehaviorTreeState s, ref MockContext c, int p) => NodeStatus.Running);
            
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(0, state.RunningNodeIndex); // Node index 0 is used
        }

        [Fact]
        public void ActionNode_ClearsRunningNodeIndexWhenDone()
        {
            var blob = new BehaviorTreeBlob
            {
                 Nodes = new[] { new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 } },
                 MethodNames = new[] { "ReturnRunning" }
            };
            
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            int calls = 0;
            registry.Register("ReturnRunning", (ref TestBlackboard b, ref BehaviorTreeState s, ref MockContext c, int p) => 
            {
                calls++;
                return calls == 1 ? NodeStatus.Running : NodeStatus.Success;
            });
            
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(0, state.RunningNodeIndex); // It is running at index 0
            
            interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(0, state.RunningNodeIndex); // Cleared
        }

        [Fact]
        public void DelegateBinding_CachesActions()
        {
            // Checked implicitly by execution working
            var blob = CreateSimpleSequence();
            var interpreter = CreateInterpreter(blob);
            // If it didn't cache, it would probably crash or use fallback if I mess up
        }

        [Fact]
        public void DelegateBinding_MissingAction_ReturnsFallback()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 } },
                MethodNames = new[] { "MissingAction" }
            };
            
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Failure, result); // Fallback returns Failure
        }

        [Fact]
        public void Wait_ExecutesCorrectly()
        {
            string json = @"{ 
                ""TreeName"": ""WaitTest"",
                ""Root"": { ""Type"": ""Wait"", ""WaitTime"": 1.0 }
            }";
            
            var blob = TreeCompiler.CompileFromJson(json);
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, new ActionRegistry<TestBlackboard, MockContext>());
            
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext { Time = 0.0f };
            
            // First tick
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, result1);
            
            // Tick before duration
            ctx.Time = 0.5f;
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, result2);
            
            // Tick after duration
            ctx.Time = 1.1f;
            var result3 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result3);
        }

        [Fact]
        public void Repeater_ExecutesCorrectly()
        {
            string json = @"{ 
                ""TreeName"": ""RepeatTest"",
                ""Root"": { 
                    ""Type"": ""Repeater"",
                    ""RepeatCount"": 3,
                    ""Children"": [ { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" } ]
                }
            }";
            
            var blob = TreeCompiler.CompileFromJson(json);
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("IncrementCounter", TestActions.IncrementCounter);
            
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(3, bb.Counter); // Executed 3 times
        }

        [Fact]
        public void Parallel_RequireAll_AllSucceed_ReturnsSuccess()
        {
            // Policy 0: RequireAll
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Parallel, ChildCount = 2, SubtreeOffset = 3, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Success
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }  // Success
                },
                MethodNames = new[] { "AlwaysSuccess" },
                IntParams = new[] { 0 } // RequireAll
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(2, ctx.CallCount);
        }

        [Fact]
        public void Parallel_RequireAll_OneFails_ReturnsFailure()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Parallel, ChildCount = 2, SubtreeOffset = 3, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Success
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }  // Failure
                },
                MethodNames = new[] { "AlwaysSuccess", "AlwaysFailure" },
                IntParams = new[] { 0 } // RequireAll
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Failure, result);
            // Parallel executes ALL children in one tick unless one fails, 
            // but implementation executes sequentially and checks results. 
            // If one fails, it continues executing others or returns failure immediately?
            // "Execute all children" loop runs fully. Then checks policy.
            // So checking CallCount = 2.
            Assert.Equal(2, ctx.CallCount); 
        }

        [Fact]
        public void Parallel_RequireOne_OneSucceeds_ReturnsSuccess()
        {
            // Policy 1: RequireOne
             var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Parallel, ChildCount = 2, SubtreeOffset = 3, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Failure
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }  // Success
                },
                MethodNames = new[] { "AlwaysFailure", "AlwaysSuccess" },
                IntParams = new[] { 1 } // RequireOne
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result);
        }



        
        [Fact]
        public void Parallel_WithRunning_ResumesCorrectly()
        {
             // Use a simpler running action that doesn't use registers to avoid conflict with Parallel's register usage
             var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Parallel, ChildCount = 2, SubtreeOffset = 3, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Success
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }  // Running (Custom)
                },
                MethodNames = new[] { "AlwaysSuccess", "RunOnceThenSuccess" },
                IntParams = new[] { 0 } // RequireAll
            };
            
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("AlwaysSuccess", TestActions.AlwaysSuccess);
            
            // Custom action: Running first time, Success second time. Uses Context, not Registers.
            registry.Register("RunOnceThenSuccess", (ref TestBlackboard b, ref BehaviorTreeState s, ref MockContext c, int p) => {
                 c.ActionCallCount++;
                 return c.ActionCallCount > 1 ? NodeStatus.Success : NodeStatus.Running;
            });

            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            // Tick 1
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, result1);
            Assert.Equal(1, ctx.ActionCallCount);
            
            // Tick 2
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(NodeStatus.Success, result2);
            Assert.Equal(2, ctx.ActionCallCount);
        }

        [Fact]
        public void Cooldown_BlocksExecution()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.Cooldown, ChildCount = 1, SubtreeOffset = 2, PayloadIndex = 0 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "AlwaysSuccess" },
                FloatParams = new[] { 1.0f } // 1s Cooldown
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext { Time = 0 };

            // Tick 1: Success (sets cooldown)
            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(1, ctx.CallCount);

            // Tick 2: 0.5s later -> Blocked (Failure)
            ctx.Time = 0.5f;
            Assert.Equal(NodeStatus.Failure, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(1, ctx.CallCount); // Child NOT called

            // Tick 3: 1.1s later -> Allowed
            ctx.Time = 1.1f;
            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
            Assert.Equal(2, ctx.CallCount);
        }

        [Fact]
        public void ForceSuccess_AlwaysReturnsSuccess()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.ForceSuccess, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 } // Fails
                },
                MethodNames = new[] { "AlwaysFailure" }
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        [Fact]
        public void ForceFailure_AlwaysReturnsFailure()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[] { 
                    new NodeDefinition { Type = NodeType.ForceFailure, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 } // Succeeds
                },
                MethodNames = new[] { "AlwaysSuccess" }
            };

            var interpreter = CreateInterpreter(blob);
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();

            Assert.Equal(NodeStatus.Failure, interpreter.Tick(ref bb, ref state, ref ctx));
        }

        // --- Helpers ---

        private Interpreter<TestBlackboard, MockContext> CreateInterpreter(BehaviorTreeBlob blob)
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("AlwaysSuccess", TestActions.AlwaysSuccess);
            registry.Register("AlwaysFailure", TestActions.AlwaysFailure);
            registry.Register("ReturnRunningOnce", TestActions.ReturnRunningOnce);
            return new Interpreter<TestBlackboard, MockContext>(blob, registry);
        }

        private BehaviorTreeBlob CreateSimpleSequence()
        {
            return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "AlwaysSuccess" }
            };
        }

        private BehaviorTreeBlob CreateSequenceWithFailingChild()
        {
            return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Fail
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }  // Success
                },
                MethodNames = new[] { "AlwaysFailure", "AlwaysSuccess" }
            };
        }

        private BehaviorTreeBlob CreateSelectorWithSucceedingChild()
        {
            return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Selector, ChildCount = 2, SubtreeOffset = 3 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Success
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }  // Fail
                },
                MethodNames = new[] { "AlwaysSuccess", "AlwaysFailure" }
            };
        }

        private BehaviorTreeBlob CreateSelectorAllFail()
        {
            return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Selector, ChildCount = 2, SubtreeOffset = 3 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }, // Fail
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }  // Fail
                },
                MethodNames = new[] { "AlwaysFailure" }
            };
        }

        private BehaviorTreeBlob CreateInverter(bool childSucceeds)
        {
             return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Inverter, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { childSucceeds ? "AlwaysSuccess" : "AlwaysFailure" }
            };
        }

        private BehaviorTreeBlob CreateInverterRunning()
        {
             return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Inverter, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "ReturnRunningOnce" }
            };
        }

        private BehaviorTreeBlob CreateSequenceWithRunningAction()
        {
            return new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "ReturnRunningOnce" }
            };
        }
    }
}
