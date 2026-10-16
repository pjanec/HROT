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
        private static readonly ConditionalWeakTable<EntityRepository, Fdp.Toolkit.Vis2D.MapCanvas> Canvases = new();

        public static void Set(EntityRepository world, RubberBandState band) => Bands.AddOrUpdate(world, band);
        public static void Set(EntityRepository world, RtsModeState mode) => Rts.AddOrUpdate(world, mode);

        public static RubberBandState? BandOf(EntityRepository? world) => world != null && Bands.TryGetValue(world, out var b) ? b : null;
        public static RtsModeState? RtsOf(EntityRepository? world) => world != null && Rts.TryGetValue(world, out var r) ? r : null;

        /// <summary>⭐ CE-1134 — the map canvas showing <paramref name="world"/> (its current camera is 2-D or 3-D).</summary>
        public static void Set(EntityRepository world, Fdp.Toolkit.Vis2D.MapCanvas canvas) => Canvases.AddOrUpdate(world, canvas);
        public static Fdp.Toolkit.Vis2D.MapCanvas? CanvasOf(EntityRepository? world) => world != null && Canvases.TryGetValue(world, out var c) ? c : null;
    }

    /// <summary>
    /// ⭐ CE-1134 — keeps <see cref="MapUiStates.CanvasOf"/> pointing at the canvas that shows the world NOW (a host may swap its world —
    /// a scenario reload, the ReplayBrowser's active recording), so the map menu's Center View moves the camera the operator sees.
    /// </summary>
    public sealed class MapCanvasLink : Fdp.Toolkit.Vis2D.Abstractions.IMapLayer
    {
        private readonly Fdp.Toolkit.Vis2D.MapCanvas _canvas;
        private readonly System.Func<EntityRepository?> _world;

        public MapCanvasLink(Fdp.Toolkit.Vis2D.MapCanvas canvas, System.Func<EntityRepository?> world)
        {
            _canvas = canvas ?? throw new System.ArgumentNullException(nameof(canvas));
            _world = world ?? throw new System.ArgumentNullException(nameof(world));
        }

        public string Name => "MapCanvasLink";
        public int LayerBitIndex => -1;

        public void Update(float dt)
        {
            if (_world() is { } w && !ReferenceEquals(MapUiStates.CanvasOf(w), _canvas)) MapUiStates.Set(w, _canvas);
        }

        public void Draw(Fdp.Toolkit.Vis2D.Abstractions.RenderContext ctx) { }
        public void DrawOverlay(Fdp.Toolkit.Vis2D.Abstractions.RenderContext ctx) { }
        public bool HandleInput(System.Numerics.Vector2 worldPos, Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(System.Numerics.Vector2 worldPos) => null;
    }
}
