using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual.UI
{
    public unsafe class StateMachineVisualizer
    {
        public void Render(Agent agent, HsmDefinitionBlob blob, MachineMetadata? metadata, float time)
        {
            ImGui.SetNextWindowSize(new Vector2(400, 600), ImGuiCond.FirstUseEver);
            ImGui.Begin($"State Machine: Agent {agent.Id}");
            
            // Header
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Agent {agent.Id}");
            ImGui.SameLine();
            ImGui.Text($"({agent.Role})");
            
            ImGui.Separator();
            
            // Basic info
            ImGui.Text($"Machine: {agent.MachineName}");
            ImGui.Text($"Position: ({agent.Position.X:F0}, {agent.Position.Y:F0})");
            ImGui.Text($"Target: ({agent.TargetPosition.X:F0}, {agent.TargetPosition.Y:F0})");
            
            ImGui.Separator();
            
            // Active states
            RenderActiveStates(agent, blob, metadata);
            
            ImGui.Separator();
            
            // State hierarchy
            RenderStateHierarchy(agent, blob, metadata);
            
            ImGui.Separator();
            
            // Context data
            RenderContext(agent);
            
            ImGui.Separator();
            
            // Transition history
            RenderTransitionHistory(agent, metadata);
            
            ImGui.Separator();
            
            // Manual event controls
            RenderEventControls(agent);
            
            ImGui.End();
        }
        
        private string GetStateName(ushort id, MachineMetadata? metadata)
        {
            if (metadata != null && metadata.StateNames.TryGetValue(id, out var name))
            {
                return $"{name} ({id})";
            }
            return $"State {id}";
        }
        
        private string GetEventName(ushort id, MachineMetadata? metadata)
        {
            if (metadata != null && metadata.EventNames.TryGetValue(id, out var name))
            {
                return $"{name} ({id})";
            }
            return $"Event {id}";
        }
        
        private void RenderActiveStates(Agent agent, HsmDefinitionBlob blob, MachineMetadata? metadata)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Active States:");
            
            if (agent.ActiveStates != null && agent.ActiveStates.Length > 0)
            {
                foreach (var stateId in agent.ActiveStates)
                {
                    if (stateId == 0xFFFF) continue;
                    ImGui.BulletText(GetStateName(stateId, metadata));
                }
            }
            else
            {
                ImGui.TextDisabled("(none)");
            }
        }
        
        private void RenderStateHierarchy(Agent agent, HsmDefinitionBlob blob, MachineMetadata? metadata)
        {
            if (ImGui.CollapsingHeader("State Hierarchy", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                // Find root state(s)
                for (ushort i = 0; i < blob.Header.StateCount; i++)
                {
                    var state = blob.States[i];
                    if (state.ParentIndex == 0xFFFF)
                    {
                        RenderStateNode(blob, i, agent.ActiveStates, 0, metadata);
                    }
                }
                
                ImGui.Unindent();
            }
        }
        
        private void RenderStateNode(HsmDefinitionBlob blob, ushort stateId, ushort[] activeStates, int depth, MachineMetadata? metadata)
        {
            var state = blob.States[stateId];
            bool isActive = activeStates != null && Array.Exists(activeStates, s => s == stateId);
            
            var color = isActive ? new Vector4(0, 1, 0, 1) : new Vector4(0.7f, 0.7f, 0.7f, 1);
            var prefix = new string(' ', depth * 2);
            var stateName = GetStateName(stateId, metadata);
            
            bool hasChildren = state.FirstChildIndex != 0xFFFF;
            
            if (hasChildren)
            {
                bool nodeOpen = ImGui.TreeNodeEx($"{prefix}{stateName}##node{stateId}", 
                    isActive ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
                
                if (isActive)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), " ◀ ACTIVE");
                }
                
                if (nodeOpen)
                {
                    // Render children
                    ushort childId = state.FirstChildIndex;
                    for (int i = 0; i < state.ChildCount; i++)
                    {
                        RenderStateNode(blob, (ushort)(childId + i), activeStates, depth + 1, metadata);
                    }
                    
                    ImGui.TreePop();
                }
            }
            else
            {
                ImGui.TextColored(color, $"{prefix}└ {stateName}");
                
                if (isActive)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), " ◀ ACTIVE");
                }
            }
        }
        
        private void RenderContext(Agent agent)
        {
            if (ImGui.CollapsingHeader("Context Data"))
            {
                ImGui.Indent();
                
                ImGui.Text($"PatrolPointIndex: {agent.Context.PatrolPointIndex}");
                ImGui.Text($"ResourceCount: {agent.Context.ResourceCount}");
                ImGui.Text($"HasTarget: {agent.Context.HasTarget}");
                ImGui.Text($"TargetAgentId: {agent.Context.TargetAgentId}");
                ImGui.Text($"DistanceToTarget: {agent.Context.DistanceToTarget:F2}");
                
                ImGui.Unindent();
            }
        }
        
        private void RenderTransitionHistory(Agent agent, MachineMetadata? metadata)
        {
            if (ImGui.CollapsingHeader("Recent Transitions"))
            {
                ImGui.Indent();
                
                if (agent.RecentTransitions.Count == 0)
                {
                    ImGui.TextDisabled("(no transitions yet)");
                }
                else
                {
                    foreach (var trans in agent.RecentTransitions.TakeLast(10).Reverse())
                    {
                        var from = GetStateName(trans.FromState, metadata);
                        var to = GetStateName(trans.ToState, metadata);
                        var evt = GetEventName(trans.EventId, metadata);
                        ImGui.Text($"{trans.Timestamp:F2}s: {from} → {to} ({evt})");
                    }
                }
                
                ImGui.Unindent();
            }
        }
        
        private void RenderEventControls(Agent agent)
        {
            if (ImGui.CollapsingHeader("Manual Events"))
            {
                ImGui.Indent();
                
                if (ImGui.Button("Trigger TimerExpired"))
                    InjectEvent(agent, MachineDefinitions.TimerExpired);
                
                if (ImGui.Button("Trigger EnemyDetected"))
                    InjectEvent(agent, MachineDefinitions.EnemyDetected);
                
                if (ImGui.Button("Trigger EnemyLost"))
                    InjectEvent(agent, MachineDefinitions.EnemyLost);
                
                if (ImGui.Button("Trigger Arrived"))
                    InjectEvent(agent, MachineDefinitions.Arrived);
                
                ImGui.Unindent();
            }
        }
        
        private void InjectEvent(Agent agent, ushort eventId)
        {
            var evt = new HsmEvent 
            { 
                EventId = eventId,
                Priority = EventPriority.Normal 
            };
            
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(inst, 64, evt);
            }
        }
    }
}
