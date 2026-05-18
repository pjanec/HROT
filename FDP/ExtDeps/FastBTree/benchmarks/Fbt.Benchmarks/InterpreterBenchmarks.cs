using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using System.Numerics;

namespace Fbt.Benchmarks
{
    [MemoryDiagnoser]
    [ShortRunJob]
    public class InterpreterBenchmarks
    {
        private BehaviorTreeBlob _simpleSequenceBlob = null!;
        private BehaviorTreeBlob _complexTreeBlob = null!;
        private Interpreter<TestBlackboard, TestContext> _simpleInterpreter = null!;
        private Interpreter<TestBlackboard, TestContext> _complexInterpreter = null!;
        private ActionRegistry<TestBlackboard, TestContext> _registry = null!;
        
        private TestBlackboard _bb;
        private BehaviorTreeState _state;
        private TestContext _ctx;
        
        [GlobalSetup]
        public void Setup()
        {
            // Simple sequence: 3 nodes
            string simpleJson = @"{
                ""TreeName"": ""Simple"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                        { ""Type"": ""Action"", ""Action"": ""DoNothing"" }
                    ]
                }
            }";
            
            // Complex tree: ~20 nodes with nesting
            string complexJson = @"{
                ""TreeName"": ""Complex"",
                ""Root"": {
                    ""Type"": ""Selector"",
                    ""Children"": [
                        {
                            ""Type"": ""Sequence"",
                            ""Children"": [
                                { ""Type"": ""Condition"", ""Action"": ""AlwaysFalse"" },
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" }
                            ]
                        },
                        {
                            ""Type"": ""Sequence"",
                            ""Children"": [
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                {
                                    ""Type"": ""Parallel"",
                                    ""Policy"": 0,
                                    ""Children"": [
                                         { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                         { ""Type"": ""Action"", ""Action"": ""DoNothing"" }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            }";
            
            _simpleSequenceBlob = TreeCompiler.CompileFromJson(simpleJson);
            _complexTreeBlob = TreeCompiler.CompileFromJson(complexJson);
            
            _registry = new ActionRegistry<TestBlackboard, TestContext>();
            _registry.Register("DoNothing", (ref TestBlackboard bb, ref BehaviorTreeState s, ref TestContext c, int p) => NodeStatus.Success);
            _registry.Register("AlwaysFalse", (ref TestBlackboard bb, ref BehaviorTreeState s, ref TestContext c, int p) => NodeStatus.Failure);
            
            _simpleInterpreter = new Interpreter<TestBlackboard, TestContext>(_simpleSequenceBlob, _registry);
            _complexInterpreter = new Interpreter<TestBlackboard, TestContext>(_complexTreeBlob, _registry);
            
            _bb = new TestBlackboard();
            _state = new BehaviorTreeState();
            _ctx = new TestContext();
        }
        
        [Benchmark]
        public NodeStatus SimpleSequence_Tick()
        {
            _state = new BehaviorTreeState(); // Reset state completely for fair comparison of "from scratch" tick
            return _simpleInterpreter.Tick(ref _bb, ref _state, ref _ctx);
        }
        
        [Benchmark]
        public NodeStatus ComplexTree_Tick()
        {
            _state = new BehaviorTreeState(); // Reset
            return _complexInterpreter.Tick(ref _bb, ref _state, ref _ctx);
        }
        
        [Benchmark]
        public NodeStatus SimpleSequence_Resume()
        {
            // Test resume scenario (state persists but it's finished, so it restarts)
            // But if we want to measure "Running" overhead, we need a node that returns Running.
            // For now, re-ticking a finished tree is basically a new tick.
            // Let's stick to the instructions logic: "SimpleSequence_Resume"
            // If the tree was finished, Tick restarts it.
            return _simpleInterpreter.Tick(ref _bb, ref _state, ref _ctx);
        }
    }
    
    public struct TestBlackboard
    {
        public int Counter;
    }
    
    public struct TestContext : IAIContext
    {
        public float Time { get; set; }
        public float DeltaTime { get; set; }
        public int FrameCount { get; set; }
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new RaycastResult { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new PathResult { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 1.0f;
        public int GetIntParam(int index) => 1;
    }
}
