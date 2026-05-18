using ImGuiNET;
using Fbt;
using Fbt.Serialization;
using Fbt.Runtime;
using System;
using System.Numerics;

namespace Fbt.Demo.Visual.UI
{
    public class NodeDetailPanel
    {
        private int? _selectedNodeIndex = null;
        
        public void Render(Agent agent, BehaviorTreeBlob blob, float currentTime)
        {
            if (_selectedNodeIndex == null) return;
            
            ImGui.Begin("Node Details");
            
            int nodeIndex = _selectedNodeIndex.Value;
            if (blob.Nodes != null && nodeIndex >= 0 && nodeIndex < blob.Nodes.Length)
            {
                RenderNodeDetails(agent, blob, nodeIndex, currentTime);
            }
            else
            {
                ImGui.Text("Invalid node index");
            }
            
            if (ImGui.Button("Close"))
            {
                _selectedNodeIndex = null;
            }
            
            ImGui.End();
        }
        
        private unsafe void RenderNodeDetails(Agent agent, BehaviorTreeBlob blob, int nodeIndex, float currentTime)
        {
            var node = blob.Nodes[nodeIndex];
            
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), 
                $"Node [{nodeIndex}] - {node.Type}");
            
            ImGui.Separator();
            
            // Basic properties
            ImGui.Text($"Type: {node.Type}");
            ImGui.Text($"ChildCount: {node.ChildCount}");
            ImGui.Text($"SubtreeOffset: {node.SubtreeOffset}");
            ImGui.Text($"PayloadIndex: {node.PayloadIndex}");
            
            ImGui.Separator();
            
            // Type-specific details
            switch (node.Type)
            {
                case NodeType.Action:
                case NodeType.Condition:
                    RenderActionDetails(blob, node, agent);
                    break;
                    
                case NodeType.Wait:
                case NodeType.Cooldown:
                    RenderWaitDetails(blob, node, agent.State, currentTime);
                    break;
                    
                case NodeType.Repeater:
                    RenderRepeaterDetails(blob, node, agent.State);
                    break;
                    
                case NodeType.Parallel:
                    RenderParallelDetails(blob, node, agent.State);
                    break;
            }
            
            // Execution state
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 1f, 0f, 1f), "Execution State");
            
            bool isRunning = agent.State.RunningNodeIndex == nodeIndex;
            ImGui.Text($"Is Running: {isRunning}");
            
            if (isRunning)
            {
                ImGui.Indent();
                ImGui.Text($"AsyncData: {agent.State.AsyncData}");
                ImGui.Text("LocalRegisters:");
                for (int i = 0; i < 4; i++)
                {
                    ImGui.Text($"  [{i}] = {agent.State.LocalRegisters[i]}");
                }
                ImGui.Unindent();
            }
        }

        private void RenderActionDetails(BehaviorTreeBlob blob, NodeDefinition node, Agent agent)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Action Details");
            
            string methodName = "Unknown";
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.MethodNames?.Length ?? 0))
            {
                methodName = blob.MethodNames![node.PayloadIndex];
            }
            
            ImGui.Text($"Method: {methodName}");
            
            // Show relevant blackboard fields based on action
            ImGui.Text("Relevant State:");
            ImGui.Indent();
            
            switch (methodName)
            {
                case "ChaseEnemy":
                case "Attack":
                case "HasEnemy":
                    ImGui.Text($"HasTarget: {agent.Blackboard.HasTarget}");
                    ImGui.Text($"TargetAgentId: {agent.Blackboard.TargetAgentId}");
                    if (agent.Blackboard.HasTarget)
                    {
                        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), 
                            $"  → Tracking Agent #{agent.Blackboard.TargetAgentId}");
                    }
                    ImGui.Text($"TargetPosition: ({agent.TargetPosition.X:F0}, {agent.TargetPosition.Y:F0})");
                    break;
                    
                case "FindPatrolPoint":
                case "MoveToTarget":
                case "FindRandomPoint":
                    ImGui.Text($"PatrolPointIndex: {agent.Blackboard.PatrolPointIndex}");
                    ImGui.Text($"TargetPosition: ({agent.TargetPosition.X:F0}, {agent.TargetPosition.Y:F0})");
                    ImGui.Text($"CurrentPosition: ({agent.Position.X:F0}, {agent.Position.Y:F0})");
                    float dist = Vector2.Distance(agent.Position, agent.TargetPosition);
                    ImGui.Text($"Distance to Target: {dist:F1}");
                    break;
                    
                case "Gather":
                    ImGui.Text($"ResourceCount: {agent.Blackboard.ResourceCount}");
                    break;
                    
                case "ScanForEnemy":
                    ImGui.Text($"HasTarget: {agent.Blackboard.HasTarget}");
                    ImGui.Text("Scan result will update HasTarget");
                    break;
            }
            
            ImGui.Unindent();
        }

        private void RenderWaitDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state, float currentTime)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Wait Details");
            
            float duration = 0f;
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.FloatParams?.Length ?? 0))
            {
                duration = blob.FloatParams![node.PayloadIndex];
            }
            
            // AsyncData holds Expiration Time (Time + Duration) usually, or Start Time.
            // Let's assume Expiration Time based on likely implementation.
            // If AsyncData > Big Number, likely absolute time.
            float expirationTime = (float)state.AsyncData;
            float remaining = expirationTime - currentTime;
            if (remaining < 0) remaining = 0;
            if (remaining > duration) remaining = duration; // Clamping if just started or AsyncData not set? 
            
            // Actually if not running, this data might be stale.
            
            float elapsed = duration - remaining;
            
            ImGui.Text($"Duration: {duration:F2}s");
            ImGui.Text($"Elapsed: {elapsed:F2}s");
            ImGui.Text($"Remaining: {remaining:F2}s");
            
            float progress = duration > 0 ? MathF.Min(1f, elapsed / duration) : 0;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress * 100:F0}%");
        }
        
        private unsafe void RenderRepeaterDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Repeater Details");
            
            int maxCount = -1;
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.IntParams?.Length ?? 0))
            {
                maxCount = blob.IntParams![node.PayloadIndex];
            }
            
            int currentCount = state.LocalRegisters[0];
            
            ImGui.Text($"Max Count: {(maxCount < 0 ? "Infinite" : maxCount.ToString())}");
            ImGui.Text($"Current Iteration: {currentCount}");
            ImGui.Text($"Uses: LocalRegisters[0]");
            
            if (maxCount > 0)
            {
                float progress = (float)currentCount / maxCount;
                ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{currentCount}/{maxCount}");
            }
        }
        
        private unsafe void RenderParallelDetails(BehaviorTreeBlob blob, NodeDefinition node, BehaviorTreeState state)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Parallel Details");
            
            int policy = 0;
            if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.IntParams?.Length ?? 0))
            {
                policy = blob.IntParams![node.PayloadIndex];
            }
            
            ImGui.Text($"Policy: {(policy == 0 ? "RequireAll" : "RequireOne")}");
            ImGui.Text($"Child Count: {node.ChildCount}");
            ImGui.Text($"Uses: LocalRegisters[3] (bitfield)");
            
            // Decode bitfield
            int bitfield = state.LocalRegisters[3];
            ImGui.Text("Child States:");
            ImGui.Indent();
            
            for (int i = 0; i < node.ChildCount && i < 16; i++)
            {
                bool finished = (bitfield & (1 << i)) != 0;
                bool success = (bitfield & (1 << (i + 16))) != 0;
                
                string status = finished 
                    ? (success ? "✓ Success" : "✗ Failure")
                    : "⋯ Running";
                
                Vector4 color = finished
                    ? (success ? new Vector4(0.3f, 1f, 0.3f, 1f) : new Vector4(1f, 0.3f, 0.3f, 1f))
                    : new Vector4(1f, 1f, 0.5f, 1f);
                
                ImGui.TextColored(color, $"  Child {i}: {status}");
            }
            
            ImGui.Unindent();
        }
        
        public void SetSelectedNode(int? nodeIndex)
        {
            _selectedNodeIndex = nodeIndex;
        }
        
        public bool IsNodeSelected(int nodeIndex)
        {
            return _selectedNodeIndex == nodeIndex;
        }
    }
}
