using System;

namespace Fhsm.Compiler.Graph
{
    public class TransitionNode
    {
        public StateNode? Source { get; set; }
        public StateNode? Target { get; set; }
        
        public ushort EventId { get; set; }
        public string? GuardFunction { get; set; }  // Optional guard
        public ushort GuardId { get; set; } // Added for JSON parser support
        public string? ActionFunction { get; set; }  // Optional action
        public ushort ActionId { get; set; } // Added for JSON parser support
        
        public byte Priority { get; set; } = 128;  // Default normal
        public bool IsInternal { get; set; }  // Internal vs External

        /// <summary>
        /// ⭐⭐ <b>CE-381 — evaluate this transition's guard every quiescent tick, with no event.</b>
        /// ⛔ Distinct from an eventless (completion) transition, which the RTC loop already selects
        /// once after another transition fires. 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c>
        /// §3.1 / §2.3.
        /// </summary>
        public bool IsPolled { get; set; }

        /// <summary>
        /// Stable identity for editor/visualisation tooling.
        /// Auto-generated when not explicitly supplied.
        /// </summary>
        public Guid VisualId { get; set; }
        
        public TransitionNode() { }

        public TransitionNode(StateNode source, StateNode target, ushort eventId)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target; // Allow null initially, set by builder
            EventId = eventId;
        }
    }
}