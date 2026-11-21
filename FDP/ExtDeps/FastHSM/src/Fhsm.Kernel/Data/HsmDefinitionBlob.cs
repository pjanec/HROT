using System;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Container for the immutable ROM definition of a state machine.
    /// helds the defining structures (States, Transitions, Regions, etc.).
    /// </summary>
    public sealed class HsmDefinitionBlob
    {
        public HsmDefinitionHeader Header;

        /// <summary>
        /// Managed metadata sidecar populated by the compiler.
        /// Used by the editor projection layer to recover authoring Guids from flat indices.
        /// </summary>
        public MachineMetadata? Metadata { get; set; }

        /// <summary>
        /// ⭐ <c>CE-2001</c> — the hash of the machine's NAME (set by <c>HsmEmitter</c>); <c>0</c> for an unnamed, hand-built
        /// blob. 📄 <c>docs/blueprints/DESIGN_Unified_Behaviour_Run.md</c> "S8j".
        /// </summary>
        public uint IdentityHash { get; set; }

        /// <summary>
        /// ⭐⭐ <c>CE-2001</c> — <b>which machine at which shape</b>: the value the kernel stamps into
        /// <c>InstanceHeader.MachineId</c> and validates against, and the key of every "which machine" table.
        /// <see cref="HsmDefinitionHeader.StructureHash"/> when <see cref="IdentityHash"/> is 0, else a mix of both (never 0).
        /// <para>⛔ It WAS <see cref="HsmDefinitionHeader.StructureHash"/>, which hashes topology only — so two machines of one
        /// shape shared param-binding seeds and occurrence keys. <c>StructureHash</c> keeps meaning "the shape" (hot reload).</para>
        /// <para>⚠ Computed, not cached: <see cref="Header"/> is a mutable field.</para>
        /// </summary>
        public uint MachineId => IdentityHash == 0 ? Header.StructureHash : CombineMachineId(Header.StructureHash, IdentityHash);

        /// <summary>The <see cref="MachineId"/> mix: a murmur3 finaliser over shape and identity; never 0 (0 = an unbound instance).</summary>
        public static uint CombineMachineId(uint structureHash, uint identityHash)
        {
            unchecked
            {
                uint h = structureHash ^ (identityHash * 0x9E3779B1u);
                h ^= h >> 16; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h == 0 ? 1u : h;
            }
        }
        
        private readonly StateDef[] _states;
        private readonly TransitionDef[] _transitions;
        private readonly RegionDef[] _regions;
        private readonly GlobalTransitionDef[] _globalTransitions;
        private readonly LinkerTableEntry[] _actionTable;
        private readonly LinkerTableEntry[] _guardTable;
        
        public HsmDefinitionBlob()
        {
            _states = Array.Empty<StateDef>();
            _transitions = Array.Empty<TransitionDef>();
            _regions = Array.Empty<RegionDef>();
            _globalTransitions = Array.Empty<GlobalTransitionDef>();
            _actionTable = Array.Empty<LinkerTableEntry>();
            _guardTable = Array.Empty<LinkerTableEntry>();
        }

        // Primary Constructor (Internal/Factory usage)
        private HsmDefinitionBlob(
            HsmDefinitionHeader header,
            StateDef[] states,
            TransitionDef[] transitions,
            RegionDef[] regions,
            GlobalTransitionDef[] globalTransitions,
            LinkerTableEntry[] actionTable,
            LinkerTableEntry[] guardTable)
        {
            Header = header;
            _states = states ?? Array.Empty<StateDef>();
            _transitions = transitions ?? Array.Empty<TransitionDef>();
            _regions = regions ?? Array.Empty<RegionDef>();
            _globalTransitions = globalTransitions ?? Array.Empty<GlobalTransitionDef>();
            _actionTable = actionTable ?? Array.Empty<LinkerTableEntry>();
            _guardTable = guardTable ?? Array.Empty<LinkerTableEntry>();
        }

        public static HsmDefinitionBlob CreateWithLinkerTables(
            HsmDefinitionHeader header,
            StateDef[] states,
            TransitionDef[] transitions,
            RegionDef[] regions,
            GlobalTransitionDef[] globalTransitions,
            LinkerTableEntry[] actionTable,
            LinkerTableEntry[] guardTable)
        {
            return new HsmDefinitionBlob(header, states, transitions, regions, globalTransitions, actionTable, guardTable);
        }

        // Compatibility Constructor (Public)
        public HsmDefinitionBlob(
            HsmDefinitionHeader header,
            StateDef[] states,
            TransitionDef[] transitions,
            RegionDef[] regions,
            GlobalTransitionDef[] globalTransitions,
            ushort[] actionIds,
            ushort[] guardIds)
        {
            Header = header;
            _states = states ?? Array.Empty<StateDef>();
            _transitions = transitions ?? Array.Empty<TransitionDef>();
            _regions = regions ?? Array.Empty<RegionDef>();
            _globalTransitions = globalTransitions ?? Array.Empty<GlobalTransitionDef>();
            
            // Convert ushort[] to LinkerTableEntry[]
            _actionTable = new LinkerTableEntry[actionIds?.Length ?? 0];
            if (actionIds != null)
                for(int i=0; i<actionIds.Length; i++) _actionTable[i] = new LinkerTableEntry { FunctionId = actionIds[i] };

            _guardTable = new LinkerTableEntry[guardIds?.Length ?? 0];
            if (guardIds != null)
                for(int i=0; i<guardIds.Length; i++) _guardTable[i] = new LinkerTableEntry { FunctionId = guardIds[i] };
        }
        
        // Span accessors only
        public ReadOnlySpan<StateDef> States => _states;
        public ReadOnlySpan<TransitionDef> Transitions => _transitions;
        public ReadOnlySpan<RegionDef> Regions => _regions;
        public ReadOnlySpan<GlobalTransitionDef> GlobalTransitions => _globalTransitions;
        public ReadOnlySpan<LinkerTableEntry> ActionTable => _actionTable.AsSpan();
        public ReadOnlySpan<LinkerTableEntry> GuardTable => _guardTable.AsSpan();

        // Indexed accessors with bounds checking
        public ref readonly StateDef GetState(int index)
        {
            if (index < 0 || index >= _states.Length)
                throw new IndexOutOfRangeException($"State index {index} out of range [0..{_states.Length-1}]");
            return ref _states[index];
        }

        public ref readonly TransitionDef GetTransition(int index)
        {
            if (index < 0 || index >= _transitions.Length)
                throw new IndexOutOfRangeException($"Transition index {index} out of range [0..{_transitions.Length-1}]");
            return ref _transitions[index];
        }

        public ref readonly RegionDef GetRegion(int index)
        {
            if (index < 0 || index >= _regions.Length)
                throw new IndexOutOfRangeException($"Region index {index} out of range [0..{_regions.Length-1}]");
            return ref _regions[index];
        }

        public ref readonly GlobalTransitionDef GetGlobalTransition(int index)
        {
            if (index < 0 || index >= _globalTransitions.Length)
                throw new IndexOutOfRangeException($"GlobalTransition index {index} out of range [0..{_globalTransitions.Length-1}]");
            return ref _globalTransitions[index];
        }
    }
}
