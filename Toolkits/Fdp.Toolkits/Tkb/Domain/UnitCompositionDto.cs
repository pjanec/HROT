using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ORBAT / unit composition descriptor for commander entities that spawn subordinates.
    /// Stores echelon metadata and the subordinate slot list.
    /// Keep <see cref="CompositionSlotDto"/> in the same file as the root aggregate.
    /// </summary>
    [TkbDescriptor("Gen.UnitComposition")]
    public record UnitCompositionDto
    {
        /// <summary>Organizational echelon label (e.g., "Platoon", "Squad").</summary>
        public string Echelon { get; init; } = "Platoon";

        /// <summary>Whether to automatically spawn subordinates together with the parent entity.</summary>
        public bool AutoCreateChildren { get; init; } = true;

        /// <summary>Ordered list of subordinate slots.</summary>
        public List<CompositionSlotDto> Subordinates { get; init; } = new();
    }

    /// <summary>One subordinate type/count slot within a <see cref="UnitCompositionDto"/>.</summary>
    public record CompositionSlotDto
    {
        /// <summary>TKB GUID (type id) of the subordinate entity type.</summary>
        public ulong TkbTypeGuid { get; init; }

        /// <summary>Number of entities of this type in the slot.</summary>
        public int Count { get; init; }

        /// <summary>
        /// Tactical designation enum value.
        /// 1 = Commander, 2 = SquadLeader, 3 = Wingman.
        /// </summary>
        public ushort Designation { get; init; }
    }
}
