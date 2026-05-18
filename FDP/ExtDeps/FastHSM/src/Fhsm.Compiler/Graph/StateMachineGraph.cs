using System;
using System.Collections.Generic;

namespace Fhsm.Compiler.Graph
{
    /// <summary>
    /// Root container for state machine graph before compilation.
    /// </summary>
    public class StateMachineGraph
    {
        public string Name { get; set; }
        public Guid MachineId { get; set; }
        
        public StateNode RootState { get; set; }
        public Dictionary<string, StateNode> States { get; } = new();
        public List<TransitionNode> GlobalTransitions { get; } = new();
        
        // Event definitions
        public Dictionary<string, ushort> EventNameToId { get; } = new();
        public List<EventDefinition> Events { get; } = new();
        
        // Function registrations
        public HashSet<string> RegisteredActions { get; } = new();
        public HashSet<string> RegisteredGuards { get; } = new();
        
        public StateMachineGraph(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MachineId = Guid.NewGuid();
            
            // Create implicit root
            RootState = new StateNode("__Root");
            States["__Root"] = RootState;
        }
        
        public StateNode AddState(StateNode state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (States.ContainsKey(state.Name))
                throw new InvalidOperationException($"State '{state.Name}' already exists");
            
            States[state.Name] = state;
            return state;
        }

        public StateNode AddState(string name, StateNode? parent)
        {
            var node = new StateNode(name);
            node.Parent = parent;
            
            if (parent != null)
            {
                parent.Children.Add(node);
            }
            else
            {
                RootState.Children.Add(node);
                node.Parent = RootState;
            }

            return AddState(node);
        }

        public StateNode? FindStateByName(string name)
        {
            return FindStateRecursive(RootState, name);
        }

        private StateNode? FindStateRecursive(StateNode node, string name)
        {
            if (node.Name == name) return node;
            
            foreach (var child in node.Children)
            {
                var found = FindStateRecursive(child, name);
                if (found != null) return found;
            }
            
            return null;
        }
        
        public StateNode? FindState(string name)
        {
            return States.TryGetValue(name, out var state) ? state : null;
        }
    }
}