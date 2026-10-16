using System.Runtime.CompilerServices;
using Fdp.Core;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ CE-1132 / CE-1136 — the map's small UI states that the interaction systems (<see cref="MapInteractionPack.Build"/>) and the
    /// map's overlay layers (<see cref="MapInteractionPack.AttachMapLayers"/>) share, keyed by the world both are given: the rubber band
    /// (the 3-D view lends it a projection) and the RTS tool's mode (the overlay draws its legend). ⛔ Not world singletons: they are
    /// presentation state, never replicated, saved or recorded, and a managed singleton would need a component id. Weak: a world that goes
    /// takes its states with it.
    /// </summary>
    public static class MapUiStates
    {
        private static readonly ConditionalWeakTable<EntityRepository, RubberBandState> Bands = new();
        private static readonly ConditionalWeakTable<EntityRepository, RtsModeState> Rts = new();

        public static void Set(EntityRepository world, RubberBandState band) => Bands.AddOrUpdate(world, band);
        public static void Set(EntityRepository world, RtsModeState mode) => Rts.AddOrUpdate(world, mode);

        public static RubberBandState? BandOf(EntityRepository? world) => world != null && Bands.TryGetValue(world, out var b) ? b : null;
        public static RtsModeState? RtsOf(EntityRepository? world) => world != null && Rts.TryGetValue(world, out var r) ? r : null;
    }
}
