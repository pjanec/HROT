using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-1257</c> (R-1029) — the editor's debugger hooks for the Brain tab: open a behaviour's graph (pinned or following the selection)
    /// and arm a "stop when it changes its mind" breakpoint. ⛔ <c>CE-1329</c> (Brain View §12r BV-72) — the "Debug this unit" switch it held
    /// (one debugged unit: its trace armed, the overlay on it) is GONE: a unit's trace is a switch in the one per-unit store
    /// (<c>UnitDebugSettings</c>), and the overlay follows the graph's pin (CE-1318). 📄 docs/DESIGN_Entity_Brain_View.md §10, §12r.
    /// <para>⭐ Composed by a host that has the AI debuggers (the editor) — <see cref="Current"/> is null elsewhere, and the buttons hide.</para>
    /// </summary>
    public sealed class UnitDebugController
    {
        /// <summary>The host's controller, or null where nothing composes one.</summary>
        public static UnitDebugController? Current { get; set; }

        /// <summary>⭐ <c>CE-1258</c> — arms a "stop when it changes its mind" breakpoint on a unit (the host wires its breakpoint manager);
        /// returns the breakpoint id, or null where there is no manager.</summary>
        public Func<EntityRepository, Entity, string?>? ArmJournalBreak { get; set; }

        /// <summary>⭐ <c>CE-1254</c> (BV-21) — opens a behaviour's graph by name in its editor (the host wires its catalog and documents);
        /// false when this host has no graph of that name. A C#-built one opens read-only (CE-1252).</summary>
        public Func<string, bool>? OpenGraph { get; set; }

        /// <summary>⭐ <c>CE-1318</c> (BV-55) — opens a behaviour's graph PINNED to a unit (name, unit, the tab's label for it): its own
        /// document whose live overlay stays on that unit. The Brain tab uses it when the tab itself is pinned.</summary>
        public Func<string, Entity, string, bool>? OpenGraphPinned { get; set; }
    }
}
