using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Events;
using Hrot.Common.Interactions;

namespace Hrot.Common.Systems
{
    // Reads GlobalActionRequestedEvent events from the isolated interaction bus and
    // dispatches them to handlers registered in the GlobalActionRegistry.
    // The bus is injected at construction so this system is not coupled to any
    // particular view implementation.
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class GlobalActionDispatchSystem : IEcsModuleSystem
    {
        private readonly GlobalActionRegistry _registry;
        private readonly FdpEventBus _interactionBus;

        public GlobalActionDispatchSystem(GlobalActionRegistry registry, FdpEventBus interactionBus)
        {
            _registry       = registry       ?? throw new System.ArgumentNullException(nameof(registry));
            _interactionBus = interactionBus ?? throw new System.ArgumentNullException(nameof(interactionBus));
        }

        // ⭐ CE-5017 — this frame's requests (reused: a frame with none allocates nothing — R-2084)
        private readonly System.Collections.Generic.List<GlobalActionRequestedEvent> _frame = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            _frame.Clear();
            foreach (ref readonly var evt in _interactionBus.Read<GlobalActionRequestedEvent>()) _frame.Add(evt);
            var world = view as EntityRepository;
            for (int i = 0; i < _frame.Count;)
            {
                // ⭐ CE-5017 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-9) — a PER-ENTITY action fanned out over the selection (A4) is ONE user gesture ⇒ ONE undo step:
                //   a run of the same per-entity action this frame is bracketed (deleting a 4-block row, or ten trees, is one undo, not ten)
                int id = _frame[i].ActionId, j = i + 1;
                if (_frame[i].Relayed == 0 && Hrot.Common.Actions.ActionCatalogue.Shared.RunsPerEntity(id))
                    while (j < _frame.Count && _frame[j].ActionId == id && _frame[j].Relayed == 0) j++;
                bool bracket = j - i > 1;
                if (bracket) Fdp.Toolkit.Replication.Edits.EditSteps.Begin(world, Hrot.Common.Actions.ActionCatalogue.Shared.LabelOf(id));
                try
                {
                    for (int k = i; k < j; k++)
                        if (_registry.TryGetHandler(_frame[k].ActionId, _frame[k].Relayed != 0, out var handler))
                            handler(view, _frame[k].Target);
                }
                finally { if (bracket) Fdp.Toolkit.Replication.Edits.EditSteps.End(world); }
                i = j;
            }
        }
    }
}
