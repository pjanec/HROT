using System;
using System.Collections.Generic;

namespace Fhsm.Compiler.Graph
{
    /// <summary>
    /// Intermediate representation of a state during compilation.
    /// Mutable graph node before flattening.
    /// </summary>
    public class StateNode
    {
        public Guid StableId { get; set; }  // For hot reload stability
        public string Name { get; set; }
        public StateNode? Parent { get; set; }
        
        public List<StateNode> Children { get; } = new();
        public List<TransitionNode> Transitions { get; } = new();
        public List<RegionNode> Regions { get; } = new();
        
        // State configuration
        public bool IsInitial { get; set; }
        public bool IsHistory { get; set; }
        public bool IsDeepHistory { get; set; }
        public bool IsParallel { get; set; }
        public bool IsFinal { get; set; }
        
        // Actions (function names - resolved later)
        public string? OnEntryAction { get; set; }
        public ushort EntryActionId { get; set; } // Added for JSON parser support
        public string? OnExitAction { get; set; }
        public ushort ExitActionId { get; set; } // Added for JSON parser support
        public string? ActivityAction { get; set; }
        public string? TimerAction { get; set; }
        
        // Computed during flattening
        public ushort FlatIndex { get; set; } = 0xFFFF;
        public ushort HistorySlotIndex { get; set; } = 0xFFFF;  // 0xFFFF = no history
        public int TimerSlotIndex { get; set; } = -1;  // Added for validator support (-1 = none)
        public byte Depth { get; set; }
        public byte OutputLaneMask { get; set; } // Added for Task 8
        
        public StateNode(string name, Guid? stableId = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            StableId = stableId ?? Guid.NewGuid();
        }
        
        public void AddChild(StateNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            child.Parent = this;
            Children.Add(child);
        }
        
        public void AddTransition(TransitionNode transition)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));
            Transitions.Add(transition);
        }
    }
}