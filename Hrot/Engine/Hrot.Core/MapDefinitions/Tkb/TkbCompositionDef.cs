using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Hrot.Map.Definitions.Tkb
{
    public struct TkbChildSlot
    {
        /// <summary>
        /// Required child TKB type ID.
        /// </summary>
        public long TkbType { get; set; }
        
        /// <summary>
        /// Number of entities of this type (e.g., 4 tanks in a platoon).
        /// </summary>
        public int Count { get; set; }
        
        /// <summary>
        /// Tactical designation of child entities within the parent unit.
        /// <c>Undefined</c> means no commander-subordinate link is created for this slot.
        /// </summary>
        public TacticalDesignation Designation { get; set; }
    }
    
    /// <summary>
    /// Composite unit (ORBAT) definition with subordinate slots.
    /// </summary>
    [ComponentId(GlobalComponentIds.TkbCompositionDef)]
    public class TkbCompositionDef
    {
        /// <summary>
        /// Subordinate entity slots.
        /// Example: Tank Platoon has 4x Tank slots.
        /// </summary>
        public List<TkbChildSlot> Subordinates { get; set; } = new();
        
        /// <summary>
        /// Organizational echelon ("Platoon", "Company", "Battalion").
        /// </summary>
        public string Echelon { get; set; } = "Platoon";
        
        /// <summary>
        /// Whether children are automatically created with parent.
        /// </summary>
        public bool AutoCreateChildren { get; set; } = true;
    }
}
