using System;
using System.IO;
using System.Numerics;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;

namespace Fbt.Examples.Console
{
    // Simple blackboard for demo
    public struct DemoBlackboard
    {
        public int PatrolPointX;
        public int PatrolPointY;
        public int EnemyDistance;
        public bool EnemyVisible;
    }
    
    // Simple context for demo
    public struct DemoContext : IAIContext
    {
        public float DeltaTime { get; set; }
        public float Time { get; set; }
        public int FrameCount { get; set; }
        
        // Minimal implementation (stubbed)
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new RaycastResult { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new PathResult { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 1.0f;
        public int GetIntParam(int index) => 1;
    }
    
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("=== FastBTree Console Demo ===\n");
            
            // Path to JSON, adjusting for bin output location
            // We assume running from project root or bin dir, try to find it
            string jsonPath = Path.Combine(AppContext.BaseDirectory, "../../../../../examples/trees/simple-patrol.json");
            jsonPath = Path.GetFullPath(jsonPath);
            
            if (!File.Exists(jsonPath))
            {
               // Fallback try
               jsonPath = "../../../trees/simple-patrol.json";
               if (!File.Exists(jsonPath))
               {
                   System.Console.WriteLine($"Error: Could not find tree file at {Path.GetFullPath(jsonPath)}");
                   return;
               }
            }

            // Load and compile tree
            string json = File.ReadAllText(jsonPath);
            System.Console.WriteLine($"Loading tree from JSON: {jsonPath}");
            
            var blob = TreeCompiler.CompileFromJson(json);
            System.Console.WriteLine($"Tree compiled: {blob.TreeName}");
            System.Console.WriteLine($"  Nodes: {blob.Nodes.Length}");
            System.Console.WriteLine($"  Methods: {blob.MethodNames.Length}");
            System.Console.WriteLine();
            
            // Register actions
            var registry = new ActionRegistry<DemoBlackboard, DemoContext>();
            registry.Register("FindRandomPatrolPoint", FindRandomPatrolPoint);
            registry.Register("MoveToTarget", MoveToTarget);
            
            // Create interpreter
            var interpreter = new Interpreter<DemoBlackboard, DemoContext>(blob, registry);
            
            // Simulate ticks
            var bb = new DemoBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new DemoContext();
            
            System.Console.WriteLine("Executing tree...\n");
            
            // Run for enough frames to cover the Wait(2.0)
            // Time step 0.5s -> 4-5 frames needed
            for (int frame = 0; frame < 10; frame++)
            {
                ctx.Time = frame * 0.5f;
                ctx.DeltaTime = 0.5f;
                ctx.FrameCount = frame;
                
                System.Console.WriteLine($"Frame {frame} (Time: {ctx.Time:F1}s):");
                var result = interpreter.Tick(ref bb, ref state, ref ctx);
                System.Console.WriteLine($"  Result: {result}");
                System.Console.WriteLine($"  Blackboard: Point=({bb.PatrolPointX}, {bb.PatrolPointY})");
                System.Console.WriteLine();
                
                if (result == NodeStatus.Success)
                {
                    System.Console.WriteLine("Tree completed successfully (Sequence finished)!");
                     state.Reset(); // Or just break
                     break;
                }
                
                if (result == NodeStatus.Failure)
                {
                    System.Console.WriteLine("Tree failed.");
                    break;
                }
            }
            
            System.Console.WriteLine("Demo complete!");
        }
        
        static NodeStatus FindRandomPatrolPoint(
            ref DemoBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int paramIndex)
        {
            bb.PatrolPointX = new Random().Next(-100, 100);
            bb.PatrolPointY = new Random().Next(-100, 100);
            System.Console.WriteLine($"  [Action] Found patrol point: ({bb.PatrolPointX}, {bb.PatrolPointY})");
            return NodeStatus.Success;
        }
        
        static NodeStatus MoveToTarget(
            ref DemoBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int paramIndex)
        {
            System.Console.WriteLine($"  [Action] Moving to target: ({bb.PatrolPointX}, {bb.PatrolPointY})");
            return NodeStatus.Success;
        }
    }
}
