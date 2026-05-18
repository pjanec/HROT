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
