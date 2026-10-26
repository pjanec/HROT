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
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, a mutable class: recorded and saved, kept out of the background snapshot (the convention it got by default)
    public record TkbCompositionDef
    {
        /// <summary>
        /// Subordinate entity slots.
        /// Example: Tank Platoon has 4x Tank slots.
        /// </summary>
        public IReadOnlyList<TkbChildSlot> Subordinates { get; init; } = System.Array.Empty<TkbChildSlot>();
        
        /// <summary>
        /// Organizational echelon ("Platoon", "Company", "Battalion").
        /// </summary>
        public string Echelon { get; init; } = "Platoon";

        /// <summary>
        /// ⭐ CE-2125 (R-272 §7.2 L2) — how long the unit's commander gives no new orders after its leader soldier falls (s).
        /// </summary>
        public float CommandDisruptionSeconds { get; init; } = 6f;
        
        /// <summary>
        /// Whether children are automatically created with parent.
        /// </summary>
        public bool AutoCreateChildren { get; init; } = true;
    }

    /// <summary>
    /// Mutable authoring surface for <see cref="TkbCompositionDef"/> (CE-4046: the component is an immutable record, so a
    /// TKB builder's <c>configure</c> lambda fills this and the builder freezes it with <see cref="Build"/>).
    /// </summary>
    public sealed class TkbCompositionDefBuilder
    {
        public List<TkbChildSlot> Subordinates { get; } = new();
        public string Echelon { get; set; } = "Platoon";
        public float CommandDisruptionSeconds { get; set; } = 6f;
        public bool AutoCreateChildren { get; set; } = true;

        /// <summary>Freezes the authored values into the immutable component.</summary>
        public TkbCompositionDef Build() => new TkbCompositionDef
        {
                Subordinates = Subordinates.ToArray(),
                Echelon = Echelon,
                CommandDisruptionSeconds = CommandDisruptionSeconds,
                AutoCreateChildren = AutoCreateChildren,
        };
    }
}
