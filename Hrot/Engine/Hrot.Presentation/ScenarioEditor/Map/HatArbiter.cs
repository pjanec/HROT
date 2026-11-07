using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Editing;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ Seats and hats H3 (📄 docs/UX/DESIGN_Seats_And_Hats.md §3.3) — the ONE hat of a station's map: DERIVED from facts that already exist (the run mode, the
    /// RTS tool, possession, the director) by one precedence, so there is no second state to keep in step. On a change it disarms every armed tool but RTS,
    /// and stands RTS down while a unit is driven (R-4163: release returns to it). Scheduled with the map's interaction systems ⇒ every map host, by construction.
    /// ⭐ Hot path: reads fields and a closure, allocates nothing.
    /// </summary>
    public sealed class HatArbiter : IEcsModuleSystem
    {
        private readonly EntityRepository _world;
        private readonly Hrot.ScenarioEditor.Tools.IToolController _tools;
        private Hat? _last;   // null until the first frame: that frame records the hat, it is not a change

        public HatArbiter(EntityRepository world, Hrot.ScenarioEditor.Tools.IToolController tools)
        {
            _world = world;
            _tools = tools;
        }

        /// <summary>This station drives a unit (set by <c>PossessionStation</c> when it takes / gives back the camera).</summary>
        public bool Embodied { get; set; }

        /// <summary>The director owns the camera (H4 sets it).</summary>
        public bool Directing { get; set; }

        /// <summary>The hat now: Embodied &gt; Direct &gt; Command (RTS armed, running) &gt; Edit (Edit mode) &gt; Observe.</summary>
        public Hat Active => Derive(Embodied, Directing, MapUiStates.RtsOf(_world)?.Armed == true, StationMode.Of(_world));

        /// <summary>The ONE precedence (§3.3).</summary>
        public static Hat Derive(bool embodied, bool directing, bool rtsArmed, EditingMode mode)
            => embodied ? Hat.Embodied
             : directing ? Hat.Direct
             : rtsArmed && mode is EditingMode.Preview or EditingMode.Live ? Hat.Command
             : mode == EditingMode.Edit ? Hat.Edit
             : Hat.Observe;

        /// <summary>The hat as the indicator words it.</summary>
        public static string WordOf(Hat hat) => hat switch
        {
            Hat.Edit => "Edit",
            Hat.Command => "Command",
            Hat.Embodied => "Embodied",
            Hat.Direct => "Direct",
            _ => "Observe",
        };

        /// <summary>The arbiter of <paramref name="world"/>'s map, or null (no map).</summary>
        public static HatArbiter? Of(EntityRepository? world) => MapUiStates.HatsOf(world);

        public void Execute(ISimulationView view, float deltaTime)
        {
            var now = Active;
            if (MapUiStates.RtsOf(_world) is { } rts) rts.StoodDown = now == Hat.Embodied;   // ⭐ RTS stays checked under the FPS view, its gizmo ignores input
            if (now == _last) return;
            bool first = _last is null;
            _last = now;
            if (first) return;   // ⛔ the first frame is not a hat change — a tool armed before it stays
            // ⭐ disarm: no placement / paste / pick tool survives a hat change; RTS is the Command hat itself (sticky, R-4163)
            for (int guard = 0; guard < 8 && _tools.ActiveModal is { } top && top.Id != Hrot.ScenarioEditor.Tools.ScenarioToolIds.RtsCommand; guard++)
                _tools.Cancel();
        }
    }
}
