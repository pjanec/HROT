using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using System.Numerics;
using System.Collections.Generic;
using System;

namespace Fbt.Demo.Visual
{
    public class BehaviorSystem
    {
        private Dictionary<string, Interpreter<AgentBlackboard, DemoContext>> _interpreters;
        private ActionRegistry<AgentBlackboard, DemoContext> _registry;
        private Random _random = new Random();
        
        public BehaviorSystem(Dictionary<string, BehaviorTreeBlob> trees)
        {
            _registry = new ActionRegistry<AgentBlackboard, DemoContext>();
            RegisterActions();
            
            _interpreters = new Dictionary<string, Interpreter<AgentBlackboard, DemoContext>>();
            foreach (var key in trees.Keys)
            {
                var blob = trees[key];
                // Check if blob is valid, otherwise it might crash
                if (blob.Nodes != null && blob.Nodes.Length > 0)
                {
                    _interpreters[key] = new Interpreter<AgentBlackboard, DemoContext>(blob, _registry);
                }
            }
        }
        
        private void RegisterActions()
        {
            Console.WriteLine("Registering Actions...");
            // Patrol actions
            _registry.Register("FindPatrolPoint", FindPatrolPoint);
            _registry.Register("MoveToTarget", MoveToTarget);
            _registry.Register("Wait", Wait);
            
            // Gather actions
            _registry.Register("FindResource", FindResource);
            _registry.Register("Gather", Gather);
            _registry.Register("ReturnToBase", ReturnToBase);
            
            // Combat actions
            _registry.Register("ScanForEnemy", ScanForEnemy);
            _registry.Register("HasEnemy", HasEnemy); // Condition
            _registry.Register("ChaseEnemy", ChaseEnemy);
            _registry.Register("Attack", Attack);
            _registry.Register("FindRandomPoint", FindRandomPoint);
        }

        // ... [Existing Action implementations] ...

        private NodeStatus HasEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
            return bb.HasTarget ? NodeStatus.Success : NodeStatus.Failure;
        }

        private NodeStatus FindRandomPoint(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
             var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
             ctx.Agent.TargetPosition = target;
             return NodeStatus.Success;
        }
        
        private NodeStatus Attack(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
            // Visual feedback - white flash effect
            ctx.Agent.AttackFlashTimer = 0.3f; // Flash for 300ms
            
            // Simulate attack action - lose target after attack
            bb.HasTarget = false;
            bb.TargetAgentId = -1;
            
            return NodeStatus.Success;
        }
        
        private List<Agent>? _currentAgents;

        public void Update(List<Agent> agents, float time, float dt)
        {
            _currentAgents = agents;
            var context = new DemoContext { Time = time, DeltaTime = dt };
            
            foreach (var agent in agents)
            {
                // Update attack flash timer
                if (agent.AttackFlashTimer > 0)
                {
                    agent.AttackFlashTimer -= dt;
                }
                
                if (!_interpreters.TryGetValue(agent.TreeName, out var interpreter))
                    continue;
                    
                context.Agent = agent;
                
                // Execute behavior tree
                var status = interpreter.Tick(
                    ref agent.Blackboard,
                    ref agent.State,
                    ref context);
                
                // Highlight current node for visualization
                if (agent.State.RunningNodeIndex > 0) // 0 is usually Root or invalid if uninitialized? 
                // Actually root is 0. If running index is valid index.
                // If status is Success/Failure, running node might be reset or point to finished node?
                // Actually Interpreter resets RunningNodeIndex if hierarchy finishes. 
                // However, we want to know what WAS running.
                // The interpreter state basically tracks the *interrupted* node if Running.
                
                if (status == NodeStatus.Running)
                {
                    agent.CurrentNode = new TreeExecutionHighlight
                    {
                        NodeIndex = agent.State.RunningNodeIndex,
                        Status = NodeStatus.Running,
                        Timestamp = time
                    };
                }
                else
                {
                    // Tree finished this frame, maybe clear or keep last
                    // agent.CurrentNode = null; 
                }
            }
        }
        
        // Action implementations
        private NodeStatus FindPatrolPoint(
            ref AgentBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int payload)
        {
            // Simple patrol point selection
            // We need screen dimensions. Hardcoding or passing them? 
            // Let's assume 1280x720.
            var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            ctx.Agent.TargetPosition = target;
            bb.PatrolPointIndex = (bb.PatrolPointIndex + 1) % 4;
            // Console.WriteLine($"Agent {ctx.Agent.Id} found patrol point: {target}");
            return NodeStatus.Success;
        }
        
        private NodeStatus MoveToTarget(
            ref AgentBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int payload)
        {
            var distance = Vector2.Distance(ctx.Agent.Position, ctx.Agent.TargetPosition);
            
            if (distance < 10f)
                return NodeStatus.Success;
            
            return NodeStatus.Running;
        }

        // Just a dummy wait action if needed as explicit Action, although Type:Wait is preferred
        private NodeStatus Wait(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
            return NodeStatus.Success;
        }

        private NodeStatus FindResource(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
             ctx.Agent.TargetPosition = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
             return NodeStatus.Success;
        }

        private NodeStatus Gather(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
            bb.ResourceCount++;
            return NodeStatus.Success;
        }

        private NodeStatus ReturnToBase(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
             ctx.Agent.TargetPosition = new Vector2(50, 50); // Base at top left
             return NodeStatus.Success;
        }

        private NodeStatus ScanForEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
            // Find closest non-combat agent
            float closestDistSq = 300f * 300f; // 300px range
            Agent? target = null;
            
            // We need access to all agents. 
            // Since DemoContext contains only current agent, we need to pass the list to Update 
            // and maybe store it in BehaviorSystem temporarily during Update?
            // HACK: For now, I'll rely on a static/shared list or modify context.
            // But let's assume we can access them. 
            // Actually, we pass List<Agent> to BehaviorSystem.Update.
            // We can store it in a field _currentAgents during Update.
            
            if (_currentAgents != null)
            {
                foreach (var other in _currentAgents)
                {
                    if (other == ctx.Agent) continue;
                    if (other.Role == AgentRole.Combat) continue; // Don't fight other combatants for now
                    
                    float dSq = Vector2.DistanceSquared(ctx.Agent.Position, other.Position);
                    if (dSq < closestDistSq)
                    {
                        closestDistSq = dSq;
                        target = other;
                    }
                }
            }

            if (target != null)
            {
                bb.HasTarget = true;
                bb.TargetAgentId = target.Id;
                ctx.Agent.TargetPosition = target.Position; // Initial spot
                return NodeStatus.Success;
            }
            
            // Random chance to "hear" something if no visual contact
            if (_random.NextDouble() < 0.10) 
            {
                 // Move to random spot to investigate
                 bb.HasTarget = true; // False positive or sound
                 bb.TargetAgentId = -1; // Unknown
                 ctx.Agent.TargetPosition = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
                 return NodeStatus.Success;
            }

            return NodeStatus.Success; // Keep patrolling
        }

        private NodeStatus ChaseEnemy(ref AgentBlackboard bb, ref BehaviorTreeState state, ref DemoContext ctx, int payload)
        {
             if (!bb.HasTarget) return NodeStatus.Failure;
             
             // Reuse Move logic
             var distance = Vector2.Distance(ctx.Agent.Position, ctx.Agent.TargetPosition);
            if (distance < 30f) return NodeStatus.Success; // Wider attack range to ensure it triggers
            return NodeStatus.Running;
        }


    }
    
    public struct DemoContext : IAIContext
    {
        public float Time { get; set; }
        public float DeltaTime { get; set; }
        public Agent Agent { get; set; }
        public int FrameCount { get; set; } // Needed by IAIContext

        
        // IAIContext implementation
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new() { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new() { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 1f;
        public int GetIntParam(int index) => 1;
    }
}
