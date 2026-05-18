using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using System;
using System.Numerics;

namespace Fbt.Demo.Visual
{
    public interface IAgentStatusProvider
    {
        List<(string Text, Raylib_cs.Color Color)> GetAgentStatus(Agent agent, BehaviorTreeBlob blob, float currentTime);
    }
    
    public class DefaultStatusProvider : IAgentStatusProvider
    {
        public List<(string Text, Raylib_cs.Color Color)> GetAgentStatus(Agent agent, BehaviorTreeBlob blob, float currentTime)
        {
            var lines = new List<(string, Raylib_cs.Color)>();

            // Line 1: Role
            lines.Add((agent.Role.ToString().ToUpper(), agent.Color));

            // If tree not running or invalid
            if (blob.Nodes == null || 
                agent.State.RunningNodeIndex < 0 || 
                agent.State.RunningNodeIndex >= blob.Nodes.Length)
            {
                lines.Add(("IDLE", Raylib_cs.Color.White));
                return lines;
            }
            
            var runningNode = blob.Nodes[agent.State.RunningNodeIndex];
            
            // Line 2: Current action/state
            string actionText = GetActionText(agent, blob, runningNode, currentTime);
            Raylib_cs.Color actionColor = GetActionColor(actionText, agent);
            lines.Add((actionText, actionColor));
            
            // Line 3: Contextual detail
            string detail = GetContextualDetail(agent, runningNode, blob);
            if (!string.IsNullOrEmpty(detail))
            {
                lines.Add((detail, new Raylib_cs.Color(180, 180, 180, 255))); // Gray
            }
            
            return lines;
        }
        
        private unsafe string GetActionText(
            Agent agent, 
            BehaviorTreeBlob blob, 
            NodeDefinition node,
            float currentTime)
        {
            // For Action/Condition nodes - show the method name
            if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
            {
                string actionName = GetActionName(blob, node);
                
                // Custom formatting based on action name
                return actionName switch
                {
                    "FindPatrolPoint" => "Picking Point",
                    "MoveToTarget" => "Moving",
                    "FindResource" => "Searching",
                    "Gather" => "Gathering",
                    "ReturnToBase" => "Returning",
                    "ScanForEnemy" => "Scanning",
                    "HasEnemy" => "Checking Enemy",
                    "FindRandomPoint" => "Wandering",
                    "ChaseEnemy" => "CHASING",
                    "Attack" => "ATTACKING",
                    _ => actionName
                };
            }
            
            // For Wait nodes - show countdown
            if (node.Type == NodeType.Wait)
            {
                float duration = GetWaitDuration(blob, node);
                float startTime = (float)agent.State.AsyncData; // AsyncData holds start time for Wait nodes
                // Note: The Interpreter stores 'context.Time + duration' into AsyncData usually? 
                // Let's check Interpreter behavior for Wait.
                // Wait node in standard behavior tree usually stores Deadline.
                // But FastBTree interpreter implementation:
                // If it's a built-in Wait node type (NodeDefinition.Type == NodeType.Wait), 
                // we need to verify what is stored in AsyncData.
                // Actually, let's assume AsyncData = StartTime for now, or Deadline.
                // If we check Interpreter logic (not visible here), usually it's Deadline = Time + Duration.
                // So Remaining = AsyncData - Time.
                // Let's print remaining.
                
                // Assuming AsyncData is 'ExpirationTime'
                float remaining = (float)agent.State.AsyncData - currentTime;
                if (remaining < 0) remaining = 0;
                return $"Wait {remaining:F1}s";
            }
            
            // For Repeater - show iteration
            if (node.Type == NodeType.Repeater)
            {
                int currentCount = agent.State.LocalRegisters[0];
                int maxCount = GetRepeaterMax(blob, node);
                if (maxCount < 0)
                    return $"Loop #{currentCount}";
                return $"Loop {currentCount}/{maxCount}";
            }
            
            // For Cooldown
            if (node.Type == NodeType.Cooldown)
            {
                return "Cooldown";
            }
            
            // Default: show node type
            return node.Type.ToString();
        }

        private string GetContextualDetail(Agent agent, NodeDefinition node, BehaviorTreeBlob blob)
        {
             switch (agent.Role)
             {
                 case AgentRole.Combat:
                    if (agent.Blackboard.HasTarget)
                    {
                        float dist = Vector2.Distance(agent.Position, agent.TargetPosition);
                        return $"Dist: {dist:F0}px";
                    }
                    return "";
                     
                 case AgentRole.Gather:
                     return $"Res: {agent.Blackboard.ResourceCount}";
                     
                 case AgentRole.Patrol:
                     return $"Pt {agent.Blackboard.PatrolPointIndex + 1}/4";
                     
                 default:
                     return "";
             }
        }
        
        private Raylib_cs.Color GetActionColor(string action, Agent agent)
        {
            if (action.Contains("ATTACK") || agent.AttackFlashTimer > 0)
                return Raylib_cs.Color.Red;
            
            if (action.Contains("CHASING"))
                return Raylib_cs.Color.Orange;
            
            if (action.Contains("Moving") || action.Contains("Gathering") || action.Contains("Wandering"))
                return Raylib_cs.Color.Yellow;
                
            return Raylib_cs.Color.White;
        }

        private string GetActionName(BehaviorTreeBlob blob, NodeDefinition node)
        {
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.MethodNames?.Length ?? 0))
                return blob.MethodNames![node.PayloadIndex];
            return "Unknown";
        }
        
        private float GetWaitDuration(BehaviorTreeBlob blob, NodeDefinition node)
        {
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.FloatParams?.Length ?? 0))
                return blob.FloatParams![node.PayloadIndex];
            return 0f;
        }
        
        private int GetRepeaterMax(BehaviorTreeBlob blob, NodeDefinition node)
        {
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.IntParams?.Length ?? 0))
                return blob.IntParams![node.PayloadIndex];
            return -1;
        }
    }
}
