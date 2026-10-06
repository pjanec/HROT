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

        /// <summary>
        /// ⭐ CE-1003 (Q84 A0) — for a child of a PARALLEL state: the declared orthogonal region it belongs to.
        /// <c>null</c> = not declared, and that child is a region of its own (the pre-CE-1003 rule, kept for
        /// hand-written machines). Children sharing an index form ONE region — a sub-state machine whose initial
        /// state is the member marked <see cref="IsInitial"/> (else the first member). 📄 FastHSM design §2.2–§2.4
        /// (<c>"regions": [{name, initial}]</c>); docs/blueprints/Architect_Question_84 §6.
        /// </summary>
        public int? RegionIndex { get; set; }
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

        /// <summary>
        /// ⭐⭐ <b><c>CE-383</c> — an EXPLICIT activity action id, overriding the name hash.</b>
        /// ⛔ <c>0</c> means "unset", exactly as for <see cref="EntryActionId"/>/<see cref="ExitActionId"/>,
        /// which have carried this shape since the JSON parser needed it.
        ///
        /// <para>⭐ <b>Why it exists:</b> a blueprint-hosted thunk registers under its
        /// <c>BlueprintId</c> — FNV-1a32 of the asset GUID — while a named action resolves through
        /// <c>FNV1a16(FQN)</c>. No authorable STRING bridges those two id spaces, so the id has to be
        /// baked. 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.2.</para>
        /// </summary>
        public ushort ActivityActionId { get; set; }
        public string? TimerAction { get; set; }
        
        // Computed during flattening
        public ushort FlatIndex { get; set; } = 0xFFFF;
        public ushort HistorySlotIndex { get; set; } = 0xFFFF;  // 0xFFFF = no history
        public int TimerSlotIndex { get; set; } = -1;  // Added for validator support (-1 = none)
        public byte Depth { get; set; }
        public byte OutputLaneMask { get; set; } // Added for Task 8
        
        // Event IDs that this state defers (populated by HsmBuilder.StateBuilder.DeferEvent).
        public List<ushort> DeferredEventIds { get; } = new();

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